using NativeWebView.Core;
using NativeWebView.Platform.Linux;
using NativeWebView.Platform.Windows;
using NativeWebView.Platform.macOS;

namespace NativeWebView.Core.Tests;

public sealed class DownloadManagerTests
{
    [Fact]
    public async Task PrepareDownloadAsync_RaisesStartingAndAppliesDestination()
    {
        var manager = new NativeWebViewDownloadManager();
        var changedCount = 0;
        var events = new List<string>();
        manager.DownloadChanged += (_, _) =>
        {
            changedCount++;
            events.Add("changed");
        };
        manager.DownloadStarting += (_, e) =>
        {
            events.Add("starting");
            e.DestinationPath = Path.Combine(Path.GetTempPath(), "nativewebview-download-test.bin");
            e.AllowOverwrite = true;
        };

        var args = await manager.PrepareDownloadAsync(
            new Uri("https://example.test/file.bin"),
            new NativeWebViewDownloadRequestOptions
            {
                SuggestedFileName = "file.bin",
                TotalBytesToReceive = 100,
            },
            new NativeWebViewDownloadNativeOperation());

        var snapshot = args.Item.Snapshot;
        Assert.False(args.Cancel);
        Assert.True(args.AllowOverwrite);
        Assert.Equal("file.bin", snapshot.SuggestedFileName);
        Assert.Equal(Path.Combine(Path.GetTempPath(), "nativewebview-download-test.bin"), snapshot.DestinationPath);
        Assert.Equal(NativeWebViewDownloadState.WaitingForDestination, snapshot.State);
        Assert.True(changedCount >= 1);
        Assert.Equal("starting", events[0]);
    }

    [Fact]
    public async Task PrepareDownloadAsync_HonorsDeferral()
    {
        var manager = new NativeWebViewDownloadManager();
        var deferralCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        manager.DownloadStarting += async (_, e) =>
        {
            using var deferral = e.GetDeferral();
            await deferralCompletion.Task.ConfigureAwait(false);
            e.DestinationPath = Path.Combine(Path.GetTempPath(), "deferred-download.bin");
        };

        var prepareTask = manager.PrepareDownloadAsync(
            new Uri("https://example.test/deferred.bin"),
            null,
            new NativeWebViewDownloadNativeOperation());

        Assert.False(prepareTask.IsCompleted);
        deferralCompletion.SetResult();

        var args = await prepareTask;
        Assert.Equal(Path.Combine(Path.GetTempPath(), "deferred-download.bin"), args.DestinationPath);
    }

    [Fact]
    public async Task PrepareDownloadAsync_HonorsFireAndForgetDeferral()
    {
        var manager = new NativeWebViewDownloadManager();
        var destination = Path.Combine(Path.GetTempPath(), "fire-and-forget-download.bin");

        manager.DownloadStarting += (_, e) =>
        {
            _ = CompleteLaterAsync(e, destination);
        };

        var prepareTask = manager.PrepareDownloadAsync(
            new Uri("https://example.test/fire-and-forget.bin"),
            null,
            new NativeWebViewDownloadNativeOperation());

        Assert.False(prepareTask.IsCompleted);

        var args = await prepareTask.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(destination, args.DestinationPath);
    }

    private static async Task CompleteLaterAsync(NativeWebViewDownloadStartingEventArgs e, string destination)
    {
        using var deferral = e.GetDeferral();
        await Task.Delay(50);
        e.DestinationPath = destination;
        deferral.Complete();
    }

    [Fact]
    public async Task DownloadItem_TracksProgressAndTerminalState()
    {
        var manager = new NativeWebViewDownloadManager();
        var completedCount = 0;
        manager.DownloadCompleted += (_, _) => completedCount++;

        var item = manager.AddStartedDownload(
            new Uri("https://example.test/file.bin"),
            new NativeWebViewDownloadRequestOptions { TotalBytesToReceive = 200 },
            new NativeWebViewDownloadNativeOperation());

        item.UpdateProgress(50, 200);
        Assert.Equal(NativeWebViewDownloadState.InProgress, item.Snapshot.State);
        Assert.Equal(0.25d, item.Snapshot.Progress);

        item.MarkCompleted();

        Assert.Equal(NativeWebViewDownloadState.Completed, item.Snapshot.State);
        Assert.Equal(1d, item.Snapshot.Progress);
        Assert.Equal(1, completedCount);
        Assert.Single(manager.Items);

        manager.ClearCompleted();
        Assert.Empty(manager.Items);
    }

    [Fact]
    public async Task CancelAction_UsesNativeOperation()
    {
        var canceled = false;
        var manager = new NativeWebViewDownloadManager();
        var item = manager.AddStartedDownload(
            new Uri("https://example.test/file.bin"),
            null,
            new NativeWebViewDownloadNativeOperation
            {
                CancelAsync = _ =>
                {
                    canceled = true;
                    return Task.FromResult(NativeWebViewDownloadActionResult.Success());
                },
            });

        var result = await item.CancelAsync();

        Assert.True(result.IsSuccess);
        Assert.True(canceled);
    }

    [Fact]
    public void DesktopBackends_AdvertiseDownloads()
    {
        using var windows = new WindowsNativeWebViewBackend();
        using var linux = new LinuxNativeWebViewBackend();
        using var macOS = new MacOSNativeWebViewBackend();

        Assert.True(windows.Features.Supports(NativeWebViewFeature.Downloads));
        Assert.True(linux.Features.Supports(NativeWebViewFeature.Downloads));
        Assert.True(windows.TryGetDownloadManager(out var windowsManager));
        Assert.NotNull(windowsManager);
        Assert.True(linux.TryGetDownloadManager(out var linuxManager));
        Assert.NotNull(linuxManager);

        Assert.False(macOS.Features.Supports(NativeWebViewFeature.Downloads));
        Assert.False(macOS.TryGetDownloadManager(out var macOSManager));
        Assert.Null(macOSManager);
    }
}
