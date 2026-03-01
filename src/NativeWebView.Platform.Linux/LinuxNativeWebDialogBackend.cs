using NativeWebView.Core;
using NativeWebView.Interop;

namespace NativeWebView.Platform.Linux;

public sealed class LinuxNativeWebDialogBackend : NativeWebDialogBackendStubBase, INativeWebDialogPlatformHandleProvider
{
    private static readonly NativePlatformHandle PlatformHandle = new((nint)0x3101, "GtkWindow");
    private static readonly NativePlatformHandle DialogHandle = new((nint)0x3102, "GtkDialog");
    private static readonly NativePlatformHandle HostWindowHandle = new((nint)0x3103, "GtkWindow");

    public LinuxNativeWebDialogBackend()
        : base(NativeWebViewPlatform.Linux, LinuxPlatformFeatures.Instance)
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
