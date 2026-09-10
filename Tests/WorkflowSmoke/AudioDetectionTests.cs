using System;
using System.Text.Json;
using VideoDownloader.Services;

public static class AudioDetectionTests
{
    public static void Run()
    {
        Check("X HLS audio without codec", """
            {"formats":[
              {"format_id":"hls-audio-128000-Audio","vcodec":"none","audio_ext":"mp4","ext":"mp4","protocol":"m3u8_native"},
              {"format_id":"hls-1328","acodec":"none","vcodec":"avc1.640032","height":1206}]}
            """, true);
        Check("null codec on audio-only stream", """
            {"formats":[{"acodec":null,"vcodec":"none","ext":"m4a"}]}
            """, true);
        Check("known audio codec", """{"formats":[{"acodec":"aac","vcodec":"h264"}]}""", true);
        Check("unknown codec with audio declaration", """{"acodec":"unknown","vcodec":"none","ext":"mp4"}""", true);
        Check("explicitly silent", """{"formats":[{"acodec":"none","vcodec":"h264"}]}""", false);
        Check("explicit none wins over container", """{"acodec":"none","vcodec":"none","audio_ext":"mp4"}""", false);
        Check("both codecs absent is not evidence", """{"formats":[{"ext":"mp4"}]}""", false);
        Check("empty codec is not evidence", """{"formats":[{"acodec":"","vcodec":"h264","ext":"mp4"}]}""", false);
        Check("image is not an audio stream", """{"vcodec":"none","ext":"jpg"}""", false);
        Check("DRM audio must be excluded", """{"formats":[{"vcodec":"none","audio_ext":"m4a","has_drm":true}]}""", false);
        Check("empty formats", """{"formats":[]}""", false);
        var info = YtDlpService.ParseInfo("""
            {"entries":[null,{"title":"audio","formats":[{"vcodec":"none","audio_ext":"mp4"}]}]}
            """);
        if (!info.HasAudio || info.PlaylistIndex != 2) throw new Exception("Audio-only carousel entry must remain selectable");
        Console.WriteLine("PASS: audio detection regression cases");
    }

    private static void Check(string name, string json, bool expected)
    {
        using var document = JsonDocument.Parse(json);
        if (MediaOptions.HasAudio(document.RootElement) != expected) throw new Exception(name);
    }
}
