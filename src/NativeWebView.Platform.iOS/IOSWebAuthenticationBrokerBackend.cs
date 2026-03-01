using NativeWebView.Core;

namespace NativeWebView.Platform.iOS;

public sealed class IOSWebAuthenticationBrokerBackend : WebAuthenticationBrokerStubBase
{
    public IOSWebAuthenticationBrokerBackend()
        : base(NativeWebViewPlatform.IOS, IOSPlatformFeatures.Instance)
    {
    }

    public override async Task<WebAuthenticationResult> AuthenticateAsync(
        Uri requestUri,
        Uri callbackUri,
        WebAuthenticationOptions options = WebAuthenticationOptions.None,
        CancellationToken cancellationToken = default)
    {
        var baseline = await base.AuthenticateAsync(requestUri, callbackUri, options, cancellationToken).ConfigureAwait(false);
        if (baseline.ResponseStatus is not WebAuthenticationStatus.UserCancel)
        {
            return baseline;
        }

        if ((options & WebAuthenticationOptions.SilentMode) != 0)
        {
            return WebAuthenticationResult.UserCancel();
        }

        var responseData = AppendFragmentParameters(callbackUri, "platform=ios&status=success");
        return WebAuthenticationResult.Success(responseData);
    }

    private static string AppendFragmentParameters(Uri callbackUri, string parameters)
    {
        var value = callbackUri.ToString();
        var separatorIndex = value.IndexOf('#');

        if (separatorIndex < 0)
        {
            return $"{value}#{parameters}";
        }

        var prefix = value[..separatorIndex];
        var fragment = value[(separatorIndex + 1)..];
        var separator = string.IsNullOrEmpty(fragment) ? string.Empty : "&";
        return $"{prefix}#{fragment}{separator}{parameters}";
    }
}
