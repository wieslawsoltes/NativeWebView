using NativeWebView.Core;
using NativeWebView.Interop;

namespace NativeWebView.Platform.Windows;

public sealed class WindowsNativeWebDialogBackend : NativeWebDialogBackendStubBase, INativeWebDialogPlatformHandleProvider
{
    private static readonly NativePlatformHandle PlatformHandle = new((nint)0x1101, "HWND");
    private static readonly NativePlatformHandle DialogHandle = new((nint)0x1102, "HWND");
    private static readonly NativePlatformHandle HostWindowHandle = new((nint)0x1103, "HWND");

    public WindowsNativeWebDialogBackend()
        : base(NativeWebViewPlatform.Windows, WindowsPlatformFeatures.Instance)
    {
    }

    public bool TryGetPlatformHandle(out NativePlatformHandle handle)
    {
        handle = PlatformHandle;
        return true;
    }

    public bool TryGetDialogHandle(out NativePlatformHandle handle)
    {
        handle = DialogHandle;
        return true;
    }

    public bool TryGetHostWindowHandle(out NativePlatformHandle handle)
    {
        handle = HostWindowHandle;
        return true;
    }
}
