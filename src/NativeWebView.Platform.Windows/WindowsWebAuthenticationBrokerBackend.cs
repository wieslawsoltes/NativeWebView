using NativeWebView.Core;

namespace NativeWebView.Platform.Windows;

public sealed class WindowsWebAuthenticationBrokerBackend : WebAuthenticationBrokerStubBase
{
    public WindowsWebAuthenticationBrokerBackend()
        : base(NativeWebViewPlatform.Windows, WindowsPlatformFeatures.Instance)
    {
    }
}
