using System.Text;

namespace NativeWebView.Core;

public static class NativeWebViewDiagnosticsValidator
{
    public static bool IsReady(
        NativeWebViewPlatformDiagnostics diagnostics,
        bool warningsAsErrors = false)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        var minimumSeverity = warningsAsErrors
            ? NativeWebViewDiagnosticSeverity.Warning
            : NativeWebViewDiagnosticSeverity.Error;
        return diagnostics.Issues.All(issue => issue.Severity < minimumSeverity);
    }

    public static void EnsureReady(
        NativeWebViewPlatformDiagnostics diagnostics,
        bool warningsAsErrors = false)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        var minimumSeverity = warningsAsErrors
            ? NativeWebViewDiagnosticSeverity.Warning
            : NativeWebViewDiagnosticSeverity.Error;
        var blockingIssues = diagnostics.Issues
            .Where(issue => issue.Severity >= minimumSeverity)
            .ToArray();

        if (blockingIssues.Length == 0)
        {
            return;
        }

        var message = BuildFailureMessage(diagnostics, blockingIssues, warningsAsErrors);
        throw new InvalidOperationException(message);
    }

    private static string BuildFailureMessage(
        NativeWebViewPlatformDiagnostics diagnostics,
        IReadOnlyList<NativeWebViewDiagnosticIssue> blockingIssues,
        bool warningsAsErrors)
    {
        var builder = new StringBuilder();
        builder.Append("Platform diagnostics validation failed for '")
            .Append(diagnostics.Platform)
            .Append("' (provider: ")
            .Append(diagnostics.ProviderName)
            .Append(", warningsAsErrors=")
            .Append(warningsAsErrors)
            .Append(").");

        foreach (var issue in blockingIssues)
        {
            builder.Append(' ')
                .Append('[')
                .Append(issue.Severity)
                .Append("] ")
                .Append(issue.Code)
                .Append(": ")
                .Append(issue.Message);

            if (!string.IsNullOrWhiteSpace(issue.Recommendation))
            {
                builder.Append(" Recommendation: ")
                    .Append(issue.Recommendation);
            }
        }

        return builder.ToString();
    }
}
