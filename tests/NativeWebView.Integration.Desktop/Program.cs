using Avalonia;
using NativeWebView.Integration;

namespace NativeWebView.Integration.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return Environment.ExitCode;
    }

    private static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
