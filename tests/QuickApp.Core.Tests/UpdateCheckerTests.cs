using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using QuickApp.Core.Services;
using Xunit;

namespace QuickApp.Core.Tests;

public sealed class UpdateCheckerTests
{
    [Fact]
    public async Task Selects_current_platform_asset_and_prefers_windows_installer()
    {
        string rid = CurrentRuntimeIdentifier();
        string nativeExtension = rid.StartsWith("linux-", StringComparison.OrdinalIgnoreCase)
            ? ".deb"
            : rid.StartsWith("osx-", StringComparison.OrdinalIgnoreCase)
                ? ".pkg"
                : ".zip";
        string expectedName = rid == "win-x64"
            ? "QuickApp-v9.9.9-win-x64-setup.exe"
            : $"QuickApp-v9.9.9-{rid}{nativeExtension}";

        string json = $"{{\"tag_name\":\"v9.9.9\",\"name\":\"QuickApp v9.9.9\",\"html_url\":\"https://example.test/release\",\"assets\":[" +
            "{\"name\":\"QuickApp-v9.9.9-win-x64.zip\",\"browser_download_url\":\"https://example.test/win.zip\"}," +
            $"{{\"name\":\"{expectedName}\",\"browser_download_url\":\"https://example.test/current\"}}," +
            $"{{\"name\":\"{expectedName}.sha256\",\"browser_download_url\":\"https://example.test/current.sha256\"}}]}}";

        var checker = CreateChecker(json);

        UpdateCheckResult result = await checker.CheckAsync(new Version(0, 1, 0));

        Assert.True(result.Succeeded);
        UpdateInfo? update = result.Update;
        Assert.NotNull(update);
        Assert.Equal(expectedName, update!.AssetName);
        Assert.Equal("https://example.test/current", update.AssetUrl);
        Assert.Equal("https://example.test/current.sha256", update.ChecksumUrl);
    }

    [Fact]
    public async Task Leaves_asset_empty_when_release_has_no_current_platform_package()
    {
        var checker = CreateChecker("{\"tag_name\":\"v9.9.9\",\"html_url\":\"https://example.test/release\",\"assets\":[{\"name\":\"QuickApp-v9.9.9-win-x64.zip\",\"browser_download_url\":\"https://example.test/win.zip\"}]}");

        UpdateCheckResult result = await checker.CheckAsync(new Version(0, 1, 0));

        Assert.True(result.Succeeded);
        UpdateInfo? update = result.Update;
        Assert.NotNull(update);
        if (CurrentRuntimeIdentifier() == "win-x64")
        {
            Assert.Equal("QuickApp-v9.9.9-win-x64.zip", update!.AssetName);
        }
        else
        {
            Assert.Null(update!.AssetName);
            Assert.Null(update.AssetUrl);
        }
    }

    [Fact]
    public async Task Portable_mode_prefers_zip_over_windows_installer()
    {
        if (CurrentRuntimeIdentifier() != "win-x64")
        {
            return;
        }

        const string json = "{\"tag_name\":\"v9.9.9\",\"assets\":[" +
            "{\"name\":\"QuickApp-v9.9.9-win-x64-setup.exe\",\"browser_download_url\":\"https://example.test/setup\"}," +
            "{\"name\":\"QuickApp-v9.9.9-win-x64.zip\",\"browser_download_url\":\"https://example.test/zip\"}]}";
        var checker = new UpdateChecker(
            new HttpClient(new StubHandler(json)),
            owner: "dotnet9",
            repo: "QuickApp",
            apiBase: "https://example.test",
            preferInstaller: false,
            webBase: "https://example.test");

        UpdateCheckResult result = await checker.CheckAsync(new Version(0, 1, 0));

        Assert.True(result.Succeeded);
        UpdateInfo? update = result.Update;
        Assert.NotNull(update);
        Assert.Equal("QuickApp-v9.9.9-win-x64.zip", update!.AssetName);
        Assert.Equal("https://example.test/zip", update.AssetUrl);
    }

    [Fact]
    public async Task Reports_http_failure_separately_from_latest_version()
    {
        var checker = new UpdateChecker(
            new HttpClient(new StubHandler("unavailable", HttpStatusCode.InternalServerError)),
            owner: "dotnet9",
            repo: "QuickApp",
            apiBase: "https://example.test",
            webBase: "https://example.test");

        UpdateCheckResult result = await checker.CheckAsync(new Version(0, 1, 0));

        Assert.False(result.Succeeded);
        Assert.Null(result.Update);
        Assert.Equal("HTTP 500", result.Error);
    }

    [Fact]
    public async Task Rate_limit_blocks_api_until_reset()
    {
        long reset = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds();
        var handler = new StubHandler("rate limited", HttpStatusCode.Forbidden, rateLimitReset: reset.ToString());
        var checker = new UpdateChecker(
            new HttpClient(handler),
            owner: "dotnet9",
            repo: "QuickApp",
            apiBase: "https://example.test",
            webBase: "https://example.test");

        UpdateCheckResult first = await checker.CheckAsync(new Version(0, 1, 0));
        UpdateCheckResult second = await checker.CheckAsync(new Version(0, 1, 0));

        Assert.False(first.Succeeded);
        Assert.Contains("限流", first.Error);
        Assert.False(second.Succeeded);
        Assert.Contains("限流", second.Error);
        Assert.Equal(1, handler.ApiRequests);                   // 限流期间不再请求 API
        Assert.Equal(3, handler.Requests);                      // 网页端点不占配额，仍会尝试（1 + 退回 1 + 第二次网页 1）
    }

    [Fact]
    public async Task Etag_enables_conditional_requests_and_304_keeps_cached_release()
    {
        string dir = Path.Combine(Path.GetTempPath(), "QuickAppTests", Guid.NewGuid().ToString("N"));
        string stateFile = Path.Combine(dir, "update-state.json");
        try
        {
            string json = "{\"tag_name\":\"v9.9.9\",\"name\":\"QuickApp v9.9.9\",\"html_url\":\"https://example.test/release\",\"assets\":[]}";
            var okHandler = new StubHandler(json, etag: "\"v999\"");
            var okChecker = new UpdateChecker(
                new HttpClient(okHandler),
                owner: "dotnet9",
                repo: "QuickApp",
                apiBase: "https://example.test",
                stateFile: stateFile,
                webBase: "https://example.test");

            UpdateCheckResult first = await okChecker.CheckAsync(new Version(0, 1, 0));

            Assert.True(first.Succeeded);
            Assert.True(File.Exists(stateFile));
            Assert.Contains("v999", File.ReadAllText(stateFile));   // ETag 与 release 一起落盘

            // 网页端点没解析出 tag → 退回 API；304 表示最新发布没变，
            // 旧版本应用仍应从缓存 release 得出「有更新」
            var notModifiedHandler = new StubHandler(string.Empty, HttpStatusCode.NotModified);
            var againChecker = new UpdateChecker(
                new HttpClient(notModifiedHandler),
                owner: "dotnet9",
                repo: "QuickApp",
                apiBase: "https://example.test",
                stateFile: stateFile,
                webBase: "https://example.test");

            UpdateCheckResult second = await againChecker.CheckAsync(new Version(0, 1, 0));

            Assert.Equal(1, notModifiedHandler.ApiRequests);
            Assert.Equal("\"v999\"", notModifiedHandler.LastIfNoneMatch);
            Assert.True(second.Succeeded);
            Assert.NotNull(second.Update);
            Assert.Equal("v9.9.9", second.Update!.Tag);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Web_redirect_and_expanded_assets_resolve_update_without_api()
    {
        string rid = CurrentRuntimeIdentifier();
        string nativeExtension = rid.StartsWith("linux-", StringComparison.OrdinalIgnoreCase)
            ? ".deb"
            : rid.StartsWith("osx-", StringComparison.OrdinalIgnoreCase)
                ? ".pkg"
                : ".zip";
        string expectedName = rid == "win-x64"
            ? "QuickApp-v9.9.9-win-x64-setup.exe"
            : $"QuickApp-v9.9.9-{rid}{nativeExtension}";

        // 一个 href 根相对、一个绝对，覆盖 expanded_assets 里的两种写法
        string html = "<div>" +
            $"<a href=\"/dotnet9/QuickApp/releases/download/v9.9.9/{expectedName}\">…</a>" +
            $"<a href=\"https://example.test/dotnet9/QuickApp/releases/download/v9.9.9/{expectedName}.sha256\">…</a>" +
            $"<a href=\"/dotnet9/QuickApp/releases/download/other/v9.9.9/ignored.txt\">…</a>" +
            "</div>";

        // 未跟随重定向：releases/latest 返回 302 + Location（自定义 handler 不做自动重定向）
        var handler = new WebStubHandler(request =>
        {
            string url = request.RequestUri?.AbsoluteUri ?? string.Empty;
            if (url.EndsWith("/releases/latest", StringComparison.Ordinal))
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                redirect.Headers.Location = new Uri("/dotnet9/QuickApp/releases/tag/v9.9.9", UriKind.Relative);
                return redirect;
            }

            return Html(html);
        });
        var checker = new UpdateChecker(
            new HttpClient(handler),
            owner: "dotnet9",
            repo: "QuickApp",
            apiBase: "https://api.example.test",
            webBase: "https://example.test");

        UpdateCheckResult result = await checker.CheckAsync(new Version(0, 1, 0));

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Update);
        Assert.Equal("v9.9.9", result.Update!.Tag);
        Assert.Equal(expectedName, result.Update.AssetName);
        Assert.Equal($"https://example.test/dotnet9/QuickApp/releases/download/v9.9.9/{expectedName}", result.Update.AssetUrl);
        Assert.Equal($"https://example.test/dotnet9/QuickApp/releases/download/v9.9.9/{expectedName}.sha256", result.Update.ChecksumUrl);
        Assert.Equal("https://example.test/dotnet9/QuickApp/releases/tag/v9.9.9", result.Update.PageUrl);
        Assert.Equal(0, handler.ApiRequests);
        Assert.Equal(1, handler.AssetRequests);
    }

    [Fact]
    public async Task Web_auto_redirect_final_url_also_resolves_tag()
    {
        // 跟随重定向时（真实 HttpClient 的默认行为），最终地址写在响应的 RequestMessage 上
        var handler = new WebStubHandler(request =>
        {
            string url = request.RequestUri?.AbsoluteUri ?? string.Empty;
            if (url.EndsWith("/releases/latest", StringComparison.Ordinal))
            {
                var final = new HttpRequestMessage(HttpMethod.Get, "https://example.test/dotnet9/QuickApp/releases/tag/v9.9.9");
                return new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = final };
            }

            return Html("<a href=\"/dotnet9/QuickApp/releases/download/v9.9.9/QuickApp-v9.9.9.zip\">…</a>");
        });
        var checker = new UpdateChecker(
            new HttpClient(handler),
            owner: "dotnet9",
            repo: "QuickApp",
            apiBase: "https://api.example.test",
            webBase: "https://example.test");

        UpdateCheckResult result = await checker.CheckAsync(new Version(0, 1, 0));

        Assert.True(result.Succeeded);
        Assert.Equal("v9.9.9", result.Update!.Tag);
        Assert.Equal(0, handler.ApiRequests);
    }

    [Fact]
    public async Task Unchanged_tag_reuses_cached_release_without_asset_or_api_requests()
    {
        string dir = Path.Combine(Path.GetTempPath(), "QuickAppTests", Guid.NewGuid().ToString("N"));
        string stateFile = Path.Combine(dir, "update-state.json");
        try
        {
            var handler = new WebStubHandler(request =>
            {
                string url = request.RequestUri?.AbsoluteUri ?? string.Empty;
                if (url.EndsWith("/releases/latest", StringComparison.Ordinal))
                {
                    var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                    redirect.Headers.Location = new Uri("/dotnet9/QuickApp/releases/tag/v9.9.9", UriKind.Relative);
                    return redirect;
                }

                return Html("<a href=\"/dotnet9/QuickApp/releases/download/v9.9.9/QuickApp-v9.9.9.zip\">…</a>");
            });
            var first = new UpdateChecker(
                new HttpClient(handler),
                owner: "dotnet9",
                repo: "QuickApp",
                apiBase: "https://api.example.test",
                stateFile: stateFile,
                webBase: "https://example.test");
            UpdateCheckResult firstResult = await first.CheckAsync(new Version(0, 1, 0));

            // tag 没变：第二个检查器（仅靠落盘缓存）应跳过资产列表与 API
            var second = new UpdateChecker(
                new HttpClient(handler),
                owner: "dotnet9",
                repo: "QuickApp",
                apiBase: "https://api.example.test",
                stateFile: stateFile,
                webBase: "https://example.test");
            UpdateCheckResult secondResult = await second.CheckAsync(new Version(0, 1, 0));

            Assert.True(firstResult.Succeeded);
            Assert.True(secondResult.Succeeded);
            Assert.Equal("v9.9.9", secondResult.Update!.Tag);
            Assert.Equal(1, handler.AssetRequests);
            Assert.Equal(0, handler.ApiRequests);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Falls_back_to_api_when_web_endpoints_unavailable()
    {
        const string json = "{\"tag_name\":\"v9.9.9\",\"assets\":[" +
            "{\"name\":\"QuickApp-v9.9.9-win-x64-setup.exe\",\"browser_download_url\":\"https://example.test/setup\"}]}";
        var handler = new WebStubHandler(request =>
        {
            string url = request.RequestUri?.AbsoluteUri ?? string.Empty;
            if (url.Contains("api.example.test", StringComparison.Ordinal))
            {
                return Json(json);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent(string.Empty) };
        });
        var checker = new UpdateChecker(
            new HttpClient(handler),
            owner: "dotnet9",
            repo: "QuickApp",
            apiBase: "https://api.example.test",
            webBase: "https://example.test");

        UpdateCheckResult result = await checker.CheckAsync(new Version(0, 1, 0));

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Update);
        Assert.Equal("v9.9.9", result.Update!.Tag);
        Assert.Equal(1, handler.ApiRequests);
    }

    [Theory]
    [InlineData("linux-x64", "QuickApp-v9.9.9-linux-x64.deb")]
    [InlineData("linux-arm64", "QuickApp-v9.9.9-linux-arm64.deb")]
    [InlineData("osx-x64", "QuickApp-v9.9.9-osx-x64.pkg")]
    [InlineData("osx-arm64", "QuickApp-v9.9.9-osx-arm64.pkg")]
    public async Task Selects_native_asset_for_requested_platform(string rid, string expectedName)
    {
        string json = $"{{\"tag_name\":\"v9.9.9\",\"assets\":[" +
            $"{{\"name\":\"{expectedName}\",\"browser_download_url\":\"https://example.test/native\"}}," +
            $"{{\"name\":\"{expectedName}.sha256\",\"browser_download_url\":\"https://example.test/native.sha256\"}}," +
            $"{{\"name\":\"QuickApp-v9.9.9-{rid}.dmg\",\"browser_download_url\":\"https://example.test/dmg\"}}]}}";
        var checker = new UpdateChecker(
            new HttpClient(new StubHandler(json)),
            owner: "dotnet9",
            repo: "QuickApp",
            apiBase: "https://example.test",
            runtimeIdentifier: rid,
            webBase: "https://example.test");

        UpdateCheckResult result = await checker.CheckAsync(new Version(0, 1, 0));

        Assert.True(result.Succeeded);
        Assert.Equal(expectedName, result.Update!.AssetName);
        Assert.Equal("https://example.test/native.sha256", result.Update.ChecksumUrl);
    }

    private static UpdateChecker CreateChecker(string json)
    {
        var handler = new StubHandler(json);
        return new UpdateChecker(
            new HttpClient(handler),
            owner: "dotnet9",
            repo: "QuickApp",
            apiBase: "https://example.test",
            webBase: "https://example.test");
    }

    private static HttpResponseMessage Json(string json)
        => new(HttpStatusCode.OK) { Content = new StringContent(json) };

    private static HttpResponseMessage Html(string html)
        => new(HttpStatusCode.OK) { Content = new StringContent(html) };

    private static string CurrentRuntimeIdentifier()
    {
        string os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsLinux() ? "linux" : OperatingSystem.IsMacOS() ? "osx" : "unknown";
        string architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X86 => "x86",
            Architecture.X64 => "x64",
            Architecture.Arm => "arm",
            Architecture.Arm64 => "arm64",
            _ => "unknown"
        };
        return $"{os}-{architecture}";
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _json;
        private readonly HttpStatusCode _statusCode;
        private readonly string? _etag;
        private readonly string? _rateLimitReset;

        public StubHandler(
            string json,
            HttpStatusCode statusCode = HttpStatusCode.OK,
            string? etag = null,
            string? rateLimitReset = null)
        {
            _json = json;
            _statusCode = statusCode;
            _etag = etag;
            _rateLimitReset = rateLimitReset;
        }

        public int Requests { get; private set; }

        public int ApiRequests { get; private set; }

        public string? LastIfNoneMatch { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            if ((request.RequestUri?.AbsoluteUri ?? string.Empty).Contains("/repos/", StringComparison.Ordinal))
            {
                ApiRequests++;
            }

            LastIfNoneMatch = request.Headers.IfNoneMatch.SingleOrDefault()?.Tag;
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_json)
            };
            if (_etag is not null)
            {
                response.Headers.ETag = new EntityTagHeaderValue(_etag);
            }

            if (_rateLimitReset is not null)
            {
                response.Headers.Add("X-RateLimit-Reset", _rateLimitReset);
            }

            return Task.FromResult(response);
        }
    }

    /// <summary>按 URL 路由响应的桩：网页端点测试用（/releases/latest、/expanded_assets、API 各返回不同内容）。</summary>
    private sealed class WebStubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public WebStubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public int Requests { get; private set; }

        public int ApiRequests { get; private set; }

        public int AssetRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            string url = request.RequestUri?.AbsoluteUri ?? string.Empty;
            if (url.Contains("/repos/", StringComparison.Ordinal))
            {
                ApiRequests++;
            }

            if (url.Contains("/expanded_assets/", StringComparison.Ordinal))
            {
                AssetRequests++;
            }

            HttpResponseMessage response = _responder(request);
            response.RequestMessage ??= request;
            return Task.FromResult(response);
        }
    }
}
