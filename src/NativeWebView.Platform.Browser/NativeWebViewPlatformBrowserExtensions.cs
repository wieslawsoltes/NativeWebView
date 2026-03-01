using NativeWebView.Core;

namespace NativeWebView.Platform.Browser;

public static class NativeWebViewPlatformBrowserExtensions
{
    public static NativeWebViewBackendFactory UseNativeWebViewBrowser(this NativeWebViewBackendFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        NativeWebViewPlatformBrowserModule.Register(factory);
        return factory;
    }
}
