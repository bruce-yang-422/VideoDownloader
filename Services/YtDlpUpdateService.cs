using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VideoDownloader.Services;

public sealed record CoreUpdateResult(bool IsReady, string? Version, bool Updated, string? Warning);

public sealed class YtDlpUpdateService
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(90) };
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly string _path;
    private readonly HttpClient _client;

    public YtDlpUpdateService(string? executablePath = null, HttpClient? client = null)
    {
        _path = executablePath ?? Path.Combine(AppContext.BaseDirectory, "Tools", "yt-dlp.exe");
        _client = client ?? Client;
    }

    public async Task<CoreUpdateResult> EnsureLatestAsync(IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        string? local = null;
        string? temporary = null;
        try
        {
            progress?.Report("正在檢查下載核心…");
            local = await ReadVersionAsync(_path, cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            var token = timeout.Token;
            using var response = await GetAsync("https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest", token);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            var release = json.RootElement;
            var latest = release.GetProperty("tag_name").GetString() ?? throw new InvalidDataException("找不到版本資訊");
            var latestDate = ParseVersion(latest) ?? throw new InvalidDataException("無法辨識下載核心版本");
            if (ParseVersion(local) is { } localDate && localDate >= latestDate)
                return new(true, local, false, null);

            progress?.Report(local is null ? "正在下載下載核心…" : $"正在更新下載核心至 {latest}…");
            var assets = release.GetProperty("assets").EnumerateArray().ToArray();
            string AssetUrl(string name)
            {
                var url = assets.First(a => a.GetProperty("name").GetString() == name)
                    .GetProperty("browser_download_url").GetString()!;
                // Pin both files to the same official release, avoiding a latest-release race.
                var expected = $"https://github.com/yt-dlp/yt-dlp/releases/download/{latest}/{name}";
                if (!string.Equals(url, expected, StringComparison.Ordinal)) throw new InvalidDataException("下載來源不正確");
                return url;
            }
            using var sumsResponse = await GetAsync(AssetUrl("SHA2-256SUMS"), token);
            var sums = await sumsResponse.Content.ReadAsStringAsync(token);
            var expectedHash = sums.Split('\n').Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                .FirstOrDefault(parts => parts.Length == 2 && parts[1].TrimStart('*') == "yt-dlp.exe")?[0];
            if (expectedHash is null || expectedHash.Length != 64) throw new InvalidDataException("找不到 SHA256 校驗資料");

            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            temporary = Path.Combine(Path.GetDirectoryName(_path)!, $"yt-dlp-{Guid.NewGuid():N}.exe");
            using (var download = await GetAsync(AssetUrl("yt-dlp.exe"), token))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await download.Content.CopyToAsync(output, token);
            await using (var input = File.OpenRead(temporary))
            {
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(input, token));
                if (!hash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("下載核心 SHA256 校驗失敗，已保留舊版");
            }
            if (await ReadVersionAsync(temporary, token) != latest)
                throw new InvalidDataException("新版下載核心無法執行或版本不符，已保留舊版");
            token.ThrowIfCancellationRequested();
            // Same-volume atomic replacement preserves the existing executable if replacement fails.
            if (File.Exists(_path)) File.Replace(temporary, _path, _path + ".bak");
            else File.Move(temporary, _path);
            return new(true, latest, true, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            return new(local is not null, local, false,
                ex is OperationCanceledException ? "下載核心更新逾時" : ex.Message);
        }
        finally
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            Gate.Release();
        }
    }

    private async Task<HttpResponseMessage> GetAsync(string url, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("VideoDownloader/1.0");
        var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        try { response.EnsureSuccessStatusCode(); return response; }
        catch { response.Dispose(); throw; }
    }

    private static DateTime? ParseVersion(string? version) =>
        DateTime.TryParseExact(version, "yyyy.MM.dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    private static async Task<string?> ReadVersionAsync(string path, CancellationToken token)
    {
        if (!File.Exists(path)) return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(path)
            {
                Arguments = "--version", UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            }
        };
        try
        {
            // Reject an obviously corrupt executable before Windows attempts to launch it.
            await using (var file = File.OpenRead(path))
            {
                var signature = new byte[2];
                if (await file.ReadAsync(signature, token) != 2 || signature[0] != 'M' || signature[1] != 'Z')
                    return null;
            }
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
                var version = (await output).Trim();
                await error;
                return process.ExitCode == 0 && ParseVersion(version) is not null ? version : null;
            }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                try { await Task.WhenAll(output, error); } catch (OperationCanceledException) { }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { return null; }
    }
}
