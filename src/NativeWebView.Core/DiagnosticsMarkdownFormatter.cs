using System.Text;

namespace NativeWebView.Core;

public static class NativeWebViewDiagnosticsMarkdownFormatter
{
    public static string FormatReport(
        NativeWebViewDiagnosticsReport report,
        string? title = null)
    {
        ArgumentNullException.ThrowIfNull(report);

        var resolvedTitle = string.IsNullOrWhiteSpace(title)
            ? "Platform Diagnostics Summary"
            : EscapeHeading(title);

        var builder = new StringBuilder();
        builder.Append("## ")
            .AppendLine(resolvedTitle)
            .AppendLine();

        builder.Append("Generated (UTC): ")
            .Append(report.GeneratedAtUtc.ToString("u"))
            .AppendLine();
        builder.Append("Warnings As Errors: ")
            .Append(report.WarningsAsErrors)
            .AppendLine();
        builder.Append("Overall Ready: ")
            .Append(report.IsReady)
            .AppendLine();
        builder.Append("Platforms: ")
            .Append(report.Platforms.Count)
            .Append(", Issues: ")
            .Append(report.IssueCount)
            .Append(", Blocking: ")
            .Append(report.BlockingIssueCount)
            .AppendLine();
        builder.AppendLine();

        builder.AppendLine("| Platform | Ready | Provider | Registered | Warnings | Errors | Blocking |");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");
        foreach (var platform in report.Platforms)
        {
            builder.Append("| ")
                .Append(EscapeCell(platform.Platform.ToString()))
                .Append(" | ")
                .Append(platform.IsReady)
                .Append(" | ")
                .Append(EscapeCell(platform.ProviderName))
                .Append(" | ")
                .Append(platform.ProviderRegistered)
                .Append(" | ")
                .Append(platform.WarningCount)
                .Append(" | ")
                .Append(platform.ErrorCount)
                .Append(" | ")
                .Append(platform.BlockingIssueCount)
                .AppendLine(" |");
        }

        foreach (var platform in report.Platforms)
        {
            builder.AppendLine();
            builder.Append("### ")
                .Append(EscapeHeading(platform.Platform.ToString()))
                .AppendLine();

            foreach (var issue in platform.Issues)
            {
                builder.Append("- [")
                    .Append(issue.Severity)
                    .Append("] `")
                    .Append(EscapeCode(issue.Code))
                    .Append("`: ")
                    .Append(EscapeText(issue.Message));

                if (!string.IsNullOrWhiteSpace(issue.Recommendation))
                {
                    builder.Append(" Recommendation: ")
                        .Append(EscapeText(issue.Recommendation));
                }

                builder.AppendLine();
            }

            if (platform.Issues.Count == 0)
            {
                builder.AppendLine("- No issues reported.");
            }
        }

        return builder.ToString();
    }

    private static string EscapeCell(string value)
    {
        return EscapeText(value).Replace("|", "\\|", StringComparison.Ordinal);
    }

    private static string EscapeHeading(string value)
    {
        return EscapeText(value).Replace("\n", " ", StringComparison.Ordinal);
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
