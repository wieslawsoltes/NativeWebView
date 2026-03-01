using System.Globalization;
using System.Text;

namespace NativeWebView.Core;

public static class NativeWebViewDiagnosticsRegressionEvaluationMarkdownFormatter
{
    public static string Format(
        NativeWebViewDiagnosticsRegressionEvaluation evaluation,
        string? title = null)
    {
        ArgumentNullException.ThrowIfNull(evaluation);

        var resolvedTitle = string.IsNullOrWhiteSpace(title)
            ? "Blocking Diagnostics Gate Evaluation"
            : EscapeText(title);

        var builder = new StringBuilder();
        builder.Append("## ")
            .AppendLine(resolvedTitle)
            .AppendLine();

        builder.Append("Generated At (UTC): ")
            .AppendLine(evaluation.GeneratedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        builder.Append("Warnings As Errors: ")
            .Append(evaluation.WarningsAsErrors)
            .AppendLine();
        builder.Append("Require Ready: ")
            .Append(evaluation.RequireReady)
            .AppendLine();
        builder.Append("Fail On Regression: ")
            .Append(evaluation.FailOnRegression)
            .AppendLine();
        builder.Append("Require Baseline Sync: ")
            .Append(evaluation.RequireBaselineSync)
            .AppendLine();
        builder.Append("Is Ready: ")
            .Append(evaluation.IsReady)
            .AppendLine();
        builder.Append("Has Comparison: ")
            .Append(evaluation.HasComparison)
            .AppendLine();
        builder.Append("Has Regression: ")
            .Append(evaluation.HasRegression)
            .AppendLine();
        builder.Append("Has Stale Baseline: ")
            .Append(evaluation.HasStaleBaseline)
            .AppendLine();
        builder.Append("Requires Baseline Update: ")
            .Append(evaluation.RequiresBaselineUpdate)
            .AppendLine();
        builder.Append("Has Multiple Gate Failures: ")
            .Append(evaluation.HasMultipleGateFailures)
            .AppendLine();
        builder.Append("Primary Failing Gate: ")
            .AppendLine(evaluation.PrimaryFailingGate?.ToString() ?? "None");
        builder.Append("Effective Exit Code: ")
            .Append(evaluation.EffectiveExitCode)
            .AppendLine();
        builder.Append("Fingerprint Version: ")
            .Append(evaluation.FingerprintVersion)
            .AppendLine();
        builder.Append("Fingerprint: ")
            .Append(evaluation.Fingerprint)
            .AppendLine();

        if (evaluation.Comparison is not null)
        {
            builder.AppendLine();
            builder.AppendLine("### Comparison Snapshot");
            builder.Append("Baseline Blocking Issues: ")
                .Append(evaluation.Comparison.BaselineBlockingIssues.Count)
                .AppendLine();
            builder.Append("Current Blocking Issues: ")
                .Append(evaluation.Comparison.CurrentBlockingIssues.Count)
                .AppendLine();
            builder.Append("New Blocking Issues: ")
                .Append(evaluation.Comparison.NewBlockingIssues.Count)
                .AppendLine();
            builder.Append("Resolved Blocking Issues: ")
                .Append(evaluation.Comparison.ResolvedBlockingIssues.Count)
                .AppendLine();
        }

        builder.AppendLine();
        builder.AppendLine("### Failing Gates");
        AppendFailingGates(builder, evaluation.GateFailures);

        return builder.ToString();
    }

    private static void AppendFailingGates(
        StringBuilder builder,
        IReadOnlyList<NativeWebViewDiagnosticsGateFailure> gateFailures)
    {
        if (gateFailures.Count == 0)
        {
            builder.AppendLine("- None");
            return;
        }

        foreach (var gateFailure in gateFailures)
        {
            builder.Append("- `")
                .Append(gateFailure.Kind)
                .Append("` (")
                .Append(gateFailure.ExitCode)
                .Append("): ")
                .Append(gateFailure.Message)
                .Append(" Recommendation: ")
                .AppendLine(gateFailure.Recommendation);
        }
    }

    private static string EscapeText(string value)
    {
        return value
            .Replace("\r", "", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
    }
}
