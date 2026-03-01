using NativeWebView.Core;

namespace NativeWebView.Platform.macOS;

public static class NativeWebViewPlatformMacOSExtensions
{
    public static NativeWebViewBackendFactory UseNativeWebViewMacOS(this NativeWebViewBackendFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        NativeWebViewPlatformMacOSModule.Register(factory);
        return factory;
    }
}
