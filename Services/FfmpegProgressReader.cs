using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace VideoDownloader.Services;

public sealed class FfmpegProgressReader(IProgress<DownloadProgress> progress)
{
    public double? Duration { get; set; }
    private string _stage = "FFmpeg 處理";
    private bool _active;
    public void Begin(string stage)
    {
        _stage = stage; _active = true;
        progress.Report(new(null, 0, null, null, stage));
    }
    public void End()
    {
        if (_active) progress.Report(new(100, 0, null, 0, _stage));
        _active = false;
    }
    public void ReadLine(string line)
    {
        if (!_active) return;
        if (line.StartsWith("out_time_us=", StringComparison.Ordinal) &&
            double.TryParse(line[12..], NumberStyles.Float, CultureInfo.InvariantCulture, out var microseconds))
        {
            var percent = Duration is > 0 ? Math.Clamp(microseconds / 1000000 / Duration.Value * 100, 0, 99.9) : (double?)null;
            progress.Report(new(percent, 0, null, null, _stage));
        }
    }
    public void ReadFile(string path)
    {
        try
        {
            if (!_active || !File.Exists(path)) return;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var line = reader.ReadToEnd().Split('\n').LastOrDefault(l => l.StartsWith("out_time_us="));
            if (line is not null) ReadLine(line.Trim());
        }
        catch (IOException) { }
    }

    public async Task WatchAsync(string path, CancellationToken token)
    {
        string? previous = null;
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (_active && File.Exists(path))
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(stream);
                    var text = await reader.ReadToEndAsync(token);
                    if (text != previous)
                    {
                        previous = text;
                        var line = text.Split('\n').LastOrDefault(l => l.StartsWith("out_time_us="));
                        if (line is not null) ReadLine(line.Trim());
                    }
                }
                await Task.Delay(100, token);
            }
            catch (IOException) { await Task.Delay(100, token); }
        }
    }
}
