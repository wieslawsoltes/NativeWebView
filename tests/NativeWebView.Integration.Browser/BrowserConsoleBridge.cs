using System.Runtime.InteropServices.JavaScript;

namespace NativeWebView.Integration.Browser;

internal static partial class BrowserConsoleBridge
{
    [JSImport("globalThis.__nativeWebViewIntegration.publish")]
    public static partial void Publish(string message);
}
