using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VideoDownloader.Services;

static class FfmpegTests
{
    public static async Task RunAsync(string root, string binaries)
    {
        var zip = Path.Combine(root, "ffmpeg.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            foreach (var name in new[] { "ffmpeg.exe", "ffprobe.exe" })
                archive.CreateEntryFromFile(Path.Combine(binaries, name), "test/bin/" + name, CompressionLevel.Fastest);
            var license = archive.CreateEntry("test/LICENSE");
            using var writer = new StreamWriter(license.Open()); writer.Write("fixture license");
        }
        var handler = new ArchiveHandler(zip);
        using var client = new HttpClient(handler);
        var target = Path.Combine(root, "tools", "ffmpeg");
        var setup = new FfmpegSetupService(client, target);
        await setup.EnsureAsync(null, CancellationToken.None);
        if (!File.Exists(Path.Combine(target, "ffprobe.exe")) || !File.Exists(Path.Combine(target, "LICENSE"))) throw new Exception("FFmpeg install incomplete");
        handler.Fail = true;
        await setup.EnsureAsync(null, CancellationToken.None);
        handler.Fail = false;
        handler.BadHash = true;
        var badTarget = Path.Combine(root, "bad-tools", "ffmpeg");
        try { await new FfmpegSetupService(client, badTarget).EnsureAsync(null, CancellationToken.None); throw new Exception("Accepted bad checksum"); }
        catch (InvalidDataException) { }
        if (Directory.Exists(badTarget) || Directory.GetDirectories(Path.GetDirectoryName(badTarget)!).Length != 0) throw new Exception("Incomplete tools left installed");
        Console.WriteLine("PASS: FFmpeg verified install, existing offline reuse, checksum rejection and cleanup");
    }
    sealed class ArchiveHandler(string zip) : HttpMessageHandler
    {
        public bool Fail, BadHash;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (Fail) throw new HttpRequestException("Offline fixture");
            HttpContent content;
            if (request.RequestUri!.AbsolutePath.EndsWith("latest"))
            {
                await using var stream = File.OpenRead(zip);
                var digest = BadHash ? new string('0', 64) : Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
                content = new StringContent(JsonSerializer.Serialize(new { assets = new[] { new {
                    name = "ffmpeg-test-essentials_build.zip", digest = "sha256:" + digest,
                    browser_download_url = "https://github.com/GyanD/codexffmpeg/releases/download/test/ffmpeg-test-essentials_build.zip"
                } } }));
            }
            else content = new StreamContent(File.OpenRead(zip));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }
    }
}
