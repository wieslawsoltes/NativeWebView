using System.Collections.ObjectModel;
using System.Text;

namespace NativeWebView.Core;

public readonly record struct NativeWebViewDiagnosticIssueReference(
    NativeWebViewPlatform Platform,
    string Code)
{
    public override string ToString()
    {
        return $"{Platform}|{Code}";
    }
}

public sealed class NativeWebViewDiagnosticsRegressionResult
{
    public NativeWebViewDiagnosticsRegressionResult(
        IReadOnlyList<NativeWebViewDiagnosticIssueReference> baselineBlockingIssues,
        IReadOnlyList<NativeWebViewDiagnosticIssueReference> currentBlockingIssues,
        IReadOnlyList<NativeWebViewDiagnosticIssueReference> newBlockingIssues,
        IReadOnlyList<NativeWebViewDiagnosticIssueReference> resolvedBlockingIssues)
    {
        ArgumentNullException.ThrowIfNull(baselineBlockingIssues);
        ArgumentNullException.ThrowIfNull(currentBlockingIssues);
        ArgumentNullException.ThrowIfNull(newBlockingIssues);
        ArgumentNullException.ThrowIfNull(resolvedBlockingIssues);

        BaselineBlockingIssues = new ReadOnlyCollection<NativeWebViewDiagnosticIssueReference>([.. baselineBlockingIssues]);
        CurrentBlockingIssues = new ReadOnlyCollection<NativeWebViewDiagnosticIssueReference>([.. currentBlockingIssues]);
        NewBlockingIssues = new ReadOnlyCollection<NativeWebViewDiagnosticIssueReference>([.. newBlockingIssues]);
        ResolvedBlockingIssues = new ReadOnlyCollection<NativeWebViewDiagnosticIssueReference>([.. resolvedBlockingIssues]);
    }

    public IReadOnlyList<NativeWebViewDiagnosticIssueReference> BaselineBlockingIssues { get; }

    public IReadOnlyList<NativeWebViewDiagnosticIssueReference> CurrentBlockingIssues { get; }

    public IReadOnlyList<NativeWebViewDiagnosticIssueReference> NewBlockingIssues { get; }

    public IReadOnlyList<NativeWebViewDiagnosticIssueReference> ResolvedBlockingIssues { get; }

    public bool HasRegression => NewBlockingIssues.Count > 0;

    public bool HasStaleBaseline => ResolvedBlockingIssues.Count > 0;

    public bool RequiresBaselineUpdate => HasRegression || HasStaleBaseline;
}

public static class NativeWebViewDiagnosticsRegressionAnalyzer
{
    public static IReadOnlyList<NativeWebViewDiagnosticIssueReference> GetBlockingIssues(
        NativeWebViewDiagnosticsReport report,
        bool? warningsAsErrors = null)
    {
        ArgumentNullException.ThrowIfNull(report);

        var effectiveWarningsAsErrors = warningsAsErrors ?? report.WarningsAsErrors;
        var minimumSeverity = effectiveWarningsAsErrors
            ? NativeWebViewDiagnosticSeverity.Warning
            : NativeWebViewDiagnosticSeverity.Error;

        var result = new HashSet<NativeWebViewDiagnosticIssueReference>();
        foreach (var platform in report.Platforms)
        {
            foreach (var issue in platform.Issues)
            {
                if (issue.Severity >= minimumSeverity)
                {
                    result.Add(new NativeWebViewDiagnosticIssueReference(platform.Platform, issue.Code));
                }
            }
        }

        return SortIssueReferences(result);
    }

    public static NativeWebViewDiagnosticsRegressionResult CompareBlockingIssues(
        IEnumerable<NativeWebViewDiagnosticIssueReference> baselineBlockingIssues,
        IEnumerable<NativeWebViewDiagnosticIssueReference> currentBlockingIssues)
    {
        ArgumentNullException.ThrowIfNull(baselineBlockingIssues);
        ArgumentNullException.ThrowIfNull(currentBlockingIssues);

        var baselineSet = baselineBlockingIssues.ToHashSet();
        var currentSet = currentBlockingIssues.ToHashSet();

        var newBlocking = currentSet
            .Where(issue => !baselineSet.Contains(issue))
            .ToArray();
        var resolvedBlocking = baselineSet
            .Where(issue => !currentSet.Contains(issue))
            .ToArray();

        return new NativeWebViewDiagnosticsRegressionResult(
            baselineBlockingIssues: SortIssueReferences(baselineSet),
            currentBlockingIssues: SortIssueReferences(currentSet),
            newBlockingIssues: SortIssueReferences(newBlocking),
            resolvedBlockingIssues: SortIssueReferences(resolvedBlocking));
    }

    public static IReadOnlyList<NativeWebViewDiagnosticIssueReference> ParseBaselineLines(
        IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var result = new HashSet<NativeWebViewDiagnosticIssueReference>();
        var lineNumber = 0;

        foreach (var rawLine in lines)
        {
            lineNumber++;
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('|');
            if (separatorIndex <= 0 || separatorIndex == line.Length - 1)
            {
                throw new FormatException($"Invalid baseline line at {lineNumber}: '{line}'. Expected '<Platform>|<Code>'.");
            }

            var platformText = line[..separatorIndex].Trim();
            var code = line[(separatorIndex + 1)..].Trim();

            if (!TryParsePlatform(platformText, out var platform))
            {
                throw new FormatException($"Invalid platform '{platformText}' at line {lineNumber}.");
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                throw new FormatException($"Invalid issue code at line {lineNumber}.");
            }

            result.Add(new NativeWebViewDiagnosticIssueReference(platform, code));
        }

        return SortIssueReferences(result);
    }

    public static string SerializeBaselineLines(
        IEnumerable<NativeWebViewDiagnosticIssueReference> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);

        var sorted = SortIssueReferences(issues);
        var builder = new StringBuilder();
        builder.AppendLine("# NativeWebView blocking diagnostics baseline");
        builder.AppendLine("# Format: <Platform>|<IssueCode>");

        foreach (var issue in sorted)
        {
            builder.Append(issue.Platform)
                .Append('|')
                .Append(issue.Code)
                .AppendLine();
        }

        return builder.ToString();
    }

    private static bool TryParsePlatform(string value, out NativeWebViewPlatform platform)
    {
        platform = value.Trim().ToLowerInvariant() switch
        {
            "windows" => NativeWebViewPlatform.Windows,
            "macos" => NativeWebViewPlatform.MacOS,
            "linux" => NativeWebViewPlatform.Linux,
            "ios" => NativeWebViewPlatform.IOS,
            "android" => NativeWebViewPlatform.Android,
            "browser" => NativeWebViewPlatform.Browser,
            "unknown" => NativeWebViewPlatform.Unknown,
            _ => NativeWebViewPlatform.Unknown,
        };

        return platform != NativeWebViewPlatform.Unknown || string.Equals(value, "unknown", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<NativeWebViewDiagnosticIssueReference> SortIssueReferences(
        IEnumerable<NativeWebViewDiagnosticIssueReference> issues)
    {
        return issues
            .OrderBy(static issue => issue.Platform)
            .ThenBy(static issue => issue.Code, StringComparer.Ordinal)
            .ToArray();
    }
}
