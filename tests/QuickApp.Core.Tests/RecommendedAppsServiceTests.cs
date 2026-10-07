using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using QuickApp.Core.Models;
using QuickApp.Core.Services;
using Xunit;

namespace QuickApp.Core.Tests;

public sealed class RecommendedAppsServiceTests
{
    [Fact]
    public void Loads_embedded_catalog_with_at_least_one_app()
    {
        var service = CreateService("{}");

        var catalog = service.GetCatalog();

        Assert.NotEmpty(catalog);
        Assert.All(catalog, app =>
        {
            Assert.False(string.IsNullOrWhiteSpace(app.Id));
            Assert.False(string.IsNullOrWhiteSpace(app.Name));
            Assert.False(string.IsNullOrWhiteSpace(app.Owner));
            Assert.False(string.IsNullOrWhiteSpace(app.Repo));
        });
    }

    [Fact]
    public async Task Resolves_latest_release_asset_for_current_platform()
    {
        RecommendedApp app = CreateService("{}").GetCatalog().First(a => a.Id == "vex");
        string rid = CurrentRuntimeIdentifier();
        string expectedAsset = rid == "win-x64"
            ? "Vex-v1.5.1-win-x64-setup.exe"
            : $"Vex-v1.5.1-{rid}{PlatformExtension(rid)}";

        var service = CreateService("{}");

        RecommendedRelease? release = await service.ResolveAsync(app, force: true);

        // 网页端点把根相对链接补全为 github.com 前缀
        string downloadBase = "https://github.com/dotnet9/Vex/releases/download/v1.5.1/";
        Assert.NotNull(release);
        Assert.Equal("v1.5.1", release!.Tag);
        Assert.Equal(downloadBase + expectedAsset, release.AssetUrl);
        Assert.Equal(expectedAsset, release.AssetName);
        Assert.Equal(downloadBase + expectedAsset + ".sha256", release.ChecksumUrl);
        // 网页端点（默认路径，不占 API 配额）不携带资产大小，允许为 null；下载时由 Content-Length 补足
        Assert.Null(release.AssetSize);
        Assert.False(string.IsNullOrWhiteSpace(release.PageUrl));
    }

    [Fact]
    public async Task Returns_null_when_release_has_no_matching_platform_asset()
    {
        RecommendedApp app = CreateService("{}").GetCatalog().First(a => a.Id == "vex");
        // expanded_assets 只有别的平台的资产
        string html = "<a href=\"/dotnet9/Vex/releases/download/v1.5.1/Vex-0.9.0-osx-arm64.dmg\">x</a>";
        var handler = new StubHandler(_ => "{}", expandedHtml: html);
        var service = new RecommendedAppsService(new HttpClient(handler) { BaseAddress = new Uri("https://github.com/") });

        RecommendedRelease? release = await service.ResolveAsync(app, force: true);

        Assert.Null(release);
    }

    [Fact]
    public async Task Caches_release_within_ttl_and_skips_second_request()
    {
        RecommendedApp app = CreateService("{}").GetCatalog().First(a => a.Id == "vex");
        var handler = new StubHandler(_ => "{}");
        var service = new RecommendedAppsService(new HttpClient(handler) { BaseAddress = new Uri("https://github.com/") });

        await service.ResolveAsync(app, force: true);
        await service.ResolveAsync(app); // TTL 内：不应再请求资产列表

        Assert.Equal(1, handler.ExpandedRequests);
    }

    [Fact]
    public async Task Force_bypasses_cache()
    {
        RecommendedApp app = CreateService("{}").GetCatalog().First(a => a.Id == "vex");
        var handler = new StubHandler(_ => "{}");
        var service = new RecommendedAppsService(new HttpClient(handler) { BaseAddress = new Uri("https://github.com/") });

        await service.ResolveAsync(app, force: true);
        await service.ResolveAsync(app, force: true);

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

    private static string PlatformExtension(string rid)
        => rid.StartsWith("linux-", StringComparison.OrdinalIgnoreCase) ? ".deb"
            : rid.StartsWith("osx-", StringComparison.OrdinalIgnoreCase) ? ".dmg"
            : "-setup.exe";

    private static RecommendedAppsService CreateService(string releaseJson)
    {
        var http = new HttpClient(new StubHandler(_ => releaseJson))
        {
            BaseAddress = new Uri("https://github.com/")
        };
        return new RecommendedAppsService(http);
    }

    private static string CurrentRuntimeIdentifier()
    {
        string os = OperatingSystem.IsWindows() ? "win"
            : OperatingSystem.IsLinux() ? "linux"
            : OperatingSystem.IsMacOS() ? "osx"
            : throw new PlatformNotSupportedException();
        string arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.X64 => "x64",
            System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
            _ => "x64"
        };
        return os + "-" + arch;
    }

    /// <summary>
    /// 按请求路径返回内容：releases/latest 302 到 tag，expanded_assets 返回当前平台资产
    /// （expandedHtml 覆盖时用调用方给的），API 兜底返回 respond 的 release JSON。
    /// </summary>
    private sealed class StubHandler(Func<string, string> respond, string? expandedHtml = null) : HttpMessageHandler
    {
        public int ExpandedRequests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri?.ToString() ?? string.Empty;
            if (url.Contains("/releases/latest", StringComparison.Ordinal))
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                redirect.Headers.Location = new Uri("https://github.com/dotnet9/Vex/releases/tag/v1.5.1");
                return Task.FromResult(redirect);
            }

            if (url.Contains("expanded_assets", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref ExpandedRequests);
                if (expandedHtml is not null)
                {
                    return Task.FromResult(JsonResponse(expandedHtml));
                }

                string rid = CurrentRuntimeIdentifier();
                string asset = rid == "win-x64"
                    ? "Vex-v1.5.1-win-x64-setup.exe"
                    : $"Vex-v1.5.1-{rid}{PlatformExtension(rid)}";
                string html = "<a href=\"/dotnet9/Vex/releases/download/v1.5.1/" + asset + "\">x</a>" +
                    "<a href=\"https://github.com/dotnet9/Vex/releases/download/v1.5.1/" + asset + ".sha256\">s</a>";
                return Task.FromResult(JsonResponse(html));
            }

            if (url.Contains("api.github.com", StringComparison.Ordinal))
            {
                return Task.FromResult(JsonResponse(respond(url)));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage JsonResponse(string content)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) };
            return response;
        }
    }
}
