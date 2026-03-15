using NativeWebView.Core;

namespace NativeWebView.Platform.iOS;

internal static class IOSPlatformFeatures
{
    private const NativeWebViewFeature BaseFeatures =
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
        NativeWebViewFeature.RenderFrameCapture;

    public static IWebViewPlatformFeatures Instance => new WebViewPlatformFeatures(
        NativeWebViewPlatform.IOS,
        BaseFeatures |
#if NATIVEWEBVIEW_IOS_RUNTIME
        (OperatingSystem.IsIOSVersionAtLeast(17)
            ? NativeWebViewFeature.ProxyConfiguration
            : NativeWebViewFeature.None));
#else
        NativeWebViewFeature.None);
#endif
}
