using System.Text;

namespace NativeWebView.Core;

public static class NativeWebViewDiagnosticsRegressionMarkdownFormatter
{
    public static string Format(
        NativeWebViewDiagnosticsRegressionResult result,
        string? title = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        var resolvedTitle = string.IsNullOrWhiteSpace(title)
            ? "Blocking Diagnostics Regression Comparison"
            : EscapeText(title);

        var builder = new StringBuilder();
        builder.Append("## ")
            .AppendLine(resolvedTitle)
            .AppendLine();

        builder.Append("Baseline Blocking Issues: ")
            .Append(result.BaselineBlockingIssues.Count)
            .AppendLine();
        builder.Append("Current Blocking Issues: ")
            .Append(result.CurrentBlockingIssues.Count)
            .AppendLine();
        builder.Append("New Blocking Issues: ")
            .Append(result.NewBlockingIssues.Count)
            .AppendLine();
        builder.Append("Resolved Blocking Issues: ")
            .Append(result.ResolvedBlockingIssues.Count)
            .AppendLine();
        builder.Append("Has Regression: ")
            .Append(result.HasRegression)
            .AppendLine();
        builder.Append("Has Stale Baseline: ")
            .Append(result.HasStaleBaseline)
            .AppendLine();
        builder.Append("Requires Baseline Update: ")
            .Append(result.RequiresBaselineUpdate)
            .AppendLine();

        builder.AppendLine();
        builder.AppendLine("### New Blocking Issues");
        AppendIssueList(builder, result.NewBlockingIssues);

        builder.AppendLine();
        builder.AppendLine("### Resolved Blocking Issues");
        AppendIssueList(builder, result.ResolvedBlockingIssues);

        return builder.ToString();
    }

    private static void AppendIssueList(
        StringBuilder builder,
        IReadOnlyList<NativeWebViewDiagnosticIssueReference> issues)
    {
        if (issues.Count == 0)
        {
            builder.AppendLine("- None");
            return;
        }

        foreach (var issue in issues)
        {
            builder.Append("- `")
                .Append(issue.Platform)
                .Append('|')
                .Append(EscapeCode(issue.Code))
                .AppendLine("`");
        }
    }

    private static string EscapeCode(string value)
    {
        return EscapeText(value).Replace("`", "'", StringComparison.Ordinal);
    }

    private static string EscapeText(string value)
    {
        return value
            .Replace("\r", "", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
    }
}
