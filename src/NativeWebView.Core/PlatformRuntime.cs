using System.Reflection;

namespace NativeWebView.Core;

public static class NativeWebViewRuntime
{
    private static readonly object RegistrationGate = new();
    private static readonly HashSet<NativeWebViewPlatform> RegisteredPlatforms = [];

    public static NativeWebViewBackendFactory Factory { get; } = new();

    public static NativeWebViewPlatform CurrentPlatform { get; } = DetectCurrentPlatform();

    public static void EnsureCurrentPlatformRegistered()
    {
        EnsurePlatformRegistered(CurrentPlatform);
    }

    public static NativeWebViewPlatformDiagnostics GetCurrentPlatformDiagnostics()
    {
        EnsureCurrentPlatformRegistered();
        return Factory.GetPlatformDiagnosticsOrDefault(CurrentPlatform);
    }

    public static NativeWebViewPlatformDiagnostics GetPlatformDiagnostics(NativeWebViewPlatform platform)
    {
        EnsurePlatformRegistered(platform);
        return Factory.GetPlatformDiagnosticsOrDefault(platform);
    }

    public static void EnsurePlatformRegistered(NativeWebViewPlatform platform)
    {
        if (platform is NativeWebViewPlatform.Unknown)
        {
            return;
        }

        lock (RegistrationGate)
        {
            if (RegisteredPlatforms.Contains(platform))
            {
                return;
            }

            var moduleTypeName = GetRegistrationModuleTypeName(platform);
            if (moduleTypeName is null)
            {
                RegisteredPlatforms.Add(platform);
                return;
            }

            var moduleType = Type.GetType(moduleTypeName, throwOnError: false)
                ?? TryResolvePlatformModuleType(moduleTypeName);
            var registerDefault = moduleType?.GetMethod(
                "RegisterDefault",
                BindingFlags.Public | BindingFlags.Static);

            if (registerDefault is null)
            {
                return;
            }

            registerDefault.Invoke(obj: null, parameters: null);
            RegisteredPlatforms.Add(platform);
        }
    }

    private static NativeWebViewPlatform DetectCurrentPlatform()
    {
        if (OperatingSystem.IsBrowser())
        {
            return NativeWebViewPlatform.Browser;
        }

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

        return NativeWebViewPlatform.Unknown;
    }

    private static string? GetRegistrationModuleTypeName(NativeWebViewPlatform platform)
    {
        return platform switch
        {
            NativeWebViewPlatform.Windows =>
                "NativeWebView.Platform.Windows.NativeWebViewPlatformWindowsModule, NativeWebView.Platform.Windows",
            NativeWebViewPlatform.MacOS =>
                "NativeWebView.Platform.macOS.NativeWebViewPlatformMacOSModule, NativeWebView.Platform.macOS",
            NativeWebViewPlatform.Linux =>
                "NativeWebView.Platform.Linux.NativeWebViewPlatformLinuxModule, NativeWebView.Platform.Linux",
            NativeWebViewPlatform.IOS =>
                "NativeWebView.Platform.iOS.NativeWebViewPlatformIOSModule, NativeWebView.Platform.iOS",
            NativeWebViewPlatform.Android =>
                "NativeWebView.Platform.Android.NativeWebViewPlatformAndroidModule, NativeWebView.Platform.Android",
            NativeWebViewPlatform.Browser =>
                "NativeWebView.Platform.Browser.NativeWebViewPlatformBrowserModule, NativeWebView.Platform.Browser",
            _ => null,
        };
    }

    private static Type? TryResolvePlatformModuleType(string typeNameWithAssembly)
    {
        var separator = typeNameWithAssembly.IndexOf(',');
        if (separator <= 0 || separator >= typeNameWithAssembly.Length - 1)
        {
            return null;
        }

        var typeName = typeNameWithAssembly[..separator].Trim();
        var assemblyName = typeNameWithAssembly[(separator + 1)..].Trim();

        try
        {
            var assembly = Assembly.Load(assemblyName);
            return assembly.GetType(typeName, throwOnError: false);
        }
        catch
        {
            return null;
        }
    }
}
