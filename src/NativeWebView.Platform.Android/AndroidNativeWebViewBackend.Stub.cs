using NativeWebView.Core;
using NativeWebView.Interop;

namespace NativeWebView.Platform.Android;

public sealed class AndroidNativeWebViewBackend : NativeWebViewBackendStubBase, INativeWebViewPlatformHandleProvider
{
    private static readonly NativePlatformHandle PlatformHandle = new((nint)0x5001, "android.view.View");
    private static readonly NativePlatformHandle ViewHandle = new((nint)0x5002, "android.webkit.WebView");
    private static readonly NativePlatformHandle ControllerHandle = new((nint)0x5003, "android.webkit.WebViewClient");

    public AndroidNativeWebViewBackend()
        : base(NativeWebViewPlatform.Android, AndroidPlatformFeatures.Instance)
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
