using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using QuickApp.Core.Services;
using Xunit;

namespace QuickApp.Core.Tests;

public sealed class UpdateDownloaderTests
{
    [Fact]
    public async Task Downloads_asset_to_temp_file_and_reports_progress()
    {
        byte[] payload = new byte[128 * 1024];
        new Random(42).NextBytes(payload);
        string fileName = "QuickApp-test-" + Guid.NewGuid().ToString("N") + ".deb";
        var progress = new List<UpdateDownloadProgress>();
        var downloader = new UpdateDownloader(new HttpClient(new StubHandler(payload)));

        UpdateDownloadResult result = await downloader.DownloadAsync(
            new UpdateInfo(
                new Version(9, 9, 9),
                "v9.9.9",
                "QuickApp v9.9.9",
                null,
                "https://example.test/release",
                "https://example.test/asset",
                fileName),
            new Progress<UpdateDownloadProgress>(progress.Add));

        try
        {
            Assert.Equal(fileName, result.FileName);
            Assert.Equal(payload.LongLength, result.BytesReceived);
            Assert.Equal(payload, await System.IO.File.ReadAllBytesAsync(result.FilePath));
            Assert.NotEmpty(progress);
            Assert.Equal(100, progress[^1].Percentage);
        }
        finally
        {
            if (System.IO.File.Exists(result.FilePath))
            {
                System.IO.File.Delete(result.FilePath);
            }
        }
    }

    [Fact]
    public async Task Rejects_http_failure()
    {
        var downloader = new UpdateDownloader(new HttpClient(new StubHandler(Array.Empty<byte>(), HttpStatusCode.ServiceUnavailable)));

        await Assert.ThrowsAsync<HttpRequestException>(() => downloader.DownloadAsync(
            new UpdateInfo(
                new Version(9, 9, 9),
                "v9.9.9",
                "QuickApp v9.9.9",
                null,
                "https://example.test/release",
                "https://example.test/asset",
                "QuickApp-test.deb")));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly byte[] _payload;
        private readonly HttpStatusCode _statusCode;

        public StubHandler(byte[] payload, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _payload = payload;
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new ByteArrayContent(_payload)
            });
        }
    }
}
