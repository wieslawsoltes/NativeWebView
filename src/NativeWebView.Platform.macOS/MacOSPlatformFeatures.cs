using NativeWebView.Core;

namespace NativeWebView.Platform.macOS;

internal static class MacOSPlatformFeatures
{
    public static readonly IWebViewPlatformFeatures Instance = new WebViewPlatformFeatures(
        NativeWebViewPlatform.MacOS,
        NativeWebViewFeature.EmbeddedView |
        NativeWebViewFeature.Dialog |
        NativeWebViewFeature.AuthenticationBroker |
        NativeWebViewFeature.ContextMenu |
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
        NativeWebViewFeature.ScriptExecution |
        NativeWebViewFeature.WebMessageChannel |
        NativeWebViewFeature.GpuSurfaceRendering |
        NativeWebViewFeature.OffscreenRendering |
        NativeWebViewFeature.RenderFrameCapture);
}
