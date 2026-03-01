using NativeWebView.Core;

namespace NativeWebView.Platform.Android;

public static class NativeWebViewPlatformAndroidModule
{
    public static void Register(NativeWebViewBackendFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        factory.RegisterNativeWebViewBackend(
            NativeWebViewPlatform.Android,
            static () => new AndroidNativeWebViewBackend(),
            AndroidPlatformFeatures.Instance);

        factory.RegisterPlatformDiagnostics(
            NativeWebViewPlatform.Android,
            static () => AndroidPlatformDiagnostics.Create());

        factory.RegisterWebAuthenticationBrokerBackend(
            NativeWebViewPlatform.Android,
            static () => new AndroidWebAuthenticationBrokerBackend());
    }

    public static void RegisterDefault()
    {
        Register(NativeWebViewRuntime.Factory);
    }
}
