using System.Text.Json;
using System.Runtime.Versioning;
using System.Runtime.InteropServices.JavaScript;
using Avalonia.Browser;
using Avalonia.Controls.Platform;
using NativeWebView.Core;
using NativeWebView.Interop;

namespace NativeWebView.Platform.Browser;

[SupportedOSPlatform("browser")]
public sealed class BrowserNativeWebViewBackend
    : INativeWebViewBackend,
      INativeWebViewFrameSource,
      INativeWebViewPlatformHandleProvider,
      INativeWebViewInstanceConfigurationTarget,
      INativeWebViewManagedControlHandleProvider
{
    private enum BrowserNavigationOperation
    {
        None = 0,
        Direct,
        Reload,
        Back,
        Forward,
    }

    private static long s_nextSyntheticHandle = 0x6000;

    private readonly SemaphoreSlim _runtimeGate = new(1, 1);
    private readonly List<Uri> _history = [];
    private readonly INativeWebViewCommandManager _commandManager = NativeWebViewBackendSupport.NoopCommandManagerInstance;
    private readonly INativeWebViewCookieManager _cookieManager = NativeWebViewBackendSupport.NoopCookieManagerInstance;
    private readonly NativePlatformHandle _platformHandle;
    private readonly NativePlatformHandle _viewHandle;
    private readonly NativePlatformHandle _controllerHandle;

    private NativeWebViewInstanceConfiguration _instanceConfiguration = new();
    private NativeWebViewEnvironmentOptions? _preparedEnvironmentOptions;
    private NativeWebViewControllerOptions? _preparedControllerOptions;

    private JSObject? _frameElement;
    private JSObject? _eventSubscription;
    private JSObjectControlHandle? _managedControlHandle;

    private Uri? _currentUrl;
    private Uri? _pendingNavigationUri;

    private int _historyIndex = -1;
    private int _pendingHistoryIndex = -1;
    private long _frameSequence;

    private bool _isStubInitialized;
    private bool _isRuntimeInitialized;
    private bool _coreInitializedRaised;
    private bool _runtimeInitializationRequested;
    private bool _disposed;

    private bool _canGoBack;
    private bool _canGoForward;
    private bool _isDevToolsEnabled;
    private bool _isContextMenuEnabled;
    private bool _isStatusBarEnabled;
    private bool _isZoomControlEnabled;

    private double _zoomFactor;
    private string? _headerString;
    private string? _userAgentString;
    private string? _suppressedCompletedNavigationKey;

    private BrowserNavigationOperation _pendingNavigationOperation;

    public BrowserNativeWebViewBackend()
    {
        Platform = NativeWebViewPlatform.Browser;
        Features = BrowserPlatformFeatures.Instance;
        var handleSeed = Interlocked.Add(ref s_nextSyntheticHandle, 3);
        _platformHandle = new NativePlatformHandle((nint)(handleSeed - 2), "Window");
        _viewHandle = new NativePlatformHandle((nint)(handleSeed - 1), "HTMLIFrameElement");
        _controllerHandle = new NativePlatformHandle((nint)handleSeed, "Window");
        _zoomFactor = 1.0;
        _isDevToolsEnabled = Features.Supports(NativeWebViewFeature.DevTools);
        _isContextMenuEnabled = Features.Supports(NativeWebViewFeature.ContextMenu);
        _isStatusBarEnabled = Features.Supports(NativeWebViewFeature.StatusBar);
        _isZoomControlEnabled = Features.Supports(NativeWebViewFeature.ZoomControl);
    }

    public NativeWebViewPlatform Platform { get; }

    public IWebViewPlatformFeatures Features { get; }

    public Uri? CurrentUrl => _currentUrl;

    public bool IsInitialized => _isRuntimeInitialized || _isStubInitialized;

    public bool CanGoBack => _canGoBack;

    public bool CanGoForward => _canGoForward;

    public bool IsDevToolsEnabled
    {
        get => _isDevToolsEnabled;
        set
        {
            EnsureNotDisposed();
            _isDevToolsEnabled = value;
        }
    }

    public bool IsContextMenuEnabled
    {
        get => _isContextMenuEnabled;
        set
        {
            EnsureNotDisposed();
            _isContextMenuEnabled = value;
        }
    }

    public bool IsStatusBarEnabled
    {
        get => _isStatusBarEnabled;
        set
        {
            EnsureNotDisposed();
            _isStatusBarEnabled = value;
        }
    }

    public bool IsZoomControlEnabled
    {
        get => _isZoomControlEnabled;
        set
        {
            EnsureNotDisposed();
            _isZoomControlEnabled = value;

            if (ShouldUseRuntimePath() && Features.Supports(NativeWebViewFeature.ZoomControl))
            {
                BrowserNativeWebViewInterop.SetZoomFactor(_frameElement!, _zoomFactor);
            }
        }
    }

    public double ZoomFactor => _zoomFactor;

    public string? HeaderString => _headerString;

    public string? UserAgentString => _userAgentString;

    public event EventHandler<CoreWebViewInitializedEventArgs>? CoreWebView2Initialized;

    public event EventHandler<NativeWebViewNavigationStartedEventArgs>? NavigationStarted;

    public event EventHandler<NativeWebViewNavigationCompletedEventArgs>? NavigationCompleted;

    public event EventHandler<NativeWebViewMessageReceivedEventArgs>? WebMessageReceived;

    public event EventHandler<NativeWebViewOpenDevToolsRequestedEventArgs>? OpenDevToolsRequested;

    public event EventHandler<NativeWebViewDestroyRequestedEventArgs>? DestroyRequested;

#pragma warning disable CS0067
    public event EventHandler<NativeWebViewRequestCustomChromeEventArgs>? RequestCustomChrome;

    public event EventHandler<NativeWebViewRequestParentWindowPositionEventArgs>? RequestParentWindowPosition;

    public event EventHandler<NativeWebViewBeginMoveDragEventArgs>? BeginMoveDrag;

    public event EventHandler<NativeWebViewBeginResizeDragEventArgs>? BeginResizeDrag;

    public event EventHandler<NativeWebViewResourceRequestedEventArgs>? WebResourceRequested;

    public event EventHandler<NativeWebViewContextMenuRequestedEventArgs>? ContextMenuRequested;
#pragma warning restore CS0067

    public event EventHandler<NativeWebViewNewWindowRequestedEventArgs>? NewWindowRequested;

    public event EventHandler<NativeWebViewNavigationHistoryChangedEventArgs>? NavigationHistoryChanged;

    public event EventHandler<CoreWebViewEnvironmentRequestedEventArgs>? CoreWebView2EnvironmentRequested;

    public event EventHandler<CoreWebViewControllerOptionsRequestedEventArgs>? CoreWebView2ControllerOptionsRequested;

    public void ApplyInstanceConfiguration(NativeWebViewInstanceConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        EnsureNotDisposed();

        _instanceConfiguration = configuration.Clone();
        if (!_coreInitializedRaised)
        {
            _preparedEnvironmentOptions = null;
            _preparedControllerOptions = null;
        }
    }

    public async ValueTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.EmbeddedView, nameof(InitializeAsync));

        if (OperatingSystem.IsBrowser())
        {
            _runtimeInitializationRequested = true;

            if (_frameElement is not null)
            {
                await EnsureRuntimeInitializedAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        EnsureStubInitialized();
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

        if (ShouldUseRuntimePath())
        {
            NavigateRuntime(uri);
            return;
        }

        NavigateFallback(uri);
    }

    public void Reload()
    {
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.EmbeddedView, nameof(Reload));

        if (ShouldUseRuntimePath())
        {
            if (_currentUrl is null)
            {
                return;
            }

            var runtimeReloadStarted = new NativeWebViewNavigationStartedEventArgs(_currentUrl, isRedirected: false);
            NavigationStarted?.Invoke(this, runtimeReloadStarted);
            if (runtimeReloadStarted.Cancel)
            {
                return;
            }

            _pendingNavigationOperation = BrowserNavigationOperation.Reload;
            _pendingNavigationUri = _currentUrl;
            BrowserNativeWebViewInterop.Reload(_frameElement!);
            return;
        }

        if (_currentUrl is null)
        {
            return;
        }

        var started = new NativeWebViewNavigationStartedEventArgs(_currentUrl, isRedirected: false);
        NavigationStarted?.Invoke(this, started);
        if (!started.Cancel)
        {
            NavigationCompleted?.Invoke(this, new NativeWebViewNavigationCompletedEventArgs(_currentUrl, isSuccess: true, httpStatusCode: 200));
        }
    }

    public void Stop()
    {
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.EmbeddedView, nameof(Stop));

        if (ShouldUseRuntimePath())
        {
            BrowserNativeWebViewInterop.Stop(_frameElement!);
        }
    }

    public void GoBack()
    {
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.EmbeddedView, nameof(GoBack));

        if (ShouldUseRuntimePath())
        {
            if (!CanGoBack)
            {
                return;
            }

            var targetIndex = _historyIndex - 1;
            var targetUri = _history[targetIndex];
            var started = new NativeWebViewNavigationStartedEventArgs(targetUri, isRedirected: false);
            NavigationStarted?.Invoke(this, started);
            if (started.Cancel)
            {
                return;
            }

            _pendingNavigationOperation = BrowserNavigationOperation.Back;
            _pendingHistoryIndex = targetIndex;
            _pendingNavigationUri = targetUri;
            BrowserNativeWebViewInterop.GoBack(_frameElement!);
            return;
        }

        GoBackFallback();
    }

    public void GoForward()
    {
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.EmbeddedView, nameof(GoForward));

        if (ShouldUseRuntimePath())
        {
            if (!CanGoForward)
            {
                return;
            }

            var targetIndex = _historyIndex + 1;
            var targetUri = _history[targetIndex];
            var started = new NativeWebViewNavigationStartedEventArgs(targetUri, isRedirected: false);
            NavigationStarted?.Invoke(this, started);
            if (started.Cancel)
            {
                return;
            }

            _pendingNavigationOperation = BrowserNavigationOperation.Forward;
            _pendingHistoryIndex = targetIndex;
            _pendingNavigationUri = targetUri;
            BrowserNativeWebViewInterop.GoForward(_frameElement!);
            return;
        }

        GoForwardFallback();
    }

    public async Task<string?> ExecuteScriptAsync(string script, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(script);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.ScriptExecution, nameof(ExecuteScriptAsync));

        if (!ShouldUseRuntimePath())
        {
            return "null";
        }

        await EnsureRuntimeInitializedAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return BrowserNativeWebViewInterop.ExecuteScript(_frameElement!, script);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Browser script execution requires same-origin iframe access. Cross-origin pages can navigate, but they cannot be script-driven through the embedded browser runtime path.",
                ex);
        }
    }

    public async Task PostWebMessageAsJsonAsync(string message, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.WebMessageChannel, nameof(PostWebMessageAsJsonAsync));
        var jsonMessage = NativeWebViewBackendSupport.NormalizeJsonMessagePayload(message);

        if (!ShouldUseRuntimePath())
        {
            WebMessageReceived?.Invoke(this, new NativeWebViewMessageReceivedEventArgs(message: null, json: jsonMessage));
            return;
        }

        await EnsureRuntimeInitializedAsync(cancellationToken).ConfigureAwait(false);
        BrowserNativeWebViewInterop.PostWebMessage(_frameElement!, "json", jsonMessage);
    }

    public async Task PostWebMessageAsStringAsync(string message, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.WebMessageChannel, nameof(PostWebMessageAsStringAsync));

        if (!ShouldUseRuntimePath())
        {
            WebMessageReceived?.Invoke(this, new NativeWebViewMessageReceivedEventArgs(message, json: null));
            return;
        }

        await EnsureRuntimeInitializedAsync(cancellationToken).ConfigureAwait(false);
        BrowserNativeWebViewInterop.PostWebMessage(_frameElement!, "string", message);
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
        _ = settings;
        return Task.FromResult(new NativeWebViewPrintResult(NativeWebViewPrintStatus.NotSupported));
    }

    public Task<bool> ShowPrintUiAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureNotDisposed();
        return Task.FromResult(false);
    }

    public void SetZoomFactor(double zoomFactor)
    {
        EnsureNotDisposed();

        if (zoomFactor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(zoomFactor), zoomFactor, "Zoom factor must be greater than zero.");
        }

        if (!Features.Supports(NativeWebViewFeature.ZoomControl))
        {
            return;
        }

        _zoomFactor = zoomFactor;
        if (ShouldUseRuntimePath())
        {
            BrowserNativeWebViewInterop.SetZoomFactor(_frameElement!, _zoomFactor);
        }
    }

    public void SetUserAgent(string? userAgent)
    {
        EnsureNotDisposed();
        _userAgentString = userAgent;
    }

    public void SetHeader(string? header)
    {
        EnsureNotDisposed();
        _headerString = header;
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

        if (ShouldUseRuntimePath())
        {
            BrowserNativeWebViewInterop.Focus(_frameElement!);
        }
    }

    public bool SupportsRenderMode(NativeWebViewRenderMode renderMode)
    {
        return renderMode switch
        {
            NativeWebViewRenderMode.Embedded => Features.Supports(NativeWebViewFeature.EmbeddedView),
            NativeWebViewRenderMode.GpuSurface => Features.Supports(NativeWebViewFeature.GpuSurfaceRendering),
            NativeWebViewRenderMode.Offscreen => Features.Supports(NativeWebViewFeature.OffscreenRendering),
            _ => false,
        };
    }

    public Task<NativeWebViewRenderFrame?> CaptureFrameAsync(
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

        var frame = NativeWebViewBackendSupport.CreateSyntheticRenderFrame(
            Platform,
            _currentUrl,
            ref _frameSequence,
            renderMode,
            Math.Max(1, request.PixelWidth),
            Math.Max(1, request.PixelHeight));
        return Task.FromResult<NativeWebViewRenderFrame?>(frame);
    }

    public bool TryGetPlatformHandle(out NativePlatformHandle handle)
    {
        handle = _platformHandle;
        return true;
    }

    public bool TryGetViewHandle(out NativePlatformHandle handle)
    {
        handle = _viewHandle;
        return true;
    }

    public bool TryGetControllerHandle(out NativePlatformHandle handle)
    {
        handle = _controllerHandle;
        return true;
    }

    public object CreateManagedControlHandle()
    {
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.EmbeddedView, nameof(CreateManagedControlHandle));

        if (_managedControlHandle is not null)
        {
            return _managedControlHandle;
        }

        if (!OperatingSystem.IsBrowser())
        {
            throw new PlatformNotSupportedException("Browser managed control handles can only be created in browser runtime.");
        }

        BrowserNativeWebViewInterop.EnsureInstalled();

        _frameElement = BrowserNativeWebViewInterop.CreateFrame();
        _eventSubscription = BrowserNativeWebViewInterop.SubscribeFrameEvents(_frameElement, HandleFrameLoaded, HandleFrameMessage);
        _managedControlHandle = new JSObjectControlHandle(_frameElement);

        if (Features.Supports(NativeWebViewFeature.ZoomControl))
        {
            BrowserNativeWebViewInterop.SetZoomFactor(_frameElement, _zoomFactor);
        }

        if (_currentUrl is not null)
        {
            _suppressedCompletedNavigationKey = _isStubInitialized
                ? GetNavigationKey(_currentUrl)
                : null;
            BrowserNativeWebViewInterop.Navigate(_frameElement, _currentUrl.ToString());
        }

        if (_runtimeInitializationRequested)
        {
            _ = TryInitializeRuntimeInBackgroundAsync();
        }

        return _managedControlHandle;
    }

    public void ReleaseManagedControlHandle(object? handle)
    {
        if (handle is not null &&
            _managedControlHandle is not null &&
            !ReferenceEquals(handle, _managedControlHandle))
        {
            return;
        }

        if (_eventSubscription is not null)
        {
            try
            {
                BrowserNativeWebViewInterop.UnsubscribeFrameEvents(_eventSubscription);
            }
            catch
            {
            }

            _eventSubscription.Dispose();
            _eventSubscription = null;
        }

        if (_frameElement is not null)
        {
            try
            {
                BrowserNativeWebViewInterop.ReleaseFrame(_frameElement);
            }
            catch
            {
            }

            _frameElement = null;
        }

        _managedControlHandle = null;
        _isRuntimeInitialized = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        var managedControlHandle = _managedControlHandle;
        _disposed = true;

        ReleaseManagedControlHandle(managedControlHandle);
        if (managedControlHandle is INativeControlHostDestroyableControlHandle destroyableControlHandle)
        {
            try
            {
                destroyableControlHandle.Destroy();
            }
            catch
            {
                // Best-effort shutdown for browser native control handles.
            }
        }

        _runtimeGate.Dispose();
        DestroyRequested?.Invoke(this, new NativeWebViewDestroyRequestedEventArgs("Disposed"));
    }

    private void NavigateFallback(Uri uri)
    {
        EnsureStubInitialized();

        var started = new NativeWebViewNavigationStartedEventArgs(uri, isRedirected: false);
        NavigationStarted?.Invoke(this, started);
        if (started.Cancel)
        {
            return;
        }

        AppendHistoryEntry(uri);
        _currentUrl = uri;
        _pendingNavigationUri = uri;
        UpdateHistorySnapshot(_historyIndex > 0, _historyIndex < _history.Count - 1);
        NavigationCompleted?.Invoke(this, new NativeWebViewNavigationCompletedEventArgs(uri, isSuccess: true, httpStatusCode: 200));
    }

    private void NavigateRuntime(Uri uri)
    {
        var started = new NativeWebViewNavigationStartedEventArgs(uri, isRedirected: false);
        NavigationStarted?.Invoke(this, started);
        if (started.Cancel)
        {
            return;
        }

        _runtimeInitializationRequested = true;
        _pendingNavigationOperation = BrowserNavigationOperation.Direct;
        _pendingNavigationUri = uri;

        if (_frameElement is not null)
        {
            BrowserNativeWebViewInterop.Navigate(_frameElement, uri.ToString());
            return;
        }

        _currentUrl = uri;
        _ = TryInitializeRuntimeInBackgroundAsync();
    }

    private void GoBackFallback()
    {
        if (!CanGoBack)
        {
            return;
        }

        _historyIndex--;
        _currentUrl = _history[_historyIndex];
        UpdateHistorySnapshot(_historyIndex > 0, _historyIndex < _history.Count - 1);
        NavigationCompleted?.Invoke(this, new NativeWebViewNavigationCompletedEventArgs(_currentUrl, isSuccess: true, httpStatusCode: 200));
    }

    private void GoForwardFallback()
    {
        if (!CanGoForward)
        {
            return;
        }

        _historyIndex++;
        _currentUrl = _history[_historyIndex];
        UpdateHistorySnapshot(_historyIndex > 0, _historyIndex < _history.Count - 1);
        NavigationCompleted?.Invoke(this, new NativeWebViewNavigationCompletedEventArgs(_currentUrl, isSuccess: true, httpStatusCode: 200));
    }

    private async Task EnsureRuntimeInitializedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _runtimeGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_isRuntimeInitialized)
            {
                return;
            }

            if (!OperatingSystem.IsBrowser() || _frameElement is null)
            {
                EnsureStubInitialized();
                return;
            }

            BrowserNativeWebViewInterop.EnsureInstalled();
            EnsurePreparedInitializationOptions();
            _isRuntimeInitialized = true;
            _isStubInitialized = false;
            RaiseInitializedIfNeeded(success: true, initializationException: null, nativeObject: _frameElement);
        }
        finally
        {
            _runtimeGate.Release();
        }
    }

    private async Task TryInitializeRuntimeInBackgroundAsync()
    {
        try
        {
            await EnsureRuntimeInitializedAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private void EnsurePreparedInitializationOptions()
    {
        if (_preparedEnvironmentOptions is not null)
        {
            return;
        }

        var environmentOptions = new NativeWebViewEnvironmentOptions();
        var controllerOptions = new NativeWebViewControllerOptions();

        _instanceConfiguration.ApplyEnvironmentOptions(environmentOptions);
        _instanceConfiguration.ApplyControllerOptions(controllerOptions);

        if (Features.Supports(NativeWebViewFeature.EnvironmentOptions))
        {
            CoreWebView2EnvironmentRequested?.Invoke(this, new CoreWebViewEnvironmentRequestedEventArgs(environmentOptions));
        }

        if (Features.Supports(NativeWebViewFeature.ControllerOptions))
        {
            CoreWebView2ControllerOptionsRequested?.Invoke(this, new CoreWebViewControllerOptionsRequestedEventArgs(controllerOptions));
        }

        _preparedEnvironmentOptions = environmentOptions.Clone();
        _preparedControllerOptions = controllerOptions.Clone();
    }

    private void EnsureStubInitialized()
    {
        if (_isStubInitialized)
        {
            return;
        }

        EnsurePreparedInitializationOptions();
        _isStubInitialized = true;
        RaiseInitializedIfNeeded(success: true, initializationException: null, nativeObject: null);
    }

    private void RaiseInitializedIfNeeded(bool success, Exception? initializationException, object? nativeObject)
    {
        if (_coreInitializedRaised)
        {
            return;
        }

        _coreInitializedRaised = true;
        CoreWebView2Initialized?.Invoke(this, new CoreWebViewInitializedEventArgs(success, initializationException, nativeObject));
    }

    private void HandleFrameLoaded(string? currentUrl)
    {
        if (_disposed)
        {
            return;
        }

        var resolvedUri = TryCreateUri(currentUrl) ?? _pendingNavigationUri ?? _currentUrl;
        if (resolvedUri is null)
        {
            return;
        }

        if (_pendingNavigationOperation == BrowserNavigationOperation.None &&
            _currentUrl is null &&
            IsAboutBlank(resolvedUri))
        {
            return;
        }

        var navigationKey = GetNavigationKey(resolvedUri);
        if (_suppressedCompletedNavigationKey is not null &&
            string.Equals(_suppressedCompletedNavigationKey, navigationKey, StringComparison.Ordinal))
        {
            _suppressedCompletedNavigationKey = null;
            _currentUrl = resolvedUri;
            ClearPendingNavigationState();
            return;
        }

        switch (_pendingNavigationOperation)
        {
            case BrowserNavigationOperation.Direct:
                AppendHistoryEntry(resolvedUri);
                break;
            case BrowserNavigationOperation.Back:
            case BrowserNavigationOperation.Forward:
                if (_pendingHistoryIndex >= 0 && _pendingHistoryIndex < _history.Count)
                {
                    _historyIndex = _pendingHistoryIndex;
                }
                break;
            case BrowserNavigationOperation.None:
                if (_historyIndex < 0 || _history[_historyIndex] != resolvedUri)
                {
                    AppendHistoryEntry(resolvedUri);
                }
                break;
        }

        _currentUrl = resolvedUri;
        UpdateHistorySnapshot(_historyIndex > 0, _historyIndex >= 0 && _historyIndex < _history.Count - 1);
        NavigationCompleted?.Invoke(this, new NativeWebViewNavigationCompletedEventArgs(resolvedUri, isSuccess: true, httpStatusCode: 200));
        ClearPendingNavigationState();
    }

    private void HandleFrameMessage(string envelopeJson)
    {
        if (_disposed || string.IsNullOrWhiteSpace(envelopeJson))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(envelopeJson);
            var root = document.RootElement;
            var kind = root.TryGetProperty("kind", out var kindElement)
                ? kindElement.GetString()
                : null;

            if (string.Equals(kind, "newWindow", StringComparison.Ordinal))
            {
                var uri = root.TryGetProperty("url", out var urlElement)
                    ? TryCreateUri(urlElement.GetString())
                    : null;
                NewWindowRequested?.Invoke(this, new NativeWebViewNewWindowRequestedEventArgs(uri));
                return;
            }

            if (string.Equals(kind, "message", StringComparison.Ordinal))
            {
                var format = root.TryGetProperty("format", out var formatElement)
                    ? formatElement.GetString()
                    : "string";
                var value = root.TryGetProperty("value", out var valueElement) &&
                    valueElement.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined
                    ? valueElement.GetString()
                    : null;

                if (string.Equals(format, "json", StringComparison.Ordinal))
                {
                    WebMessageReceived?.Invoke(this, new NativeWebViewMessageReceivedEventArgs(message: null, json: value ?? "null"));
                    return;
                }

                WebMessageReceived?.Invoke(this, new NativeWebViewMessageReceivedEventArgs(value, json: null));
                return;
            }
        }
        catch
        {
        }

        WebMessageReceived?.Invoke(this, new NativeWebViewMessageReceivedEventArgs(envelopeJson, json: null));
    }

    private void AppendHistoryEntry(Uri uri)
    {
        if (_historyIndex < _history.Count - 1)
        {
            _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
        }

        _history.Add(uri);
        _historyIndex = _history.Count - 1;
    }

    private void ClearPendingNavigationState()
    {
        _pendingNavigationOperation = BrowserNavigationOperation.None;
        _pendingHistoryIndex = -1;
        _pendingNavigationUri = null;
    }

    private void UpdateHistorySnapshot(bool canGoBack, bool canGoForward)
    {
        var changed = _canGoBack != canGoBack || _canGoForward != canGoForward;
        _canGoBack = canGoBack;
        _canGoForward = canGoForward;

        if (changed)
        {
            NavigationHistoryChanged?.Invoke(this, new NativeWebViewNavigationHistoryChangedEventArgs(_canGoBack, _canGoForward));
        }
    }

    private bool ShouldUseRuntimePath()
    {
        return OperatingSystem.IsBrowser() && _frameElement is not null;
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

    private static Uri? TryCreateUri(string? value)
    {
        return Uri.TryCreate(value, UriKind.RelativeOrAbsolute, out var uri)
            ? uri
            : null;
    }

    private static bool IsAboutBlank(Uri uri)
    {
        return uri.IsAbsoluteUri &&
            string.Equals(uri.Scheme, "about", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(uri.OriginalString, "about:blank", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetNavigationKey(Uri uri)
    {
        return uri.IsAbsoluteUri
            ? uri.AbsoluteUri
            : uri.OriginalString;
    }
}
