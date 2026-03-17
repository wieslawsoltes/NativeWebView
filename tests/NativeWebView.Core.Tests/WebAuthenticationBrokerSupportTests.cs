using NativeWebView.Core;
using NativeWebView.Platform.Android;
using NativeWebView.Platform.Browser;
using NativeWebView.Platform.Linux;
using NativeWebView.Platform.Windows;
using NativeWebView.Platform.iOS;
using NativeWebView.Platform.macOS;

namespace NativeWebView.Core.Tests;

public sealed class WebAuthenticationBrokerSupportTests
{
    [Fact]
    public void IsCallbackUri_IgnoresQueryAndFragment_ButRequiresSchemeHostPortAndPath()
    {
        var callback = new Uri("https://example.com/callback/path?state=seed#token=seed");

        Assert.True(WebAuthenticationBrokerBackendSupport.IsCallbackUri(
            new Uri("https://example.com/callback/path?code=123#done"),
            callback));
        Assert.False(WebAuthenticationBrokerBackendSupport.IsCallbackUri(
            new Uri("https://example.com/callback/other"),
            callback));
        Assert.False(WebAuthenticationBrokerBackendSupport.IsCallbackUri(
            new Uri("custom://example.com/callback/path"),
            callback));
    }

    [Fact]
    public async Task AuthenticateWithDialogAsync_ReturnsImmediateSuccess_WhenRequestAlreadyMatchesCallback()
    {
        var callback = new Uri("https://example.com/callback?state=1#token=seed");
        var backend = new TestDialogBackend();

        var result = await WebAuthenticationBrokerBackendSupport.AuthenticateWithDialogAsync(
            backend,
            callback,
            callback);

        Assert.Equal(WebAuthenticationStatus.Success, result.ResponseStatus);
        Assert.Equal(callback.AbsoluteUri, result.ResponseData);
        Assert.False(backend.ShowCalled);
        Assert.True(backend.DisposeCalled);
    }

    [Fact]
    public async Task AuthenticateWithDialogAsync_CompletesFromCallbackNavigation_AndClosesDialog()
    {
        var callback = new Uri("https://example.com/callback?code=123#state=seed");
        var backend = new TestDialogBackend((dialog, requestUri) =>
        {
            _ = requestUri;
            dialog.RaiseNavigationStarted(callback);
        });

        var result = await WebAuthenticationBrokerBackendSupport.AuthenticateWithDialogAsync(
            backend,
            new Uri("https://example.com/auth"),
            callback,
            WebAuthenticationOptions.UseTitle);

        Assert.Equal(WebAuthenticationStatus.Success, result.ResponseStatus);
        Assert.Equal(callback.AbsoluteUri, result.ResponseData);
        Assert.True(backend.ShowCalled);
        Assert.True(backend.CloseCalled);
        Assert.True(backend.DisposeCalled);
    }

    [Fact]
    public async Task AuthenticateWithDialogAsync_CompletesFromCallbackNavigationCompleted_AndClosesDialog()
    {
        var callback = new Uri("https://example.com/callback?code=123#state=seed");
        var backend = new TestDialogBackend((dialog, requestUri) =>
        {
            _ = requestUri;
            dialog.RaiseNavigationCompleted(callback);
        });

        var result = await WebAuthenticationBrokerBackendSupport.AuthenticateWithDialogAsync(
            backend,
            new Uri("https://example.com/auth"),
            callback,
            WebAuthenticationOptions.UseTitle);

        Assert.Equal(WebAuthenticationStatus.Success, result.ResponseStatus);
        Assert.Equal(callback.AbsoluteUri, result.ResponseData);
        Assert.True(backend.ShowCalled);
        Assert.True(backend.CloseCalled);
        Assert.True(backend.DisposeCalled);
    }

    [Fact]
    public async Task AuthenticateWithDialogAsync_CompletesFromScriptObservedCallback_WhenNavigationEventsAreMissing()
    {
        var callback = new Uri("https://example.com/callback?code=123#state=seed");
        TestDialogBackend? backend = null;
        backend = new TestDialogBackend(
            navigateHandler: (dialog, requestUri) =>
            {
                _ = requestUri;
                dialog.CurrentUrl = callback;
            },
            executeScriptHandler: (script, cancellationToken) =>
            {
                _ = script;
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult<string?>(
                    backend!.CurrentUrl is not null && WebAuthenticationBrokerBackendSupport.IsCallbackUri(backend.CurrentUrl, callback)
                        ? $"\"{callback.AbsoluteUri}\""
                        : "\"https://example.com/auth\"");
            });

        var result = await WebAuthenticationBrokerBackendSupport.AuthenticateWithDialogAsync(
            backend,
            new Uri("https://example.com/auth"),
            callback,
            WebAuthenticationOptions.UseTitle);

        Assert.Equal(WebAuthenticationStatus.Success, result.ResponseStatus);
        Assert.Equal(callback.AbsoluteUri, result.ResponseData);
        Assert.True(backend.ShowCalled);
        Assert.True(backend.CloseCalled);
        Assert.True(backend.DisposeCalled);
    }

    [Fact]
    public async Task AuthenticateWithDialogAsync_DoesNotProbeScriptBeforeShowingDialog()
    {
        var callback = new Uri("https://example.com/callback?code=123#state=seed");
        var probedBeforeShow = false;
        TestDialogBackend? backend = null;
        backend = new TestDialogBackend(
            navigateHandler: (dialog, requestUri) =>
            {
                _ = requestUri;
                dialog.CurrentUrl = callback;
            },
            executeScriptHandler: (script, cancellationToken) =>
            {
                _ = script;
                cancellationToken.ThrowIfCancellationRequested();

                if (!backend!.ShowCalled)
                {
                    probedBeforeShow = true;
                }

                var currentUrl = backend.CurrentUrl?.AbsoluteUri ?? "about:blank";
                return Task.FromResult<string?>($"\"{currentUrl}\"");
            });

        var result = await WebAuthenticationBrokerBackendSupport.AuthenticateWithDialogAsync(
            backend,
            new Uri("https://example.com/auth"),
            callback,
            WebAuthenticationOptions.UseTitle);

        Assert.Equal(WebAuthenticationStatus.Success, result.ResponseStatus);
        Assert.False(probedBeforeShow);
        Assert.True(backend.ShowCalled);
        Assert.True(backend.CloseCalled);
        Assert.True(backend.DisposeCalled);
    }

    [Fact]
    public async Task AuthenticateWithDialogAsync_ReturnsUserCancel_WhenDialogClosesBeforeCallback()
    {
        var backend = new TestDialogBackend((dialog, requestUri) =>
        {
            _ = requestUri;
            dialog.Close();
        });

        var result = await WebAuthenticationBrokerBackendSupport.AuthenticateWithDialogAsync(
            backend,
            new Uri("https://example.com/auth"),
            new Uri("https://example.com/callback"));

        Assert.Equal(WebAuthenticationStatus.UserCancel, result.ResponseStatus);
        Assert.True(backend.CloseCalled);
        Assert.True(backend.DisposeCalled);
    }

    [Fact]
    public async Task AuthenticateWithDialogAsync_ReturnsRuntimeUnavailable_WhenDialogInitializationFails()
    {
        var backend = new TestDialogBackend(
            navigateHandler: static (_, _) => throw new InvalidOperationException("init failed"));

        var result = await WebAuthenticationBrokerBackendSupport.AuthenticateWithDialogAsync(
            backend,
            new Uri("https://example.com/auth"),
            new Uri("https://example.com/callback"));

        Assert.Equal(WebAuthenticationStatus.ErrorHttp, result.ResponseStatus);
        Assert.Equal(WebAuthenticationBrokerBackendSupport.NotImplementedError, result.ResponseErrorDetail);
        Assert.True(backend.ShowCalled);
        Assert.True(backend.DisposeCalled);
    }

    [Theory]
    [InlineData("https://app.example.com/callback", "https://app.example.com", true)]
    [InlineData("https://app.example.com/callback", "https://other.example.com", false)]
    [InlineData("http://app.example.com/callback", "http://app.example.com", true)]
    [InlineData("custom://callback", "https://app.example.com", false)]
    public void BrowserInspectableHttpCallback_RequiresReadableOrigin(
        string callback,
        string inspectableOrigin,
        bool expected)
    {
        var result = BrowserWebAuthenticationBrokerBackend.IsInspectableHttpCallback(
            new Uri(callback),
            new Uri(inspectableOrigin));

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task InteractivePlatformBackends_ReturnError_WhenRuntimeIsUnavailable()
    {
        var requestUri = new Uri("https://example.com/auth");
        var callbackUri = new Uri("https://example.com/callback");
        var backendFactories = new List<Func<IWebAuthenticationBrokerBackend>>
        {
            static () => new IOSWebAuthenticationBrokerBackend(),
            static () => new AndroidWebAuthenticationBrokerBackend(),
            static () => new BrowserWebAuthenticationBrokerBackend(),
        };

        if (!OperatingSystem.IsWindows())
        {
            backendFactories.Add(static () => new WindowsWebAuthenticationBrokerBackend());
        }

        if (!OperatingSystem.IsLinux())
        {
            backendFactories.Add(static () => new LinuxWebAuthenticationBrokerBackend());
        }

        if (!OperatingSystem.IsMacOS())
        {
            backendFactories.Add(static () => new MacOSWebAuthenticationBrokerBackend());
        }

        foreach (var createBackend in backendFactories)
        {
            using var backend = createBackend();

            var result = await backend.AuthenticateAsync(requestUri, callbackUri);

            Assert.Equal(WebAuthenticationStatus.ErrorHttp, result.ResponseStatus);
            Assert.Equal(WebAuthenticationBrokerBackendSupport.NotImplementedError, result.ResponseErrorDetail);
        }
    }

    private sealed class TestDialogBackend : INativeWebDialogBackend
    {
        private readonly Action<TestDialogBackend, Uri>? _navigateHandler;
        private readonly Func<string, CancellationToken, Task<string?>>? _executeScriptHandler;

        public TestDialogBackend(
            Action<TestDialogBackend, Uri>? navigateHandler = null,
            Func<string, CancellationToken, Task<string?>>? executeScriptHandler = null)
        {
            _navigateHandler = navigateHandler;
            _executeScriptHandler = executeScriptHandler;
            Platform = NativeWebViewPlatform.Windows;
            Features = new WebViewPlatformFeatures(
                NativeWebViewPlatform.Windows,
                NativeWebViewFeature.Dialog |
                NativeWebViewFeature.ScriptExecution |
                NativeWebViewFeature.WebMessageChannel);
        }

        public NativeWebViewPlatform Platform { get; }

        public IWebViewPlatformFeatures Features { get; }

        public bool IsVisible { get; private set; }

        public Uri? CurrentUrl { get; set; }

        public bool CanGoBack => false;

        public bool CanGoForward => false;

        public bool IsDevToolsEnabled { get; set; }

        public bool IsContextMenuEnabled { get; set; }

        public bool IsStatusBarEnabled { get; set; }

        public bool IsZoomControlEnabled { get; set; }

        public double ZoomFactor => 1.0;

        public string? HeaderString => null;

        public string? UserAgentString => null;

        public bool ShowCalled { get; private set; }

        public bool CloseCalled { get; private set; }

        public bool DisposeCalled { get; private set; }

        public event EventHandler<EventArgs>? Shown;

        public event EventHandler<EventArgs>? Closed;

        public event EventHandler<NativeWebViewNavigationStartedEventArgs>? NavigationStarted;

#pragma warning disable CS0067
        public event EventHandler<NativeWebViewNavigationCompletedEventArgs>? NavigationCompleted;

        public event EventHandler<NativeWebViewMessageReceivedEventArgs>? WebMessageReceived;

        public event EventHandler<NativeWebViewNewWindowRequestedEventArgs>? NewWindowRequested;

        public event EventHandler<NativeWebViewResourceRequestedEventArgs>? WebResourceRequested;

        public event EventHandler<NativeWebViewContextMenuRequestedEventArgs>? ContextMenuRequested;
#pragma warning restore CS0067

        public void Show(NativeWebDialogShowOptions? options = null)
        {
            _ = options;
            ShowCalled = true;
            IsVisible = true;
            Shown?.Invoke(this, EventArgs.Empty);
        }

        public void Close()
        {
            CloseCalled = true;

            if (!IsVisible)
            {
                return;
            }

            IsVisible = false;
            Closed?.Invoke(this, EventArgs.Empty);
        }

        public void Move(double left, double top)
        {
            _ = left;
            _ = top;
        }

        public void Resize(double width, double height)
        {
            _ = width;
            _ = height;
        }

        public void Navigate(string url)
        {
            Navigate(new Uri(url));
        }

        public void Navigate(Uri uri)
        {
            CurrentUrl = uri;
            _navigateHandler?.Invoke(this, uri);
        }

        public void Reload()
        {
        }

        public void Stop()
        {
        }

        public void GoBack()
        {
        }

        public void GoForward()
        {
        }

        public Task<string?> ExecuteScriptAsync(string script, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_executeScriptHandler is not null)
            {
                return _executeScriptHandler(script, cancellationToken);
            }

            _ = script;
            return Task.FromResult<string?>("null");
        }

        public Task PostWebMessageAsJsonAsync(string message, CancellationToken cancellationToken = default)
        {
            _ = message;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task PostWebMessageAsStringAsync(string message, CancellationToken cancellationToken = default)
        {
            _ = message;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public void OpenDevToolsWindow()
        {
        }

        public Task<NativeWebViewPrintResult> PrintAsync(NativeWebViewPrintSettings? settings = null, CancellationToken cancellationToken = default)
        {
            _ = settings;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new NativeWebViewPrintResult(NativeWebViewPrintStatus.NotSupported));
        }

        public Task<bool> ShowPrintUiAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(false);
        }

        public void SetZoomFactor(double zoomFactor)
        {
            _ = zoomFactor;
        }

        public void SetUserAgent(string? userAgent)
        {
            _ = userAgent;
        }

        public void SetHeader(string? header)
        {
            _ = header;
        }

        public void Dispose()
        {
            DisposeCalled = true;
            IsVisible = false;
        }

        public void RaiseNavigationStarted(Uri uri)
        {
            var args = new NativeWebViewNavigationStartedEventArgs(uri, isRedirected: false);
            NavigationStarted?.Invoke(this, args);
        }

        public void RaiseNavigationCompleted(Uri uri)
        {
            NavigationCompleted?.Invoke(this, new NativeWebViewNavigationCompletedEventArgs(uri, isSuccess: true, httpStatusCode: 200));
        }
    }
}
