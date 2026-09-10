using Avalonia;
using System;
using System.Runtime.InteropServices;

namespace VideoDownloader;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Keep this ID aligned with the installer shortcuts; do not include a version.
        if (OperatingSystem.IsWindows())
        {
            var result = SetCurrentProcessExplicitAppUserModelID("BruceYang.VideoDownloader");
            if (result < 0) System.Diagnostics.Trace.WriteLine($"Taskbar identity: 0x{result:X8}");
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
