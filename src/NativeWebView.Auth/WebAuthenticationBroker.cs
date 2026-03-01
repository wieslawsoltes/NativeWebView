using NativeWebView.Core;

namespace NativeWebView.Auth;

public sealed class WebAuthenticationBroker : IDisposable
{
    private readonly WebAuthenticationBrokerController _controller;

    public WebAuthenticationBroker()
        : this(CreateDefaultBackend())
    {
    }

    public WebAuthenticationBroker(IWebAuthenticationBrokerBackend backend)
    {
        _controller = new WebAuthenticationBrokerController(backend);
    }

    public NativeWebViewPlatform Platform => _controller.Platform;

    public IWebViewPlatformFeatures Features => _controller.Features;

    public WebAuthenticationBrokerState State => _controller.State;

    public Task<WebAuthenticationResult> AuthenticateAsync(
        Uri requestUri,
        Uri callbackUri,
        WebAuthenticationOptions options = WebAuthenticationOptions.None,
        CancellationToken cancellationToken = default)
    {
        return _controller.AuthenticateAsync(requestUri, callbackUri, options, cancellationToken);
    }

    public void Dispose()
    {
        _controller.Dispose();
    }

    private static IWebAuthenticationBrokerBackend CreateDefaultBackend()
    {
        NativeWebViewRuntime.EnsureCurrentPlatformRegistered();
        NativeWebViewRuntime.Factory.TryCreateWebAuthenticationBrokerBackend(NativeWebViewRuntime.CurrentPlatform, out var backend);
        return backend;
    }
}
