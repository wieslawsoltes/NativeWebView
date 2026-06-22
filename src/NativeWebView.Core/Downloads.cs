using System.Collections.ObjectModel;
using System.Diagnostics;

namespace NativeWebView.Core;

public enum NativeWebViewDownloadState
{
    Created = 0,
    WaitingForDestination,
    InProgress,
    Paused,
    Completed,
    Canceled,
    Failed
}

public enum NativeWebViewDownloadActionStatus
{
    Success = 0,
    Unsupported,
    InvalidState,
    Failed
}

public sealed class NativeWebViewDownloadActionResult
{
    public NativeWebViewDownloadActionResult(NativeWebViewDownloadActionStatus status, string? errorMessage = null)
    {
        Status = status;
        ErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? null : errorMessage;
    }

    public NativeWebViewDownloadActionStatus Status { get; }

    public string? ErrorMessage { get; }

    public bool IsSuccess => Status == NativeWebViewDownloadActionStatus.Success;

    public static NativeWebViewDownloadActionResult Success() => new(NativeWebViewDownloadActionStatus.Success);

    public static NativeWebViewDownloadActionResult Unsupported(string? message = null) =>
        new(NativeWebViewDownloadActionStatus.Unsupported, message);

    public static NativeWebViewDownloadActionResult InvalidState(string? message = null) =>
        new(NativeWebViewDownloadActionStatus.InvalidState, message);

    public static NativeWebViewDownloadActionResult Failed(string? message = null) =>
        new(NativeWebViewDownloadActionStatus.Failed, message);
}

public sealed class NativeWebViewDownloadRequestOptions
{
    public string? SuggestedFileName { get; set; }

    public string? DestinationPath { get; set; }

    public bool AllowOverwrite { get; set; }

    public string? MimeType { get; set; }

    public string? ContentDisposition { get; set; }

    public long? TotalBytesToReceive { get; set; }
}

public sealed class NativeWebViewDownloadSnapshot
{
    internal NativeWebViewDownloadSnapshot(
        Guid id,
        Guid? restartedFromId,
        Uri uri,
        string? suggestedFileName,
        string? destinationPath,
        string? mimeType,
        string? contentDisposition,
        NativeWebViewDownloadState state,
        long bytesReceived,
        long? totalBytesToReceive,
        double? progress,
        double? transferRateBytesPerSecond,
        DateTimeOffset createdAtUtc,
        DateTimeOffset? startedAtUtc,
        DateTimeOffset? completedAtUtc,
        string? errorMessage,
        string? errorCode,
        bool canPause,
        bool canResume,
        bool canCancel,
        bool canRestart)
    {
        Id = id;
        RestartedFromId = restartedFromId;
        Uri = uri;
        SuggestedFileName = suggestedFileName;
        DestinationPath = destinationPath;
        MimeType = mimeType;
        ContentDisposition = contentDisposition;
        State = state;
        BytesReceived = bytesReceived;
        TotalBytesToReceive = totalBytesToReceive;
        Progress = progress;
        TransferRateBytesPerSecond = transferRateBytesPerSecond;
        CreatedAtUtc = createdAtUtc;
        StartedAtUtc = startedAtUtc;
        CompletedAtUtc = completedAtUtc;
        ErrorMessage = errorMessage;
        ErrorCode = errorCode;
        CanPause = canPause;
        CanResume = canResume;
        CanCancel = canCancel;
        CanRestart = canRestart;
    }

    public Guid Id { get; }

    public Guid? RestartedFromId { get; }

    public Uri Uri { get; }

    public string? SuggestedFileName { get; }

    public string? DestinationPath { get; }

    public string? MimeType { get; }

    public string? ContentDisposition { get; }

    public NativeWebViewDownloadState State { get; }

    public long BytesReceived { get; }

    public long? TotalBytesToReceive { get; }

    public double? Progress { get; }

    public double? TransferRateBytesPerSecond { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset? StartedAtUtc { get; }

    public DateTimeOffset? CompletedAtUtc { get; }

    public string? ErrorMessage { get; }

    public string? ErrorCode { get; }

    public bool CanPause { get; }

    public bool CanResume { get; }

    public bool CanCancel { get; }

    public bool CanRestart { get; }
}

public sealed class NativeWebViewDownloadStartingEventArgs : EventArgs
{
    private readonly List<NativeWebViewDeferral> _deferrals = [];

    internal NativeWebViewDownloadStartingEventArgs(INativeWebViewDownloadItem item)
    {
        Item = item;
        var snapshot = item.Snapshot;
        Uri = snapshot.Uri;
        SuggestedFileName = snapshot.SuggestedFileName;
        MimeType = snapshot.MimeType;
        ContentDisposition = snapshot.ContentDisposition;
        TotalBytesToReceive = snapshot.TotalBytesToReceive;
        DestinationPath = snapshot.DestinationPath;
    }

    public INativeWebViewDownloadItem Item { get; }

    public Uri Uri { get; }

    public string? SuggestedFileName { get; }

    public string? MimeType { get; }

    public string? ContentDisposition { get; }

    public long? TotalBytesToReceive { get; }

    public string? DestinationPath { get; set; }

    public bool AllowOverwrite { get; set; }

    public bool Cancel { get; set; }

    public NativeWebViewDeferral GetDeferral()
    {
        var deferral = new NativeWebViewDeferral();
        _deferrals.Add(deferral);
        return deferral;
    }

    internal async Task WaitForDeferralsAsync(CancellationToken cancellationToken)
    {
        foreach (var deferral in _deferrals.ToArray())
        {
            await deferral.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}

public sealed class NativeWebViewDownloadItemEventArgs : EventArgs
{
    public NativeWebViewDownloadItemEventArgs(INativeWebViewDownloadItem item)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
        Snapshot = item.Snapshot;
    }

    public INativeWebViewDownloadItem Item { get; }

    public NativeWebViewDownloadSnapshot Snapshot { get; }
}

public sealed class NativeWebViewDeferral : IDisposable
{
    private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _completed;

    public void Complete()
    {
        if (Interlocked.Exchange(ref _completed, 1) == 0)
        {
            _completion.TrySetResult(true);
        }
    }

    public void Dispose()
    {
        Complete();
    }

    internal Task WaitAsync(CancellationToken cancellationToken)
    {
        return cancellationToken.CanBeCanceled
            ? _completion.Task.WaitAsync(cancellationToken)
            : _completion.Task;
    }
}

public sealed class NativeWebViewDownloadNativeOperation
{
    public Func<CancellationToken, Task<NativeWebViewDownloadActionResult>>? PauseAsync { get; set; }

    public Func<CancellationToken, Task<NativeWebViewDownloadActionResult>>? ResumeAsync { get; set; }

    public Func<CancellationToken, Task<NativeWebViewDownloadActionResult>>? CancelAsync { get; set; }

    public Func<CancellationToken, Task<NativeWebViewDownloadActionResult>>? RestartAsync { get; set; }
}

public sealed class NativeWebViewDownloadManager : INativeWebViewDownloadManager
{
    private readonly List<NativeWebViewDownloadItem> _items = [];
    private readonly object _gate = new();
    private readonly Func<Uri, NativeWebViewDownloadRequestOptions?, CancellationToken, Task<INativeWebViewDownloadItem>>? _startDownload;

    public NativeWebViewDownloadManager(
        Func<Uri, NativeWebViewDownloadRequestOptions?, CancellationToken, Task<INativeWebViewDownloadItem>>? startDownload = null)
    {
        _startDownload = startDownload;
    }

    public event EventHandler<NativeWebViewDownloadStartingEventArgs>? DownloadStarting;

    public event EventHandler<NativeWebViewDownloadItemEventArgs>? DownloadStarted;

    public event EventHandler<NativeWebViewDownloadItemEventArgs>? DownloadChanged;

    public event EventHandler<NativeWebViewDownloadItemEventArgs>? DownloadCompleted;

    public IReadOnlyList<INativeWebViewDownloadItem> Items
    {
        get
        {
            lock (_gate)
            {
                return new ReadOnlyCollection<INativeWebViewDownloadItem>(_items.Cast<INativeWebViewDownloadItem>().ToArray());
            }
        }
    }

    public bool TryGetItem(Guid id, out INativeWebViewDownloadItem? item)
    {
        lock (_gate)
        {
            item = _items.FirstOrDefault(candidate => candidate.Snapshot.Id == id);
            return item is not null;
        }
    }

    public void ClearCompleted()
    {
        lock (_gate)
        {
            _items.RemoveAll(static item => item.Snapshot.State is
                NativeWebViewDownloadState.Completed or
                NativeWebViewDownloadState.Canceled or
                NativeWebViewDownloadState.Failed);
        }
    }

    public Task<INativeWebViewDownloadItem> StartDownloadAsync(
        Uri uri,
        NativeWebViewDownloadRequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        cancellationToken.ThrowIfCancellationRequested();

        return _startDownload is null
            ? Task.FromResult<INativeWebViewDownloadItem>(
                CreateFailedDownload(
                    uri,
                    options,
                    "Programmatic download start is not supported by this backend.",
                    "Unsupported"))
            : _startDownload(uri, options, cancellationToken);
    }

    public async Task<NativeWebViewDownloadStartingEventArgs> PrepareDownloadAsync(
        Uri uri,
        NativeWebViewDownloadRequestOptions? options,
        NativeWebViewDownloadNativeOperation nativeOperation,
        Guid? restartedFromId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(nativeOperation);
        cancellationToken.ThrowIfCancellationRequested();

        var item = AddItem(uri, options, nativeOperation, restartedFromId);
        item.UpdateState(NativeWebViewDownloadState.WaitingForDestination, notify: false);

        var args = new NativeWebViewDownloadStartingEventArgs(item)
        {
            AllowOverwrite = options?.AllowOverwrite == true,
        };

        DownloadStarting?.Invoke(this, args);
        await args.WaitForDeferralsAsync(cancellationToken).ConfigureAwait(false);

        item.SetDestination(args.DestinationPath, args.AllowOverwrite);
        if (args.Cancel)
        {
            item.MarkCanceled("Download was canceled before it started.");
        }

        return args;
    }

    public NativeWebViewDownloadItem AddStartedDownload(
        Uri uri,
        NativeWebViewDownloadRequestOptions? options,
        NativeWebViewDownloadNativeOperation nativeOperation,
        Guid? restartedFromId = null)
    {
        var item = AddItem(uri, options, nativeOperation, restartedFromId);
        item.MarkStarted();
        return item;
    }

    private NativeWebViewDownloadItem CreateFailedDownload(
        Uri uri,
        NativeWebViewDownloadRequestOptions? options,
        string errorMessage,
        string errorCode)
    {
        var item = AddItem(uri, options, new NativeWebViewDownloadNativeOperation(), restartedFromId: null);
        item.MarkFailed(errorMessage, errorCode);
        return item;
    }

    private NativeWebViewDownloadItem AddItem(
        Uri uri,
        NativeWebViewDownloadRequestOptions? options,
        NativeWebViewDownloadNativeOperation nativeOperation,
        Guid? restartedFromId)
    {
        var item = new NativeWebViewDownloadItem(
            this,
            Guid.NewGuid(),
            restartedFromId,
            uri,
            options,
            nativeOperation);

        lock (_gate)
        {
            _items.Add(item);
        }

        return item;
    }

    private void RaiseStarted(NativeWebViewDownloadItem item) =>
        InvokeSafely(DownloadStarted, new NativeWebViewDownloadItemEventArgs(item));

    private void RaiseChanged(NativeWebViewDownloadItem item) =>
        InvokeSafely(DownloadChanged, new NativeWebViewDownloadItemEventArgs(item));

    private void RaiseCompleted(NativeWebViewDownloadItem item) =>
        InvokeSafely(DownloadCompleted, new NativeWebViewDownloadItemEventArgs(item));

    private void InvokeSafely<TEventArgs>(EventHandler<TEventArgs>? handler, TEventArgs args)
        where TEventArgs : EventArgs
    {
        if (handler is null)
        {
            return;
        }

        foreach (EventHandler<TEventArgs> subscriber in handler.GetInvocationList().Cast<EventHandler<TEventArgs>>())
        {
            try
            {
                subscriber(this, args);
            }
            catch
            {
                // Consumer event handlers must not fail or interrupt native download operations.
            }
        }
    }

    public sealed class NativeWebViewDownloadItem : INativeWebViewDownloadItem
    {
        private readonly NativeWebViewDownloadManager _owner;
        private readonly NativeWebViewDownloadNativeOperation _nativeOperation;
        private readonly Stopwatch _stopwatch = new();
        private readonly object _gate = new();

        private NativeWebViewDownloadSnapshot _snapshot;
        private bool _allowOverwrite;

        internal NativeWebViewDownloadItem(
            NativeWebViewDownloadManager owner,
            Guid id,
            Guid? restartedFromId,
            Uri uri,
            NativeWebViewDownloadRequestOptions? options,
            NativeWebViewDownloadNativeOperation nativeOperation)
        {
            _owner = owner;
            _nativeOperation = nativeOperation;
            _allowOverwrite = options?.AllowOverwrite == true;
            _snapshot = new NativeWebViewDownloadSnapshot(
                id,
                restartedFromId,
                uri,
                options?.SuggestedFileName,
                options?.DestinationPath,
                options?.MimeType,
                options?.ContentDisposition,
                NativeWebViewDownloadState.Created,
                bytesReceived: 0,
                options?.TotalBytesToReceive,
                progress: 0,
                transferRateBytesPerSecond: null,
                DateTimeOffset.UtcNow,
                startedAtUtc: null,
                completedAtUtc: null,
                errorMessage: null,
                errorCode: null,
                canPause: nativeOperation.PauseAsync is not null,
                canResume: nativeOperation.ResumeAsync is not null,
                canCancel: nativeOperation.CancelAsync is not null,
                canRestart: nativeOperation.RestartAsync is not null);
        }

        public NativeWebViewDownloadSnapshot Snapshot
        {
            get
            {
                lock (_gate)
                {
                    return _snapshot;
                }
            }
        }

        public Task<NativeWebViewDownloadActionResult> PauseAsync(CancellationToken cancellationToken = default) =>
            InvokeAsync(_nativeOperation.PauseAsync, "Pause is not supported for this download.", cancellationToken);

        public Task<NativeWebViewDownloadActionResult> ResumeAsync(CancellationToken cancellationToken = default) =>
            InvokeAsync(_nativeOperation.ResumeAsync, "Resume is not supported for this download.", cancellationToken);

        public Task<NativeWebViewDownloadActionResult> CancelAsync(CancellationToken cancellationToken = default) =>
            InvokeAsync(_nativeOperation.CancelAsync, "Cancel is not supported for this download.", cancellationToken);

        public Task<NativeWebViewDownloadActionResult> RestartAsync(CancellationToken cancellationToken = default) =>
            InvokeAsync(_nativeOperation.RestartAsync, "Restart is not supported for this download.", cancellationToken);

        public bool AllowOverwrite => _allowOverwrite;

        public void SetDestination(string? destinationPath, bool allowOverwrite)
        {
            _allowOverwrite = allowOverwrite;
            Update(destinationPath: NormalizePath(destinationPath));
        }

        public void MarkStarted()
        {
            _stopwatch.Restart();
            Update(
                state: NativeWebViewDownloadState.InProgress,
                startedAtUtc: DateTimeOffset.UtcNow,
                errorMessage: null,
                errorCode: null);
            _owner.RaiseStarted(this);
        }

        public void UpdateProgress(long bytesReceived, long? totalBytesToReceive = null, double? progress = null)
        {
            var elapsed = _stopwatch.Elapsed.TotalSeconds;
            double? rate = elapsed > 0 ? bytesReceived / elapsed : null;
            Update(
                bytesReceived: Math.Max(0, bytesReceived),
                totalBytesToReceive: totalBytesToReceive,
                progress: progress,
                transferRateBytesPerSecond: rate);
        }

        public void UpdateState(NativeWebViewDownloadState state, bool notify = true)
        {
            Update(state: state, notify: notify);
        }

        public void MarkPaused() => Update(state: NativeWebViewDownloadState.Paused);

        public void MarkResumed() => Update(state: NativeWebViewDownloadState.InProgress);

        public void MarkCanceled(string? message = null)
        {
            _stopwatch.Stop();
            Update(
                state: NativeWebViewDownloadState.Canceled,
                completedAtUtc: DateTimeOffset.UtcNow,
                errorMessage: message,
                errorCode: "Canceled");
            _owner.RaiseCompleted(this);
        }

        public void MarkFailed(string? message, string? code = null)
        {
            _stopwatch.Stop();
            Update(
                state: NativeWebViewDownloadState.Failed,
                completedAtUtc: DateTimeOffset.UtcNow,
                errorMessage: message,
                errorCode: code);
            _owner.RaiseCompleted(this);
        }

        public void MarkCompleted()
        {
            _stopwatch.Stop();
            Update(
                state: NativeWebViewDownloadState.Completed,
                progress: 1,
                completedAtUtc: DateTimeOffset.UtcNow,
                errorMessage: null,
                errorCode: null);
            _owner.RaiseCompleted(this);
        }

        private async Task<NativeWebViewDownloadActionResult> InvokeAsync(
            Func<CancellationToken, Task<NativeWebViewDownloadActionResult>>? action,
            string unsupportedMessage,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (action is null)
            {
                return NativeWebViewDownloadActionResult.Unsupported(unsupportedMessage);
            }

            try
            {
                return await action(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return NativeWebViewDownloadActionResult.Failed(ex.Message);
            }
        }

        private void Update(
            NativeWebViewDownloadState? state = null,
            string? destinationPath = null,
            long? bytesReceived = null,
            long? totalBytesToReceive = null,
            double? progress = null,
            double? transferRateBytesPerSecond = null,
            DateTimeOffset? startedAtUtc = null,
            DateTimeOffset? completedAtUtc = null,
            string? errorMessage = null,
            string? errorCode = null,
            bool notify = true)
        {
            lock (_gate)
            {
                var current = _snapshot;
                var nextState = state ?? current.State;
                var nextBytesReceived = bytesReceived ?? current.BytesReceived;
                var nextTotal = totalBytesToReceive ?? current.TotalBytesToReceive;
                var nextProgress = progress ?? ResolveProgress(nextBytesReceived, nextTotal, current.Progress);
                var isTerminal = nextState is NativeWebViewDownloadState.Completed or NativeWebViewDownloadState.Canceled or NativeWebViewDownloadState.Failed;
                var isPaused = nextState == NativeWebViewDownloadState.Paused;

                _snapshot = new NativeWebViewDownloadSnapshot(
                    current.Id,
                    current.RestartedFromId,
                    current.Uri,
                    current.SuggestedFileName,
                    destinationPath ?? current.DestinationPath,
                    current.MimeType,
                    current.ContentDisposition,
                    nextState,
                    nextBytesReceived,
                    nextTotal,
                    nextProgress,
                    transferRateBytesPerSecond ?? current.TransferRateBytesPerSecond,
                    current.CreatedAtUtc,
                    startedAtUtc ?? current.StartedAtUtc,
                    completedAtUtc ?? current.CompletedAtUtc,
                    errorMessage ?? current.ErrorMessage,
                    errorCode ?? current.ErrorCode,
                    canPause: !isTerminal && !isPaused && _nativeOperation.PauseAsync is not null,
                    canResume: !isTerminal && isPaused && _nativeOperation.ResumeAsync is not null,
                    canCancel: !isTerminal && _nativeOperation.CancelAsync is not null,
                    canRestart: isTerminal && _nativeOperation.RestartAsync is not null);
            }

            if (notify)
            {
                _owner.RaiseChanged(this);
            }
        }

        private static double? ResolveProgress(long bytesReceived, long? totalBytesToReceive, double? fallback)
        {
            if (totalBytesToReceive is > 0)
            {
                return Math.Clamp(bytesReceived / (double)totalBytesToReceive.Value, 0, 1);
            }

            return fallback;
        }

        private static string? NormalizePath(string? path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? null
                : Path.GetFullPath(path);
        }
    }
}
