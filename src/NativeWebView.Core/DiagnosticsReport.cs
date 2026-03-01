using System.Collections.ObjectModel;

namespace NativeWebView.Core;

public sealed class NativeWebViewDiagnosticsReport
{
    public NativeWebViewDiagnosticsReport(
        DateTimeOffset generatedAtUtc,
        bool warningsAsErrors,
        IReadOnlyList<NativeWebViewPlatformDiagnosticsReportEntry> platforms)
    {
        ArgumentNullException.ThrowIfNull(platforms);

        GeneratedAtUtc = generatedAtUtc;
        WarningsAsErrors = warningsAsErrors;
        Platforms = new ReadOnlyCollection<NativeWebViewPlatformDiagnosticsReportEntry>([.. platforms]);
    }

    public DateTimeOffset GeneratedAtUtc { get; }

    public bool WarningsAsErrors { get; }

    public IReadOnlyList<NativeWebViewPlatformDiagnosticsReportEntry> Platforms { get; }

    public bool IsReady => Platforms.All(static entry => entry.IsReady);

    public int IssueCount => Platforms.Sum(static entry => entry.IssueCount);

    public int WarningCount => Platforms.Sum(static entry => entry.WarningCount);

    public int ErrorCount => Platforms.Sum(static entry => entry.ErrorCount);

    public int BlockingIssueCount => Platforms.Sum(static entry => entry.BlockingIssueCount);
}

public sealed class NativeWebViewPlatformDiagnosticsReportEntry
{
    public NativeWebViewPlatformDiagnosticsReportEntry(
        NativeWebViewPlatformDiagnostics diagnostics,
        bool providerRegistered,
        bool warningsAsErrors)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        Platform = diagnostics.Platform;
        ProviderName = diagnostics.ProviderName;
        ProviderRegistered = providerRegistered;
        Issues = new ReadOnlyCollection<NativeWebViewDiagnosticIssue>([.. diagnostics.Issues]);

        IssueCount = Issues.Count;
        WarningCount = Issues.Count(static issue => issue.Severity == NativeWebViewDiagnosticSeverity.Warning);
        ErrorCount = Issues.Count(static issue => issue.Severity == NativeWebViewDiagnosticSeverity.Error);
        BlockingIssueCount = Issues.Count(issue => IsBlockingSeverity(issue.Severity, warningsAsErrors));
        IsReady = BlockingIssueCount == 0;
    }

    public NativeWebViewPlatform Platform { get; }

    public string ProviderName { get; }

    public bool ProviderRegistered { get; }

    public IReadOnlyList<NativeWebViewDiagnosticIssue> Issues { get; }

    public bool IsReady { get; }

    public int IssueCount { get; }

    public int WarningCount { get; }

    public int ErrorCount { get; }

    public int BlockingIssueCount { get; }

    private static bool IsBlockingSeverity(
        NativeWebViewDiagnosticSeverity severity,
        bool warningsAsErrors)
    {
        var minimumSeverity = warningsAsErrors
            ? NativeWebViewDiagnosticSeverity.Warning
            : NativeWebViewDiagnosticSeverity.Error;
        return severity >= minimumSeverity;
    }
}

public static class NativeWebViewDiagnosticsReporter
{
    private static readonly IReadOnlyList<NativeWebViewPlatform> DefaultPlatforms =
        Array.AsReadOnly(
        [
            NativeWebViewPlatform.Windows,
            NativeWebViewPlatform.MacOS,
            NativeWebViewPlatform.Linux,
            NativeWebViewPlatform.IOS,
            NativeWebViewPlatform.Android,
            NativeWebViewPlatform.Browser,
        ]);

    public static IReadOnlyList<NativeWebViewPlatform> GetDefaultPlatforms()
    {
        return DefaultPlatforms;
    }

    public static NativeWebViewDiagnosticsReport CreateReport(
        NativeWebViewBackendFactory factory,
        IEnumerable<NativeWebViewPlatform> platforms,
        bool warningsAsErrors = false)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(platforms);

        var uniquePlatforms = GetUniquePlatforms(platforms);
        if (uniquePlatforms.Count == 0)
        {
            throw new ArgumentException("At least one platform must be provided.", nameof(platforms));
        }

        var reportEntries = new List<NativeWebViewPlatformDiagnosticsReportEntry>(uniquePlatforms.Count);

        foreach (var platform in uniquePlatforms)
        {
            var providerRegistered = factory.TryGetPlatformDiagnostics(platform, out var diagnostics);
            reportEntries.Add(new NativeWebViewPlatformDiagnosticsReportEntry(diagnostics, providerRegistered, warningsAsErrors));
        }

        return new NativeWebViewDiagnosticsReport(
            generatedAtUtc: DateTimeOffset.UtcNow,
            warningsAsErrors,
            reportEntries);
    }

    private static IReadOnlyList<NativeWebViewPlatform> GetUniquePlatforms(IEnumerable<NativeWebViewPlatform> platforms)
    {
        var unique = new List<NativeWebViewPlatform>();
        var seen = new HashSet<NativeWebViewPlatform>();

        foreach (var platform in platforms)
        {
            if (!seen.Add(platform))
            {
                continue;
            }

            unique.Add(platform);
        }

        return unique;
    }
}
