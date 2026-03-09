using System.Runtime.InteropServices;
using Avalonia.Platform;
using NativeWebView.Core;

namespace NativeWebView.Controls;

internal sealed class MacOSNativeWebViewHost : IDisposable
{
    private const double CompositedOverlayAlpha = 0.011;
    private const nuint NSViewWidthSizable = 1u << 1;
    private const nuint NSViewHeightSizable = 1u << 4;

    private static class NativeSymbols
    {
        public static readonly IntPtr NSStringClass = ObjC.GetClass("NSString");
        public static readonly IntPtr NSURLClass = ObjC.GetClass("NSURL");
        public static readonly IntPtr NSURLRequestClass = ObjC.GetClass("NSURLRequest");
        public static readonly IntPtr WKWebViewClass = ObjC.GetClass("WKWebView");
        public static readonly IntPtr WKWebViewConfigurationClass = ObjC.GetClass("WKWebViewConfiguration");

        public static readonly IntPtr SelAlloc = ObjC.GetSelector("alloc");
        public static readonly IntPtr SelInit = ObjC.GetSelector("init");
        public static readonly IntPtr SelRelease = ObjC.GetSelector("release");
        public static readonly IntPtr SelRemoveFromSuperview = ObjC.GetSelector("removeFromSuperview");
        public static readonly IntPtr SelAddSubview = ObjC.GetSelector("addSubview:");
        public static readonly IntPtr SelSetAutoresizingMask = ObjC.GetSelector("setAutoresizingMask:");
        public static readonly IntPtr SelBounds = ObjC.GetSelector("bounds");
        public static readonly IntPtr SelStringWithUtf8String = ObjC.GetSelector("stringWithUTF8String:");
        public static readonly IntPtr SelUrlWithString = ObjC.GetSelector("URLWithString:");
        public static readonly IntPtr SelRequestWithUrl = ObjC.GetSelector("requestWithURL:");
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
        public static readonly IntPtr SelDataWithPdfInsideRect = ObjC.GetSelector("dataWithPDFInsideRect:");
        public static readonly IntPtr SelWriteToFileAtomically = ObjC.GetSelector("writeToFile:atomically:");
        public static readonly IntPtr SelSetHidden = ObjC.GetSelector("setHidden:");
        public static readonly IntPtr SelSetNeedsDisplay = ObjC.GetSelector("setNeedsDisplay:");
        public static readonly IntPtr SelDisplayIfNeeded = ObjC.GetSelector("displayIfNeeded");
        public static readonly IntPtr SelSetFrame = ObjC.GetSelector("setFrame:");
        public static readonly IntPtr SelSetAlphaValue = ObjC.GetSelector("setAlphaValue:");
        public static readonly IntPtr SelSuperview = ObjC.GetSelector("superview");
        public static readonly IntPtr SelWindow = ObjC.GetSelector("window");
        public static readonly IntPtr SelMakeFirstResponder = ObjC.GetSelector("makeFirstResponder:");
        public static readonly IntPtr SelBackingScaleFactor = ObjC.GetSelector("backingScaleFactor");
        public static readonly IntPtr SelBitmapImageRepForCachingDisplayInRect = ObjC.GetSelector("bitmapImageRepForCachingDisplayInRect:");
        public static readonly IntPtr SelCacheDisplayInRectToBitmapImageRep = ObjC.GetSelector("cacheDisplayInRect:toBitmapImageRep:");
        public static readonly IntPtr SelBitmapData = ObjC.GetSelector("bitmapData");
        public static readonly IntPtr SelBytesPerRow = ObjC.GetSelector("bytesPerRow");
        public static readonly IntPtr SelPixelsWide = ObjC.GetSelector("pixelsWide");
        public static readonly IntPtr SelPixelsHigh = ObjC.GetSelector("pixelsHigh");
    }

    private bool _disposed;
    private NativeWebViewRenderMode _renderMode = NativeWebViewRenderMode.Embedded;
    private bool _compositedPassthroughEnabled;
    private int _capturePixelWidth = 1;
    private int _capturePixelHeight = 1;
    private long _captureFrameSequence;

    public MacOSNativeWebViewHost(IPlatformHandle parent)
    {
        ArgumentNullException.ThrowIfNull(parent);

        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("MacOSNativeWebViewHost can only be created on macOS.");
        }

        if (parent.Handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Parent native handle is invalid.");
        }

        var initialFrame = ObjC.SendCGRect(parent.Handle, NativeSymbols.SelBounds);

        ConfigurationHandle = ObjC.SendIntPtr(ObjC.SendIntPtr(NativeSymbols.WKWebViewConfigurationClass, NativeSymbols.SelAlloc), NativeSymbols.SelInit);
        ViewHandle = ObjC.SendIntPtrCGRectIntPtr(
            ObjC.SendIntPtr(NativeSymbols.WKWebViewClass, NativeSymbols.SelAlloc),
            NativeSymbols.SelInitWithFrameConfiguration,
            initialFrame,
            ConfigurationHandle);

        if (ViewHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create WKWebView native view.");
        }

        ObjC.SendVoidIntPtr(parent.Handle, NativeSymbols.SelAddSubview, ViewHandle);
        ObjC.SendVoidCGRect(ViewHandle, NativeSymbols.SelSetFrame, initialFrame);
        ObjC.SendVoidDouble(ViewHandle, NativeSymbols.SelSetAlphaValue, 1d);
        ObjC.SendVoidNUInt(ViewHandle, NativeSymbols.SelSetAutoresizingMask, NSViewWidthSizable | NSViewHeightSizable);
        PlatformHandle = new PlatformHandle(ViewHandle, "NSView");
    }

    public IPlatformHandle PlatformHandle { get; }

    public IntPtr ViewHandle { get; private set; }

    public IntPtr ConfigurationHandle { get; private set; }

    public bool SupportsRenderMode(NativeWebViewRenderMode renderMode)
    {
        return renderMode is NativeWebViewRenderMode.GpuSurface or NativeWebViewRenderMode.Offscreen;
    }

    public void SetRenderMode(NativeWebViewRenderMode renderMode)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _renderMode = renderMode;

        if (_renderMode == NativeWebViewRenderMode.Embedded)
        {
            ObjC.SendVoidByte(ViewHandle, NativeSymbols.SelSetHidden, 0);
            ObjC.SendVoidDouble(ViewHandle, NativeSymbols.SelSetAlphaValue, 1d);
        }
        else
        {
            // AppKit skips hit-testing for near-zero alpha values; keep a faint overlay so
            // mouse/keyboard continue to target the WKWebView in composited modes.
            ObjC.SendVoidByte(ViewHandle, NativeSymbols.SelSetHidden, 0);
            ObjC.SendVoidDouble(ViewHandle, NativeSymbols.SelSetAlphaValue, ResolveCompositedOverlayAlpha());
            TryMakeFirstResponder();
        }

        UpdateLayoutForCurrentMode();
    }

    public void SetCompositedPassthrough(bool enabled)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _compositedPassthroughEnabled = enabled;
        if (_renderMode != NativeWebViewRenderMode.Embedded && ViewHandle != IntPtr.Zero)
        {
            ObjC.SendVoidDouble(ViewHandle, NativeSymbols.SelSetAlphaValue, ResolveCompositedOverlayAlpha());
        }
    }

    public void SetCaptureSize(int pixelWidth, int pixelHeight)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var newWidth = Math.Max(1, pixelWidth);
        var newHeight = Math.Max(1, pixelHeight);
        var captureSizeChanged = newWidth != _capturePixelWidth || newHeight != _capturePixelHeight;

        _capturePixelWidth = newWidth;
        _capturePixelHeight = newHeight;

        if (_renderMode != NativeWebViewRenderMode.Embedded && captureSizeChanged)
        {
            UpdateLayoutForCurrentMode();
        }
    }

    public void UpdateLayoutForCurrentMode()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (ViewHandle == IntPtr.Zero)
        {
            return;
        }

        if (_renderMode == NativeWebViewRenderMode.Embedded)
        {
            RestoreEmbeddedFrame();
        }
        else
        {
            // Composited mode keeps the view aligned with host bounds so pointer/keyboard input remains active.
            RestoreEmbeddedFrame();
        }
    }

    public bool TryCaptureFrame(
        NativeWebViewRenderMode renderMode,
        int pixelWidth,
        int pixelHeight,
        out NativeWebViewRenderFrame? frame)
    {
        frame = null;

        if (!SupportsRenderMode(renderMode) || renderMode == NativeWebViewRenderMode.Embedded)
        {
            return false;
        }

        ObjectDisposedException.ThrowIf(_disposed, this);

        if (ViewHandle == IntPtr.Zero || !ObjC.IsMainThread())
        {
            return false;
        }

        var width = Math.Max(1, pixelWidth);
        var height = Math.Max(1, pixelHeight);
        SetCaptureSize(width, height);

        var captureRect = ObjC.SendCGRect(ViewHandle, NativeSymbols.SelBounds);
        if (captureRect.Size.Width <= 0 || captureRect.Size.Height <= 0)
        {
            var scale = GetBackingScaleFactor();
            captureRect = new CGRect(
                new CGPoint(0, 0),
                new CGSize(Math.Max(1, width / scale), Math.Max(1, height / scale)));
        }

        try
        {
            var restoreOverlayAlpha = false;
            if (_renderMode != NativeWebViewRenderMode.Embedded)
            {
                ObjC.SendVoidDouble(ViewHandle, NativeSymbols.SelSetAlphaValue, 1d);
                restoreOverlayAlpha = true;
            }

            ObjC.SendVoidByte(ViewHandle, NativeSymbols.SelSetNeedsDisplay, 1);
            ObjC.SendVoid(ViewHandle, NativeSymbols.SelDisplayIfNeeded);

            var bitmapRep = ObjC.SendIntPtrCGRect(ViewHandle, NativeSymbols.SelBitmapImageRepForCachingDisplayInRect, captureRect);
            if (bitmapRep == IntPtr.Zero)
            {
                return false;
            }

            ObjC.SendVoidCGRectIntPtr(ViewHandle, NativeSymbols.SelCacheDisplayInRectToBitmapImageRep, captureRect, bitmapRep);

            var bitmapData = ObjC.SendIntPtr(bitmapRep, NativeSymbols.SelBitmapData);
            if (bitmapData == IntPtr.Zero)
            {
                return false;
            }

            var bytesPerRow = ObjC.SendNInt(bitmapRep, NativeSymbols.SelBytesPerRow);
            var pixelsWide = ObjC.SendNInt(bitmapRep, NativeSymbols.SelPixelsWide);
            var pixelsHigh = ObjC.SendNInt(bitmapRep, NativeSymbols.SelPixelsHigh);

            if (bytesPerRow <= 0 || pixelsWide <= 0 || pixelsHigh <= 0)
            {
                return false;
            }

            var totalBytes = checked((int)bytesPerRow * (int)pixelsHigh);
            var pixelData = new byte[totalBytes];
            Marshal.Copy(bitmapData, pixelData, 0, totalBytes);

            frame = new NativeWebViewRenderFrame(
                (int)pixelsWide,
                (int)pixelsHigh,
                (int)bytesPerRow,
                NativeWebViewRenderPixelFormat.Bgra8888Premultiplied,
                pixelData,
                isSynthetic: false,
                frameId: Interlocked.Increment(ref _captureFrameSequence),
                capturedAtUtc: DateTimeOffset.UtcNow,
                renderMode: renderMode,
                origin: NativeWebViewRenderFrameOrigin.NativeCapture);

            if (restoreOverlayAlpha)
            {
                ObjC.SendVoidDouble(ViewHandle, NativeSymbols.SelSetAlphaValue, ResolveCompositedOverlayAlpha());
            }

            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (_renderMode == NativeWebViewRenderMode.Embedded)
            {
                ObjC.SendVoidDouble(ViewHandle, NativeSymbols.SelSetAlphaValue, 1d);
            }
            else
            {
                ObjC.SendVoidDouble(ViewHandle, NativeSymbols.SelSetAlphaValue, ResolveCompositedOverlayAlpha());
            }
        }
    }

    public void Navigate(Uri uri)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(uri);

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

        _ = ObjC.SendIntPtrIntPtr(ViewHandle, NativeSymbols.SelLoadRequest, request);
    }

    public void Reload()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ = ObjC.SendIntPtr(ViewHandle, NativeSymbols.SelReload);
    }

    public void Stop()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ObjC.SendVoid(ViewHandle, NativeSymbols.SelStopLoading);
    }

    public void GoBack()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ = ObjC.SendIntPtr(ViewHandle, NativeSymbols.SelGoBack);
    }

    public void GoForward()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ = ObjC.SendIntPtr(ViewHandle, NativeSymbols.SelGoForward);
    }

    public void SetUserAgent(string? userAgent)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var agent = userAgent is null
            ? IntPtr.Zero
            : CreateNSString(userAgent);

        ObjC.SendVoidIntPtr(ViewHandle, NativeSymbols.SelSetCustomUserAgent, agent);
    }

    public void SetZoomFactor(double zoomFactor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!ObjC.SendBoolIntPtr(ViewHandle, NativeSymbols.SelRespondsToSelector, NativeSymbols.SelSetPageZoom))
        {
            return;
        }

        ObjC.SendVoidDouble(ViewHandle, NativeSymbols.SelSetPageZoom, zoomFactor);
    }

    public NativeWebViewPrintResult Print(NativeWebViewPrintSettings? settings = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (ViewHandle == IntPtr.Zero)
        {
            return new NativeWebViewPrintResult(NativeWebViewPrintStatus.Failed, "Native WKWebView handle is unavailable.");
        }

        if (!ObjC.IsMainThread())
        {
            return new NativeWebViewPrintResult(NativeWebViewPrintStatus.Failed, "Print operations must run on the macOS main thread.");
        }

        if (!string.IsNullOrWhiteSpace(settings?.OutputPath))
        {
            return ExportPdf(settings.OutputPath!);
        }

        return ShowPrintUi()
            ? new NativeWebViewPrintResult(NativeWebViewPrintStatus.Success)
            : new NativeWebViewPrintResult(NativeWebViewPrintStatus.Failed, "Native print UI is unavailable.");
    }

    public bool ShowPrintUi()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (ViewHandle == IntPtr.Zero || !ObjC.IsMainThread())
        {
            return false;
        }

        if (!ObjC.SendBoolIntPtr(ViewHandle, NativeSymbols.SelRespondsToSelector, NativeSymbols.SelPrint))
        {
            return false;
        }

        ObjC.SendVoidIntPtr(ViewHandle, NativeSymbols.SelPrint, IntPtr.Zero);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (ViewHandle != IntPtr.Zero)
        {
            ObjC.SendVoid(ViewHandle, NativeSymbols.SelStopLoading);
            ObjC.SendVoid(ViewHandle, NativeSymbols.SelRemoveFromSuperview);
            ObjC.SendVoid(ViewHandle, NativeSymbols.SelRelease);
            ViewHandle = IntPtr.Zero;
        }

        if (ConfigurationHandle != IntPtr.Zero)
        {
            ObjC.SendVoid(ConfigurationHandle, NativeSymbols.SelRelease);
            ConfigurationHandle = IntPtr.Zero;
        }
    }

    private void RestoreEmbeddedFrame()
    {
        var superView = ObjC.SendIntPtr(ViewHandle, NativeSymbols.SelSuperview);
        if (superView == IntPtr.Zero)
        {
            return;
        }

        var bounds = ObjC.SendCGRect(superView, NativeSymbols.SelBounds);
        ObjC.SendVoidCGRect(ViewHandle, NativeSymbols.SelSetFrame, bounds);
    }

    private void PlaceOffscreen()
    {
        var offscreenRect = new CGRect(
            new CGPoint(-10000, -10000),
            new CGSize(Math.Max(1, _capturePixelWidth), Math.Max(1, _capturePixelHeight)));

        ObjC.SendVoidCGRect(ViewHandle, NativeSymbols.SelSetFrame, offscreenRect);
    }

    private double GetBackingScaleFactor()
    {
        var window = ObjC.SendIntPtr(ViewHandle, NativeSymbols.SelWindow);
        if (window == IntPtr.Zero)
        {
            return 1d;
        }

        var scale = ObjC.SendDouble(window, NativeSymbols.SelBackingScaleFactor);
        return scale > 0 ? scale : 1d;
    }

    private void TryMakeFirstResponder()
    {
        var window = ObjC.SendIntPtr(ViewHandle, NativeSymbols.SelWindow);
        if (window == IntPtr.Zero)
        {
            return;
        }

        _ = ObjC.SendBoolIntPtr(window, NativeSymbols.SelMakeFirstResponder, ViewHandle);
    }

    private double ResolveCompositedOverlayAlpha()
    {
        return _compositedPassthroughEnabled ? 1d : CompositedOverlayAlpha;
    }

    private static IntPtr CreateNSStringBackedObject(IntPtr classHandle, IntPtr selector, string value)
    {
        var nsString = CreateNSString(value);
        return ObjC.SendIntPtrIntPtr(classHandle, selector, nsString);
    }

    private NativeWebViewPrintResult ExportPdf(string outputPath)
    {
        try
        {
            if (!ObjC.SendBoolIntPtr(ViewHandle, NativeSymbols.SelRespondsToSelector, NativeSymbols.SelDataWithPdfInsideRect))
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

            var bounds = ObjC.SendCGRect(ViewHandle, NativeSymbols.SelBounds);
            var pdfData = ObjC.SendIntPtrCGRect(ViewHandle, NativeSymbols.SelDataWithPdfInsideRect, bounds);

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
        private static extern nint objc_msgSend_NInt(IntPtr receiver, IntPtr selector);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_IntPtr_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_IntPtr_CGRect_IntPtr(IntPtr receiver, IntPtr selector, CGRect arg1, IntPtr arg2);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_IntPtr_CGRect(IntPtr receiver, IntPtr selector, CGRect arg1);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern CGRect objc_msgSend_CGRect(IntPtr receiver, IntPtr selector);

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
        private static extern void objc_msgSend_Void_NUInt(IntPtr receiver, IntPtr selector, nuint arg1);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend_Void_Byte(IntPtr receiver, IntPtr selector, byte arg1);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend_Void_CGRect(IntPtr receiver, IntPtr selector, CGRect arg1);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend_Void_CGRect_IntPtr(IntPtr receiver, IntPtr selector, CGRect arg1, IntPtr arg2);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern double objc_msgSend_Double(IntPtr receiver, IntPtr selector);

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

        public static nint SendNInt(IntPtr receiver, IntPtr selector)
        {
            return objc_msgSend_NInt(receiver, selector);
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

        public static bool SendBoolIntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1)
        {
            return objc_msgSend_Byte_IntPtr(receiver, selector, arg1) != 0;
        }

        public static bool SendBoolIntPtrBool(IntPtr receiver, IntPtr selector, IntPtr arg1, bool arg2)
        {
            return objc_msgSend_Byte_IntPtr_Byte(receiver, selector, arg1, arg2 ? (byte)1 : (byte)0) != 0;
        }

        public static double SendDouble(IntPtr receiver, IntPtr selector)
        {
            return objc_msgSend_Double(receiver, selector);
        }

        public static bool IsMainThread()
        {
            return pthread_main_np() != 0;
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

        public static void SendVoidNUInt(IntPtr receiver, IntPtr selector, nuint arg1)
        {
            objc_msgSend_Void_NUInt(receiver, selector, arg1);
        }

        public static void SendVoidByte(IntPtr receiver, IntPtr selector, byte arg1)
        {
            objc_msgSend_Void_Byte(receiver, selector, arg1);
        }

        public static void SendVoidCGRect(IntPtr receiver, IntPtr selector, CGRect arg1)
        {
            objc_msgSend_Void_CGRect(receiver, selector, arg1);
        }

        public static void SendVoidCGRectIntPtr(IntPtr receiver, IntPtr selector, CGRect arg1, IntPtr arg2)
        {
            objc_msgSend_Void_CGRect_IntPtr(receiver, selector, arg1, arg2);
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
}
