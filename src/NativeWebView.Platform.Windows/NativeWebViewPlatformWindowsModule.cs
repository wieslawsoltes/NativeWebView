using NativeWebView.Core;

namespace NativeWebView.Platform.Windows;

public static class NativeWebViewPlatformWindowsModule
{
    public static void Register(NativeWebViewBackendFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        factory.RegisterNativeWebViewBackend(
            NativeWebViewPlatform.Windows,
            static () => new WindowsNativeWebViewBackend(),
            WindowsPlatformFeatures.Instance);

        factory.RegisterPlatformDiagnostics(
            NativeWebViewPlatform.Windows,
            static () => WindowsPlatformDiagnostics.Create());

        factory.RegisterNativeWebDialogBackend(
            NativeWebViewPlatform.Windows,
            static () => new WindowsNativeWebDialogBackend());

        factory.RegisterWebAuthenticationBrokerBackend(
            NativeWebViewPlatform.Windows,
            static () => new WindowsWebAuthenticationBrokerBackend());
    }

    public static void RegisterDefault()
    {
        Register(NativeWebViewRuntime.Factory);
    }
}
