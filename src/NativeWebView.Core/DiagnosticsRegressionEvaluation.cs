using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;

namespace NativeWebView.Core;

public sealed class NativeWebViewDiagnosticsGateFailure
{
    public NativeWebViewDiagnosticsGateFailure(
        NativeWebViewDiagnosticsGateFailureKind kind,
        int exitCode,
        string message,
        string recommendation)
    {
        Kind = kind;
        ExitCode = exitCode;
        Message = string.IsNullOrWhiteSpace(message)
            ? throw new ArgumentException("Gate failure message must not be empty.", nameof(message))
            : message;
        Recommendation = string.IsNullOrWhiteSpace(recommendation)
            ? throw new ArgumentException("Gate failure recommendation must not be empty.", nameof(recommendation))
            : recommendation;
    }

    public NativeWebViewDiagnosticsGateFailureKind Kind { get; }

    public int ExitCode { get; }

    public string Message { get; }

    public string Recommendation { get; }
}

public enum NativeWebViewDiagnosticsGateFailureKind
{
    RequireReady,
    Regression,
    BaselineSync,
}

public sealed class NativeWebViewDiagnosticsRegressionEvaluation
{
    public const int CurrentFingerprintVersion = 1;

    private readonly IReadOnlyList<NativeWebViewDiagnosticsGateFailureKind> _failingGates;
    private readonly IReadOnlyList<NativeWebViewDiagnosticsGateFailure> _gateFailures;

    public NativeWebViewDiagnosticsRegressionEvaluation(
        DateTimeOffset generatedAtUtc,
        bool warningsAsErrors,
        bool requireReady,
        bool failOnRegression,
        bool requireBaselineSync,
        bool isReady,
        NativeWebViewDiagnosticsRegressionResult? comparison)
    {
        GeneratedAtUtc = generatedAtUtc;
        WarningsAsErrors = warningsAsErrors;
        RequireReady = requireReady;
        FailOnRegression = failOnRegression;
        RequireBaselineSync = requireBaselineSync;
        IsReady = isReady;
        Comparison = comparison;
        _failingGates = new ReadOnlyCollection<NativeWebViewDiagnosticsGateFailureKind>(GetFailingGates().ToArray());
        _gateFailures = new ReadOnlyCollection<NativeWebViewDiagnosticsGateFailure>(_failingGates
            .Select(static gate => new NativeWebViewDiagnosticsGateFailure(
                gate,
                GetExitCodeForGate(gate),
                GetGateFailureMessage(gate),
                GetGateFailureRecommendation(gate)))
            .ToArray());
        Fingerprint = ComputeFingerprint(
            CurrentFingerprintVersion,
            warningsAsErrors,
            requireReady,
            failOnRegression,
            requireBaselineSync,
            isReady,
            comparison,
            _failingGates);
    }

    public DateTimeOffset GeneratedAtUtc { get; }

    public bool WarningsAsErrors { get; }

    public bool RequireReady { get; }

    public bool FailOnRegression { get; }

    public bool RequireBaselineSync { get; }

    public bool IsReady { get; }

    public NativeWebViewDiagnosticsRegressionResult? Comparison { get; }

    public bool HasComparison => Comparison is not null;

    public bool HasRegression => Comparison?.HasRegression ?? false;

    public bool HasStaleBaseline => Comparison?.HasStaleBaseline ?? false;

    public bool RequiresBaselineUpdate => Comparison?.RequiresBaselineUpdate ?? false;

    public bool WouldFailRequireReady => RequireReady && !IsReady;

    public bool WouldFailRegressionGate => FailOnRegression && HasRegression;

    public bool WouldFailBaselineSyncGate => RequireBaselineSync && HasStaleBaseline;

    public IReadOnlyList<NativeWebViewDiagnosticsGateFailureKind> FailingGates => _failingGates;

    public IReadOnlyList<NativeWebViewDiagnosticsGateFailure> GateFailures => _gateFailures;

    public bool HasMultipleGateFailures => _failingGates.Count > 1;

    public NativeWebViewDiagnosticsGateFailureKind? PrimaryFailingGate =>
        _failingGates.Count == 1
            ? _failingGates[0]
            : null;

    public int EffectiveExitCode =>
        _failingGates.Count switch
        {
            0 => 0,
            > 1 => 13,
            _ => GetExitCodeForGate(_failingGates[0]),
        };

    public string Fingerprint { get; }

    public int FingerprintVersion => CurrentFingerprintVersion;

    public static int GetExitCodeForGate(NativeWebViewDiagnosticsGateFailureKind gate)
    {
        return gate switch
        {
            NativeWebViewDiagnosticsGateFailureKind.RequireReady => 10,
            NativeWebViewDiagnosticsGateFailureKind.Regression => 11,
            NativeWebViewDiagnosticsGateFailureKind.BaselineSync => 12,
            _ => throw new ArgumentOutOfRangeException(nameof(gate), gate, "Unsupported diagnostics gate failure kind."),
        };
    }

    public static string GetGateFailureMessage(NativeWebViewDiagnosticsGateFailureKind gate)
    {
        return gate switch
        {
            NativeWebViewDiagnosticsGateFailureKind.RequireReady =>
                "Blocking diagnostics issues were found and --require-ready is enabled.",
            NativeWebViewDiagnosticsGateFailureKind.Regression =>
                "Blocking diagnostics regressions were found and regression gating is enabled.",
            NativeWebViewDiagnosticsGateFailureKind.BaselineSync =>
                "Blocking diagnostics baseline contains resolved entries and --require-baseline-sync is enabled.",
            _ => throw new ArgumentOutOfRangeException(nameof(gate), gate, "Unsupported diagnostics gate failure kind."),
        };
    }

    public static string GetGateFailureRecommendation(NativeWebViewDiagnosticsGateFailureKind gate)
    {
        return gate switch
        {
            NativeWebViewDiagnosticsGateFailureKind.RequireReady =>
                "Fix blocking diagnostics issues or run with --allow-not-ready when collecting non-gating reports.",
            NativeWebViewDiagnosticsGateFailureKind.Regression =>
                "Resolve newly introduced blocking issues or run with --allow-regression when triaging intentional changes.",
            NativeWebViewDiagnosticsGateFailureKind.BaselineSync =>
                "Refresh baseline using ./scripts/update-blocking-baseline.sh when resolved entries are intentional.",
            _ => throw new ArgumentOutOfRangeException(nameof(gate), gate, "Unsupported diagnostics gate failure kind."),
        };
    }

    private IEnumerable<NativeWebViewDiagnosticsGateFailureKind> GetFailingGates()
    {
        if (WouldFailRequireReady)
        {
            yield return NativeWebViewDiagnosticsGateFailureKind.RequireReady;
        }

        if (WouldFailRegressionGate)
        {
            yield return NativeWebViewDiagnosticsGateFailureKind.Regression;
        }

        if (WouldFailBaselineSyncGate)
        {
            yield return NativeWebViewDiagnosticsGateFailureKind.BaselineSync;
        }
    }

    private static string ComputeFingerprint(
        int fingerprintVersion,
        bool warningsAsErrors,
        bool requireReady,
        bool failOnRegression,
        bool requireBaselineSync,
        bool isReady,
        NativeWebViewDiagnosticsRegressionResult? comparison,
        IReadOnlyList<NativeWebViewDiagnosticsGateFailureKind> failingGates)
    {
        var builder = new StringBuilder();
        builder.Append("fingerprintVersion=")
            .Append(fingerprintVersion)
            .AppendLine();
        builder.Append("warningsAsErrors=")
            .Append(warningsAsErrors)
            .AppendLine();
        builder.Append("requireReady=")
            .Append(requireReady)
            .AppendLine();
        builder.Append("failOnRegression=")
            .Append(failOnRegression)
            .AppendLine();
        builder.Append("requireBaselineSync=")
            .Append(requireBaselineSync)
            .AppendLine();
        builder.Append("isReady=")
            .Append(isReady)
            .AppendLine();

        if (comparison is null)
        {
            builder.AppendLine("hasComparison=false");
        }
        else
        {
            builder.AppendLine("hasComparison=true");
            AppendIssueReferences(builder, "baseline", comparison.BaselineBlockingIssues);
            AppendIssueReferences(builder, "current", comparison.CurrentBlockingIssues);
            AppendIssueReferences(builder, "new", comparison.NewBlockingIssues);
            AppendIssueReferences(builder, "resolved", comparison.ResolvedBlockingIssues);
        }

        foreach (var gate in failingGates)
        {
            builder.Append("gate=")
                .Append(gate)
                .Append('|')
                .Append(GetExitCodeForGate(gate))
                .AppendLine();
        }

        builder.Append("effectiveExitCode=")
            .Append(failingGates.Count switch
            {
                0 => 0,
                > 1 => 13,
                _ => GetExitCodeForGate(failingGates[0]),
            })
            .AppendLine();

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void AppendIssueReferences(
        StringBuilder builder,
        string category,
        IReadOnlyList<NativeWebViewDiagnosticIssueReference> issues)
    {
        foreach (var issue in issues
            .OrderBy(static issue => issue.Platform)
            .ThenBy(static issue => issue.Code, StringComparer.Ordinal))
        {
            builder.Append(category)
                .Append('=')
                .Append(issue.Platform)
                .Append('|')
                .Append(issue.Code)
                .AppendLine();
        }
    }
}

public static class NativeWebViewDiagnosticsRegressionEvaluator
{
    public static NativeWebViewDiagnosticsRegressionEvaluation Evaluate(
        NativeWebViewDiagnosticsReport report,
        NativeWebViewDiagnosticsRegressionResult? comparison,
        bool requireReady = false,
        bool failOnRegression = true,
        bool requireBaselineSync = false)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (requireBaselineSync && comparison is null)
        {
            throw new ArgumentException(
                "Baseline sync policy requires a baseline comparison result.",
                nameof(comparison));
        }

        return new NativeWebViewDiagnosticsRegressionEvaluation(
            generatedAtUtc: DateTimeOffset.UtcNow,
            warningsAsErrors: report.WarningsAsErrors,
            requireReady,
            failOnRegression,
            requireBaselineSync,
            isReady: report.IsReady,
            comparison);
    }
}
