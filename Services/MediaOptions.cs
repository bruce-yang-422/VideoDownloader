using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VideoDownloader.Services;

public sealed record VideoQuality(string Label, int? Resolution, string Selector)
{
    public override string ToString() => Label;
}

public sealed record AudioQuality(string Label, string Value)
{
    public override string ToString() => Label;
}

public sealed record DownloadOptions(string Format, VideoQuality? Video, AudioQuality? Audio, int? PlaylistIndex = null)
{
    public bool AudioOnly => Format is "mp3" or "wav";
    public string FileTag => AudioOnly ? $"audio-{Format}-{Audio?.Value ?? "0"}" : $"{Format}-{Video?.Resolution?.ToString() ?? "best"}p";
}

public static class MediaOptions
{
    public static readonly AudioQuality[] AudioQualities =
    [new("最佳音質（可變位元率）", "0"), new("320 kbps", "320K"), new("256 kbps", "256K"),
     new("192 kbps", "192K"), new("128 kbps", "128K"), new("96 kbps", "96K")];

    public static readonly AudioQuality[] WavQualities =
    [new("16-bit · 來源取樣率", "s16"), new("24-bit · 來源取樣率", "s24"),
     new("16-bit · 44.1 kHz", "s16-44100"), new("24-bit · 48 kHz", "s24-48000")];

    private static IEnumerable<JsonElement> Formats(JsonElement root) =>
        root.TryGetProperty("formats", out var formats) && formats.ValueKind == JsonValueKind.Array
            ? formats.EnumerateArray() : new[] { root };

    private static string? Text(JsonElement format, string name) =>
        format.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static int? Dimension(JsonElement format, string name) =>
        format.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && number > 0
            ? (int)number : null;
    private static bool Usable(JsonElement format) =>
        !format.TryGetProperty("has_drm", out var drm) || drm.ValueKind != JsonValueKind.True;

    public static bool HasAudio(JsonElement root) => Formats(root).Any(f =>
    {
        if (!Usable(f)) return false;
        var codec = Text(f, "acodec");
        if (codec == "none") return false;
        if (!string.IsNullOrWhiteSpace(codec)) return true;

        // X's HLS audio renditions may omit acodec while explicitly declaring
        // vcodec=none and an audio container. Missing codec alone is not evidence.
        if (Text(f, "vcodec") != "none") return false;
        var audioExtension = Text(f, "audio_ext");
        if (!string.IsNullOrWhiteSpace(audioExtension) && audioExtension != "none") return true;
        return Text(f, "ext") is "m4a" or "mp4" or "webm" or "mp3" or "aac"
            or "ogg" or "opus" or "wav" or "flac";
    });

    public static IReadOnlyList<VideoQuality> GetVideoQualities(JsonElement root)
    {
        var formats = Formats(root).Where(f => Usable(f) && Text(f, "vcodec") is not (null or "none")).ToArray();
        var groups = formats.Select(f => new
        {
            Format = f,
            Resolution = Dimension(f, "width") is { } width && Dimension(f, "height") is { } height
                ? Math.Min(width, height) : Dimension(f, "height")
        }).Where(f => f.Resolution != null).GroupBy(f => f.Resolution!.Value).OrderByDescending(g => g.Key);
        var result = new List<VideoQuality>();
        foreach (var group in groups)
        {
            // Exact format IDs prevent selecting an unadvertised resolution, including portrait videos.
            var ids = group.Select(f => Text(f.Format, "format_id"))
                .Where(id => id != null && Regex.IsMatch(id, @"^[a-zA-Z0-9_.-]+$")).Distinct().ToArray();
            if (ids.Length == 0) continue;
            var filter = "[format_id~='^(" + string.Join("|", ids.Select(id => Regex.Escape(id!))) + ")$']";
            var label = group.Key switch { 2160 => "4K · 2160p", 1440 => "2K · 1440p", 4320 => "8K · 4320p", _ => $"{group.Key}p" };
            if (result.Count == 0) label += "（來源最高）";
            result.Add(new VideoQuality(label, group.Key, $"bv{filter}+ba/b{filter}/bv{filter}"));
        }
        if (result.Count == 0 && formats.Length > 0)
            result.Add(new VideoQuality("來源最佳（解析度未知）", null, "bv+ba/b/bv"));
        return result;
    }

    public static string[] BuildArguments(DownloadOptions options)
    {
        var arguments = BuildMediaArguments(options);
        if (options.PlaylistIndex is null) return arguments;
        if (options.PlaylistIndex <= 0) throw new ArgumentException("貼文項目編號必須大於零");
        return ["--playlist-items", options.PlaylistIndex.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), .. arguments];
    }

    private static string[] BuildMediaArguments(DownloadOptions options)
    {
        if (options.Format is not ("mp4" or "mkv" or "mp3" or "wav"))
            throw new ArgumentException("不支援的下載格式");
        if (options.AudioOnly)
        {
            if (options.Audio is null || !(options.Format == "wav" ? WavQualities : AudioQualities).Any(q => q.Value == options.Audio.Value))
                throw new ArgumentException("請選擇音訊品質");
            // Decode to an intermediate WAV so matching source codecs cannot bypass quality conversion.
            return ["-f", "ba/b", "-x", "--audio-format", "wav",
                "--postprocessor-args", "ExtractAudio+ffmpeg_o:-c:a pcm_s24le"];
        }
        return ["-f", options.Video?.Selector ?? "bv+ba/b/bv", "--merge-output-format", options.Format,
            "--remux-video", options.Format];
    }
}
