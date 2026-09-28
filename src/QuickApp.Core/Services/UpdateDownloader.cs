using System;
using System.IO;
using System.Net.Http;
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

        string fileName = Path.GetFileName(update.AssetName ?? string.Empty);
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
}
