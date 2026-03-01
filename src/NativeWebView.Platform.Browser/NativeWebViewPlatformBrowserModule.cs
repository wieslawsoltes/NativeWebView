using NativeWebView.Core;

namespace NativeWebView.Platform.Browser;

public static class NativeWebViewPlatformBrowserModule
{
    public static void Register(NativeWebViewBackendFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        factory.RegisterNativeWebViewBackend(
            NativeWebViewPlatform.Browser,
            static () => new BrowserNativeWebViewBackend(),
            BrowserPlatformFeatures.Instance);

        factory.RegisterPlatformDiagnostics(
            NativeWebViewPlatform.Browser,
            static () => BrowserPlatformDiagnostics.Create());

        factory.RegisterWebAuthenticationBrokerBackend(
            NativeWebViewPlatform.Browser,
            static () => new BrowserWebAuthenticationBrokerBackend());
    }

    public static void RegisterDefault()
    {
        Register(NativeWebViewRuntime.Factory);
    }
}
