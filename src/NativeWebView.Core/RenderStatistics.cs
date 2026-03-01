namespace NativeWebView.Core;

public sealed class NativeWebViewRenderStatistics
{
    public NativeWebViewRenderStatistics(
        long captureAttemptCount,
        long captureSuccessCount,
        long captureFailureCount,
        long captureSkippedCount,
        long syntheticFrameCount,
        long nativeFrameCount,
        long lastFrameId,
        DateTimeOffset? lastFrameCapturedAtUtc,
        NativeWebViewRenderMode lastFrameRenderMode,
        NativeWebViewRenderFrameOrigin lastFrameOrigin,
        string? lastFailureMessage,
        DateTimeOffset? lastFailureAtUtc)
    {
        CaptureAttemptCount = captureAttemptCount;
        CaptureSuccessCount = captureSuccessCount;
        CaptureFailureCount = captureFailureCount;
        CaptureSkippedCount = captureSkippedCount;
        SyntheticFrameCount = syntheticFrameCount;
        NativeFrameCount = nativeFrameCount;
        LastFrameId = lastFrameId;
        LastFrameCapturedAtUtc = lastFrameCapturedAtUtc?.ToUniversalTime();
        LastFrameRenderMode = lastFrameRenderMode;
        LastFrameOrigin = lastFrameOrigin;
        LastFailureMessage = lastFailureMessage;
        LastFailureAtUtc = lastFailureAtUtc?.ToUniversalTime();
    }

    public long CaptureAttemptCount { get; }

    public long CaptureSuccessCount { get; }

    public long CaptureFailureCount { get; }

    public long CaptureSkippedCount { get; }

    public long SyntheticFrameCount { get; }

    public long NativeFrameCount { get; }

    public long LastFrameId { get; }

    public DateTimeOffset? LastFrameCapturedAtUtc { get; }

    public NativeWebViewRenderMode LastFrameRenderMode { get; }

    public NativeWebViewRenderFrameOrigin LastFrameOrigin { get; }

    public string? LastFailureMessage { get; }

    public DateTimeOffset? LastFailureAtUtc { get; }
}

public sealed class NativeWebViewRenderStatisticsTracker
{
    private readonly object _sync = new();

    private long _captureAttemptCount;
    private long _captureSuccessCount;
    private long _captureFailureCount;
    private long _captureSkippedCount;
    private long _syntheticFrameCount;
    private long _nativeFrameCount;
    private long _lastFrameId;
    private DateTimeOffset? _lastFrameCapturedAtUtc;
    private NativeWebViewRenderMode _lastFrameRenderMode = NativeWebViewRenderMode.Embedded;
    private NativeWebViewRenderFrameOrigin _lastFrameOrigin = NativeWebViewRenderFrameOrigin.Unknown;
    private string? _lastFailureMessage;
    private DateTimeOffset? _lastFailureAtUtc;

    public void MarkCaptureAttempt()
    {
        lock (_sync)
        {
            _captureAttemptCount++;
        }
    }

    public void MarkCaptureSkipped(string? reason = null)
    {
        _ = reason;

        lock (_sync)
        {
            _captureSkippedCount++;
        }
    }

    public void MarkCaptureFailure(string failureMessage, DateTimeOffset? atUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureMessage);

        lock (_sync)
        {
            _captureFailureCount++;
            _lastFailureMessage = failureMessage;
            _lastFailureAtUtc = (atUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        }
    }

    public void MarkCaptureSuccess(NativeWebViewRenderFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        lock (_sync)
        {
            _captureSuccessCount++;
            _lastFrameId = frame.FrameId;
            _lastFrameCapturedAtUtc = frame.CapturedAtUtc.ToUniversalTime();
            _lastFrameRenderMode = frame.RenderMode;
            _lastFrameOrigin = frame.Origin;
            _lastFailureMessage = null;
            _lastFailureAtUtc = null;

            if (frame.Origin == NativeWebViewRenderFrameOrigin.NativeCapture)
            {
                _nativeFrameCount++;
            }
            else if (frame.Origin == NativeWebViewRenderFrameOrigin.SyntheticFallback || frame.IsSynthetic)
            {
                _syntheticFrameCount++;
            }
        }
    }

    public NativeWebViewRenderStatistics CreateSnapshot()
    {
        lock (_sync)
        {
            return new NativeWebViewRenderStatistics(
                _captureAttemptCount,
                _captureSuccessCount,
                _captureFailureCount,
                _captureSkippedCount,
                _syntheticFrameCount,
                _nativeFrameCount,
                _lastFrameId,
                _lastFrameCapturedAtUtc,
                _lastFrameRenderMode,
                _lastFrameOrigin,
                _lastFailureMessage,
                _lastFailureAtUtc);
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _captureAttemptCount = 0;
            _captureSuccessCount = 0;
            _captureFailureCount = 0;
            _captureSkippedCount = 0;
            _syntheticFrameCount = 0;
            _nativeFrameCount = 0;
            _lastFrameId = 0;
            _lastFrameCapturedAtUtc = null;
            _lastFrameRenderMode = NativeWebViewRenderMode.Embedded;
            _lastFrameOrigin = NativeWebViewRenderFrameOrigin.Unknown;
            _lastFailureMessage = null;
            _lastFailureAtUtc = null;
        }
    }
}
