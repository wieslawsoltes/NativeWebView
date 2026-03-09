using NativeWebView.Core;

namespace NativeWebView.Platform.iOS;

internal static class IOSPlatformDiagnostics
{
    public static NativeWebViewPlatformDiagnostics Create()
    {
        var issues = new List<NativeWebViewDiagnosticIssue>();

        if (!NativeWebViewDiagnosticsHostPlatformOverride.IsEffectiveHostPlatform(NativeWebViewPlatform.IOS))
        {
            issues.Add(new NativeWebViewDiagnosticIssue(
                code: "ios.host.mismatch",
                severity: NativeWebViewDiagnosticSeverity.Warning,
                message: "iOS backend diagnostics are running on a non-iOS host.",
                recommendation: "Run this diagnostic on iOS to validate device/runtime prerequisites."));
        }
        else if (!OperatingSystem.IsIOSVersionAtLeast(15))
        {
            issues.Add(new NativeWebViewDiagnosticIssue(
                code: "ios.os.version",
                severity: NativeWebViewDiagnosticSeverity.Error,
                message: "iOS 15 or newer is required.",
                recommendation: "Upgrade the target device/simulator to iOS 15+."));
        }

        if (issues.Count == 0)
        {
            issues.Add(new NativeWebViewDiagnosticIssue(
                code: "ios.ready",
                severity: NativeWebViewDiagnosticSeverity.Info,
                message: "iOS prerequisite checks passed."));
        }

        return new NativeWebViewPlatformDiagnostics(
            NativeWebViewPlatform.IOS,
            providerName: nameof(IOSPlatformDiagnostics),
            issues);
    }
}
