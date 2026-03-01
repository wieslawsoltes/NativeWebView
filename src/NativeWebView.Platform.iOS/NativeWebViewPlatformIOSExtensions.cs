using NativeWebView.Core;

namespace NativeWebView.Platform.iOS;

public static class NativeWebViewPlatformIOSExtensions
{
    public static NativeWebViewBackendFactory UseNativeWebViewIOS(this NativeWebViewBackendFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        NativeWebViewPlatformIOSModule.Register(factory);
        return factory;
    }
}
