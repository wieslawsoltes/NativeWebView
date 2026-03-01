using NativeWebView.Core;
using NativeWebView.Interop;

namespace NativeWebView.Platform.iOS;

public sealed class IOSNativeWebViewBackend : NativeWebViewBackendStubBase, INativeWebViewPlatformHandleProvider
{
    private static readonly NativePlatformHandle PlatformHandle = new((nint)0x4001, "UIView");
    private static readonly NativePlatformHandle ViewHandle = new((nint)0x4002, "WKWebView");
    private static readonly NativePlatformHandle ControllerHandle = new((nint)0x4003, "WKWebViewConfiguration");

    public IOSNativeWebViewBackend()
        : base(NativeWebViewPlatform.IOS, IOSPlatformFeatures.Instance)
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
