using System;
using System.Linq;
using System.Text.Json;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VideoDownloader.Services;

CarouselTests.Run();
AudioDetectionTests.Run();
await QueueTests.RunAsync();
var ytDlp = Path.GetFullPath(args[0]);
var ffmpegDirectory = Path.GetFullPath(args[1]);
Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "Tools"));
File.Copy(ytDlp, Path.Combine(AppContext.BaseDirectory, "Tools", "yt-dlp.exe"), true);
Environment.SetEnvironmentVariable("PATH", ffmpegDirectory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"));
var testRoot = Path.Combine(Path.GetTempPath(), "VideoDownloader.WorkflowSmoke", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testRoot);
var fixture = Path.Combine(testRoot, "fixture.mp4");
using (var generator = Process.Start(new ProcessStartInfo(Path.Combine(ffmpegDirectory, "ffmpeg.exe"))
{
    UseShellExecute = false, CreateNoWindow = true,
    ArgumentList = { "-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "color=c=blue:s=1280x720:r=24:d=2",
        "-f", "lavfi", "-i", "sine=frequency=440:duration=2", "-c:v", "libx264", "-c:a", "aac", "-shortest", fixture }
})!) { await generator.WaitForExitAsync(); if (generator.ExitCode != 0) throw new Exception("Fixture generation failed"); }
var manifest = Path.Combine(testRoot, "manifest.mpd");
using (var generator = Process.Start(new ProcessStartInfo(Path.Combine(ffmpegDirectory, "ffmpeg.exe"))
{
    UseShellExecute = false, CreateNoWindow = true,
    ArgumentList = { "-hide_banner", "-loglevel", "error", "-i", fixture, "-map", "0", "-c", "copy", "-f", "dash", manifest }
})!) { await generator.WaitForExitAsync(); if (generator.ExitCode != 0) throw new Exception("DASH fixture generation failed"); }
var state = new UserDataService(Path.Combine(testRoot, "state"));
var settings = new UserSettings(testRoot, "wav", "192K", "s24-48000");
state.Save("settings.json", settings);
if (state.Load("settings.json", new UserSettings("bad")) != settings) throw new Exception("Settings roundtrip failed");
var record = new DownloadRecord("測試影片", "YouTube", "WAV", DateTimeOffset.Now, fixture, testRoot, "成功", null);
state.Save("history.json", new[] { record });
if (state.Load("history.json", Array.Empty<DownloadRecord>()).Single() != record) throw new Exception("History roundtrip failed");
if (!Path.IsPathFullyQualified(UserDataService.DefaultDownloadDirectory())) throw new Exception("Invalid default directory");
await FfmpegTests.RunAsync(testRoot, ffmpegDirectory);
var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
var port = ((IPEndPoint)listener.LocalEndpoint).Port;
using var lifetime = new CancellationTokenSource();
var data = File.ReadAllBytes(fixture);
var server = Task.Run(async () =>
{
    while (!lifetime.IsCancellationRequested)
    {
        using var client = await listener.AcceptTcpClientAsync(lifetime.Token);
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, leaveOpen: true);
        var first = await reader.ReadLineAsync();
        while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }
        var name = Path.GetFileName((first ?? "GET /fixture.mp4 HTTP/1.1").Split(' ')[1]);
        var responseData = File.ReadAllBytes(Path.Combine(testRoot, name));
        var contentType = name.EndsWith(".mpd") ? "application/dash+xml" : "video/mp4";
        var header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: {contentType}\r\nContent-Length: {responseData.Length}\r\nConnection: close\r\n\r\n");
        try
        {
            await stream.WriteAsync(header);
            if (first?.StartsWith("HEAD ") != true) await stream.WriteAsync(responseData);
        }
        catch (IOException) { }
    }
});
try
{
    var service = new YtDlpService();
    var url = $"http://127.0.0.1:{port}/fixture.mp4";
    var info = await service.GetInfoAsync(url, CancellationToken.None);
    if (string.IsNullOrEmpty(info.Title)) throw new Exception("No title");
    var progress = new Capture();
    var directory = Path.Combine(testRoot, "downloads");
    using var metadata = JsonDocument.Parse("""
    {"formats":[
    {"format_id":"low","vcodec":"h264","acodec":"aac","width":640,"height":360},
    {"format_id":"mid","vcodec":"h264","acodec":"none","width":1280,"height":720},
    {"format_id":"high","vcodec":"vp9","acodec":"none","width":3840,"height":2160},
    {"format_id":"portrait","vcodec":"h264","acodec":"none","width":720,"height":1280},
    {"format_id":"drm","vcodec":"h264","width":7680,"height":4320,"has_drm":true},
    {"format_id":"audio","vcodec":"none","acodec":"aac"}]}
    """);
    var qualities = MediaOptions.GetVideoQualities(metadata.RootElement);
    if (!qualities.Select(q => q.Resolution).SequenceEqual(new int?[] {2160,720,360})) throw new Exception("Wrong quality list");
    if (!MediaOptions.HasAudio(metadata.RootElement)) throw new Exception("Missing audio");
    using var low = JsonDocument.Parse("""{"formats":[{"format_id":"720","vcodec":"h264","width":1280,"height":720}]}""");
    if (MediaOptions.GetVideoQualities(low.RootElement).Single().Resolution != 720) throw new Exception("Invented 4K");
    using var direct = JsonDocument.Parse("""{"formats":[{"format_id":"mp4","vcodec":"h264","acodec":"aac","width":1280,"height":720}]}""");
    var selected = MediaOptions.GetVideoQualities(direct.RootElement).Single();
    foreach (var format in new[] {"mp4", "mkv", "mp3", "wav"})
    {
        var options = new DownloadOptions(format, selected, new AudioQuality("test", format == "wav" ? "s24-48000" : "128K"));
        var path = await service.DownloadAsync(url, directory, progress, CancellationToken.None, options);
        var start = new ProcessStartInfo(Path.Combine(ffmpegDirectory, "ffprobe.exe")) { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] {"-v","error","-show_streams","-of","json",path}) start.ArgumentList.Add(arg);
        using var probe = Process.Start(start)!;
        var json = await probe.StandardOutput.ReadToEndAsync();
        await probe.WaitForExitAsync();
        using var doc = JsonDocument.Parse(json);
        var streams = doc.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        bool video = streams.Any(st => st.GetProperty("codec_type").GetString() == "video");
        if (video == options.AudioOnly) throw new Exception("Incorrect streams " + format);
        if (!streams.Any(st => st.GetProperty("codec_type").GetString() == "audio")) throw new Exception("Missing audio " + format);
        if (video && streams.First(st => st.GetProperty("codec_type").GetString() == "video").GetProperty("height").GetInt32() != 720) throw new Exception("Wrong resolution");
        if (format == "mp3")
        {
            var audio = streams.First(st => st.GetProperty("codec_type").GetString() == "audio");
            var bitrate = int.Parse(audio.GetProperty("bit_rate").GetString()!);
            if (bitrate < 100000 || bitrate > 145000) throw new Exception("Incorrect bitrate: " + bitrate);
        }
        if (format == "wav")
        {
            var audio = streams.First(st => st.GetProperty("codec_type").GetString() == "audio");
            if (audio.GetProperty("codec_name").GetString() != "pcm_s24le" || audio.GetProperty("sample_rate").GetString() != "48000")
                throw new Exception("Incorrect WAV quality");
        }
        if (options.AudioOnly && !progress.SawConversionPercent) throw new Exception("Missing real FFmpeg conversion progress");
        Console.WriteLine("PASS: " + format);
    }
    var dashUrl = $"http://127.0.0.1:{port}/manifest.mpd";
    var dashInfo = await service.GetInfoAsync(dashUrl, CancellationToken.None);
    var merged = await service.DownloadAsync(dashUrl, directory, progress, CancellationToken.None,
        new DownloadOptions("mp4", dashInfo.Qualities.First(), null));
    if (!File.Exists(merged) || !progress.SawMerge) throw new Exception("Missing real merger stage");
    Console.WriteLine("PASS: real separate DASH video/audio merge and FFmpeg stage");
    var duplicate1 = await service.DownloadAsync(url, directory, progress, CancellationToken.None, new DownloadOptions("mp4", selected, null));
    var duplicate2 = await service.DownloadAsync(url, directory, progress, CancellationToken.None, new DownloadOptions("mp4", selected, null));
    if (duplicate1 == duplicate2 || !File.Exists(duplicate1)) throw new Exception("Duplicate overwrote previous file");
    using var cancelActive = new CancellationTokenSource();
    var cancelProgress = new CancelProgress(cancelActive);
    try { await service.DownloadAsync(url, directory, cancelProgress, cancelActive.Token, new DownloadOptions("mp4", selected, null)); throw new Exception("Active cancellation failed"); }
    catch (OperationCanceledException) { }
    if (Directory.GetDirectories(directory, ".videodownloader-*").Length != 0) throw new Exception("Staging directory leaked");
    if (!File.Exists(duplicate1)) throw new Exception("Cleanup removed completed file");
    Console.WriteLine("PASS: settings/history, duplicate preservation, active cancellation and staging cleanup");
    if (progress.Count == 0 || progress.Last?.Percent != 100) throw new Exception("No final progress");
    try { await service.DownloadAsync("file:///bad", directory, progress, CancellationToken.None); throw new Exception("Accepted invalid URL"); }
    catch (ArgumentException) { }
    using var canceled = new CancellationTokenSource();
    canceled.Cancel();
    try { await service.GetInfoAsync(url, canceled.Token); throw new Exception("Ignored cancellation"); }
    catch (OperationCanceledException) { }
    Console.WriteLine($"PASS: metadata, real yt-dlp download, format selection, audio extraction, {progress.Count} progress events, invalid URL, cancellation");
}
finally
{
    lifetime.Cancel();
    listener.Stop();
    try { await server; } catch (OperationCanceledException) { }
}
sealed class Capture : IProgress<DownloadProgress>
{
    public int Count;
    public bool SawConversionPercent;
    public bool SawMerge;
    public DownloadProgress? Last;
    public void Report(DownloadProgress value) { Count++; Last = value; if (value.Stage == "合併音視訊") SawMerge = true; if (value.Stage.StartsWith("轉換") && value.Percent is > 0 and < 100) SawConversionPercent = true; }
}

sealed class CancelProgress(CancellationTokenSource source) : IProgress<DownloadProgress>
{
    public void Report(DownloadProgress value) => source.Cancel();
}
