using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace VideoDownloader.Services;

public static class AboutInfoService
{
    private static readonly Assembly AppAssembly = typeof(AboutInfoService).Assembly;
    public static string Version => VersionOf(AppAssembly);
    public static string Author => Metadata("Author");
    public static string ReleaseStatus => Metadata("ReleaseStatus");
    public static string ReleaseDate => Metadata("ReleaseDate");
    public static string Copyright => AppAssembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "";
    public static string Framework => RuntimeInformation.FrameworkDescription;
    public static string Platform => $"{(OperatingSystem.IsWindows() ? "Windows" : RuntimeInformation.OSDescription)} {RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}";

    private static string Metadata(string key) => AppAssembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == key)?.Value ?? "未設定";

    public static string VersionOf(Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? assembly.GetName().Version?.ToString() ?? "未知";

    public static async Task<string> ReadToolVersionAsync(string? executable, string argument, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (executable is null || !File.Exists(executable)) return "尚未準備";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                ArgumentList = { argument }
            }
        };
        try
        {
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
                var text = (await output).Trim();
                await error;
                if (process.ExitCode != 0 || text.Length == 0) return "無法取得版本";
                var firstLine = text.Split('\n')[0].Trim();
                const string prefix = "ffmpeg version ";
                return firstLine.StartsWith(prefix, StringComparison.Ordinal)
                    ? firstLine[prefix.Length..].Split(' ')[0] : firstLine;
            }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                try { await Task.WhenAll(output, error); } catch (OperationCanceledException) { }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return "讀取逾時"; }
        catch (Exception) { return "無法取得版本"; }
    }
}
