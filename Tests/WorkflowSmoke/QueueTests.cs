using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VideoDownloader.Services;

static class QueueTests
{
    public static async Task RunAsync()
    {
        var queue = new DownloadQueue();
        DownloadJob Job(string title) => new("https://example.com/" + title, title, "test", "MP4", "C:/Downloads", new("mp4", null, null));
        var first = Job("first"); var removed = Job("removed"); var second = Job("second");
        queue.Add(first); queue.Add(removed);
        var started = new List<string>();
        var release = new TaskCompletionSource();
        var active = 0;
        async Task Run(DownloadJob job)
        {
            if (++active != 1) throw new Exception("Concurrent execution");
            started.Add(job.Title);
            try
            {
                if (job == first) { await release.Task; throw new OperationCanceledException(); }
            }
            finally { active--; }
        }
        queue.Start(Run, CancellationToken.None);
        if (queue.Remove(first)) throw new Exception("Removed running job");
        if (!queue.Remove(removed)) throw new Exception("Could not remove waiting job");
        queue.Add(second);
        queue.Start(Run, CancellationToken.None);
        release.SetResult();
        await queue.Completion;
        if (string.Join(",", started) != "first,second" || queue.Items.Count != 0) throw new Exception("Queue order/cancellation failed");
        queue.Add(Job("third")); queue.Start(Run, CancellationToken.None); await queue.Completion;
        if (started.Count != 3) throw new Exception("Queue did not restart");
        var capture = new ProgressCapture();
        var reader = new FfmpegProgressReader(capture) { Duration = 20 };
        reader.Begin("merge"); reader.ReadLine("out_time_us=10000000");
        if (capture.Last?.Percent != 50) throw new Exception("FFmpeg time percentage wrong");
        reader.Duration = null; reader.ReadLine("out_time_us=10000000");
        if (capture.Last?.Percent != null) throw new Exception("Invented percentage for unknown duration");
        reader.End(); if (capture.Last?.Percent != 100) throw new Exception("Completion event missing");
        Console.WriteLine("PASS: sequential queue, removal, cancellation continuation, restart, FFmpeg percentage parsing");
    }
    sealed class ProgressCapture : IProgress<DownloadProgress>
    {
        public DownloadProgress? Last;
        public void Report(DownloadProgress value) => Last = value;
    }
}
