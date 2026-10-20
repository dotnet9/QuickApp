using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using QuickApp.Core.Json;
using QuickApp.Core.Models;

namespace QuickApp.Core.Services;

/// <summary>某个推荐应用按当前系统实时解析出的最新发布信息。</summary>
public sealed record RecommendedRelease(
    RecommendedApp App,
    string Tag,
    string? AssetUrl,
    string? AssetName,
    string? ChecksumUrl,
    long? AssetSize,
    string PageUrl);

/// <summary>推荐应用目录与最新发布解析。实现必须保证安装包地址不落盘、不写死。</summary>
public interface IRecommendedAppsService
{
    /// <summary>内置推荐目录（随应用版本发布）。</summary>
    IReadOnlyList<RecommendedApp> GetCatalog();

    /// <summary>
    /// 解析推荐应用在当前系统的最新发布资产。带进程内缓存（TTL 见实现），
    /// 缓存过期或 <paramref name="force"/> 时重新访问 GitHub（优先网页端点，不占 API 配额）。
    /// 解析失败返回 null，由调用方提示稍后重试。
    /// </summary>
    Task<RecommendedRelease?> ResolveAsync(RecommendedApp app, bool force = false, CancellationToken cancellationToken = default);
}

/// <summary>
/// 推荐应用服务：目录内嵌在程序集（随应用版本更新），发布资产走 GitHub 网页端点实时解析——
/// releases/latest 的 302 拿最新 tag，expanded_assets 按目录中的平台后缀选安装包，
/// 因此被推荐软件发了新版本后，这里拿到的永远是最新安装包地址，无需发版 QuickApp。
/// </summary>
public sealed class RecommendedAppsService : IRecommendedAppsService
{
    private const string CatalogResourceName = "QuickApp.Core.RecommendedApps.json";

    /// <summary>解析结果缓存时长。设置窗口每次打开都会走这条路径，过期才真正出网。</summary>
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(1);

    private readonly HttpClient _http;
    private readonly Action<string>? _log;
    private readonly string? _runtimeIdentifier;
    private readonly Lazy<IReadOnlyList<RecommendedApp>> _catalog;
    private readonly Dictionary<string, (RecommendedRelease Release, DateTimeOffset FetchedAt)> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    public RecommendedAppsService(HttpClient http, Action<string>? log = null, string? runtimeIdentifier = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _log = log;
        _runtimeIdentifier = runtimeIdentifier ?? UpdateServiceOptions.CurrentRuntimeIdentifier();
        _catalog = new Lazy<IReadOnlyList<RecommendedApp>>(LoadCatalog);
    }

    public IReadOnlyList<RecommendedApp> GetCatalog() => _catalog.Value;

    public async Task<RecommendedRelease?> ResolveAsync(RecommendedApp app, bool force = false, CancellationToken cancellationToken = default)
    {
        if (app is null)
        {
            throw new ArgumentNullException(nameof(app));
        }

        if (!force && TryReadCache(app.Id, out RecommendedRelease? cached))
        {
            return cached;
        }

        await _cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // 双重检查：等锁期间可能已被并行请求填上
            if (!force && TryReadCache(app.Id, out cached))
            {
                return cached;
            }

            RecommendedRelease? release = await FetchLatestAsync(app, cancellationToken).ConfigureAwait(false);
            if (release is not null)
            {
                _cache[app.Id] = (release, DateTimeOffset.UtcNow);
            }

            return release;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private bool TryReadCache(string appId, out RecommendedRelease? release)
    {
        release = null;
        if (_cache.TryGetValue(appId, out var entry) &&
            DateTimeOffset.UtcNow - entry.FetchedAt < CacheLifetime)
        {
            release = entry.Release;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 只从最新发布的资产列表选择目录指定的安装包与同名校验文件；不同仓库可采用不同平台命名。
    /// </summary>
    private async Task<RecommendedRelease?> FetchLatestAsync(RecommendedApp app, CancellationToken cancellationToken)
    {
        try
        {
            if (_runtimeIdentifier is null ||
                !app.InstallerSuffixes.TryGetValue(_runtimeIdentifier, out string? suffix) ||
                string.IsNullOrWhiteSpace(suffix))
            {
                return null;
            }

            var repository = new Uri($"https://github.com/{app.Owner}/{app.Repo}/");
            var latestUrl = new Uri(repository, "releases/latest");
            using HttpResponseMessage latest = await GetAsync(latestUrl, app.Repo, cancellationToken).ConfigureAwait(false);
            bool redirected = (int)latest.StatusCode is >= 300 and < 400;
            if (!redirected)
            {
                latest.EnsureSuccessStatusCode();
            }

            // 同时支持已跟随重定向的 HttpClient 与只返回 Location 的客户端。
            Uri? page = redirected && latest.Headers.Location is { } location
                ? new Uri(latestUrl, location)
                : latest.RequestMessage?.RequestUri;
            string tagPrefix = repository.AbsolutePath + "releases/tag/";
            if (page is null || page.Host != repository.Host ||
                !page.AbsolutePath.StartsWith(tagPrefix, StringComparison.Ordinal))
            {
                return null;
            }

            string escapedTag = page.AbsolutePath[tagPrefix.Length..].TrimEnd('/');
            string tag = Uri.UnescapeDataString(escapedTag);
            if (string.IsNullOrWhiteSpace(tag))
            {
                return null;
            }

            using HttpResponseMessage response = await GetAsync(
                new Uri(repository, "releases/expanded_assets/" + escapedTag), app.Repo, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            string html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            string downloadPrefix = repository.AbsolutePath + "releases/download/" + escapedTag + "/";
            var assets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in Regex.Matches(html, "href\\s*=\\s*[\"'](?<url>[^\"']+)[\"']",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            {
                if (Uri.TryCreate(repository, WebUtility.HtmlDecode(match.Groups["url"].Value), out Uri? url) &&
                    url.Scheme == Uri.UriSchemeHttps && url.Host == repository.Host &&
                    url.AbsolutePath.StartsWith(downloadPrefix, StringComparison.Ordinal))
                {
                    string name = Uri.UnescapeDataString(url.AbsolutePath[downloadPrefix.Length..]);
                    if (!name.Contains('/'))
                    {
                        assets[name] = url.AbsoluteUri;
                    }
                }
            }

            string? assetName = assets.Keys.FirstOrDefault(name => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
            if (assetName is null || !assets.TryGetValue(assetName + ".sha256", out string? checksumUrl))
            {
                _log?.Invoke($"解析 {app.Repo} 最新发布失败：没有匹配当前系统的安装包或校验文件");
                return null;
            }

            return new RecommendedRelease(
                app, tag, assets[assetName], assetName, checksumUrl, null, page.AbsoluteUri);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            _log?.Invoke($"解析 {app.Repo} 最新发布异常：{ex.Message}");
            return null;
        }
    }

    private async Task<HttpResponseMessage> GetAsync(Uri url, string userAgent, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", userAgent + "-QuickApp");
        return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
    }

    private IReadOnlyList<RecommendedApp> LoadCatalog()
    {
        try
        {
            Assembly assembly = typeof(RecommendedAppsService).Assembly;
            using Stream stream = assembly.GetManifestResourceStream(CatalogResourceName)
                ?? throw new InvalidOperationException("内嵌推荐目录缺失：" + CatalogResourceName);
            using var reader = new StreamReader(stream);
            string json = reader.ReadToEnd();
            List<RecommendedApp>? apps = JsonSerializer.Deserialize(json, AppJsonContext.Default.ListRecommendedApp);
            return apps ?? [];
        }
        catch (Exception ex)
        {
            _log?.Invoke("读取推荐目录失败：" + ex.Message);
            return [];
        }
    }
}
