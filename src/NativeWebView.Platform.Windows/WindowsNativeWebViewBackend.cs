using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Web.WebView2.Core;
using NativeWebView.Core;
using NativeWebView.Interop;

namespace NativeWebView.Platform.Windows;

public sealed class WindowsNativeWebViewBackend
    : INativeWebViewBackend,
      INativeWebViewFrameSource,
      INativeWebViewPlatformHandleProvider,
      INativeWebViewInstanceConfigurationTarget,
      INativeWebViewNativeControlAttachment
{
    private static readonly NativePlatformHandle PlaceholderPlatformHandle = new((nint)0x1001, "HWND");
    private static readonly NativePlatformHandle PlaceholderViewHandle = new((nint)0x1002, "ICoreWebView2");
    private static readonly NativePlatformHandle PlaceholderControllerHandle = new((nint)0x1003, "ICoreWebView2Controller");
    private static readonly object WindowClassGate = new();
    private static readonly Win32.WndProc ChildWindowProcDelegate = ChildWindowProc;

    private static ushort _childWindowClassAtom;

    private readonly SemaphoreSlim _runtimeGate = new(1, 1);
    private readonly List<Uri> _history = [];
    private readonly INativeWebViewCommandManager _commandManager = NativeWebViewBackendSupport.NoopCommandManagerInstance;
    private readonly INativeWebViewCookieManager _cookieManager = NativeWebViewBackendSupport.NoopCookieManagerInstance;

    private TaskCompletionSource<bool> _attachmentTcs = CreatePendingAttachmentSource();
    private NativeWebViewInstanceConfiguration _instanceConfiguration = new();

    private CoreWebView2Environment? _environment;
    private CoreWebView2Controller? _controller;
    private CoreWebView2? _coreWebView;
    private NativeWebViewEnvironmentOptions? _preparedEnvironmentOptions;
    private NativeWebViewControllerOptions? _preparedControllerOptions;

    private Uri? _currentUrl;
    private Uri? _pendingNavigationUri;

    private GCHandle _selfHandle;
    private nint _parentWindowHandle;
    private nint _childWindowHandle;
    private nint _viewComHandle;
    private nint _controllerComHandle;

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

    public WindowsNativeWebViewBackend()
    {
        Platform = NativeWebViewPlatform.Windows;
        Features = WindowsPlatformFeatures.Instance;
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
            ApplyRuntimeSettings();
        }
    }

    public bool IsContextMenuEnabled
    {
        get => _isContextMenuEnabled;
        set
        {
            EnsureNotDisposed();
            _isContextMenuEnabled = value;
            ApplyRuntimeSettings();
        }
    }

    public bool IsStatusBarEnabled
    {
        get => _isStatusBarEnabled;
        set
        {
            EnsureNotDisposed();
            _isStatusBarEnabled = value;
            ApplyRuntimeSettings();
        }
    }

    public bool IsZoomControlEnabled
    {
        get => _isZoomControlEnabled;
        set
        {
            EnsureNotDisposed();
            _isZoomControlEnabled = value;
            ApplyRuntimeSettings();
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

        if (OperatingSystem.IsWindows())
        {
            _runtimeInitializationRequested = true;

            if (_childWindowHandle != IntPtr.Zero)
            {
                await EnsureRuntimeInitializedAsync(cancellationToken).ConfigureAwait(true);
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

            if (_coreWebView is not null)
            {
                NavigateCore(uri);
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
            if (_coreWebView is not null)
            {
                _coreWebView.Reload();
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

        if (_coreWebView is not null)
        {
            _coreWebView.Stop();
        }
    }

    public void GoBack()
    {
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.EmbeddedView, nameof(GoBack));

        if (ShouldUseRuntimePath())
        {
            if (_coreWebView is not null && _coreWebView.CanGoBack)
            {
                _coreWebView.GoBack();
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
            if (_coreWebView is not null && _coreWebView.CanGoForward)
            {
                _coreWebView.GoForward();
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
            await EnsureRuntimeInitializedAsync(cancellationToken).ConfigureAwait(true);
            return await _coreWebView!.ExecuteScriptAsync(script).ConfigureAwait(true);
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
            await EnsureRuntimeInitializedAsync(cancellationToken).ConfigureAwait(true);
            _coreWebView!.PostWebMessageAsJson(jsonMessage);
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
            await EnsureRuntimeInitializedAsync(cancellationToken).ConfigureAwait(true);
            _coreWebView!.PostWebMessageAsString(message);
            return;
        }

        EnsureStubInitialized();
        WebMessageReceived?.Invoke(this, new NativeWebViewMessageReceivedEventArgs(message, json: null));
    }

    public void OpenDevToolsWindow()
    {
        EnsureNotDisposed();
        EnsureFeature(NativeWebViewFeature.DevTools, nameof(OpenDevToolsWindow));

        if (ShouldUseRuntimePath() && _coreWebView is not null && _isDevToolsEnabled)
        {
            _coreWebView.OpenDevToolsWindow();
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
            await EnsureRuntimeInitializedAsync(cancellationToken).ConfigureAwait(true);

            try
            {
                if (!string.IsNullOrWhiteSpace(settings?.OutputPath))
                {
                    var fullPath = Path.GetFullPath(settings.OutputPath!);
                    var directory = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    var printed = await _coreWebView!.PrintToPdfAsync(fullPath, CreatePrintSettings(settings)).ConfigureAwait(true);
                    return printed
                        ? new NativeWebViewPrintResult(NativeWebViewPrintStatus.Success)
                        : new NativeWebViewPrintResult(NativeWebViewPrintStatus.Failed, $"Failed to write PDF to '{fullPath}'.");
                }

                var status = await _coreWebView!.PrintAsync(CreatePrintSettings(settings)).ConfigureAwait(true);
                return status == CoreWebView2PrintStatus.Succeeded
                    ? new NativeWebViewPrintResult(NativeWebViewPrintStatus.Success)
                    : new NativeWebViewPrintResult(NativeWebViewPrintStatus.Failed, status.ToString());
            }
            catch (Exception ex)
            {
                return new NativeWebViewPrintResult(NativeWebViewPrintStatus.Failed, ex.Message);
            }
        }

        EnsureStubInitialized();
        return new NativeWebViewPrintResult(NativeWebViewPrintStatus.Success);
    }

    public async Task<bool> ShowPrintUiAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureNotDisposed();

        if (!Features.Supports(NativeWebViewFeature.PrintUi))
        {
            return false;
        }

        if (ShouldUseRuntimePath())
        {
            await EnsureRuntimeInitializedAsync(cancellationToken).ConfigureAwait(true);

            try
            {
                _coreWebView!.ShowPrintUI();
                return true;
            }
            catch
            {
                return false;
            }
        }

        EnsureStubInitialized();
        return true;
    }

    public void SetZoomFactor(double zoomFactor)
    {
        EnsureNotDisposed();

        if (zoomFactor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(zoomFactor), zoomFactor, "Zoom factor must be greater than zero.");
        }

        _zoomFactor = zoomFactor;

        if (_controller is not null)
        {
            _controller.ZoomFactor = zoomFactor;
        }
    }

    public void SetUserAgent(string? userAgent)
    {
        EnsureNotDisposed();
        _userAgentString = userAgent;
        ApplyRuntimeSettings();
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

        if (_controller is null)
        {
            return;
        }

        _controller.MoveFocus(direction switch
        {
            NativeWebViewFocusMoveDirection.Next => CoreWebView2MoveFocusReason.Next,
            NativeWebViewFocusMoveDirection.Previous => CoreWebView2MoveFocusReason.Previous,
            _ => CoreWebView2MoveFocusReason.Programmatic,
        });
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
        handle = _childWindowHandle != IntPtr.Zero
            ? new NativePlatformHandle(_childWindowHandle, "HWND")
            : PlaceholderPlatformHandle;
        return true;
    }

    public bool TryGetViewHandle(out NativePlatformHandle handle)
    {
        handle = _viewComHandle != IntPtr.Zero
            ? new NativePlatformHandle(_viewComHandle, "ICoreWebView2")
            : PlaceholderViewHandle;
        return true;
    }

    public bool TryGetControllerHandle(out NativePlatformHandle handle)
    {
        handle = _controllerComHandle != IntPtr.Zero
            ? new NativePlatformHandle(_controllerComHandle, "ICoreWebView2Controller")
            : PlaceholderControllerHandle;
        return true;
    }

    public NativePlatformHandle AttachToNativeParent(NativePlatformHandle parentHandle)
    {
        EnsureNotDisposed();

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows native control attachment can only run on Windows.");
        }

        if (parentHandle.Handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Parent native handle is invalid.");
        }

        if (!string.Equals(parentHandle.HandleDescriptor, "HWND", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Windows native control attachment requires an HWND parent, but received '{parentHandle.HandleDescriptor}'.");
        }

        if (_childWindowHandle != IntPtr.Zero)
        {
            if (_parentWindowHandle == parentHandle.Handle)
            {
                return new NativePlatformHandle(_childWindowHandle, "HWND");
            }

            DetachFromNativeParent();
        }

        EnsureChildWindowClassRegistered();

        var instanceHandle = Win32.GetModuleHandle(null);
        var childHandle = Win32.CreateWindowEx(
            0,
            Win32.ChildWindowClassName,
            string.Empty,
            Win32.WindowStyles.WS_CHILD |
            Win32.WindowStyles.WS_VISIBLE |
            Win32.WindowStyles.WS_CLIPSIBLINGS |
            Win32.WindowStyles.WS_CLIPCHILDREN,
            0,
            0,
            1,
            1,
            parentHandle.Handle,
            IntPtr.Zero,
            instanceHandle,
            IntPtr.Zero);

        if (childHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"Failed to create Windows child host window. Win32 error: {Marshal.GetLastWin32Error()}.");
        }

        _parentWindowHandle = parentHandle.Handle;
        _childWindowHandle = childHandle;
        _attachmentTcs.TrySetResult(true);

        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }

        _selfHandle = GCHandle.Alloc(this);
        Win32.SetWindowLongPtr(childHandle, Win32.WindowLongIndex.GWLP_USERDATA, GCHandle.ToIntPtr(_selfHandle));
        UpdateControllerBounds();

        if (_runtimeInitializationRequested)
        {
            _ = TryInitializeRuntimeInBackgroundAsync();
        }

        return new NativePlatformHandle(childHandle, "HWND");
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

        ReleaseComHandle(ref _viewComHandle);
        ReleaseComHandle(ref _controllerComHandle);

        _environment = null;
        _coreWebView = null;
        _controller = null;
        _preparedEnvironmentOptions = null;
        _preparedControllerOptions = null;

        DestroyRequested?.Invoke(this, new NativeWebViewDestroyRequestedEventArgs("Disposed"));
        _runtimeGate.Dispose();
    }

    private void DetachFromNativeParentCore()
    {
        DestroyRuntimeController();

        if (_childWindowHandle != IntPtr.Zero)
        {
            Win32.SetWindowLongPtr(_childWindowHandle, Win32.WindowLongIndex.GWLP_USERDATA, IntPtr.Zero);

            if (Win32.IsWindow(_childWindowHandle))
            {
                Win32.DestroyWindow(_childWindowHandle);
            }
        }

        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }

        _childWindowHandle = IntPtr.Zero;
        _parentWindowHandle = IntPtr.Zero;
        _attachmentTcs = CreatePendingAttachmentSource();
    }

    private static TaskCompletionSource<bool> CreatePendingAttachmentSource()
    {
        return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static IntPtr ChildWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        var userData = Win32.GetWindowLongPtr(hwnd, Win32.WindowLongIndex.GWLP_USERDATA);
        if (userData != IntPtr.Zero)
        {
            var handle = GCHandle.FromIntPtr(userData);
            if (handle.IsAllocated && handle.Target is WindowsNativeWebViewBackend backend)
            {
                return backend.ProcessChildWindowMessage(hwnd, message, wParam, lParam);
            }
        }

        return Win32.DefWindowProc(hwnd, message, wParam, lParam);
    }

    private IntPtr ProcessChildWindowMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case Win32.WindowMessage.WM_SIZE:
                UpdateControllerBounds();
                break;

            case Win32.WindowMessage.WM_SETFOCUS:
                if (_controller is not null)
                {
                    _controller.MoveFocus(CoreWebView2MoveFocusReason.Programmatic);
                }

                break;
        }

        return Win32.DefWindowProc(hwnd, message, wParam, lParam);
    }

    [SupportedOSPlatform("windows")]
    private async Task TryInitializeRuntimeInBackgroundAsync()
    {
        try
        {
            await EnsureRuntimeInitializedAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch
        {
            // Explicit InitializeAsync should surface failures. Background warmup is best effort.
        }
    }

    [SupportedOSPlatform("windows")]
    private async Task EnsureRuntimeInitializedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_coreWebView is not null && _controller is not null)
        {
            return;
        }

        await _runtimeGate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            if (_coreWebView is not null && _controller is not null)
            {
                return;
            }

            await WaitForAttachmentAsync(cancellationToken).ConfigureAwait(true);
            EnsurePreparedInitializationOptions();

            if (_environment is null)
            {
                _environment = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: NormalizePath(_preparedEnvironmentOptions!.BrowserExecutableFolder),
                    userDataFolder: NormalizePath(_preparedEnvironmentOptions.UserDataFolder),
                    options: CreateRuntimeEnvironmentOptions(_preparedEnvironmentOptions)).ConfigureAwait(true);
            }

            if (_childWindowHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException("Cannot initialize WebView2 without an attached child HWND.");
            }

            var controllerOptions = CreateRuntimeControllerOptions(_environment, _preparedControllerOptions);
            _controller = controllerOptions is null
                ? await _environment.CreateCoreWebView2ControllerAsync(_childWindowHandle).ConfigureAwait(true)
                : await _environment.CreateCoreWebView2ControllerAsync(_childWindowHandle, controllerOptions).ConfigureAwait(true);

            _coreWebView = _controller.CoreWebView2;
            CaptureRuntimeHandles();
            AttachRuntimeEvents();
            ApplyRuntimeSettings();
            UpdateControllerBounds();
            SyncNavigationSnapshotFromRuntime();

            if (_pendingNavigationUri is not null)
            {
                NavigateCore(_pendingNavigationUri);
            }

            _isRuntimeInitialized = true;
            RaiseInitializedIfNeeded(success: true, initializationException: null, nativeObject: _coreWebView);
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
        if (_childWindowHandle != IntPtr.Zero)
        {
            return;
        }

        if (!cancellationToken.CanBeCanceled)
        {
            await _attachmentTcs.Task.ConfigureAwait(true);
            return;
        }

        var cancellationSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(
            static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true),
            cancellationSource);

        var completed = await Task.WhenAny(_attachmentTcs.Task, cancellationSource.Task).ConfigureAwait(true);
        if (completed == cancellationSource.Task)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        await _attachmentTcs.Task.ConfigureAwait(true);
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

    [SupportedOSPlatformGuard("windows")]
    private bool ShouldUseRuntimePath()
    {
        return OperatingSystem.IsWindows() && _childWindowHandle != IntPtr.Zero;
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

    private void NavigateCore(Uri uri)
    {
        if (_coreWebView is null)
        {
            return;
        }

        _pendingNavigationUri = uri;
        var navigationTarget = uri.IsAbsoluteUri
            ? uri.AbsoluteUri
            : uri.ToString();

        if (!string.IsNullOrWhiteSpace(_headerString) &&
            _environment is not null &&
            (uri.IsAbsoluteUri && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)))
        {
            var request = _environment.CreateWebResourceRequest(
                navigationTarget,
                "GET",
                null,
                _headerString);
            _coreWebView.NavigateWithWebResourceRequest(request);
            return;
        }

        _coreWebView.Navigate(navigationTarget);
    }

    private void ApplyRuntimeSettings()
    {
        if (_coreWebView is null || _controller is null)
        {
            return;
        }

        var settings = _coreWebView.Settings;
        settings.AreDevToolsEnabled = _isDevToolsEnabled;
        settings.AreDefaultContextMenusEnabled = _isContextMenuEnabled;
        settings.IsStatusBarEnabled = _isStatusBarEnabled;
        settings.IsZoomControlEnabled = _isZoomControlEnabled;
        settings.IsWebMessageEnabled = true;

        if (_userAgentString is not null)
        {
            settings.UserAgent = _userAgentString;
        }

        if (_zoomFactor > 0)
        {
            _controller.ZoomFactor = _zoomFactor;
        }
    }

    private void AttachRuntimeEvents()
    {
        if (_coreWebView is null || _controller is null)
        {
            return;
        }

        _coreWebView.NavigationStarting += OnNavigationStarting;
        _coreWebView.NavigationCompleted += OnNavigationCompleted;
        _coreWebView.WebMessageReceived += OnWebMessageReceived;
        _coreWebView.HistoryChanged += OnHistoryChanged;
        _coreWebView.NewWindowRequested += OnNewWindowRequested;
        _coreWebView.ContextMenuRequested += OnContextMenuRequested;
        _coreWebView.WindowCloseRequested += OnWindowCloseRequested;
        _coreWebView.WebResourceRequested += OnWebResourceRequested;
        _coreWebView.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        _controller.ZoomFactorChanged += OnZoomFactorChanged;
    }

    private void DetachRuntimeEvents()
    {
        if (_coreWebView is not null)
        {
            _coreWebView.NavigationStarting -= OnNavigationStarting;
            _coreWebView.NavigationCompleted -= OnNavigationCompleted;
            _coreWebView.WebMessageReceived -= OnWebMessageReceived;
            _coreWebView.HistoryChanged -= OnHistoryChanged;
            _coreWebView.NewWindowRequested -= OnNewWindowRequested;
            _coreWebView.ContextMenuRequested -= OnContextMenuRequested;
            _coreWebView.WindowCloseRequested -= OnWindowCloseRequested;
            _coreWebView.WebResourceRequested -= OnWebResourceRequested;

            try
            {
                _coreWebView.RemoveWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            }
            catch
            {
                // Ignore teardown failures from optional filters.
            }
        }

        if (_controller is not null)
        {
            _controller.ZoomFactorChanged -= OnZoomFactorChanged;
        }
    }

    private void DestroyRuntimeController()
    {
        if (_controller is null && _coreWebView is null)
        {
            return;
        }

        DetachRuntimeEvents();
        ReleaseComHandle(ref _viewComHandle);
        ReleaseComHandle(ref _controllerComHandle);

        if (_controller is not null)
        {
            try
            {
                _controller.Close();
            }
            catch
            {
                // Best-effort shutdown.
            }
        }

        _controller = null;
        _coreWebView = null;
        _isRuntimeInitialized = false;
        UpdateHistorySnapshot(canGoBack: false, canGoForward: false);
    }

    [SupportedOSPlatform("windows")]
    private void CaptureRuntimeHandles()
    {
        ReleaseComHandle(ref _viewComHandle);
        ReleaseComHandle(ref _controllerComHandle);

        if (_coreWebView is not null)
        {
            _viewComHandle = Marshal.GetIUnknownForObject(_coreWebView);
        }

        if (_controller is not null)
        {
            _controllerComHandle = Marshal.GetIUnknownForObject(_controller);
        }
    }

    private void UpdateControllerBounds()
    {
        if (_controller is null || _childWindowHandle == IntPtr.Zero)
        {
            return;
        }

        if (!Win32.GetClientRect(_childWindowHandle, out var rect))
        {
            return;
        }

        var width = Math.Max(1, rect.Right - rect.Left);
        var height = Math.Max(1, rect.Bottom - rect.Top);
        _controller.Bounds = new Rectangle(0, 0, width, height);
    }

    private void SyncNavigationSnapshotFromRuntime()
    {
        if (_coreWebView is null)
        {
            return;
        }

        _currentUrl = TryCreateUri(_coreWebView.Source) ?? _currentUrl;
        _pendingNavigationUri = _currentUrl;
        UpdateHistorySnapshot(_coreWebView.CanGoBack, _coreWebView.CanGoForward);
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        var uri = TryCreateUri(e.Uri);
        var forwarded = new NativeWebViewNavigationStartedEventArgs(uri, e.IsRedirected);
        NavigationStarted?.Invoke(this, forwarded);
        e.Cancel = forwarded.Cancel;
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        SyncNavigationSnapshotFromRuntime();
        var uri = _currentUrl ?? TryCreateUri(_coreWebView?.Source);
        var statusCode = e.IsSuccess ? TryConvertHttpStatusCode(e.HttpStatusCode) : null;
        var error = e.IsSuccess ? null : e.WebErrorStatus.ToString();
        NavigationCompleted?.Invoke(this, new NativeWebViewNavigationCompletedEventArgs(uri, e.IsSuccess, statusCode, error));
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string? message = null;

        try
        {
            message = e.TryGetWebMessageAsString();
        }
        catch
        {
            // The payload was not a simple string message.
        }

        WebMessageReceived?.Invoke(this, new NativeWebViewMessageReceivedEventArgs(message, e.WebMessageAsJson));
    }

    private void OnHistoryChanged(object? sender, object e)
    {
        if (_coreWebView is null)
        {
            return;
        }

        UpdateHistorySnapshot(_coreWebView.CanGoBack, _coreWebView.CanGoForward);
    }

    private void OnZoomFactorChanged(object? sender, object e)
    {
        if (_controller is not null)
        {
            _zoomFactor = _controller.ZoomFactor;
        }
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        var forwarded = new NativeWebViewNewWindowRequestedEventArgs(TryCreateUri(e.Uri));
        NewWindowRequested?.Invoke(this, forwarded);
        e.Handled = forwarded.Handled;
    }

    private void OnContextMenuRequested(object? sender, CoreWebView2ContextMenuRequestedEventArgs e)
    {
        var forwarded = new NativeWebViewContextMenuRequestedEventArgs(e.Location.X, e.Location.Y);
        ContextMenuRequested?.Invoke(this, forwarded);
        e.Handled = forwarded.Handled;
    }

    private void OnWindowCloseRequested(object? sender, object e)
    {
        DestroyRequested?.Invoke(this, new NativeWebViewDestroyRequestedEventArgs("WindowCloseRequested"));
    }

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var forwarded = new NativeWebViewResourceRequestedEventArgs(
            TryCreateUri(e.Request.Uri),
            e.Request.Method);

        WebResourceRequested?.Invoke(this, forwarded);
        if (!forwarded.Handled || _environment is null)
        {
            return;
        }

        var responseBody = forwarded.ResponseBody ?? string.Empty;
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(responseBody));
        var headers = string.IsNullOrWhiteSpace(forwarded.ContentType)
            ? string.Empty
            : $"Content-Type: {forwarded.ContentType}";

        e.Response = _environment.CreateWebResourceResponse(
            stream,
            forwarded.StatusCode,
            "Handled",
            headers);
    }

    private CoreWebView2EnvironmentOptions CreateRuntimeEnvironmentOptions(NativeWebViewEnvironmentOptions options)
    {
        return new CoreWebView2EnvironmentOptions
        {
            AdditionalBrowserArguments = NativeWebViewWindowsProxyArgumentsBuilder.Merge(
                options.AdditionalBrowserArguments,
                options.Proxy),
            AllowSingleSignOnUsingOSPrimaryAccount = options.AllowSingleSignOnUsingOSPrimaryAccount,
            Language = options.Language,
            TargetCompatibleBrowserVersion = options.TargetCompatibleBrowserVersion,
        };
    }

    private static CoreWebView2ControllerOptions? CreateRuntimeControllerOptions(
        CoreWebView2Environment environment,
        NativeWebViewControllerOptions? options)
    {
        if (options is null)
        {
            return null;
        }

        var controllerOptions = environment.CreateCoreWebView2ControllerOptions();

        if (!string.IsNullOrWhiteSpace(options.ProfileName))
        {
            controllerOptions.ProfileName = options.ProfileName;
        }

        controllerOptions.IsInPrivateModeEnabled = options.IsInPrivateModeEnabled;

        if (options.ScriptLocale is not null)
        {
            controllerOptions.ScriptLocale = options.ScriptLocale;
        }

        return controllerOptions;
    }

    private CoreWebView2PrintSettings? CreatePrintSettings(NativeWebViewPrintSettings? settings)
    {
        if (_environment is null)
        {
            return null;
        }

        var printSettings = _environment.CreatePrintSettings();
        if (settings is null)
        {
            return printSettings;
        }

        printSettings.ShouldPrintBackgrounds = settings.BackgroundsEnabled;
        printSettings.Orientation = settings.Landscape
            ? CoreWebView2PrintOrientation.Landscape
            : CoreWebView2PrintOrientation.Portrait;
        return printSettings;
    }

    private static string? NormalizePath(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? null
            : Path.GetFullPath(path);
    }

    private static Uri? TryCreateUri(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Uri.TryCreate(value, UriKind.RelativeOrAbsolute, out var uri)
            ? uri
            : null;
    }

    private static int? TryConvertHttpStatusCode(long statusCode)
    {
        return statusCode is >= int.MinValue and <= int.MaxValue
            ? (int)statusCode
            : null;
    }

    private static void ReleaseComHandle(ref nint handle)
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }

        Marshal.Release(handle);
        handle = IntPtr.Zero;
    }

    private static void EnsureChildWindowClassRegistered()
    {
        if (_childWindowClassAtom != 0)
        {
            return;
        }

        lock (WindowClassGate)
        {
            if (_childWindowClassAtom != 0)
            {
                return;
            }

            var instanceHandle = Win32.GetModuleHandle(null);
            var windowClass = new Win32.WndClassEx
            {
                cbSize = (uint)Marshal.SizeOf<Win32.WndClassEx>(),
                style = 0,
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(ChildWindowProcDelegate),
                cbClsExtra = 0,
                cbWndExtra = 0,
                hInstance = instanceHandle,
                hIcon = IntPtr.Zero,
                hCursor = Win32.LoadCursor(IntPtr.Zero, Win32.CursorIdcArrow),
                hbrBackground = IntPtr.Zero,
                lpszMenuName = null,
                lpszClassName = Win32.ChildWindowClassName,
                hIconSm = IntPtr.Zero,
            };

            _childWindowClassAtom = Win32.RegisterClassEx(ref windowClass);
            if (_childWindowClassAtom == 0)
            {
                var error = Marshal.GetLastWin32Error();
                if (error != Win32.ErrorClassAlreadyExists)
                {
                    throw new InvalidOperationException(
                        $"Failed to register Windows child host window class. Win32 error: {error}.");
                }

                _childWindowClassAtom = ushort.MaxValue;
            }
        }
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

    private static class Win32
    {
        public const int CursorIdcArrow = 32512;
        public const int ErrorClassAlreadyExists = 1410;
        public const string ChildWindowClassName = "NativeWebView.WebView2HostWindow";

        internal static class WindowLongIndex
        {
            public const int GWLP_USERDATA = -21;
        }

        internal static class WindowStyles
        {
            public const uint WS_CHILD = 0x40000000;
            public const uint WS_VISIBLE = 0x10000000;
            public const uint WS_CLIPSIBLINGS = 0x04000000;
            public const uint WS_CLIPCHILDREN = 0x02000000;
        }

        internal static class WindowMessage
        {
            public const uint WM_SIZE = 0x0005;
            public const uint WM_SETFOCUS = 0x0007;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct WndClassEx
        {
            public uint cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string? lpszMenuName;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string? lpszClassName;
            public IntPtr hIconSm;
        }

        internal delegate IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern ushort RegisterClassEx([In] ref WndClassEx lpwcx);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr CreateWindowEx(
            uint dwExStyle,
            string lpClassName,
            string lpWindowName,
            uint dwStyle,
            int x,
            int y,
            int nWidth,
            int nHeight,
            IntPtr hWndParent,
            IntPtr hMenu,
            IntPtr hInstance,
            IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool GetClientRect(IntPtr hWnd, out Rect lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

        [DllImport("user32.dll", EntryPoint = "DefWindowProcW", SetLastError = true)]
        internal static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        internal static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        internal static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    }
}
