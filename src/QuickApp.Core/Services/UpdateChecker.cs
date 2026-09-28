using System;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using QuickApp.Core.Json;

namespace QuickApp.Core.Services;

/// <summary>
/// 通过 GitHub Releases API 检查更新：够用、无额外依赖、AOT 友好（源生成 JSON）。
/// 任何网络/解析异常都吞掉返回 null，不打扰用户。
/// </summary>
public sealed class UpdateChecker : IUpdateChecker
{
    private const string DefaultApiBase = "https://api.github.com";

    private readonly HttpClient _http;
    private readonly string _owner;
    private readonly string _repo;
    private readonly string _apiBase;
    private readonly Action<string>? _log;

    public UpdateChecker(
        HttpClient http,
        string owner,
        string repo,
        string apiBase = DefaultApiBase,
        Action<string>? log = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _owner = owner;
        _repo = repo;
        _apiBase = apiBase.TrimEnd('/');
        _log = log;
    }

    public async Task<UpdateInfo?> CheckAsync(Version current, CancellationToken cancellationToken = default)
    {
        try
        {
            string url = $"{_apiBase}/repos/{_owner}/{_repo}/releases/latest";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
            request.Headers.TryAddWithoutValidation("User-Agent", "QuickApp-UpdateChecker");

            using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _log?.Invoke($"检查更新失败：HTTP {(int)response.StatusCode}");
                return null;
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            GitHubRelease? release = JsonSerializer.Deserialize(json, AppJsonContext.Default.GitHubRelease);
            if (release is null || release.Draft || release.Prerelease)
            {
                return null;
            }

            Version? candidate = VersionUtil.Parse(release.TagName);
            if (!VersionUtil.IsNewer(candidate, current))
            {
                return null;
            }

            (string? assetUrl, string? assetName) = PickAsset(release, CurrentRuntimeIdentifier());
            string title = string.IsNullOrWhiteSpace(release.Name) ? release.TagName ?? string.Empty : release.Name!;

            return new UpdateInfo(
                candidate!,
                release.TagName ?? string.Empty,
                title,
                release.Body,
                release.HtmlUrl ?? $"https://github.com/{_owner}/{_repo}/releases",
                assetUrl,
                assetName);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            _log?.Invoke($"检查更新异常：{ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 选择当前系统/架构的可下载资产。Windows 优先安装器，方便普通用户直接升级；
    /// 其它平台使用对应的 zip。没有匹配包时交给用户打开 Release 页面。
    /// </summary>
    private static (string? Url, string? Name) PickAsset(GitHubRelease release, string? runtimeIdentifier)
    {
        if (release.Assets is null || release.Assets.Length == 0)
        {
            return (null, null);
        }

        if (string.IsNullOrWhiteSpace(runtimeIdentifier))
        {
            return (null, null);
        }

        string marker = "-" + runtimeIdentifier + "-";
        GitHubAsset? preferred = null;

        // The installer is only published for Windows x64. Keep the zip as a
        // fallback so older releases remain usable during the transition.
        if (string.Equals(runtimeIdentifier, "win-x64", StringComparison.OrdinalIgnoreCase))
        {
            preferred = release.Assets.FirstOrDefault(a =>
                IsAssetForRuntime(a, marker) &&
                a.Name!.EndsWith("-setup.exe", StringComparison.OrdinalIgnoreCase));
        }

        preferred ??= release.Assets.FirstOrDefault(a =>
            IsAssetForRuntime(a, marker) &&
            a.Name!.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

        return preferred is null ? (null, null) : (preferred.BrowserDownloadUrl, preferred.Name);
    }

    private static bool IsAssetForRuntime(GitHubAsset asset, string marker)
    {
        return !string.IsNullOrWhiteSpace(asset.Name) &&
            !string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl) &&
            asset.Name!.Contains(marker, StringComparison.OrdinalIgnoreCase);
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
