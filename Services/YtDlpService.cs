using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VideoDownloader.Services;

public sealed record VideoInfo(string Title, string Author, double? Duration, string? Thumbnail, IReadOnlyList<VideoQuality> Qualities, bool HasAudio, int? PlaylistIndex = null);
public sealed record DownloadProgress(double? Percent, double Downloaded, double? Speed, double? Eta, string Stage = "下載串流");

public sealed class YtDlpService
{
    public string ExecutablePath { get; } = Path.Combine(AppContext.BaseDirectory, "Tools", "yt-dlp.exe");
    public bool IsAvailable => File.Exists(ExecutablePath);

    public static bool IsValidUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && uri.Host.Length > 0;

    public async Task<VideoInfo> GetInfoAsync(string url, CancellationToken cancellationToken)
    {
        if (!IsValidUrl(url)) throw new ArgumentException("請輸入完整的 HTTP 或 HTTPS 網址");
        var json = new StringBuilder();
        try
        {
            // A mixed-media post can contain playable videos alongside failed/image entries.
            // Metadata only: actual downloads must still fail on extraction/conversion errors.
            await RunAsync(["--dump-single-json", "--skip-download", "--ignore-errors", "--format", "bv*+ba/b/ba", "--", url],
                line => json.AppendLine(line), cancellationToken);
        }
        catch (InvalidOperationException) when (json.Length > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return ParseInfo(json.ToString()); }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException) { }
            // No playable partial result: preserve yt-dlp's original diagnostic.
            throw;
        }
        return ParseInfo(json.ToString());
    }

    internal static VideoInfo ParseInfo(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("無法取得可下載的影音資訊");
        int? playlistIndex = null;
        if (root.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array)
        {
            var position = 0;
            foreach (var entry in entries.EnumerateArray())
            {
                position++;
                if (entry.ValueKind != JsonValueKind.Object) continue;
                if (MediaOptions.GetVideoQualities(entry).Count == 0 && !MediaOptions.HasAudio(entry)) continue;
                var index = Number(entry, "playlist_index");
                playlistIndex = index is > 0 and <= int.MaxValue && index == Math.Truncate(index.Value)
                    ? (int)index.Value : position;
                root = entry;
                break;
            }
            if (playlistIndex is null)
                throw new InvalidOperationException("這則貼文沒有可下載的影片或音訊，可能只有圖片或需要登入。");
        }
        return new VideoInfo(Text(root, "title") ?? "未命名影片",
            Text(root, "uploader") ?? Text(root, "channel") ?? "未知作者",
            Number(root, "duration"), Text(root, "thumbnail"), MediaOptions.GetVideoQualities(root), MediaOptions.HasAudio(root), playlistIndex);
    }

    public async Task<string> DownloadAsync(string url, string directory,
        IProgress<DownloadProgress> progress, CancellationToken cancellationToken, DownloadOptions? options = null)
    {
        if (!IsValidUrl(url)) throw new ArgumentException("請輸入完整的 HTTP 或 HTTPS 網址");
        if (!Path.IsPathFullyQualified(directory)) throw new ArgumentException("請選擇完整的儲存路徑");
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(directory);
        var work = Path.Combine(Path.GetFullPath(directory), ".videodownloader-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var source = await DownloadCoreAsync(url, work, progress, cancellationToken, options);
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.Combine(directory, Path.GetFileName(source));
            for (var suffix = 1; ; suffix++)
            {
                try { File.Move(source, target); return target; }
                catch (IOException) when (File.Exists(target))
                {
                    target = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(source)} ({suffix}){Path.GetExtension(source)}");
                }
            }
        }
        finally
        {
            // Only this invocation's freshly created staging directory is eligible for cleanup.
            if (Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(work)!) == Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) &&
                Path.GetFileName(work).StartsWith(".videodownloader-", StringComparison.Ordinal))
            {
                try { Directory.Delete(work, recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private async Task<string> DownloadCoreAsync(string url, string directory,
        IProgress<DownloadProgress> progress, CancellationToken cancellationToken, DownloadOptions? options = null)
    {
        if (!IsValidUrl(url)) throw new ArgumentException("請輸入完整的 HTTP 或 HTTPS 網址");
        if (!Path.IsPathFullyQualified(directory)) throw new ArgumentException("請選擇完整的儲存路徑");
        options ??= new DownloadOptions("mkv", null, null);
        var ffmpeg = FindFfmpegDirectory();
        if (ffmpeg is null) throw new FileNotFoundException("需要 FFmpeg 與 ffprobe，請放入 Tools 資料夾或加入 PATH");
        Directory.CreateDirectory(directory);
        string? outputPath = null;
        var processing = new FfmpegProgressReader(progress);
        var progressFile = Path.Combine(directory, "ffmpeg-progress.txt");
        var arguments = MediaOptions.BuildArguments(options).ToList();
        arguments.AddRange([
            "--ffmpeg-location", ffmpeg,
            "--newline", "--progress", "--no-simulate",
            "--progress-template", "download:VD_PROGRESS:%(progress)j",
            "--print", "before_dl:VD_DURATION:%(duration)j",
            "--progress-template", "postprocess:VD_POST:%(progress.postprocessor)s|%(progress.status)s",
            "--postprocessor-args", "ffmpeg:-progress \"" + progressFile + "\" -stats_period 0.2",
            "--print", "after_move:VD_FILE:%(filepath)j",
            "--paths", directory, "--output", "%(title).120B [%(id)s]." + options.FileTag + ".%(ext)s",
            "--no-overwrites", "--", url]);
        using var watchCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var watch = processing.WatchAsync(progressFile, watchCancellation.Token);
        try
        {
        await RunAsync(arguments.ToArray(), line =>
        {
            if (line.StartsWith("VD_DURATION:"))
            {
                if (double.TryParse(line[12..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var duration)) processing.Duration = duration;
                return;
            }
            if (line.StartsWith("VD_POST:"))
            {
                var parts = line[8..].Split('|');
                if (parts.Length == 2)
                {
                    if (parts[1] == "started")
                    {
                        processing.Begin(parts[0] switch { "Merger" => "合併音視訊", "ExtractAudio" => "擷取音訊", "VideoRemuxer" => "封裝影片", _ => "整理檔案" });
                    }
                    else if (parts[1] == "finished") { processing.ReadFile(progressFile); processing.End(); }
                }
                return;
            }
            if (line.StartsWith("VD_FILE:", StringComparison.Ordinal))
                outputPath = JsonSerializer.Deserialize<string>(line[8..]);
            if (!line.StartsWith("VD_PROGRESS:", StringComparison.Ordinal)) return;
            using var document = JsonDocument.Parse(line[12..]);
            var root = document.RootElement;
            var downloaded = Number(root, "downloaded_bytes") ?? 0;
            var total = Number(root, "total_bytes") ?? Number(root, "total_bytes_estimate");
            progress.Report(new DownloadProgress(total > 0 ? Math.Clamp(downloaded / total.Value * 100, 0, 100) : null,
                downloaded, Number(root, "speed"), Number(root, "eta")));
        }, cancellationToken);
        }
        finally
        {
            watchCancellation.Cancel();
            try { await watch; } catch (OperationCanceledException) { }
        }
        if (outputPath is null || !File.Exists(outputPath))
            throw new IOException("下載程序結束，但找不到輸出檔案");
        if (options.AudioOnly)
        {
            var finalPath = Path.ChangeExtension(outputPath, options.Format);
            var temporaryPath = Path.Combine(directory, $"{Guid.NewGuid():N}.{options.Format}");
            try
            {
                var conversion = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin", "-i", outputPath, "-vn" };
                if (options.Format == "wav")
                {
                    var quality = options.Audio!.Value.Split('-');
                    conversion.AddRange(["-c:a", quality[0] == "s24" ? "pcm_s24le" : "pcm_s16le"]);
                    if (quality.Length == 2) conversion.AddRange(["-ar", quality[1]]);
                }
                else
                {
                    conversion.AddRange(["-c:a", "libmp3lame"]);
                    conversion.AddRange(options.Audio!.Value == "0"
                        ? new[] { "-q:a", "0" } : new[] { "-b:a", options.Audio.Value });
                }
                if (processing.Duration is not > 0)
                {
                    var durationText = new StringBuilder();
                    await RunAsync(["-v", "error", "-show_entries", "format=duration", "-of", "default=noprint_wrappers=1:nokey=1", outputPath],
                        line => durationText.Append(line), cancellationToken, Path.Combine(ffmpeg, "ffprobe.exe"));
                    if (double.TryParse(durationText.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds)) processing.Duration = seconds;
                }
                processing.Begin("轉換 " + options.Format.ToUpperInvariant());
                conversion.AddRange(["-progress", "pipe:1", "-stats_period", "0.2"]);
                conversion.Add(temporaryPath);
                await RunAsync(conversion.ToArray(), processing.ReadLine, cancellationToken, Path.Combine(ffmpeg, "ffmpeg.exe"));
                processing.End();
                File.Move(temporaryPath, finalPath, overwrite: true);
                if (!string.Equals(outputPath, finalPath, StringComparison.OrdinalIgnoreCase)) File.Delete(outputPath);
                outputPath = finalPath;
            }
            finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
        }
        return outputPath;
    }

    public static string? FindFfmpegDirectory()
    {
        var directories = new[] { FfmpegSetupService.InstallDirectory, Path.Combine(AppContext.BaseDirectory, "Tools") }
            .Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator));
        return directories.FirstOrDefault(directory => File.Exists(Path.Combine(directory, "ffmpeg.exe")) &&
            File.Exists(Path.Combine(directory, "ffprobe.exe")));
    }

    private async Task RunAsync(string[] arguments, Action<string> onOutput, CancellationToken cancellationToken, string? executable = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsAvailable) throw new FileNotFoundException("找不到下載核心，請將 yt-dlp.exe 放入程式的 Tools 資料夾");
        var start = new ProcessStartInfo(executable ?? ExecutablePath)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in executable is not null ? Array.Empty<string>() : new[] { "--ignore-config", "--no-playlist", "--encoding", "utf-8", "--socket-timeout", "30" })
            start.ArgumentList.Add(argument);
        var plugins = Path.Combine(AppContext.BaseDirectory, "Tools", "yt-dlp-plugins");
        if (executable is null && Directory.Exists(plugins))
        {
            start.ArgumentList.Add("--plugin-dirs");
            start.ArgumentList.Add(plugins);
        }
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        process.Start();
        using var registration = cancellationToken.Register(() => Kill(process));
        var errors = process.StandardError.ReadToEndAsync();
        try
        {
            while (await process.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
                onOutput(line);
            await process.WaitForExitAsync(cancellationToken);
            var error = await errors;
            cancellationToken.ThrowIfCancellationRequested();
            if (process.ExitCode != 0)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? $"下載核心失敗（{process.ExitCode}）" : error.Trim());
        }
        finally
        {
            Kill(process);
            await process.WaitForExitAsync();
            await errors;
        }
    }

    private static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private static string? Text(JsonElement root, string key) =>
        root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static double? Number(JsonElement root, string key) =>
        root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)
            ? number : null;
}
