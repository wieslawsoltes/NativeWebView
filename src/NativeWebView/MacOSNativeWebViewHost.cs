using System.Runtime.InteropServices;
using Avalonia.Platform;
using NativeWebView.Core;

namespace NativeWebView.Controls;

internal sealed class MacOSNativeWebViewHost : IDisposable
{
    private const double CompositedOverlayAlpha = 0.011;
    private const nuint NSViewWidthSizable = 1u << 1;
    private const nuint NSViewHeightSizable = 1u << 4;

    private static IntPtr NSStringClass => ObjC.GetClass("NSString");
    private static IntPtr NSURLClass => ObjC.GetClass("NSURL");
    private static IntPtr NSURLRequestClass => ObjC.GetClass("NSURLRequest");
    private static IntPtr WKWebViewClass => ObjC.GetClass("WKWebView");
    private static IntPtr WKWebViewConfigurationClass => ObjC.GetClass("WKWebViewConfiguration");

    private static readonly IntPtr SelAlloc = ObjC.GetSelector("alloc");
    private static readonly IntPtr SelInit = ObjC.GetSelector("init");
    private static readonly IntPtr SelRelease = ObjC.GetSelector("release");
    private static readonly IntPtr SelRemoveFromSuperview = ObjC.GetSelector("removeFromSuperview");
    private static readonly IntPtr SelAddSubview = ObjC.GetSelector("addSubview:");
    private static readonly IntPtr SelSetAutoresizingMask = ObjC.GetSelector("setAutoresizingMask:");
    private static readonly IntPtr SelBounds = ObjC.GetSelector("bounds");
    private static readonly IntPtr SelStringWithUtf8String = ObjC.GetSelector("stringWithUTF8String:");
    private static readonly IntPtr SelUrlWithString = ObjC.GetSelector("URLWithString:");
    private static readonly IntPtr SelRequestWithUrl = ObjC.GetSelector("requestWithURL:");
    private static readonly IntPtr SelInitWithFrameConfiguration = ObjC.GetSelector("initWithFrame:configuration:");
    private static readonly IntPtr SelLoadRequest = ObjC.GetSelector("loadRequest:");
    private static readonly IntPtr SelReload = ObjC.GetSelector("reload");
    private static readonly IntPtr SelStopLoading = ObjC.GetSelector("stopLoading");
    private static readonly IntPtr SelGoBack = ObjC.GetSelector("goBack");
    private static readonly IntPtr SelGoForward = ObjC.GetSelector("goForward");
    private static readonly IntPtr SelSetCustomUserAgent = ObjC.GetSelector("setCustomUserAgent:");
    private static readonly IntPtr SelRespondsToSelector = ObjC.GetSelector("respondsToSelector:");
    private static readonly IntPtr SelSetPageZoom = ObjC.GetSelector("setPageZoom:");
    private static readonly IntPtr SelPrint = ObjC.GetSelector("print:");
    private static readonly IntPtr SelDataWithPdfInsideRect = ObjC.GetSelector("dataWithPDFInsideRect:");
    private static readonly IntPtr SelWriteToFileAtomically = ObjC.GetSelector("writeToFile:atomically:");
    private static readonly IntPtr SelSetHidden = ObjC.GetSelector("setHidden:");
    private static readonly IntPtr SelSetNeedsDisplay = ObjC.GetSelector("setNeedsDisplay:");
    private static readonly IntPtr SelDisplayIfNeeded = ObjC.GetSelector("displayIfNeeded");
    private static readonly IntPtr SelSetFrame = ObjC.GetSelector("setFrame:");
    private static readonly IntPtr SelSetAlphaValue = ObjC.GetSelector("setAlphaValue:");
    private static readonly IntPtr SelSuperview = ObjC.GetSelector("superview");
    private static readonly IntPtr SelWindow = ObjC.GetSelector("window");
    private static readonly IntPtr SelMakeFirstResponder = ObjC.GetSelector("makeFirstResponder:");
    private static readonly IntPtr SelBackingScaleFactor = ObjC.GetSelector("backingScaleFactor");
    private static readonly IntPtr SelBitmapImageRepForCachingDisplayInRect = ObjC.GetSelector("bitmapImageRepForCachingDisplayInRect:");
    private static readonly IntPtr SelCacheDisplayInRectToBitmapImageRep = ObjC.GetSelector("cacheDisplayInRect:toBitmapImageRep:");
    private static readonly IntPtr SelBitmapData = ObjC.GetSelector("bitmapData");
    private static readonly IntPtr SelBytesPerRow = ObjC.GetSelector("bytesPerRow");
    private static readonly IntPtr SelPixelsWide = ObjC.GetSelector("pixelsWide");
    private static readonly IntPtr SelPixelsHigh = ObjC.GetSelector("pixelsHigh");

    private bool _disposed;
    private NativeWebViewRenderMode _renderMode = NativeWebViewRenderMode.Embedded;
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

        var initialFrame = ObjC.SendCGRect(parent.Handle, SelBounds);

        ConfigurationHandle = ObjC.SendIntPtr(ObjC.SendIntPtr(WKWebViewConfigurationClass, SelAlloc), SelInit);
        ViewHandle = ObjC.SendIntPtrCGRectIntPtr(
            ObjC.SendIntPtr(WKWebViewClass, SelAlloc),
            SelInitWithFrameConfiguration,
            initialFrame,
            ConfigurationHandle);

        if (ViewHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create WKWebView native view.");
        }

        ObjC.SendVoidIntPtr(parent.Handle, SelAddSubview, ViewHandle);
        ObjC.SendVoidCGRect(ViewHandle, SelSetFrame, initialFrame);
        ObjC.SendVoidDouble(ViewHandle, SelSetAlphaValue, 1d);
        ObjC.SendVoidNUInt(ViewHandle, SelSetAutoresizingMask, NSViewWidthSizable | NSViewHeightSizable);
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
            ObjC.SendVoidByte(ViewHandle, SelSetHidden, 0);
            ObjC.SendVoidDouble(ViewHandle, SelSetAlphaValue, 1d);
        }
        else
        {
            // AppKit skips hit-testing for near-zero alpha values; keep a faint overlay so
            // mouse/keyboard continue to target the WKWebView in composited modes.
            ObjC.SendVoidByte(ViewHandle, SelSetHidden, 0);
            ObjC.SendVoidDouble(ViewHandle, SelSetAlphaValue, CompositedOverlayAlpha);
            TryMakeFirstResponder();
        }

        UpdateLayoutForCurrentMode();
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

        var captureRect = ObjC.SendCGRect(ViewHandle, SelBounds);
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
                ObjC.SendVoidDouble(ViewHandle, SelSetAlphaValue, 1d);
                restoreOverlayAlpha = true;
            }

            ObjC.SendVoidByte(ViewHandle, SelSetNeedsDisplay, 1);
            ObjC.SendVoid(ViewHandle, SelDisplayIfNeeded);

            var bitmapRep = ObjC.SendIntPtrCGRect(ViewHandle, SelBitmapImageRepForCachingDisplayInRect, captureRect);
            if (bitmapRep == IntPtr.Zero)
            {
                return false;
            }

            ObjC.SendVoidCGRectIntPtr(ViewHandle, SelCacheDisplayInRectToBitmapImageRep, captureRect, bitmapRep);

            var bitmapData = ObjC.SendIntPtr(bitmapRep, SelBitmapData);
            if (bitmapData == IntPtr.Zero)
            {
                return false;
            }

            var bytesPerRow = ObjC.SendNInt(bitmapRep, SelBytesPerRow);
            var pixelsWide = ObjC.SendNInt(bitmapRep, SelPixelsWide);
            var pixelsHigh = ObjC.SendNInt(bitmapRep, SelPixelsHigh);

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
                ObjC.SendVoidDouble(ViewHandle, SelSetAlphaValue, CompositedOverlayAlpha);
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
                ObjC.SendVoidDouble(ViewHandle, SelSetAlphaValue, 1d);
            }
            else
            {
                ObjC.SendVoidDouble(ViewHandle, SelSetAlphaValue, CompositedOverlayAlpha);
            }
        }
    }

    public void Navigate(Uri uri)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(uri);

        var nsUrl = CreateNSStringBackedObject(NSURLClass, SelUrlWithString, uri.AbsoluteUri);
        if (nsUrl == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create NSURL from URI.");
        }

        var request = ObjC.SendIntPtrIntPtr(NSURLRequestClass, SelRequestWithUrl, nsUrl);
        if (request == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create NSURLRequest.");
        }

        _ = ObjC.SendIntPtrIntPtr(ViewHandle, SelLoadRequest, request);
    }

    public void Reload()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ = ObjC.SendIntPtr(ViewHandle, SelReload);
    }

    public void Stop()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ObjC.SendVoid(ViewHandle, SelStopLoading);
    }

    public void GoBack()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ = ObjC.SendIntPtr(ViewHandle, SelGoBack);
    }

    public void GoForward()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ = ObjC.SendIntPtr(ViewHandle, SelGoForward);
    }

    public void SetUserAgent(string? userAgent)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var agent = userAgent is null
            ? IntPtr.Zero
            : CreateNSString(userAgent);

        ObjC.SendVoidIntPtr(ViewHandle, SelSetCustomUserAgent, agent);
    }

    public void SetZoomFactor(double zoomFactor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!ObjC.SendBoolIntPtr(ViewHandle, SelRespondsToSelector, SelSetPageZoom))
        {
            return;
        }

        ObjC.SendVoidDouble(ViewHandle, SelSetPageZoom, zoomFactor);
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

        if (!ObjC.SendBoolIntPtr(ViewHandle, SelRespondsToSelector, SelPrint))
        {
            return false;
        }

        ObjC.SendVoidIntPtr(ViewHandle, SelPrint, IntPtr.Zero);
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
            ObjC.SendVoid(ViewHandle, SelStopLoading);
            ObjC.SendVoid(ViewHandle, SelRemoveFromSuperview);
            ObjC.SendVoid(ViewHandle, SelRelease);
            ViewHandle = IntPtr.Zero;
        }

        if (ConfigurationHandle != IntPtr.Zero)
        {
            ObjC.SendVoid(ConfigurationHandle, SelRelease);
            ConfigurationHandle = IntPtr.Zero;
        }
    }

    private void RestoreEmbeddedFrame()
    {
        var superView = ObjC.SendIntPtr(ViewHandle, SelSuperview);
        if (superView == IntPtr.Zero)
        {
            return;
        }

        var bounds = ObjC.SendCGRect(superView, SelBounds);
        ObjC.SendVoidCGRect(ViewHandle, SelSetFrame, bounds);
    }

    private void PlaceOffscreen()
    {
        var offscreenRect = new CGRect(
            new CGPoint(-10000, -10000),
            new CGSize(Math.Max(1, _capturePixelWidth), Math.Max(1, _capturePixelHeight)));

        ObjC.SendVoidCGRect(ViewHandle, SelSetFrame, offscreenRect);
    }

    private double GetBackingScaleFactor()
    {
        var window = ObjC.SendIntPtr(ViewHandle, SelWindow);
        if (window == IntPtr.Zero)
        {
            return 1d;
        }

        var scale = ObjC.SendDouble(window, SelBackingScaleFactor);
        return scale > 0 ? scale : 1d;
    }

    private void TryMakeFirstResponder()
    {
        var window = ObjC.SendIntPtr(ViewHandle, SelWindow);
        if (window == IntPtr.Zero)
        {
            return;
        }

        _ = ObjC.SendBoolIntPtr(window, SelMakeFirstResponder, ViewHandle);
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
            if (!ObjC.SendBoolIntPtr(ViewHandle, SelRespondsToSelector, SelDataWithPdfInsideRect))
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

            var bounds = ObjC.SendCGRect(ViewHandle, SelBounds);
            var pdfData = ObjC.SendIntPtrCGRect(ViewHandle, SelDataWithPdfInsideRect, bounds);

            if (pdfData == IntPtr.Zero)
            {
                return new NativeWebViewPrintResult(NativeWebViewPrintStatus.Failed, "WKWebView returned no PDF data.");
            }

            var pathHandle = CreateNSString(fullPath);
            var written = ObjC.SendBoolIntPtrBool(pdfData, SelWriteToFileAtomically, pathHandle, true);
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
            return ObjC.SendIntPtrIntPtr(NSStringClass, SelStringWithUtf8String, utf8);
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
