using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VideoDownloader.Services;

// Run with a real yt-dlp executable. HTTP responses are deterministic fixtures.
var source = Path.GetFullPath(args[0]);
var bytes = await File.ReadAllBytesAsync(source);
using var versionProcess = Process.Start(new ProcessStartInfo(source, "--version")
{ UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
var version = (await versionProcess.StandardOutput.ReadToEndAsync()).Trim();
await versionProcess.WaitForExitAsync();
var root = Path.Combine(Path.GetTempPath(), "VideoDownloader.UpdateTests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var path = Path.Combine(root, "yt-dlp.exe");
var handler = new ReleaseHandler(bytes, version);
using var client = new HttpClient(handler);
var updater = new YtDlpUpdateService(path, client);
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

var result = await updater.EnsureLatestAsync();
Check(result.IsReady && result.Updated && File.Exists(path), "First install failed");
var downloads = handler.Downloads;
result = await updater.EnsureLatestAsync();
Check(result.IsReady && !result.Updated && handler.Downloads == downloads, "Same version downloaded again");
handler.Version = "2000.01.01";
result = await updater.EnsureLatestAsync();
Check(result.IsReady && !result.Updated && handler.Downloads == downloads, "Downgraded a newer version");
handler.Offline = true;
result = await updater.EnsureLatestAsync();
Check(result.IsReady && result.Warning != null, "Offline fallback failed");
result = await new YtDlpUpdateService(Path.Combine(root, "missing.exe"), client).EnsureLatestAsync();
Check(!result.IsReady, "Missing executable incorrectly ready offline");
handler.Offline = false;
handler.Version = "2099.01.01";
handler.BadHash = true;
result = await updater.EnsureLatestAsync();
Check(result.IsReady && result.Warning != null && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes), "Checksum failure lost old version");
handler.BadHash = false;
result = await updater.EnsureLatestAsync();
Check(result.IsReady && result.Warning != null && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes), "Version mismatch lost old version");
handler.Version = version;
var corrupt = Path.Combine(root, "corrupt.exe");
await File.WriteAllTextAsync(corrupt, "invalid executable");
result = await new YtDlpUpdateService(corrupt, client).EnsureLatestAsync();
Check(result.IsReady && result.Updated && File.ReadAllText(corrupt + ".bak") == "invalid executable", "Repair/backup failed");
using var canceled = new CancellationTokenSource();
canceled.Cancel();
try { await updater.EnsureLatestAsync(cancellationToken: canceled.Token); throw new Exception("Ignored cancellation"); }
catch (OperationCanceledException) { }
Check(Directory.GetFiles(root, "yt-dlp-*.exe").Length == 0, "Temporary executable leaked");
Console.WriteLine("PASS: first install, same/newer skip, offline fallback, missing offline, checksum failure, version mismatch, atomic repair/backup, cancellation, cleanup");

sealed class ReleaseHandler(byte[] binary, string version) : HttpMessageHandler
{
    public string Version = version;
    public bool Offline, BadHash;
    public int Downloads;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Offline) throw new HttpRequestException("Offline fixture");
        HttpContent content;
        var url = request.RequestUri!.AbsoluteUri;
        if (url.EndsWith("/latest"))
        {
            var prefix = $"https://github.com/yt-dlp/yt-dlp/releases/download/{Version}/";
            content = new StringContent(JsonSerializer.Serialize(new
            {
                tag_name = Version,
                assets = new[] {
                    new { name = "yt-dlp.exe", browser_download_url = prefix + "yt-dlp.exe" },
                    new { name = "SHA2-256SUMS", browser_download_url = prefix + "SHA2-256SUMS" }
                }
            }));
        }
        else if (url.EndsWith("SHA2-256SUMS"))
            content = new StringContent((BadHash ? new string('0', 64) : Convert.ToHexString(SHA256.HashData(binary))) + "  yt-dlp.exe\n");
        else { Downloads++; content = new ByteArrayContent(binary); }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
    }
}
