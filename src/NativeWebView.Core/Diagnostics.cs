using System.Collections.ObjectModel;

namespace NativeWebView.Core;

public enum NativeWebViewDiagnosticSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2
}

public sealed class NativeWebViewDiagnosticIssue
{
    public NativeWebViewDiagnosticIssue(
        string code,
        NativeWebViewDiagnosticSeverity severity,
        string message,
        string? recommendation = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Code = code;
        Severity = severity;
        Message = message;
        Recommendation = recommendation;
    }

    public string Code { get; }

    public NativeWebViewDiagnosticSeverity Severity { get; }

    public string Message { get; }

    public string? Recommendation { get; }
}

public sealed class NativeWebViewPlatformDiagnostics
{
    public static readonly NativeWebViewPlatformDiagnostics Unknown = new(
        NativeWebViewPlatform.Unknown,
        "unregistered",
        [
            new NativeWebViewDiagnosticIssue(
                code: "platform.unregistered",
                severity: NativeWebViewDiagnosticSeverity.Error,
                message: "No diagnostics provider is registered for this platform.",
                recommendation: "Register a platform package module before requesting diagnostics.")
        ]);

    public NativeWebViewPlatformDiagnostics(
        NativeWebViewPlatform platform,
        string providerName,
        IReadOnlyList<NativeWebViewDiagnosticIssue> issues)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentNullException.ThrowIfNull(issues);

        Platform = platform;
        ProviderName = providerName;
        Issues = new ReadOnlyCollection<NativeWebViewDiagnosticIssue>([.. issues]);
    }

    public NativeWebViewPlatform Platform { get; }

    public string ProviderName { get; }

    public IReadOnlyList<NativeWebViewDiagnosticIssue> Issues { get; }

    public bool IsReady => Issues.All(static issue => issue.Severity != NativeWebViewDiagnosticSeverity.Error);
}
