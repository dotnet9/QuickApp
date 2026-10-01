using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using QuickApp.Core.Json;

namespace QuickApp.Core.Services;

/// <summary>更新检查缓存：ETag 配上次响应的 release 内容。304 只代表「最新发布没变」，
/// 不代表「当前应用已是最新」，所以必须留着 release 才能做版本比较。</summary>
public sealed record UpdateCheckState(string? Etag, GitHubRelease? Release);

/// <summary>
/// 通过 GitHub Releases API 检查更新：够用、无额外依赖、AOT 友好（源生成 JSON）。
/// 任何网络/解析异常都吞掉返回 null，不打扰用户。
/// 未认证 API 配额只有 60 次/小时/IP（走代理时被同出口用户共享、极易耗尽），所以：
/// - 带上 ETag 条件请求，304 Not Modified 不计入配额，日常检查基本免费；
/// - 收到 403/429 时解析限流恢复时间，期间直接本地返回，不再发请求放大流量。
/// </summary>
public sealed class UpdateChecker : IUpdateChecker
{
    private const string DefaultApiBase = "https://api.github.com";

    private readonly HttpClient _http;
    private readonly string _owner;
    private readonly string _repo;
    private readonly string _apiBase;
    private readonly Action<string>? _log;
    private readonly bool _preferInstaller;
    private readonly string? _runtimeIdentifier;
    private readonly string? _stateFile;
    private DateTime _blockedUntilUtc = DateTime.MinValue;

    public UpdateChecker(
        HttpClient http,
        string owner,
        string repo,
        string apiBase = DefaultApiBase,
        Action<string>? log = null,
        bool preferInstaller = true,
        string? runtimeIdentifier = null,
        string? stateFile = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _owner = owner;
        _repo = repo;
        _apiBase = apiBase.TrimEnd('/');
        _log = log;
        _preferInstaller = preferInstaller;
        _runtimeIdentifier = runtimeIdentifier;
        _stateFile = stateFile;
    }

    public async Task<UpdateCheckResult> CheckAsync(Version current, CancellationToken cancellationToken = default)
    {
        try
        {
            if (DateTime.UtcNow < _blockedUntilUtc)
            {
                return UpdateCheckResult.Failed(RateLimitMessage);
            }

            string url = $"{_apiBase}/repos/{_owner}/{_repo}/releases/latest";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
            request.Headers.TryAddWithoutValidation("User-Agent", "QuickApp-UpdateChecker");
            UpdateCheckState? state = TryReadState();
            if (!string.IsNullOrEmpty(state?.Etag))
            {
                request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(state.Etag));
            }

            using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                // 最新发布没变：条件请求不计配额，用缓存的 release 内容做版本比较
                return state?.Release is { } cached
                    ? EvaluateRelease(cached, current)
                    : UpdateCheckResult.Latest();
            }

            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                _blockedUntilUtc = ParseBlockedUntil(response);
                _log?.Invoke($"检查更新被限流（HTTP {(int)response.StatusCode}），恢复前不再请求");
                return UpdateCheckResult.Failed(RateLimitMessage);
            }

            if (!response.IsSuccessStatusCode)
            {
                _log?.Invoke($"检查更新失败：HTTP {(int)response.StatusCode}");
                return UpdateCheckResult.Failed($"HTTP {(int)response.StatusCode}");
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            GitHubRelease? release = JsonSerializer.Deserialize(json, AppJsonContext.Default.GitHubRelease);
            if (release is null || release.Draft || release.Prerelease)
            {
                return UpdateCheckResult.Latest();
            }

            TrySaveState(new UpdateCheckState(response.Headers.ETag?.Tag, release));
            return EvaluateRelease(release, current);
        }
        catch (OperationCanceledException)
        {
            return UpdateCheckResult.Failed("操作已取消");
        }
        catch (Exception ex)
        {
            _log?.Invoke($"检查更新异常：{ex.Message}");
            return UpdateCheckResult.Failed(ex.Message);
        }
    }

    private UpdateCheckResult EvaluateRelease(GitHubRelease release, Version current)
    {
        Version? candidate = VersionUtil.Parse(release.TagName);
        if (!VersionUtil.IsNewer(candidate, current))
        {
            return UpdateCheckResult.Latest();
        }

        (string? assetUrl, string? assetName, string? checksumUrl) = PickAsset(
            release,
            _runtimeIdentifier ?? CurrentRuntimeIdentifier(),
            _preferInstaller);
        string title = string.IsNullOrWhiteSpace(release.Name) ? release.TagName ?? string.Empty : release.Name!;

        return new UpdateCheckResult(new UpdateInfo(
            candidate!,
            release.TagName ?? string.Empty,
            title,
            release.Body,
            release.HtmlUrl ?? $"https://github.com/{_owner}/{_repo}/releases",
            assetUrl,
            assetName,
            checksumUrl), true, null);
    }

    private string RateLimitMessage
        => "GitHub API 限流中，约 " + _blockedUntilUtc.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture) + " 后恢复";

    /// <summary>从 Retry-After 或 X-RateLimit-Reset 推算限流恢复时间，兜底 10 分钟，最长封顶 1 小时。</summary>
    private static DateTime ParseBlockedUntil(HttpResponseMessage response)
    {
        DateTime now = DateTime.UtcNow;
        TimeSpan? retryAfter = response.Headers.RetryAfter?.Delta;
        if (retryAfter is null && response.Headers.RetryAfter?.Date is { } retryDate)
        {
            retryAfter = retryDate - DateTimeOffset.UtcNow;
        }

        DateTime blocked = retryAfter is { } delay && delay > TimeSpan.Zero
            ? now + delay
            : response.Headers.TryGetValues("X-RateLimit-Reset", out var values)
              && long.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long resetSeconds)
                ? DateTimeOffset.FromUnixTimeSeconds(resetSeconds).UtcDateTime
                : now + TimeSpan.FromMinutes(10);

        blocked += TimeSpan.FromSeconds(30);
        return blocked > now + TimeSpan.FromHours(1) ? now + TimeSpan.FromHours(1) : blocked;
    }

    private UpdateCheckState? TryReadState()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_stateFile) || !File.Exists(_stateFile))
            {
                return null;
            }

            string json = File.ReadAllText(_stateFile);
            return JsonSerializer.Deserialize(json, AppJsonContext.Default.UpdateCheckState);
        }
        catch
        {
            // 缓存读不出来就当作没有，走普通请求
            return null;
        }
    }

    private void TrySaveState(UpdateCheckState state)
    {
        if (string.IsNullOrWhiteSpace(_stateFile))
        {
            return;
        }

        try
        {
            string? dir = Path.GetDirectoryName(_stateFile);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(_stateFile, JsonSerializer.Serialize(state, AppJsonContext.Default.UpdateCheckState));
        }
        catch
        {
            // 缓存写失败不影响本次检查结果
        }
    }

    /// <summary>
    /// 选择当前系统/架构的可下载资产。Windows 使用安装器，Linux 使用 deb，macOS 使用 pkg/dmg；
    /// 对旧版本保留 zip 回退。没有匹配包时交给用户打开 Release 页面。
    /// </summary>
    private static (string? Url, string? Name, string? ChecksumUrl) PickAsset(
        GitHubRelease release,
        string? runtimeIdentifier,
        bool preferInstaller)
    {
        if (release.Assets is null || release.Assets.Length == 0)
        {
            return (null, null, null);
        }

        if (string.IsNullOrWhiteSpace(runtimeIdentifier))
        {
            return (null, null, null);
        }

        string marker = "-" + runtimeIdentifier;
        GitHubAsset? preferred = null;

        // The installer is only published for Windows x64. Keep the zip as a
        // fallback so older releases remain usable during the transition.
        if (preferInstaller && string.Equals(runtimeIdentifier, "win-x64", StringComparison.OrdinalIgnoreCase))
        {
            preferred = release.Assets.FirstOrDefault(a =>
                IsAssetForRuntime(a, marker) &&
                a.Name!.EndsWith("-setup.exe", StringComparison.OrdinalIgnoreCase));
        }

        string nativeExtension = runtimeIdentifier.StartsWith("linux-", StringComparison.OrdinalIgnoreCase)
            ? ".deb"
            : runtimeIdentifier.StartsWith("osx-", StringComparison.OrdinalIgnoreCase)
                ? ".pkg"
                : ".zip";

        preferred ??= release.Assets.FirstOrDefault(a =>
            IsAssetForRuntime(a, marker) &&
            a.Name!.EndsWith(nativeExtension, StringComparison.OrdinalIgnoreCase));

        if (runtimeIdentifier.StartsWith("osx-", StringComparison.OrdinalIgnoreCase))
        {
            preferred ??= release.Assets.FirstOrDefault(a =>
                IsAssetForRuntime(a, marker) &&
                a.Name!.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase));
        }

        // Releases before native installers were introduced only contain ZIP files.
        preferred ??= release.Assets.FirstOrDefault(a =>
            IsAssetForRuntime(a, marker) &&
            a.Name!.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

        if (preferred is null)
        {
            return (null, null, null);
        }

        GitHubAsset? checksum = release.Assets.FirstOrDefault(asset =>
            string.Equals(asset.Name, preferred.Name + ".sha256", StringComparison.OrdinalIgnoreCase));
        return (preferred.BrowserDownloadUrl, preferred.Name, checksum?.BrowserDownloadUrl);
    }

    private static bool IsAssetForRuntime(GitHubAsset asset, string marker)
    {
        return !string.IsNullOrWhiteSpace(asset.Name) &&
            !string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl) &&
            (asset.Name!.Contains(marker + ".", StringComparison.OrdinalIgnoreCase) ||
             asset.Name.Contains(marker + "-", StringComparison.OrdinalIgnoreCase));
    }

    private static string? CurrentRuntimeIdentifier()
    {
        string os = OperatingSystem.IsWindows()
            ? "win"
            : OperatingSystem.IsLinux()
                ? "linux"
                : OperatingSystem.IsMacOS()
                    ? "osx"
                    : string.Empty;

        string architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X86 => "x86",
            Architecture.X64 => "x64",
            Architecture.Arm => "arm",
            Architecture.Arm64 => "arm64",
            _ => string.Empty
        };

        return os.Length == 0 || architecture.Length == 0 ? null : os + "-" + architecture;
    }
}
