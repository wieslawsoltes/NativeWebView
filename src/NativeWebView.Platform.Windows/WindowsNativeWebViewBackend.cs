using NativeWebView.Core;
using NativeWebView.Interop;

namespace NativeWebView.Platform.Windows;

public sealed class WindowsNativeWebViewBackend : NativeWebViewBackendStubBase, INativeWebViewPlatformHandleProvider
{
    private static readonly NativePlatformHandle PlatformHandle = new((nint)0x1001, "HWND");
    private static readonly NativePlatformHandle ViewHandle = new((nint)0x1002, "ICoreWebView2");
    private static readonly NativePlatformHandle ControllerHandle = new((nint)0x1003, "ICoreWebView2Controller");

    public WindowsNativeWebViewBackend()
        : base(NativeWebViewPlatform.Windows, WindowsPlatformFeatures.Instance)
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
