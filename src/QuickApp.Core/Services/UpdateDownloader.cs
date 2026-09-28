using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace QuickApp.Core.Services;

/// <summary>
/// 把 GitHub Release 资产下载到临时目录。使用临时文件和原子改名，
/// 避免半个安装包被误当成可执行文件。
/// </summary>
public sealed class UpdateDownloader : IUpdateDownloader
{
    private readonly HttpClient _http;

    public UpdateDownloader(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    public async Task<UpdateDownloadResult> DownloadAsync(
        UpdateInfo update,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (update is null)
        {
            throw new ArgumentNullException(nameof(update));
        }
        if (string.IsNullOrWhiteSpace(update.AssetUrl))
        {
            throw new InvalidOperationException("更新没有可下载的资产。");
        }
        if (string.IsNullOrWhiteSpace(update.ChecksumUrl))
        {
            throw new InvalidOperationException("更新资产缺少 SHA-256 校验文件。");
        }
        if (!IsHttpsUrl(update.AssetUrl) || !IsHttpsUrl(update.ChecksumUrl))
        {
            throw new InvalidOperationException("更新地址必须使用 HTTPS。");
        }

        string fileName = Path.GetFileName((update.AssetName ?? string.Empty).Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidOperationException("更新资产名称无效。");
        }

        string directory = Path.Combine(Path.GetTempPath(), "QuickApp", "updates");
        Directory.CreateDirectory(directory);
        string finalPath = Path.Combine(directory, fileName);
        string partialPath = finalPath + ".download";

        using var request = new HttpRequestMessage(HttpMethod.Get, update.AssetUrl);
        request.Headers.TryAddWithoutValidation("Accept", "application/octet-stream");
        request.Headers.TryAddWithoutValidation("User-Agent", "QuickApp-UpdateDownloader");
        using HttpResponseMessage response = await _http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        long? totalBytes = response.Content.Headers.ContentLength;
        long received = 0;
        try
        {
            {
                await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using FileStream target = new(
                    partialPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 81920,
                    useAsync: true);

                byte[] buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    received += read;
                    progress?.Report(new UpdateDownloadProgress(received, totalBytes));
                }

                await target.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            using var checksumRequest = new HttpRequestMessage(HttpMethod.Get, update.ChecksumUrl);
            checksumRequest.Headers.TryAddWithoutValidation("Accept", "application/octet-stream");
            checksumRequest.Headers.TryAddWithoutValidation("User-Agent", "QuickApp-UpdateDownloader");
            using HttpResponseMessage checksumResponse = await _http.SendAsync(
                checksumRequest,
                cancellationToken).ConfigureAwait(false);
            checksumResponse.EnsureSuccessStatusCode();
            string checksumText = await checksumResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            string[] checksumTokens = checksumText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (checksumTokens.Length == 0 || checksumTokens[0].Length != 64 || !checksumTokens[0].All(Uri.IsHexDigit))
            {
                throw new InvalidDataException("SHA-256 校验文件格式无效。");
            }

            await using (FileStream downloaded = File.OpenRead(partialPath))
            {
                byte[] actualHash = await SHA256.HashDataAsync(downloaded, cancellationToken).ConfigureAwait(false);
                string actualHashText = Convert.ToHexString(actualHash);
                if (!string.Equals(actualHashText, checksumTokens[0], StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("更新文件 SHA-256 校验失败。");
                }
            }

            File.Move(partialPath, finalPath, overwrite: true);
        }
        catch
        {
            TryDelete(partialPath);
            throw;
        }

        progress?.Report(new UpdateDownloadProgress(received, totalBytes ?? received));
        return new UpdateDownloadResult(finalPath, fileName, received);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // A stale partial file is harmless and will be overwritten next time.
        }
    }

    private static bool IsHttpsUrl(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed) && parsed.Scheme == Uri.UriSchemeHttps;
}
