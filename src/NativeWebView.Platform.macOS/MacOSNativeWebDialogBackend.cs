using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using NativeWebView.Core;
using NativeWebView.Interop;

namespace NativeWebView.Platform.macOS;

public sealed class MacOSNativeWebDialogBackend : INativeWebDialogBackend, INativeWebDialogPlatformHandleProvider, INativeWebViewInstanceConfigurationTarget
{
    private const nuint NSWindowStyleMaskTitled = 1u << 0;
    private const nuint NSWindowStyleMaskClosable = 1u << 1;
    private const nuint NSWindowStyleMaskMiniaturizable = 1u << 2;
    private const nuint NSWindowStyleMaskResizable = 1u << 3;
    private const nint NSBackingStoreBuffered = 2;
    private const nuint NSViewWidthSizable = 1u << 1;
    private const nuint NSViewHeightSizable = 1u << 4;

    private static readonly NativePlatformHandle PlaceholderPlatformHandle = new((nint)0x2101, "NSWindow");
    private static readonly NativePlatformHandle PlaceholderDialogHandle = new((nint)0x2102, "WKWebView");
    private static readonly NativePlatformHandle PlaceholderHostWindowHandle = new((nint)0x2103, "NSWindow");

    private static class NativeSymbols
    {
        public static readonly IntPtr NSArrayClass = ObjC.GetClass("NSArray");
        public static readonly IntPtr NSUUIDClass = ObjC.GetClass("NSUUID");
        public static readonly IntPtr NSStringClass = ObjC.GetClass("NSString");
        public static readonly IntPtr NSURLClass = ObjC.GetClass("NSURL");
        public static readonly IntPtr NSURLRequestClass = ObjC.GetClass("NSURLRequest");
        public static readonly IntPtr NSWindowClass = ObjC.GetClass("NSWindow");
        public static readonly IntPtr WKWebViewClass = ObjC.GetClass("WKWebView");
        public static readonly IntPtr WKWebViewConfigurationClass = ObjC.GetClass("WKWebViewConfiguration");
        public static readonly IntPtr WKWebsiteDataStoreClass = ObjC.GetClass("WKWebsiteDataStore");

        public static readonly IntPtr SelAlloc = ObjC.GetSelector("alloc");
        public static readonly IntPtr SelInit = ObjC.GetSelector("init");
        public static readonly IntPtr SelInitWithUUIDString = ObjC.GetSelector("initWithUUIDString:");
        public static readonly IntPtr SelRelease = ObjC.GetSelector("release");
        public static readonly IntPtr SelArrayWithObject = ObjC.GetSelector("arrayWithObject:");
        public static readonly IntPtr SelStringWithUtf8String = ObjC.GetSelector("stringWithUTF8String:");
        public static readonly IntPtr SelUrlWithString = ObjC.GetSelector("URLWithString:");
        public static readonly IntPtr SelRequestWithUrl = ObjC.GetSelector("requestWithURL:");
        public static readonly IntPtr SelInitWithContentRectStyleMaskBackingDefer = ObjC.GetSelector("initWithContentRect:styleMask:backing:defer:");
        public static readonly IntPtr SelSetReleasedWhenClosed = ObjC.GetSelector("setReleasedWhenClosed:");
        public static readonly IntPtr SelSetTitle = ObjC.GetSelector("setTitle:");
        public static readonly IntPtr SelCenter = ObjC.GetSelector("center");
        public static readonly IntPtr SelMakeKeyAndOrderFront = ObjC.GetSelector("makeKeyAndOrderFront:");
        public static readonly IntPtr SelClose = ObjC.GetSelector("close");
        public static readonly IntPtr SelIsVisible = ObjC.GetSelector("isVisible");
        public static readonly IntPtr SelSetFrameOrigin = ObjC.GetSelector("setFrameOrigin:");
        public static readonly IntPtr SelSetContentSize = ObjC.GetSelector("setContentSize:");
        public static readonly IntPtr SelContentView = ObjC.GetSelector("contentView");
        public static readonly IntPtr SelSetFrame = ObjC.GetSelector("setFrame:");
        public static readonly IntPtr SelAddSubview = ObjC.GetSelector("addSubview:");
        public static readonly IntPtr SelRemoveFromSuperview = ObjC.GetSelector("removeFromSuperview");
        public static readonly IntPtr SelSetAutoresizingMask = ObjC.GetSelector("setAutoresizingMask:");
        public static readonly IntPtr SelInitWithFrameConfiguration = ObjC.GetSelector("initWithFrame:configuration:");
        public static readonly IntPtr SelLoadRequest = ObjC.GetSelector("loadRequest:");
        public static readonly IntPtr SelReload = ObjC.GetSelector("reload");
        public static readonly IntPtr SelStopLoading = ObjC.GetSelector("stopLoading");
        public static readonly IntPtr SelGoBack = ObjC.GetSelector("goBack");
        public static readonly IntPtr SelGoForward = ObjC.GetSelector("goForward");
        public static readonly IntPtr SelSetCustomUserAgent = ObjC.GetSelector("setCustomUserAgent:");
        public static readonly IntPtr SelRespondsToSelector = ObjC.GetSelector("respondsToSelector:");
        public static readonly IntPtr SelSetPageZoom = ObjC.GetSelector("setPageZoom:");
        public static readonly IntPtr SelPrint = ObjC.GetSelector("print:");
        public static readonly IntPtr SelBounds = ObjC.GetSelector("bounds");
        public static readonly IntPtr SelDataWithPdfInsideRect = ObjC.GetSelector("dataWithPDFInsideRect:");
        public static readonly IntPtr SelWriteToFileAtomically = ObjC.GetSelector("writeToFile:atomically:");
        public static readonly IntPtr SelDataStoreForIdentifier = ObjC.GetSelector("dataStoreForIdentifier:");
        public static readonly IntPtr SelSetWebsiteDataStore = ObjC.GetSelector("setWebsiteDataStore:");
        public static readonly IntPtr SelSetProxyConfigurations = ObjC.GetSelector("setProxyConfigurations:");
    }

    private readonly List<Uri> _history = [];
    private readonly bool _useNative;
    private NativeWebViewInstanceConfiguration _instanceConfiguration = new();
    private int _historyIndex = -1;
    private bool _disposed;
    private bool _isVisible;
    private Uri? _currentUrl;
    private IntPtr _windowHandle;
    private IntPtr _contentViewHandle;
    private IntPtr _webViewHandle;
    private IntPtr _configurationHandle;

    public MacOSNativeWebDialogBackend()
    {
        Platform = NativeWebViewPlatform.MacOS;
        Features = MacOSPlatformFeatures.Instance;
        _useNative = OperatingSystem.IsMacOS();
        IsDevToolsEnabled = Features.Supports(NativeWebViewFeature.DevTools);
        IsContextMenuEnabled = Features.Supports(NativeWebViewFeature.ContextMenu);
        IsStatusBarEnabled = Features.Supports(NativeWebViewFeature.StatusBar);
        IsZoomControlEnabled = Features.Supports(NativeWebViewFeature.ZoomControl);
        ZoomFactor = 1.0;
    }

    public NativeWebViewPlatform Platform { get; }

    public IWebViewPlatformFeatures Features { get; }

    public bool IsVisible
    {
        get
        {
            if (_useNative && _windowHandle != IntPtr.Zero && ObjC.IsMainThread())
            {
                _isVisible = ObjC.SendBool(_windowHandle, NativeSymbols.SelIsVisible);
            }

            return _isVisible;
        }
    }

    public Uri? CurrentUrl => _currentUrl;

    public bool CanGoBack => _historyIndex > 0;

    public bool CanGoForward => _historyIndex >= 0 && _historyIndex < _history.Count - 1;

    public bool IsDevToolsEnabled { get; set; }

    public bool IsContextMenuEnabled { get; set; }

    public bool IsStatusBarEnabled { get; set; }

    public bool IsZoomControlEnabled { get; set; }

    public double ZoomFactor { get; private set; }

    public string? HeaderString { get; private set; }

    public string? UserAgentString { get; private set; }

    public void ApplyInstanceConfiguration(NativeWebViewInstanceConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _instanceConfiguration = configuration.Clone();
    }

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

        EnsureWindowCreated(options);

        if (_useNative && _windowHandle != IntPtr.Zero && ObjC.IsMainThread())
        {
            ApplyWindowOptions(options);
            ObjC.SendVoidIntPtr(_windowHandle, NativeSymbols.SelMakeKeyAndOrderFront, IntPtr.Zero);
            _isVisible = ObjC.SendBool(_windowHandle, NativeSymbols.SelIsVisible);
        }
        else
        {
            _isVisible = true;
        }

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

        if (_useNative && _windowHandle != IntPtr.Zero && ObjC.IsMainThread())
        {
            ObjC.SendVoid(_windowHandle, NativeSymbols.SelClose);
        }

        _isVisible = false;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    public void Move(double left, double top)
    {
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.Dialog, nameof(Move));

        if (_useNative && _windowHandle != IntPtr.Zero && ObjC.IsMainThread())
        {
            ObjC.SendVoidCGPoint(_windowHandle, NativeSymbols.SelSetFrameOrigin, new CGPoint(left, top));
        }
    }

    public void Resize(double width, double height)
    {
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.Dialog, nameof(Resize));

        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Dialog size must be greater than zero.");
        }

        if (_useNative && _windowHandle != IntPtr.Zero && ObjC.IsMainThread())
        {
            ObjC.SendVoidCGSize(_windowHandle, NativeSymbols.SelSetContentSize, new CGSize(width, height));
        }
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
        _currentUrl = uri;

        if (_useNative && _webViewHandle != IntPtr.Zero && uri.IsAbsoluteUri && ObjC.IsMainThread())
        {
            LoadUrl(uri);
        }

        NavigationCompleted?.Invoke(this, new NativeWebViewNavigationCompletedEventArgs(uri, isSuccess: true, httpStatusCode: 200));
    }

    public void Reload()
    {
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.Dialog, nameof(Reload));

        if (_currentUrl is null)
        {
            return;
        }

        if (_useNative && _webViewHandle != IntPtr.Zero && ObjC.IsMainThread())
        {
            _ = ObjC.SendIntPtr(_webViewHandle, NativeSymbols.SelReload);
        }

        NavigationStarted?.Invoke(this, new NativeWebViewNavigationStartedEventArgs(_currentUrl, isRedirected: false));
        NavigationCompleted?.Invoke(this, new NativeWebViewNavigationCompletedEventArgs(_currentUrl, isSuccess: true, httpStatusCode: 200));
    }

    public void Stop()
    {
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.Dialog, nameof(Stop));

        if (_useNative && _webViewHandle != IntPtr.Zero && ObjC.IsMainThread())
        {
            ObjC.SendVoid(_webViewHandle, NativeSymbols.SelStopLoading);
        }
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
        _currentUrl = _history[_historyIndex];

        if (_useNative && _webViewHandle != IntPtr.Zero && ObjC.IsMainThread())
        {
            _ = ObjC.SendIntPtr(_webViewHandle, NativeSymbols.SelGoBack);
        }

        NavigationCompleted?.Invoke(this, new NativeWebViewNavigationCompletedEventArgs(_currentUrl, isSuccess: true, httpStatusCode: 200));
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
        _currentUrl = _history[_historyIndex];

        if (_useNative && _webViewHandle != IntPtr.Zero && ObjC.IsMainThread())
        {
            _ = ObjC.SendIntPtr(_webViewHandle, NativeSymbols.SelGoForward);
        }

        NavigationCompleted?.Invoke(this, new NativeWebViewNavigationCompletedEventArgs(_currentUrl, isSuccess: true, httpStatusCode: 200));
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

    public Task<NativeWebViewPrintResult> PrintAsync(
        NativeWebViewPrintSettings? settings = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.Dialog, nameof(PrintAsync));
        EnsureFeature(NativeWebViewFeature.Printing, nameof(PrintAsync));

        if (!_useNative)
        {
            return Task.FromResult(new NativeWebViewPrintResult(NativeWebViewPrintStatus.Success));
        }

        EnsureWindowCreated(options: null);

        if (_webViewHandle == IntPtr.Zero)
        {
            return Task.FromResult(new NativeWebViewPrintResult(
                NativeWebViewPrintStatus.Failed,
                "Native WKWebView handle is unavailable. Show the dialog before printing."));
        }

        if (!ObjC.IsMainThread())
        {
            return Task.FromResult(new NativeWebViewPrintResult(
                NativeWebViewPrintStatus.Failed,
                "Print operations must run on the macOS main thread."));
        }

        if (!string.IsNullOrWhiteSpace(settings?.OutputPath))
        {
            return Task.FromResult(ExportPdf(settings.OutputPath!));
        }

        return Task.FromResult(
            ShowNativePrintUi()
                ? new NativeWebViewPrintResult(NativeWebViewPrintStatus.Success)
                : new NativeWebViewPrintResult(NativeWebViewPrintStatus.Failed, "Native print UI is unavailable."));
    }

    public Task<bool> ShowPrintUiAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.Dialog, nameof(ShowPrintUiAsync));
        EnsureFeature(NativeWebViewFeature.PrintUi, nameof(ShowPrintUiAsync));

        if (!_useNative)
        {
            return Task.FromResult(true);
        }

        EnsureWindowCreated(options: null);
        return Task.FromResult(ShowNativePrintUi());
    }

    public void SetZoomFactor(double zoomFactor)
    {
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.Dialog, nameof(SetZoomFactor));

        if (zoomFactor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(zoomFactor), zoomFactor, "Zoom factor must be greater than zero.");
        }

        if (!Features.Supports(NativeWebViewFeature.ZoomControl))
        {
            return;
        }

        ZoomFactor = zoomFactor;

        if (_useNative && _webViewHandle != IntPtr.Zero && ObjC.IsMainThread() &&
            ObjC.SendBoolIntPtr(_webViewHandle, NativeSymbols.SelRespondsToSelector, NativeSymbols.SelSetPageZoom))
        {
            ObjC.SendVoidDouble(_webViewHandle, NativeSymbols.SelSetPageZoom, zoomFactor);
        }
    }

    public void SetUserAgent(string? userAgent)
    {
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.Dialog, nameof(SetUserAgent));

        UserAgentString = userAgent;

        if (_useNative && _webViewHandle != IntPtr.Zero && ObjC.IsMainThread())
        {
            var userAgentHandle = userAgent is null
                ? IntPtr.Zero
                : CreateNSString(userAgent);
            ObjC.SendVoidIntPtr(_webViewHandle, NativeSymbols.SelSetCustomUserAgent, userAgentHandle);
        }
    }

    public void SetHeader(string? header)
    {
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.Dialog, nameof(SetHeader));
        HeaderString = header;
    }

    public bool TryGetPlatformHandle(out NativePlatformHandle handle)
    {
        handle = _windowHandle != IntPtr.Zero
            ? new NativePlatformHandle(_windowHandle, "NSWindow")
            : PlaceholderPlatformHandle;
        return true;
    }

    public bool TryGetDialogHandle(out NativePlatformHandle handle)
    {
        handle = _webViewHandle != IntPtr.Zero
            ? new NativePlatformHandle(_webViewHandle, "WKWebView")
            : PlaceholderDialogHandle;
        return true;
    }

    public bool TryGetHostWindowHandle(out NativePlatformHandle handle)
    {
        handle = _windowHandle != IntPtr.Zero
            ? new NativePlatformHandle(_windowHandle, "NSWindow")
            : PlaceholderHostWindowHandle;
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_useNative && ObjC.IsMainThread())
        {
            if (_windowHandle != IntPtr.Zero)
            {
                ObjC.SendVoid(_windowHandle, NativeSymbols.SelClose);
            }

            if (_webViewHandle != IntPtr.Zero)
            {
                ObjC.SendVoid(_webViewHandle, NativeSymbols.SelStopLoading);
                ObjC.SendVoid(_webViewHandle, NativeSymbols.SelRemoveFromSuperview);
                ObjC.SendVoid(_webViewHandle, NativeSymbols.SelRelease);
                _webViewHandle = IntPtr.Zero;
            }

            if (_configurationHandle != IntPtr.Zero)
            {
                ObjC.SendVoid(_configurationHandle, NativeSymbols.SelRelease);
                _configurationHandle = IntPtr.Zero;
            }

            if (_windowHandle != IntPtr.Zero)
            {
                ObjC.SendVoid(_windowHandle, NativeSymbols.SelRelease);
                _windowHandle = IntPtr.Zero;
            }

            _contentViewHandle = IntPtr.Zero;
        }

        _isVisible = false;
    }

    private void EnsureWindowCreated(NativeWebDialogShowOptions? options)
    {
        if (!_useNative || !ObjC.IsMainThread())
        {
            return;
        }

        if (_windowHandle != IntPtr.Zero && _webViewHandle != IntPtr.Zero)
        {
            return;
        }

        var normalized = options ?? new NativeWebDialogShowOptions();
        var width = normalized.Width > 0 ? normalized.Width : 1024;
        var height = normalized.Height > 0 ? normalized.Height : 768;
        var left = normalized.Left;
        var top = normalized.Top;

        var windowStyleMask =
            NSWindowStyleMaskTitled |
            NSWindowStyleMaskClosable |
            NSWindowStyleMaskMiniaturizable |
            NSWindowStyleMaskResizable;

        var frame = new CGRect(new CGPoint(left, top), new CGSize(width, height));

        _windowHandle = ObjC.SendIntPtrCGRectNUIntNIntBool(
            ObjC.SendIntPtr(NativeSymbols.NSWindowClass, NativeSymbols.SelAlloc),
            NativeSymbols.SelInitWithContentRectStyleMaskBackingDefer,
            frame,
            windowStyleMask,
            NSBackingStoreBuffered,
            false);

        if (_windowHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create NSWindow for NativeWebDialog.");
        }

        ObjC.SendVoidBool(_windowHandle, NativeSymbols.SelSetReleasedWhenClosed, false);

        _contentViewHandle = ObjC.SendIntPtr(_windowHandle, NativeSymbols.SelContentView);
        if (_contentViewHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to resolve NSWindow contentView.");
        }

        _configurationHandle = ObjC.SendIntPtr(ObjC.SendIntPtr(NativeSymbols.WKWebViewConfigurationClass, NativeSymbols.SelAlloc), NativeSymbols.SelInit);
        ApplyProxyConfiguration();
        _webViewHandle = ObjC.SendIntPtrCGRectIntPtr(
            ObjC.SendIntPtr(NativeSymbols.WKWebViewClass, NativeSymbols.SelAlloc),
            NativeSymbols.SelInitWithFrameConfiguration,
            CGRect.Zero,
            _configurationHandle);

        if (_webViewHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create WKWebView for NativeWebDialog.");
        }

        ObjC.SendVoidIntPtr(_contentViewHandle, NativeSymbols.SelAddSubview, _webViewHandle);
        ObjC.SendVoidNUInt(_webViewHandle, NativeSymbols.SelSetAutoresizingMask, NSViewWidthSizable | NSViewHeightSizable);
        var contentBounds = ObjC.SendCGRect(_contentViewHandle, NativeSymbols.SelBounds);
        ObjC.SendVoidCGRect(_webViewHandle, NativeSymbols.SelSetFrame, contentBounds);

        if (!string.IsNullOrWhiteSpace(UserAgentString))
        {
            var userAgentHandle = CreateNSString(UserAgentString);
            ObjC.SendVoidIntPtr(_webViewHandle, NativeSymbols.SelSetCustomUserAgent, userAgentHandle);
        }

        if (ZoomFactor > 0 &&
            ObjC.SendBoolIntPtr(_webViewHandle, NativeSymbols.SelRespondsToSelector, NativeSymbols.SelSetPageZoom))
        {
            ObjC.SendVoidDouble(_webViewHandle, NativeSymbols.SelSetPageZoom, ZoomFactor);
        }

        if (_currentUrl is { IsAbsoluteUri: true } initialUri)
        {
            LoadUrl(initialUri);
        }
    }

    private void ApplyWindowOptions(NativeWebDialogShowOptions? options)
    {
        if (!_useNative || _windowHandle == IntPtr.Zero)
        {
            return;
        }

        var normalized = options ?? new NativeWebDialogShowOptions();
        var title = string.IsNullOrWhiteSpace(normalized.Title)
            ? "NativeWebView Dialog"
            : normalized.Title;

        var titleHandle = CreateNSString(title);
        ObjC.SendVoidIntPtr(_windowHandle, NativeSymbols.SelSetTitle, titleHandle);

        if (normalized.Width > 0 && normalized.Height > 0)
        {
            ObjC.SendVoidCGSize(_windowHandle, NativeSymbols.SelSetContentSize, new CGSize(normalized.Width, normalized.Height));
        }

        if (normalized.CenterOnParent)
        {
            ObjC.SendVoid(_windowHandle, NativeSymbols.SelCenter);
        }
        else
        {
            ObjC.SendVoidCGPoint(_windowHandle, NativeSymbols.SelSetFrameOrigin, new CGPoint(normalized.Left, normalized.Top));
        }
    }

    private void LoadUrl(Uri uri)
    {
        var nsUrl = CreateNSStringBackedObject(NativeSymbols.NSURLClass, NativeSymbols.SelUrlWithString, uri.AbsoluteUri);
        if (nsUrl == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create NSURL from URI.");
        }

        var request = ObjC.SendIntPtrIntPtr(NativeSymbols.NSURLRequestClass, NativeSymbols.SelRequestWithUrl, nsUrl);
        if (request == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create NSURLRequest.");
        }

        _ = ObjC.SendIntPtrIntPtr(_webViewHandle, NativeSymbols.SelLoadRequest, request);
    }

    private static IntPtr CreateNSStringBackedObject(IntPtr classHandle, IntPtr selector, string value)
    {
        var nsString = CreateNSString(value);
        return ObjC.SendIntPtrIntPtr(classHandle, selector, nsString);
    }

    private void ApplyProxyConfiguration()
    {
        var proxyConfiguration = NativeWebViewProxyConfigurationResolver.Resolve(_instanceConfiguration.EnvironmentOptions.Proxy);
        if (proxyConfiguration is null)
        {
            return;
        }

        if (proxyConfiguration.Kind == NativeWebViewProxyKind.AutoConfigUrl)
        {
            throw new PlatformNotSupportedException(
                "WKWebView proxy auto-configuration URLs are not supported by the current macOS dialog integration. Use an explicit http(s) or socks5 proxy server.");
        }

        if (!OperatingSystem.IsMacOSVersionAtLeast(14))
        {
            throw new PlatformNotSupportedException(
                "Per-instance proxy configuration requires macOS 14.0 or later for WKWebsiteDataStore.proxyConfigurations.");
        }

        var dataStoreHandle = CreateWebsiteDataStoreHandle(proxyConfiguration);
        if (dataStoreHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create a dedicated WKWebsiteDataStore for dialog proxy configuration.");
        }

        if (!ObjC.SendBoolIntPtr(dataStoreHandle, NativeSymbols.SelRespondsToSelector, NativeSymbols.SelSetProxyConfigurations))
        {
            throw new PlatformNotSupportedException(
                "The current WKWebsiteDataStore runtime does not expose proxyConfigurations.");
        }

        var nativeProxyConfiguration = CreateNativeProxyConfiguration(proxyConfiguration);
        try
        {
            var arrayHandle = ObjC.SendIntPtrIntPtr(NativeSymbols.NSArrayClass, NativeSymbols.SelArrayWithObject, nativeProxyConfiguration);
            if (arrayHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create the dialog proxy configuration array for WKWebsiteDataStore.");
            }

            ObjC.SendVoidIntPtr(dataStoreHandle, NativeSymbols.SelSetProxyConfigurations, arrayHandle);
            ObjC.SendVoidIntPtr(_configurationHandle, NativeSymbols.SelSetWebsiteDataStore, dataStoreHandle);
        }
        finally
        {
            Network.nw_release(nativeProxyConfiguration);
        }
    }

    private IntPtr CreateWebsiteDataStoreHandle(NativeWebViewResolvedProxyConfiguration proxyConfiguration)
    {
        var identifier = CreateWebsiteDataStoreIdentifier(_instanceConfiguration, proxyConfiguration);
        var uuidHandle = CreateNativeUuid(identifier);
        try
        {
            return ObjC.SendIntPtrIntPtr(
                NativeSymbols.WKWebsiteDataStoreClass,
                NativeSymbols.SelDataStoreForIdentifier,
                uuidHandle);
        }
        finally
        {
            if (uuidHandle != IntPtr.Zero)
            {
                ObjC.SendVoid(uuidHandle, NativeSymbols.SelRelease);
            }
        }
    }

    private static IntPtr CreateNativeProxyConfiguration(NativeWebViewResolvedProxyConfiguration configuration)
    {
        var endpoint = CreateEndpoint(configuration.Host, configuration.Port);
        IntPtr tlsOptions = IntPtr.Zero;

        try
        {
            if (configuration.UseTls)
            {
                tlsOptions = Network.nw_tls_create_options();
                if (tlsOptions == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Failed to create TLS options for the configured proxy.");
                }
            }

            var proxyHandle = configuration.Kind switch
            {
                NativeWebViewProxyKind.HttpConnect => Network.nw_proxy_config_create_http_connect(endpoint, tlsOptions),
                NativeWebViewProxyKind.Socks5 => Network.nw_proxy_config_create_socksv5(endpoint),
                _ => IntPtr.Zero,
            };

            if (proxyHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create the native dialog proxy configuration.");
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(configuration.Username))
                {
                    Network.nw_proxy_config_set_username_and_password(
                        proxyHandle,
                        configuration.Username!,
                        configuration.Password);
                }

                foreach (var excludedDomain in configuration.ExcludedDomains)
                {
                    if (TryNormalizeExcludedDomain(excludedDomain, out var normalizedExcludedDomain))
                    {
                        Network.nw_proxy_config_add_excluded_domain(proxyHandle, normalizedExcludedDomain);
                    }
                }

                return proxyHandle;
            }
            catch
            {
                Network.nw_release(proxyHandle);
                throw;
            }
        }
        finally
        {
            if (tlsOptions != IntPtr.Zero)
            {
                Network.nw_release(tlsOptions);
            }

            if (endpoint != IntPtr.Zero)
            {
                Network.nw_release(endpoint);
            }
        }
    }

    private static IntPtr CreateEndpoint(string host, int port)
    {
        var hostUtf8 = Marshal.StringToCoTaskMemUTF8(host);
        var portUtf8 = Marshal.StringToCoTaskMemUTF8(port.ToString(CultureInfo.InvariantCulture));
        try
        {
            var endpoint = Network.nw_endpoint_create_host(hostUtf8, portUtf8);
            if (endpoint == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Failed to create a dialog proxy endpoint for '{host}:{port}'.");
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

    private static Guid CreateWebsiteDataStoreIdentifier(
        NativeWebViewInstanceConfiguration configuration,
        NativeWebViewResolvedProxyConfiguration proxyConfiguration)
    {
        var builder = new StringBuilder();
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

        var environmentOptions = configuration.EnvironmentOptions;
        AppendIdentityPart(builder, "user-data-folder", environmentOptions.UserDataFolder);
        AppendIdentityPart(builder, "cache-folder", environmentOptions.CacheFolder);
        AppendIdentityPart(builder, "cookie-data-folder", environmentOptions.CookieDataFolder);
        AppendIdentityPart(builder, "session-data-folder", environmentOptions.SessionDataFolder);
        AppendIdentityPart(builder, "profile-name", configuration.ControllerOptions.ProfileName);

        return CreateDeterministicGuid(builder.ToString());
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

        // Mark the identifier as a stable name-based UUID.
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);

        return new Guid(guidBytes);
    }

    private static IntPtr CreateNativeUuid(Guid identifier)
    {
        var identifierString = CreateNSString(identifier.ToString("D"));
        var uuidHandle = ObjC.SendIntPtrIntPtr(
            ObjC.SendIntPtr(NativeSymbols.NSUUIDClass, NativeSymbols.SelAlloc),
            NativeSymbols.SelInitWithUUIDString,
            identifierString);

        if (uuidHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Failed to create NSUUID for dialog website data store identifier '{identifier:D}'.");
        }

        return uuidHandle;
    }

    private NativeWebViewPrintResult ExportPdf(string outputPath)
    {
        try
        {
            if (!ObjC.SendBoolIntPtr(_webViewHandle, NativeSymbols.SelRespondsToSelector, NativeSymbols.SelDataWithPdfInsideRect))
            {
                return new NativeWebViewPrintResult(
                    NativeWebViewPrintStatus.NotSupported,
                    "WKWebView PDF export API is unavailable on this macOS runtime.");
            }

            var fullPath = Path.GetFullPath(outputPath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var bounds = ObjC.SendCGRect(_webViewHandle, NativeSymbols.SelBounds);
            var pdfData = ObjC.SendIntPtrCGRect(_webViewHandle, NativeSymbols.SelDataWithPdfInsideRect, bounds);
            if (pdfData == IntPtr.Zero)
            {
                return new NativeWebViewPrintResult(NativeWebViewPrintStatus.Failed, "WKWebView returned no PDF data.");
            }

            var pathHandle = CreateNSString(fullPath);
            var written = ObjC.SendBoolIntPtrBool(pdfData, NativeSymbols.SelWriteToFileAtomically, pathHandle, true);
            return written
                ? new NativeWebViewPrintResult(NativeWebViewPrintStatus.Success)
                : new NativeWebViewPrintResult(NativeWebViewPrintStatus.Failed, $"Failed to write PDF to '{fullPath}'.");
        }
        catch (Exception ex)
        {
            return new NativeWebViewPrintResult(NativeWebViewPrintStatus.Failed, ex.Message);
        }
    }

    private bool ShowNativePrintUi()
    {
        if (!_useNative || _webViewHandle == IntPtr.Zero || !ObjC.IsMainThread())
        {
            return false;
        }

        if (!ObjC.SendBoolIntPtr(_webViewHandle, NativeSymbols.SelRespondsToSelector, NativeSymbols.SelPrint))
        {
            return false;
        }

        ObjC.SendVoidIntPtr(_webViewHandle, NativeSymbols.SelPrint, IntPtr.Zero);
        return true;
    }

    private static IntPtr CreateNSString(string value)
    {
        var utf8 = Marshal.StringToCoTaskMemUTF8(value);
        try
        {
            return ObjC.SendIntPtrIntPtr(NativeSymbols.NSStringClass, NativeSymbols.SelStringWithUtf8String, utf8);
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8);
        }
    }

    private void EnsureFeature(NativeWebViewFeature feature, string operationName)
    {
        if (!Features.Supports(feature))
        {
            throw new PlatformNotSupportedException($"Operation '{operationName}' is not supported on platform '{Platform}'.");
        }
    }

    private void RaiseNewWindowRequested(Uri? uri)
    {
        NewWindowRequested?.Invoke(this, new NativeWebViewNewWindowRequestedEventArgs(uri));
    }

    private void RaiseWebResourceRequested(Uri? uri, string method, IReadOnlyDictionary<string, string>? headers = null)
    {
        WebResourceRequested?.Invoke(this, new NativeWebViewResourceRequestedEventArgs(uri, method, headers));
    }

    private void RaiseContextMenuRequested(double x, double y)
    {
        ContextMenuRequested?.Invoke(this, new NativeWebViewContextMenuRequestedEventArgs(x, y));
    }

    private void EnsureNotDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CGPoint
    {
        public readonly double X;
        public readonly double Y;

        public CGPoint(double x, double y)
        {
            X = x;
            Y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CGSize
    {
        public readonly double Width;
        public readonly double Height;

        public CGSize(double width, double height)
        {
            Width = width;
            Height = height;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CGRect
    {
        public readonly CGPoint Origin;
        public readonly CGSize Size;

        public CGRect(CGPoint origin, CGSize size)
        {
            Origin = origin;
            Size = size;
        }

        public static CGRect Zero => new(new CGPoint(0, 0), new CGSize(0, 0));
    }

    private static class ObjC
    {
        private const int RtldNow = 2;
        private static readonly object FrameworkLoadGate = new();
        private static bool _frameworksLoaded;

        [DllImport("/usr/lib/libobjc.A.dylib")]
        public static extern IntPtr objc_getClass(string name);

        [DllImport("/usr/lib/libobjc.A.dylib")]
        public static extern IntPtr sel_registerName(string name);

        [DllImport("/usr/lib/libSystem.B.dylib")]
        private static extern IntPtr dlopen(string path, int mode);

        [DllImport("/usr/lib/libSystem.B.dylib")]
        private static extern int pthread_main_np();

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_IntPtr_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_IntPtr_CGRect_IntPtr(IntPtr receiver, IntPtr selector, CGRect arg1, IntPtr arg2);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_IntPtr_CGRect(IntPtr receiver, IntPtr selector, CGRect arg1);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern CGRect objc_msgSend_CGRect(IntPtr receiver, IntPtr selector);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_IntPtr_CGRect_NUInt_NInt_Byte(
            IntPtr receiver,
            IntPtr selector,
            CGRect arg1,
            nuint arg2,
            nint arg3,
            byte arg4);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern byte objc_msgSend_Byte(IntPtr receiver, IntPtr selector);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern byte objc_msgSend_Byte_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern byte objc_msgSend_Byte_IntPtr_Byte(IntPtr receiver, IntPtr selector, IntPtr arg1, byte arg2);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend_Void(IntPtr receiver, IntPtr selector);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend_Void_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend_Void_Double(IntPtr receiver, IntPtr selector, double arg1);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend_Void_Byte(IntPtr receiver, IntPtr selector, byte arg1);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend_Void_NUInt(IntPtr receiver, IntPtr selector, nuint arg1);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend_Void_CGPoint(IntPtr receiver, IntPtr selector, CGPoint arg1);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend_Void_CGSize(IntPtr receiver, IntPtr selector, CGSize arg1);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend_Void_CGRect(IntPtr receiver, IntPtr selector, CGRect arg1);

        public static IntPtr GetClass(string name)
        {
            if (!OperatingSystem.IsMacOS())
            {
                throw new PlatformNotSupportedException("Objective-C interop is only available on macOS.");
            }

            EnsureFrameworksLoaded();

            var handle = objc_getClass(name);
            if (handle == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Objective-C class '{name}' was not found.");
            }

            return handle;
        }

        public static IntPtr GetSelector(string name)
        {
            if (!OperatingSystem.IsMacOS())
            {
                throw new PlatformNotSupportedException("Objective-C interop is only available on macOS.");
            }

            var handle = sel_registerName(name);
            if (handle == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Objective-C selector '{name}' was not found.");
            }

            return handle;
        }

        public static IntPtr SendIntPtr(IntPtr receiver, IntPtr selector)
        {
            return objc_msgSend_IntPtr(receiver, selector);
        }

        public static IntPtr SendIntPtrIntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1)
        {
            return objc_msgSend_IntPtr_IntPtr(receiver, selector, arg1);
        }

        public static IntPtr SendIntPtrCGRectIntPtr(IntPtr receiver, IntPtr selector, CGRect arg1, IntPtr arg2)
        {
            return objc_msgSend_IntPtr_CGRect_IntPtr(receiver, selector, arg1, arg2);
        }

        public static IntPtr SendIntPtrCGRect(IntPtr receiver, IntPtr selector, CGRect arg1)
        {
            return objc_msgSend_IntPtr_CGRect(receiver, selector, arg1);
        }

        public static CGRect SendCGRect(IntPtr receiver, IntPtr selector)
        {
            return objc_msgSend_CGRect(receiver, selector);
        }

        public static IntPtr SendIntPtrCGRectNUIntNIntBool(
            IntPtr receiver,
            IntPtr selector,
            CGRect arg1,
            nuint arg2,
            nint arg3,
            bool arg4)
        {
            return objc_msgSend_IntPtr_CGRect_NUInt_NInt_Byte(receiver, selector, arg1, arg2, arg3, arg4 ? (byte)1 : (byte)0);
        }

        public static bool SendBool(IntPtr receiver, IntPtr selector)
        {
            return objc_msgSend_Byte(receiver, selector) != 0;
        }

        public static bool IsMainThread()
        {
            return pthread_main_np() != 0;
        }

        public static bool SendBoolIntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1)
        {
            return objc_msgSend_Byte_IntPtr(receiver, selector, arg1) != 0;
        }

        public static bool SendBoolIntPtrBool(IntPtr receiver, IntPtr selector, IntPtr arg1, bool arg2)
        {
            return objc_msgSend_Byte_IntPtr_Byte(receiver, selector, arg1, arg2 ? (byte)1 : (byte)0) != 0;
        }

        public static void SendVoid(IntPtr receiver, IntPtr selector)
        {
            objc_msgSend_Void(receiver, selector);
        }

        public static void SendVoidIntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1)
        {
            objc_msgSend_Void_IntPtr(receiver, selector, arg1);
        }

        public static void SendVoidDouble(IntPtr receiver, IntPtr selector, double arg1)
        {
            objc_msgSend_Void_Double(receiver, selector, arg1);
        }

        public static void SendVoidBool(IntPtr receiver, IntPtr selector, bool arg1)
        {
            objc_msgSend_Void_Byte(receiver, selector, arg1 ? (byte)1 : (byte)0);
        }

        public static void SendVoidNUInt(IntPtr receiver, IntPtr selector, nuint arg1)
        {
            objc_msgSend_Void_NUInt(receiver, selector, arg1);
        }

        public static void SendVoidCGPoint(IntPtr receiver, IntPtr selector, CGPoint arg1)
        {
            objc_msgSend_Void_CGPoint(receiver, selector, arg1);
        }

        public static void SendVoidCGSize(IntPtr receiver, IntPtr selector, CGSize arg1)
        {
            objc_msgSend_Void_CGSize(receiver, selector, arg1);
        }

        public static void SendVoidCGRect(IntPtr receiver, IntPtr selector, CGRect arg1)
        {
            objc_msgSend_Void_CGRect(receiver, selector, arg1);
        }

        private static void EnsureFrameworksLoaded()
        {
            if (_frameworksLoaded)
            {
                return;
            }

            lock (FrameworkLoadGate)
            {
                if (_frameworksLoaded)
                {
                    return;
                }

                LoadFramework("/System/Library/Frameworks/Foundation.framework/Foundation");
                LoadFramework("/System/Library/Frameworks/AppKit.framework/AppKit");
                LoadFramework("/System/Library/Frameworks/WebKit.framework/WebKit");
                LoadFramework("/System/Library/Frameworks/Network.framework/Network");
                _frameworksLoaded = true;
            }
        }

        private static void LoadFramework(string path)
        {
            var handle = dlopen(path, RtldNow);
            if (handle == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Failed to load framework '{path}'.");
            }
        }
    }

    private static class Network
    {
        [DllImport("/System/Library/Frameworks/Network.framework/Network")]
        public static extern IntPtr nw_endpoint_create_host(IntPtr hostname, IntPtr port);

        [DllImport("/System/Library/Frameworks/Network.framework/Network")]
        public static extern IntPtr nw_proxy_config_create_http_connect(IntPtr proxyEndpoint, IntPtr proxyTlsOptions);

        [DllImport("/System/Library/Frameworks/Network.framework/Network")]
        public static extern IntPtr nw_proxy_config_create_socksv5(IntPtr proxyEndpoint);

        [DllImport("/System/Library/Frameworks/Network.framework/Network")]
        public static extern IntPtr nw_tls_create_options();

        [DllImport("/System/Library/Frameworks/Network.framework/Network")]
        private static extern void nw_proxy_config_set_username_and_password(IntPtr proxyConfig, IntPtr username, IntPtr password);

        [DllImport("/System/Library/Frameworks/Network.framework/Network")]
        private static extern void nw_proxy_config_add_excluded_domain(IntPtr proxyConfig, IntPtr excludedDomain);

        [DllImport("/System/Library/Frameworks/Network.framework/Network")]
        public static extern void nw_release(IntPtr obj);

        public static void nw_proxy_config_set_username_and_password(IntPtr proxyConfig, string username, string? password)
        {
            var usernameUtf8 = Marshal.StringToCoTaskMemUTF8(username);
            var passwordUtf8 = password is null ? IntPtr.Zero : Marshal.StringToCoTaskMemUTF8(password);
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
}
