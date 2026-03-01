using NativeWebView.Core;
using NativeWebView.Interop;

namespace NativeWebView.Platform.Linux;

public sealed class LinuxNativeWebViewBackend : NativeWebViewBackendStubBase, INativeWebViewPlatformHandleProvider
{
    private static readonly NativePlatformHandle PlatformHandle = new((nint)0x3001, "GtkWidget");
    private static readonly NativePlatformHandle ViewHandle = new((nint)0x3002, "WebKitWebView");
    private static readonly NativePlatformHandle ControllerHandle = new((nint)0x3003, "WebKitSettings");

    public LinuxNativeWebViewBackend()
        : base(NativeWebViewPlatform.Linux, LinuxPlatformFeatures.Instance)
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
