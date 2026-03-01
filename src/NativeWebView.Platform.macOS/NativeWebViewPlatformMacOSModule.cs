using NativeWebView.Core;

namespace NativeWebView.Platform.macOS;

public static class NativeWebViewPlatformMacOSModule
{
    public static void Register(NativeWebViewBackendFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        factory.RegisterNativeWebViewBackend(
            NativeWebViewPlatform.MacOS,
            static () => new MacOSNativeWebViewBackend(),
            MacOSPlatformFeatures.Instance);

        factory.RegisterPlatformDiagnostics(
            NativeWebViewPlatform.MacOS,
            static () => MacOSPlatformDiagnostics.Create());

        factory.RegisterNativeWebDialogBackend(
            NativeWebViewPlatform.MacOS,
            static () => new MacOSNativeWebDialogBackend());

        factory.RegisterWebAuthenticationBrokerBackend(
            NativeWebViewPlatform.MacOS,
            static () => new MacOSWebAuthenticationBrokerBackend());
    }

    public static void RegisterDefault()
    {
        Register(NativeWebViewRuntime.Factory);
    }
}
