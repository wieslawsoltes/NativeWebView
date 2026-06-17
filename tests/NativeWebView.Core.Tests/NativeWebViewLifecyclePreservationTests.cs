using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.Reflection;
using System.Runtime.CompilerServices;
using NativeWebView.Controls;
using NativeWebView.Core;
using NativeWebView.Interop;
using NativeWebView.Platform.Windows;

namespace NativeWebView.Core.Tests;

#pragma warning disable CS0067
public sealed class NativeWebViewLifecyclePreservationTests
{
    [Fact]
    public void DestroyNativeControlCore_GpuSurface_RequestsRuntimePreservation()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var backend = new AttachmentFrameBackend();
        using var webView = new TestableNativeWebView(backend)
        {
            RenderMode = NativeWebViewRenderMode.GpuSurface,
        };

        var handle = webView.CreateNativeControl(new PlatformHandle((nint)0x2000, "HWND"));
        webView.DestroyNativeControl(handle);

        Assert.True(backend.LastDetachPreserveRuntime);
        Assert.False(backend.DetachDestroyedRuntime);
    }

    [Fact]
    public void DestroyNativeControlCore_EmbeddedMode_RequestsRuntimePreservation()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var backend = new AttachmentFrameBackend();
        using var webView = new TestableNativeWebView(backend)
        {
            RenderMode = NativeWebViewRenderMode.Embedded,
        };

        var handle = webView.CreateNativeControl(new PlatformHandle((nint)0x2000, "HWND"));
        webView.DestroyNativeControl(handle);

        Assert.True(backend.LastDetachPreserveRuntime);
        Assert.False(backend.DetachDestroyedRuntime);
    }

    [Fact]
    public void Dispose_DisposesBackendAfterPreservedDetach()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var backend = new AttachmentFrameBackend();
        var webView = new TestableNativeWebView(backend)
        {
            RenderMode = NativeWebViewRenderMode.GpuSurface,
        };

        var handle = webView.CreateNativeControl(new PlatformHandle((nint)0x2000, "HWND"));
        webView.DestroyNativeControl(handle);
        webView.Dispose();

        Assert.True(backend.LastDetachPreserveRuntime);
        Assert.True(backend.IsDisposed);
    }

    [Fact]
    public void WindowsBackend_NavigateBeforeAttachment_DoesNotInitializeStub()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var backend = new WindowsNativeWebViewBackend();
        var uri = new Uri("https://example.com/");

        backend.Navigate(uri);

        Assert.False(backend.IsInitialized);
        Assert.Equal(uri, backend.CurrentUrl);
    }

    [Fact]
    public void Dispose_PresenterWithExternalInstance_DoesNotDisposeBackendUntilInstanceDispose()
    {
        using var backend = new AttachmentFrameBackend();
        using var instance = new NativeWebViewInstance(backend);
        var webView = new TestableNativeWebView(instance);

        webView.Dispose();

        Assert.False(backend.IsDisposed);

        instance.Dispose();

        Assert.True(backend.IsDisposed);
    }

    [Fact]
    public void Dispose_PresenterWithExternalInstance_DetachesForwardedEventHandlers()
    {
        using var backend = new AttachmentFrameBackend();
        using var instance = new NativeWebViewInstance(backend);
        var disposedPresenterCallCount = 0;
        var activePresenterCallCount = 0;

        var disposedPresenter = new TestableNativeWebView(instance);
        disposedPresenter.NavigationCompleted += (_, _) => disposedPresenterCallCount++;
        disposedPresenter.Dispose();

        using var activePresenter = new TestableNativeWebView(instance);
        activePresenter.NavigationCompleted += (_, _) => activePresenterCallCount++;

        backend.Navigate(new Uri("https://example.com/"));

        Assert.Equal(0, disposedPresenterCallCount);
        Assert.Equal(1, activePresenterCallCount);
    }

    [Fact]
    public void DestroyNativeControlCore_AfterExternalInstanceDispose_DoesNotThrow()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var backend = new AttachmentFrameBackend();
        using var instance = new NativeWebViewInstance(backend);
        using var webView = new TestableNativeWebView(instance)
        {
            RenderMode = NativeWebViewRenderMode.Embedded,
        };

        var handle = webView.CreateNativeControl(new PlatformHandle((nint)0x2000, "HWND"));
        instance.Dispose();

        webView.DestroyNativeControl(handle);
    }

    [Fact]
    public void DestroyNativeControlCore_StalePresenter_DoesNotDetachActiveInstance()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var backend = new AttachmentFrameBackend();
        using var instance = new NativeWebViewInstance(backend);
        using var firstPresenter = new TestableNativeWebView(instance)
        {
            RenderMode = NativeWebViewRenderMode.GpuSurface,
        };
        using var secondPresenter = new TestableNativeWebView(instance)
        {
            RenderMode = NativeWebViewRenderMode.GpuSurface,
        };

        var firstHandle = firstPresenter.CreateNativeControl(new PlatformHandle((nint)0x2000, "HWND"));
        var secondHandle = secondPresenter.CreateNativeControl(new PlatformHandle((nint)0x4000, "HWND"));

        firstPresenter.DestroyNativeControl(firstHandle);

        Assert.Equal(0, backend.DetachCount);

        secondPresenter.DestroyNativeControl(secondHandle);

        Assert.Equal(1, backend.DetachCount);
        Assert.True(backend.LastDetachPreserveRuntime);
    }

    [Theory]
    [InlineData(NativeWebViewRenderMode.GpuSurface)]
    [InlineData(NativeWebViewRenderMode.Offscreen)]
    public void CapturedCompositedFrame_RemainsRetainedAfterDetachReattach(NativeWebViewRenderMode renderMode)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var backend = new AttachmentFrameBackend();
        using var webView = new TestableNativeWebView(backend)
        {
            RenderMode = renderMode,
            Width = 320,
            Height = 200,
        };

        var firstHandle = webView.CreateNativeControl(new PlatformHandle((nint)0x2000, "HWND"));
        var retainedBitmap = CreateUninitializedBitmapSentinel();
        webView.SetRetainedCompositedBitmap(renderMode, retainedBitmap);

        Assert.NotNull(retainedBitmap);

        webView.DestroyNativeControl(firstHandle);
        var secondHandle = webView.CreateNativeControl(new PlatformHandle((nint)0x4000, "HWND"));

        Assert.Same(retainedBitmap, GetRetainedCompositedBitmap(webView, renderMode));

        webView.DestroyNativeControl(secondHandle);
        webView.SetRetainedCompositedBitmap(renderMode, value: null);
    }

    private sealed class TestableNativeWebView : NativeWebView.Controls.NativeWebView
    {
        public TestableNativeWebView(INativeWebViewBackend backend)
            : base(backend)
        {
        }

        public TestableNativeWebView(NativeWebViewInstance instance)
            : base(instance)
        {
        }

        public IPlatformHandle CreateNativeControl(IPlatformHandle parent)
        {
            return CreateNativeControlCore(parent);
        }

        public void DestroyNativeControl(IPlatformHandle control)
        {
            DestroyNativeControlCore(control);
        }

        public void SetRetainedCompositedBitmap(NativeWebViewRenderMode renderMode, object? value)
        {
            var field = GetRetainedCompositedBitmapField(renderMode);
            field.SetValue(this, value);
        }
    }

    private static WriteableBitmap CreateUninitializedBitmapSentinel()
    {
        return (WriteableBitmap)RuntimeHelpers.GetUninitializedObject(typeof(WriteableBitmap));
    }

    private static FieldInfo GetRetainedCompositedBitmapField(NativeWebViewRenderMode renderMode)
    {
        var fieldName = renderMode == NativeWebViewRenderMode.GpuSurface
            ? "_gpuSurfaceBitmap"
            : "_offscreenBitmap";
        var field = typeof(NativeWebView.Controls.NativeWebView).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(field);
        return field!;
    }

    private static object? GetRetainedCompositedBitmap(
        NativeWebView.Controls.NativeWebView webView,
        NativeWebViewRenderMode renderMode)
    {
        return GetRetainedCompositedBitmapField(renderMode).GetValue(webView);
    }

    private sealed class AttachmentFrameBackend
        : INativeWebViewBackend,
          INativeWebViewNativeControlAttachment,
          INativeWebViewFrameSource
    {
        private long _frameId;

        public NativeWebViewPlatform Platform => NativeWebViewPlatform.Windows;

        public IWebViewPlatformFeatures Features { get; } = new WebViewPlatformFeatures(
            NativeWebViewPlatform.Windows,
            NativeWebViewFeature.EmbeddedView |
            NativeWebViewFeature.GpuSurfaceRendering |
            NativeWebViewFeature.OffscreenRendering |
            NativeWebViewFeature.RenderFrameCapture);

        public Uri? CurrentUrl { get; private set; }

        public bool IsInitialized { get; private set; }

        public bool CanGoBack => false;

        public bool CanGoForward => false;

        public bool IsDevToolsEnabled { get; set; }

        public bool IsContextMenuEnabled { get; set; }

        public bool IsStatusBarEnabled { get; set; }

        public bool IsZoomControlEnabled { get; set; }

        public double ZoomFactor { get; private set; } = 1d;

        public string? HeaderString { get; private set; }

        public string? UserAgentString { get; private set; }

        public bool LastDetachPreserveRuntime { get; private set; }

        public bool DetachDestroyedRuntime { get; private set; }

        public int DetachCount { get; private set; }

        public bool IsDisposed { get; private set; }

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

        public NativePlatformHandle AttachToNativeParent(NativePlatformHandle parentHandle)
        {
            return new NativePlatformHandle((nint)0x3000, parentHandle.HandleDescriptor);
        }

        public void DetachFromNativeParent()
        {
            DetachFromNativeParent(preserveRuntime: false);
        }

        public void DetachFromNativeParent(bool preserveRuntime)
        {
            DetachCount++;
            LastDetachPreserveRuntime = preserveRuntime;
            DetachDestroyedRuntime = !preserveRuntime;
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

            var width = Math.Max(1, request.PixelWidth);
            var height = Math.Max(1, request.PixelHeight);
            var bytesPerRow = width * 4;
            var pixels = new byte[bytesPerRow * height];

            for (var index = 0; index < pixels.Length; index += 4)
            {
                pixels[index] = 0x40;
                pixels[index + 1] = 0x80;
                pixels[index + 2] = 0xC0;
                pixels[index + 3] = 0xFF;
            }

            return Task.FromResult<NativeWebViewRenderFrame?>(new NativeWebViewRenderFrame(
                width,
                height,
                bytesPerRow,
                NativeWebViewRenderPixelFormat.Bgra8888Premultiplied,
                pixels,
                isSynthetic: false,
                frameId: Interlocked.Increment(ref _frameId),
                capturedAtUtc: DateTimeOffset.UtcNow,
                renderMode: renderMode,
                origin: NativeWebViewRenderFrameOrigin.NativeCapture));
        }

        public ValueTask InitializeAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IsInitialized = true;
            CoreWebView2Initialized?.Invoke(this, new CoreWebViewInitializedEventArgs(isSuccess: true));
            return ValueTask.CompletedTask;
        }

        public void Navigate(string url)
        {
            Navigate(new Uri(url));
        }

        public void Navigate(Uri uri)
        {
            CurrentUrl = uri;
            NavigationCompleted?.Invoke(this, new NativeWebViewNavigationCompletedEventArgs(uri, isSuccess: true));
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
            return Task.FromResult<string?>("null");
        }

        public Task PostWebMessageAsJsonAsync(string message, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task PostWebMessageAsStringAsync(string message, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public void OpenDevToolsWindow()
        {
        }

        public Task<NativeWebViewPrintResult> PrintAsync(
            NativeWebViewPrintSettings? settings = null,
            CancellationToken cancellationToken = default)
        {
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
            ZoomFactor = zoomFactor;
        }

        public void SetUserAgent(string? userAgent)
        {
            UserAgentString = userAgent;
        }

        public void SetHeader(string? header)
        {
            HeaderString = header;
        }

        public bool TryGetCommandManager(out INativeWebViewCommandManager? commandManager)
        {
            commandManager = null;
            return false;
        }

        public bool TryGetCookieManager(out INativeWebViewCookieManager? cookieManager)
        {
            cookieManager = null;
            return false;
        }

        public void MoveFocus(NativeWebViewFocusMoveDirection direction)
        {
        }

        public void Dispose()
        {
            IsDisposed = true;
            DestroyRequested?.Invoke(this, new NativeWebViewDestroyRequestedEventArgs("Disposed"));
        }
    }
}
#pragma warning restore CS0067
