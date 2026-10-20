using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using QuickApp.Core.Models;
using QuickApp.Core.Json;
using QuickApp.Core.Services;
using Xunit;

namespace QuickApp.Core.Tests;

public sealed class RecommendedAppsServiceTests
{
    [Fact]
    public void Loads_embedded_catalog_with_valid_unique_entries()
    {
        var service = CreateService();

        var catalog = service.GetCatalog();

        Assert.NotNull(catalog);
        Assert.Equal(catalog.Count, catalog.Select(app => app.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(catalog, app =>
        {
            Assert.False(string.IsNullOrWhiteSpace(app.Id));
            Assert.False(string.IsNullOrWhiteSpace(app.Name));
            Assert.False(string.IsNullOrWhiteSpace(app.Owner));
            Assert.False(string.IsNullOrWhiteSpace(app.Repo));
            Assert.NotEmpty(app.InstallerSuffixes);
            Assert.All(app.InstallerSuffixes, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Value)));
        });
    }

    [Theory]
    [InlineData("win-x64", "Vex-v0.5.2-windows-x64-setup.exe")]
    [InlineData("linux-x64", "Vex-v0.5.2-linux-x64.deb")]
    [InlineData("linux-arm64", "Vex-v0.5.2-linux-arm64.deb")]
    [InlineData("osx-x64", "Vex-v0.5.2-macos-universal.dmg")]
    [InlineData("osx-arm64", "Vex-v0.5.2-macos-universal.dmg")]
    public async Task Resolves_latest_flutter_release_asset_for_platform(string rid, string expectedAsset)
    {
        var service = CreateService(runtimeIdentifier: rid);
        RecommendedApp app = CreateApp();

        RecommendedRelease? release = await service.ResolveAsync(app, force: true);

        // 网页端点把根相对链接补全为 github.com 前缀
        string downloadBase = "https://github.com/dotnet9/Vex-Flutter/releases/download/v0.5.2/";
        Assert.NotNull(release);
        Assert.Equal("v0.5.2", release!.Tag);
        Assert.Equal(downloadBase + expectedAsset, release.AssetUrl);
        Assert.Equal(expectedAsset, release.AssetName);
        Assert.Equal(downloadBase + expectedAsset + ".sha256", release.ChecksumUrl);
        // 网页端点（默认路径，不占 API 配额）不携带资产大小，允许为 null；下载时由 Content-Length 补足
        Assert.Null(release.AssetSize);
        Assert.Equal("https://github.com/dotnet9/Vex-Flutter/releases/tag/v0.5.2", release.PageUrl);
    }

    [Fact]
    public async Task Returns_null_when_release_has_no_matching_platform_asset()
    {
        RecommendedApp app = CreateApp();
        // expanded_assets 只有别的平台的资产
        string html = "<a href=\"/dotnet9/Vex-Flutter/releases/download/v0.5.2/Vex-v0.5.2-android-arm64.apk\">x</a>";
        var service = CreateService(new StubHandler(expandedHtml: html));

        RecommendedRelease? release = await service.ResolveAsync(app, force: true);

        Assert.Null(release);
    }

    [Theory]
    [InlineData("/dotnet9/Vex-Flutter/releases/download/v0.5.2/", false)]
    [InlineData("/dotnet9/Vex/releases/download/v0.5.2/", true)]
    [InlineData("/dotnet9/Vex-Flutter/releases/download/v0.5.1/", true)]
    [InlineData("https://example.test/dotnet9/Vex-Flutter/releases/download/v0.5.2/", true)]
    public async Task Rejects_missing_checksum_or_assets_outside_latest_repository(string prefix, bool includeChecksum)
    {
        const string asset = "Vex-v0.5.2-windows-x64-setup.exe";
        string html = Link(prefix + asset) + (includeChecksum ? Link(prefix + asset + ".sha256") : string.Empty);
        var service = CreateService(new StubHandler(expandedHtml: html), "win-x64");

        Assert.Null(await service.ResolveAsync(CreateApp()));
    }

    [Fact]
    public async Task Resolves_tag_after_client_follows_redirect()
    {
        var service = CreateService(new StubHandler(followRedirect: true));

        Assert.NotNull(await service.ResolveAsync(CreateApp()));
    }

    [Fact]
    public async Task Failed_asset_request_is_not_cached()
    {
        var handler = new StubHandler { AssetsStatusCode = HttpStatusCode.ServiceUnavailable };
        var service = CreateService(handler);
        RecommendedApp app = CreateApp();

        Assert.Null(await service.ResolveAsync(app));
        handler.AssetsStatusCode = HttpStatusCode.OK;
        Assert.NotNull(await service.ResolveAsync(app));
        Assert.Equal(2, handler.ExpandedRequests);
    }

    [Fact]
    public async Task Unsupported_platform_has_no_package()
    {
        var service = CreateService(runtimeIdentifier: "win-arm64");

        Assert.Null(await service.ResolveAsync(CreateApp()));
    }

    [Fact]
    public async Task Caches_release_within_ttl_and_skips_second_request()
    {
        var handler = new StubHandler();
        var service = CreateService(handler);
        RecommendedApp app = CreateApp();

        Assert.NotNull(await service.ResolveAsync(app, force: true));
        Assert.NotNull(await service.ResolveAsync(app)); // TTL 内：不应再请求资产列表

        Assert.Equal(1, handler.ExpandedRequests);
    }

    [Fact]
    public async Task Force_bypasses_cache()
    {
        var handler = new StubHandler();
        var service = CreateService(handler);
        RecommendedApp app = CreateApp();

        Assert.NotNull(await service.ResolveAsync(app, force: true));
        Assert.NotNull(await service.ResolveAsync(app, force: true));

        Assert.Equal(2, handler.ExpandedRequests);
    }

    [Fact]
    public void Platform_launcher_paths_match_current_os()
    {
        var app = new RecommendedApp
        {
            Id = "t",
            Name = "T",
            Owner = "o",
            Repo = "r",
            LauncherPathsWindows = ["C:\\Program Files\\T\\T.exe"],
            LauncherPathsLinux = ["/usr/bin/t"],
            LauncherPathsMacOS = ["/Applications/T.app"]
        };

        string[] paths = app.LauncherPathsForCurrentPlatform();
        Assert.Single(paths);
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal("C:\\Program Files\\T\\T.exe", paths[0]);
        }
        else if (OperatingSystem.IsMacOS())
        {
            Assert.Equal("/Applications/T.app", paths[0]);
        }
        else if (OperatingSystem.IsLinux())
        {
            Assert.Equal("/usr/bin/t", paths[0]);
        }
    }

    [Fact]
    public void Reads_application_text_from_configuration_with_language_fallbacks()
    {
        const string json = """
            {
              "id": "another-app",
              "name": "Notes",
              "displayName": "笔记工具",
              "description": "默认介绍",
              "localizations": {
                "en-US": { "displayName": "My Notes", "description": "Configured introduction" },
                "ja-JP": { "displayName": "ノート" }
              }
            }
            """;
        RecommendedApp app = JsonSerializer.Deserialize(json, AppJsonContext.Default.RecommendedApp)!;

        Assert.Equal("My Notes", app.DisplayNameForCulture("en-US"));
        Assert.Equal("Configured introduction", app.DescriptionForCulture("en-US"));
        Assert.Equal("ノート", app.DisplayNameForCulture("ja-JP"));
        Assert.Equal("默认介绍", app.DescriptionForCulture("ja-JP"));
        Assert.Equal("笔记工具", app.DisplayNameForCulture("fr-FR"));
        Assert.Equal("默认介绍", app.DescriptionForCulture(null));
        Assert.Equal("Notes", new RecommendedApp { Name = "Notes" }.DisplayNameForCulture("en-US"));
    }

    // 解析测试使用固定样例，与内置推荐列表的增删无关。
    private static RecommendedApp CreateApp() => new()
    {
        Id = "release-fixture",
        Name = "Vex",
        Owner = "dotnet9",
        Repo = "Vex-Flutter",
        InstallerSuffixes = new()
        {
            ["win-x64"] = "-windows-x64-setup.exe",
            ["linux-x64"] = "-linux-x64.deb",
            ["linux-arm64"] = "-linux-arm64.deb",
            ["osx-x64"] = "-macos-universal.dmg",
            ["osx-arm64"] = "-macos-universal.dmg"
        }
    };

    private static RecommendedAppsService CreateService(StubHandler? handler = null, string runtimeIdentifier = "win-x64")
    {
        return new RecommendedAppsService(new HttpClient(handler ?? new StubHandler()), runtimeIdentifier: runtimeIdentifier);
    }

    private static string Link(string url) => "<a href=\"" + url + "\">download</a>";

    /// <summary>
    /// 最新发布的真实命名覆盖全部桌面平台，并混入 Android 安装包与旧仓库链接。
    /// </summary>
    private sealed class StubHandler(string? expandedHtml = null, bool followRedirect = false) : HttpMessageHandler
    {
        public int ExpandedRequests;
        public HttpStatusCode AssetsStatusCode = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri?.ToString() ?? string.Empty;
            Assert.StartsWith("https://github.com/dotnet9/Vex-Flutter/", url);
            if (url.Contains("/releases/latest", StringComparison.Ordinal))
            {
                var page = new Uri("https://github.com/dotnet9/Vex-Flutter/releases/tag/v0.5.2");
                var redirect = new HttpResponseMessage(followRedirect ? HttpStatusCode.OK : HttpStatusCode.Found);
                if (followRedirect)
                {
                    redirect.RequestMessage = new HttpRequestMessage(HttpMethod.Get, page);
                }
                else
                {
                    redirect.Headers.Location = new Uri(page.PathAndQuery, UriKind.Relative);
                }
                return Task.FromResult(redirect);
            }

            if (url.Contains("expanded_assets", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref ExpandedRequests);
                if (expandedHtml is not null)
                {
                    return Task.FromResult(HtmlResponse(expandedHtml));
                }

                string[] suffixes = ["android-arm64.apk", "windows-x64-setup.exe", "macos-universal.dmg", "linux-x64.deb", "linux-arm64.deb"];
                string html = Link("/dotnet9/Vex/releases/download/v0.5.2/Vex-v0.5.2-windows-x64-setup.exe");
                foreach (string suffix in suffixes)
                {
                    string asset = "Vex-v0.5.2-" + suffix;
                    html += Link("/dotnet9/Vex-Flutter/releases/download/v0.5.2/" + asset) +
                        Link("https://github.com/dotnet9/Vex-Flutter/releases/download/v0.5.2/" + asset + ".sha256");
                }
                return Task.FromResult(HtmlResponse(html));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private HttpResponseMessage HtmlResponse(string content)
        {
            var response = new HttpResponseMessage(AssetsStatusCode) { Content = new StringContent(content) };
            return response;
        }
    }
}
