using Avalonia;
using Avalonia.iOS;
using Foundation;
using NativeWebView.Integration;

namespace NativeWebView.Integration.iOS;

[Register("AppDelegate")]
public sealed class AppDelegate : AvaloniaAppDelegate<App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder);
    }
}
