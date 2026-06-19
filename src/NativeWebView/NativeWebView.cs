using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using NativeWebView.Core;
using NativeWebView.Interop;

namespace NativeWebView.Controls;

public class NativeWebView : NativeControlHost, IDisposable
{
    private const int MinRenderFramesPerSecond = 1;
    private const int MaxRenderFramesPerSecond = 60;
    private const int DefaultRenderFramesPerSecond = 30;

    private static readonly SolidColorBrush RenderBackgroundBrush = new(Color.FromRgb(15, 23, 42));
    private static readonly SolidColorBrush RenderOutlineBrush = new(Color.FromArgb(180, 148, 163, 184));
    private static readonly SolidColorBrush RenderTextBrush = new(Color.FromRgb(226, 232, 240));
    private const string MacOsCompositedVideoPassthroughMessage =
        "Video host detected. Using native passthrough in composited mode to preserve hardware-accelerated playback.";
    private const string MacOsCompositedForcedPassthroughMessage =
        "Manual override active. Native passthrough is forced in composited mode.";
    private const string MacOsCompositedForcedDisabledMessage =
        "Manual override active. Native passthrough is disabled in composited mode.";
    private static readonly string[] MacOsCompositedPassthroughVideoHosts =
    [
        "youtube.com",
        "youtu.be",
        "vimeo.com",
        "twitch.tv",
        "dailymotion.com",
        "netflix.com",
    ];

    private readonly NativeWebViewInstance _instance;
    private readonly bool _ownsInstance;
    private readonly NativeWebViewController _controller;
    private readonly NativeWebViewRenderStatisticsTracker _renderStatisticsTracker = new();
    private static long s_nextPresenterId;

    private MacOSNativeWebViewHost? _macOSHost;
    private readonly long _presenterId = Interlocked.Increment(ref s_nextPresenterId);
    private bool _isDisposed;
    private DispatcherTimer? _framePump;
    private WriteableBitmap? _gpuSurfaceBitmap;
    private WriteableBitmap? _offscreenBitmap;
    private Vector _gpuSurfaceDpi = new(96, 96);

    private bool _isAttached;
    private bool _frameCaptureInProgress;
    private bool _isUsingSyntheticFrameSource;
    private bool _isMacOsCompositedPassthroughActive;
    private bool? _macOsCompositedPassthroughOverride;
    private string? _renderDiagnosticsMessage;
    private EventHandler<CoreWebViewInitializedEventArgs>? _coreWebView2Initialized;
    private EventHandler<NativeWebViewNavigationStartedEventArgs>? _navigationStarted;
    private EventHandler<NativeWebViewNavigationCompletedEventArgs>? _navigationCompleted;
    private EventHandler<NativeWebViewMessageReceivedEventArgs>? _webMessageReceived;
    private EventHandler<NativeWebViewOpenDevToolsRequestedEventArgs>? _openDevToolsRequested;
    private EventHandler<NativeWebViewDestroyRequestedEventArgs>? _destroyRequested;
    private EventHandler<NativeWebViewRequestCustomChromeEventArgs>? _requestCustomChrome;
    private EventHandler<NativeWebViewRequestParentWindowPositionEventArgs>? _requestParentWindowPosition;
    private EventHandler<NativeWebViewBeginMoveDragEventArgs>? _beginMoveDrag;
    private EventHandler<NativeWebViewBeginResizeDragEventArgs>? _beginResizeDrag;
    private EventHandler<NativeWebViewNewWindowRequestedEventArgs>? _newWindowRequested;
    private EventHandler<NativeWebViewResourceRequestedEventArgs>? _webResourceRequested;
    private EventHandler<NativeWebViewContextMenuRequestedEventArgs>? _contextMenuRequested;
    private EventHandler<NativeWebViewNavigationHistoryChangedEventArgs>? _navigationHistoryChanged;
    private EventHandler<CoreWebViewEnvironmentRequestedEventArgs>? _coreWebView2EnvironmentRequested;
    private EventHandler<CoreWebViewControllerOptionsRequestedEventArgs>? _coreWebView2ControllerOptionsRequested;
    private EventHandler<NativeWebViewFaviconChangedEventArgs>? _faviconChanged;

    public static readonly StyledProperty<NativeWebViewRenderMode> RenderModeProperty =
        AvaloniaProperty.Register<NativeWebView, NativeWebViewRenderMode>(nameof(RenderMode), NativeWebViewRenderMode.Embedded);

    public static readonly StyledProperty<int> RenderFramesPerSecondProperty =
        AvaloniaProperty.Register<NativeWebView, int>(nameof(RenderFramesPerSecond), DefaultRenderFramesPerSecond);

    public NativeWebView()
        : this(new NativeWebViewInstance(), ownsInstance: true)
    {
    }

    public NativeWebView(NativeWebViewInstanceConfiguration instanceConfiguration)
        : this(new NativeWebViewInstance(instanceConfiguration), ownsInstance: true)
    {
    }

    public NativeWebView(INativeWebViewBackend backend)
        : this(new NativeWebViewInstance(backend), ownsInstance: true)
    {
    }

    public NativeWebView(INativeWebViewBackend backend, NativeWebViewInstanceConfiguration? instanceConfiguration)
        : this(new NativeWebViewInstance(backend, instanceConfiguration), ownsInstance: true)
    {
    }

    public NativeWebView(NativeWebViewInstance instance)
        : this(instance, ownsInstance: false)
    {
    }

    private NativeWebView(NativeWebViewInstance instance, bool ownsInstance)
    {
        _instance = instance ?? throw new ArgumentNullException(nameof(instance));
        _ownsInstance = ownsInstance;
        _controller = _instance.Controller;
        _macOSHost = _instance.MacOSHost;
        _controller.CoreWebView2EnvironmentRequested += OnCoreWebView2EnvironmentRequestedInternal;
        _controller.CoreWebView2ControllerOptionsRequested += OnCoreWebView2ControllerOptionsRequestedInternal;
        AttachControllerEventForwarders();
        ApplyInstanceConfigurationToBackend();
    }

    public NativeWebViewPlatform Platform => _controller.Platform;

    public IWebViewPlatformFeatures Features => _controller.Features;

    public NativeWebComponentState LifecycleState => _controller.State;

    public NativeWebViewInstanceConfiguration InstanceConfiguration
    {
        get => _instance.InstanceConfiguration;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _instance.ApplyInstanceConfiguration(value);
            ApplyInstanceConfigurationToBackend();
        }
    }

    public NativeWebViewRenderMode RenderMode
    {
        get => GetValue(RenderModeProperty);
        set => SetValue(RenderModeProperty, value);
    }

    public int RenderFramesPerSecond
    {
        get => GetValue(RenderFramesPerSecondProperty);
        set => SetValue(RenderFramesPerSecondProperty, value);
    }

    public bool IsUsingSyntheticFrameSource => _isUsingSyntheticFrameSource;

    public string? RenderDiagnosticsMessage => _renderDiagnosticsMessage;

    public NativeWebViewRenderStatistics RenderStatistics => _renderStatisticsTracker.CreateSnapshot();

    public bool? MacOsCompositedPassthroughOverride => _macOsCompositedPassthroughOverride;

    public Uri? Source
    {
        get => _controller.CurrentUrl;
        set
        {
            if (value is not null)
            {
                Navigate(value);
            }
        }
    }

    public Uri? CurrentUrl => _controller.CurrentUrl;

    public new bool IsInitialized => _controller.IsInitialized;

    public bool CanGoBack => _controller.CanGoBack;

    public bool CanGoForward => _controller.CanGoForward;

    public bool IsDevToolsEnabled
    {
        get => _controller.IsDevToolsEnabled;
        set => _controller.IsDevToolsEnabled = value;
    }

    public bool IsContextMenuEnabled
    {
        get => _controller.IsContextMenuEnabled;
        set => _controller.IsContextMenuEnabled = value;
    }

    public bool IsStatusBarEnabled
    {
        get => _controller.IsStatusBarEnabled;
        set => _controller.IsStatusBarEnabled = value;
    }

    public bool IsZoomControlEnabled
    {
        get => _controller.IsZoomControlEnabled;
        set => _controller.IsZoomControlEnabled = value;
    }

    public double ZoomFactor => _controller.ZoomFactor;

    public string? HeaderString => _controller.HeaderString;

    public string? UserAgentString => _controller.UserAgentString;

    public event EventHandler<CoreWebViewInitializedEventArgs>? CoreWebView2Initialized
    {
        add => _coreWebView2Initialized += value;
        remove => _coreWebView2Initialized -= value;
    }

    public event EventHandler<NativeWebViewNavigationStartedEventArgs>? NavigationStarted
    {
        add => _navigationStarted += value;
        remove => _navigationStarted -= value;
    }

    public event EventHandler<NativeWebViewNavigationCompletedEventArgs>? NavigationCompleted
    {
        add => _navigationCompleted += value;
        remove => _navigationCompleted -= value;
    }

    public event EventHandler<NativeWebViewMessageReceivedEventArgs>? WebMessageReceived
    {
        add => _webMessageReceived += value;
        remove => _webMessageReceived -= value;
    }

    public event EventHandler<NativeWebViewOpenDevToolsRequestedEventArgs>? OpenDevToolsRequested
    {
        add => _openDevToolsRequested += value;
        remove => _openDevToolsRequested -= value;
    }

    public event EventHandler<NativeWebViewDestroyRequestedEventArgs>? DestroyRequested
    {
        add => _destroyRequested += value;
        remove => _destroyRequested -= value;
    }

    public event EventHandler<NativeWebViewRequestCustomChromeEventArgs>? RequestCustomChrome
    {
        add => _requestCustomChrome += value;
        remove => _requestCustomChrome -= value;
    }

    public event EventHandler<NativeWebViewRequestParentWindowPositionEventArgs>? RequestParentWindowPosition
    {
        add => _requestParentWindowPosition += value;
        remove => _requestParentWindowPosition -= value;
    }

    public event EventHandler<NativeWebViewBeginMoveDragEventArgs>? BeginMoveDrag
    {
        add => _beginMoveDrag += value;
        remove => _beginMoveDrag -= value;
    }

    public event EventHandler<NativeWebViewBeginResizeDragEventArgs>? BeginResizeDrag
    {
        add => _beginResizeDrag += value;
        remove => _beginResizeDrag -= value;
    }

    public event EventHandler<NativeWebViewNewWindowRequestedEventArgs>? NewWindowRequested
    {
        add => _newWindowRequested += value;
        remove => _newWindowRequested -= value;
    }

    public event EventHandler<NativeWebViewResourceRequestedEventArgs>? WebResourceRequested
    {
        add => _webResourceRequested += value;
        remove => _webResourceRequested -= value;
    }

    public event EventHandler<NativeWebViewContextMenuRequestedEventArgs>? ContextMenuRequested
    {
        add => _contextMenuRequested += value;
        remove => _contextMenuRequested -= value;
    }

    public event EventHandler<NativeWebViewNavigationHistoryChangedEventArgs>? NavigationHistoryChanged
    {
        add => _navigationHistoryChanged += value;
        remove => _navigationHistoryChanged -= value;
    }

    public event EventHandler<CoreWebViewEnvironmentRequestedEventArgs>? CoreWebView2EnvironmentRequested
    {
        add => _coreWebView2EnvironmentRequested += value;
        remove => _coreWebView2EnvironmentRequested -= value;
    }

    public event EventHandler<CoreWebViewControllerOptionsRequestedEventArgs>? CoreWebView2ControllerOptionsRequested
    {
        add => _coreWebView2ControllerOptionsRequested += value;
        remove => _coreWebView2ControllerOptionsRequested -= value;
    }

    public event EventHandler<NativeWebViewFaviconChangedEventArgs>? FaviconChanged
    {
        add => _faviconChanged += value;
        remove => _faviconChanged -= value;
    }

    public event EventHandler<NativeWebViewRenderFrameCapturedEventArgs>? RenderFrameCaptured;

    public bool SupportsRenderMode(NativeWebViewRenderMode renderMode)
    {
        if (renderMode == NativeWebViewRenderMode.Embedded)
        {
            return Features.Supports(NativeWebViewFeature.EmbeddedView);
        }

        if (renderMode == NativeWebViewRenderMode.GpuSurface &&
            !Features.Supports(NativeWebViewFeature.GpuSurfaceRendering))
        {
            return false;
        }

        if (renderMode == NativeWebViewRenderMode.Offscreen &&
            !Features.Supports(NativeWebViewFeature.OffscreenRendering))
        {
            return false;
        }

        if (_macOSHost is not null && _macOSHost.SupportsRenderMode(renderMode))
        {
            return true;
        }

        return _controller.TryGetBackend<INativeWebViewFrameSource>(out var frameSource) &&
               frameSource.SupportsRenderMode(renderMode);
    }

    public async Task<NativeWebViewRenderFrame?> CaptureRenderFrameAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Features.Supports(NativeWebViewFeature.RenderFrameCapture))
        {
            _renderDiagnosticsMessage =
                $"Frame capture is not supported on platform '{Platform}'.";
            _renderStatisticsTracker.MarkCaptureSkipped("Frame capture is not supported on this backend.");
            InvalidateVisual();
            return null;
        }

        return await CaptureAndRenderFrameAsync(cancellationToken).ConfigureAwait(true);
    }

    public NativeWebViewRenderStatistics GetRenderStatisticsSnapshot()
    {
        return _renderStatisticsTracker.CreateSnapshot();
    }

    public void ResetRenderStatistics()
    {
        _renderStatisticsTracker.Reset();
    }

    public void SetCompositedPassthroughOverride(bool? enabled)
    {
        _macOsCompositedPassthroughOverride = enabled;
        UpdateMacOsCompositedPassthroughPolicy();

        if (RenderMode != NativeWebViewRenderMode.Embedded)
        {
            _ = CaptureAndRenderFrameAsync();
        }
    }

    private void AttachControllerEventForwarders()
    {
        _controller.CoreWebView2Initialized += ForwardCoreWebView2Initialized;
        _controller.NavigationStarted += ForwardNavigationStarted;
        _controller.NavigationCompleted += ForwardNavigationCompleted;
        _controller.WebMessageReceived += ForwardWebMessageReceived;
        _controller.OpenDevToolsRequested += ForwardOpenDevToolsRequested;
        _controller.DestroyRequested += ForwardDestroyRequested;
        _controller.RequestCustomChrome += ForwardRequestCustomChrome;
        _controller.RequestParentWindowPosition += ForwardRequestParentWindowPosition;
        _controller.BeginMoveDrag += ForwardBeginMoveDrag;
        _controller.BeginResizeDrag += ForwardBeginResizeDrag;
        _controller.NewWindowRequested += ForwardNewWindowRequested;
        _controller.WebResourceRequested += ForwardWebResourceRequested;
        _controller.ContextMenuRequested += ForwardContextMenuRequested;
        _controller.NavigationHistoryChanged += ForwardNavigationHistoryChanged;
        _controller.CoreWebView2EnvironmentRequested += ForwardCoreWebView2EnvironmentRequested;
        _controller.CoreWebView2ControllerOptionsRequested += ForwardCoreWebView2ControllerOptionsRequested;
        _controller.FaviconChanged += ForwardFaviconChanged;
    }

    private void DetachControllerEventForwarders()
    {
        _controller.CoreWebView2Initialized -= ForwardCoreWebView2Initialized;
        _controller.NavigationStarted -= ForwardNavigationStarted;
        _controller.NavigationCompleted -= ForwardNavigationCompleted;
        _controller.WebMessageReceived -= ForwardWebMessageReceived;
        _controller.OpenDevToolsRequested -= ForwardOpenDevToolsRequested;
        _controller.DestroyRequested -= ForwardDestroyRequested;
        _controller.RequestCustomChrome -= ForwardRequestCustomChrome;
        _controller.RequestParentWindowPosition -= ForwardRequestParentWindowPosition;
        _controller.BeginMoveDrag -= ForwardBeginMoveDrag;
        _controller.BeginResizeDrag -= ForwardBeginResizeDrag;
        _controller.NewWindowRequested -= ForwardNewWindowRequested;
        _controller.WebResourceRequested -= ForwardWebResourceRequested;
        _controller.ContextMenuRequested -= ForwardContextMenuRequested;
        _controller.NavigationHistoryChanged -= ForwardNavigationHistoryChanged;
        _controller.CoreWebView2EnvironmentRequested -= ForwardCoreWebView2EnvironmentRequested;
        _controller.CoreWebView2ControllerOptionsRequested -= ForwardCoreWebView2ControllerOptionsRequested;
        _controller.FaviconChanged -= ForwardFaviconChanged;
    }

    private void ForwardCoreWebView2Initialized(object? sender, CoreWebViewInitializedEventArgs e) =>
        _coreWebView2Initialized?.Invoke(sender, e);

    private void ForwardNavigationStarted(object? sender, NativeWebViewNavigationStartedEventArgs e) =>
        _navigationStarted?.Invoke(sender, e);

    private void ForwardNavigationCompleted(object? sender, NativeWebViewNavigationCompletedEventArgs e) =>
        _navigationCompleted?.Invoke(sender, e);

    private void ForwardWebMessageReceived(object? sender, NativeWebViewMessageReceivedEventArgs e) =>
        _webMessageReceived?.Invoke(sender, e);

    private void ForwardOpenDevToolsRequested(object? sender, NativeWebViewOpenDevToolsRequestedEventArgs e) =>
        _openDevToolsRequested?.Invoke(sender, e);

    private void ForwardDestroyRequested(object? sender, NativeWebViewDestroyRequestedEventArgs e) =>
        _destroyRequested?.Invoke(sender, e);

    private void ForwardRequestCustomChrome(object? sender, NativeWebViewRequestCustomChromeEventArgs e) =>
        _requestCustomChrome?.Invoke(sender, e);

    private void ForwardRequestParentWindowPosition(object? sender, NativeWebViewRequestParentWindowPositionEventArgs e) =>
        _requestParentWindowPosition?.Invoke(sender, e);

    private void ForwardBeginMoveDrag(object? sender, NativeWebViewBeginMoveDragEventArgs e) =>
        _beginMoveDrag?.Invoke(sender, e);

    private void ForwardBeginResizeDrag(object? sender, NativeWebViewBeginResizeDragEventArgs e) =>
        _beginResizeDrag?.Invoke(sender, e);

    private void ForwardNewWindowRequested(object? sender, NativeWebViewNewWindowRequestedEventArgs e) =>
        _newWindowRequested?.Invoke(sender, e);

    private void ForwardWebResourceRequested(object? sender, NativeWebViewResourceRequestedEventArgs e) =>
        _webResourceRequested?.Invoke(sender, e);

    private void ForwardContextMenuRequested(object? sender, NativeWebViewContextMenuRequestedEventArgs e) =>
        _contextMenuRequested?.Invoke(sender, e);

    private void ForwardNavigationHistoryChanged(object? sender, NativeWebViewNavigationHistoryChangedEventArgs e) =>
        _navigationHistoryChanged?.Invoke(sender, e);

    private void ForwardCoreWebView2EnvironmentRequested(object? sender, CoreWebViewEnvironmentRequestedEventArgs e) =>
        _coreWebView2EnvironmentRequested?.Invoke(sender, e);

    private void ForwardCoreWebView2ControllerOptionsRequested(object? sender, CoreWebViewControllerOptionsRequestedEventArgs e) =>
        _coreWebView2ControllerOptionsRequested?.Invoke(sender, e);

    private void ForwardFaviconChanged(object? sender, NativeWebViewFaviconChangedEventArgs e) =>
        _faviconChanged?.Invoke(sender, e);

    public async Task<bool> SaveRenderFrameAsync(string outputPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        cancellationToken.ThrowIfCancellationRequested();

        var frame = await CaptureRenderFrameAsync(cancellationToken).ConfigureAwait(true);
        if (frame is null)
        {
            return false;
        }

        try
        {
            await SaveFramePngAsync(frame, outputPath, cancellationToken).ConfigureAwait(true);
            _renderDiagnosticsMessage = null;
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _renderDiagnosticsMessage = $"Failed to save render frame: {ex.GetType().Name}: {ex.Message}";
            InvalidateVisual();
            return false;
        }
    }

    public async Task<bool> SaveRenderFrameWithMetadataAsync(
        string outputPath,
        string? metadataOutputPath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        cancellationToken.ThrowIfCancellationRequested();

        var frame = await CaptureRenderFrameAsync(cancellationToken).ConfigureAwait(true);
        if (frame is null)
        {
            return false;
        }

        // Snapshot render state immediately after capture so sidecar metadata
        // stays consistent with the frame being exported.
        var statisticsSnapshot = _renderStatisticsTracker.CreateSnapshot();
        var platform = Platform;
        var renderMode = RenderMode;
        var renderFramesPerSecond = NormalizeRenderFramesPerSecond(RenderFramesPerSecond);
        var isUsingSyntheticFrameSource = _isUsingSyntheticFrameSource;
        var renderDiagnosticsMessage = _renderDiagnosticsMessage;
        var currentUrl = CurrentUrl;

        try
        {
            await SaveFramePngAsync(frame, outputPath, cancellationToken).ConfigureAwait(true);

            var metadataPath = string.IsNullOrWhiteSpace(metadataOutputPath)
                ? $"{Path.GetFullPath(outputPath)}.json"
                : metadataOutputPath;

            var metadata = NativeWebViewRenderFrameMetadataSerializer.Create(
                frame,
                statisticsSnapshot,
                platform,
                renderMode,
                renderFramesPerSecond,
                isUsingSyntheticFrameSource,
                renderDiagnosticsMessage,
                currentUrl);

            await NativeWebViewRenderFrameMetadataSerializer
                .WriteToFileAsync(metadata, metadataPath, cancellationToken)
                .ConfigureAwait(true);

            _renderDiagnosticsMessage = null;
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _renderDiagnosticsMessage = $"Failed to save render frame with metadata: {ex.GetType().Name}: {ex.Message}";
            InvalidateVisual();
            return false;
        }
    }

    public ValueTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        return _controller.InitializeAsync(cancellationToken);
    }

    public void Navigate(string url)
    {
        if (_macOSHost is not null &&
            Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var parsedUri) &&
            parsedUri.IsAbsoluteUri)
        {
            _macOSHost.Navigate(parsedUri);
        }

        _controller.Navigate(url);
        UpdateMacOsCompositedPassthroughPolicy();
    }

    public void Navigate(Uri uri)
    {
        if (_macOSHost is not null && uri.IsAbsoluteUri)
        {
            _macOSHost.Navigate(uri);
        }

        _controller.Navigate(uri);
        UpdateMacOsCompositedPassthroughPolicy();
    }

    public void Reload()
    {
        _macOSHost?.Reload();
        _controller.Reload();
        UpdateMacOsCompositedPassthroughPolicy();
    }

    public void Stop()
    {
        _macOSHost?.Stop();
        _controller.Stop();
    }

    public void GoBack()
    {
        _macOSHost?.GoBack();
        _controller.GoBack();
        UpdateMacOsCompositedPassthroughPolicy();
    }

    public void GoForward()
    {
        _macOSHost?.GoForward();
        _controller.GoForward();
        UpdateMacOsCompositedPassthroughPolicy();
    }

    public Task<string?> ExecuteScriptAsync(string script, CancellationToken cancellationToken = default)
    {
        return _controller.ExecuteScriptAsync(script, cancellationToken);
    }

    public Task PostWebMessageAsJsonAsync(string message, CancellationToken cancellationToken = default)
    {
        return _controller.PostWebMessageAsJsonAsync(message, cancellationToken);
    }

    public Task PostWebMessageAsStringAsync(string message, CancellationToken cancellationToken = default)
    {
        return _controller.PostWebMessageAsStringAsync(message, cancellationToken);
    }

    public void OpenDevToolsWindow()
    {
        _controller.OpenDevToolsWindow();
    }

    public Task<NativeWebViewPrintResult> PrintAsync(NativeWebViewPrintSettings? settings = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_macOSHost is not null && OperatingSystem.IsMacOS())
        {
            return Task.FromResult(_macOSHost.Print(settings));
        }

        return _controller.PrintAsync(settings, cancellationToken);
    }

    public Task<bool> ShowPrintUiAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_macOSHost is not null && OperatingSystem.IsMacOS())
        {
            return Task.FromResult(_macOSHost.ShowPrintUi());
        }

        return _controller.ShowPrintUiAsync(cancellationToken);
    }

    public Task<NativeWebViewFavicon?> GetFaviconAsync(
        NativeWebViewFaviconFormat format = NativeWebViewFaviconFormat.Original,
        CancellationToken cancellationToken = default)
    {
        return _controller.GetFaviconAsync(format, cancellationToken);
    }

    public void SetZoomFactor(double zoomFactor)
    {
        _macOSHost?.SetZoomFactor(zoomFactor);
        _controller.SetZoomFactor(zoomFactor);
    }

    public void SetUserAgent(string? userAgent)
    {
        _macOSHost?.SetUserAgent(userAgent);
        _controller.SetUserAgent(userAgent);
    }

    public void SetHeader(string? header)
    {
        _controller.SetHeader(header);
    }

    public bool TryGetCommandManager(out INativeWebViewCommandManager? commandManager)
    {
        return _controller.TryGetCommandManager(out commandManager);
    }

    public bool TryGetCookieManager(out INativeWebViewCookieManager? cookieManager)
    {
        return _controller.TryGetCookieManager(out cookieManager);
    }

    public bool TryGetPlatformHandle(out NativePlatformHandle handle)
    {
        if (_macOSHost is not null && _macOSHost.ViewHandle != IntPtr.Zero)
        {
            handle = new NativePlatformHandle(_macOSHost.ViewHandle, "NSView");
            return true;
        }

        if (_controller.TryGetBackend<INativeWebViewPlatformHandleProvider>(out var provider) &&
            provider.TryGetPlatformHandle(out handle))
        {
            return true;
        }

        handle = default;
        return false;
    }

    public bool TryGetViewHandle(out NativePlatformHandle handle)
    {
        if (_macOSHost is not null && _macOSHost.ViewHandle != IntPtr.Zero)
        {
            handle = new NativePlatformHandle(_macOSHost.ViewHandle, "WKWebView");
            return true;
        }

        if (_controller.TryGetBackend<INativeWebViewPlatformHandleProvider>(out var provider) &&
            provider.TryGetViewHandle(out handle))
        {
            return true;
        }

        handle = default;
        return false;
    }

    public bool TryGetControllerHandle(out NativePlatformHandle handle)
    {
        if (_macOSHost is not null && _macOSHost.ConfigurationHandle != IntPtr.Zero)
        {
            handle = new NativePlatformHandle(_macOSHost.ConfigurationHandle, "WKWebViewConfiguration");
            return true;
        }

        if (_controller.TryGetBackend<INativeWebViewPlatformHandleProvider>(out var provider) &&
            provider.TryGetControllerHandle(out handle))
        {
            return true;
        }

        handle = default;
        return false;
    }

    public void MoveFocus(NativeWebViewFocusMoveDirection direction)
    {
        _controller.MoveFocus(direction);
    }

    public override void Render(DrawingContext context)
    {
        if (RenderMode == NativeWebViewRenderMode.Embedded)
        {
            base.Render(context);
            return;
        }

        var destinationRect = new Rect(Bounds.Size);
        var surface = RenderMode == NativeWebViewRenderMode.GpuSurface
            ? _gpuSurfaceBitmap ?? _offscreenBitmap
            : _offscreenBitmap ?? _gpuSurfaceBitmap;

        if (surface is not null)
        {
            var sourceRect = new Rect(surface.Size);
            context.DrawImage(surface, sourceRect, destinationRect);
            return;
        }

        if (!string.IsNullOrWhiteSpace(_renderDiagnosticsMessage))
        {
            DrawRenderFallback(context, destinationRect);
        }
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (_instance.IsDisposed)
        {
            return base.CreateNativeControlCore(parent);
        }

        _instance.ActivePresenterId = _presenterId;

        if (_controller.Platform == NativeWebViewPlatform.MacOS && OperatingSystem.IsMacOS())
        {
            if (_macOSHost is not null)
            {
                _macOSHost.AttachToParent(parent);
                ApplyRenderModeToNativeHost();
                return _macOSHost.PlatformHandle;
            }

            _macOSHost = new MacOSNativeWebViewHost(parent, _instance.InstanceConfiguration);
            _instance.MacOSHost = _macOSHost;

            _macOSHost.SetUserAgent(_controller.UserAgentString);

            if (_controller.ZoomFactor > 0)
            {
                _macOSHost.SetZoomFactor(_controller.ZoomFactor);
            }

            if (_controller.CurrentUrl is { } currentUrl && currentUrl.IsAbsoluteUri)
            {
                _macOSHost.Navigate(currentUrl);
            }

            ApplyRenderModeToNativeHost();
            return _macOSHost.PlatformHandle;
        }

        if (OperatingSystem.IsBrowser() &&
            _controller.TryGetBackend<INativeWebViewManagedControlHandleProvider>(out var managedControlHandleProvider))
        {
            var managedControlHandle = managedControlHandleProvider.CreateManagedControlHandle();
            if (managedControlHandle is not IPlatformHandle platformHandle)
            {
                throw new InvalidOperationException(
                    $"Browser managed control handle provider returned '{managedControlHandle.GetType().FullName ?? "<null>"}' instead of an Avalonia platform handle.");
            }

            return platformHandle;
        }

        if (TryGetNativeControlAttachment(out var nativeControlAttachment, out var defaultParentDescriptor))
        {
            var handle = nativeControlAttachment.AttachToNativeParent(
                new NativePlatformHandle(parent.Handle, parent.HandleDescriptor ?? defaultParentDescriptor));
            return new PlatformHandle(handle.Handle, handle.HandleDescriptor);
        }

        return base.CreateNativeControlCore(parent);
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        if (_instance.IsDisposed)
        {
            return;
        }

        if (_instance.ActivePresenterId != _presenterId)
        {
            return;
        }

        if (_macOSHost is not null)
        {
            _macOSHost.DetachFromParent(preserveRuntime: true);

            if (_instance.ActivePresenterId == _presenterId)
            {
                _instance.ActivePresenterId = 0;
            }

            return;
        }

        if (OperatingSystem.IsBrowser() &&
            _controller.TryGetBackend<INativeWebViewManagedControlHandleProvider>(out var managedControlHandleProvider))
        {
            managedControlHandleProvider.ReleaseManagedControlHandle(control);
            base.DestroyNativeControlCore(control);
            if (_instance.ActivePresenterId == _presenterId)
            {
                _instance.ActivePresenterId = 0;
            }

            return;
        }

        if (TryGetNativeControlAttachment(out var nativeControlAttachment, out _))
        {
            nativeControlAttachment.DetachFromNativeParent(preserveRuntime: true);
            if (_instance.ActivePresenterId == _presenterId)
            {
                _instance.ActivePresenterId = 0;
            }

            return;
        }

        base.DestroyNativeControlCore(control);
        if (_instance.ActivePresenterId == _presenterId)
        {
            _instance.ActivePresenterId = 0;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        ApplyRenderModeState(forceRefresh: true);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _isAttached = false;
        StopFramePump();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == RenderModeProperty)
        {
            ApplyRenderModeState(forceRefresh: true);
            return;
        }

        if (change.Property == RenderFramesPerSecondProperty)
        {
            ApplyRenderModeState(forceRefresh: false);
            return;
        }

        if (change.Property == BoundsProperty && RenderMode != NativeWebViewRenderMode.Embedded)
        {
            _macOSHost?.UpdateLayoutForCurrentMode();
            SyncNativeHostCaptureSize();
            _ = CaptureAndRenderFrameAsync();
            return;
        }

        if (change.Property == BoundsProperty)
        {
            _macOSHost?.UpdateLayoutForCurrentMode();
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        StopFramePump();
        DisposeRenderSurfaces();

        DetachControllerEventForwarders();
        _controller.CoreWebView2EnvironmentRequested -= OnCoreWebView2EnvironmentRequestedInternal;
        _controller.CoreWebView2ControllerOptionsRequested -= OnCoreWebView2ControllerOptionsRequestedInternal;

        if (_ownsInstance)
        {
            _instance.Dispose();
        }

        _macOSHost = null;
    }

    private void ApplyInstanceConfigurationToBackend()
    {
        if (_controller.TryGetBackend<INativeWebViewInstanceConfigurationTarget>(out var target))
        {
            target.ApplyInstanceConfiguration(_instance.InstanceConfiguration.Clone());
        }
    }

    private bool TryGetNativeControlAttachment(
        out INativeWebViewNativeControlAttachment nativeControlAttachment,
        out string defaultParentDescriptor)
    {
        nativeControlAttachment = default!;
        defaultParentDescriptor = string.Empty;

        if (_controller.Platform == NativeWebViewPlatform.Windows &&
            OperatingSystem.IsWindows() &&
            _controller.TryGetBackend<INativeWebViewNativeControlAttachment>(out var windowsAttachment))
        {
            nativeControlAttachment = windowsAttachment;
            defaultParentDescriptor = "HWND";
            return true;
        }

        if (_controller.Platform == NativeWebViewPlatform.Linux &&
            OperatingSystem.IsLinux() &&
            _controller.TryGetBackend<INativeWebViewNativeControlAttachment>(out var linuxAttachment))
        {
            nativeControlAttachment = linuxAttachment;
            defaultParentDescriptor = "XID";
            return true;
        }

        if (_controller.Platform == NativeWebViewPlatform.IOS &&
            OperatingSystem.IsIOS() &&
            _controller.TryGetBackend<INativeWebViewNativeControlAttachment>(out var iosAttachment))
        {
            nativeControlAttachment = iosAttachment;
            defaultParentDescriptor = "UIView";
            return true;
        }

        if (_controller.Platform == NativeWebViewPlatform.Android &&
            OperatingSystem.IsAndroid() &&
            _controller.TryGetBackend<INativeWebViewNativeControlAttachment>(out var androidAttachment))
        {
            nativeControlAttachment = androidAttachment;
            defaultParentDescriptor = "android.view.View";
            return true;
        }

        return false;
    }

    private void OnCoreWebView2EnvironmentRequestedInternal(object? sender, CoreWebViewEnvironmentRequestedEventArgs e)
    {
        _instance.InstanceConfiguration.ApplyEnvironmentOptions(e.Options);
    }

    private void OnCoreWebView2ControllerOptionsRequestedInternal(object? sender, CoreWebViewControllerOptionsRequestedEventArgs e)
    {
        _instance.InstanceConfiguration.ApplyControllerOptions(e.Options);
    }

    private void ApplyRenderModeState(bool forceRefresh)
    {
        ApplyRenderModeToNativeHost();
        UpdateMacOsCompositedPassthroughPolicy();
        _macOSHost?.UpdateLayoutForCurrentMode();

        if (RenderMode == NativeWebViewRenderMode.Embedded)
        {
            StopFramePump();
            DisposeRenderSurfaces();
            if (!IsPassthroughDiagnosticsMessage(_renderDiagnosticsMessage))
            {
                _renderDiagnosticsMessage = null;
            }
            _isUsingSyntheticFrameSource = false;

            if (forceRefresh)
            {
                InvalidateVisual();
            }

            return;
        }

        SyncNativeHostCaptureSize();

        if (_isAttached)
        {
            EnsureFramePump();
        }

        if (forceRefresh)
        {
            _ = CaptureAndRenderFrameAsync();
        }
    }

    private void EnsureFramePump()
    {
        _framePump ??= new DispatcherTimer();
        _framePump.Interval = TimeSpan.FromMilliseconds(1000.0 / NormalizeRenderFramesPerSecond(RenderFramesPerSecond));

        if (!_framePump.IsEnabled)
        {
            _framePump.Tick += FramePumpOnTick;
            _framePump.Start();
        }
    }

    private void StopFramePump()
    {
        if (_framePump is null)
        {
            return;
        }

        if (_framePump.IsEnabled)
        {
            _framePump.Stop();
            _framePump.Tick -= FramePumpOnTick;
        }
    }

    private void FramePumpOnTick(object? sender, EventArgs e)
    {
        _ = CaptureAndRenderFrameAsync();
    }

    private async Task<NativeWebViewRenderFrame?> CaptureAndRenderFrameAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_frameCaptureInProgress)
        {
            _renderStatisticsTracker.MarkCaptureSkipped("Frame capture skipped because another capture is already in progress.");
            return null;
        }

        if (RenderMode == NativeWebViewRenderMode.Embedded)
        {
            _renderStatisticsTracker.MarkCaptureSkipped("Frame capture skipped because RenderMode is Embedded.");
            return null;
        }

        if (!TryCreateFrameRequest(out var frameRequest))
        {
            _renderStatisticsTracker.MarkCaptureSkipped("Frame capture skipped because control bounds are not ready.");
            return null;
        }

        _frameCaptureInProgress = true;
        _renderStatisticsTracker.MarkCaptureAttempt();

        try
        {
            var frame = await CaptureFrameCoreAsync(RenderMode, frameRequest, cancellationToken).ConfigureAwait(true);
            if (frame is null)
            {
                _renderDiagnosticsMessage =
                    $"Frame source is unavailable for render mode '{RenderMode}' on platform '{Platform}'.";
                _renderStatisticsTracker.MarkCaptureFailure(_renderDiagnosticsMessage);
                InvalidateVisual();
                return null;
            }

            _isUsingSyntheticFrameSource = frame.IsSynthetic;
            _renderDiagnosticsMessage = null;

            UpdateCapturedRenderSurface(frame);
            _renderStatisticsTracker.MarkCaptureSuccess(frame);
            RaiseRenderFrameCaptured(frame);
            InvalidateVisual();
            return frame;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _renderStatisticsTracker.MarkCaptureSkipped("Frame capture canceled.");
            throw;
        }
        catch (Exception ex)
        {
            _renderDiagnosticsMessage = $"Frame capture failed: {ex.GetType().Name}: {ex.Message}";
            _renderStatisticsTracker.MarkCaptureFailure(_renderDiagnosticsMessage);
            InvalidateVisual();
            return null;
        }
        finally
        {
            _frameCaptureInProgress = false;
        }
    }

    private void UpdateCapturedRenderSurface(NativeWebViewRenderFrame frame)
    {
        if (frame.IsSynthetic && HasRetainedCompositedFrame(RenderMode))
        {
            return;
        }

        if (RenderMode == NativeWebViewRenderMode.GpuSurface)
        {
            UpdateGpuSurfaceFrame(frame);
            if (!frame.IsSynthetic)
            {
                DisposeOffscreenSurface();
            }
        }
        else
        {
            UpdateOffscreenFrame(frame);
            if (!frame.IsSynthetic)
            {
                DisposeGpuSurface();
            }
        }
    }

    private bool HasRetainedCompositedFrame(NativeWebViewRenderMode renderMode)
    {
        return renderMode == NativeWebViewRenderMode.GpuSurface
            ? _gpuSurfaceBitmap is not null || _offscreenBitmap is not null
            : _offscreenBitmap is not null || _gpuSurfaceBitmap is not null;
    }

    private async Task<NativeWebViewRenderFrame?> CaptureFrameCoreAsync(
        NativeWebViewRenderMode renderMode,
        NativeWebViewRenderFrameRequest frameRequest,
        CancellationToken cancellationToken)
    {
        if (_macOSHost is not null &&
            _macOSHost.TryCaptureFrame(renderMode, frameRequest.PixelWidth, frameRequest.PixelHeight, out var hostFrame))
        {
            return hostFrame;
        }

        if (_controller.TryGetBackend<INativeWebViewFrameSource>(out var frameSource) &&
            frameSource.SupportsRenderMode(renderMode))
        {
            return await frameSource.CaptureFrameAsync(renderMode, frameRequest, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private void UpdateGpuSurfaceFrame(NativeWebViewRenderFrame frame)
    {
        if (frame.PixelFormat != NativeWebViewRenderPixelFormat.Bgra8888Premultiplied)
        {
            throw new NotSupportedException($"Unsupported frame pixel format '{frame.PixelFormat}'.");
        }

        var frameDpi = ResolveFrameDpi(frame.PixelWidth, frame.PixelHeight);

        if (_gpuSurfaceBitmap is null ||
            _gpuSurfaceBitmap.PixelSize.Width != frame.PixelWidth ||
            _gpuSurfaceBitmap.PixelSize.Height != frame.PixelHeight ||
            !AreClose(_gpuSurfaceDpi, frameDpi))
        {
            DisposeGpuSurface();
            _gpuSurfaceBitmap = new WriteableBitmap(
                new PixelSize(frame.PixelWidth, frame.PixelHeight),
                frameDpi,
                PixelFormat.Bgra8888,
                AlphaFormat.Premul);
            _gpuSurfaceDpi = frameDpi;
        }

        CopyFramePixels(frame, _gpuSurfaceBitmap);
    }

    private void UpdateOffscreenFrame(NativeWebViewRenderFrame frame)
    {
        if (frame.PixelFormat != NativeWebViewRenderPixelFormat.Bgra8888Premultiplied)
        {
            throw new NotSupportedException($"Unsupported frame pixel format '{frame.PixelFormat}'.");
        }

        var frameDpi = ResolveFrameDpi(frame.PixelWidth, frame.PixelHeight);
        var offscreen = new WriteableBitmap(
            new PixelSize(frame.PixelWidth, frame.PixelHeight),
            frameDpi,
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        CopyFramePixels(frame, offscreen);

        _offscreenBitmap?.Dispose();
        _offscreenBitmap = offscreen;
    }

    private static void CopyFramePixels(NativeWebViewRenderFrame frame, WriteableBitmap bitmap)
    {
        using var framebuffer = bitmap.Lock();

        var copyRows = Math.Min(frame.PixelHeight, framebuffer.Size.Height);
        var rowCopyBytes = Math.Min(frame.BytesPerRow, framebuffer.RowBytes);

        for (var row = 0; row < copyRows; row++)
        {
            var sourceOffset = row * frame.BytesPerRow;
            var destination = IntPtr.Add(framebuffer.Address, row * framebuffer.RowBytes);
            Marshal.Copy(frame.PixelData, sourceOffset, destination, rowCopyBytes);
        }
    }

    private static async Task SaveFramePngAsync(
        NativeWebViewRenderFrame frame,
        string outputPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var bitmap = new WriteableBitmap(
            new PixelSize(frame.PixelWidth, frame.PixelHeight),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        CopyFramePixels(frame, bitmap);

        await using var stream = File.Create(fullPath);
        bitmap.Save(stream);
    }

    private void RaiseRenderFrameCaptured(NativeWebViewRenderFrame frame)
    {
        var handlers = RenderFrameCaptured;
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                ((EventHandler<NativeWebViewRenderFrameCapturedEventArgs>)handler).Invoke(
                    this,
                    new NativeWebViewRenderFrameCapturedEventArgs(frame));
            }
            catch
            {
                // ignored
            }
        }
    }

    private void DrawRenderFallback(DrawingContext context, Rect destinationRect)
    {
        context.FillRectangle(RenderBackgroundBrush, destinationRect);
        context.DrawRectangle(null, new Pen(RenderOutlineBrush, 1), destinationRect.Deflate(0.5));

        var status = _renderDiagnosticsMessage ??
                     $"{RenderMode} active. Waiting for first rendered web frame.";
        var formattedText = new FormattedText(
            status,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            13,
            RenderTextBrush);

        context.DrawText(formattedText, new Point(12, 12));
    }

    private void DisposeRenderSurfaces()
    {
        DisposeGpuSurface();
        DisposeOffscreenSurface();
    }

    private void DisposeGpuSurface()
    {
        _gpuSurfaceBitmap?.Dispose();
        _gpuSurfaceBitmap = null;
        _gpuSurfaceDpi = new Vector(96, 96);
    }

    private void DisposeOffscreenSurface()
    {
        _offscreenBitmap?.Dispose();
        _offscreenBitmap = null;
    }

    private bool TryCreateFrameRequest(out NativeWebViewRenderFrameRequest request)
    {
        request = new NativeWebViewRenderFrameRequest();

        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return false;
        }

        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1d;
        request.PixelWidth = Math.Max(1, (int)Math.Ceiling(size.Width * scale));
        request.PixelHeight = Math.Max(1, (int)Math.Ceiling(size.Height * scale));
        return true;
    }

    private static int NormalizeRenderFramesPerSecond(int value)
    {
        if (value < MinRenderFramesPerSecond)
        {
            return MinRenderFramesPerSecond;
        }

        if (value > MaxRenderFramesPerSecond)
        {
            return MaxRenderFramesPerSecond;
        }

        return value;
    }

    private void ApplyRenderModeToNativeHost()
    {
        if (_macOSHost is null)
        {
            return;
        }

        _macOSHost.SetRenderMode(RenderMode);
    }

    private void SyncNativeHostCaptureSize()
    {
        if (_macOSHost is null)
        {
            return;
        }

        if (!TryCreateFrameRequest(out var request))
        {
            return;
        }

        _macOSHost.SetCaptureSize(request.PixelWidth, request.PixelHeight);
    }

    private Vector ResolveFrameDpi(int pixelWidth, int pixelHeight)
    {
        if (TryCreateFrameRequest(out var request) &&
            request.PixelWidth > 0 &&
            request.PixelHeight > 0)
        {
            var dpiX = 96d * pixelWidth / request.PixelWidth;
            var dpiY = 96d * pixelHeight / request.PixelHeight;
            if (double.IsFinite(dpiX) && double.IsFinite(dpiY) && dpiX > 0 && dpiY > 0)
            {
                return new Vector(dpiX, dpiY);
            }
        }

        return new Vector(96d, 96d);
    }

    private static bool AreClose(Vector left, Vector right)
    {
        const double epsilon = 0.01;
        return Math.Abs(left.X - right.X) < epsilon && Math.Abs(left.Y - right.Y) < epsilon;
    }

    private void UpdateMacOsCompositedPassthroughPolicy()
    {
        if (_macOSHost is null || !OperatingSystem.IsMacOS())
        {
            _isMacOsCompositedPassthroughActive = false;
            return;
        }

        var shouldEnable = ResolveMacOsCompositedPassthroughEnabled();

        _macOSHost.SetCompositedPassthrough(shouldEnable);

        if (_isMacOsCompositedPassthroughActive == shouldEnable)
        {
            return;
        }

        _isMacOsCompositedPassthroughActive = shouldEnable;
        var passthroughDiagnostics = ResolvePassthroughDiagnosticsMessage(shouldEnable);
        if (!string.IsNullOrWhiteSpace(passthroughDiagnostics))
        {
            _renderDiagnosticsMessage = passthroughDiagnostics;
        }
        else if (IsPassthroughDiagnosticsMessage(_renderDiagnosticsMessage))
        {
            _renderDiagnosticsMessage = null;
        }
    }

    private bool ResolveMacOsCompositedPassthroughEnabled()
    {
        if (RenderMode == NativeWebViewRenderMode.Embedded)
        {
            return false;
        }

        if (_macOsCompositedPassthroughOverride.HasValue)
        {
            return _macOsCompositedPassthroughOverride.Value;
        }

        return IsKnownVideoHost(CurrentUrl);
    }

    private string? ResolvePassthroughDiagnosticsMessage(bool passthroughEnabled)
    {
        if (_macOsCompositedPassthroughOverride.HasValue)
        {
            return passthroughEnabled
                ? MacOsCompositedForcedPassthroughMessage
                : MacOsCompositedForcedDisabledMessage;
        }

        return passthroughEnabled
            ? MacOsCompositedVideoPassthroughMessage
            : null;
    }

    private static bool IsPassthroughDiagnosticsMessage(string? message)
    {
        return string.Equals(message, MacOsCompositedVideoPassthroughMessage, StringComparison.Ordinal) ||
               string.Equals(message, MacOsCompositedForcedPassthroughMessage, StringComparison.Ordinal) ||
               string.Equals(message, MacOsCompositedForcedDisabledMessage, StringComparison.Ordinal);
    }

    private static bool IsKnownVideoHost(Uri? uri)
    {
        if (uri is null || !uri.IsAbsoluteUri)
        {
            return false;
        }

        var host = uri.Host;
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        for (var i = 0; i < MacOsCompositedPassthroughVideoHosts.Length; i++)
        {
            var videoHost = MacOsCompositedPassthroughVideoHosts[i];
            if (host.Equals(videoHost, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith($".{videoHost}", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
