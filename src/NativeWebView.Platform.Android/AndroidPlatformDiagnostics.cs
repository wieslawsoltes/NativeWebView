using NativeWebView.Core;

namespace NativeWebView.Platform.Android;

internal static class AndroidPlatformDiagnostics
{
    public static NativeWebViewPlatformDiagnostics Create()
    {
        var issues = new List<NativeWebViewDiagnosticIssue>();

        if (!OperatingSystem.IsAndroid())
        {
            issues.Add(new NativeWebViewDiagnosticIssue(
                code: "android.host.mismatch",
                severity: NativeWebViewDiagnosticSeverity.Warning,
                message: "Android backend diagnostics are running on a non-Android host.",
                recommendation: "Run this diagnostic on Android/emulator to validate runtime prerequisites."));
        }
        else
        {
            ValidateApiLevel(issues);
        }

        if (issues.Count == 0)
        {
            issues.Add(new NativeWebViewDiagnosticIssue(
                code: "android.ready",
                severity: NativeWebViewDiagnosticSeverity.Info,
                message: "Android prerequisite checks passed."));
        }

        return new NativeWebViewPlatformDiagnostics(
            NativeWebViewPlatform.Android,
            providerName: nameof(AndroidPlatformDiagnostics),
            issues);
    }

    private static void ValidateApiLevel(List<NativeWebViewDiagnosticIssue> issues)
    {
        var apiLevel = Environment.GetEnvironmentVariable("ANDROID_API_LEVEL");
        if (string.IsNullOrWhiteSpace(apiLevel))
        {
            issues.Add(new NativeWebViewDiagnosticIssue(
                code: "android.api.unset",
                severity: NativeWebViewDiagnosticSeverity.Warning,
                message: "ANDROID_API_LEVEL is not set.",
                recommendation: "Set ANDROID_API_LEVEL in CI/device diagnostics to enforce minimum API level checks."));
            return;
        }

        if (!int.TryParse(apiLevel, out var parsed))
        {
            issues.Add(new NativeWebViewDiagnosticIssue(
                code: "android.api.invalid",
                severity: NativeWebViewDiagnosticSeverity.Warning,
                message: $"ANDROID_API_LEVEL is invalid: {apiLevel}",
                recommendation: "Set ANDROID_API_LEVEL to a numeric API level."));
            return;
        }

        const int minimumApi = 24;
        if (parsed < minimumApi)
        {
            issues.Add(new NativeWebViewDiagnosticIssue(
                code: "android.api.too_low",
                severity: NativeWebViewDiagnosticSeverity.Error,
                message: $"Android API level {parsed} is below required minimum {minimumApi}.",
                recommendation: "Use API 24+ for Android WebView support."));
        }
    }
}
