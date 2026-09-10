using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using VideoDownloader;
using VideoDownloader.Services;
using VideoDownloader.Views;

static void Check(bool condition, string description)
{
    if (!condition) throw new Exception(description);
    Console.WriteLine("PASS: " + description);
}

Check(AboutInfoService.Author == "Bruce Yang", "author metadata comes from the application assembly");
Check(AboutInfoService.Version == typeof(App).Assembly.GetName().Version!.ToString(3), "display version matches application version");
Check(AboutInfoService.ReadToolVersionAsync(null, "--version", CancellationToken.None).GetAwaiter().GetResult() == "尚未準備", "missing tools have an explicit state");
using (var canceled = new CancellationTokenSource())
{
    canceled.Cancel();
    try
    {
        AboutInfoService.ReadToolVersionAsync(null, "--version", canceled.Token).GetAwaiter().GetResult();
        throw new Exception("Cancellation was ignored");
    }
    catch (OperationCanceledException) { Console.WriteLine("PASS: version lookup observes cancellation"); }
}
if (args.Length > 0)
{
    var version = AboutInfoService.ReadToolVersionAsync(Path.GetFullPath(args[0]), "--version", CancellationToken.None).GetAwaiter().GetResult();
    Check(DateTime.TryParseExact(version, "yyyy.MM.dd", null, System.Globalization.DateTimeStyles.None, out _), "real yt-dlp version: " + version);
}
var ffmpeg = YtDlpService.FindFfmpegDirectory();
if (ffmpeg is not null)
{
    var version = AboutInfoService.ReadToolVersionAsync(Path.Combine(ffmpeg, "ffmpeg.exe"), "-version", CancellationToken.None).GetAwaiter().GetResult();
    Check(version.Length > 0 && char.IsAsciiDigit(version.TrimStart('n', 'N')[0]), "real FFmpeg version: " + version);
}

AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
var calls = 0;
var window = new AboutWindow(() => { calls++; return Task.FromResult("測試工具檢查完成"); });
T Control<T>(string name) where T : Control => window.FindControl<T>(name) ?? throw new Exception(name);
void PumpUntil(Func<bool> ready)
{
    var watch = Stopwatch.StartNew();
    while (!ready())
    {
        Dispatcher.UIThread.RunJobs();
        if (watch.Elapsed > TimeSpan.FromSeconds(15)) throw new Exception("UI operation timed out");
        Thread.Sleep(10);
    }
}
window.Show();
PumpUntil(() => Control<Button>("CopyInfoButton").IsEnabled);
Check(Control<TextBlock>("AuthorText").Text == "Bruce Yang", "author appears in About");
Check(Control<TextBlock>("AvaloniaText").Text!.StartsWith("12.1.2"), "Avalonia runtime version appears in About");
foreach (var size in new[] { new Size(460, 520), new Size(420, 400) })
{
    window.Width = size.Width;
    window.Height = size.Height;
    window.UpdateLayout();
    Dispatcher.UIThread.RunJobs();
    var copy = Control<Button>("CopyInfoButton");
    var point = copy.TranslatePoint(new Point(0, 0), window)!.Value;
    Check(point.Y >= 0 && point.Y + copy.Bounds.Height <= window.Bounds.Height && copy.Bounds.Width > 0,
        "actions remain visible at " + size);
}
Control<Button>("CopyInfoButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
PumpUntil(() => Control<TextBlock>("StatusText").Text == "已複製系統資訊。");
var clipboard = window.Clipboard!.TryGetTextAsync();
PumpUntil(() => clipboard.IsCompleted);
var copied = clipboard.GetAwaiter().GetResult()!;
Check(copied.Contains("Bruce Yang") && copied.Contains("yt-dlp：") && copied.Contains("FFmpeg：") && copied.Contains(AboutInfoService.Version), "clipboard includes product and actual tool information");
Check(copied.Contains("FFmpeg：" + Control<TextBlock>("FfmpegText").Text), "compact display preserves the full FFmpeg version in clipboard");
Control<Button>("CheckUpdatesButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
PumpUntil(() => Control<Button>("CheckUpdatesButton").IsEnabled);
Check(calls == 1 && Control<TextBlock>("StatusText").Text == "測試工具檢查完成", "update action invokes the supplied workflow and refreshes versions");
window.Close();
Console.WriteLine("PASS: About dialog closed");
