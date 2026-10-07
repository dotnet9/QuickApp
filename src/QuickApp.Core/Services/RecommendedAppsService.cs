using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
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
/// 推荐应用服务：目录内嵌在程序集（随应用版本更新），发布资产走 <see cref="UpdateChecker"/>
/// 的网页端点实时解析——releases/latest 的 302 拿最新 tag，expanded_assets 拿按平台选好的安装包，
/// 因此被推荐软件发了新版本后，这里拿到的永远是最新安装包地址，无需发版 QuickApp。
/// </summary>
public sealed class RecommendedAppsService : IRecommendedAppsService
{
    private const string CatalogResourceName = "QuickApp.Core.RecommendedApps.json";

    /// <summary>解析结果缓存时长。设置窗口每次打开都会走这条路径，过期才真正出网。</summary>
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(1);

    private readonly HttpClient _http;
    private readonly Action<string>? _log;
    private readonly Lazy<IReadOnlyList<RecommendedApp>> _catalog;
    private readonly Dictionary<string, (RecommendedRelease Release, DateTimeOffset FetchedAt)> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    public RecommendedAppsService(HttpClient http, Action<string>? log = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _log = log;
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
    /// 复用自更新检查器按平台选包的逻辑（win 用 setup.exe、linux 用 deb、osx 用 dmg/pkg），
    /// current 传 0.0.0 表示「永远要最新发布」；stateFile 为 null，不与自更新的检查缓存互相覆盖。
    /// </summary>
    private async Task<RecommendedRelease?> FetchLatestAsync(RecommendedApp app, CancellationToken cancellationToken)
    {
        try
        {
            var checker = new UpdateChecker(
                _http,
                owner: app.Owner,
                repo: app.Repo,
                log: _log,
                preferInstaller: true,
                stateFile: null);

            UpdateCheckResult result = await checker
                .CheckAsync(new Version(0, 0, 0), cancellationToken)
                .ConfigureAwait(false);

            // Update 非空但资产/校验为空 = 没有匹配当前系统的安装包，同样视为解析失败
            if (!result.Succeeded || result.Update is null ||
                result.Update.AssetUrl is null || result.Update.ChecksumUrl is null)
            {
                _log?.Invoke($"解析 {app.Repo} 最新发布失败：{result.Error ?? "没有匹配当前系统的安装包"}");
                return null;
            }

            UpdateInfo info = result.Update;
            return new RecommendedRelease(
                app,
                info.Tag,
                info.AssetUrl,
                info.AssetName,
                info.ChecksumUrl,
                info.AssetSize,
                info.PageUrl);
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
