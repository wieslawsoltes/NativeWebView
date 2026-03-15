using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CoreGraphics;
using Foundation;
using NativeWebView.Core;
using NativeWebView.Interop;
using ObjCRuntime;
using UIKit;
using WebKit;

namespace NativeWebView.Platform.iOS;

[SupportedOSPlatform("ios15.0")]
public sealed class IOSNativeWebViewBackend
    : INativeWebViewBackend,
      INativeWebViewFrameSource,
      INativeWebViewPlatformHandleProvider,
      INativeWebViewInstanceConfigurationTarget,
      INativeWebViewNativeControlAttachment
{
    private const string ScriptMessageHandlerName = "nativewebview";

    private static readonly NativePlatformHandle PlaceholderPlatformHandle = new((nint)0x4001, "UIView");
    private static readonly NativePlatformHandle PlaceholderViewHandle = new((nint)0x4002, "WKWebView");
    private static readonly NativePlatformHandle PlaceholderControllerHandle = new((nint)0x4003, "WKWebViewConfiguration");

    private static readonly IntPtr NSArrayClassHandle = Class.GetHandle("NSArray");
    private static readonly IntPtr WKWebsiteDataStoreClassHandle = Class.GetHandle("WKWebsiteDataStore");
    private static readonly IntPtr ArrayWithObjectSelectorHandle = Selector.GetHandle("arrayWithObject:");
    private static readonly IntPtr DataStoreForIdentifierSelectorHandle = Selector.GetHandle("dataStoreForIdentifier:");
    private static readonly IntPtr RespondsToSelectorHandle = Selector.GetHandle("respondsToSelector:");
    private static readonly IntPtr SetProxyConfigurationsSelectorHandle = Selector.GetHandle("setProxyConfigurations:");

    private static readonly string JavaScriptBridgeSource = """
        (() => {
          const chromeRoot = window.chrome = window.chrome || {};
          const webview = chromeRoot.webview = chromeRoot.webview || {};
          const listeners = webview.__listeners = webview.__listeners || [];

          webview.postMessage = (message) => {
            const handler = window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.nativewebview;
            if (!handler || typeof handler.postMessage !== 'function') {
              return;
            }

            if (typeof message === 'string') {
              handler.postMessage({ kind: 'string', value: message });
              return;
            }

            try {
              handler.postMessage({ kind: 'json', value: JSON.stringify(message) });
            } catch (error) {
              handler.postMessage({ kind: 'string', value: String(message) });
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
    private readonly INativeWebViewCommandManager _commandManager = NativeWebViewBackendSupport.NoopCommandManagerInstance;
    private readonly INativeWebViewCookieManager _cookieManager = NativeWebViewBackendSupport.NoopCookieManagerInstance;

    private TaskCompletionSource<bool> _attachmentTcs = CreatePendingAttachmentSource();
    private NativeWebViewInstanceConfiguration _instanceConfiguration = new();

    private NativeWebViewEnvironmentOptions? _preparedEnvironmentOptions;
    private NativeWebViewControllerOptions? _preparedControllerOptions;

    private UIView? _parentView;
    private UIView? _containerView;
    private WKWebView? _webView;
    private WKWebViewConfiguration? _configuration;
    private WKUserContentController? _userContentController;
    private WKWebsiteDataStore? _websiteDataStore;
    private IOSNavigationDelegate? _navigationDelegate;
    private IOSUiDelegate? _uiDelegate;
    private IOSScriptMessageHandler? _scriptMessageHandler;

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

    private double _zoomFactor;
    private string? _headerString;
    private string? _userAgentString;

    public IOSNativeWebViewBackend()
    {
        Platform = NativeWebViewPlatform.IOS;
        Features = IOSPlatformFeatures.Instance;
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

        if (OperatingSystem.IsIOS())
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
            if (_webView is not null)
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
            if (_webView is not null)
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
                async () => ConvertScriptResult(await _webView!.EvaluateJavaScriptAsync(script).ConfigureAwait(false)),
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
                async () => await _webView!.EvaluateJavaScriptAsync(script).ConfigureAwait(false),
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
                async () => await _webView!.EvaluateJavaScriptAsync(script).ConfigureAwait(false),
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
            _ = InvokeOnMainThreadAsync(
                webView => webView.PageZoom = (nfloat)_zoomFactor,
                _webView);
        }
    }

    public void SetUserAgent(string? userAgent)
    {
        EnsureNotDisposed();
        _userAgentString = userAgent;

        if (_webView is not null)
        {
            _ = InvokeOnMainThreadAsync(
                webView => webView.CustomUserAgent = userAgent,
                _webView);
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
                    webView.BecomeFirstResponder();
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
            ? new NativePlatformHandle((nint)_containerView.Handle, "UIView")
            : PlaceholderPlatformHandle;
        return true;
    }

    public bool TryGetViewHandle(out NativePlatformHandle handle)
    {
        handle = _webView is not null
            ? new NativePlatformHandle((nint)_webView.Handle, "WKWebView")
            : PlaceholderViewHandle;
        return true;
    }

    public bool TryGetControllerHandle(out NativePlatformHandle handle)
    {
        handle = _configuration is not null
            ? new NativePlatformHandle((nint)_configuration.Handle, "WKWebViewConfiguration")
            : PlaceholderControllerHandle;
        return true;
    }

    public NativePlatformHandle AttachToNativeParent(NativePlatformHandle parentHandle)
    {
        EnsureNotDisposed();

        if (!OperatingSystem.IsIOS())
        {
            throw new PlatformNotSupportedException("iOS native control attachment can only run on iOS.");
        }

        if (parentHandle.Handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Parent native handle is invalid.");
        }

        if (!string.Equals(parentHandle.HandleDescriptor, "UIView", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"iOS native control attachment requires a UIView parent, but received '{parentHandle.HandleDescriptor}'.");
        }

        if (_containerView is not null && _parentView is not null && (nint)_parentView.Handle == parentHandle.Handle)
        {
            return new NativePlatformHandle((nint)_containerView.Handle, "UIView");
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

        return new NativePlatformHandle((nint)_containerView!.Handle, "UIView");
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
                nativeObject: new NativePlatformHandle((nint)_webView!.Handle, "WKWebView"));
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
            throw new InvalidOperationException("Runtime initialization requires an attached UIView host.");
        }

        if (_webView is not null)
        {
            return;
        }

        var environmentOptions = _preparedEnvironmentOptions?.Clone() ?? new NativeWebViewEnvironmentOptions();
        var controllerOptions = _preparedControllerOptions?.Clone() ?? new NativeWebViewControllerOptions();

        _userContentController = new WKUserContentController();
        _scriptMessageHandler = new IOSScriptMessageHandler(this);
        _navigationDelegate = new IOSNavigationDelegate(this);
        _uiDelegate = new IOSUiDelegate(this);

        _userContentController.AddUserScript(
            new WKUserScript((NSString)JavaScriptBridgeSource, WKUserScriptInjectionTime.AtDocumentStart, isForMainFrameOnly: false));
        _userContentController.AddScriptMessageHandler(_scriptMessageHandler, ScriptMessageHandlerName);

        _websiteDataStore = CreateWebsiteDataStore(environmentOptions, controllerOptions);

        _configuration = new WKWebViewConfiguration
        {
            UserContentController = _userContentController,
            WebsiteDataStore = _websiteDataStore,
        };

        _webView = new WKWebView(_containerView.Bounds, _configuration)
        {
            AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight,
            NavigationDelegate = _navigationDelegate,
            UIDelegate = _uiDelegate,
            CustomUserAgent = _userAgentString,
            Opaque = false,
            BackgroundColor = UIColor.Clear,
        };

        if (_zoomFactor > 0)
        {
            _webView.PageZoom = (nfloat)_zoomFactor;
        }

        _containerView.AddSubview(_webView);
        _webView.Frame = _containerView.Bounds;
        UpdateHistorySnapshot(_webView.CanGoBack, _webView.CanGoForward);

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
        var parentView = Runtime.GetNSObject<UIView>((NativeHandle)parentHandle.Handle)
            ?? throw new InvalidOperationException("Failed to resolve the UIView parent handle.");

        _parentView = parentView;
        _containerView = new UIView(parentView.Bounds)
        {
            AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight,
            BackgroundColor = UIColor.Clear,
        };

        parentView.AddSubview(_containerView);
        _containerView.Frame = parentView.Bounds;
    }

    private void DetachFromNativeParentCore()
    {
        InvokeOnMainThread(DetachOnMainThread);
        _attachmentTcs = CreatePendingAttachmentSource();
    }

    private void DetachOnMainThread()
    {
        if (_userContentController is not null)
        {
            _userContentController.RemoveScriptMessageHandler(ScriptMessageHandlerName);
            _userContentController.RemoveAllUserScripts();
        }

        if (_webView is not null)
        {
            _webView.WeakNavigationDelegate = null;
            _webView.WeakUIDelegate = null;
            _webView.StopLoading();
            _webView.RemoveFromSuperview();
            _webView.Dispose();
            _webView = null;
        }

        _configuration?.Dispose();
        _configuration = null;

        _websiteDataStore?.Dispose();
        _websiteDataStore = null;

        _scriptMessageHandler?.Dispose();
        _scriptMessageHandler = null;

        _navigationDelegate?.Dispose();
        _navigationDelegate = null;

        _uiDelegate?.Dispose();
        _uiDelegate = null;

        _userContentController?.Dispose();
        _userContentController = null;

        _containerView?.RemoveFromSuperview();
        _containerView?.Dispose();
        _containerView = null;
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
        return OperatingSystem.IsIOS() && _containerView is not null;
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

        _pendingNavigationUri = uri;
        var request = CreateNavigationRequest(uri);
        _webView.LoadRequest(request);
    }

    private NSMutableUrlRequest CreateNavigationRequest(Uri uri)
    {
        var navigationTarget = uri.IsAbsoluteUri
            ? uri.AbsoluteUri
            : uri.ToString();

        var nsUrl = new NSUrl(navigationTarget)
            ?? throw new InvalidOperationException($"Failed to create NSURL for '{navigationTarget}'.");

        var request = new NSMutableUrlRequest(nsUrl);
        if (!string.IsNullOrWhiteSpace(_headerString) &&
            uri.IsAbsoluteUri &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            ApplyRequestHeaders(request, _headerString);
        }

        return request;
    }

    private static void ApplyRequestHeaders(NSMutableUrlRequest request, string headerString)
    {
        foreach (var rawLine in headerString.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
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

            request[headerName] = headerValue;
        }
    }

    private WKWebsiteDataStore CreateWebsiteDataStore(
        NativeWebViewEnvironmentOptions environmentOptions,
        NativeWebViewControllerOptions controllerOptions)
    {
        var proxyConfiguration = NativeWebViewProxyConfigurationResolver.Resolve(environmentOptions.Proxy);

        if (controllerOptions.IsInPrivateModeEnabled)
        {
            if (proxyConfiguration is not null)
            {
                ApplyProxyConfiguration(WKWebsiteDataStore.NonPersistentDataStore, proxyConfiguration);
            }

            return WKWebsiteDataStore.NonPersistentDataStore;
        }

        WKWebsiteDataStore dataStore;
        if (RequiresDedicatedWebsiteDataStore(environmentOptions, controllerOptions, proxyConfiguration))
        {
            if (!OperatingSystem.IsIOSVersionAtLeast(17))
            {
                throw new PlatformNotSupportedException(
                    "Dedicated WKWebsiteDataStore identities require iOS 17.0 or later.");
            }

            dataStore = CreateWebsiteDataStoreWithIdentifier(
                CreateWebsiteDataStoreIdentifier(environmentOptions, controllerOptions, proxyConfiguration));
        }
        else
        {
            dataStore = WKWebsiteDataStore.DefaultDataStore;
        }

        if (proxyConfiguration is not null)
        {
            ApplyProxyConfiguration(dataStore, proxyConfiguration);
        }

        return dataStore;
    }

    private static bool RequiresDedicatedWebsiteDataStore(
        NativeWebViewEnvironmentOptions environmentOptions,
        NativeWebViewControllerOptions controllerOptions,
        NativeWebViewResolvedProxyConfiguration? proxyConfiguration)
    {
        return proxyConfiguration is not null ||
            !string.IsNullOrWhiteSpace(environmentOptions.UserDataFolder) ||
            !string.IsNullOrWhiteSpace(environmentOptions.CacheFolder) ||
            !string.IsNullOrWhiteSpace(environmentOptions.CookieDataFolder) ||
            !string.IsNullOrWhiteSpace(environmentOptions.SessionDataFolder) ||
            !string.IsNullOrWhiteSpace(controllerOptions.ProfileName);
    }

    private static void ApplyProxyConfiguration(
        WKWebsiteDataStore dataStore,
        NativeWebViewResolvedProxyConfiguration configuration)
    {
        if (configuration.Kind == NativeWebViewProxyKind.AutoConfigUrl)
        {
            throw new PlatformNotSupportedException(
                "WKWebView proxy auto-configuration URLs are not supported by the current iOS integration. Use an explicit http(s) or socks5 proxy server.");
        }

        if (!OperatingSystem.IsIOSVersionAtLeast(17))
        {
            throw new PlatformNotSupportedException(
                "Per-instance proxy configuration requires iOS 17.0 or later for WKWebsiteDataStore.proxyConfigurations.");
        }

        if (ObjCInterop.bool_objc_msgSend_IntPtr(dataStore.Handle, RespondsToSelectorHandle, SetProxyConfigurationsSelectorHandle) == 0)
        {
            throw new PlatformNotSupportedException(
                "The current WKWebsiteDataStore runtime does not expose proxyConfigurations.");
        }

        var proxyConfigHandle = CreateNativeProxyConfiguration(configuration);
        try
        {
            var proxyArrayHandle = ObjCInterop.IntPtr_objc_msgSend_IntPtr(
                NSArrayClassHandle,
                ArrayWithObjectSelectorHandle,
                proxyConfigHandle);
            if (proxyArrayHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create the proxy configuration array for WKWebsiteDataStore.");
            }

            ObjCInterop.void_objc_msgSend_IntPtr(dataStore.Handle, SetProxyConfigurationsSelectorHandle, proxyArrayHandle);
        }
        finally
        {
            NetworkInterop.nw_release(proxyConfigHandle);
        }
    }

    private static IntPtr CreateNativeProxyConfiguration(NativeWebViewResolvedProxyConfiguration configuration)
    {
        var endpointHandle = CreateEndpoint(configuration.Host, configuration.Port);
        IntPtr tlsOptionsHandle = IntPtr.Zero;

        try
        {
            if (configuration.UseTls)
            {
                tlsOptionsHandle = NetworkInterop.nw_tls_create_options();
                if (tlsOptionsHandle == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Failed to create TLS options for the configured proxy.");
                }
            }

            var proxyHandle = configuration.Kind switch
            {
                NativeWebViewProxyKind.HttpConnect => NetworkInterop.nw_proxy_config_create_http_connect(endpointHandle, tlsOptionsHandle),
                NativeWebViewProxyKind.Socks5 => NetworkInterop.nw_proxy_config_create_socksv5(endpointHandle),
                _ => IntPtr.Zero,
            };

            if (proxyHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create the native iOS proxy configuration.");
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(configuration.Username))
                {
                    NetworkInterop.nw_proxy_config_set_username_and_password(
                        proxyHandle,
                        configuration.Username!,
                        configuration.Password);
                }

                foreach (var excludedDomain in configuration.ExcludedDomains)
                {
                    if (TryNormalizeExcludedDomain(excludedDomain, out var normalizedExcludedDomain))
                    {
                        NetworkInterop.nw_proxy_config_add_excluded_domain(proxyHandle, normalizedExcludedDomain);
                    }
                }

                return proxyHandle;
            }
            catch
            {
                NetworkInterop.nw_release(proxyHandle);
                throw;
            }
        }
        finally
        {
            if (tlsOptionsHandle != IntPtr.Zero)
            {
                NetworkInterop.nw_release(tlsOptionsHandle);
            }

            if (endpointHandle != IntPtr.Zero)
            {
                NetworkInterop.nw_release(endpointHandle);
            }
        }
    }

    private static IntPtr CreateEndpoint(string host, int port)
    {
        var hostUtf8 = Marshal.StringToCoTaskMemUTF8(host);
        var portUtf8 = Marshal.StringToCoTaskMemUTF8(port.ToString(CultureInfo.InvariantCulture));

        try
        {
            var endpoint = NetworkInterop.nw_endpoint_create_host(hostUtf8, portUtf8);
            if (endpoint == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Failed to create a proxy endpoint for '{host}:{port}'.");
            }

            return endpoint;
        }
        finally
        {
            Marshal.FreeCoTaskMem(hostUtf8);
            Marshal.FreeCoTaskMem(portUtf8);
        }
    }

    private static bool TryNormalizeExcludedDomain(string value, out string normalized)
    {
        normalized = value.Trim();
        if (normalized.Length == 0)
        {
            return false;
        }

        if (normalized.StartsWith('<') && normalized.EndsWith('>'))
        {
            return false;
        }

        if (normalized.StartsWith("*.", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }
        else if (normalized.StartsWith(".", StringComparison.Ordinal))
        {
            normalized = normalized[1..];
        }

        if (normalized.Contains("://", StringComparison.Ordinal) &&
            Uri.TryCreate(normalized, UriKind.Absolute, out var normalizedUri) &&
            normalizedUri is not null)
        {
            normalized = normalizedUri.Host;
        }

        normalized = normalized.Replace("*", string.Empty, StringComparison.Ordinal).Trim();
        return normalized.Length > 0;
    }

    private static NSUuid CreateWebsiteDataStoreIdentifier(
        NativeWebViewEnvironmentOptions environmentOptions,
        NativeWebViewControllerOptions controllerOptions,
        NativeWebViewResolvedProxyConfiguration? proxyConfiguration)
    {
        var builder = new StringBuilder();
        if (proxyConfiguration is not null)
        {
            AppendIdentityPart(builder, "proxy-kind", proxyConfiguration.Kind.ToString());
            AppendIdentityPart(builder, "proxy-host", proxyConfiguration.Host);
            AppendIdentityPart(builder, "proxy-port", proxyConfiguration.Port.ToString(CultureInfo.InvariantCulture));
            AppendIdentityPart(builder, "proxy-tls", proxyConfiguration.UseTls ? "true" : "false");
            AppendIdentityPart(builder, "proxy-username", proxyConfiguration.Username);
            AppendIdentityPart(builder, "proxy-autoconfig", proxyConfiguration.AutoConfigUrl);

            var normalizedExcludedDomains = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var excludedDomain in proxyConfiguration.ExcludedDomains)
            {
                if (TryNormalizeExcludedDomain(excludedDomain, out var normalizedExcludedDomain))
                {
                    normalizedExcludedDomains.Add(normalizedExcludedDomain);
                }
            }

            foreach (var excludedDomain in normalizedExcludedDomains)
            {
                AppendIdentityPart(builder, "proxy-bypass", excludedDomain);
            }
        }

        AppendIdentityPart(builder, "user-data-folder", environmentOptions.UserDataFolder);
        AppendIdentityPart(builder, "cache-folder", environmentOptions.CacheFolder);
        AppendIdentityPart(builder, "cookie-data-folder", environmentOptions.CookieDataFolder);
        AppendIdentityPart(builder, "session-data-folder", environmentOptions.SessionDataFolder);
        AppendIdentityPart(builder, "profile-name", controllerOptions.ProfileName);

        return new NSUuid(CreateDeterministicGuid(builder.ToString()).ToString("D"));
    }

    private static void AppendIdentityPart(StringBuilder builder, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        builder.Append(key)
            .Append('=')
            .Append(value.Trim())
            .Append('\n');
    }

    private static Guid CreateDeterministicGuid(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        Span<byte> guidBytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(guidBytes);
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }

    [SupportedOSPlatform("ios17.0")]
    private static WKWebsiteDataStore CreateWebsiteDataStoreWithIdentifier(NSUuid identifier)
    {
        var dataStoreHandle = ObjCInterop.IntPtr_objc_msgSend_IntPtr(
            WKWebsiteDataStoreClassHandle,
            DataStoreForIdentifierSelectorHandle,
            identifier.Handle);
        if (dataStoreHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create a dedicated WKWebsiteDataStore for the iOS NativeWebView runtime.");
        }

        return Runtime.GetNSObject<WKWebsiteDataStore>(dataStoreHandle)
            ?? throw new InvalidOperationException("Failed to materialize the dedicated WKWebsiteDataStore instance.");
    }

    private void OnNavigationAction(WKNavigationAction navigationAction, Action<WKNavigationActionPolicy> decisionHandler)
    {
        if (_disposed)
        {
            decisionHandler(WKNavigationActionPolicy.Cancel);
            return;
        }

        var targetUri = CreateUri(navigationAction.Request?.Url);
        var targetFrame = navigationAction.TargetFrame;
        var isMainFrame = targetFrame?.MainFrame ?? true;
        if (!isMainFrame)
        {
            decisionHandler(WKNavigationActionPolicy.Allow);
            return;
        }

        var started = new NativeWebViewNavigationStartedEventArgs(targetUri, isRedirected: false);
        NavigationStarted?.Invoke(this, started);
        if (started.Cancel)
        {
            decisionHandler(WKNavigationActionPolicy.Cancel);
            return;
        }

        _pendingNavigationUri = targetUri;
        decisionHandler(WKNavigationActionPolicy.Allow);
    }

    private void OnNavigationCompleted(WKWebView webView)
    {
        _currentUrl = CreateUri(webView.Url) ?? _pendingNavigationUri ?? _currentUrl;
        _pendingNavigationUri = _currentUrl;
        UpdateHistorySnapshot(webView.CanGoBack, webView.CanGoForward);
        NavigationCompleted?.Invoke(this, new NativeWebViewNavigationCompletedEventArgs(_currentUrl, isSuccess: true, httpStatusCode: 200));
    }

    private void OnNavigationFailed(WKWebView webView, NSError error)
    {
        _currentUrl = CreateUri(webView.Url) ?? _pendingNavigationUri ?? _currentUrl;
        UpdateHistorySnapshot(webView.CanGoBack, webView.CanGoForward);
        NavigationCompleted?.Invoke(
            this,
            new NativeWebViewNavigationCompletedEventArgs(
                _currentUrl,
                isSuccess: false,
                httpStatusCode: null,
                error: error.LocalizedDescription));
    }

    private void OnNewWindowRequested(WKWebView webView, WKNavigationAction navigationAction)
    {
        var targetUri = CreateUri(navigationAction.Request?.Url);
        var args = new NativeWebViewNewWindowRequestedEventArgs(targetUri);
        NewWindowRequested?.Invoke(this, args);
        if (args.Handled)
        {
            return;
        }

        if (navigationAction.Request is not null)
        {
            webView.LoadRequest(navigationAction.Request);
        }
    }

    private void OnScriptMessageReceived(WKScriptMessage message)
    {
        if (message.Body is NSDictionary dictionary)
        {
            var kind = dictionary["kind"]?.ToString();
            var value = dictionary["value"]?.ToString();
            if (string.Equals(kind, "json", StringComparison.Ordinal))
            {
                WebMessageReceived?.Invoke(this, new NativeWebViewMessageReceivedEventArgs(message: null, json: value));
                return;
            }

            WebMessageReceived?.Invoke(this, new NativeWebViewMessageReceivedEventArgs(value, json: null));
            return;
        }

        var payload = message.Body?.ToString();
        WebMessageReceived?.Invoke(this, new NativeWebViewMessageReceivedEventArgs(payload, json: null));
    }

    private static Uri? CreateUri(NSUrl? url)
    {
        var absoluteString = url?.AbsoluteString;
        return absoluteString is not null && Uri.TryCreate(absoluteString, UriKind.Absolute, out var uri)
            ? uri
            : null;
    }

    private static string? ConvertScriptResult(NSObject? result)
    {
        if (result is null)
        {
            return "null";
        }

        if (result is NSDictionary or NSArray)
        {
            var jsonData = NSJsonSerialization.Serialize(result, NSJsonWritingOptions.FragmentsAllowed, out var error);
            if (error is null && jsonData is not null)
            {
                return NSString.FromData(jsonData, NSStringEncoding.UTF8)?.ToString();
            }
        }

        return result.ToString();
    }

    private static string BuildDispatchScript(string payloadExpression)
    {
        return $"(function() {{ var payload = {payloadExpression}; if (window.chrome && window.chrome.webview && typeof window.chrome.webview.__dispatchMessage === 'function') {{ window.chrome.webview.__dispatchMessage(payload); }} else {{ window.dispatchEvent(new MessageEvent(\"message\", {{ data: payload }})); }} return null; }})();";
    }

    private static void InvokeOnMainThread(Action action)
    {
        if (NSThread.IsMain)
        {
            action();
            return;
        }

        Exception? capturedException = null;
        using var completed = new ManualResetEventSlim();
        UIApplication.SharedApplication.BeginInvokeOnMainThread(() =>
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
        });

        completed.Wait();
        if (capturedException is not null)
        {
            throw new InvalidOperationException("Failed to run the requested iOS UI action on the main thread.", capturedException);
        }
    }

    private static Task InvokeOnMainThreadAsync(Action action, CancellationToken cancellationToken = default)
    {
        return InvokeOnMainThreadAsync(
            () =>
            {
                action();
                return Task.CompletedTask;
            },
            cancellationToken);
    }

    private static Task InvokeOnMainThreadAsync<T>(Action<T> action, T state, CancellationToken cancellationToken = default)
    {
        return InvokeOnMainThreadAsync(
            () =>
            {
                action(state);
                return Task.CompletedTask;
            },
            cancellationToken);
    }

    private static Task InvokeOnMainThreadAsync(Func<Task> action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (NSThread.IsMain)
        {
            return action();
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        UIApplication.SharedApplication.BeginInvokeOnMainThread(async () =>
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
        });

        return cancellationToken.CanBeCanceled
            ? tcs.Task.WaitAsync(cancellationToken)
            : tcs.Task;
    }

    private static Task<T> InvokeOnMainThreadAsync<T>(Func<T> action, CancellationToken cancellationToken = default)
    {
        return InvokeOnMainThreadAsync(
            () => Task.FromResult(action()),
            cancellationToken);
    }

    private static Task<T> InvokeOnMainThreadAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (NSThread.IsMain)
        {
            return action();
        }

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        UIApplication.SharedApplication.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                tcs.TrySetResult(await action().ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

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

    private sealed class IOSNavigationDelegate : WKNavigationDelegate
    {
        private readonly WeakReference<IOSNativeWebViewBackend> _owner;

        public IOSNavigationDelegate(IOSNativeWebViewBackend owner)
        {
            _owner = new WeakReference<IOSNativeWebViewBackend>(owner);
        }

        public override void DecidePolicy(
            WKWebView webView,
            WKNavigationAction navigationAction,
            Action<WKNavigationActionPolicy> decisionHandler)
        {
            if (_owner.TryGetTarget(out var owner))
            {
                owner.OnNavigationAction(navigationAction, decisionHandler);
                return;
            }

            decisionHandler(WKNavigationActionPolicy.Cancel);
        }

        public override void DidFinishNavigation(WKWebView webView, WKNavigation navigation)
        {
            if (_owner.TryGetTarget(out var owner))
            {
                owner.OnNavigationCompleted(webView);
            }
        }

        public override void DidFailNavigation(WKWebView webView, WKNavigation navigation, NSError error)
        {
            if (_owner.TryGetTarget(out var owner))
            {
                owner.OnNavigationFailed(webView, error);
            }
        }

        public override void DidFailProvisionalNavigation(WKWebView webView, WKNavigation navigation, NSError error)
        {
            if (_owner.TryGetTarget(out var owner))
            {
                owner.OnNavigationFailed(webView, error);
            }
        }
    }

    private sealed class IOSUiDelegate : WKUIDelegate
    {
        private readonly WeakReference<IOSNativeWebViewBackend> _owner;

        public IOSUiDelegate(IOSNativeWebViewBackend owner)
        {
            _owner = new WeakReference<IOSNativeWebViewBackend>(owner);
        }

        public override WKWebView? CreateWebView(
            WKWebView webView,
            WKWebViewConfiguration configuration,
            WKNavigationAction navigationAction,
            WKWindowFeatures windowFeatures)
        {
            _ = configuration;
            _ = windowFeatures;

            if (_owner.TryGetTarget(out var owner))
            {
                owner.OnNewWindowRequested(webView, navigationAction);
            }

            return null;
        }
    }

    private sealed class IOSScriptMessageHandler : WKScriptMessageHandler
    {
        private readonly WeakReference<IOSNativeWebViewBackend> _owner;

        public IOSScriptMessageHandler(IOSNativeWebViewBackend owner)
        {
            _owner = new WeakReference<IOSNativeWebViewBackend>(owner);
        }

        public override void DidReceiveScriptMessage(WKUserContentController userContentController, WKScriptMessage message)
        {
            _ = userContentController;

            if (_owner.TryGetTarget(out var owner))
            {
                owner.OnScriptMessageReceived(message);
            }
        }
    }

    private static class NetworkInterop
    {
        [DllImport("/System/Library/Frameworks/Network.framework/Network")]
        public static extern IntPtr nw_endpoint_create_host(IntPtr hostname, IntPtr port);

        [DllImport("/System/Library/Frameworks/Network.framework/Network")]
        public static extern IntPtr nw_tls_create_options();

        [DllImport("/System/Library/Frameworks/Network.framework/Network")]
        public static extern IntPtr nw_proxy_config_create_http_connect(IntPtr proxyEndpoint, IntPtr proxyTlsOptions);

        [DllImport("/System/Library/Frameworks/Network.framework/Network")]
        public static extern IntPtr nw_proxy_config_create_socksv5(IntPtr proxyEndpoint);

        [DllImport("/System/Library/Frameworks/Network.framework/Network")]
        private static extern void nw_proxy_config_set_username_and_password(IntPtr proxyConfig, IntPtr username, IntPtr password);

        [DllImport("/System/Library/Frameworks/Network.framework/Network")]
        private static extern void nw_proxy_config_add_excluded_domain(IntPtr proxyConfig, IntPtr excludedDomain);

        [DllImport("/System/Library/Frameworks/Network.framework/Network")]
        public static extern void nw_release(IntPtr obj);

        public static void nw_proxy_config_set_username_and_password(IntPtr proxyConfig, string username, string? password)
        {
            var usernameUtf8 = Marshal.StringToCoTaskMemUTF8(username);
            var passwordUtf8 = string.IsNullOrEmpty(password)
                ? IntPtr.Zero
                : Marshal.StringToCoTaskMemUTF8(password);

            try
            {
                nw_proxy_config_set_username_and_password(proxyConfig, usernameUtf8, passwordUtf8);
            }
            finally
            {
                Marshal.FreeCoTaskMem(usernameUtf8);
                if (passwordUtf8 != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(passwordUtf8);
                }
            }
        }

        public static void nw_proxy_config_add_excluded_domain(IntPtr proxyConfig, string excludedDomain)
        {
            var excludedDomainUtf8 = Marshal.StringToCoTaskMemUTF8(excludedDomain);
            try
            {
                nw_proxy_config_add_excluded_domain(proxyConfig, excludedDomainUtf8);
            }
            finally
            {
                Marshal.FreeCoTaskMem(excludedDomainUtf8);
            }
        }
    }

    private static class ObjCInterop
    {
        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
        public static extern IntPtr IntPtr_objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);

        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
        public static extern void void_objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);

        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
        public static extern byte bool_objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);
    }
}
