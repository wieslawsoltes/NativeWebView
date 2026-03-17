namespace NativeWebView.Core;

using System.Text.Json;

internal static class WebAuthenticationBrokerBackendSupport
{
    public const int NotImplementedError = unchecked((int)0x80004001);

    private const double DefaultDialogWidth = 480;
    private const double DefaultDialogHeight = 720;

    public static bool IsCallbackUri(Uri? navigationUri, Uri callbackUri)
    {
        ArgumentNullException.ThrowIfNull(callbackUri);

        if (navigationUri is null || !navigationUri.IsAbsoluteUri || !callbackUri.IsAbsoluteUri)
        {
            return false;
        }

        return Uri.Compare(
            navigationUri,
            callbackUri,
            UriComponents.SchemeAndServer | UriComponents.Path,
            UriFormat.SafeUnescaped,
            StringComparison.OrdinalIgnoreCase) == 0;
    }

    public static bool TryCreateImmediateSuccess(Uri requestUri, Uri callbackUri, out WebAuthenticationResult result)
    {
        ArgumentNullException.ThrowIfNull(requestUri);
        ArgumentNullException.ThrowIfNull(callbackUri);

        if (IsCallbackUri(requestUri, callbackUri))
        {
            result = WebAuthenticationResult.Success(ToResponseData(requestUri));
            return true;
        }

        result = null!;
        return false;
    }

    public static WebAuthenticationResult UnsupportedHttpPost()
    {
        return WebAuthenticationResult.Error(NotImplementedError);
    }

    public static WebAuthenticationResult RuntimeUnavailable()
    {
        return WebAuthenticationResult.Error(NotImplementedError);
    }

    public static string CreateInteractiveTitle(Uri requestUri, WebAuthenticationOptions options)
    {
        ArgumentNullException.ThrowIfNull(requestUri);

        if ((options & WebAuthenticationOptions.UseTitle) != 0 &&
            !string.IsNullOrWhiteSpace(requestUri.Host))
        {
            return requestUri.Host;
        }

        return "Authentication";
    }

    public static async Task<WebAuthenticationResult> AuthenticateWithDialogAsync(
        INativeWebDialogBackend dialogBackend,
        Uri requestUri,
        Uri callbackUri,
        WebAuthenticationOptions options = WebAuthenticationOptions.None,
        CancellationToken cancellationToken = default,
        bool supportsHttpPost = false)
    {
        ArgumentNullException.ThrowIfNull(dialogBackend);
        ArgumentNullException.ThrowIfNull(requestUri);
        ArgumentNullException.ThrowIfNull(callbackUri);
        cancellationToken.ThrowIfCancellationRequested();

        if (TryCreateImmediateSuccess(requestUri, callbackUri, out var immediateResult))
        {
            dialogBackend.Dispose();
            return immediateResult;
        }

        if ((options & WebAuthenticationOptions.SilentMode) != 0)
        {
            dialogBackend.Dispose();
            return WebAuthenticationResult.UserCancel();
        }

        if ((options & WebAuthenticationOptions.UseHttpPost) != 0 && !supportsHttpPost)
        {
            dialogBackend.Dispose();
            return UnsupportedHttpPost();
        }

        var completion = new TaskCompletionSource<WebAuthenticationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completionState = 0;

        void TryCloseDialog()
        {
            try
            {
                if (dialogBackend.IsVisible)
                {
                    dialogBackend.Close();
                }
            }
            catch
            {
                // Best-effort shutdown for completion paths.
            }
        }

        void TryComplete(WebAuthenticationResult result, bool closeDialog)
        {
            if (Interlocked.CompareExchange(ref completionState, 1, 0) != 0)
            {
                return;
            }

            completion.TrySetResult(result);
            if (closeDialog)
            {
                TryCloseDialog();
            }
        }

        void TryCancel()
        {
            if (Interlocked.CompareExchange(ref completionState, 1, 0) != 0)
            {
                return;
            }

            completion.TrySetCanceled(cancellationToken);
            TryCloseDialog();
        }

        void OnClosed(object? sender, EventArgs e)
        {
            _ = sender;
            _ = e;
            TryComplete(WebAuthenticationResult.UserCancel(), closeDialog: false);
        }

        void OnNavigationStarted(object? sender, NativeWebViewNavigationStartedEventArgs e)
        {
            _ = sender;

            if (TryCompleteFromCallbackUri(e.Uri))
            {
                e.Cancel = true;
            }
        }

        void OnNavigationCompleted(object? sender, NativeWebViewNavigationCompletedEventArgs e)
        {
            _ = sender;
            TryCompleteFromCallbackUri(e.Uri);
        }

        void OnNewWindowRequested(object? sender, NativeWebViewNewWindowRequestedEventArgs e)
        {
            _ = sender;

            if (e.Uri is null)
            {
                return;
            }

            if (IsCallbackUri(e.Uri, callbackUri))
            {
                e.Handled = true;
                TryComplete(WebAuthenticationResult.Success(ToResponseData(e.Uri)), closeDialog: true);
                return;
            }

            e.Handled = true;

            try
            {
                dialogBackend.Navigate(e.Uri);
            }
            catch
            {
                // Leave the current flow running if popup redirection fails.
            }
        }

        bool TryCompleteFromCallbackUri(Uri? uri)
        {
            if (!IsCallbackUri(uri, callbackUri))
            {
                return false;
            }

            TryComplete(WebAuthenticationResult.Success(ToResponseData(uri!)), closeDialog: true);
            return true;
        }

        dialogBackend.Closed += OnClosed;
        dialogBackend.NavigationStarted += OnNavigationStarted;
        dialogBackend.NavigationCompleted += OnNavigationCompleted;
        dialogBackend.NewWindowRequested += OnNewWindowRequested;

        using var cancellationRegistration = cancellationToken.Register(static state =>
        {
            ((Action)state!).Invoke();
        }, (Action)TryCancel);

        try
        {
            dialogBackend.Show(new NativeWebDialogShowOptions
            {
                Title = CreateInteractiveTitle(requestUri, options),
                Width = DefaultDialogWidth,
                Height = DefaultDialogHeight,
                CenterOnParent = true,
            });

            var callbackObserverTask = ObserveCallbackUriAsync(
                dialogBackend,
                callbackUri,
                TryCompleteFromCallbackUri,
                () => Volatile.Read(ref completionState) != 0,
                cancellationToken);

            dialogBackend.Navigate(requestUri);
            var result = await completion.Task.ConfigureAwait(true);
            await callbackObserverTask.ConfigureAwait(true);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return RuntimeUnavailable();
        }
        finally
        {
            dialogBackend.Closed -= OnClosed;
            dialogBackend.NavigationStarted -= OnNavigationStarted;
            dialogBackend.NavigationCompleted -= OnNavigationCompleted;
            dialogBackend.NewWindowRequested -= OnNewWindowRequested;
            dialogBackend.Dispose();
        }
    }

    public static string ToResponseData(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return uri.IsAbsoluteUri ? uri.AbsoluteUri : uri.ToString();
    }

    private static async Task ObserveCallbackUriAsync(
        INativeWebDialogBackend dialogBackend,
        Uri callbackUri,
        Func<Uri?, bool> tryCompleteFromUri,
        Func<bool> isCompleted,
        CancellationToken cancellationToken)
    {
        while (!isCompleted())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (tryCompleteFromUri(dialogBackend.CurrentUrl))
            {
                return;
            }

            try
            {
                var location = await dialogBackend.ExecuteScriptAsync("window.location.href", cancellationToken).ConfigureAwait(true);
                if (Uri.TryCreate(ParseScriptString(location), UriKind.Absolute, out var currentUri))
                {
                    if (tryCompleteFromUri(currentUri))
                    {
                        return;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // The dialog runtime may not be script-ready yet. Continue observing navigation.
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken).ConfigureAwait(true);
        }
    }

    private static string? ParseScriptString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var trimmed = value.Trim();

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            if (document.RootElement.ValueKind == JsonValueKind.String)
            {
                return document.RootElement.GetString();
            }
        }
        catch (JsonException)
        {
        }

        return trimmed;
    }
}
