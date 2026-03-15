using System.Text;
using System.Text.Json;
using System.Runtime.Versioning;
using Android.Graphics;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Webkit;
using Android.Widget;
using Java.Interop;
using NativeWebView.Core;
using NativeWebView.Interop;
using Object = Java.Lang.Object;

namespace NativeWebView.Platform.Android;

[SupportedOSPlatform("android24.0")]
public sealed class AndroidNativeWebViewBackend
    : INativeWebViewBackend,
      INativeWebViewFrameSource,
      INativeWebViewPlatformHandleProvider,
      INativeWebViewInstanceConfigurationTarget,
      INativeWebViewNativeControlAttachment
{
    private const string ScriptBridgeName = "nativewebview";

    private static readonly NativePlatformHandle PlaceholderPlatformHandle = new((nint)0x5001, "android.view.View");
    private static readonly NativePlatformHandle PlaceholderViewHandle = new((nint)0x5002, "android.webkit.WebView");
    private static readonly NativePlatformHandle PlaceholderControllerHandle = new((nint)0x5003, "android.webkit.WebViewClient");
    private static readonly ConsumingLongClickListener DisabledContextMenuListener = new();

    private static readonly string JavaScriptBridgeSource = """
        (() => {
          const native = window.nativewebview;
          const chromeRoot = window.chrome = window.chrome || {};
          const webview = chromeRoot.webview = chromeRoot.webview || {};
          if (webview.__nativeBridgeReady) {
            return;
          }

          webview.__nativeBridgeReady = true;
          const listeners = webview.__listeners = webview.__listeners || [];

          webview.postMessage = (message) => {
            if (!native || typeof native.postMessage !== 'function') {
              return;
            }

            if (typeof message === 'string') {
              native.postMessage('string', message);
              return;
            }

            try {
              native.postMessage('json', JSON.stringify(message));
            } catch (error) {
              native.postMessage('string', String(message));
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

          if (!webview.__nativeOriginalOpen) {
            webview.__nativeOriginalOpen = typeof window.open === 'function'
              ? window.open.bind(window)
              : null;
          }

          window.open = (url, target, features) => {
            if (native && typeof native.openWindow === 'function') {
              native.openWindow(url == null ? '' : String(url));
              return window;
            }

            return webview.__nativeOriginalOpen
              ? webview.__nativeOriginalOpen(url, target, features)
              : null;
          };
        })();
        """;

    private readonly Handler _mainHandler = new(Looper.MainLooper ?? throw new InvalidOperationException("Android main looper is unavailable."));
    private readonly SemaphoreSlim _runtimeGate = new(1, 1);
    private readonly List<Uri> _history = [];
    private readonly INativeWebViewCommandManager _commandManager = NativeWebViewBackendSupport.NoopCommandManagerInstance;
    private readonly INativeWebViewCookieManager _cookieManager = NativeWebViewBackendSupport.NoopCookieManagerInstance;

    private TaskCompletionSource<bool> _attachmentTcs = CreatePendingAttachmentSource();
    private NativeWebViewInstanceConfiguration _instanceConfiguration = new();

    private NativeWebViewEnvironmentOptions? _preparedEnvironmentOptions;
    private NativeWebViewControllerOptions? _preparedControllerOptions;

    private View? _parentView;
    private FrameLayout? _containerView;
    private WebView? _webView;
    private AndroidNavigationClient? _webViewClient;
    private AndroidChromeClient? _webChromeClient;
    private AndroidScriptBridge? _scriptBridge;

    private Uri? _currentUrl;
    private Uri? _pendingNavigationUri;

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
    private bool _activeNavigationFailed;

    private double _zoomFactor;
    private double _appliedZoomFactor = 1.0;
    private string? _headerString;
    private string? _userAgentString;
    private string? _defaultUserAgentString;
    private string? _suppressedStartedNavigationKey;

    public AndroidNativeWebViewBackend()
    {
        Platform = NativeWebViewPlatform.Android;
        Features = AndroidPlatformFeatures.Instance;
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

            if (_webView is not null)
            {
                _ = InvokeOnMainThreadAsync(ApplyContextMenuModeOnMainThread);
            }
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

            if (_webView is not null)
            {
                _ = InvokeOnMainThreadAsync(ApplyZoomControlModeOnMainThread);
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
#pragma warning restore CS0067

    public event EventHandler<NativeWebViewNewWindowRequestedEventArgs>? NewWindowRequested;

#pragma warning disable CS0067
    public event EventHandler<NativeWebViewResourceRequestedEventArgs>? WebResourceRequested;

    public event EventHandler<NativeWebViewContextMenuRequestedEventArgs>? ContextMenuRequested;
#pragma warning restore CS0067

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

        if (OperatingSystem.IsAndroid())
        {
            _runtimeInitializationRequested = true;

            if (_containerView is not null)
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

            if (_webView is not null)
            {
                _ = InvokeOnMainThreadAsync(() => NavigateRuntimeCore(uri));
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
            if (_webView is not null)
            {
                _ = InvokeOnMainThreadAsync(static webView => webView.Reload(), _webView);
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

        if (ShouldUseRuntimePath() && _webView is not null)
        {
            _ = InvokeOnMainThreadAsync(static webView => webView.StopLoading(), _webView);
        }
    }

    public void GoBack()
    {
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.EmbeddedView, nameof(GoBack));

        if (ShouldUseRuntimePath())
        {
            if (_webView is not null && _webView.CanGoBack())
            {
                _ = InvokeOnMainThreadAsync(static webView => webView.GoBack(), _webView);
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
            if (_webView is not null && _webView.CanGoForward())
            {
                _ = InvokeOnMainThreadAsync(static webView => webView.GoForward(), _webView);
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
            return await InvokeOnMainThreadAsync(
                () => EvaluateJavascriptCoreAsync(script),
                cancellationToken).ConfigureAwait(false);
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
        var jsonMessage = NativeWebViewBackendSupport.NormalizeJsonMessagePayload(message);

        if (ShouldUseRuntimePath())
        {
            await EnsureRuntimeInitializedAsync(cancellationToken).ConfigureAwait(false);
            var script = BuildDispatchScript(jsonMessage);
            _ = await InvokeOnMainThreadAsync(
                () => EvaluateJavascriptCoreAsync(script),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        EnsureStubInitialized();
        WebMessageReceived?.Invoke(this, new NativeWebViewMessageReceivedEventArgs(message: null, json: jsonMessage));
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
            var script = BuildDispatchScript(JsonSerializer.Serialize(message));
            _ = await InvokeOnMainThreadAsync(
                () => EvaluateJavascriptCoreAsync(script),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        EnsureStubInitialized();
        WebMessageReceived?.Invoke(this, new NativeWebViewMessageReceivedEventArgs(message, json: null));
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
        _zoomFactor = zoomFactor > 0 ? zoomFactor : 1.0;

        if (_webView is not null)
        {
            _ = InvokeOnMainThreadAsync(ApplyZoomFactorOnMainThread);
        }
    }

    public void SetUserAgent(string? userAgent)
    {
        EnsureNotDisposed();
        _userAgentString = userAgent;

        if (_webView is not null)
        {
            _ = InvokeOnMainThreadAsync(ApplyUserAgentOnMainThread);
        }
    }

    public void SetHeader(string? header)
    {
        EnsureNotDisposed();
        _headerString = string.IsNullOrWhiteSpace(header)
            ? null
            : header.Trim();
    }

    public bool TryGetCommandManager(out INativeWebViewCommandManager? commandManager)
    {
        commandManager = _commandManager;
        return true;
    }

    public bool TryGetCookieManager(out INativeWebViewCookieManager? cookieManager)
    {
        cookieManager = _cookieManager;
        return true;
    }

    public void MoveFocus(NativeWebViewFocusMoveDirection direction)
    {
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.EmbeddedView, nameof(MoveFocus));

        if (_webView is not null)
        {
            _ = InvokeOnMainThreadAsync(
                webView =>
                {
                    _ = direction;
                    webView.RequestFocus();
                },
                _webView);
        }
    }

    public bool SupportsRenderMode(NativeWebViewRenderMode renderMode)
    {
        return renderMode is NativeWebViewRenderMode.GpuSurface or NativeWebViewRenderMode.Offscreen;
    }

    public Task<NativeWebViewRenderFrame?> CaptureFrameAsync(
        NativeWebViewRenderMode renderMode,
        NativeWebViewRenderFrameRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureNotDisposed();

        if (!SupportsRenderMode(renderMode) || renderMode == NativeWebViewRenderMode.Embedded)
        {
            return Task.FromResult<NativeWebViewRenderFrame?>(null);
        }

        var frame = NativeWebViewBackendSupport.CreateSyntheticRenderFrame(
            Platform,
            _currentUrl,
            ref _frameSequence,
            renderMode,
            request.PixelWidth,
            request.PixelHeight);

        return Task.FromResult<NativeWebViewRenderFrame?>(frame);
    }

    public bool TryGetPlatformHandle(out NativePlatformHandle handle)
    {
        handle = _containerView is not null
            ? new NativePlatformHandle((nint)_containerView.Handle, "android.view.View")
            : PlaceholderPlatformHandle;
        return true;
    }

    public bool TryGetViewHandle(out NativePlatformHandle handle)
    {
        handle = _webView is not null
            ? new NativePlatformHandle((nint)_webView.Handle, "android.webkit.WebView")
            : PlaceholderViewHandle;
        return true;
    }

    public bool TryGetControllerHandle(out NativePlatformHandle handle)
    {
        handle = _webViewClient is not null
            ? new NativePlatformHandle((nint)_webViewClient.Handle, "android.webkit.WebViewClient")
            : PlaceholderControllerHandle;
        return true;
    }

    public NativePlatformHandle AttachToNativeParent(NativePlatformHandle parentHandle)
    {
        EnsureNotDisposed();

        if (!OperatingSystem.IsAndroid())
        {
            throw new PlatformNotSupportedException("Android native control attachment can only run on Android.");
        }

        if (parentHandle.Handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Parent native handle is invalid.");
        }

        if (!string.Equals(parentHandle.HandleDescriptor, "android.view.View", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Android native control attachment requires an android.view.View parent, but received '{parentHandle.HandleDescriptor}'.");
        }

        if (_containerView is not null && _parentView is not null && (nint)_parentView.Handle == parentHandle.Handle)
        {
            return new NativePlatformHandle((nint)_containerView.Handle, "android.view.View");
        }

        if (_containerView is not null)
        {
            DetachFromNativeParent();
        }

        InvokeOnMainThread(
            () => AttachToNativeParentCore(parentHandle));

        _attachmentTcs.TrySetResult(true);
        if (_runtimeInitializationRequested)
        {
            _ = TryInitializeRuntimeInBackgroundAsync();
        }

        return new NativePlatformHandle((nint)_containerView!.Handle, "android.view.View");
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

    private static TaskCompletionSource<bool> CreatePendingAttachmentSource()
    {
        return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

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

    private async Task EnsureRuntimeInitializedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!ShouldUseRuntimePath())
        {
            EnsureStubInitialized();
            return;
        }

        if (_webView is not null)
        {
            return;
        }

        await _runtimeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_webView is not null)
            {
                return;
            }

            await WaitForAttachmentAsync(cancellationToken).ConfigureAwait(false);
            EnsurePreparedInitializationOptions();

            await InvokeOnMainThreadAsync(InitializeRuntimeOnMainThread, cancellationToken).ConfigureAwait(false);

            _isRuntimeInitialized = true;
            RaiseInitializedIfNeeded(
                success: true,
                initializationException: null,
                nativeObject: new NativePlatformHandle((nint)_webView!.Handle, "android.webkit.WebView"));
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

    private Task WaitForAttachmentAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _containerView is not null
            ? Task.CompletedTask
            : _attachmentTcs.Task.WaitAsync(cancellationToken);
    }

    private void InitializeRuntimeOnMainThread()
    {
        if (_containerView is null)
        {
            throw new InvalidOperationException("Runtime initialization requires an attached Android host view.");
        }

        if (_webView is not null)
        {
            return;
        }

        var environmentOptions = _preparedEnvironmentOptions?.Clone() ?? new NativeWebViewEnvironmentOptions();
        var controllerOptions = _preparedControllerOptions?.Clone() ?? new NativeWebViewControllerOptions();
        ValidateRuntimeConfiguration(environmentOptions);

        _scriptBridge = new AndroidScriptBridge(this);
        _webViewClient = new AndroidNavigationClient(this);
        _webChromeClient = new AndroidChromeClient(this);

        _webView = new WebView(_containerView.Context)
        {
            LayoutParameters = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent),
            Focusable = true,
            FocusableInTouchMode = true,
        };

        _webView.SetBackgroundColor(Color.Transparent);
        _webView.SetWebViewClient(_webViewClient);
        _webView.SetWebChromeClient(_webChromeClient);
        _webView.AddJavascriptInterface(_scriptBridge, ScriptBridgeName);

        var settings = _webView.Settings
            ?? throw new InvalidOperationException("Android WebView settings are unavailable.");

        settings.JavaScriptEnabled = true;
        settings.DomStorageEnabled = true;
        settings.JavaScriptCanOpenWindowsAutomatically = true;
        settings.SetSupportMultipleWindows(true);
        settings.MediaPlaybackRequiresUserGesture = false;
        settings.AllowFileAccess = true;

        _defaultUserAgentString = settings.UserAgentString;
        _containerView.AddView(_webView);

        ApplyZoomControlModeOnMainThread();
        ApplyContextMenuModeOnMainThread();
        ApplyUserAgentOnMainThread();
        ApplyZoomFactorOnMainThread();
        _ = controllerOptions;

        UpdateHistorySnapshot(_webView.CanGoBack(), _webView.CanGoForward());

        if (_pendingNavigationUri is not null)
        {
            NavigateRuntimeCore(_pendingNavigationUri);
        }
        else if (_currentUrl is not null)
        {
            NavigateRuntimeCore(_currentUrl);
        }
    }

    private void AttachToNativeParentCore(NativePlatformHandle parentHandle)
    {
        var parentView = Object.GetObject<View>((IntPtr)parentHandle.Handle, JniHandleOwnership.DoNotTransfer)
            ?? throw new InvalidOperationException("Failed to resolve the Android parent view handle.");
        var parentGroup = parentView as ViewGroup
            ?? throw new InvalidOperationException("Android native control attachment requires a ViewGroup parent handle.");

        _parentView = parentView;
        _containerView = new FrameLayout(parentView.Context)
        {
            LayoutParameters = new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent),
        };

        _containerView.SetBackgroundColor(Color.Transparent);
        parentGroup.AddView(_containerView);
    }

    private void DetachFromNativeParentCore()
    {
        InvokeOnMainThread(DetachOnMainThread);
        _attachmentTcs = CreatePendingAttachmentSource();
    }

    private void DetachOnMainThread()
    {
        if (_webView is not null)
        {
            _webView.StopLoading();
            _webView.SetWebChromeClient(null);
            _webView.SetWebViewClient(null);
            _webView.RemoveJavascriptInterface(ScriptBridgeName);

            if (_webView.Parent is ViewGroup parentGroup)
            {
                parentGroup.RemoveView(_webView);
            }

            _webView.Destroy();
            _webView.Dispose();
            _webView = null;
        }

        _webViewClient?.Dispose();
        _webViewClient = null;

        _webChromeClient?.Dispose();
        _webChromeClient = null;

        _scriptBridge?.Dispose();
        _scriptBridge = null;

        if (_containerView is not null)
        {
            _containerView.RemoveAllViews();

            if (_containerView.Parent is ViewGroup parentGroup)
            {
                parentGroup.RemoveView(_containerView);
            }

            _containerView.Dispose();
            _containerView = null;
        }

        _parentView = null;
        _isRuntimeInitialized = false;
        UpdateHistorySnapshot(canGoBack: false, canGoForward: false);
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

    private bool ShouldUseRuntimePath()
    {
        return OperatingSystem.IsAndroid() && _containerView is not null;
    }

    private void ValidateRuntimeConfiguration(NativeWebViewEnvironmentOptions environmentOptions)
    {
        var proxyConfiguration = NativeWebViewProxyConfigurationResolver.Resolve(environmentOptions.Proxy);
        if (proxyConfiguration is not null)
        {
            throw new PlatformNotSupportedException(
                "Android per-instance proxy configuration is not supported by the current runtime path. AndroidX WebKit proxy override is app-wide and is not integrated by this repo.");
        }
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

    private void NavigateRuntimeCore(Uri uri)
    {
        if (_webView is null)
        {
            return;
        }

        var started = new NativeWebViewNavigationStartedEventArgs(uri, isRedirected: false);
        NavigationStarted?.Invoke(this, started);
        if (started.Cancel)
        {
            return;
        }

        _pendingNavigationUri = uri;
        _suppressedStartedNavigationKey = GetNavigationKey(uri);
        _activeNavigationFailed = false;

        var target = GetNavigationTarget(uri);
        var headers = BuildNavigationHeaders(uri);
        if (headers is not null)
        {
            _webView.LoadUrl(target, headers);
            return;
        }

        _webView.LoadUrl(target);
    }

    private Dictionary<string, string>? BuildNavigationHeaders(Uri uri)
    {
        if (string.IsNullOrWhiteSpace(_headerString) ||
            !uri.IsAbsoluteUri ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        Dictionary<string, string>? headers = null;
        foreach (var rawLine in _headerString.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = rawLine.IndexOf(':');
            if (separator <= 0 || separator >= rawLine.Length - 1)
            {
                continue;
            }

            var headerName = rawLine[..separator].Trim();
            var headerValue = rawLine[(separator + 1)..].Trim();
            if (headerName.Length == 0 || headerValue.Length == 0)
            {
                continue;
            }

            headers ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            headers[headerName] = headerValue;
        }

        return headers;
    }

    private static string GetNavigationTarget(Uri uri)
    {
        return uri.IsAbsoluteUri
            ? uri.AbsoluteUri
            : uri.ToString();
    }

    private async Task<string?> EvaluateJavascriptCoreAsync(string script)
    {
        if (_webView is null)
        {
            return "null";
        }

        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var callback = new ScriptValueCallback(tcs);
        _webView.EvaluateJavascript(script, callback);
        return await tcs.Task.ConfigureAwait(false);
    }

    private void ApplyZoomControlModeOnMainThread()
    {
        if (_webView?.Settings is null)
        {
            return;
        }

        _webView.Settings.SetSupportZoom(_isZoomControlEnabled);
        _webView.Settings.BuiltInZoomControls = _isZoomControlEnabled;
        _webView.Settings.DisplayZoomControls = _isZoomControlEnabled;
    }

    private void ApplyContextMenuModeOnMainThread()
    {
        if (_webView is null)
        {
            return;
        }

        _webView.LongClickable = _isContextMenuEnabled;
        _webView.HapticFeedbackEnabled = _isContextMenuEnabled;
        _webView.SetOnLongClickListener(_isContextMenuEnabled ? null : DisabledContextMenuListener);
    }

    private void ApplyZoomFactorOnMainThread()
    {
        if (_webView is null)
        {
            return;
        }

        var targetZoomFactor = _zoomFactor > 0 ? _zoomFactor : 1.0;
        if (_webView.Url is null)
        {
            _webView.SetInitialScale((int)Math.Round(targetZoomFactor * 100, MidpointRounding.AwayFromZero));
            _appliedZoomFactor = targetZoomFactor;
            return;
        }

        if (Math.Abs(targetZoomFactor - _appliedZoomFactor) < 0.001d)
        {
            return;
        }

        var ratio = targetZoomFactor / (_appliedZoomFactor <= 0 ? 1.0 : _appliedZoomFactor);
        if (ratio > 0)
        {
            _webView.ZoomBy((float)ratio);
            _appliedZoomFactor = targetZoomFactor;
        }
    }

    private void ApplyUserAgentOnMainThread()
    {
        if (_webView?.Settings is null)
        {
            return;
        }

        _webView.Settings.UserAgentString = string.IsNullOrWhiteSpace(_userAgentString)
            ? _defaultUserAgentString
            : _userAgentString;
    }

    private void OnRuntimeNavigationStarted(string? url, bool isRedirected)
    {
        var uri = CreateUri(url) ?? _pendingNavigationUri ?? _currentUrl;
        _currentUrl = uri ?? _currentUrl;
        _pendingNavigationUri = uri ?? _pendingNavigationUri;
        _activeNavigationFailed = false;

        var navigationKey = GetNavigationKey(uri);
        if (_suppressedStartedNavigationKey is not null &&
            string.Equals(_suppressedStartedNavigationKey, navigationKey, StringComparison.Ordinal))
        {
            _suppressedStartedNavigationKey = null;
            return;
        }

        NavigationStarted?.Invoke(this, new NativeWebViewNavigationStartedEventArgs(uri, isRedirected));
    }

    private void OnRuntimeNavigationCompleted(WebView webView, string? url)
    {
        _currentUrl = CreateUri(url) ?? CreateUri(webView.Url) ?? _pendingNavigationUri ?? _currentUrl;
        _pendingNavigationUri = _currentUrl;
        UpdateHistorySnapshot(webView.CanGoBack(), webView.CanGoForward());

        try
        {
            webView.EvaluateJavascript(JavaScriptBridgeSource, null);
        }
        catch
        {
            // Best-effort bridge reinjection after navigation.
        }

        if (_activeNavigationFailed)
        {
            _activeNavigationFailed = false;
            return;
        }

        NavigationCompleted?.Invoke(this, new NativeWebViewNavigationCompletedEventArgs(_currentUrl, isSuccess: true, httpStatusCode: 200));
    }

    private void OnRuntimeNavigationFailed(WebView? webView, Uri? uri, int? httpStatusCode, string? error)
    {
        _currentUrl = uri ?? _pendingNavigationUri ?? _currentUrl;
        _activeNavigationFailed = true;
        UpdateHistorySnapshot(webView?.CanGoBack() ?? false, webView?.CanGoForward() ?? false);
        NavigationCompleted?.Invoke(this, new NativeWebViewNavigationCompletedEventArgs(_currentUrl, isSuccess: false, httpStatusCode, error));
    }

    private bool OnRuntimeShouldOverrideNavigation(Uri? uri, bool isRedirected)
    {
        var started = new NativeWebViewNavigationStartedEventArgs(uri, isRedirected);
        NavigationStarted?.Invoke(this, started);
        if (started.Cancel)
        {
            return true;
        }

        _pendingNavigationUri = uri ?? _pendingNavigationUri;
        _suppressedStartedNavigationKey = GetNavigationKey(uri);
        _activeNavigationFailed = false;
        return false;
    }

    private void OnScriptMessageReceived(string kind, string? payload)
    {
        if (string.Equals(kind, "json", StringComparison.Ordinal))
        {
            WebMessageReceived?.Invoke(this, new NativeWebViewMessageReceivedEventArgs(message: null, json: payload));
            return;
        }

        WebMessageReceived?.Invoke(this, new NativeWebViewMessageReceivedEventArgs(payload, json: null));
    }

    private void OnNewWindowRequested(string? rawTarget)
    {
        var targetUri = CreateUri(rawTarget);
        var args = new NativeWebViewNewWindowRequestedEventArgs(targetUri);
        NewWindowRequested?.Invoke(this, args);

        if (!args.Handled && targetUri is not null)
        {
            Navigate(targetUri);
        }
    }

    private static string? GetNavigationKey(Uri? uri)
    {
        if (uri is null)
        {
            return null;
        }

        return uri.IsAbsoluteUri
            ? uri.AbsoluteUri
            : uri.ToString();
    }

    private Uri? CreateUri(Android.Net.Uri? uri)
    {
        return CreateUri(uri?.ToString());
    }

    private Uri? CreateUri(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri;
        }

        if (_currentUrl is { IsAbsoluteUri: true } current &&
            Uri.TryCreate(current, value, out var resolvedRelativeUri))
        {
            return resolvedRelativeUri;
        }

        return Uri.TryCreate(value, UriKind.Relative, out var relativeUri)
            ? relativeUri
            : null;
    }

    private static string BuildDispatchScript(string payloadExpression)
    {
        return $"(function() {{ var payload = {payloadExpression}; if (window.chrome && window.chrome.webview && typeof window.chrome.webview.__dispatchMessage === 'function') {{ window.chrome.webview.__dispatchMessage(payload); }} else {{ window.dispatchEvent(new MessageEvent(\"message\", {{ data: payload }})); }} return null; }})();";
    }

    private void InvokeOnMainThread(Action action)
    {
        if (Looper.MyLooper() == Looper.MainLooper)
        {
            action();
            return;
        }

        Exception? capturedException = null;
        using var completed = new ManualResetEventSlim();
        if (!_mainHandler.Post(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    capturedException = ex;
                }
                finally
                {
                    completed.Set();
                }
            }))
        {
            throw new InvalidOperationException("Failed to schedule the requested Android UI action on the main thread.");
        }

        completed.Wait();
        if (capturedException is not null)
        {
            throw new InvalidOperationException("Failed to run the requested Android UI action on the main thread.", capturedException);
        }
    }

    private Task InvokeOnMainThreadAsync(Action action, CancellationToken cancellationToken = default)
    {
        return InvokeOnMainThreadAsync(
            () =>
            {
                action();
                return Task.CompletedTask;
            },
            cancellationToken);
    }

    private Task InvokeOnMainThreadAsync<T>(Action<T> action, T state, CancellationToken cancellationToken = default)
    {
        return InvokeOnMainThreadAsync(
            () =>
            {
                action(state);
                return Task.CompletedTask;
            },
            cancellationToken);
    }

    private Task InvokeOnMainThreadAsync(Func<Task> action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Looper.MyLooper() == Looper.MainLooper)
        {
            return action();
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_mainHandler.Post(async () =>
            {
                try
                {
                    await action().ConfigureAwait(false);
                    tcs.TrySetResult();
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }))
        {
            throw new InvalidOperationException("Failed to schedule the requested Android UI action on the main thread.");
        }

        return cancellationToken.CanBeCanceled
            ? tcs.Task.WaitAsync(cancellationToken)
            : tcs.Task;
    }

    private Task<T> InvokeOnMainThreadAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Looper.MyLooper() == Looper.MainLooper)
        {
            return action();
        }

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_mainHandler.Post(async () =>
            {
                try
                {
                    tcs.TrySetResult(await action().ConfigureAwait(false));
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }))
        {
            throw new InvalidOperationException("Failed to schedule the requested Android UI action on the main thread.");
        }

        return cancellationToken.CanBeCanceled
            ? tcs.Task.WaitAsync(cancellationToken)
            : tcs.Task;
    }

    private void EnsureNotDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void EnsureFeature(NativeWebViewFeature feature, string operationName)
    {
        if (!Features.Supports(feature))
        {
            throw new PlatformNotSupportedException(
                $"Operation '{operationName}' is not supported on platform '{Platform}'.");
        }
    }

    private sealed class AndroidNavigationClient : WebViewClient
    {
        private readonly WeakReference<AndroidNativeWebViewBackend> _owner;

        public AndroidNavigationClient(AndroidNativeWebViewBackend owner)
        {
            _owner = new WeakReference<AndroidNativeWebViewBackend>(owner);
        }

        public override void OnPageStarted(WebView? view, string? url, Bitmap? favicon)
        {
            base.OnPageStarted(view, url, favicon);

            if (_owner.TryGetTarget(out var owner))
            {
                owner.OnRuntimeNavigationStarted(url, isRedirected: false);
            }
        }

        public override void OnPageFinished(WebView? view, string? url)
        {
            base.OnPageFinished(view, url);

            if (view is null)
            {
                return;
            }

            if (_owner.TryGetTarget(out var owner))
            {
                owner.OnRuntimeNavigationCompleted(view, url);
            }
        }

        public override bool ShouldOverrideUrlLoading(WebView? view, IWebResourceRequest? request)
        {
            if (request is null || !request.IsForMainFrame)
            {
                return false;
            }

            if (_owner.TryGetTarget(out var owner))
            {
                return owner.OnRuntimeShouldOverrideNavigation(owner.CreateUri(request.Url), request.IsRedirect);
            }

            return false;
        }

        public override bool ShouldOverrideUrlLoading(WebView? view, string? url)
        {
            if (_owner.TryGetTarget(out var owner))
            {
                return owner.OnRuntimeShouldOverrideNavigation(owner.CreateUri(url), isRedirected: false);
            }

            return false;
        }

        public override void OnReceivedError(WebView? view, IWebResourceRequest? request, WebResourceError? error)
        {
            base.OnReceivedError(view, request, error);

            if (request is not null && !request.IsForMainFrame)
            {
                return;
            }

            if (_owner.TryGetTarget(out var owner))
            {
                owner.OnRuntimeNavigationFailed(
                    view,
                    owner.CreateUri(request?.Url),
                    httpStatusCode: null,
                    error?.Description?.ToString());
            }
        }

        public override void OnReceivedError(WebView? view, ClientError errorCode, string? description, string? failingUrl)
        {
            base.OnReceivedError(view, errorCode, description, failingUrl);

            if (_owner.TryGetTarget(out var owner))
            {
                owner.OnRuntimeNavigationFailed(
                    view,
                    owner.CreateUri(failingUrl),
                    httpStatusCode: null,
                    description);
            }
        }

        public override void OnReceivedHttpError(WebView? view, IWebResourceRequest? request, WebResourceResponse? errorResponse)
        {
            base.OnReceivedHttpError(view, request, errorResponse);

            if (request is null || !request.IsForMainFrame)
            {
                return;
            }

            if (_owner.TryGetTarget(out var owner))
            {
                owner.OnRuntimeNavigationFailed(
                    view,
                    owner.CreateUri(request.Url),
                    (int?)errorResponse?.StatusCode,
                    errorResponse?.ReasonPhrase);
            }
        }
    }

    private sealed class AndroidChromeClient : WebChromeClient
    {
        private readonly WeakReference<AndroidNativeWebViewBackend> _owner;

        public AndroidChromeClient(AndroidNativeWebViewBackend owner)
        {
            _owner = new WeakReference<AndroidNativeWebViewBackend>(owner);
        }

        public override bool OnCreateWindow(WebView? view, bool isDialog, bool isUserGesture, Message? resultMsg)
        {
            if (!_owner.TryGetTarget(out var owner) || view is null || resultMsg?.Obj is null)
            {
                return false;
            }

            var transport = Object.GetObject<WebView.WebViewTransport>(resultMsg.Obj.Handle, JniHandleOwnership.DoNotTransfer);
            if (transport is null)
            {
                return false;
            }

            var popupWebView = new WebView(view.Context);
            popupWebView.SetWebViewClient(new PopupNavigationClient(owner, popupWebView));
            transport.WebView = popupWebView;
            resultMsg.SendToTarget();
            return true;
        }

        public override void OnCloseWindow(WebView? window)
        {
            base.OnCloseWindow(window);

            if (window is null)
            {
                return;
            }

            window.StopLoading();
            window.SetWebViewClient(null);
            window.SetWebChromeClient(null);
            window.Destroy();
            window.Dispose();
        }
    }

    private sealed class PopupNavigationClient : WebViewClient
    {
        private readonly WeakReference<AndroidNativeWebViewBackend> _owner;
        private readonly WeakReference<WebView> _popupWebView;
        private bool _handled;

        public PopupNavigationClient(AndroidNativeWebViewBackend owner, WebView popupWebView)
        {
            _owner = new WeakReference<AndroidNativeWebViewBackend>(owner);
            _popupWebView = new WeakReference<WebView>(popupWebView);
        }

        public override bool ShouldOverrideUrlLoading(WebView? view, IWebResourceRequest? request)
        {
            if (request is null || !request.IsForMainFrame)
            {
                return false;
            }

            HandlePopupNavigation(request.Url?.ToString());
            return true;
        }

        public override bool ShouldOverrideUrlLoading(WebView? view, string? url)
        {
            HandlePopupNavigation(url);
            return true;
        }

        public override void OnPageStarted(WebView? view, string? url, Bitmap? favicon)
        {
            base.OnPageStarted(view, url, favicon);
            HandlePopupNavigation(url);
        }

        private void HandlePopupNavigation(string? url)
        {
            if (_handled)
            {
                return;
            }

            _handled = true;

            if (_owner.TryGetTarget(out var owner))
            {
                owner.OnNewWindowRequested(url);
            }

            if (_popupWebView.TryGetTarget(out var popupWebView))
            {
                popupWebView.StopLoading();
                popupWebView.SetWebViewClient(null);
                popupWebView.SetWebChromeClient(null);

                if (popupWebView.Parent is ViewGroup popupParent)
                {
                    popupParent.RemoveView(popupWebView);
                }

                popupWebView.Destroy();
                popupWebView.Dispose();
            }
        }
    }

    private sealed class AndroidScriptBridge : Object
    {
        private readonly WeakReference<AndroidNativeWebViewBackend> _owner;

        public AndroidScriptBridge(AndroidNativeWebViewBackend owner)
        {
            _owner = new WeakReference<AndroidNativeWebViewBackend>(owner);
        }

        [JavascriptInterface]
        [Export("postMessage")]
        public void PostMessage(string kind, string? payload)
        {
            if (!_owner.TryGetTarget(out var owner))
            {
                return;
            }

            _ = owner.InvokeOnMainThreadAsync(() =>
            {
                owner.OnScriptMessageReceived(kind, payload);
                return Task.CompletedTask;
            });
        }

        [JavascriptInterface]
        [Export("openWindow")]
        public void OpenWindow(string? url)
        {
            if (!_owner.TryGetTarget(out var owner))
            {
                return;
            }

            _ = owner.InvokeOnMainThreadAsync(() =>
            {
                owner.OnNewWindowRequested(url);
                return Task.CompletedTask;
            });
        }
    }

    private sealed class ScriptValueCallback : Object, IValueCallback
    {
        private readonly TaskCompletionSource<string?> _tcs;

        public ScriptValueCallback(TaskCompletionSource<string?> tcs)
        {
            _tcs = tcs;
        }

        public void OnReceiveValue(Object? value)
        {
            _tcs.TrySetResult(value?.ToString() ?? "null");
        }
    }

    private sealed class ConsumingLongClickListener : Object, View.IOnLongClickListener
    {
        public bool OnLongClick(View? v)
        {
            _ = v;
            return true;
        }
    }
}
