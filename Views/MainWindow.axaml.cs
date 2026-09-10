using System;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Input;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using VideoDownloader.Services;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace VideoDownloader.Views;

public partial class MainWindow : Window
{
    private readonly YtDlpService _downloader = new();
    private readonly CancellationTokenSource _lifetime = new();
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private CancellationTokenSource? _infoRequest;
    private Bitmap? _thumbnail;
    private readonly UserDataService _userData = new();
    private UserSettings _settings = new(UserDataService.DefaultDownloadDirectory());
    private readonly ObservableCollection<DownloadRecord> _history = new();
    private CancellationTokenSource? _downloadRequest;
    private bool _updatingQuality;
    private readonly DownloadQueue _queue = new();
    private bool _preparingTools;
    private bool _downloading;
    private bool _coreReady;
    private bool _checkingCore = true;
    private VideoInfo? _videoInfo;
    private string? _infoUrl;
    private string? _lastCompletedPath;

    public MainWindow()
    {
        InitializeComponent();
        LoadUserData();
        QueueListBox.ItemsSource = _queue.Items;
        _queue.Items.CollectionChanged += (_, _) =>
        {
            QueuePanel.IsVisible = _queue.Items.Count > 0;
            QueueCountText.Text = $"下載佇列 · {_queue.Items.Count}";
        };
        HistoryListBox.ItemsSource = _history;
        HistoryListBox.SelectedItem = _history.FirstOrDefault();
        UpdateHistoryControls();
        SavePathTextBox.LostFocus += (_, _) => SaveSettings();
        QualityComboBox.SelectionChanged += (_, _) => { if (!_updatingQuality) SaveSettings(); };
        // Subscribe after all named controls have been initialized.
        UrlTextBox.TextChanged += UrlTextBox_TextChanged;
        DetectPlatform();
        FormatComboBox.SelectionChanged += (_, _) => { UpdateQualityOptions(); SaveSettings(); };
        UpdateQualityOptions();
        SetCoreStatus("正在檢查下載核心…", "#A16207");
        Opened += InitializeCore;
        Closed += (_, _) =>
        {
            SaveSettings();
            _lifetime.Cancel();
            _infoRequest?.Cancel();
            _thumbnail?.Dispose();
        };
    }

    private async void InitializeCore(object? sender, EventArgs e)
    {
        Opened -= InitializeCore;
        await PrepareToolsAsync();
    }

    private async void RetryTools_Click(object? sender, RoutedEventArgs e) => await PrepareToolsAsync();

    private async void About_Click(object? sender, RoutedEventArgs e)
    {
        var about = new AboutWindow(async () =>
        {
            if (_downloading) return "下載進行中，請等佇列處理完成後再檢查工具更新。";
            if (_preparingTools) return "主視窗正在準備工具，請稍後再檢查。";
            await PrepareToolsAsync();
            return CoreStatusTextBlock.Text ?? "工具檢查完成。";
        });
        await about.ShowDialog(this);
    }

    private async Task PrepareToolsAsync()
    {
        if (_preparingTools || _downloading || _lifetime.IsCancellationRequested) return;
        _preparingTools = true;
        _checkingCore = true;
        _coreReady = false;
        _infoRequest?.Cancel();
        RetryToolsButton.IsEnabled = false;
        UpdateQualityOptions();
        try
        {
            var progress = new Progress<string>(message =>
            {
                if (_checkingCore && !_lifetime.IsCancellationRequested) SetCoreStatus(message, "#A16207");
            });
            var result = await new YtDlpUpdateService(_downloader.ExecutablePath)
                .EnsureLatestAsync(progress, _lifetime.Token);
            if (_lifetime.IsCancellationRequested) return;
            if (result.IsReady) await new FfmpegSetupService().EnsureAsync(progress, _lifetime.Token);
            _checkingCore = false;
            _coreReady = result.IsReady;
            RetryToolsButton.IsVisible = !result.IsReady || result.Warning is not null;
            var message = result.IsReady
                ? result.Warning is null ? $"下載核心已就緒 · {result.Version}" : "更新失敗，使用現有核心"
                : "下載核心無法更新";
            SetCoreStatus(message, result.Warning is null ? "#047857" : result.IsReady ? "#A16207" : "#B91C1C");
            ToolTip.SetTip(CoreStatusBorder, result.Warning ?? $"yt-dlp {result.Version}");
            UpdateQualityOptions();
            if (_coreReady) RefreshVideoInfo();
            else DownloadStatusTextBlock.Text = $"無法準備下載核心：{result.Warning}。請檢查網路與 Tools 資料夾權限後按「重試」。";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _checkingCore = false;
            _coreReady = false;
            SetCoreStatus("工具準備失敗，請按重試", "#B91C1C");
            ToolTip.SetTip(CoreStatusBorder, ex.Message);
            DownloadStatusTextBlock.Text = ex.Message;
            RetryToolsButton.IsVisible = true;
        }
        finally
        {
            _preparingTools = false;
            _checkingCore = false;
            RetryToolsButton.IsEnabled = !_downloading;
            UpdateQualityOptions();
        }
    }

    private void SetCoreStatus(string message, string color)
    {
        var brush = Brush.Parse(color);
        CoreStatusTextBlock.Text = message;
        CoreStatusTextBlock.Foreground = brush;
        CoreStatusIndicator.Fill = brush;
        CoreStatusBorder.BorderBrush = brush;
        CoreStatusBorder.Background = Brush.Parse(color == "#047857" ? "#ECFDF5" : color == "#A16207" ? "#FFFBEB" : "#FEF2F2");
    }

    private async void PasteButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var clipboard = Clipboard;
            if (clipboard is null)
            {
                ShowPlatform("無法讀取剪貼簿", "目前系統不支援剪貼簿功能");
                return;
            }

            var text = await clipboard.TryGetTextAsync();
            if (string.IsNullOrWhiteSpace(text))
            {
                ShowPlatform("剪貼簿沒有文字", "請先複製影片網址，再按貼上");
                return;
            }

            UrlTextBox.Text = text.Trim();
            DetectPlatform();
        }
        catch (Exception ex)
        {
            ShowPlatform("剪貼簿讀取失敗", ex.Message);
        }
    }

    private void UrlTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        DetectPlatform();
        RefreshVideoInfo();
    }

    private async void RefreshVideoInfo()
    {
        _infoRequest?.Cancel();
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _infoRequest = request;
        var token = request.Token;
        var url = UrlTextBox.Text?.Trim() ?? string.Empty;
        _videoInfo = null;
        _infoUrl = null;
        UpdateQualityOptions();
        ThumbnailImage.Source = null;
        _thumbnail?.Dispose();
        _thumbnail = null;
        ThumbnailPlaceholder.IsVisible = true;
        VideoTitleTextBlock.Text = "等待影片網址";
        VideoDetailsTextBlock.Text = "取得影片資訊後，作者與時長將顯示於此。";
        try
        {
            if (!_coreReady || !YtDlpService.IsValidUrl(url)) return;
            VideoTitleTextBlock.Text = "正在取得影片資訊…";
            await Task.Delay(650, token);
            var info = await _downloader.GetInfoAsync(url, token);
            token.ThrowIfCancellationRequested();
            _videoInfo = info;
            _infoUrl = url;
            UpdateQualityOptions();
            VideoTitleTextBlock.Text = info.Title;
            var duration = info.Duration is >= 0 ? TimeSpan.FromSeconds(info.Duration.Value).ToString(@"hh\:mm\:ss") : "未知時長";
            VideoDetailsTextBlock.Text = $"{info.Author} · {duration}";
            if (info.PlaylistIndex is { } index)
                VideoDetailsTextBlock.Text += $" · 多項貼文：選取第 {index} 項（第一個可下載影音）";
            if (info.Thumbnail is { } thumbnail && YtDlpService.IsValidUrl(thumbnail))
            {
                try
                {
                    using var stream = await Http.GetStreamAsync(thumbnail, token);
                    using var buffer = new System.IO.MemoryStream();
                    await stream.CopyToAsync(buffer, token);
                    token.ThrowIfCancellationRequested();
                    buffer.Position = 0;
                    _thumbnail = Bitmap.DecodeToWidth(buffer, 600);
                    ThumbnailImage.Source = _thumbnail;
                    ThumbnailPlaceholder.IsVisible = false;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                catch (Exception) { if (!token.IsCancellationRequested) VideoDetailsTextBlock.Text += " · 縮圖載入失敗"; }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested)
            {
                VideoTitleTextBlock.Text = "無法取得影片資訊";
                VideoDetailsTextBlock.Text = ex.Message;
            }
        }
        finally
        {
            if (ReferenceEquals(_infoRequest, request)) _infoRequest = null;
        }
    }

    private void DownloadButton_Click(object? sender, RoutedEventArgs e)
    {
        if (!_coreReady) return;
        SetCompletion(null);
        var url = UrlTextBox.Text?.Trim() ?? string.Empty;
        if (!YtDlpService.IsValidUrl(url))
        {
            DownloadStatusTextBlock.Text = "請先輸入有效的影片網址";
            return;
        }
        var format = (FormatComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "mp4";
        if (_videoInfo is null || _infoUrl != url || QualityComboBox.SelectedItem is null)
        {
            DownloadStatusTextBlock.Text = "請等待影片資訊載入，並選擇可用的品質";
            return;
        }
        var options = new DownloadOptions(format, QualityComboBox.SelectedItem as VideoQuality,
            QualityComboBox.SelectedItem as AudioQuality, _videoInfo.PlaylistIndex);
        var directory = SavePathTextBox.Text?.Trim() ?? "";
        if (!Path.IsPathFullyQualified(directory))
        {
            DownloadStatusTextBlock.Text = "請選擇完整的儲存路徑";
            return;
        }
        if (_queue.Items.Any(j => j.Url == url && j.Options == options && j.Directory == directory))
        {
            DownloadStatusTextBlock.Text = "相同設定的網址已在佇列中";
            return;
        }
        SaveSettings();
        _queue.Add(new DownloadJob(url, _videoInfo.Title, PlatformTextBlock.Text ?? "其他網站",
            format.ToUpperInvariant() + " · " + QualityComboBox.SelectedItem, directory, options));
        _queue.Start(ExecuteJobAsync, _lifetime.Token);
    }

    private async Task ExecuteJobAsync(DownloadJob job)
    {
        var url = job.Url;
        var title = job.Title;
        var platform = job.Platform;
        var quality = job.Quality;
        var directory = job.Directory;
        var options = job.Options;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _downloadRequest = request;
        SetCompletion(null);
        string? completedPath = null;
        string status = "失敗";
        string? error = null;
        SaveSettings();
        _downloading = true;
        RetryToolsButton.IsEnabled = false;
        DownloadButton.Content = "加入下載佇列";
        CancelDownloadButton.IsVisible = true;
        CancelDownloadButton.IsEnabled = true;
        DownloadProgressBar.Value = 0;
        DownloadProgressBar.IsIndeterminate = true;
        PercentTextBlock.Text = "--%";
        SpeedTextBlock.Text = "-- MB/s";
        DownloadedTextBlock.Text = "-- MB";
        EtaTextBlock.Text = "--:--";
        DownloadStatusTextBlock.Text = "正在準備下載…";
        try
        {
            var progress = new Progress<DownloadProgress>(value =>
            {
                if (!_downloading || request.IsCancellationRequested || !ReferenceEquals(_downloadRequest, request)) return;
                job.Status = value.Stage + (value.Percent is { } p ? $" {p:F1}%" : "…");
                DownloadStatusTextBlock.Text = $"{title} · {job.Status}";
                DownloadProgressBar.IsIndeterminate = value.Percent is null;
                DownloadProgressBar.Value = value.Percent ?? 0;
                PercentTextBlock.Text = value.Percent is { } percent ? $"{percent:F1}%" : "--%";
                DownloadedTextBlock.Text = value.Stage == "下載串流" ? $"{value.Downloaded / 1048576:F1} MB" : "—";
                SpeedTextBlock.Text = value.Stage == "下載串流" && value.Speed is { } speed ? $"{speed / 1048576:F1} MB/s" : "-- MB/s";
                EtaTextBlock.Text = value.Eta is >= 0 ? TimeSpan.FromSeconds(value.Eta.Value).ToString(@"hh\:mm\:ss") : "--:--";
            });
            var path = await _downloader.DownloadAsync(url, directory, progress, request.Token, options);
            completedPath = path;
            status = "成功";
            DownloadStatusTextBlock.Text = $"下載完成：{path}";
            DownloadProgressBar.Value = 100;
            PercentTextBlock.Text = "100%";
            EtaTextBlock.Text = "00:00";
            SetCompletion(path);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
            status = "已取消";
            DownloadStatusTextBlock.Text = "已取消下載";
            PercentTextBlock.Text = "—";
            SetCompletion(null);
        }
        catch (Exception ex) { error = ex.Message; DownloadStatusTextBlock.Text = $"下載失敗：{ex.Message}"; SetCompletion(null); }
        finally
        {
            _downloadRequest = null;
            AddHistory(new DownloadRecord(title, platform, quality, DateTimeOffset.Now, completedPath, directory, status, error));
            job.Status = status;
            _downloading = false;
            RetryToolsButton.IsEnabled = !_preparingTools;
            DownloadButton.Content = "開始下載／加入佇列";
            DownloadButton.IsVisible = true;
            CancelDownloadButton.IsVisible = false;
            SavePathTextBox.IsReadOnly = false;
            SelectFolderButton.IsEnabled = true;
            FormatComboBox.IsEnabled = true;
            QualityComboBox.IsEnabled = QualityComboBox.ItemCount > 0;
            DownloadButton.IsEnabled = _coreReady && QualityComboBox.ItemCount > 0;
            PasteButton.IsEnabled = true;
            UrlTextBox.IsReadOnly = false;
            DownloadProgressBar.IsIndeterminate = false;
        }
    }

    private void UpdateQualityOptions()
    {
        var format = (FormatComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "mp4";
        var audioOnly = format is "mp3" or "wav";
        QualityLabel.Text = audioOnly ? "音訊品質" : "影片畫質";
        _updatingQuality = true;
        QualityComboBox.ItemsSource = null;
        if (_videoInfo is not null)
        {
            if (audioOnly && _videoInfo.HasAudio)
                QualityComboBox.ItemsSource = format == "wav" ? MediaOptions.WavQualities : MediaOptions.AudioQualities;
            else if (!audioOnly)
                QualityComboBox.ItemsSource = _videoInfo.Qualities;
        }
        QualityComboBox.SelectedIndex = QualityComboBox.ItemCount > 0 ? 0 : -1;
        if (audioOnly)
        {
            var preferred = format == "wav" ? _settings.WavQuality : _settings.Mp3Quality;
            var match = QualityComboBox.Items.OfType<AudioQuality>().FirstOrDefault(q => q.Value == preferred);
            if (match is not null) QualityComboBox.SelectedItem = match;
        }
        _updatingQuality = false;
        QualityComboBox.IsEnabled = QualityComboBox.ItemCount > 0;
        DownloadButton.IsEnabled = _coreReady && QualityComboBox.ItemCount > 0;
        QualityHint.Text = _videoInfo is null ? "貼上網址後，將列出來源可用畫質與音訊選項。"
            : audioOnly ? (!_videoInfo.HasAudio ? "此來源沒有可用音訊。"
                : format == "wav" ? "WAV 為未壓縮音訊；提高位元深度或取樣率不會提升來源音質。"
                : "位元率為轉檔輸出品質；提高位元率不會提升來源音質。")
            : _videoInfo.Qualities.Count == 0 ? "此來源沒有可用影片。"
            : "僅列出來源實際提供的解析度，預設選擇最高畫質。";
    }

    private void LoadUserData()
    {
        try
        {
            _settings = _userData.Load("settings.json", _settings);
            if (!Path.IsPathFullyQualified(_settings.SaveDirectory)) _settings = _settings with { SaveDirectory = UserDataService.DefaultDownloadDirectory() };
        }
        catch (Exception ex) { DownloadStatusTextBlock.Text = $"設定讀取失敗，使用預設值：{ex.Message}"; }
        SavePathTextBox.Text = _settings.SaveDirectory;
        var format = FormatComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag as string == _settings.Format);
        if (format is not null) FormatComboBox.SelectedItem = format;
        try
        {
            foreach (var item in _userData.Load("history.json", new List<DownloadRecord>()).Take(100)) _history.Add(item);
        }
        catch (Exception ex) { DownloadStatusTextBlock.Text = $"下載紀錄讀取失敗：{ex.Message}"; }
    }

    private void SaveSettings()
    {
        var path = SavePathTextBox.Text?.Trim() ?? "";
        if (!Path.IsPathFullyQualified(path)) return;
        var format = (FormatComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "mp4";
        _settings = _settings with { SaveDirectory = path, Format = format };
        if (QualityComboBox.SelectedItem is AudioQuality quality)
            _settings = format == "wav" ? _settings with { WavQuality = quality.Value } : _settings with { Mp3Quality = quality.Value };
        try { _userData.Save("settings.json", _settings); }
        catch (Exception ex) { DownloadStatusTextBlock.Text = $"設定無法保存：{ex.Message}"; }
    }

    private void AddHistory(DownloadRecord record)
    {
        _history.Insert(0, record);
        while (_history.Count > 100) _history.RemoveAt(_history.Count - 1);
        HistoryListBox.SelectedItem = record;
        HistoryListBox.ScrollIntoView(record);
        UpdateHistoryControls();
        try { _userData.Save("history.json", _history.ToList()); }
        catch (Exception ex) { DownloadStatusTextBlock.Text += $"（紀錄無法保存：{ex.Message}）"; }
    }

    private void CancelDownloadButton_Click(object? sender, RoutedEventArgs e)
    {
        CancelDownloadButton.IsEnabled = false;
        DownloadStatusTextBlock.Text = "正在取消並清理暫存檔…";
        _downloadRequest?.Cancel();
    }

    private void SetCompletion(string? path)
    {
        _lastCompletedPath = path;
        DownloadCompletionBanner.IsVisible = !string.IsNullOrWhiteSpace(path);
        if (DownloadCompletionBanner.IsVisible)
            MainContentScrollViewer.Offset = default;
        DownloadCompletionPathTextBlock.Text = path is null ? string.Empty : Path.GetFileName(path);
        OpenCompletedFileButton.IsEnabled = path is not null && File.Exists(path);
        OpenCompletedFolderButton.IsEnabled = path is not null &&
            Directory.Exists(Path.GetDirectoryName(path));
    }

    private void OpenCompletedFile_Click(object? sender, RoutedEventArgs e)
    {
        if (_lastCompletedPath is { } path) OpenLocalPath(path, false);
    }

    private void OpenCompletedFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (_lastCompletedPath is { } path && Path.GetDirectoryName(path) is { } directory)
            OpenLocalPath(directory, true);
    }

    private void RemoveQueued_Click(object? sender, RoutedEventArgs e)
    {
        if (QueueListBox.SelectedItem is not DownloadJob job) return;
        if (!_queue.Remove(job))
        {
            DownloadStatusTextBlock.Text = "這筆正在執行，請使用取消目前下載";
            return;
        }
        AddHistory(new DownloadRecord(job.Title, job.Platform, job.Quality, DateTimeOffset.Now, null, job.Directory, "已取消", "從等待佇列移除"));
    }

    private void HistorySelectionChanged(object? sender, SelectionChangedEventArgs e)
        => UpdateHistoryControls();

    private void UpdateHistoryControls()
    {
        EmptyHistoryText.IsVisible = _history.Count == 0;
        OpenHistoryFileButton.IsEnabled = HistoryListBox.SelectedItem is DownloadRecord { CanOpen: true };
        OpenHistoryFolderButton.IsEnabled = HistoryListBox.SelectedItem is DownloadRecord;
        RemoveHistoryButton.IsEnabled = HistoryListBox.SelectedItem is DownloadRecord;
        ClearHistoryButton.IsEnabled = _history.Count > 0;
        HistoryHintText.IsVisible = _history.Count > 0;
        HistoryHintText.Text = HistoryListBox.SelectedItem is DownloadRecord record
            ? record.CanOpen ? "已選取紀錄，可開啟檔案或資料夾；清除紀錄不會刪除檔案。"
                : "此筆未成功下載，可開啟儲存資料夾或移除紀錄。"
            : "請先點選一筆紀錄，再開啟檔案或資料夾。";
    }

    private void RemoveHistory_Click(object? sender, RoutedEventArgs e)
    {
        if (HistoryListBox.SelectedItem is not DownloadRecord selected) return;
        var index = _history.IndexOf(selected);
        var remaining = _history.ToList();
        remaining.RemoveAt(index);
        try
        {
            _userData.Save("history.json", remaining);
            _history.RemoveAt(index);
            HistoryListBox.SelectedItem = _history.Count == 0 ? null : _history[Math.Min(index, _history.Count - 1)];
            UpdateHistoryControls();
        }
        catch (Exception ex) { DownloadStatusTextBlock.Text = $"無法移除紀錄：{ex.Message}"; }
    }

    private void ClearHistory_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            _userData.Save("history.json", new List<DownloadRecord>());
            _history.Clear();
            HistoryListBox.SelectedItem = null;
            UpdateHistoryControls();
        }
        catch (Exception ex) { DownloadStatusTextBlock.Text = $"無法清除紀錄：{ex.Message}"; }
    }
    private void HistoryDoubleTapped(object? sender, TappedEventArgs e) => OpenHistoryFile_Click(sender, e);
    private void OpenHistoryFile_Click(object? sender, RoutedEventArgs e)
    {
        if (HistoryListBox.SelectedItem is DownloadRecord { CanOpen: true } record) OpenLocalPath(record.FilePath!, false);
    }
    private void OpenHistoryFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (HistoryListBox.SelectedItem is DownloadRecord record) OpenLocalPath(record.Directory, true);
    }
    private void OpenSaveFolder_Click(object? sender, RoutedEventArgs e) => OpenLocalPath(SavePathTextBox.Text ?? "", true);
    private void OpenLocalPath(string path, bool folder)
    {
        try
        {
            if (!Path.IsPathFullyQualified(path)) throw new IOException("請選擇完整的本機路徑");
            if (folder ? !Directory.Exists(path) : !File.Exists(path)) throw new IOException("找不到檔案或資料夾，可能已移動或刪除");
            Process.Start(new ProcessStartInfo(Path.GetFullPath(path)) { UseShellExecute = true });
        }
        catch (Exception ex) { DownloadStatusTextBlock.Text = ex.Message; }
    }

    private void DetectPlatform()
    {
        var url = UrlTextBox.Text?.Trim() ?? string.Empty;
        if (url.Length == 0)
        {
            ShowPlatform("等待網址", "貼上網址後將自動辨識來源平台");
            return;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrEmpty(uri.Host))
        {
            ShowPlatform("網址格式不正確", "請輸入完整的 http:// 或 https:// 網址");
            return;
        }

        var host = uri.IdnHost.TrimEnd('.');
        if (MatchesDomain(host, "youtube.com") || MatchesDomain(host, "youtu.be"))
            ShowPlatform("YouTube", "已辨識為 YouTube 網址");
        else if (MatchesDomain(host, "facebook.com") || MatchesDomain(host, "fb.watch"))
            ShowPlatform("Facebook", "已辨識為 Facebook 網址");
        else if (MatchesDomain(host, "instagram.com"))
            ShowPlatform("Instagram", "已辨識為 Instagram 網址");
        else if (MatchesDomain(host, "threads.net") || MatchesDomain(host, "threads.com"))
            ShowPlatform("Threads", "已辨識為 Threads 網址");
        else if (MatchesDomain(host, "x.com") || MatchesDomain(host, "twitter.com"))
            ShowPlatform("X / Twitter", "已辨識為 X / Twitter 網址");
        else
            ShowPlatform("其他網站", "將由下載核心確認此網址是否支援");
    }

    private static bool MatchesDomain(string host, string domain)
    {
        return host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);
    }

    private void ShowPlatform(string platform, string description)
    {
        PlatformTextBlock.Text = platform;
        PlatformDescriptionTextBlock.Text = description;
    }

    private async void SelectFolderButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (!StorageProvider.CanPickFolder)
            {
                ShowPlatform("無法選擇資料夾", "目前系統不支援資料夾選擇功能");
                return;
            }

            var folders = await StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions
                {
                    Title = "選擇影片儲存位置",
                    AllowMultiple = false
                });

            if (folders.Count == 0)
                return;

            using var folder = folders[0];
            var path = folder.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                ShowPlatform("無法使用此資料夾", "請選擇本機資料夾作為影片儲存位置");
                return;
            }

            SavePathTextBox.Text = path;
            SaveSettings();
            DetectPlatform();
        }
        catch (Exception ex)
        {
            ShowPlatform("資料夾選擇失敗", ex.Message);
        }
    }
}
