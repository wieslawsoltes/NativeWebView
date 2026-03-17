using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using NativeWebView.Auth;
using NativeWebView.Core;
using NativeWebView.Dialog;
using NativeWebView.Interop;

namespace NativeWebView.Integration;

internal sealed class IntegrationView : UserControl
{
    private readonly NativeWebView.Controls.NativeWebView _webView;
    private readonly TextBlock _statusBlock;
    private readonly TextBox _logBox;
    private readonly StringBuilder _logBuffer = new();

    private bool _started;

    public IntegrationView()
    {
        _statusBlock = new TextBlock
        {
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Text = "Waiting for integration host...",
        };

        _logBox = new TextBox
        {
            AcceptsReturn = true,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            Background = Brushes.Transparent,
        };

        _webView = new NativeWebView.Controls.NativeWebView
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        var headerBorder = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#EEF2FF")),
            BorderBrush = new SolidColorBrush(Color.Parse("#C7D2FE")),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 12),
            Child = _statusBlock,
        };

        var webViewBorder = new Border
        {
            Margin = new Thickness(0, 16, 0, 16),
            BorderBrush = new SolidColorBrush(Color.Parse("#CBD5E1")),
            BorderThickness = new Thickness(1),
            Child = _webView,
        };

        var logBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.Parse("#CBD5E1")),
            BorderThickness = new Thickness(1),
            Child = _logBox,
        };

        Grid.SetRow(webViewBorder, 1);
        Grid.SetRow(logBorder, 2);

        Content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,220"),
            Margin = new Thickness(16),
            Children =
            {
                headerBorder,
                webViewBorder,
                logBorder,
            },
        };
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (_started)
        {
            return;
        }

        _started = true;
        Dispatcher.UIThread.Post(() => _ = RunAsync(), DispatcherPriority.Background);
    }

    private async Task RunAsync()
    {
        var platform = NativeWebViewRuntime.CurrentPlatform;
        var result = new IntegrationRunResult
        {
            Platform = platform.ToString(),
            StartedAtUtc = DateTimeOffset.UtcNow,
        };

        AppendLog($"Starting integration run for {platform}.");
        UpdateStatus($"Running integration scenarios for {platform}...");

        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromMinutes(3));

        try
        {
            await using var pages = await IntegrationPageCatalog.CreateAsync(platform, cancellationSource.Token).ConfigureAwait(true);

            result.Scenarios.Add(await RunWebViewScenarioAsync(platform, pages, cancellationSource.Token).ConfigureAwait(true));
            result.Scenarios.Add(await RunDialogScenarioAsync(platform, pages, cancellationSource.Token).ConfigureAwait(true));
            result.Scenarios.Add(await RunAuthenticationScenarioAsync(platform, pages, cancellationSource.Token).ConfigureAwait(true));
        }
        catch (Exception ex)
        {
            result.Scenarios.Add(new IntegrationScenarioResult
            {
                Name = "harness",
                Passed = false,
                Details = $"{ex.GetType().Name}: {ex.Message}",
            });

            AppendLog($"Harness failure: {FormatException(ex)}");
        }

        result.CompletedAtUtc = DateTimeOffset.UtcNow;
        IntegrationLog.PublishResult(result);

        UpdateStatus(result.Passed
            ? $"Integration run passed for {platform}."
            : $"Integration run failed for {platform}.");

        AppendLog($"Integration run completed. Passed={result.Passed}.");
        TryShutdownIfDesktop(result.Passed ? 0 : 1);
    }

    private async Task<IntegrationScenarioResult> RunWebViewScenarioAsync(
        NativeWebViewPlatform platform,
        IntegrationPageCatalog pages,
        CancellationToken cancellationToken)
    {
        var scenario = new IntegrationScenarioResult { Name = "webview" };

        var initializedCompletion = new TaskCompletionSource<CoreWebViewInitializedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        var navigationCompletion = new TaskCompletionSource<NativeWebViewNavigationCompletedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pageReadyCompletion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnInitialized(object? sender, CoreWebViewInitializedEventArgs e)
        {
            initializedCompletion.TrySetResult(e);
        }

        void OnNavigationCompleted(object? sender, NativeWebViewNavigationCompletedEventArgs e)
        {
            if (e.Uri is not null && AreSameUri(e.Uri, pages.WebViewPageUri))
            {
                navigationCompletion.TrySetResult(e);
            }
        }

        void OnWebMessageReceived(object? sender, NativeWebViewMessageReceivedEventArgs e)
        {
            var message = e.Message ?? e.Json;
            if (string.Equals(message, "page-ready:webview", StringComparison.Ordinal))
            {
                pageReadyCompletion.TrySetResult(message!);
            }
        }

        _webView.CoreWebView2Initialized += OnInitialized;
        _webView.NavigationCompleted += OnNavigationCompleted;
        _webView.WebMessageReceived += OnWebMessageReceived;

        try
        {
            AppendLog("[webview] Initializing embedded control.");

            if (platform == NativeWebViewPlatform.MacOS)
            {
                _webView.RenderMode = NativeWebViewRenderMode.Offscreen;
            }

            await _webView.InitializeAsync(cancellationToken).ConfigureAwait(true);

            if (platform != NativeWebViewPlatform.MacOS)
            {
                var initializedArgs = await initializedCompletion.Task
                    .WaitAsync(TimeSpan.FromSeconds(20), cancellationToken)
                    .ConfigureAwait(true);

                if (!initializedArgs.IsSuccess || initializedArgs.NativeObject is null)
                {
                    throw new InvalidOperationException("Embedded control did not report a runtime-native initialization object.");
                }

                scenario.Evidence.Add($"initialized:{initializedArgs.NativeObject.GetType().FullName ?? initializedArgs.NativeObject}");
            }
            else
            {
                scenario.Evidence.Add("initialized:macos-native-host");
            }

            _webView.Navigate(pages.WebViewPageUri);

            var navigationArgs = await navigationCompletion.Task
                .WaitAsync(TimeSpan.FromSeconds(30), cancellationToken)
                .ConfigureAwait(true);

            if (!navigationArgs.IsSuccess)
            {
                throw new InvalidOperationException($"Navigation failed: {navigationArgs.Error ?? "unknown error"}");
            }

            scenario.Evidence.Add($"navigated:{navigationArgs.Uri}");

            if (platform == NativeWebViewPlatform.MacOS)
            {
                var frame = await WaitForRenderFrameAsync(cancellationToken).ConfigureAwait(true);
                if (frame is not null && !frame.IsSynthetic)
                {
                    scenario.Evidence.Add($"frame:{frame.PixelWidth}x{frame.PixelHeight}:{frame.Origin}");
                }
                else
                {
                    var outputPath = Path.Combine(
                        IntegrationPlatformContext.GetArtifactsDirectory(platform),
                        "macos-webview-proof.pdf");

                    var printResult = await _webView.PrintAsync(
                            new NativeWebViewPrintSettings { OutputPath = outputPath },
                            cancellationToken)
                        .ConfigureAwait(true);

                    if (printResult.Status != NativeWebViewPrintStatus.Success ||
                        !File.Exists(outputPath) ||
                        new FileInfo(outputPath).Length == 0)
                    {
                        throw new InvalidOperationException(
                            $"macOS embedded control did not produce a render proof. Print status={printResult.Status}, error={printResult.ErrorMessage ?? "<none>"}");
                    }

                    scenario.Evidence.Add($"printed:{outputPath}");
                }
            }
            else
            {
                if (platform != NativeWebViewPlatform.Browser)
                {
                    await pageReadyCompletion.Task
                        .WaitAsync(TimeSpan.FromSeconds(20), cancellationToken)
                        .ConfigureAwait(true);
                }
            }

            if (platform != NativeWebViewPlatform.MacOS)
            {
                var pageReady = await EvaluateBooleanAsync(
                        _webView.ExecuteScriptAsync,
                        "window.__nativeWebViewIntegrationState && window.__nativeWebViewIntegrationState.pageReady",
                        cancellationToken)
                    .ConfigureAwait(true);

                if (!pageReady)
                {
                    throw new InvalidOperationException("Embedded page did not report a ready state.");
                }

                var location = await EvaluateStringAsync(_webView.ExecuteScriptAsync, "window.location.href", cancellationToken)
                    .ConfigureAwait(true);

                if (!Uri.TryCreate(location, UriKind.Absolute, out var actualLocation) ||
                    actualLocation is null ||
                    !AreSameUri(actualLocation, pages.WebViewPageUri))
                {
                    throw new InvalidOperationException($"Unexpected embedded page location '{location ?? "<null>"}'.");
                }

                scenario.Evidence.Add($"location:{location}");

                if (platform == NativeWebViewPlatform.Browser)
                {
                    scenario.Evidence.Add("message-channel:not-asserted-browser-runtime");
                }
                else
                {
                    await _webView.PostWebMessageAsStringAsync("native-ping", cancellationToken).ConfigureAwait(true);
                    var lastNativeMessage = await WaitForStringResultAsync(
                            _webView.ExecuteScriptAsync,
                            "window.__nativeWebViewIntegrationState && window.__nativeWebViewIntegrationState.lastNativeMessage",
                            "native-ping",
                            cancellationToken)
                        .ConfigureAwait(true);

                    scenario.Evidence.Add($"native-message:{lastNativeMessage}");
                }
            }

            scenario.Passed = true;
            scenario.Details = "Embedded control created and validated.";
            AppendLog("[webview] Embedded control validation passed.");
        }
        catch (Exception ex)
        {
            scenario.Passed = false;
            scenario.Details = FormatException(ex);
            scenario.Evidence.Add(ex.GetType().Name);
            AppendLog($"[webview] Failure: {FormatException(ex)}");
        }
        finally
        {
            _webView.CoreWebView2Initialized -= OnInitialized;
            _webView.NavigationCompleted -= OnNavigationCompleted;
            _webView.WebMessageReceived -= OnWebMessageReceived;
        }

        return scenario;
    }

    private async Task<IntegrationScenarioResult> RunDialogScenarioAsync(
        NativeWebViewPlatform platform,
        IntegrationPageCatalog pages,
        CancellationToken cancellationToken)
    {
        var scenario = new IntegrationScenarioResult { Name = "dialog" };

        using var dialog = new NativeWebDialog();

        if (!IsDesktopPlatform(platform))
        {
            try
            {
                dialog.Show();
                scenario.Passed = false;
                scenario.Details = "Dialog unexpectedly succeeded on a platform that should not support it.";
            }
            catch (PlatformNotSupportedException)
            {
                scenario.Passed = true;
                scenario.Details = "Dialog is unsupported on this platform, as expected.";
                scenario.Evidence.Add("unsupported");
            }
            catch (Exception ex)
            {
                scenario.Passed = false;
                scenario.Details = FormatException(ex);
                scenario.Evidence.Add(ex.GetType().Name);
            }

            AppendLog($"[dialog] {scenario.Details}");
            return scenario;
        }

        var navigationCompletion = new TaskCompletionSource<NativeWebViewNavigationCompletedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pageToNativeCompletion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnNavigationCompleted(object? sender, NativeWebViewNavigationCompletedEventArgs e)
        {
            if (e.Uri is not null && AreSameUri(e.Uri, pages.DialogPageUri))
            {
                navigationCompletion.TrySetResult(e);
            }
        }

        void OnWebMessageReceived(object? sender, NativeWebViewMessageReceivedEventArgs e)
        {
            var message = e.Message ?? e.Json;
            if (string.Equals(message, "dialog-script-ping", StringComparison.Ordinal))
            {
                pageToNativeCompletion.TrySetResult(message!);
            }
        }

        dialog.NavigationCompleted += OnNavigationCompleted;
        dialog.WebMessageReceived += OnWebMessageReceived;

        try
        {
            AppendLog("[dialog] Showing native dialog.");

            dialog.Show(new NativeWebDialogShowOptions
            {
                Title = "NativeWebView Integration Dialog",
                Width = 960,
                Height = 720,
                CenterOnParent = true,
            });

            if (!dialog.IsVisible)
            {
                throw new InvalidOperationException("Dialog did not become visible.");
            }

            RequireNativeHandle(dialog.TryGetDialogHandle(out var dialogHandle), dialogHandle, "dialog");
            RequireNativeHandle(dialog.TryGetHostWindowHandle(out var hostHandle), hostHandle, "host");

            scenario.Evidence.Add($"dialog-handle:{dialogHandle.HandleDescriptor}");
            scenario.Evidence.Add($"host-handle:{hostHandle.HandleDescriptor}");

            dialog.Navigate(pages.DialogPageUri);

            if (platform == NativeWebViewPlatform.MacOS)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(750), cancellationToken).ConfigureAwait(true);

                var outputPath = Path.Combine(
                    IntegrationPlatformContext.GetArtifactsDirectory(platform),
                    "macos-dialog-proof.pdf");

                var printResult = await dialog.PrintAsync(
                        new NativeWebViewPrintSettings { OutputPath = outputPath },
                        cancellationToken)
                    .ConfigureAwait(true);

                if (printResult.Status != NativeWebViewPrintStatus.Success ||
                    !File.Exists(outputPath) ||
                    new FileInfo(outputPath).Length == 0)
                {
                    throw new InvalidOperationException(
                        $"macOS dialog did not produce a runtime proof. Print status={printResult.Status}, error={printResult.ErrorMessage ?? "<none>"}");
                }

                scenario.Evidence.Add($"printed:{outputPath}");
            }
            else
            {
                var location = await WaitForUriResultAsync(
                        () => dialog.CurrentUrl,
                        dialog.ExecuteScriptAsync,
                        "window.location.href",
                        pages.DialogPageUri,
                        cancellationToken)
                    .ConfigureAwait(true);

                scenario.Evidence.Add($"location:{location}");

                if (navigationCompletion.Task.IsCompletedSuccessfully)
                {
                    var navigationArgs = await navigationCompletion.Task.ConfigureAwait(true);
                    if (!navigationArgs.IsSuccess)
                    {
                        throw new InvalidOperationException($"Dialog navigation failed: {navigationArgs.Error ?? "unknown error"}");
                    }

                    scenario.Evidence.Add($"navigated:{navigationArgs.Uri}");
                }

                await WaitForBooleanResultAsync(
                        dialog.ExecuteScriptAsync,
                        "window.__nativeWebViewIntegrationState && window.__nativeWebViewIntegrationState.pageReady",
                        cancellationToken)
                    .ConfigureAwait(true);

                await dialog.ExecuteScriptAsync(
                        "if (window.chrome && window.chrome.webview && typeof window.chrome.webview.postMessage === 'function') { window.chrome.webview.postMessage('dialog-script-ping'); }",
                        cancellationToken)
                    .ConfigureAwait(true);

                await pageToNativeCompletion.Task
                    .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken)
                    .ConfigureAwait(true);

                await dialog.PostWebMessageAsStringAsync("dialog-native-ping", cancellationToken).ConfigureAwait(true);
                var lastNativeMessage = await WaitForStringResultAsync(
                        dialog.ExecuteScriptAsync,
                        "window.__nativeWebViewIntegrationState && window.__nativeWebViewIntegrationState.lastNativeMessage",
                        "dialog-native-ping",
                        cancellationToken)
                    .ConfigureAwait(true);

                scenario.Evidence.Add($"native-message:{lastNativeMessage}");
            }

            dialog.Close();
            scenario.Passed = true;
            scenario.Details = "Dialog runtime verified.";
            AppendLog("[dialog] Dialog validation passed.");
        }
        catch (Exception ex)
        {
            scenario.Passed = false;
            scenario.Details = FormatException(ex);
            scenario.Evidence.Add(ex.GetType().Name);
            AppendLog($"[dialog] Failure: {FormatException(ex)}");
        }
        finally
        {
            dialog.NavigationCompleted -= OnNavigationCompleted;
            dialog.WebMessageReceived -= OnWebMessageReceived;
        }

        return scenario;
    }

    private async Task<IntegrationScenarioResult> RunAuthenticationScenarioAsync(
        NativeWebViewPlatform platform,
        IntegrationPageCatalog pages,
        CancellationToken cancellationToken)
    {
        var scenario = new IntegrationScenarioResult { Name = "auth" };

        if (platform == NativeWebViewPlatform.MacOS)
        {
            scenario.Passed = true;
            scenario.Details = "Skipped on macOS until the native dialog backend surfaces WKWebView redirect callbacks.";
            scenario.Evidence.Add("skipped:macos-auth-redirect-callbacks");
            AppendLog("[auth] Skipping macOS runtime auth validation because redirect callbacks are not surfaced by the current native dialog backend.");
            return scenario;
        }

        try
        {
            AppendLog("[auth] Starting authentication flow.");

            using var broker = new WebAuthenticationBroker();
            using var authCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            authCancellationSource.CancelAfter(TimeSpan.FromSeconds(60));
            var result = await broker.AuthenticateAsync(
                    pages.AuthRequestUri,
                    pages.AuthCallbackUri,
                    WebAuthenticationOptions.UseTitle,
                    authCancellationSource.Token)
                .ConfigureAwait(true);

            if (result.ResponseStatus != WebAuthenticationStatus.Success)
            {
                throw new InvalidOperationException(
                    $"Authentication did not succeed. Status={result.ResponseStatus}, error={result.ResponseErrorDetail}.");
            }

            if (!Uri.TryCreate(result.ResponseData, UriKind.Absolute, out var responseUri) ||
                responseUri is null ||
                !MatchesCallbackPath(responseUri, pages.AuthCallbackUri))
            {
                throw new InvalidOperationException($"Unexpected authentication callback '{result.ResponseData ?? "<null>"}'.");
            }

            if (!responseUri.Query.Contains("token=integration-ok", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Authentication callback was missing the expected token.");
            }

            scenario.Passed = true;
            scenario.Details = "Authentication broker completed an interactive redirect.";
            scenario.Evidence.Add($"response:{result.ResponseData}");
            scenario.Evidence.Add($"platform:{platform}");
            AppendLog("[auth] Authentication validation passed.");
        }
        catch (Exception ex)
        {
            scenario.Passed = false;
            scenario.Details = FormatException(ex);
            scenario.Evidence.Add(ex.GetType().Name);
            AppendLog($"[auth] Failure: {FormatException(ex)}");
        }

        return scenario;
    }

    private void AppendLog(string message)
    {
        var line = $"[{DateTimeOffset.Now:HH:mm:ss}] {message}";
        _logBuffer.AppendLine(line);
        _logBox.Text = _logBuffer.ToString();
        IntegrationLog.Write(line);
    }

    private void UpdateStatus(string message)
    {
        _statusBlock.Text = message;
    }

    private static bool IsDesktopPlatform(NativeWebViewPlatform platform)
    {
        return platform is NativeWebViewPlatform.Windows or NativeWebViewPlatform.MacOS or NativeWebViewPlatform.Linux;
    }

    private async Task<NativeWebViewRenderFrame?> WaitForRenderFrameAsync(CancellationToken cancellationToken)
    {
        Exception? lastException = null;

        for (var attempt = 0; attempt < 8; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var frame = await _webView.CaptureRenderFrameAsync(cancellationToken).ConfigureAwait(true);
                if (frame is not null && !frame.IsSynthetic)
                {
                    return frame;
                }
            }
            catch (Exception ex)
            {
                lastException = ex;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(true);
        }

        if (lastException is not null)
        {
            throw lastException;
        }

        return null;
    }

    private static void RequireNativeHandle(bool success, NativePlatformHandle handle, string description)
    {
        if (!success || handle.Handle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Expected a real native {description} handle.");
        }
    }

    private static async Task<bool> EvaluateBooleanAsync(
        Func<string, CancellationToken, Task<string?>> executeScriptAsync,
        string script,
        CancellationToken cancellationToken)
    {
        var result = await executeScriptAsync(script, cancellationToken).ConfigureAwait(true);
        var parsed = ParseJsonLike(result);

        return parsed switch
        {
            bool booleanValue => booleanValue,
            string stringValue when bool.TryParse(stringValue, out var booleanValue) => booleanValue,
            string stringValue when stringValue == "1" => true,
            string stringValue when stringValue == "0" => false,
            _ => false,
        };
    }

    private static async Task<string?> EvaluateStringAsync(
        Func<string, CancellationToken, Task<string?>> executeScriptAsync,
        string script,
        CancellationToken cancellationToken)
    {
        var result = await executeScriptAsync(script, cancellationToken).ConfigureAwait(true);
        var parsed = ParseJsonLike(result);
        return parsed?.ToString();
    }

    private static async Task<string> WaitForStringResultAsync(
        Func<string, CancellationToken, Task<string?>> executeScriptAsync,
        string script,
        string expectedValue,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;

        for (var attempt = 0; attempt < 30; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var value = await EvaluateStringAsync(executeScriptAsync, script, cancellationToken).ConfigureAwait(true);
                if (string.Equals(value, expectedValue, StringComparison.Ordinal))
                {
                    return value!;
                }
            }
            catch (Exception ex)
            {
                lastException = ex;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken).ConfigureAwait(true);
        }

        if (lastException is not null)
        {
            throw lastException;
        }

        throw new InvalidOperationException($"Timed out waiting for script value '{expectedValue}'.");
    }

    private static async Task<string> WaitForUriResultAsync(
        Func<Uri?> currentUriProvider,
        Func<string, CancellationToken, Task<string?>> executeScriptAsync,
        string script,
        Uri expectedValue,
        CancellationToken cancellationToken,
        int maxAttempts = 100)
    {
        Exception? lastException = null;
        string? lastObservedValue = null;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentUri = currentUriProvider();
            if (currentUri is not null && AreSameUri(currentUri, expectedValue))
            {
                return currentUri.AbsoluteUri;
            }

            try
            {
                var value = await EvaluateStringAsync(executeScriptAsync, script, cancellationToken).ConfigureAwait(true);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    lastObservedValue = value;

                    if (Uri.TryCreate(value, UriKind.Absolute, out var actualUri) &&
                        actualUri is not null &&
                        AreSameUri(actualUri, expectedValue))
                    {
                        return value;
                    }
                }
            }
            catch (Exception ex)
            {
                lastException = ex;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken).ConfigureAwait(true);
        }

        if (lastException is not null)
        {
            throw lastException;
        }

        var observed = currentUriProvider()?.AbsoluteUri ?? lastObservedValue ?? "<null>";
        throw new InvalidOperationException(
            $"Timed out waiting for URI '{expectedValue.AbsoluteUri}'. Last observed value was '{observed}'.");
    }

    private static async Task WaitForBooleanResultAsync(
        Func<string, CancellationToken, Task<string?>> executeScriptAsync,
        string script,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;

        for (var attempt = 0; attempt < 30; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (await EvaluateBooleanAsync(executeScriptAsync, script, cancellationToken).ConfigureAwait(true))
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                lastException = ex;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken).ConfigureAwait(true);
        }

        if (lastException is not null)
        {
            throw lastException;
        }

        throw new InvalidOperationException("Timed out waiting for script boolean result to become true.");
    }

    private static object? ParseJsonLike(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim();
        if (string.Equals(trimmed, "null", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            return document.RootElement.ValueKind switch
            {
                JsonValueKind.String => document.RootElement.GetString(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number => document.RootElement.ToString(),
                JsonValueKind.Object => document.RootElement.GetRawText(),
                JsonValueKind.Array => document.RootElement.GetRawText(),
                JsonValueKind.Null => null,
                _ => trimmed,
            };
        }
        catch (JsonException)
        {
            return trimmed;
        }
    }

    private static bool AreSameUri(Uri actual, Uri expected)
    {
        return Uri.Compare(
            actual,
            expected,
            UriComponents.SchemeAndServer | UriComponents.PathAndQuery,
            UriFormat.SafeUnescaped,
            StringComparison.OrdinalIgnoreCase) == 0;
    }

    private static bool MatchesCallbackPath(Uri actual, Uri expected)
    {
        return Uri.Compare(
            actual,
            expected,
            UriComponents.SchemeAndServer | UriComponents.Path,
            UriFormat.SafeUnescaped,
            StringComparison.OrdinalIgnoreCase) == 0;
    }

    private static string FormatException(Exception ex)
    {
        return $"{ex.GetType().Name}: {ex.Message}";
    }

    private static void TryShutdownIfDesktop(int exitCode)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Environment.ExitCode = exitCode;
            desktop.Shutdown(exitCode);
        }
    }
}
