using NativeWebView.Core;

namespace NativeWebView.Platform.macOS;

public sealed class MacOSWebAuthenticationBrokerBackend : WebAuthenticationBrokerStubBase
{
    public MacOSWebAuthenticationBrokerBackend()
        : base(NativeWebViewPlatform.MacOS, MacOSPlatformFeatures.Instance)
    {
    }
}
