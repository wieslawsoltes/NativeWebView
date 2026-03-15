using NativeWebView.Core;

namespace NativeWebView.Platform.Windows;

internal static class WindowsPlatformFeatures
{
    public static readonly IWebViewPlatformFeatures Instance = new WebViewPlatformFeatures(
        NativeWebViewPlatform.Windows,
        NativeWebViewFeature.EmbeddedView |
        NativeWebViewFeature.Dialog |
        NativeWebViewFeature.AuthenticationBroker |
        NativeWebViewFeature.DevTools |
        NativeWebViewFeature.ContextMenu |
        NativeWebViewFeature.StatusBar |
        NativeWebViewFeature.ZoomControl |
        NativeWebViewFeature.Printing |
        NativeWebViewFeature.PrintUi |
        NativeWebViewFeature.WebResourceRequestInterception |
        NativeWebViewFeature.NewWindowRequestInterception |
        NativeWebViewFeature.EnvironmentOptions |
        NativeWebViewFeature.ControllerOptions |
        NativeWebViewFeature.NativePlatformHandle |
        NativeWebViewFeature.CookieManager |
        NativeWebViewFeature.CommandManager |
        NativeWebViewFeature.CustomChrome |
        NativeWebViewFeature.WindowMoveResize |
        NativeWebViewFeature.ScriptExecution |
        NativeWebViewFeature.WebMessageChannel |
        NativeWebViewFeature.GpuSurfaceRendering |
        NativeWebViewFeature.OffscreenRendering |
        NativeWebViewFeature.RenderFrameCapture |
        NativeWebViewFeature.ProxyConfiguration);
}
