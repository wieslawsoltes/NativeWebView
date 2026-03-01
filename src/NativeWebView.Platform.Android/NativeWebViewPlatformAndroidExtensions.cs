using NativeWebView.Core;

namespace NativeWebView.Platform.Android;

public static class NativeWebViewPlatformAndroidExtensions
{
    public static NativeWebViewBackendFactory UseNativeWebViewAndroid(this NativeWebViewBackendFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        NativeWebViewPlatformAndroidModule.Register(factory);
        return factory;
    }
}
