using NativeWebView.Core;
using NativeWebView.Interop;

namespace NativeWebView.Platform.Browser;

public sealed class BrowserNativeWebViewBackend : NativeWebViewBackendStubBase, INativeWebViewPlatformHandleProvider
{
    private static readonly NativePlatformHandle PlatformHandle = new((nint)0x6001, "Window");
    private static readonly NativePlatformHandle ViewHandle = new((nint)0x6002, "HTMLIFrameElement");
    private static readonly NativePlatformHandle ControllerHandle = new((nint)0x6003, "IJSObjectReference");

    public BrowserNativeWebViewBackend()
        : base(NativeWebViewPlatform.Browser, BrowserPlatformFeatures.Instance)
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
