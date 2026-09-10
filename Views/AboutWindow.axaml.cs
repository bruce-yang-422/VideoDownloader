using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using VideoDownloader.Services;

namespace VideoDownloader.Views;

public partial class AboutWindow : Window
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Func<Task<string>>? _checkUpdates;
    private bool _busy;

    public AboutWindow() : this(null) { }

    public AboutWindow(Func<Task<string>>? checkUpdates)
    {
        InitializeComponent();
        _checkUpdates = checkUpdates;
        VersionText.Text = $"Version {AboutInfoService.Version}";
        ReleaseStatusText.Text = AboutInfoService.ReleaseStatus;
        AuthorText.Text = AboutInfoService.Author;
        ReleaseDateText.Text = AboutInfoService.ReleaseDate;
        CopyrightText.Text = AboutInfoService.Copyright;
        PlatformText.Text = AboutInfoService.Platform;
        FrameworkText.Text = AboutInfoService.Framework.Replace(".NET ", "", StringComparison.Ordinal);
        AvaloniaText.Text = AboutInfoService.VersionOf(typeof(Window).Assembly);
        ToolTip.SetTip(FrameworkText, AboutInfoService.Framework);
        ToolTip.SetTip(AvaloniaText, AvaloniaText.Text);
        CheckUpdatesButton.IsEnabled = false;
        Opened += async (_, _) => await RefreshVersionsAsync();
        Closed += (_, _) => _lifetime.Cancel();
    }

    private async Task RefreshVersionsAsync()
    {
        CopyInfoButton.IsEnabled = false;
        try
        {
            var directory = YtDlpService.FindFfmpegDirectory();
            var versions = await Task.WhenAll(
                AboutInfoService.ReadToolVersionAsync(new YtDlpService().ExecutablePath, "--version", _lifetime.Token),
                AboutInfoService.ReadToolVersionAsync(directory is null ? null : Path.Combine(directory, "ffmpeg.exe"), "-version", _lifetime.Token));
            _lifetime.Token.ThrowIfCancellationRequested();
            YtDlpText.Text = versions[0];
            FfmpegText.Text = versions[1];
            ToolTip.SetTip(YtDlpText, versions[0]);
            ToolTip.SetTip(FfmpegText, versions[1]);
            CopyInfoButton.IsEnabled = true;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception)
        {
            if (_lifetime.IsCancellationRequested) return;
            YtDlpText.Text = FfmpegText.Text = "無法取得版本";
            ToolTip.SetTip(YtDlpText, YtDlpText.Text);
            ToolTip.SetTip(FfmpegText, FfmpegText.Text);
            StatusText.Text = "無法讀取工具版本，可稍後重新開啟關於視窗。";
            CopyInfoButton.IsEnabled = true;
        }
        finally
        {
            if (!_lifetime.IsCancellationRequested)
                CheckUpdatesButton.IsEnabled = !_busy && _checkUpdates is not null;
        }
    }

    private async void CheckUpdates_Click(object? sender, RoutedEventArgs e)
    {
        if (_busy || _checkUpdates is null) return;
        _busy = true;
        CheckUpdatesButton.IsEnabled = false;
        CopyInfoButton.IsEnabled = false;
        StatusText.Text = "正在檢查下載工具，首次準備可能需要數分鐘…";
        try
        {
            var result = await _checkUpdates();
            if (_lifetime.IsCancellationRequested) return;
            StatusText.Text = result;
            await RefreshVersionsAsync();
        }
        catch (Exception)
        {
            if (!_lifetime.IsCancellationRequested) StatusText.Text = "檢查未完成，請查看主視窗的工具狀態後重試。";
        }
        finally
        {
            _busy = false;
            if (!_lifetime.IsCancellationRequested)
            {
                CheckUpdatesButton.IsEnabled = true;
                CopyInfoButton.IsEnabled = true;
            }
        }
    }

    private async void CopyInfo_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (Clipboard is null) { StatusText.Text = "目前無法使用剪貼簿。"; return; }
            await Clipboard.SetTextAsync($"VideoDownloader\n{VersionText.Text}\n狀態：{ReleaseStatusText.Text}\n作者：{AuthorText.Text}\n{CopyrightText.Text}\n更新日期：{ReleaseDateText.Text}\n平台：{PlatformText.Text}\n作業系統：{System.Runtime.InteropServices.RuntimeInformation.OSDescription}\n.NET：{FrameworkText.Text}\nAvalonia：{AvaloniaText.Text}\nyt-dlp：{YtDlpText.Text}\nFFmpeg：{FfmpegText.Text}\n內附 Threads 解析器隨主程式更新。");
            if (!_lifetime.IsCancellationRequested) StatusText.Text = "已複製系統資訊。";
        }
        catch (Exception) { if (!_lifetime.IsCancellationRequested) StatusText.Text = "複製失敗，請再試一次。"; }
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
