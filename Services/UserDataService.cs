using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace VideoDownloader.Services;

public sealed record UserSettings(string SaveDirectory, string Format = "mp4", string Mp3Quality = "0", string WavQuality = "s16");
public sealed record DownloadRecord(string Title, string Platform, string Quality, DateTimeOffset CompletedAt,
    string? FilePath, string Directory, string Status, string? Error)
{
    public string Summary => $"{Status} · {Platform} · {Quality} · {CompletedAt:MM/dd HH:mm}";
    public string Details => $"{Title}\n{Summary}\n{FilePath ?? Directory}\n{Error}";
    public bool CanOpen => Status == "成功" && !string.IsNullOrEmpty(FilePath);
}

public sealed class UserDataService
{
    private readonly string _directory;
    public UserDataService(string? directory = null) => _directory = directory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VideoDownloader");
    public T Load<T>(string name, T fallback)
    {
        var path = Path.Combine(_directory, name);
        if (!File.Exists(path)) return fallback;
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path)) ?? fallback;
    }
    public void Save<T>(string name, T value)
    {
        System.IO.Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, name);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static string DefaultDownloadDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            var id = new Guid("374DE290-123F-4565-9164-39C4925E467B");
            if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out var pointer) == 0)
            {
                try { return Path.Combine(Marshal.PtrToStringUni(pointer)!, "VideoDownloader"); }
                finally { Marshal.FreeCoTaskMem(pointer); }
            }
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "VideoDownloader");
    }
    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out IntPtr path);
}
