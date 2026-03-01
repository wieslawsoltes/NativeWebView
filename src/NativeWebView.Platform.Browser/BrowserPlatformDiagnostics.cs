using NativeWebView.Core;

namespace NativeWebView.Platform.Browser;

internal static class BrowserPlatformDiagnostics
{
    public static NativeWebViewPlatformDiagnostics Create()
    {
        var issues = new List<NativeWebViewDiagnosticIssue>();

        if (!OperatingSystem.IsBrowser())
        {
            issues.Add(new NativeWebViewDiagnosticIssue(
                code: "browser.host.mismatch",
                severity: NativeWebViewDiagnosticSeverity.Warning,
                message: "Browser backend diagnostics are running on a non-browser host.",
                recommendation: "Run this diagnostic in WASM/browser runtime for accurate validation."));
        }

        var popupSupport = Environment.GetEnvironmentVariable("NATIVEWEBVIEW_BROWSER_POPUP_SUPPORT");
        if (string.Equals(popupSupport, "0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(popupSupport, "false", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new NativeWebViewDiagnosticIssue(
                code: "browser.popup.disabled",
                severity: NativeWebViewDiagnosticSeverity.Warning,
                message: "Popup support is marked as disabled.",
                recommendation: "Enable window.open/popups for interactive auth flows."));
        }

        if (issues.Count == 0)
        {
            issues.Add(new NativeWebViewDiagnosticIssue(
                code: "browser.ready",
                severity: NativeWebViewDiagnosticSeverity.Info,
                message: "Browser prerequisite checks passed."));
        }

        return new NativeWebViewPlatformDiagnostics(
            NativeWebViewPlatform.Browser,
            providerName: nameof(BrowserPlatformDiagnostics),
            issues);
    }
}
