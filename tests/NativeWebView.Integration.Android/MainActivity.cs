using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using Avalonia;
using Avalonia.Android;
using NativeWebView.Integration;

namespace NativeWebView.Integration.Android;

[Activity(
    Label = "NativeWebView Integration",
    Theme = "@style/MyTheme.NoActionBar",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity<App>
{
    private const string IntegrationLogTag = "NWVIntegration";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        IntegrationPlatformContext.ExternalLogger = static message => Log.Info(IntegrationLogTag, message);
        base.OnCreate(savedInstanceState);
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder);
    }
}
