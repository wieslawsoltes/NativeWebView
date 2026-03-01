using NativeWebView.Core;

namespace NativeWebView.Platform.Browser;

internal static class BrowserPlatformFeatures
{
    public static readonly IWebViewPlatformFeatures Instance = new WebViewPlatformFeatures(
        NativeWebViewPlatform.Browser,
        NativeWebViewFeature.EmbeddedView |
        NativeWebViewFeature.AuthenticationBroker |
        NativeWebViewFeature.NewWindowRequestInterception |
        NativeWebViewFeature.EnvironmentOptions |
        NativeWebViewFeature.ControllerOptions |
        NativeWebViewFeature.NativePlatformHandle |
        NativeWebViewFeature.CookieManager |
        NativeWebViewFeature.CommandManager |
        NativeWebViewFeature.ScriptExecution |
        NativeWebViewFeature.WebMessageChannel |
        NativeWebViewFeature.GpuSurfaceRendering |
        NativeWebViewFeature.OffscreenRendering |
        NativeWebViewFeature.RenderFrameCapture);
}
