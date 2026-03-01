using NativeWebView.Core;

namespace NativeWebView.Platform.Windows;

public static class NativeWebViewPlatformWindowsExtensions
{
    public static NativeWebViewBackendFactory UseNativeWebViewWindows(this NativeWebViewBackendFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        NativeWebViewPlatformWindowsModule.Register(factory);
        return factory;
    }
}
