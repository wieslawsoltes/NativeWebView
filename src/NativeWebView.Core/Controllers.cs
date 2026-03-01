using System.Diagnostics.CodeAnalysis;

namespace NativeWebView.Core;

public enum NativeWebComponentState
{
    Created = 0,
    Initializing,
    Ready,
    Disposed,
}

public enum WebAuthenticationBrokerState
{
    Ready = 0,
    Authenticating,
    Disposed,
}

public sealed class NativeWebViewController : IDisposable
{
    private readonly INativeWebViewBackend _backend;
    private readonly SemaphoreSlim _initializeGate = new(1, 1);
    private readonly object _taskGate = new();
    private Task? _initializeTask;

    private int _state = (int)NativeWebComponentState.Created;
    private int _disposed;

    private Uri? _currentUrl;
    private bool _canGoBack;
    private bool _canGoForward;
    private bool _hasNavigationSnapshot;

    public NativeWebViewController(INativeWebViewBackend backend)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));

        AttachBackendEvents();
        SyncNavigationStateFromBackend();

        if (_backend.IsInitialized)
        {
            SetState(NativeWebComponentState.Ready);
        }
    }

    public NativeWebViewPlatform Platform => _backend.Platform;

    public IWebViewPlatformFeatures Features => _backend.Features;

    public NativeWebComponentState State => (NativeWebComponentState)Volatile.Read(ref _state);

    public bool IsInitialized => _backend.IsInitialized;

    public Uri? CurrentUrl => _hasNavigationSnapshot ? _currentUrl : _backend.CurrentUrl;

    public bool CanGoBack => _hasNavigationSnapshot ? _canGoBack : _backend.CanGoBack;

    public bool CanGoForward => _hasNavigationSnapshot ? _canGoForward : _backend.CanGoForward;

    public bool IsDevToolsEnabled
    {
        get => _backend.IsDevToolsEnabled;
        set
        {
            ThrowIfDisposed();
            _backend.IsDevToolsEnabled = value;
        }
    }

    public bool IsContextMenuEnabled
    {
        get => _backend.IsContextMenuEnabled;
        set
        {
            ThrowIfDisposed();
            _backend.IsContextMenuEnabled = value;
        }
    }

    public bool IsStatusBarEnabled
    {
        get => _backend.IsStatusBarEnabled;
        set
        {
            ThrowIfDisposed();
            _backend.IsStatusBarEnabled = value;
        }
    }

    public bool IsZoomControlEnabled
    {
        get => _backend.IsZoomControlEnabled;
        set
        {
            ThrowIfDisposed();
            _backend.IsZoomControlEnabled = value;
        }
    }

    public double ZoomFactor => _backend.ZoomFactor;

    public string? HeaderString => _backend.HeaderString;

    public string? UserAgentString => _backend.UserAgentString;

    public event EventHandler<CoreWebViewInitializedEventArgs>? CoreWebView2Initialized;

    public event EventHandler<NativeWebViewNavigationStartedEventArgs>? NavigationStarted;

    public event EventHandler<NativeWebViewNavigationCompletedEventArgs>? NavigationCompleted;

    public event EventHandler<NativeWebViewMessageReceivedEventArgs>? WebMessageReceived;

    public event EventHandler<NativeWebViewOpenDevToolsRequestedEventArgs>? OpenDevToolsRequested;

    public event EventHandler<NativeWebViewDestroyRequestedEventArgs>? DestroyRequested;

    public event EventHandler<NativeWebViewRequestCustomChromeEventArgs>? RequestCustomChrome;

    public event EventHandler<NativeWebViewRequestParentWindowPositionEventArgs>? RequestParentWindowPosition;

    public event EventHandler<NativeWebViewBeginMoveDragEventArgs>? BeginMoveDrag;

    public event EventHandler<NativeWebViewBeginResizeDragEventArgs>? BeginResizeDrag;

    public event EventHandler<NativeWebViewNewWindowRequestedEventArgs>? NewWindowRequested;

    public event EventHandler<NativeWebViewResourceRequestedEventArgs>? WebResourceRequested;

    public event EventHandler<NativeWebViewContextMenuRequestedEventArgs>? ContextMenuRequested;

    public event EventHandler<NativeWebViewNavigationHistoryChangedEventArgs>? NavigationHistoryChanged;

    public event EventHandler<CoreWebViewEnvironmentRequestedEventArgs>? CoreWebView2EnvironmentRequested;

    public event EventHandler<CoreWebViewControllerOptionsRequestedEventArgs>? CoreWebView2ControllerOptionsRequested;

    public ValueTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return new ValueTask(EnsureInitializedCoreAsync(cancellationToken));
    }

    public void Navigate(string url)
    {
        ThrowIfDisposed();
        _backend.Navigate(url);
    }

    public void Navigate(Uri uri)
    {
        ThrowIfDisposed();
        _backend.Navigate(uri);
    }

    public void Reload()
    {
        ThrowIfDisposed();
        _backend.Reload();
    }

    public void Stop()
    {
        ThrowIfDisposed();
        _backend.Stop();
    }

    public void GoBack()
    {
        ThrowIfDisposed();
        _backend.GoBack();
    }

    public void GoForward()
    {
        ThrowIfDisposed();
        _backend.GoForward();
    }

    public async Task<string?> ExecuteScriptAsync(string script, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await EnsureInitializedCoreAsync(cancellationToken).ConfigureAwait(false);
        return await _backend.ExecuteScriptAsync(script, cancellationToken).ConfigureAwait(false);
    }

    public async Task PostWebMessageAsJsonAsync(string message, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await EnsureInitializedCoreAsync(cancellationToken).ConfigureAwait(false);
        await _backend.PostWebMessageAsJsonAsync(message, cancellationToken).ConfigureAwait(false);
    }

    public async Task PostWebMessageAsStringAsync(string message, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await EnsureInitializedCoreAsync(cancellationToken).ConfigureAwait(false);
        await _backend.PostWebMessageAsStringAsync(message, cancellationToken).ConfigureAwait(false);
    }

    public void OpenDevToolsWindow()
    {
        ThrowIfDisposed();
        _backend.OpenDevToolsWindow();
    }

    public async Task<NativeWebViewPrintResult> PrintAsync(
        NativeWebViewPrintSettings? settings = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await EnsureInitializedCoreAsync(cancellationToken).ConfigureAwait(false);
        return await _backend.PrintAsync(settings, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> ShowPrintUiAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await EnsureInitializedCoreAsync(cancellationToken).ConfigureAwait(false);
        return await _backend.ShowPrintUiAsync(cancellationToken).ConfigureAwait(false);
    }

    public void SetZoomFactor(double zoomFactor)
    {
        ThrowIfDisposed();
        _backend.SetZoomFactor(zoomFactor);
    }

    public void SetUserAgent(string? userAgent)
    {
        ThrowIfDisposed();
        _backend.SetUserAgent(userAgent);
    }

    public void SetHeader(string? header)
    {
        ThrowIfDisposed();
        _backend.SetHeader(header);
    }

    public bool TryGetCommandManager(out INativeWebViewCommandManager? commandManager)
    {
        ThrowIfDisposed();
        return _backend.TryGetCommandManager(out commandManager);
    }

    public bool TryGetCookieManager(out INativeWebViewCookieManager? cookieManager)
    {
        ThrowIfDisposed();
        return _backend.TryGetCookieManager(out cookieManager);
    }

    public bool TryGetBackend<TBackend>([NotNullWhen(true)] out TBackend? backend)
        where TBackend : class
    {
        ThrowIfDisposed();
        backend = _backend as TBackend;
        return backend is not null;
    }

    public void MoveFocus(NativeWebViewFocusMoveDirection direction)
    {
        ThrowIfDisposed();
        _backend.MoveFocus(direction);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        SetState(NativeWebComponentState.Disposed);
        DetachBackendEvents();

        _backend.Dispose();
    }

    private async Task EnsureInitializedCoreAsync(CancellationToken cancellationToken)
    {
        if (State == NativeWebComponentState.Ready)
        {
            return;
        }

        await _initializeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        Task initializeTask;

        try
        {
            ThrowIfDisposed();

            if (State == NativeWebComponentState.Ready)
            {
                return;
            }

            lock (_taskGate)
            {
                _initializeTask ??= InitializeBackendCoreAsync();
                initializeTask = _initializeTask;
            }
        }
        finally
        {
            _initializeGate.Release();
        }

        await initializeTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task InitializeBackendCoreAsync()
    {
        SetState(NativeWebComponentState.Initializing);

        try
        {
            await _backend.InitializeAsync(CancellationToken.None).ConfigureAwait(false);

            if (IsDisposed)
            {
                return;
            }

            if (!_backend.IsInitialized)
            {
                throw new InvalidOperationException("Backend initialization completed without setting IsInitialized.");
            }

            SyncNavigationStateFromBackend();

            if (IsDisposed)
            {
                return;
            }

            SetState(NativeWebComponentState.Ready);
        }
        catch
        {
            lock (_taskGate)
            {
                _initializeTask = null;
            }

            if (State != NativeWebComponentState.Disposed)
            {
                SetState(NativeWebComponentState.Created);
            }

            throw;
        }
    }

    private void AttachBackendEvents()
    {
        _backend.CoreWebView2Initialized += OnCoreWebView2Initialized;
        _backend.NavigationStarted += OnNavigationStarted;
        _backend.NavigationCompleted += OnNavigationCompleted;
        _backend.WebMessageReceived += OnWebMessageReceived;
        _backend.OpenDevToolsRequested += OnOpenDevToolsRequested;
        _backend.DestroyRequested += OnDestroyRequested;
        _backend.RequestCustomChrome += OnRequestCustomChrome;
        _backend.RequestParentWindowPosition += OnRequestParentWindowPosition;
        _backend.BeginMoveDrag += OnBeginMoveDrag;
        _backend.BeginResizeDrag += OnBeginResizeDrag;
        _backend.NewWindowRequested += OnNewWindowRequested;
        _backend.WebResourceRequested += OnWebResourceRequested;
        _backend.ContextMenuRequested += OnContextMenuRequested;
        _backend.NavigationHistoryChanged += OnNavigationHistoryChanged;
        _backend.CoreWebView2EnvironmentRequested += OnCoreWebView2EnvironmentRequested;
        _backend.CoreWebView2ControllerOptionsRequested += OnCoreWebView2ControllerOptionsRequested;
    }

    private void DetachBackendEvents()
    {
        _backend.CoreWebView2Initialized -= OnCoreWebView2Initialized;
        _backend.NavigationStarted -= OnNavigationStarted;
        _backend.NavigationCompleted -= OnNavigationCompleted;
        _backend.WebMessageReceived -= OnWebMessageReceived;
        _backend.OpenDevToolsRequested -= OnOpenDevToolsRequested;
        _backend.DestroyRequested -= OnDestroyRequested;
        _backend.RequestCustomChrome -= OnRequestCustomChrome;
        _backend.RequestParentWindowPosition -= OnRequestParentWindowPosition;
        _backend.BeginMoveDrag -= OnBeginMoveDrag;
        _backend.BeginResizeDrag -= OnBeginResizeDrag;
        _backend.NewWindowRequested -= OnNewWindowRequested;
        _backend.WebResourceRequested -= OnWebResourceRequested;
        _backend.ContextMenuRequested -= OnContextMenuRequested;
        _backend.NavigationHistoryChanged -= OnNavigationHistoryChanged;
        _backend.CoreWebView2EnvironmentRequested -= OnCoreWebView2EnvironmentRequested;
        _backend.CoreWebView2ControllerOptionsRequested -= OnCoreWebView2ControllerOptionsRequested;
    }

    private void OnCoreWebView2Initialized(object? sender, CoreWebViewInitializedEventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        SetState(e.IsSuccess ? NativeWebComponentState.Ready : NativeWebComponentState.Created);
        CoreWebView2Initialized?.Invoke(this, e);
    }

    private void OnNavigationStarted(object? sender, NativeWebViewNavigationStartedEventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        NavigationStarted?.Invoke(this, e);

        if (!e.Cancel)
        {
            _hasNavigationSnapshot = true;
            _currentUrl = e.Uri;
        }
    }

    private void OnNavigationCompleted(object? sender, NativeWebViewNavigationCompletedEventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        if (e.IsSuccess)
        {
            _hasNavigationSnapshot = true;
            _currentUrl = e.Uri;
        }

        SyncNavigationStateFromBackend();
        NavigationCompleted?.Invoke(this, e);
    }

    private void OnWebMessageReceived(object? sender, NativeWebViewMessageReceivedEventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        WebMessageReceived?.Invoke(this, e);
    }

    private void OnOpenDevToolsRequested(object? sender, NativeWebViewOpenDevToolsRequestedEventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        OpenDevToolsRequested?.Invoke(this, e);
    }

    private void OnDestroyRequested(object? sender, NativeWebViewDestroyRequestedEventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        DestroyRequested?.Invoke(this, e);
    }

    private void OnRequestCustomChrome(object? sender, NativeWebViewRequestCustomChromeEventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        RequestCustomChrome?.Invoke(this, e);
    }

    private void OnRequestParentWindowPosition(object? sender, NativeWebViewRequestParentWindowPositionEventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        RequestParentWindowPosition?.Invoke(this, e);
    }

    private void OnBeginMoveDrag(object? sender, NativeWebViewBeginMoveDragEventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        BeginMoveDrag?.Invoke(this, e);
    }

    private void OnBeginResizeDrag(object? sender, NativeWebViewBeginResizeDragEventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        BeginResizeDrag?.Invoke(this, e);
    }

    private void OnNewWindowRequested(object? sender, NativeWebViewNewWindowRequestedEventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        NewWindowRequested?.Invoke(this, e);
    }

    private void OnWebResourceRequested(object? sender, NativeWebViewResourceRequestedEventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        WebResourceRequested?.Invoke(this, e);
    }

    private void OnContextMenuRequested(object? sender, NativeWebViewContextMenuRequestedEventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        ContextMenuRequested?.Invoke(this, e);
    }

    private void OnNavigationHistoryChanged(object? sender, NativeWebViewNavigationHistoryChangedEventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        _hasNavigationSnapshot = true;
        _canGoBack = e.CanGoBack;
        _canGoForward = e.CanGoForward;

        NavigationHistoryChanged?.Invoke(this, e);
    }

    private void OnCoreWebView2EnvironmentRequested(object? sender, CoreWebViewEnvironmentRequestedEventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        CoreWebView2EnvironmentRequested?.Invoke(this, e);
    }

    private void OnCoreWebView2ControllerOptionsRequested(object? sender, CoreWebViewControllerOptionsRequestedEventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        CoreWebView2ControllerOptionsRequested?.Invoke(this, e);
    }

    private void SyncNavigationStateFromBackend()
    {
        _hasNavigationSnapshot = true;
        _currentUrl = _backend.CurrentUrl;
        _canGoBack = _backend.CanGoBack;
        _canGoForward = _backend.CanGoForward;
    }

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    private void ThrowIfDisposed()
    {
        if (IsDisposed)
        {
            throw new ObjectDisposedException(nameof(NativeWebViewController));
        }
    }

    private void SetState(NativeWebComponentState state)
    {
        Volatile.Write(ref _state, (int)state);
    }
}

public sealed class NativeWebDialogController : IDisposable
{
    private readonly INativeWebDialogBackend _backend;
    private int _state = (int)NativeWebComponentState.Ready;
    private int _disposed;

    private Uri? _currentUrl;
    private bool _canGoBack;
    private bool _canGoForward;
    private bool _isVisible;
    private bool _hasNavigationSnapshot;

    public NativeWebDialogController(INativeWebDialogBackend backend)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));

        AttachBackendEvents();
        SyncStateFromBackend();
    }

    public NativeWebViewPlatform Platform => _backend.Platform;

    public IWebViewPlatformFeatures Features => _backend.Features;

    public NativeWebComponentState State => (NativeWebComponentState)Volatile.Read(ref _state);

    public bool IsVisible => _isVisible;

    public Uri? CurrentUrl => _hasNavigationSnapshot ? _currentUrl : _backend.CurrentUrl;

    public bool CanGoBack => _hasNavigationSnapshot ? _canGoBack : _backend.CanGoBack;

    public bool CanGoForward => _hasNavigationSnapshot ? _canGoForward : _backend.CanGoForward;

    public bool IsDevToolsEnabled
    {
        get => _backend.IsDevToolsEnabled;
        set
        {
            ThrowIfDisposed();
            _backend.IsDevToolsEnabled = value;
        }
    }

    public bool IsContextMenuEnabled
    {
        get => _backend.IsContextMenuEnabled;
        set
        {
            ThrowIfDisposed();
            _backend.IsContextMenuEnabled = value;
        }
    }

    public bool IsStatusBarEnabled
    {
        get => _backend.IsStatusBarEnabled;
        set
        {
            ThrowIfDisposed();
            _backend.IsStatusBarEnabled = value;
        }
    }

    public bool IsZoomControlEnabled
    {
        get => _backend.IsZoomControlEnabled;
        set
        {
            ThrowIfDisposed();
            _backend.IsZoomControlEnabled = value;
        }
    }

    public double ZoomFactor => _backend.ZoomFactor;

    public string? HeaderString => _backend.HeaderString;

    public string? UserAgentString => _backend.UserAgentString;

    public event EventHandler<EventArgs>? Shown;

    public event EventHandler<EventArgs>? Closed;

    public event EventHandler<NativeWebViewNavigationStartedEventArgs>? NavigationStarted;

    public event EventHandler<NativeWebViewNavigationCompletedEventArgs>? NavigationCompleted;

    public event EventHandler<NativeWebViewMessageReceivedEventArgs>? WebMessageReceived;

    public event EventHandler<NativeWebViewNewWindowRequestedEventArgs>? NewWindowRequested;

    public event EventHandler<NativeWebViewResourceRequestedEventArgs>? WebResourceRequested;

    public event EventHandler<NativeWebViewContextMenuRequestedEventArgs>? ContextMenuRequested;

    public void Show(NativeWebDialogShowOptions? options = null)
    {
        ThrowIfDisposed();
        _backend.Show(options);
    }

    public void Close()
    {
        ThrowIfDisposed();
        _backend.Close();
    }

    public void Move(double left, double top)
    {
        ThrowIfDisposed();
        _backend.Move(left, top);
    }

    public void Resize(double width, double height)
    {
        ThrowIfDisposed();
        _backend.Resize(width, height);
    }

    public void Navigate(string url)
    {
        ThrowIfDisposed();
        _backend.Navigate(url);
    }

    public void Navigate(Uri uri)
    {
        ThrowIfDisposed();
        _backend.Navigate(uri);
    }

    public void Reload()
    {
        ThrowIfDisposed();
        _backend.Reload();
    }

    public void Stop()
    {
        ThrowIfDisposed();
        _backend.Stop();
    }

    public void GoBack()
    {
        ThrowIfDisposed();
        _backend.GoBack();
    }

    public void GoForward()
    {
        ThrowIfDisposed();
        _backend.GoForward();
    }

    public Task<string?> ExecuteScriptAsync(string script, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _backend.ExecuteScriptAsync(script, cancellationToken);
    }

    public Task PostWebMessageAsJsonAsync(string message, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _backend.PostWebMessageAsJsonAsync(message, cancellationToken);
    }

    public Task PostWebMessageAsStringAsync(string message, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _backend.PostWebMessageAsStringAsync(message, cancellationToken);
    }

    public void OpenDevToolsWindow()
    {
        ThrowIfDisposed();
        _backend.OpenDevToolsWindow();
    }

    public Task<NativeWebViewPrintResult> PrintAsync(NativeWebViewPrintSettings? settings = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _backend.PrintAsync(settings, cancellationToken);
    }

    public Task<bool> ShowPrintUiAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _backend.ShowPrintUiAsync(cancellationToken);
    }

    public void SetZoomFactor(double zoomFactor)
    {
        ThrowIfDisposed();
        _backend.SetZoomFactor(zoomFactor);
    }

    public void SetUserAgent(string? userAgent)
    {
        ThrowIfDisposed();
        _backend.SetUserAgent(userAgent);
    }

    public void SetHeader(string? header)
    {
        ThrowIfDisposed();
        _backend.SetHeader(header);
    }

    public bool TryGetBackend<TBackend>([NotNullWhen(true)] out TBackend? backend)
        where TBackend : class
    {
        ThrowIfDisposed();
        backend = _backend as TBackend;
        return backend is not null;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Volatile.Write(ref _state, (int)NativeWebComponentState.Disposed);
        DetachBackendEvents();

        _backend.Dispose();
    }

    private void AttachBackendEvents()
    {
        _backend.Shown += OnShown;
        _backend.Closed += OnClosed;
        _backend.NavigationStarted += OnNavigationStarted;
        _backend.NavigationCompleted += OnNavigationCompleted;
        _backend.WebMessageReceived += OnWebMessageReceived;
        _backend.NewWindowRequested += OnNewWindowRequested;
        _backend.WebResourceRequested += OnWebResourceRequested;
        _backend.ContextMenuRequested += OnContextMenuRequested;
    }

    private void DetachBackendEvents()
    {
        _backend.Shown -= OnShown;
        _backend.Closed -= OnClosed;
        _backend.NavigationStarted -= OnNavigationStarted;
        _backend.NavigationCompleted -= OnNavigationCompleted;
        _backend.WebMessageReceived -= OnWebMessageReceived;
        _backend.NewWindowRequested -= OnNewWindowRequested;
        _backend.WebResourceRequested -= OnWebResourceRequested;
        _backend.ContextMenuRequested -= OnContextMenuRequested;
    }

    private void OnShown(object? sender, EventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        _isVisible = true;
        Shown?.Invoke(this, e);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        _isVisible = false;
        Closed?.Invoke(this, e);
    }

    private void OnNavigationStarted(object? sender, NativeWebViewNavigationStartedEventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        NavigationStarted?.Invoke(this, e);

        if (!e.Cancel)
        {
            _hasNavigationSnapshot = true;
            _currentUrl = e.Uri;
        }
    }

    private void OnNavigationCompleted(object? sender, NativeWebViewNavigationCompletedEventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        if (e.IsSuccess)
        {
            _hasNavigationSnapshot = true;
            _currentUrl = e.Uri;
        }

        SyncNavigationStateFromBackend();
        NavigationCompleted?.Invoke(this, e);
    }

    private void OnWebMessageReceived(object? sender, NativeWebViewMessageReceivedEventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        WebMessageReceived?.Invoke(this, e);
    }

    private void OnNewWindowRequested(object? sender, NativeWebViewNewWindowRequestedEventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        NewWindowRequested?.Invoke(this, e);
    }

    private void OnWebResourceRequested(object? sender, NativeWebViewResourceRequestedEventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        WebResourceRequested?.Invoke(this, e);
    }

    private void OnContextMenuRequested(object? sender, NativeWebViewContextMenuRequestedEventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        ContextMenuRequested?.Invoke(this, e);
    }

    private void SyncStateFromBackend()
    {
        _isVisible = _backend.IsVisible;
        SyncNavigationStateFromBackend();
    }

    private void SyncNavigationStateFromBackend()
    {
        _hasNavigationSnapshot = true;
        _currentUrl = _backend.CurrentUrl;
        _canGoBack = _backend.CanGoBack;
        _canGoForward = _backend.CanGoForward;
    }

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    private void ThrowIfDisposed()
    {
        if (IsDisposed)
        {
            throw new ObjectDisposedException(nameof(NativeWebDialogController));
        }
    }
}

public sealed class WebAuthenticationBrokerController : IDisposable
{
    private readonly IWebAuthenticationBrokerBackend _backend;
    private readonly SemaphoreSlim _authenticationGate = new(1, 1);

    private int _state = (int)WebAuthenticationBrokerState.Ready;
    private int _disposed;

    public WebAuthenticationBrokerController(IWebAuthenticationBrokerBackend backend)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    public NativeWebViewPlatform Platform => _backend.Platform;

    public IWebViewPlatformFeatures Features => _backend.Features;

    public WebAuthenticationBrokerState State => (WebAuthenticationBrokerState)Volatile.Read(ref _state);

    public async Task<WebAuthenticationResult> AuthenticateAsync(
        Uri requestUri,
        Uri callbackUri,
        WebAuthenticationOptions options = WebAuthenticationOptions.None,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateAuthenticationUris(requestUri, callbackUri);

        await _authenticationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            Volatile.Write(ref _state, (int)WebAuthenticationBrokerState.Authenticating);

            var result = await _backend.AuthenticateAsync(
                requestUri,
                callbackUri,
                options,
                cancellationToken).ConfigureAwait(false);

            return result;
        }
        finally
        {
            if (!IsDisposed)
            {
                Volatile.Write(ref _state, (int)WebAuthenticationBrokerState.Ready);
            }

            _authenticationGate.Release();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Volatile.Write(ref _state, (int)WebAuthenticationBrokerState.Disposed);
        _backend.Dispose();
    }

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    private void ThrowIfDisposed()
    {
        if (IsDisposed)
        {
            throw new ObjectDisposedException(nameof(WebAuthenticationBrokerController));
        }
    }

    private static void ValidateAuthenticationUris(Uri requestUri, Uri callbackUri)
    {
        ArgumentNullException.ThrowIfNull(requestUri);
        ArgumentNullException.ThrowIfNull(callbackUri);

        if (!requestUri.IsAbsoluteUri)
        {
            throw new ArgumentException("Request URI must be absolute.", nameof(requestUri));
        }

        if (!callbackUri.IsAbsoluteUri)
        {
            throw new ArgumentException("Callback URI must be absolute.", nameof(callbackUri));
        }

        if (!IsHttpScheme(requestUri.Scheme))
        {
            throw new ArgumentException("Request URI scheme must be http or https.", nameof(requestUri));
        }

        if (IsUnsafeCallbackScheme(callbackUri.Scheme))
        {
            throw new ArgumentException("Callback URI scheme is not allowed.", nameof(callbackUri));
        }

        if (!string.IsNullOrEmpty(requestUri.UserInfo))
        {
            throw new ArgumentException("Request URI must not include user info.", nameof(requestUri));
        }

        if (!string.IsNullOrEmpty(callbackUri.UserInfo))
        {
            throw new ArgumentException("Callback URI must not include user info.", nameof(callbackUri));
        }
    }

    private static bool IsHttpScheme(string scheme)
    {
        return string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnsafeCallbackScheme(string scheme)
    {
        return string.Equals(scheme, "javascript", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(scheme, "data", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(scheme, "file", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(scheme, "about", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(scheme, "blob", StringComparison.OrdinalIgnoreCase);
    }
}
