namespace NativeWebView.Core;

internal static class NativeWebViewDiagnosticsHostPlatformOverride
{
    internal const string EnvironmentVariableName = "NATIVEWEBVIEW_DIAGNOSTICS_HOST_PLATFORM";

    public static bool IsEffectiveHostPlatform(NativeWebViewPlatform platform)
    {
        return IsEffectiveHostPlatform(
            platform,
            Environment.GetEnvironmentVariable(EnvironmentVariableName),
            DetectActualHostPlatform());
    }

    internal static bool IsEffectiveHostPlatform(
        NativeWebViewPlatform platform,
        string? overrideValue,
        NativeWebViewPlatform actualHostPlatform)
    {
        if (!TryParseOverride(overrideValue, out var overriddenHostPlatform))
        {
            return actualHostPlatform == platform;
        }

        return overriddenHostPlatform == platform && actualHostPlatform == platform;
    }

    private static bool TryParseOverride(string? value, out NativeWebViewPlatform platform)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            platform = NativeWebViewPlatform.Unknown;
            return false;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "windows":
                platform = NativeWebViewPlatform.Windows;
                return true;
            case "macos":
                platform = NativeWebViewPlatform.MacOS;
                return true;
            case "linux":
                platform = NativeWebViewPlatform.Linux;
                return true;
            case "ios":
                platform = NativeWebViewPlatform.IOS;
                return true;
            case "android":
                platform = NativeWebViewPlatform.Android;
                return true;
            case "browser":
                platform = NativeWebViewPlatform.Browser;
                return true;
            case "unknown":
            case "none":
            case "neutral":
                platform = NativeWebViewPlatform.Unknown;
                return true;
            default:
                platform = NativeWebViewPlatform.Unknown;
                return false;
        }
    }

    private static NativeWebViewPlatform DetectActualHostPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return NativeWebViewPlatform.Windows;
        }

        if (OperatingSystem.IsMacOS())
        {
            return NativeWebViewPlatform.MacOS;
        }

        if (OperatingSystem.IsLinux())
        {
            return NativeWebViewPlatform.Linux;
        }

        if (OperatingSystem.IsIOS())
        {
            return NativeWebViewPlatform.IOS;
        }

        if (OperatingSystem.IsAndroid())
        {
            return NativeWebViewPlatform.Android;
        }

        if (OperatingSystem.IsBrowser())
        {
            return NativeWebViewPlatform.Browser;
        }

        return NativeWebViewPlatform.Unknown;
    }
}
