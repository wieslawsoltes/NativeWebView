using System.Runtime.Versioning;
using System.Text.Json;
using NativeWebView.Core;
using NativeWebView.Interop;

namespace NativeWebView.Platform.Linux;

public sealed class LinuxNativeWebViewBackend
    : INativeWebViewBackend,
      INativeWebViewFrameSource,
      INativeWebViewPlatformHandleProvider,
      INativeWebViewInstanceConfigurationTarget,
      INativeWebViewNativeControlAttachment
{
    private static readonly NativePlatformHandle PlaceholderPlatformHandle = new((nint)0x3001, "XID");
    private static readonly NativePlatformHandle PlaceholderViewHandle = new((nint)0x3002, "WebKitWebView");
    private static readonly NativePlatformHandle PlaceholderControllerHandle = new((nint)0x3003, "WebKitSettings");
    private const string ScriptMessageHandlerName = "nativewebview";

    private static readonly string JavaScriptBridgeSource = """
        (() => {
          const chromeRoot = window.chrome = window.chrome || {};
          const webview = chromeRoot.webview = chromeRoot.webview || {};
          const listeners = webview.__listeners = webview.__listeners || [];

          webview.postMessage = (message) => {
            const handler = window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.nativewebview;
            if (handler && typeof handler.postMessage === 'function') {
              handler.postMessage(message);
            }
          };

          webview.addEventListener = (type, listener) => {
            if (type !== 'message' || typeof listener !== 'function' || listeners.includes(listener)) {
              return;
            }

            listeners.push(listener);
          };

          webview.removeEventListener = (type, listener) => {
            if (type !== 'message') {
              return;
            }

            const index = listeners.indexOf(listener);
            if (index >= 0) {
              listeners.splice(index, 1);
            }
          };

          webview.__dispatchMessage = (message) => {
            const event = { data: message };
            for (const listener of [...listeners]) {
              try {
                listener(event);
              } catch (error) {
                console.error(error);
              }
            }

            window.dispatchEvent(new MessageEvent('message', { data: message }));
            return true;
          };
        })();
        """;

    private readonly SemaphoreSlim _runtimeGate = new(1, 1);
    private readonly List<Uri> _history = [];
    private readonly List<IDisposable> _signalSubscriptions = [];
    private readonly INativeWebViewCommandManager _commandManager = NativeWebViewBackendSupport.NoopCommandManagerInstance;
    private readonly INativeWebViewCookieManager _cookieManager = NativeWebViewBackendSupport.NoopCookieManagerInstance;

    private TaskCompletionSource<bool> _attachmentTcs = CreatePendingAttachmentSource();
    private NativeWebViewInstanceConfiguration _instanceConfiguration = new();

    private NativeWebViewEnvironmentOptions? _preparedEnvironmentOptions;
    private NativeWebViewControllerOptions? _preparedControllerOptions;

    private Uri? _currentUrl;
    private Uri? _pendingNavigationUri;

    private nint _parentWindowXid;
    private nint _gtkWindow;
    private nint _hostWindowXid;
    private nint _webView;
    private nint _settings;
    private nint _webContext;
    private nint _websiteDataManager;
    private nint _userContentManager;

    private int _historyIndex = -1;
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

    public LinuxNativeWebViewBackend()
    {
        Platform = NativeWebViewPlatform.Linux;
        Features = LinuxPlatformFeatures.Instance;
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
            _ = ApplyRuntimeSettingsAsync();
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
#pragma warning restore CS0067

    public event EventHandler<NativeWebViewNewWindowRequestedEventArgs>? NewWindowRequested;

    public event EventHandler<NativeWebViewResourceRequestedEventArgs>? WebResourceRequested;

    public event EventHandler<NativeWebViewContextMenuRequestedEventArgs>? ContextMenuRequested;

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

        if (OperatingSystem.IsLinux())
        {
            _runtimeInitializationRequested = true;

            if (_hostWindowXid != IntPtr.Zero)
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
            _currentUrl = uri;
            _pendingNavigationUri = uri;
            _runtimeInitializationRequested = true;

            if (_webView != IntPtr.Zero)
            {
                _ = LinuxGtkDispatcher.InvokeAsync(() => LinuxNativeInterop.webkit_web_view_load_uri(_webView, ToNavigationString(uri)));
            }
            else
            {
                _ = TryInitializeRuntimeInBackgroundAsync();
            }

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
            if (_webView != IntPtr.Zero)
            {
                _ = LinuxGtkDispatcher.InvokeAsync(() => LinuxNativeInterop.webkit_web_view_reload(_webView));
            }
            else if (_currentUrl is not null)
            {
                _pendingNavigationUri = _currentUrl;
                _ = TryInitializeRuntimeInBackgroundAsync();
            }

            return;
        }

        if (_currentUrl is null)
        {
            return;
        }

        NavigationStarted?.Invoke(this, new NativeWebViewNavigationStartedEventArgs(_currentUrl, isRedirected: false));
        NavigationCompleted?.Invoke(this, new NativeWebViewNavigationCompletedEventArgs(_currentUrl, isSuccess: true, httpStatusCode: 200));
    }

    public void Stop()
    {
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.EmbeddedView, nameof(Stop));

        if (_webView != IntPtr.Zero)
        {
            _ = LinuxGtkDispatcher.InvokeAsync(() => LinuxNativeInterop.webkit_web_view_stop_loading(_webView));
        }
    }

    public void GoBack()
    {
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.EmbeddedView, nameof(GoBack));

        if (ShouldUseRuntimePath())
        {
            if (_webView != IntPtr.Zero)
            {
                _ = LinuxGtkDispatcher.InvokeAsync(() =>
                {
                    if (LinuxNativeInterop.webkit_web_view_can_go_back(_webView))
                    {
                        LinuxNativeInterop.webkit_web_view_go_back(_webView);
                    }
                });
            }

            return;
        }

        if (!CanGoBack)
        {
            return;
        }

        _historyIndex--;
        _currentUrl = _history[_historyIndex];
        UpdateHistorySnapshot(_historyIndex > 0, _historyIndex < _history.Count - 1);
        NavigationCompleted?.Invoke(this, new NativeWebViewNavigationCompletedEventArgs(_currentUrl, isSuccess: true, httpStatusCode: 200));
    }

    public void GoForward()
    {
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.EmbeddedView, nameof(GoForward));

        if (ShouldUseRuntimePath())
        {
            if (_webView != IntPtr.Zero)
            {
                _ = LinuxGtkDispatcher.InvokeAsync(() =>
                {
                    if (LinuxNativeInterop.webkit_web_view_can_go_forward(_webView))
                    {
                        LinuxNativeInterop.webkit_web_view_go_forward(_webView);
                    }
                });
            }

            return;
        }

        if (!CanGoForward)
        {
            return;
        }

        _historyIndex++;
        _currentUrl = _history[_historyIndex];
        UpdateHistorySnapshot(_historyIndex > 0, _historyIndex < _history.Count - 1);
        NavigationCompleted?.Invoke(this, new NativeWebViewNavigationCompletedEventArgs(_currentUrl, isSuccess: true, httpStatusCode: 200));
    }

    public async Task<string?> ExecuteScriptAsync(string script, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(script);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.ScriptExecution, nameof(ExecuteScriptAsync));

        if (ShouldUseRuntimePath())
        {
            await EnsureRuntimeInitializedAsync(cancellationToken).ConfigureAwait(false);
            var execution = await LinuxGtkDispatcher.InvokeAsync(
                () => LinuxNativeInterop.RunJavaScriptAsync(_webView, script, cancellationToken),
                cancellationToken).ConfigureAwait(false);

            return await execution.ConfigureAwait(false);
        }

        EnsureStubInitialized();
        return "null";
    }

    public async Task PostWebMessageAsJsonAsync(string message, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.WebMessageChannel, nameof(PostWebMessageAsJsonAsync));

        if (ShouldUseRuntimePath())
        {
            await EnsureRuntimeInitializedAsync(cancellationToken).ConfigureAwait(false);
            var script = BuildDispatchScript(message);
            var execution = await LinuxGtkDispatcher.InvokeAsync(
                () => LinuxNativeInterop.RunJavaScriptAsync(_webView, script, cancellationToken),
                cancellationToken).ConfigureAwait(false);

            await execution.ConfigureAwait(false);
            return;
        }

        EnsureStubInitialized();
        WebMessageReceived?.Invoke(this, new NativeWebViewMessageReceivedEventArgs(message: null, json: message));
    }

    public async Task PostWebMessageAsStringAsync(string message, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.WebMessageChannel, nameof(PostWebMessageAsStringAsync));

        if (ShouldUseRuntimePath())
        {
            await EnsureRuntimeInitializedAsync(cancellationToken).ConfigureAwait(false);
            var payload = JsonSerializer.Serialize(message);
            var script = BuildDispatchScript(payload);
            var execution = await LinuxGtkDispatcher.InvokeAsync(
                () => LinuxNativeInterop.RunJavaScriptAsync(_webView, script, cancellationToken),
                cancellationToken).ConfigureAwait(false);

            await execution.ConfigureAwait(false);
            return;
        }

        EnsureStubInitialized();
        WebMessageReceived?.Invoke(this, new NativeWebViewMessageReceivedEventArgs(message, json: null));
    }

    public void OpenDevToolsWindow()
    {
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.DevTools, nameof(OpenDevToolsWindow));

        if (ShouldUseRuntimePath() && _webView != IntPtr.Zero && _isDevToolsEnabled)
        {
            _ = LinuxGtkDispatcher.InvokeAsync(() =>
            {
                var inspector = LinuxNativeInterop.webkit_web_view_get_inspector(_webView);
                if (inspector != IntPtr.Zero)
                {
                    LinuxNativeInterop.webkit_web_inspector_show(inspector);
                }
            });
        }

        OpenDevToolsRequested?.Invoke(this, new NativeWebViewOpenDevToolsRequestedEventArgs());
    }

    public async Task<NativeWebViewPrintResult> PrintAsync(
        NativeWebViewPrintSettings? settings = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureNotDisposed();

        if (!Features.Supports(NativeWebViewFeature.Printing))
        {
            return new NativeWebViewPrintResult(NativeWebViewPrintStatus.NotSupported);
        }

        if (ShouldUseRuntimePath())
        {
            await EnsureRuntimeInitializedAsync(cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(settings?.OutputPath))
            {
                return new NativeWebViewPrintResult(
                    NativeWebViewPrintStatus.NotSupported,
                    "The Linux WebKitGTK backend currently supports native print delegation, but not direct PDF export.");
            }

            try
            {
                await LinuxGtkDispatcher.InvokeAsync(() =>
                {
                    var printOperation = LinuxNativeInterop.webkit_print_operation_new(_webView);
                    if (printOperation == IntPtr.Zero)
                    {
                        throw new InvalidOperationException("Unable to create a WebKitGTK print operation.");
                    }

                    try
                    {
                        LinuxNativeInterop.webkit_print_operation_print(printOperation);
                    }
                    finally
                    {
                        LinuxNativeInterop.g_object_unref(printOperation);
                    }
                }, cancellationToken).ConfigureAwait(false);

                return new NativeWebViewPrintResult(NativeWebViewPrintStatus.Success);
            }
            catch (Exception ex)
            {
                return new NativeWebViewPrintResult(NativeWebViewPrintStatus.Failed, ex.Message);
            }
        }

        EnsureStubInitialized();
        return new NativeWebViewPrintResult(NativeWebViewPrintStatus.Success);
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

        _zoomFactor = zoomFactor;
        if (_webView != IntPtr.Zero)
        {
            _ = LinuxGtkDispatcher.InvokeAsync(() => LinuxNativeInterop.webkit_web_view_set_zoom_level(_webView, zoomFactor));
        }
    }

    public void SetUserAgent(string? userAgent)
    {
        EnsureNotDisposed();
        _userAgentString = userAgent;
        _ = ApplyRuntimeSettingsAsync();
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

        if (_webView != IntPtr.Zero)
        {
            _ = LinuxGtkDispatcher.InvokeAsync(() => LinuxNativeInterop.gtk_widget_grab_focus(_webView));
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

        return Task.FromResult<NativeWebViewRenderFrame?>(
            NativeWebViewBackendSupport.CreateSyntheticRenderFrame(
                Platform,
                _currentUrl,
                ref _frameSequence,
                renderMode,
                request.PixelWidth,
                request.PixelHeight));
    }

    public bool TryGetPlatformHandle(out NativePlatformHandle handle)
    {
        handle = _hostWindowXid != IntPtr.Zero
            ? new NativePlatformHandle(_hostWindowXid, "XID")
            : PlaceholderPlatformHandle;
        return true;
    }

    public bool TryGetViewHandle(out NativePlatformHandle handle)
    {
        handle = _webView != IntPtr.Zero
            ? new NativePlatformHandle(_webView, "WebKitWebView")
            : PlaceholderViewHandle;
        return true;
    }

    public bool TryGetControllerHandle(out NativePlatformHandle handle)
    {
        handle = _settings != IntPtr.Zero
            ? new NativePlatformHandle(_settings, "WebKitSettings")
            : PlaceholderControllerHandle;
        return true;
    }

    public NativePlatformHandle AttachToNativeParent(NativePlatformHandle parentHandle)
    {
        EnsureNotDisposed();

        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("Linux native control attachment can only run on Linux.");
        }

        if (parentHandle.Handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Parent native handle is invalid.");
        }

        if (!string.Equals(parentHandle.HandleDescriptor, "XID", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Linux native control attachment requires an XID parent, but received '{parentHandle.HandleDescriptor}'.");
        }

        if (_hostWindowXid != IntPtr.Zero)
        {
            if (_parentWindowXid == parentHandle.Handle)
            {
                return new NativePlatformHandle(_hostWindowXid, "XID");
            }

            DetachFromNativeParent();
        }

        var hostHandle = LinuxGtkDispatcher.InvokeAsync(
            CreateHostWindowOnGtkThread,
            CancellationToken.None).GetAwaiter().GetResult();

        _parentWindowXid = parentHandle.Handle;
        _gtkWindow = hostHandle.GtkWindow;
        _hostWindowXid = hostHandle.Xid;
        _attachmentTcs.TrySetResult(true);

        if (_runtimeInitializationRequested)
        {
            _ = TryInitializeRuntimeInBackgroundAsync();
        }

        return new NativePlatformHandle(_hostWindowXid, "XID");
    }

    public void DetachFromNativeParent()
    {
        EnsureNotDisposed();
        DetachFromNativeParentCore();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            DetachFromNativeParentCore();
        }
        catch
        {
            // Best-effort shutdown for native resources.
        }

        _disposed = true;
        _preparedEnvironmentOptions = null;
        _preparedControllerOptions = null;

        DestroyRequested?.Invoke(this, new NativeWebViewDestroyRequestedEventArgs("Disposed"));
        _runtimeGate.Dispose();
    }

    private void DetachFromNativeParentCore()
    {
        DestroyRuntimeHost();
        _parentWindowXid = IntPtr.Zero;
        _hostWindowXid = IntPtr.Zero;
        _attachmentTcs = CreatePendingAttachmentSource();
    }

    private void DestroyRuntimeHost()
    {
        if (!OperatingSystem.IsLinux())
        {
            _gtkWindow = IntPtr.Zero;
            _webView = IntPtr.Zero;
            _settings = IntPtr.Zero;
            _webContext = IntPtr.Zero;
            _websiteDataManager = IntPtr.Zero;
            _userContentManager = IntPtr.Zero;
            _isRuntimeInitialized = false;
            UpdateHistorySnapshot(canGoBack: false, canGoForward: false);
            return;
        }

        if (_gtkWindow == IntPtr.Zero &&
            _webContext == IntPtr.Zero &&
            _signalSubscriptions.Count == 0)
        {
            _isRuntimeInitialized = false;
            UpdateHistorySnapshot(canGoBack: false, canGoForward: false);
            return;
        }

        try
        {
            LinuxGtkDispatcher.InvokeAsync(() =>
            {
                foreach (var subscription in _signalSubscriptions)
                {
                    subscription.Dispose();
                }

                _signalSubscriptions.Clear();

                if (_gtkWindow != IntPtr.Zero)
                {
                    LinuxNativeInterop.gtk_widget_destroy(_gtkWindow);
                }

                if (_webContext != IntPtr.Zero)
                {
                    LinuxNativeInterop.g_object_unref(_webContext);
                }

                _gtkWindow = IntPtr.Zero;
                _webView = IntPtr.Zero;
                _settings = IntPtr.Zero;
                _webContext = IntPtr.Zero;
                _websiteDataManager = IntPtr.Zero;
                _userContentManager = IntPtr.Zero;
            }).GetAwaiter().GetResult();
        }
        catch
        {
            _signalSubscriptions.Clear();
            _gtkWindow = IntPtr.Zero;
            _webView = IntPtr.Zero;
            _settings = IntPtr.Zero;
            _webContext = IntPtr.Zero;
            _websiteDataManager = IntPtr.Zero;
            _userContentManager = IntPtr.Zero;
        }

        _isRuntimeInitialized = false;
        UpdateHistorySnapshot(canGoBack: false, canGoForward: false);
    }

    private static TaskCompletionSource<bool> CreatePendingAttachmentSource()
    {
        return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    [SupportedOSPlatform("linux")]
    private async Task TryInitializeRuntimeInBackgroundAsync()
    {
        try
        {
            await EnsureRuntimeInitializedAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Explicit InitializeAsync should surface failures. Background warmup is best effort.
        }
    }

    [SupportedOSPlatform("linux")]
    private async Task EnsureRuntimeInitializedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_webView != IntPtr.Zero)
        {
            return;
        }

        await _runtimeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_webView != IntPtr.Zero)
            {
                return;
            }

            await WaitForAttachmentAsync(cancellationToken).ConfigureAwait(false);
            EnsurePreparedInitializationOptions();

            await LinuxGtkDispatcher.InvokeAsync(
                InitializeRuntimeOnGtkThread,
                cancellationToken).ConfigureAwait(false);

            _isRuntimeInitialized = true;
            RaiseInitializedIfNeeded(
                success: true,
                initializationException: null,
                nativeObject: new NativePlatformHandle(_webView, "WebKitWebView"));
        }
        catch (Exception ex)
        {
            RaiseInitializedIfNeeded(success: false, initializationException: ex, nativeObject: null);
            throw;
        }
        finally
        {
            _runtimeGate.Release();
        }
    }

    private async Task WaitForAttachmentAsync(CancellationToken cancellationToken)
    {
        if (_hostWindowXid != IntPtr.Zero)
        {
            return;
        }

        if (!cancellationToken.CanBeCanceled)
        {
            await _attachmentTcs.Task.ConfigureAwait(false);
            return;
        }

        var cancellationSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(
            static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true),
            cancellationSource);

        var completed = await Task.WhenAny(_attachmentTcs.Task, cancellationSource.Task).ConfigureAwait(false);
        if (completed == cancellationSource.Task)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        await _attachmentTcs.Task.ConfigureAwait(false);
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

    [SupportedOSPlatformGuard("linux")]
    private bool ShouldUseRuntimePath()
    {
        return OperatingSystem.IsLinux() && _hostWindowXid != IntPtr.Zero;
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

        if (_historyIndex < _history.Count - 1)
        {
            _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
        }

        _history.Add(uri);
        _historyIndex = _history.Count - 1;
        _currentUrl = uri;
        _pendingNavigationUri = uri;
        UpdateHistorySnapshot(_historyIndex > 0, _historyIndex < _history.Count - 1);
        NavigationCompleted?.Invoke(this, new NativeWebViewNavigationCompletedEventArgs(uri, isSuccess: true, httpStatusCode: 200));
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

    private async Task ApplyRuntimeSettingsAsync()
    {
        if (_settings == IntPtr.Zero || !OperatingSystem.IsLinux())
        {
            return;
        }

        try
        {
            await LinuxGtkDispatcher.InvokeAsync(ApplyRuntimeSettingsOnGtkThread).ConfigureAwait(false);
        }
        catch
        {
            // Runtime settings updates are best effort.
        }
    }

    [SupportedOSPlatform("linux")]
    private void ApplyRuntimeSettingsOnGtkThread()
    {
        if (_settings == IntPtr.Zero)
        {
            return;
        }

        LinuxNativeInterop.webkit_settings_set_enable_developer_extras(_settings, _isDevToolsEnabled);
        LinuxNativeInterop.webkit_settings_set_user_agent(_settings, _userAgentString);

        if (_webView != IntPtr.Zero && _zoomFactor > 0)
        {
            LinuxNativeInterop.webkit_web_view_set_zoom_level(_webView, _zoomFactor);
        }
    }

    [SupportedOSPlatform("linux")]
    private void InitializeRuntimeOnGtkThread()
    {
        if (_gtkWindow == IntPtr.Zero)
        {
            throw new InvalidOperationException("Cannot initialize WebKitGTK without an attached GTK host window.");
        }

        if (_webView != IntPtr.Zero)
        {
            return;
        }

        var isPrivateMode = _preparedControllerOptions?.IsInPrivateModeEnabled == true;
        _webContext = isPrivateMode
            ? LinuxNativeInterop.webkit_web_context_new_ephemeral()
            : LinuxNativeInterop.webkit_web_context_new();

        if (_webContext == IntPtr.Zero)
        {
            throw new InvalidOperationException("Unable to create a WebKitGTK web context.");
        }

        ApplyEnvironmentOptionsToContextOnGtkThread(_webContext, _preparedEnvironmentOptions, isPrivateMode);

        _websiteDataManager = LinuxNativeInterop.webkit_web_context_get_website_data_manager(_webContext);
        _webView = LinuxNativeInterop.webkit_web_view_new_with_context(_webContext);

        if (_webView == IntPtr.Zero)
        {
            throw new InvalidOperationException("Unable to create a WebKitGTK web view.");
        }

        _settings = LinuxNativeInterop.webkit_web_view_get_settings(_webView);
        _userContentManager = LinuxNativeInterop.webkit_web_view_get_user_content_manager(_webView);

        if (_settings == IntPtr.Zero)
        {
            throw new InvalidOperationException("Unable to access WebKitGTK settings.");
        }

        if (_userContentManager == IntPtr.Zero)
        {
            throw new InvalidOperationException("Unable to access WebKitGTK user content manager.");
        }

        if (!LinuxNativeInterop.webkit_user_content_manager_register_script_message_handler(_userContentManager, ScriptMessageHandlerName))
        {
            throw new InvalidOperationException("Unable to register the WebKitGTK script message handler.");
        }

        var bridgeScript = LinuxNativeInterop.webkit_user_script_new(
            JavaScriptBridgeSource,
            LinuxNativeInterop.WebKitUserContentInjectedFrames.AllFrames,
            LinuxNativeInterop.WebKitUserScriptInjectionTime.DocumentStart,
            IntPtr.Zero,
            IntPtr.Zero);

        if (bridgeScript != IntPtr.Zero)
        {
            try
            {
                LinuxNativeInterop.webkit_user_content_manager_add_script(_userContentManager, bridgeScript);
            }
            finally
            {
                LinuxNativeInterop.webkit_user_script_unref(bridgeScript);
            }
        }

        _signalSubscriptions.Add(LinuxNativeInterop.ConnectSignal(_webView, "load-changed", new LinuxNativeInterop.LoadChangedSignal(OnLoadChanged)));
        _signalSubscriptions.Add(LinuxNativeInterop.ConnectSignal(_webView, "load-failed", new LinuxNativeInterop.LoadFailedSignal(OnLoadFailed)));
        _signalSubscriptions.Add(LinuxNativeInterop.ConnectSignal(_webView, "decide-policy", new LinuxNativeInterop.DecidePolicySignal(OnDecidePolicy)));
        _signalSubscriptions.Add(LinuxNativeInterop.ConnectSignal(_webView, "close", new LinuxNativeInterop.CloseSignal(OnCloseRequested)));
        _signalSubscriptions.Add(LinuxNativeInterop.ConnectSignal(_webView, "context-menu", new LinuxNativeInterop.ContextMenuSignal(OnContextMenu)));
        _signalSubscriptions.Add(LinuxNativeInterop.ConnectSignal(_userContentManager, $"script-message-received::{ScriptMessageHandlerName}", new LinuxNativeInterop.ScriptMessageReceivedSignal(OnScriptMessageReceived)));

        LinuxNativeInterop.gtk_container_add(_gtkWindow, _webView);
        LinuxNativeInterop.gtk_widget_show_all(_gtkWindow);
        ApplyRuntimeSettingsOnGtkThread();
        SyncNavigationSnapshotFromRuntimeOnGtkThread();

        if (_pendingNavigationUri is not null)
        {
            LinuxNativeInterop.webkit_web_view_load_uri(_webView, ToNavigationString(_pendingNavigationUri));
        }
    }

    [SupportedOSPlatform("linux")]
    private void ApplyEnvironmentOptionsToContextOnGtkThread(
        nint webContext,
        NativeWebViewEnvironmentOptions? options,
        bool isPrivateMode)
    {
        options ??= new NativeWebViewEnvironmentOptions();

        if (!string.IsNullOrWhiteSpace(options.Language))
        {
            var languages = ParsePreferredLanguages(options.Language);
            if (languages.Count > 0)
            {
                using var languagePointers = new LinuxUtf8StringArray(languages);
                LinuxNativeInterop.webkit_web_context_set_preferred_languages(webContext, languagePointers.Pointer);
            }
        }

        var websiteDataManager = LinuxNativeInterop.webkit_web_context_get_website_data_manager(webContext);
        if (websiteDataManager == IntPtr.Zero)
        {
            return;
        }

        if (Features.Supports(NativeWebViewFeature.ProxyConfiguration))
        {
            var proxySettings = NativeWebViewLinuxProxySettingsBuilder.Build(options.Proxy);
            if (proxySettings is not null)
            {
                using var ignoreHosts = new LinuxUtf8StringArray(proxySettings.IgnoreHosts);
                var nativeProxySettings = LinuxNativeInterop.webkit_network_proxy_settings_new(
                    proxySettings.DefaultProxyUri,
                    ignoreHosts.Pointer);

                try
                {
                    LinuxNativeInterop.webkit_website_data_manager_set_network_proxy_settings(
                        websiteDataManager,
                        LinuxNativeInterop.WebKitNetworkProxyMode.Custom,
                        nativeProxySettings);
                }
                finally
                {
                    if (nativeProxySettings != IntPtr.Zero)
                    {
                        LinuxNativeInterop.webkit_network_proxy_settings_free(nativeProxySettings);
                    }
                }
            }
        }

        if (!isPrivateMode && !string.IsNullOrWhiteSpace(options.CookieDataFolder))
        {
            var cookieStoragePath = ResolveCookieStoragePath(options.CookieDataFolder);
            var cookieDirectory = Path.GetDirectoryName(cookieStoragePath);
            if (!string.IsNullOrWhiteSpace(cookieDirectory))
            {
                Directory.CreateDirectory(cookieDirectory);
            }

            var cookieManager = LinuxNativeInterop.webkit_website_data_manager_get_cookie_manager(websiteDataManager);
            if (cookieManager != IntPtr.Zero)
            {
                LinuxNativeInterop.webkit_cookie_manager_set_persistent_storage(
                    cookieManager,
                    cookieStoragePath,
                    LinuxNativeInterop.WebKitCookiePersistentStorage.Sqlite);
            }
        }
    }

    [SupportedOSPlatform("linux")]
    private void SyncNavigationSnapshotFromRuntimeOnGtkThread()
    {
        _currentUrl = TryCreateUri(LinuxNativeInterop.ConvertUtf8Pointer(LinuxNativeInterop.webkit_web_view_get_uri(_webView))) ?? _currentUrl;
        _pendingNavigationUri = _currentUrl;
        UpdateHistorySnapshot(
            LinuxNativeInterop.webkit_web_view_can_go_back(_webView),
            LinuxNativeInterop.webkit_web_view_can_go_forward(_webView));
    }

    [SupportedOSPlatform("linux")]
    private void OnLoadChanged(IntPtr webView, LinuxNativeInterop.WebKitLoadEvent loadEvent, IntPtr userData)
    {
        switch (loadEvent)
        {
            case LinuxNativeInterop.WebKitLoadEvent.Redirected:
                NavigationStarted?.Invoke(
                    this,
                    new NativeWebViewNavigationStartedEventArgs(
                        TryCreateUri(LinuxNativeInterop.ConvertUtf8Pointer(LinuxNativeInterop.webkit_web_view_get_uri(webView))),
                        isRedirected: true));
                break;

            case LinuxNativeInterop.WebKitLoadEvent.Committed:
                SyncNavigationSnapshotFromRuntimeOnGtkThread();
                break;

            case LinuxNativeInterop.WebKitLoadEvent.Finished:
                SyncNavigationSnapshotFromRuntimeOnGtkThread();
                NavigationCompleted?.Invoke(
                    this,
                    new NativeWebViewNavigationCompletedEventArgs(_currentUrl, isSuccess: true, httpStatusCode: 200));
                break;
        }
    }

    private int OnLoadFailed(IntPtr webView, LinuxNativeInterop.WebKitLoadEvent loadEvent, IntPtr failingUri, IntPtr error, IntPtr userData)
    {
        var uri = TryCreateUri(LinuxNativeInterop.ConvertUtf8Pointer(failingUri));
        var message = LinuxNativeInterop.GetErrorMessageAndFree(error);
        _currentUrl = uri ?? _currentUrl;
        _pendingNavigationUri = _currentUrl;
        UpdateHistorySnapshot(
            LinuxNativeInterop.webkit_web_view_can_go_back(webView),
            LinuxNativeInterop.webkit_web_view_can_go_forward(webView));

        NavigationCompleted?.Invoke(
            this,
            new NativeWebViewNavigationCompletedEventArgs(uri, isSuccess: false, error: message));

        return 0;
    }

    private int OnDecidePolicy(IntPtr webView, IntPtr decision, LinuxNativeInterop.WebKitPolicyDecisionType decisionType, IntPtr userData)
    {
        var request = decisionType switch
        {
            LinuxNativeInterop.WebKitPolicyDecisionType.NavigationAction => LinuxNativeInterop.webkit_navigation_policy_decision_get_request(decision),
            LinuxNativeInterop.WebKitPolicyDecisionType.NewWindowAction => LinuxNativeInterop.webkit_navigation_policy_decision_get_request(decision),
            LinuxNativeInterop.WebKitPolicyDecisionType.Response => LinuxNativeInterop.webkit_response_policy_decision_get_request(decision),
            _ => IntPtr.Zero,
        };

        var uri = request == IntPtr.Zero
            ? null
            : TryCreateUri(LinuxNativeInterop.ConvertUtf8Pointer(LinuxNativeInterop.webkit_uri_request_get_uri(request)));

        switch (decisionType)
        {
            case LinuxNativeInterop.WebKitPolicyDecisionType.NavigationAction:
            {
                var args = new NativeWebViewNavigationStartedEventArgs(uri, isRedirected: false);
                NavigationStarted?.Invoke(this, args);
                if (args.Cancel)
                {
                    LinuxNativeInterop.webkit_policy_decision_ignore(decision);
                    return 1;
                }

                break;
            }

            case LinuxNativeInterop.WebKitPolicyDecisionType.NewWindowAction:
            {
                var args = new NativeWebViewNewWindowRequestedEventArgs(uri);
                NewWindowRequested?.Invoke(this, args);
                if (args.Handled)
                {
                    LinuxNativeInterop.webkit_policy_decision_ignore(decision);
                    return 1;
                }

                break;
            }

            case LinuxNativeInterop.WebKitPolicyDecisionType.Response:
            {
                var method = request == IntPtr.Zero
                    ? "GET"
                    : LinuxNativeInterop.ConvertUtf8Pointer(LinuxNativeInterop.webkit_uri_request_get_http_method(request)) ?? "GET";

                var args = new NativeWebViewResourceRequestedEventArgs(uri, method);
                WebResourceRequested?.Invoke(this, args);
                if (args.Handled)
                {
                    LinuxNativeInterop.webkit_policy_decision_ignore(decision);
                    return 1;
                }

                break;
            }
        }

        return 0;
    }

    private void OnScriptMessageReceived(IntPtr manager, IntPtr jsResult, IntPtr userData)
    {
        var json = LinuxNativeInterop.ConvertJavaScriptResultToJson(jsResult);
        string? message = null;

        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind == JsonValueKind.String)
                {
                    message = document.RootElement.GetString();
                }
            }
            catch
            {
                // Keep JSON only when payload is not parseable as a JSON string.
            }
        }

        WebMessageReceived?.Invoke(this, new NativeWebViewMessageReceivedEventArgs(message, json));
    }

    private int OnContextMenu(IntPtr webView, IntPtr contextMenu, IntPtr eventHandle, IntPtr hitTestResult, IntPtr userData)
    {
        if (!_isContextMenuEnabled)
        {
            return 1;
        }

        var args = new NativeWebViewContextMenuRequestedEventArgs(0, 0);
        ContextMenuRequested?.Invoke(this, args);
        return args.Handled ? 1 : 0;
    }

    private void OnCloseRequested(IntPtr webView, IntPtr userData)
    {
        DestroyRequested?.Invoke(this, new NativeWebViewDestroyRequestedEventArgs("WindowCloseRequested"));
    }

    private static string BuildDispatchScript(string payloadExpression)
    {
        return $"(function() {{ var payload = {payloadExpression}; if (window.chrome && window.chrome.webview && typeof window.chrome.webview.__dispatchMessage === 'function') {{ window.chrome.webview.__dispatchMessage(payload); }} else {{ window.dispatchEvent(new MessageEvent(\"message\", {{ data: payload }})); }} return null; }})();";
    }

    private static string ToNavigationString(Uri uri)
    {
        return uri.IsAbsoluteUri
            ? uri.AbsoluteUri
            : uri.ToString();
    }

    private static IReadOnlyList<string> ParsePreferredLanguages(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return Array.Empty<string>();
        }

        var values = language.Split([',', ';', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return values.Length == 0
            ? Array.Empty<string>()
            : values;
    }

    private static string ResolveCookieStoragePath(string cookieDataFolder)
    {
        var fullPath = Path.GetFullPath(cookieDataFolder);

        if (Path.HasExtension(fullPath))
        {
            return fullPath;
        }

        return Path.Combine(fullPath, "cookies.sqlite");
    }

    private static Uri? TryCreateUri(string? uri)
    {
        return Uri.TryCreate(uri, UriKind.RelativeOrAbsolute, out var parsed)
            ? parsed
            : null;
    }

    private static LinuxHostWindowHandle CreateHostWindowOnGtkThread()
    {
        var gtkWindow = LinuxNativeInterop.gtk_window_new(LinuxNativeInterop.GtkWindowType.Popup);
        if (gtkWindow == IntPtr.Zero)
        {
            throw new InvalidOperationException("Unable to create the GTK host window.");
        }

        LinuxNativeInterop.gtk_window_set_decorated(gtkWindow, false);
        LinuxNativeInterop.gtk_window_set_resizable(gtkWindow, true);
        LinuxNativeInterop.gtk_widget_realize(gtkWindow);
        LinuxNativeInterop.gtk_widget_show_all(gtkWindow);

        var gdkWindow = LinuxNativeInterop.gtk_widget_get_window(gtkWindow);
        if (gdkWindow == IntPtr.Zero)
        {
            LinuxNativeInterop.gtk_widget_destroy(gtkWindow);
            throw new InvalidOperationException("GTK did not expose a realized GDK window for the Linux host.");
        }

        var xid = LinuxNativeInterop.gdk_x11_window_get_xid(gdkWindow);
        if (xid == IntPtr.Zero)
        {
            LinuxNativeInterop.gtk_widget_destroy(gtkWindow);
            throw new InvalidOperationException("GTK did not expose an X11 child window for the Linux host.");
        }

        return new LinuxHostWindowHandle(gtkWindow, xid);
    }

    private void EnsureFeature(NativeWebViewFeature feature, string operation)
    {
        if (!Features.Supports(feature))
        {
            throw new NotSupportedException($"{operation} is not supported on platform '{Platform}'.");
        }
    }

    private void EnsureNotDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private readonly record struct LinuxHostWindowHandle(nint GtkWindow, nint Xid);
}
