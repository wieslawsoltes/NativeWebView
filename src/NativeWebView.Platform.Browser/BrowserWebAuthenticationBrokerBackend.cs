using NativeWebView.Core;

namespace NativeWebView.Platform.Browser;

public sealed class BrowserWebAuthenticationBrokerBackend : WebAuthenticationBrokerStubBase
{
    public BrowserWebAuthenticationBrokerBackend()
        : base(NativeWebViewPlatform.Browser, BrowserPlatformFeatures.Instance)
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

        var responseData = AppendQueryParameters(callbackUri, "popup=1&platform=browser");
        return WebAuthenticationResult.Success(responseData);
    }

    private static string AppendQueryParameters(Uri callbackUri, string parameters)
    {
        var value = callbackUri.ToString();
        var fragmentIndex = value.IndexOf('#');
        var beforeFragment = fragmentIndex >= 0 ? value[..fragmentIndex] : value;
        var fragment = fragmentIndex >= 0 ? value[fragmentIndex..] : string.Empty;

        var separator = beforeFragment.Contains('?', StringComparison.Ordinal)
            ? (beforeFragment.EndsWith("?", StringComparison.Ordinal) || beforeFragment.EndsWith("&", StringComparison.Ordinal) ? string.Empty : "&")
            : "?";
        return $"{beforeFragment}{separator}{parameters}{fragment}";
    }
}
