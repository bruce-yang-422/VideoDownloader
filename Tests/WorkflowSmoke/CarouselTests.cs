using System;
using System.Linq;
using VideoDownloader.Services;

public static class CarouselTests
{
    public static void Run()
    {
        const string video = """
            {"title":"clip","playlist_index":3,"formats":[
              {"format_id":"v720","height":720,"width":1280,"vcodec":"h264","acodec":"aac"}]}
            """;
        var single = YtDlpService.ParseInfo(video);
        if (single.PlaylistIndex is not null || single.Qualities.Count != 1) throw new Exception("Single video regression");
        var carousel = YtDlpService.ParseInfo("{\"entries\":[null,{\"formats\":[]}," + video + "," + video + "]}");
        if (carousel.PlaylistIndex != 3 || !carousel.HasAudio || carousel.Qualities.Single().Resolution != 720)
            throw new Exception("Must skip failed/image entries and keep original item index");
        var fallback = YtDlpService.ParseInfo("{\"entries\":[null," + video.Replace("\"playlist_index\":3,", "") + "]}");
        if (fallback.PlaylistIndex != 2) throw new Exception("Must use original array position when index is absent");
        foreach (var format in new[] { "mp4", "mkv", "mp3", "wav" })
        {
            var arguments = MediaOptions.BuildArguments(new DownloadOptions(format, carousel.Qualities[0],
                format == "wav" ? MediaOptions.WavQualities[0] : MediaOptions.AudioQualities[0], carousel.PlaylistIndex));
            if (arguments[0] != "--playlist-items" || arguments[1] != "3" || arguments.Contains("--ignore-errors"))
                throw new Exception("Download must select only the chosen entry and propagate download failures");
        }
        var failed = false;
        try { YtDlpService.ParseInfo("{\"entries\":[null,{\"formats\":[]}]}"); }
        catch (InvalidOperationException) { failed = true; }
        if (!failed) throw new Exception("Image-only post should report no playable media");
        Console.WriteLine("PASS: carousel selection, single video, image-only post and all output formats");
    }
}
