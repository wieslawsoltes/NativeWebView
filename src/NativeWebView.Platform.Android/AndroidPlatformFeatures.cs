using NativeWebView.Core;

namespace NativeWebView.Platform.Android;

internal static class AndroidPlatformFeatures
{
    public static readonly IWebViewPlatformFeatures Instance = new WebViewPlatformFeatures(
        NativeWebViewPlatform.Android,
        NativeWebViewFeature.EmbeddedView |
        NativeWebViewFeature.AuthenticationBroker |
        NativeWebViewFeature.ContextMenu |
        NativeWebViewFeature.ZoomControl |
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
