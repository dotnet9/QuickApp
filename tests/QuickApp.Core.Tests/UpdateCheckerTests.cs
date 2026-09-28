using System;
using System.Net;
using System.Net.Http;
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
            preferInstaller: false);

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
            new HttpClient(new StubHandler("unavailable", HttpStatusCode.Forbidden)),
            owner: "dotnet9",
            repo: "QuickApp",
            apiBase: "https://example.test");

        UpdateCheckResult result = await checker.CheckAsync(new Version(0, 1, 0));

        Assert.False(result.Succeeded);
        Assert.Null(result.Update);
        Assert.Equal("HTTP 403", result.Error);
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
            runtimeIdentifier: rid);

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
            apiBase: "https://example.test");
    }

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

        public StubHandler(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _json = json;
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_json)
            });
        }
    }
}
