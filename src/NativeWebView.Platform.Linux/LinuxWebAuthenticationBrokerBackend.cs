using NativeWebView.Core;

namespace NativeWebView.Platform.Linux;

public sealed class LinuxWebAuthenticationBrokerBackend : WebAuthenticationBrokerStubBase
{
    public LinuxWebAuthenticationBrokerBackend()
        : base(NativeWebViewPlatform.Linux, LinuxPlatformFeatures.Instance)
    {
    }
}
