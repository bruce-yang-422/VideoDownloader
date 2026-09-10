using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace VideoDownloader.Services;

public sealed class DownloadJob(string url, string title, string platform, string quality, string directory, DownloadOptions options) : INotifyPropertyChanged
{
    public string Url { get; } = url;
    public string Title { get; } = title;
    public string Platform { get; } = platform;
    public string Quality { get; } = quality;
    public string Directory { get; } = directory;
    public DownloadOptions Options { get; } = options;
    private string _status = "等待中";
    public string Status { get => _status; internal set { _status = value; PropertyChanged?.Invoke(this, new(nameof(Status))); PropertyChanged?.Invoke(this, new(nameof(Summary))); } }
    public string Summary => $"{Status} · {Quality}";
    public event PropertyChangedEventHandler? PropertyChanged;
}

// Owned by the UI thread: enqueue and removal stay synchronous, only one runner is active.
public sealed class DownloadQueue
{
    public ObservableCollection<DownloadJob> Items { get; } = new();
    public DownloadJob? Current { get; private set; }
    public Task Completion { get; private set; } = Task.CompletedTask;
    private bool _running;
    public void Add(DownloadJob job) => Items.Add(job);
    public bool Remove(DownloadJob job) => job != Current && Items.Remove(job);
    public void Start(Func<DownloadJob, Task> run, CancellationToken token)
    {
        if (_running) return;
        _running = true;
        Completion = RunAsync(run, token);
    }
    private async Task RunAsync(Func<DownloadJob, Task> run, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && Items.FirstOrDefault() is { } job)
            {
                Current = job;
                job.Status = "執行中";
                try { await run(job); }
                catch (OperationCanceledException) { job.Status = "已取消"; }
                catch (Exception) { job.Status = "失敗"; }
                finally { Items.Remove(job); Current = null; }
            }
        }
        finally { _running = false; }
    }
}
