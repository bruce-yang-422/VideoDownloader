using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VideoDownloader.Services;

public sealed class FfmpegSetupService
{
    public static string InstallDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VideoDownloader", "Tools", "ffmpeg");
    private static readonly HttpClient Client = new();
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly HttpClient _client;
    private readonly string _destination;
    public FfmpegSetupService(HttpClient? client = null, string? destination = null)
    { _client = client ?? Client; _destination = destination ?? InstallDirectory; }

    public async Task EnsureAsync(IProgress<string>? progress, CancellationToken token)
    {
        await Gate.WaitAsync(token);
        string? stage = null;
        try
        {
            progress?.Report("正在檢查 FFmpeg…");
            var existing = _destination == InstallDirectory ? YtDlpService.FindFfmpegDirectory() : _destination;
            if (existing is not null && await IsUsableAsync(existing, token)) return;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromMinutes(10));
            var ct = timeout.Token;
            progress?.Report("正在下載 FFmpeg（首次準備可能需數分鐘）…");
            using var releaseResponse = await GetAsync("https://api.github.com/repos/GyanD/codexffmpeg/releases/latest", ct);
            using var json = JsonDocument.Parse(await releaseResponse.Content.ReadAsStringAsync(ct));
            var asset = json.RootElement.GetProperty("assets").EnumerateArray()
                .First(a => a.GetProperty("name").GetString()!.EndsWith("-essentials_build.zip", StringComparison.Ordinal));
            var url = asset.GetProperty("browser_download_url").GetString()!;
            if (!url.StartsWith("https://github.com/GyanD/codexffmpeg/releases/download/", StringComparison.Ordinal))
                throw new InvalidDataException("FFmpeg 下載來源不正確");
            var digest = asset.GetProperty("digest").GetString();
            if (digest is null || !digest.StartsWith("sha256:") || digest.Length != 71)
                throw new InvalidDataException("FFmpeg 發行檔缺少 SHA256 資料");
            var parent = Path.GetDirectoryName(_destination)!;
            Directory.CreateDirectory(parent);
            stage = Path.Combine(parent, ".ffmpeg-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            var zip = Path.Combine(stage, "package.zip");
            using (var download = await GetAsync(url, ct))
            await using (var output = File.Create(zip)) await download.Content.CopyToAsync(output, ct);
            await using (var input = File.OpenRead(zip))
            {
                if (!Convert.ToHexString(await SHA256.HashDataAsync(input, ct)).Equals(digest[7..], StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("FFmpeg SHA256 校驗失敗");
            }
            progress?.Report("正在驗證並安裝 FFmpeg…");
            var extracted = Path.Combine(stage, "ready");
            Directory.CreateDirectory(extracted);
            using (var archive = ZipFile.OpenRead(zip))
            {
                foreach (var name in new[] { "ffmpeg.exe", "ffprobe.exe", "LICENSE", "README.txt" })
                {
                    var entry = archive.Entries.FirstOrDefault(e => e.Name == name);
                    if (entry is null && name.EndsWith(".exe")) throw new InvalidDataException("FFmpeg 壓縮檔缺少 " + name);
                    if (entry is not null)
                    {
                        await using var input = entry.Open();
                        await using var output = File.Create(Path.Combine(extracted, name));
                        await input.CopyToAsync(output, ct);
                    }
                }
            }
            if (!await IsUsableAsync(extracted, ct)) throw new InvalidDataException("FFmpeg 無法執行");
            ct.ThrowIfCancellationRequested();
            var backup = _destination + ".backup-" + Guid.NewGuid().ToString("N");
            var hadPrevious = Directory.Exists(_destination);
            if (hadPrevious) Directory.Move(_destination, backup);
            try { Directory.Move(extracted, _destination); }
            catch { if (hadPrevious) Directory.Move(backup, _destination); throw; }
        }
        finally
        {
            if (stage is not null && Path.GetDirectoryName(stage) == Path.GetDirectoryName(_destination) &&
                Path.GetFileName(stage).StartsWith(".ffmpeg-", StringComparison.Ordinal))
            {
                try { Directory.Delete(stage, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            Gate.Release();
        }
    }
    private async Task<HttpResponseMessage> GetAsync(string url, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("VideoDownloader/1.0");
        var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        try { response.EnsureSuccessStatusCode(); return response; } catch { response.Dispose(); throw; }
    }
    private static async Task<bool> IsUsableAsync(string directory, CancellationToken token)
    {
        foreach (var name in new[] { "ffmpeg.exe", "ffprobe.exe" })
        {
            var path = Path.Combine(directory, name);
            if (!File.Exists(path)) return false;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            using var process = new Process { StartInfo = new ProcessStartInfo(path, "-version")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
            try
            {
                process.Start();
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                try { await process.WaitForExitAsync(timeout.Token); if (process.ExitCode != 0) return false; }
                finally
                {
                    if (!process.HasExited) process.Kill(true);
                    await process.WaitForExitAsync();
                    await Task.WhenAll(output, error);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception) { return false; }
        }
        return true;
    }
}
