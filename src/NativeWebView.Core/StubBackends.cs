namespace NativeWebView.Core;

public abstract class NativeWebViewBackendStubBase : INativeWebViewBackend, INativeWebViewFrameSource
{
    private readonly List<Uri> _history = [];
    private readonly INativeWebViewCommandManager _commandManager = new NoopCommandManager();
    private readonly INativeWebViewCookieManager _cookieManager = new NoopCookieManager();
    private int _historyIndex = -1;
    private bool _disposed;
    private long _frameSequence;

    protected NativeWebViewBackendStubBase(NativeWebViewPlatform platform, IWebViewPlatformFeatures features)
    {
        Platform = platform;
        Features = features;
        IsDevToolsEnabled = features.Supports(NativeWebViewFeature.DevTools);
        IsContextMenuEnabled = features.Supports(NativeWebViewFeature.ContextMenu);
        IsStatusBarEnabled = features.Supports(NativeWebViewFeature.StatusBar);
        IsZoomControlEnabled = features.Supports(NativeWebViewFeature.ZoomControl);
        ZoomFactor = 1.0;
    }

    public NativeWebViewPlatform Platform { get; }

    public IWebViewPlatformFeatures Features { get; }

    public Uri? CurrentUrl { get; private set; }

    public bool IsInitialized { get; private set; }

    public bool CanGoBack => _historyIndex > 0;

    public bool CanGoForward => _historyIndex >= 0 && _historyIndex < _history.Count - 1;

    public bool IsDevToolsEnabled { get; set; }

    public bool IsContextMenuEnabled { get; set; }

    public bool IsStatusBarEnabled { get; set; }

    public bool IsZoomControlEnabled { get; set; }

    public double ZoomFactor { get; private set; }

    public string? HeaderString { get; private set; }

    public string? UserAgentString { get; private set; }

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
        cancellationToken.ThrowIfCancellationRequested();
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.EmbeddedView, nameof(InitializeAsync));

        EnsureInitialized();
        return ValueTask.CompletedTask;
    }

    public void Navigate(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        if (!Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var uri))
        {
            throw new ArgumentException($"Invalid URL: {url}", nameof(url));
        }

        Navigate(uri);
    }

    public void Navigate(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.EmbeddedView, nameof(Navigate));
        EnsureInitialized();

        var started = new NativeWebViewNavigationStartedEventArgs(uri, isRedirected: false);
        NavigationStarted?.Invoke(this, started);

        if (started.Cancel)
        {
            return;
        }

        if (_historyIndex < _history.Count - 1)
        {
            _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
        }

        _history.Add(uri);
        _historyIndex = _history.Count - 1;
        CurrentUrl = uri;

        NavigationCompleted?.Invoke(this, new NativeWebViewNavigationCompletedEventArgs(uri, isSuccess: true, httpStatusCode: 200));
        NavigationHistoryChanged?.Invoke(this, new NativeWebViewNavigationHistoryChangedEventArgs(CanGoBack, CanGoForward));
    }

    public void Reload()
    {
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.EmbeddedView, nameof(Reload));

        if (CurrentUrl is null)
        {
            return;
        }

        NavigationStarted?.Invoke(this, new NativeWebViewNavigationStartedEventArgs(CurrentUrl, isRedirected: false));
        NavigationCompleted?.Invoke(this, new NativeWebViewNavigationCompletedEventArgs(CurrentUrl, isSuccess: true, httpStatusCode: 200));
    }

    public void Stop()
    {
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.EmbeddedView, nameof(Stop));
    }

    public void GoBack()
    {
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.EmbeddedView, nameof(GoBack));

        if (!CanGoBack)
        {
            return;
        }

        _historyIndex--;
        CurrentUrl = _history[_historyIndex];
        NavigationCompleted?.Invoke(this, new NativeWebViewNavigationCompletedEventArgs(CurrentUrl, isSuccess: true, httpStatusCode: 200));
        NavigationHistoryChanged?.Invoke(this, new NativeWebViewNavigationHistoryChangedEventArgs(CanGoBack, CanGoForward));
    }

    public void GoForward()
    {
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.EmbeddedView, nameof(GoForward));

        if (!CanGoForward)
        {
            return;
        }

        _historyIndex++;
        CurrentUrl = _history[_historyIndex];
        NavigationCompleted?.Invoke(this, new NativeWebViewNavigationCompletedEventArgs(CurrentUrl, isSuccess: true, httpStatusCode: 200));
        NavigationHistoryChanged?.Invoke(this, new NativeWebViewNavigationHistoryChangedEventArgs(CanGoBack, CanGoForward));
    }

    public Task<string?> ExecuteScriptAsync(string script, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(script);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.ScriptExecution, nameof(ExecuteScriptAsync));

        return Task.FromResult<string?>("null");
    }

    public Task PostWebMessageAsJsonAsync(string message, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.WebMessageChannel, nameof(PostWebMessageAsJsonAsync));
        var jsonMessage = NativeWebViewBackendSupport.NormalizeJsonMessagePayload(message);

        WebMessageReceived?.Invoke(this, new NativeWebViewMessageReceivedEventArgs(message: null, json: jsonMessage));
        return Task.CompletedTask;
    }

    public Task PostWebMessageAsStringAsync(string message, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.WebMessageChannel, nameof(PostWebMessageAsStringAsync));

        WebMessageReceived?.Invoke(this, new NativeWebViewMessageReceivedEventArgs(message, json: null));
        return Task.CompletedTask;
    }

    public void OpenDevToolsWindow()
    {
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.DevTools, nameof(OpenDevToolsWindow));

        OpenDevToolsRequested?.Invoke(this, new NativeWebViewOpenDevToolsRequestedEventArgs());
    }

    public Task<NativeWebViewPrintResult> PrintAsync(NativeWebViewPrintSettings? settings = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureNotDisposed();

        return Task.FromResult(
            Features.Supports(NativeWebViewFeature.Printing)
                ? new NativeWebViewPrintResult(NativeWebViewPrintStatus.Success)
                : new NativeWebViewPrintResult(NativeWebViewPrintStatus.NotSupported));
    }

    public Task<bool> ShowPrintUiAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureNotDisposed();
        return Task.FromResult(Features.Supports(NativeWebViewFeature.PrintUi));
    }

    public void SetZoomFactor(double zoomFactor)
    {
        EnsureNotDisposed();

        if (zoomFactor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(zoomFactor), zoomFactor, "Zoom factor must be greater than zero.");
        }

        if (Features.Supports(NativeWebViewFeature.ZoomControl))
        {
            ZoomFactor = zoomFactor;
        }
    }

    public void SetUserAgent(string? userAgent)
    {
        EnsureNotDisposed();
        UserAgentString = userAgent;
    }

    public void SetHeader(string? header)
    {
        EnsureNotDisposed();
        HeaderString = header;
    }

    public bool TryGetCommandManager(out INativeWebViewCommandManager? commandManager)
    {
        EnsureNotDisposed();

        if (Features.Supports(NativeWebViewFeature.CommandManager))
        {
            commandManager = _commandManager;
            return true;
        }

        commandManager = null;
        return false;
    }

    public bool TryGetCookieManager(out INativeWebViewCookieManager? cookieManager)
    {
        EnsureNotDisposed();

        if (Features.Supports(NativeWebViewFeature.CookieManager))
        {
            cookieManager = _cookieManager;
            return true;
        }

        cookieManager = null;
        return false;
    }

    public void MoveFocus(NativeWebViewFocusMoveDirection direction)
    {
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.EmbeddedView, nameof(MoveFocus));
        _ = direction;
    }

    public virtual bool SupportsRenderMode(NativeWebViewRenderMode renderMode)
    {
        return renderMode switch
        {
            NativeWebViewRenderMode.Embedded => Features.Supports(NativeWebViewFeature.EmbeddedView),
            NativeWebViewRenderMode.GpuSurface => Features.Supports(NativeWebViewFeature.GpuSurfaceRendering),
            NativeWebViewRenderMode.Offscreen => Features.Supports(NativeWebViewFeature.OffscreenRendering),
            _ => false,
        };
    }

    public virtual Task<NativeWebViewRenderFrame?> CaptureFrameAsync(
        NativeWebViewRenderMode renderMode,
        NativeWebViewRenderFrameRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureNotDisposed();
        ArgumentNullException.ThrowIfNull(request);

        if (renderMode == NativeWebViewRenderMode.Embedded || !SupportsRenderMode(renderMode))
        {
            return Task.FromResult<NativeWebViewRenderFrame?>(null);
        }

        var pixelWidth = Math.Max(1, request.PixelWidth);
        var pixelHeight = Math.Max(1, request.PixelHeight);
        return Task.FromResult<NativeWebViewRenderFrame?>(CreateSyntheticRenderFrame(renderMode, pixelWidth, pixelHeight));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DestroyRequested?.Invoke(this, new NativeWebViewDestroyRequestedEventArgs("Disposed"));
    }

    protected void RaiseRequestCustomChrome(bool useCustomChrome)
    {
        RequestCustomChrome?.Invoke(this, new NativeWebViewRequestCustomChromeEventArgs
        {
            UseCustomChrome = useCustomChrome,
        });
    }

    protected void RaiseRequestParentWindowPosition(int left, int top, int width, int height)
    {
        RequestParentWindowPosition?.Invoke(this, new NativeWebViewRequestParentWindowPositionEventArgs
        {
            Left = left,
            Top = top,
            Width = width,
            Height = height,
        });
    }

    protected void RaiseBeginMoveDrag()
    {
        BeginMoveDrag?.Invoke(this, new NativeWebViewBeginMoveDragEventArgs());
    }

    protected void RaiseBeginResizeDrag(NativeWindowResizeEdge edge)
    {
        BeginResizeDrag?.Invoke(this, new NativeWebViewBeginResizeDragEventArgs(edge));
    }

    protected void RaiseNewWindowRequested(Uri? uri)
    {
        NewWindowRequested?.Invoke(this, new NativeWebViewNewWindowRequestedEventArgs(uri));
    }

    protected void RaiseWebResourceRequested(Uri? uri, string method, IReadOnlyDictionary<string, string>? headers = null)
    {
        WebResourceRequested?.Invoke(this, new NativeWebViewResourceRequestedEventArgs(uri, method, headers));
    }

    protected void RaiseContextMenuRequested(double x, double y)
    {
        ContextMenuRequested?.Invoke(this, new NativeWebViewContextMenuRequestedEventArgs(x, y));
    }

    private void EnsureInitialized()
    {
        if (IsInitialized)
        {
            return;
        }

        if (Features.Supports(NativeWebViewFeature.EnvironmentOptions))
        {
            var envOptions = new NativeWebViewEnvironmentOptions();
            CoreWebView2EnvironmentRequested?.Invoke(this, new CoreWebViewEnvironmentRequestedEventArgs(envOptions));
        }

        if (Features.Supports(NativeWebViewFeature.ControllerOptions))
        {
            var controllerOptions = new NativeWebViewControllerOptions();
            CoreWebView2ControllerOptionsRequested?.Invoke(this, new CoreWebViewControllerOptionsRequestedEventArgs(controllerOptions));
        }

        IsInitialized = true;
        CoreWebView2Initialized?.Invoke(this, new CoreWebViewInitializedEventArgs(isSuccess: true));
    }

    private void EnsureFeature(NativeWebViewFeature feature, string operationName)
    {
        if (!Features.Supports(feature))
        {
            throw new PlatformNotSupportedException($"Operation '{operationName}' is not supported on platform '{Platform}'.");
        }
    }

    private void EnsureNotDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private NativeWebViewRenderFrame CreateSyntheticRenderFrame(
        NativeWebViewRenderMode renderMode,
        int pixelWidth,
        int pixelHeight)
    {
        const int bytesPerPixel = 4;
        var bytesPerRow = pixelWidth * bytesPerPixel;
        var pixelData = new byte[bytesPerRow * pixelHeight];

        var frameId = Interlocked.Increment(ref _frameSequence);
        var sequence = (int)(frameId & int.MaxValue);
        var urlHash = CurrentUrl is null
            ? Platform.GetHashCode()
            : StringComparer.Ordinal.GetHashCode(CurrentUrl.AbsoluteUri);

        var hashOffset = Math.Abs(urlHash % 256);
        var stripePeriod = 28 + Math.Abs(urlHash % 16);

        for (var y = 0; y < pixelHeight; y++)
        {
            var row = y * bytesPerRow;
            for (var x = 0; x < pixelWidth; x++)
            {
                var offset = row + (x * bytesPerPixel);
                var stripe = ((x + y + sequence) / stripePeriod) & 1;
                var blue = (byte)((x + hashOffset + sequence) & 0xFF);
                var green = (byte)((y + (hashOffset / 2) + (sequence * 3)) & 0xFF);
                var redBase = (byte)(((x ^ y) + hashOffset + (sequence * 2)) & 0xFF);
                var red = stripe == 0 ? redBase : (byte)(255 - redBase);

                pixelData[offset + 0] = blue;
                pixelData[offset + 1] = green;
                pixelData[offset + 2] = red;
                pixelData[offset + 3] = 255;
            }
        }

        return new NativeWebViewRenderFrame(
            pixelWidth,
            pixelHeight,
            bytesPerRow,
            NativeWebViewRenderPixelFormat.Bgra8888Premultiplied,
            pixelData,
            isSynthetic: true,
            frameId: frameId,
            capturedAtUtc: DateTimeOffset.UtcNow,
            renderMode: renderMode,
            origin: NativeWebViewRenderFrameOrigin.SyntheticFallback);
    }

    private sealed class NoopCommandManager : INativeWebViewCommandManager
    {
        public bool TryExecute(string commandName, string? payload = null)
        {
            _ = commandName;
            _ = payload;
            return false;
        }
    }

    private sealed class NoopCookieManager : INativeWebViewCookieManager
    {
        public Task<IReadOnlyDictionary<string, string>> GetCookiesAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(uri);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyDictionary<string, string>>(EmptyReadOnlyDictionary.Instance);
        }

        public Task SetCookieAsync(Uri uri, string name, string value, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(uri);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(value);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task DeleteCookieAsync(Uri uri, string name, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(uri);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}

public abstract class NativeWebDialogBackendStubBase : INativeWebDialogBackend
{
    private readonly List<Uri> _history = [];
    private int _historyIndex = -1;
    private bool _disposed;

    protected NativeWebDialogBackendStubBase(NativeWebViewPlatform platform, IWebViewPlatformFeatures features)
    {
        Platform = platform;
        Features = features;
        IsDevToolsEnabled = features.Supports(NativeWebViewFeature.DevTools);
        IsContextMenuEnabled = features.Supports(NativeWebViewFeature.ContextMenu);
        IsStatusBarEnabled = features.Supports(NativeWebViewFeature.StatusBar);
        IsZoomControlEnabled = features.Supports(NativeWebViewFeature.ZoomControl);
        ZoomFactor = 1.0;
    }

    public NativeWebViewPlatform Platform { get; }

    public IWebViewPlatformFeatures Features { get; }

    public bool IsVisible { get; private set; }

    public Uri? CurrentUrl { get; private set; }

    public bool CanGoBack => _historyIndex > 0;

    public bool CanGoForward => _historyIndex >= 0 && _historyIndex < _history.Count - 1;

    public bool IsDevToolsEnabled { get; set; }

    public bool IsContextMenuEnabled { get; set; }

    public bool IsStatusBarEnabled { get; set; }

    public bool IsZoomControlEnabled { get; set; }

    public double ZoomFactor { get; private set; }

    public string? HeaderString { get; private set; }

    public string? UserAgentString { get; private set; }

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
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.Dialog, nameof(Show));
        _ = options;
        IsVisible = true;
        Shown?.Invoke(this, EventArgs.Empty);
    }

    public void Close()
    {
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.Dialog, nameof(Close));

        if (!IsVisible)
        {
            return;
        }

        IsVisible = false;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    public void Move(double left, double top)
    {
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.Dialog, nameof(Move));
        _ = left;
        _ = top;
    }

    public void Resize(double width, double height)
    {
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.Dialog, nameof(Resize));
        _ = width;
        _ = height;
    }

    public void Navigate(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        if (!Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var uri))
        {
            throw new ArgumentException($"Invalid URL: {url}", nameof(url));
        }

        Navigate(uri);
    }

    public void Navigate(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.Dialog, nameof(Navigate));

        var started = new NativeWebViewNavigationStartedEventArgs(uri, isRedirected: false);
        NavigationStarted?.Invoke(this, started);

        if (started.Cancel)
        {
            return;
        }

        if (_historyIndex < _history.Count - 1)
        {
            _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
        }

        _history.Add(uri);
        _historyIndex = _history.Count - 1;
        CurrentUrl = uri;

        NavigationCompleted?.Invoke(this, new NativeWebViewNavigationCompletedEventArgs(uri, isSuccess: true, httpStatusCode: 200));
    }

    public void Reload()
    {
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.Dialog, nameof(Reload));

        if (CurrentUrl is null)
        {
            return;
        }

        NavigationStarted?.Invoke(this, new NativeWebViewNavigationStartedEventArgs(CurrentUrl, isRedirected: false));
        NavigationCompleted?.Invoke(this, new NativeWebViewNavigationCompletedEventArgs(CurrentUrl, isSuccess: true, httpStatusCode: 200));
    }

    public void Stop()
    {
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.Dialog, nameof(Stop));
    }

    public void GoBack()
    {
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.Dialog, nameof(GoBack));

        if (!CanGoBack)
        {
            return;
        }

        _historyIndex--;
        CurrentUrl = _history[_historyIndex];
        NavigationCompleted?.Invoke(this, new NativeWebViewNavigationCompletedEventArgs(CurrentUrl, isSuccess: true, httpStatusCode: 200));
    }

    public void GoForward()
    {
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.Dialog, nameof(GoForward));

        if (!CanGoForward)
        {
            return;
        }

        _historyIndex++;
        CurrentUrl = _history[_historyIndex];
        NavigationCompleted?.Invoke(this, new NativeWebViewNavigationCompletedEventArgs(CurrentUrl, isSuccess: true, httpStatusCode: 200));
    }

    public Task<string?> ExecuteScriptAsync(string script, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(script);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.Dialog, nameof(ExecuteScriptAsync));
        EnsureFeature(NativeWebViewFeature.ScriptExecution, nameof(ExecuteScriptAsync));

        return Task.FromResult<string?>("null");
    }

    public Task PostWebMessageAsJsonAsync(string message, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.Dialog, nameof(PostWebMessageAsJsonAsync));
        EnsureFeature(NativeWebViewFeature.WebMessageChannel, nameof(PostWebMessageAsJsonAsync));
        var jsonMessage = NativeWebViewBackendSupport.NormalizeJsonMessagePayload(message);

        WebMessageReceived?.Invoke(this, new NativeWebViewMessageReceivedEventArgs(message: null, json: jsonMessage));
        return Task.CompletedTask;
    }

    public Task PostWebMessageAsStringAsync(string message, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.Dialog, nameof(PostWebMessageAsStringAsync));
        EnsureFeature(NativeWebViewFeature.WebMessageChannel, nameof(PostWebMessageAsStringAsync));

        WebMessageReceived?.Invoke(this, new NativeWebViewMessageReceivedEventArgs(message, json: null));
        return Task.CompletedTask;
    }

    public void OpenDevToolsWindow()
    {
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.Dialog, nameof(OpenDevToolsWindow));
        EnsureFeature(NativeWebViewFeature.DevTools, nameof(OpenDevToolsWindow));
    }

    public Task<NativeWebViewPrintResult> PrintAsync(NativeWebViewPrintSettings? settings = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.Dialog, nameof(PrintAsync));
        _ = settings;

        return Task.FromResult(
            Features.Supports(NativeWebViewFeature.Printing)
                ? new NativeWebViewPrintResult(NativeWebViewPrintStatus.Success)
                : new NativeWebViewPrintResult(NativeWebViewPrintStatus.NotSupported));
    }

    public Task<bool> ShowPrintUiAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.Dialog, nameof(ShowPrintUiAsync));
        return Task.FromResult(Features.Supports(NativeWebViewFeature.PrintUi));
    }

    public void SetZoomFactor(double zoomFactor)
    {
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.Dialog, nameof(SetZoomFactor));

        if (zoomFactor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(zoomFactor), zoomFactor, "Zoom factor must be greater than zero.");
        }

        if (Features.Supports(NativeWebViewFeature.ZoomControl))
        {
            ZoomFactor = zoomFactor;
        }
    }

    public void SetUserAgent(string? userAgent)
    {
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.Dialog, nameof(SetUserAgent));
        UserAgentString = userAgent;
    }

    public void SetHeader(string? header)
    {
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.Dialog, nameof(SetHeader));
        HeaderString = header;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        IsVisible = false;
    }

    protected void RaiseNewWindowRequested(Uri? uri)
    {
        NewWindowRequested?.Invoke(this, new NativeWebViewNewWindowRequestedEventArgs(uri));
    }

    protected void RaiseWebResourceRequested(Uri? uri, string method, IReadOnlyDictionary<string, string>? headers = null)
    {
        WebResourceRequested?.Invoke(this, new NativeWebViewResourceRequestedEventArgs(uri, method, headers));
    }

    protected void RaiseContextMenuRequested(double x, double y)
    {
        ContextMenuRequested?.Invoke(this, new NativeWebViewContextMenuRequestedEventArgs(x, y));
    }

    private void EnsureFeature(NativeWebViewFeature feature, string operationName)
    {
        if (!Features.Supports(feature))
        {
            throw new PlatformNotSupportedException($"Operation '{operationName}' is not supported on platform '{Platform}'.");
        }
    }

    private void EnsureNotDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}

public abstract class WebAuthenticationBrokerStubBase : IWebAuthenticationBrokerBackend
{
    private bool _disposed;

    protected WebAuthenticationBrokerStubBase(NativeWebViewPlatform platform, IWebViewPlatformFeatures features)
    {
        Platform = platform;
        Features = features;
    }

    public NativeWebViewPlatform Platform { get; }

    public IWebViewPlatformFeatures Features { get; }

    public virtual Task<WebAuthenticationResult> AuthenticateAsync(
        Uri requestUri,
        Uri callbackUri,
        WebAuthenticationOptions options = WebAuthenticationOptions.None,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(requestUri);
        ArgumentNullException.ThrowIfNull(callbackUri);
        cancellationToken.ThrowIfCancellationRequested();
        _ = options;

        if (!Features.Supports(NativeWebViewFeature.AuthenticationBroker))
        {
            return Task.FromResult(WebAuthenticationResult.Error(unchecked((int)0x80004001)));
        }

        return Task.FromResult(WebAuthenticationResult.UserCancel());
    }

    public virtual void Dispose()
    {
        _disposed = true;
    }
}

public sealed class UnregisteredNativeWebViewBackend : NativeWebViewBackendStubBase
{
    public UnregisteredNativeWebViewBackend(NativeWebViewPlatform platform, IWebViewPlatformFeatures features)
        : base(platform, features)
    {
    }
}

public sealed class UnregisteredNativeWebDialogBackend : NativeWebDialogBackendStubBase
{
    public UnregisteredNativeWebDialogBackend(NativeWebViewPlatform platform, IWebViewPlatformFeatures features)
        : base(platform, features)
    {
    }
}

public sealed class UnregisteredWebAuthenticationBrokerBackend : WebAuthenticationBrokerStubBase
{
    public UnregisteredWebAuthenticationBrokerBackend(NativeWebViewPlatform platform, IWebViewPlatformFeatures features)
        : base(platform, features)
    {
    }
}
