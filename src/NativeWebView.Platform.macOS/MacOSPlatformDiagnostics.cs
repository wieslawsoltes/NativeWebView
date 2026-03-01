using NativeWebView.Core;

namespace NativeWebView.Platform.macOS;

internal static class MacOSPlatformDiagnostics
{
    public static NativeWebViewPlatformDiagnostics Create()
    {
        var issues = new List<NativeWebViewDiagnosticIssue>();

        if (!OperatingSystem.IsMacOS())
        {
            issues.Add(new NativeWebViewDiagnosticIssue(
                code: "macos.host.mismatch",
                severity: NativeWebViewDiagnosticSeverity.Warning,
                message: "macOS backend diagnostics are running on a non-macOS host.",
                recommendation: "Run this diagnostic on macOS to validate WKWebView prerequisites."));
        }
        else if (!OperatingSystem.IsMacOSVersionAtLeast(11))
        {
            issues.Add(new NativeWebViewDiagnosticIssue(
                code: "macos.os.version",
                severity: NativeWebViewDiagnosticSeverity.Error,
                message: "macOS 11 or newer is required.",
                recommendation: "Upgrade macOS to version 11+."));
        }

        if (issues.Count == 0)
        {
            issues.Add(new NativeWebViewDiagnosticIssue(
                code: "macos.ready",
                severity: NativeWebViewDiagnosticSeverity.Info,
                message: "macOS prerequisite checks passed."));
        }

        return new NativeWebViewPlatformDiagnostics(
            NativeWebViewPlatform.MacOS,
            providerName: nameof(MacOSPlatformDiagnostics),
            issues);
    }
}
