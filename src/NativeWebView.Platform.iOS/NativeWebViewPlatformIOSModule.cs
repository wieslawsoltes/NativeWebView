using NativeWebView.Core;

namespace NativeWebView.Platform.iOS;

public static class NativeWebViewPlatformIOSModule
{
    public static void Register(NativeWebViewBackendFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        factory.RegisterNativeWebViewBackend(
            NativeWebViewPlatform.IOS,
            static () => new IOSNativeWebViewBackend(),
            IOSPlatformFeatures.Instance);

        factory.RegisterPlatformDiagnostics(
            NativeWebViewPlatform.IOS,
            static () => IOSPlatformDiagnostics.Create());

        factory.RegisterWebAuthenticationBrokerBackend(
            NativeWebViewPlatform.IOS,
            static () => new IOSWebAuthenticationBrokerBackend());
    }

    public static void RegisterDefault()
    {
        Register(NativeWebViewRuntime.Factory);
    }
}
