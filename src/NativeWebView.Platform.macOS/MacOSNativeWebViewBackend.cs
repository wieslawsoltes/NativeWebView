using NativeWebView.Core;
using NativeWebView.Interop;

namespace NativeWebView.Platform.macOS;

public sealed class MacOSNativeWebViewBackend : NativeWebViewBackendStubBase, INativeWebViewPlatformHandleProvider
{
    private static readonly NativePlatformHandle PlatformHandle = new((nint)0x2001, "NSView");
    private static readonly NativePlatformHandle ViewHandle = new((nint)0x2002, "WKWebView");
    private static readonly NativePlatformHandle ControllerHandle = new((nint)0x2003, "WKWebViewConfiguration");

    public MacOSNativeWebViewBackend()
        : base(NativeWebViewPlatform.MacOS, MacOSPlatformFeatures.Instance)
    {
    }

    public bool TryGetPlatformHandle(out NativePlatformHandle handle)
    {
        handle = PlatformHandle;
        return true;
    }

    public bool TryGetViewHandle(out NativePlatformHandle handle)
    {
        handle = ViewHandle;
        return true;
    }

    public bool TryGetControllerHandle(out NativePlatformHandle handle)
    {
        handle = ControllerHandle;
        return true;
    }
}
