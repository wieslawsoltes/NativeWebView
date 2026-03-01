using System.Globalization;
using System.IO;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using NativeWebView.Auth;
using NativeWebView.Core;
using NativeWebView.Dialog;
using NativeWebView.Interop;
using NativeWebView.Platform.Linux;
using NativeWebView.Platform.Windows;
using NativeWebView.Platform.macOS;

namespace NativeWebView.Sample.Desktop;

public partial class MainWindow : Window
{
    private readonly StringBuilder _eventLog = new();
    private NativeWebDialog? _dialog;
    private WebAuthenticationBroker? _authenticationBroker;
    private bool _dialogEventsAttached;
    private NativeWebViewPlatform _platform;
    private string? _lastWebViewScriptResult;
    private string? _lastDialogScriptResult;
    private NativeWebViewPrintStatus? _lastPrintStatus;
    private NativeWebViewPrintStatus? _lastDialogPrintStatus;
    private bool? _lastPrintUiResult;
    private bool? _lastDialogPrintUiResult;
    private string? _lastAuthStatus;
    private string? _lastCapturedFrameInfo;
    private string? _lastSavedFramePath;
    private string? _lastSavedFrameMetadataPath;
    private string? _lastLoadedFrameMetadataSummary;
    private bool? _manualCompositedPassthroughOverride;

    public MainWindow()
    {
        InitializeComponent(true);
        ConfigureDefaults();
        AttachWebViewEvents();
        Opened += OnOpened;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        _dialog?.Dispose();
        _authenticationBroker?.Dispose();
        WebViewControl.Dispose();
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        await InitializeSampleAsync();
    }

    private async Task InitializeSampleAsync()
    {
        _platform = NativeWebViewRuntime.CurrentPlatform;
        HeaderTextBlock.Text = $"NativeWebView Feature Explorer ({_platform})";

        if (!IsDesktopPlatform(_platform))
        {
            AddLog($"Current platform '{_platform}' is not a desktop target for this sample.");
            DiagnosticsSummaryBox.Text = "Desktop sample supports Windows, macOS, and Linux.";
            return;
        }

        NativeWebViewRuntime.EnsureCurrentPlatformRegistered();
        PopulateDiagnosticsSummary();

        try
        {
            await EnsureWebViewInitializedAsync();
            if (TryParseUri(UrlTextBox.Text, out var uri))
            {
                WebViewControl.Navigate(uri);
            }

            SyncCheckBoxState();
            AddLog("WebView control initialized.");
        }
        catch (Exception ex)
        {
            AddLog($"Initialization failed: {FormatException(ex)}");
        }

        RefreshSummaries();
    }

    private static bool IsDesktopPlatform(NativeWebViewPlatform platform)
    {
        return platform is NativeWebViewPlatform.Windows or NativeWebViewPlatform.MacOS or NativeWebViewPlatform.Linux;
    }

    private void ConfigureDefaults()
    {
        UrlTextBox.Text = "https://example.com/";
        ScriptTextBox.Text = "window.location.href";
        MessageTextBox.Text = "sample-message";
        ZoomTextBox.Text = "1.10";
        UserAgentTextBox.Text = "NativeWebView.Sample.Desktop/1.0";
        HeaderValueTextBox.Text = "X-Sample-Header: NativeWebView";

        DialogUrlTextBox.Text = "https://example.com/dialog";
        DialogScriptTextBox.Text = "window.location.href";
        DialogMessageTextBox.Text = "{\"dialog\":true}";
        DialogLeftTextBox.Text = "120";
        DialogTopTextBox.Text = "120";
        DialogWidthTextBox.Text = "900";
        DialogHeightTextBox.Text = "640";
        DialogZoomTextBox.Text = "1.00";
        DialogUserAgentTextBox.Text = "NativeWebView.Dialog.Sample/1.0";
        DialogHeaderTextBox.Text = "X-Dialog-Sample: NativeWebView";

        AuthRequestUriTextBox.Text = "https://example.com/auth";
        AuthCallbackUriTextBox.Text = "https://example.com/callback";
        AuthResultTextBlock.Text = "Authentication result: (not run)";

        HeaderTextBlock.Text = "NativeWebView Feature Explorer";
        DiagnosticsSummaryBox.Text = "Diagnostics not collected yet.";
        FeatureMatrixBox.Text = "Feature matrix not available yet.";
        HandleSummaryBox.Text = "Handle summary not available yet.";
        StateSummaryBox.Text = "State summary not available yet.";

        WebViewControl.RenderMode = NativeWebViewRenderMode.Embedded;
        WebViewControl.RenderFramesPerSecond = 30;
        WebViewControl.SetCompositedPassthroughOverride(null);
        _manualCompositedPassthroughOverride = null;
        ManagedOverlayPanel.IsVisible = false;
        ManagedOverlayTextBox.Text = string.Empty;
    }

    private void PopulateDiagnosticsSummary()
    {
        var factory = new NativeWebViewBackendFactory();
        RegisterDesktop(factory, _platform);

        var diagnostics = factory.GetPlatformDiagnosticsOrDefault(_platform);
        var builder = new StringBuilder();
        builder.Append("Provider: ").AppendLine(diagnostics.ProviderName)
            .Append("Ready: ").AppendLine(diagnostics.IsReady.ToString(CultureInfo.InvariantCulture))
            .Append("Issues: ").AppendLine(diagnostics.Issues.Count.ToString(CultureInfo.InvariantCulture));

        foreach (var issue in diagnostics.Issues)
        {
            builder.Append("- [")
                .Append(issue.Severity)
                .Append("] ")
                .Append(issue.Code)
                .Append(": ")
                .Append(issue.Message);

            if (!string.IsNullOrWhiteSpace(issue.Recommendation))
            {
                builder.Append(" Recommendation: ").Append(issue.Recommendation);
            }

            builder.AppendLine();
        }

        DiagnosticsSummaryBox.Text = builder.ToString();

        if (GetBooleanEnvironmentVariable("NATIVEWEBVIEW_DIAGNOSTICS_REQUIRE_READY"))
        {
            var warningsAsErrors = GetBooleanEnvironmentVariable("NATIVEWEBVIEW_DIAGNOSTICS_WARNINGS_AS_ERRORS");
            NativeWebViewDiagnosticsValidator.EnsureReady(diagnostics, warningsAsErrors);
        }
    }

    private static void RegisterDesktop(NativeWebViewBackendFactory factory, NativeWebViewPlatform platform)
    {
        switch (platform)
        {
            case NativeWebViewPlatform.Windows:
                factory.UseNativeWebViewWindows();
                break;
            case NativeWebViewPlatform.MacOS:
                factory.UseNativeWebViewMacOS();
                break;
            case NativeWebViewPlatform.Linux:
                factory.UseNativeWebViewLinux();
                break;
        }
    }

    private static bool GetBooleanEnvironmentVariable(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return bool.TryParse(value, out var parsed)
            ? parsed
            : string.Equals(value, "1", StringComparison.OrdinalIgnoreCase);
    }

    private void AttachWebViewEvents()
    {
        WebViewControl.CoreWebView2Initialized += (_, e) =>
            AddLog($"CoreWebView2Initialized: success={e.IsSuccess}");

        WebViewControl.NavigationStarted += (_, e) =>
            AddLog($"NavigationStarted: uri={e.Uri}");

        WebViewControl.NavigationCompleted += (_, e) =>
            AddLog($"NavigationCompleted: uri={e.Uri}, success={e.IsSuccess}, status={e.HttpStatusCode}");

        WebViewControl.WebMessageReceived += (_, e) =>
            AddLog($"WebMessageReceived: message={e.Message ?? "<null>"}, json={e.Json ?? "<null>"}");

        WebViewControl.OpenDevToolsRequested += (_, _) => AddLog("OpenDevToolsRequested");

        WebViewControl.DestroyRequested += (_, e) =>
            AddLog($"DestroyRequested: reason={e.Reason ?? "<null>"}");

        WebViewControl.RequestCustomChrome += (_, e) =>
            AddLog($"RequestCustomChrome: useCustomChrome={e.UseCustomChrome}");

        WebViewControl.RequestParentWindowPosition += (_, e) =>
            AddLog($"RequestParentWindowPosition: left={e.Left}, top={e.Top}, width={e.Width}, height={e.Height}");

        WebViewControl.BeginMoveDrag += (_, _) => AddLog("BeginMoveDrag");

        WebViewControl.BeginResizeDrag += (_, e) => AddLog($"BeginResizeDrag: edge={e.Edge}");

        WebViewControl.NewWindowRequested += (_, e) =>
            AddLog($"NewWindowRequested: uri={e.Uri}");

        WebViewControl.WebResourceRequested += (_, e) =>
            AddLog($"WebResourceRequested: method={e.Method}, uri={e.Uri}");

        WebViewControl.ContextMenuRequested += (_, e) =>
            AddLog($"ContextMenuRequested: x={e.X:0.##}, y={e.Y:0.##}");

        WebViewControl.NavigationHistoryChanged += (_, e) =>
            AddLog($"NavigationHistoryChanged: canGoBack={e.CanGoBack}, canGoForward={e.CanGoForward}");

        WebViewControl.CoreWebView2EnvironmentRequested += (_, e) =>
        {
            e.Options.Language ??= "en-US";
            e.Options.UserDataFolder ??= "./artifacts/sample-webview-userdata";
            AddLog("CoreWebView2EnvironmentRequested");
        };

        WebViewControl.CoreWebView2ControllerOptionsRequested += (_, e) =>
        {
            e.Options.ProfileName ??= "sample-profile";
            e.Options.ScriptLocale ??= "en-US";
            AddLog("CoreWebView2ControllerOptionsRequested");
        };
    }

    private void AttachDialogEvents(NativeWebDialog dialog)
    {
        if (_dialogEventsAttached)
        {
            return;
        }

        dialog.Shown += (_, _) => AddLog("Dialog.Shown");
        dialog.Closed += (_, _) => AddLog("Dialog.Closed");
        dialog.NavigationStarted += (_, e) => AddLog($"Dialog.NavigationStarted: uri={e.Uri}");
        dialog.NavigationCompleted += (_, e) => AddLog($"Dialog.NavigationCompleted: uri={e.Uri}, success={e.IsSuccess}");
        dialog.WebMessageReceived += (_, e) => AddLog($"Dialog.WebMessageReceived: message={e.Message ?? "<null>"}, json={e.Json ?? "<null>"}");
        dialog.NewWindowRequested += (_, e) => AddLog($"Dialog.NewWindowRequested: uri={e.Uri}");
        dialog.WebResourceRequested += (_, e) => AddLog($"Dialog.WebResourceRequested: method={e.Method}, uri={e.Uri}");
        dialog.ContextMenuRequested += (_, e) => AddLog($"Dialog.ContextMenuRequested: x={e.X:0.##}, y={e.Y:0.##}");

        _dialogEventsAttached = true;
    }

    private NativeWebDialog EnsureDialog()
    {
        _dialog ??= new NativeWebDialog();
        AttachDialogEvents(_dialog);
        return _dialog;
    }

    private WebAuthenticationBroker EnsureAuthenticationBroker()
    {
        _authenticationBroker ??= new WebAuthenticationBroker();
        return _authenticationBroker;
    }

    private async Task EnsureWebViewInitializedAsync()
    {
        if (!WebViewControl.IsInitialized)
        {
            await WebViewControl.InitializeAsync();
        }
    }

    private void SyncCheckBoxState()
    {
        DevToolsEnabledCheckBox.IsChecked = WebViewControl.IsDevToolsEnabled;
        ContextMenuEnabledCheckBox.IsChecked = WebViewControl.IsContextMenuEnabled;
        StatusBarEnabledCheckBox.IsChecked = WebViewControl.IsStatusBarEnabled;
        ZoomControlEnabledCheckBox.IsChecked = WebViewControl.IsZoomControlEnabled;
    }

    private void AddLog(string message)
    {
        _eventLog.Append('[')
            .Append(DateTimeOffset.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture))
            .Append("] ")
            .AppendLine(message);

        EventLogBox.Text = _eventLog.ToString();
        EventLogBox.CaretIndex = EventLogBox.Text?.Length ?? 0;
    }

    private void RefreshSummaries()
    {
        var featureBuilder = new StringBuilder();
        featureBuilder.Append("Platform: ").AppendLine(WebViewControl.Platform.ToString())
            .AppendLine("Features:");

        foreach (var feature in Enum.GetValues<NativeWebViewFeature>())
        {
            if (feature == NativeWebViewFeature.None)
            {
                continue;
            }

            featureBuilder.Append("- ")
                .Append(feature)
                .Append(": ")
                .AppendLine(WebViewControl.Features.Supports(feature) ? "Supported" : "Not Supported");
        }

        FeatureMatrixBox.Text = featureBuilder.ToString();

        var renderStats = WebViewControl.RenderStatistics;

        var stateBuilder = new StringBuilder();
        stateBuilder.Append("WebView State").AppendLine()
            .Append("Lifecycle: ").AppendLine(WebViewControl.LifecycleState.ToString())
            .Append("Initialized: ").AppendLine(WebViewControl.IsInitialized.ToString(CultureInfo.InvariantCulture))
            .Append("Current URL: ").AppendLine(WebViewControl.CurrentUrl?.ToString() ?? "<null>")
            .Append("CanGoBack: ").AppendLine(WebViewControl.CanGoBack.ToString(CultureInfo.InvariantCulture))
            .Append("CanGoForward: ").AppendLine(WebViewControl.CanGoForward.ToString(CultureInfo.InvariantCulture))
            .Append("ZoomFactor: ").AppendLine(WebViewControl.ZoomFactor.ToString("0.###", CultureInfo.InvariantCulture))
            .Append("IsDevToolsEnabled: ").AppendLine(WebViewControl.IsDevToolsEnabled.ToString(CultureInfo.InvariantCulture))
            .Append("IsContextMenuEnabled: ").AppendLine(WebViewControl.IsContextMenuEnabled.ToString(CultureInfo.InvariantCulture))
            .Append("IsStatusBarEnabled: ").AppendLine(WebViewControl.IsStatusBarEnabled.ToString(CultureInfo.InvariantCulture))
            .Append("IsZoomControlEnabled: ").AppendLine(WebViewControl.IsZoomControlEnabled.ToString(CultureInfo.InvariantCulture))
            .Append("HeaderString: ").AppendLine(WebViewControl.HeaderString ?? "<null>")
            .Append("UserAgentString: ").AppendLine(WebViewControl.UserAgentString ?? "<null>")
            .Append("RenderMode: ").AppendLine(WebViewControl.RenderMode.ToString())
            .Append("RenderFramesPerSecond: ").AppendLine(WebViewControl.RenderFramesPerSecond.ToString(CultureInfo.InvariantCulture))
            .Append("CompositedPassthroughOverride: ").AppendLine(FormatPassthroughOverride(WebViewControl.MacOsCompositedPassthroughOverride))
            .Append("SupportsRenderMode(Embedded): ").AppendLine(WebViewControl.SupportsRenderMode(NativeWebViewRenderMode.Embedded).ToString(CultureInfo.InvariantCulture))
            .Append("SupportsRenderMode(GpuSurface): ").AppendLine(WebViewControl.SupportsRenderMode(NativeWebViewRenderMode.GpuSurface).ToString(CultureInfo.InvariantCulture))
            .Append("SupportsRenderMode(Offscreen): ").AppendLine(WebViewControl.SupportsRenderMode(NativeWebViewRenderMode.Offscreen).ToString(CultureInfo.InvariantCulture))
            .Append("IsUsingSyntheticFrameSource: ").AppendLine(WebViewControl.IsUsingSyntheticFrameSource.ToString(CultureInfo.InvariantCulture))
            .Append("RenderDiagnosticsMessage: ").AppendLine(WebViewControl.RenderDiagnosticsMessage ?? "<null>")
            .Append("RenderStats.Attempts: ").AppendLine(renderStats.CaptureAttemptCount.ToString(CultureInfo.InvariantCulture))
            .Append("RenderStats.Successes: ").AppendLine(renderStats.CaptureSuccessCount.ToString(CultureInfo.InvariantCulture))
            .Append("RenderStats.Failures: ").AppendLine(renderStats.CaptureFailureCount.ToString(CultureInfo.InvariantCulture))
            .Append("RenderStats.Skipped: ").AppendLine(renderStats.CaptureSkippedCount.ToString(CultureInfo.InvariantCulture))
            .Append("RenderStats.SyntheticFrames: ").AppendLine(renderStats.SyntheticFrameCount.ToString(CultureInfo.InvariantCulture))
            .Append("RenderStats.NativeFrames: ").AppendLine(renderStats.NativeFrameCount.ToString(CultureInfo.InvariantCulture))
            .Append("RenderStats.LastFrameId: ").AppendLine(renderStats.LastFrameId.ToString(CultureInfo.InvariantCulture))
            .Append("RenderStats.LastFrameCapturedAtUtc: ").AppendLine(renderStats.LastFrameCapturedAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? "<null>")
            .Append("RenderStats.LastFrameRenderMode: ").AppendLine(renderStats.LastFrameRenderMode.ToString())
            .Append("RenderStats.LastFrameOrigin: ").AppendLine(renderStats.LastFrameOrigin.ToString())
            .Append("RenderStats.LastFailureMessage: ").AppendLine(renderStats.LastFailureMessage ?? "<null>")
            .Append("RenderStats.LastFailureAtUtc: ").AppendLine(renderStats.LastFailureAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? "<null>")
            .Append("LastCapturedFrame: ").AppendLine(_lastCapturedFrameInfo ?? "<null>")
            .Append("LastSavedFramePath: ").AppendLine(_lastSavedFramePath ?? "<null>")
            .Append("LastSavedFrameMetadataPath: ").AppendLine(_lastSavedFrameMetadataPath ?? "<null>")
            .Append("LastLoadedFrameMetadata: ").AppendLine(_lastLoadedFrameMetadataSummary ?? "<null>")
            .Append("LastScriptResult: ").AppendLine(_lastWebViewScriptResult ?? "<null>")
            .Append("LastPrintStatus: ").AppendLine(_lastPrintStatus?.ToString() ?? "<null>")
            .Append("LastPrintUiResult: ").AppendLine(_lastPrintUiResult?.ToString() ?? "<null>")
            .AppendLine()
            .Append("Dialog State").AppendLine()
            .Append("Created: ").AppendLine((_dialog is not null).ToString(CultureInfo.InvariantCulture));

        if (_dialog is not null)
        {
            stateBuilder.Append("Lifecycle: ").AppendLine(_dialog.LifecycleState.ToString())
                .Append("Visible: ").AppendLine(_dialog.IsVisible.ToString(CultureInfo.InvariantCulture))
                .Append("Current URL: ").AppendLine(_dialog.CurrentUrl?.ToString() ?? "<null>")
                .Append("CanGoBack: ").AppendLine(_dialog.CanGoBack.ToString(CultureInfo.InvariantCulture))
                .Append("CanGoForward: ").AppendLine(_dialog.CanGoForward.ToString(CultureInfo.InvariantCulture))
                .Append("ZoomFactor: ").AppendLine(_dialog.ZoomFactor.ToString("0.###", CultureInfo.InvariantCulture))
                .Append("LastScriptResult: ").AppendLine(_lastDialogScriptResult ?? "<null>")
                .Append("LastPrintStatus: ").AppendLine(_lastDialogPrintStatus?.ToString() ?? "<null>")
                .Append("LastPrintUiResult: ").AppendLine(_lastDialogPrintUiResult?.ToString() ?? "<null>");
        }

        stateBuilder.AppendLine()
            .Append("Authentication State").AppendLine()
            .Append("Created: ").AppendLine((_authenticationBroker is not null).ToString(CultureInfo.InvariantCulture))
            .Append("Broker State: ").AppendLine(_authenticationBroker?.State.ToString() ?? "<null>")
            .Append("LastAuthResult: ").AppendLine(_lastAuthStatus ?? "<null>");

        StateSummaryBox.Text = stateBuilder.ToString();

        var handleBuilder = new StringBuilder();
        handleBuilder.AppendLine("WebView Handles")
            .AppendLine("--------------")
            .AppendLine($"Platform: {FormatHandle(WebViewControl.TryGetPlatformHandle(out var webViewPlatformHandle), webViewPlatformHandle)}")
            .AppendLine($"View: {FormatHandle(WebViewControl.TryGetViewHandle(out var webViewHandle), webViewHandle)}")
            .AppendLine($"Controller: {FormatHandle(WebViewControl.TryGetControllerHandle(out var webViewControllerHandle), webViewControllerHandle)}")
            .AppendLine()
            .AppendLine("Dialog Handles")
            .AppendLine("------------");

        if (_dialog is null)
        {
            handleBuilder.AppendLine("Dialog not created.");
        }
        else
        {
            handleBuilder.AppendLine($"Platform: {FormatHandle(_dialog.TryGetPlatformHandle(out var dialogPlatformHandle), dialogPlatformHandle)}")
                .AppendLine($"Dialog: {FormatHandle(_dialog.TryGetDialogHandle(out var dialogHandle), dialogHandle)}")
                .AppendLine($"Host: {FormatHandle(_dialog.TryGetHostWindowHandle(out var hostHandle), hostHandle)}");
        }

        HandleSummaryBox.Text = handleBuilder.ToString();
    }

    private static string FormatHandle(bool available, NativePlatformHandle handle)
    {
        return available
            ? $"0x{handle.Handle.ToInt64():X} ({handle.HandleDescriptor})"
            : "Unavailable";
    }

    private static string FormatException(Exception ex)
    {
        return $"{ex.GetType().Name}: {ex.Message}";
    }

    private static string FormatPassthroughOverride(bool? value)
    {
        return value switch
        {
            true => "ForcedOn",
            false => "ForcedOff",
            null => "Auto",
        };
    }

    private static bool TryParseUri(string? raw, out Uri uri)
    {
        if (!string.IsNullOrWhiteSpace(raw) && Uri.TryCreate(raw.Trim(), UriKind.Absolute, out uri!))
        {
            return true;
        }

        uri = null!;
        return false;
    }

    private static bool TryParseDouble(string? raw, out double value)
    {
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private async Task RunWebViewActionAsync(string name, Func<Task> action)
    {
        try
        {
            await EnsureWebViewInitializedAsync();
            await action();
            AddLog($"[WebView] {name}: OK");
        }
        catch (Exception ex)
        {
            AddLog($"[WebView] {name}: {FormatException(ex)}");
        }
        finally
        {
            RefreshSummaries();
        }
    }

    private void RunWebViewAction(string name, Action action)
    {
        try
        {
            action();
            AddLog($"[WebView] {name}: OK");
        }
        catch (Exception ex)
        {
            AddLog($"[WebView] {name}: {FormatException(ex)}");
        }
        finally
        {
            RefreshSummaries();
        }
    }

    private async Task RunDialogActionAsync(string name, Func<NativeWebDialog, Task> action)
    {
        try
        {
            var dialog = EnsureDialog();
            await action(dialog);
            AddLog($"[Dialog] {name}: OK");
        }
        catch (Exception ex)
        {
            AddLog($"[Dialog] {name}: {FormatException(ex)}");
        }
        finally
        {
            RefreshSummaries();
        }
    }

    private void RunDialogAction(string name, Action<NativeWebDialog> action)
    {
        try
        {
            var dialog = EnsureDialog();
            action(dialog);
            AddLog($"[Dialog] {name}: OK");
        }
        catch (Exception ex)
        {
            AddLog($"[Dialog] {name}: {FormatException(ex)}");
        }
        finally
        {
            RefreshSummaries();
        }
    }

    private async Task RunAuthActionAsync(string name, Func<WebAuthenticationBroker, Task> action)
    {
        try
        {
            var broker = EnsureAuthenticationBroker();
            await action(broker);
            AddLog($"[Auth] {name}: OK");
        }
        catch (Exception ex)
        {
            AddLog($"[Auth] {name}: {FormatException(ex)}");
        }
        finally
        {
            RefreshSummaries();
        }
    }

    private async void NavigateButtonOnClick(object? sender, RoutedEventArgs e)
    {
        await RunWebViewActionAsync("Navigate", () =>
        {
            if (!TryParseUri(UrlTextBox.Text, out var uri))
            {
                throw new ArgumentException("Invalid URL.");
            }

            WebViewControl.Navigate(uri);
            return Task.CompletedTask;
        });
    }

    private async void BackButtonOnClick(object? sender, RoutedEventArgs e)
    {
        await RunWebViewActionAsync("GoBack", () =>
        {
            WebViewControl.GoBack();
            return Task.CompletedTask;
        });
    }

    private async void ForwardButtonOnClick(object? sender, RoutedEventArgs e)
    {
        await RunWebViewActionAsync("GoForward", () =>
        {
            WebViewControl.GoForward();
            return Task.CompletedTask;
        });
    }

    private async void ReloadButtonOnClick(object? sender, RoutedEventArgs e)
    {
        await RunWebViewActionAsync("Reload", () =>
        {
            WebViewControl.Reload();
            return Task.CompletedTask;
        });
    }

    private async void StopButtonOnClick(object? sender, RoutedEventArgs e)
    {
        await RunWebViewActionAsync("Stop", () =>
        {
            WebViewControl.Stop();
            return Task.CompletedTask;
        });
    }

    private async void ExecuteScriptButtonOnClick(object? sender, RoutedEventArgs e)
    {
        await RunWebViewActionAsync("ExecuteScript", async () =>
        {
            _lastWebViewScriptResult = await WebViewControl.ExecuteScriptAsync(ScriptTextBox.Text ?? "");
            AddLog($"Script result: {_lastWebViewScriptResult ?? "<null>"}");
        });
    }

    private async void PostMessageStringButtonOnClick(object? sender, RoutedEventArgs e)
    {
        await RunWebViewActionAsync("PostWebMessageAsString", () =>
            WebViewControl.PostWebMessageAsStringAsync(MessageTextBox.Text ?? ""));
    }

    private async void PostMessageJsonButtonOnClick(object? sender, RoutedEventArgs e)
    {
        await RunWebViewActionAsync("PostWebMessageAsJson", () =>
            WebViewControl.PostWebMessageAsJsonAsync(MessageTextBox.Text ?? "{}"));
    }

    private async void SetZoomButtonOnClick(object? sender, RoutedEventArgs e)
    {
        await RunWebViewActionAsync("SetZoomFactor", () =>
        {
            if (!TryParseDouble(ZoomTextBox.Text, out var zoom))
            {
                throw new ArgumentException("Invalid zoom value.");
            }

            WebViewControl.SetZoomFactor(zoom);
            return Task.CompletedTask;
        });
    }

    private async void SetUserAgentButtonOnClick(object? sender, RoutedEventArgs e)
    {
        await RunWebViewActionAsync("SetUserAgent", () =>
        {
            WebViewControl.SetUserAgent(UserAgentTextBox.Text);
            return Task.CompletedTask;
        });
    }

    private async void SetHeaderButtonOnClick(object? sender, RoutedEventArgs e)
    {
        await RunWebViewActionAsync("SetHeader", () =>
        {
            WebViewControl.SetHeader(HeaderValueTextBox.Text);
            return Task.CompletedTask;
        });
    }

    private async void OpenDevToolsButtonOnClick(object? sender, RoutedEventArgs e)
    {
        await RunWebViewActionAsync("OpenDevToolsWindow", () =>
        {
            WebViewControl.OpenDevToolsWindow();
            return Task.CompletedTask;
        });
    }

    private async void PrintButtonOnClick(object? sender, RoutedEventArgs e)
    {
        await RunWebViewActionAsync("PrintAsync", async () =>
        {
            var result = await WebViewControl.PrintAsync(new NativeWebViewPrintSettings
            {
                OutputPath = "artifacts/sample-webview-print.pdf",
                BackgroundsEnabled = true,
            });
            _lastPrintStatus = result.Status;
            AddLog($"PrintAsync status: {result.Status}, error={result.ErrorMessage ?? "<null>"}");
        });
    }

    private async void PrintUiButtonOnClick(object? sender, RoutedEventArgs e)
    {
        await RunWebViewActionAsync("ShowPrintUiAsync", async () =>
        {
            _lastPrintUiResult = await WebViewControl.ShowPrintUiAsync();
            AddLog($"ShowPrintUiAsync result: {_lastPrintUiResult}");
        });
    }

    private async void MoveFocusNextButtonOnClick(object? sender, RoutedEventArgs e)
    {
        await RunWebViewActionAsync("MoveFocus(Next)", () =>
        {
            WebViewControl.MoveFocus(NativeWebViewFocusMoveDirection.Next);
            return Task.CompletedTask;
        });
    }

    private async void MoveFocusPreviousButtonOnClick(object? sender, RoutedEventArgs e)
    {
        await RunWebViewActionAsync("MoveFocus(Previous)", () =>
        {
            WebViewControl.MoveFocus(NativeWebViewFocusMoveDirection.Previous);
            return Task.CompletedTask;
        });
    }

    private async void MoveFocusProgrammaticButtonOnClick(object? sender, RoutedEventArgs e)
    {
        await RunWebViewActionAsync("MoveFocus(Programmatic)", () =>
        {
            WebViewControl.MoveFocus(NativeWebViewFocusMoveDirection.Programmatic);
            return Task.CompletedTask;
        });
    }

    private void CommandManagerButtonOnClick(object? sender, RoutedEventArgs e)
    {
        RunWebViewAction("TryGetCommandManager/TryExecute", () =>
        {
            if (!WebViewControl.TryGetCommandManager(out var manager) || manager is null)
            {
                throw new InvalidOperationException("Command manager is unavailable.");
            }

            var executed = manager.TryExecute("sample.command", "payload");
            AddLog($"Command manager TryExecute returned: {executed}");
        });
    }

    private async void CookieReadButtonOnClick(object? sender, RoutedEventArgs e)
    {
        await RunWebViewActionAsync("GetCookiesAsync", async () =>
        {
            if (!WebViewControl.TryGetCookieManager(out var manager) || manager is null)
            {
                throw new InvalidOperationException("Cookie manager is unavailable.");
            }

            if (!TryParseUri(UrlTextBox.Text, out var uri))
            {
                throw new ArgumentException("Invalid URL for cookie lookup.");
            }

            var cookies = await manager.GetCookiesAsync(uri);
            AddLog($"Cookie count: {cookies.Count}");
        });
    }

    private async void CookieSetButtonOnClick(object? sender, RoutedEventArgs e)
    {
        await RunWebViewActionAsync("SetCookieAsync", async () =>
        {
            if (!WebViewControl.TryGetCookieManager(out var manager) || manager is null)
            {
                throw new InvalidOperationException("Cookie manager is unavailable.");
            }

            if (!TryParseUri(UrlTextBox.Text, out var uri))
            {
                throw new ArgumentException("Invalid URL for cookie update.");
            }

            await manager.SetCookieAsync(uri, "sample-cookie", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        });
    }

    private async void CookieDeleteButtonOnClick(object? sender, RoutedEventArgs e)
    {
        await RunWebViewActionAsync("DeleteCookieAsync", async () =>
        {
            if (!WebViewControl.TryGetCookieManager(out var manager) || manager is null)
            {
                throw new InvalidOperationException("Cookie manager is unavailable.");
            }

            if (!TryParseUri(UrlTextBox.Text, out var uri))
            {
                throw new ArgumentException("Invalid URL for cookie update.");
            }

            await manager.DeleteCookieAsync(uri, "sample-cookie");
        });
    }

    private void RefreshStateButtonOnClick(object? sender, RoutedEventArgs e)
    {
        RefreshSummaries();
        AddLog("State summary refreshed.");
    }

    private void ToggleAdvancedPanelsMenuItemOnClick(object? sender, RoutedEventArgs e)
    {
        AdvancedTabControl.IsVisible = !AdvancedTabControl.IsVisible;
        AddLog($"Advanced controls panel: {(AdvancedTabControl.IsVisible ? "visible" : "hidden")}");
    }

    private void ToggleManagedOverlayMenuItemOnClick(object? sender, RoutedEventArgs e)
    {
        ManagedOverlayPanel.IsVisible = !ManagedOverlayPanel.IsVisible;
        if (ManagedOverlayPanel.IsVisible)
        {
            ManagedOverlayTextBox.Focus();
        }

        AddLog($"Managed overlay panel: {(ManagedOverlayPanel.IsVisible ? "visible" : "hidden")}");
    }

    private void ManagedOverlayActionButtonOnClick(object? sender, RoutedEventArgs e)
    {
        AddLog($"Managed overlay action invoked. Text='{ManagedOverlayTextBox.Text ?? "<null>"}'");
    }

    private void ToggleCompositedPassthroughOverrideMenuItemOnClick(object? sender, RoutedEventArgs e)
    {
        _manualCompositedPassthroughOverride = _manualCompositedPassthroughOverride switch
        {
            null => true,
            true => false,
            false => null,
        };

        RunWebViewAction("Set Composited Passthrough Override", () =>
        {
            WebViewControl.SetCompositedPassthroughOverride(_manualCompositedPassthroughOverride);
            AddLog($"Composited passthrough override: {FormatPassthroughOverride(_manualCompositedPassthroughOverride)}");
        });
    }

    private void ShowWebViewPanelMenuItemOnClick(object? sender, RoutedEventArgs e)
    {
        ShowAdvancedPanel(index: 0, "WebView");
    }

    private void ShowDialogPanelMenuItemOnClick(object? sender, RoutedEventArgs e)
    {
        ShowAdvancedPanel(index: 1, "Dialog");
    }

    private void ShowAuthPanelMenuItemOnClick(object? sender, RoutedEventArgs e)
    {
        ShowAdvancedPanel(index: 2, "Auth");
    }

    private void UseEmbeddedRenderModeMenuItemOnClick(object? sender, RoutedEventArgs e)
    {
        SetRenderMode(NativeWebViewRenderMode.Embedded);
    }

    private void UseGpuSurfaceRenderModeMenuItemOnClick(object? sender, RoutedEventArgs e)
    {
        SetRenderMode(NativeWebViewRenderMode.GpuSurface);
    }

    private void UseOffscreenRenderModeMenuItemOnClick(object? sender, RoutedEventArgs e)
    {
        SetRenderMode(NativeWebViewRenderMode.Offscreen);
    }

    private void BoostRenderFpsMenuItemOnClick(object? sender, RoutedEventArgs e)
    {
        ChangeRenderFps(delta: 5);
    }

    private void ReduceRenderFpsMenuItemOnClick(object? sender, RoutedEventArgs e)
    {
        ChangeRenderFps(delta: -5);
    }

    private void SetRenderMode(NativeWebViewRenderMode renderMode)
    {
        RunWebViewAction($"Set RenderMode ({renderMode})", () =>
        {
            if (!WebViewControl.SupportsRenderMode(renderMode))
            {
                throw new PlatformNotSupportedException(
                    $"Render mode '{renderMode}' is not supported on platform '{WebViewControl.Platform}'.");
            }

            WebViewControl.RenderMode = renderMode;
        });
    }

    private void ChangeRenderFps(int delta)
    {
        RunWebViewAction($"Change Render FPS ({delta:+#;-#;0})", () =>
        {
            var value = Math.Clamp(WebViewControl.RenderFramesPerSecond + delta, 1, 60);
            WebViewControl.RenderFramesPerSecond = value;
            AddLog($"Render FPS set to {value}.");
        });
    }

    private async void CaptureRenderFrameMenuItemOnClick(object? sender, RoutedEventArgs e)
    {
        await RunWebViewActionAsync("CaptureRenderFrameAsync", async () =>
        {
            var frame = await WebViewControl.CaptureRenderFrameAsync();
            if (frame is null)
            {
                _lastCapturedFrameInfo = null;
                AddLog("CaptureRenderFrameAsync returned no frame.");
                return;
            }

            _lastCapturedFrameInfo =
                $"id={frame.FrameId}, utc={frame.CapturedAtUtc:O}, mode={frame.RenderMode}, origin={frame.Origin}, " +
                $"{frame.PixelWidth}x{frame.PixelHeight}, bytesPerRow={frame.BytesPerRow}, synthetic={frame.IsSynthetic}";
            AddLog($"Captured frame: {_lastCapturedFrameInfo}");
        });
    }

    private async void SaveRenderFrameMenuItemOnClick(object? sender, RoutedEventArgs e)
    {
        await RunWebViewActionAsync("SaveRenderFrameAsync", async () =>
        {
            var outputPath = "artifacts/sample-webview-render-frame.png";
            var saved = await WebViewControl.SaveRenderFrameAsync(outputPath);
            if (!saved)
            {
                _lastSavedFramePath = null;
                _lastSavedFrameMetadataPath = null;
                AddLog("SaveRenderFrameAsync returned false.");
                return;
            }

            _lastSavedFramePath = Path.GetFullPath(outputPath);
            _lastSavedFrameMetadataPath = null;
            AddLog($"Saved render frame: {_lastSavedFramePath}");
        });
    }

    private async void SaveRenderFrameWithMetadataMenuItemOnClick(object? sender, RoutedEventArgs e)
    {
        await RunWebViewActionAsync("SaveRenderFrameWithMetadataAsync", async () =>
        {
            var outputPath = "artifacts/sample-webview-render-frame-with-metadata.png";
            var metadataPath = "artifacts/sample-webview-render-frame-with-metadata.json";
            var saved = await WebViewControl.SaveRenderFrameWithMetadataAsync(outputPath, metadataPath);
            if (!saved)
            {
                _lastSavedFramePath = null;
                _lastSavedFrameMetadataPath = null;
                AddLog("SaveRenderFrameWithMetadataAsync returned false.");
                return;
            }

            _lastSavedFramePath = Path.GetFullPath(outputPath);
            _lastSavedFrameMetadataPath = Path.GetFullPath(metadataPath);
            AddLog($"Saved render frame with metadata: {_lastSavedFramePath} + {_lastSavedFrameMetadataPath} (includes PixelDataSha256)");
        });
    }

    private async void LoadRenderFrameMetadataMenuItemOnClick(object? sender, RoutedEventArgs e)
    {
        await RunWebViewActionAsync("LoadRenderFrameMetadata", async () =>
        {
            if (string.IsNullOrWhiteSpace(_lastSavedFrameMetadataPath))
            {
                _lastLoadedFrameMetadataSummary = null;
                AddLog("No saved render metadata path is available.");
                return;
            }

            var metadata = await NativeWebViewRenderFrameMetadataSerializer.ReadFromFileAsync(_lastSavedFrameMetadataPath);
            _lastLoadedFrameMetadataSummary =
                $"v={metadata.FormatVersion}, frameId={metadata.FrameId}, hash={metadata.PixelDataSha256}, length={metadata.PixelDataLength}";
            AddLog($"Loaded render frame metadata: {_lastLoadedFrameMetadataSummary}");
        });
    }

    private void ResetRenderStatisticsMenuItemOnClick(object? sender, RoutedEventArgs e)
    {
        RunWebViewAction("ResetRenderStatistics", () =>
        {
            WebViewControl.ResetRenderStatistics();
            AddLog("Render statistics reset.");
        });
    }

    private void ShowAdvancedPanel(int index, string panelName)
    {
        AdvancedTabControl.IsVisible = true;
        AdvancedTabControl.SelectedIndex = index;
        AddLog($"Advanced controls panel opened: {panelName}");
    }

    private void DevToolsEnabledCheckBoxOnChecked(object? sender, RoutedEventArgs e)
    {
        RunWebViewAction("Set IsDevToolsEnabled", () => WebViewControl.IsDevToolsEnabled = DevToolsEnabledCheckBox.IsChecked ?? false);
    }

    private void ContextMenuEnabledCheckBoxOnChecked(object? sender, RoutedEventArgs e)
    {
        RunWebViewAction("Set IsContextMenuEnabled", () => WebViewControl.IsContextMenuEnabled = ContextMenuEnabledCheckBox.IsChecked ?? false);
    }

    private void StatusBarEnabledCheckBoxOnChecked(object? sender, RoutedEventArgs e)
    {
        RunWebViewAction("Set IsStatusBarEnabled", () => WebViewControl.IsStatusBarEnabled = StatusBarEnabledCheckBox.IsChecked ?? false);
    }

    private void ZoomControlEnabledCheckBoxOnChecked(object? sender, RoutedEventArgs e)
    {
        RunWebViewAction("Set IsZoomControlEnabled", () => WebViewControl.IsZoomControlEnabled = ZoomControlEnabledCheckBox.IsChecked ?? false);
    }

    private void DialogShowButtonOnClick(object? sender, RoutedEventArgs e)
    {
        RunDialogAction("Show", dialog =>
        {
            var options = new NativeWebDialogShowOptions();
            if (TryParseDouble(DialogWidthTextBox.Text, out var width))
            {
                options.Width = width;
            }

            if (TryParseDouble(DialogHeightTextBox.Text, out var height))
            {
                options.Height = height;
            }

            if (TryParseDouble(DialogLeftTextBox.Text, out var left))
            {
                options.Left = left;
            }

            if (TryParseDouble(DialogTopTextBox.Text, out var top))
            {
                options.Top = top;
            }

            options.Title = "NativeWebView Sample Dialog";
            options.CenterOnParent = false;
            dialog.Show(options);

            if (TryParseUri(DialogUrlTextBox.Text, out var uri))
            {
                dialog.Navigate(uri);
            }
        });
    }

    private void DialogCloseButtonOnClick(object? sender, RoutedEventArgs e)
    {
        RunDialogAction("Close", static dialog => dialog.Close());
    }

    private void DialogNavigateButtonOnClick(object? sender, RoutedEventArgs e)
    {
        RunDialogAction("Navigate", dialog =>
        {
            if (!TryParseUri(DialogUrlTextBox.Text, out var uri))
            {
                throw new ArgumentException("Invalid dialog URL.");
            }

            dialog.Navigate(uri);
        });
    }

    private void DialogBackButtonOnClick(object? sender, RoutedEventArgs e)
    {
        RunDialogAction("GoBack", static dialog => dialog.GoBack());
    }

    private void DialogForwardButtonOnClick(object? sender, RoutedEventArgs e)
    {
        RunDialogAction("GoForward", static dialog => dialog.GoForward());
    }

    private void DialogReloadButtonOnClick(object? sender, RoutedEventArgs e)
    {
        RunDialogAction("Reload", static dialog => dialog.Reload());
    }

    private void DialogStopButtonOnClick(object? sender, RoutedEventArgs e)
    {
        RunDialogAction("Stop", static dialog => dialog.Stop());
    }

    private async void DialogExecuteScriptButtonOnClick(object? sender, RoutedEventArgs e)
    {
        await RunDialogActionAsync("ExecuteScript", async dialog =>
        {
            _lastDialogScriptResult = await dialog.ExecuteScriptAsync(DialogScriptTextBox.Text ?? "");
            AddLog($"Dialog script result: {_lastDialogScriptResult ?? "<null>"}");
        });
    }

    private async void DialogPostStringButtonOnClick(object? sender, RoutedEventArgs e)
    {
        await RunDialogActionAsync("PostWebMessageAsString", dialog =>
            dialog.PostWebMessageAsStringAsync(DialogMessageTextBox.Text ?? ""));
    }

    private async void DialogPostJsonButtonOnClick(object? sender, RoutedEventArgs e)
    {
        await RunDialogActionAsync("PostWebMessageAsJson", dialog =>
            dialog.PostWebMessageAsJsonAsync(DialogMessageTextBox.Text ?? "{}"));
    }

    private void DialogMoveButtonOnClick(object? sender, RoutedEventArgs e)
    {
        RunDialogAction("Move", dialog =>
        {
            if (!TryParseDouble(DialogLeftTextBox.Text, out var left) || !TryParseDouble(DialogTopTextBox.Text, out var top))
            {
                throw new ArgumentException("Invalid dialog move coordinates.");
            }

            dialog.Move(left, top);
        });
    }

    private void DialogResizeButtonOnClick(object? sender, RoutedEventArgs e)
    {
        RunDialogAction("Resize", dialog =>
        {
            if (!TryParseDouble(DialogWidthTextBox.Text, out var width) || !TryParseDouble(DialogHeightTextBox.Text, out var height))
            {
                throw new ArgumentException("Invalid dialog size values.");
            }

            dialog.Resize(width, height);
        });
    }

    private void DialogSetZoomButtonOnClick(object? sender, RoutedEventArgs e)
    {
        RunDialogAction("SetZoomFactor", dialog =>
        {
            if (!TryParseDouble(DialogZoomTextBox.Text, out var zoom))
            {
                throw new ArgumentException("Invalid dialog zoom value.");
            }

            dialog.SetZoomFactor(zoom);
        });
    }

    private void DialogSetUserAgentButtonOnClick(object? sender, RoutedEventArgs e)
    {
        RunDialogAction("SetUserAgent", dialog => dialog.SetUserAgent(DialogUserAgentTextBox.Text));
    }

    private void DialogSetHeaderButtonOnClick(object? sender, RoutedEventArgs e)
    {
        RunDialogAction("SetHeader", dialog => dialog.SetHeader(DialogHeaderTextBox.Text));
    }

    private void DialogOpenDevToolsButtonOnClick(object? sender, RoutedEventArgs e)
    {
        RunDialogAction("OpenDevToolsWindow", static dialog => dialog.OpenDevToolsWindow());
    }

    private async void DialogPrintButtonOnClick(object? sender, RoutedEventArgs e)
    {
        await RunDialogActionAsync("PrintAsync", async dialog =>
        {
            var result = await dialog.PrintAsync(new NativeWebViewPrintSettings
            {
                OutputPath = "artifacts/sample-dialog-print.pdf",
                Landscape = true,
            });
            _lastDialogPrintStatus = result.Status;
            AddLog($"Dialog PrintAsync status: {result.Status}, error={result.ErrorMessage ?? "<null>"}");
        });
    }

    private async void DialogPrintUiButtonOnClick(object? sender, RoutedEventArgs e)
    {
        await RunDialogActionAsync("ShowPrintUiAsync", async dialog =>
        {
            _lastDialogPrintUiResult = await dialog.ShowPrintUiAsync();
            AddLog($"Dialog ShowPrintUiAsync result: {_lastDialogPrintUiResult}");
        });
    }

    private async void AuthenticateButtonOnClick(object? sender, RoutedEventArgs e)
    {
        await RunAuthActionAsync("AuthenticateAsync", async broker =>
        {
            if (!TryParseUri(AuthRequestUriTextBox.Text, out var requestUri))
            {
                throw new ArgumentException("Invalid request URI.");
            }

            if (!TryParseUri(AuthCallbackUriTextBox.Text, out var callbackUri))
            {
                throw new ArgumentException("Invalid callback URI.");
            }

            var options = WebAuthenticationOptions.None;
            if (AuthSilentModeCheckBox.IsChecked == true)
            {
                options |= WebAuthenticationOptions.SilentMode;
            }

            if (AuthUseTitleCheckBox.IsChecked == true)
            {
                options |= WebAuthenticationOptions.UseTitle;
            }

            if (AuthUseHttpPostCheckBox.IsChecked == true)
            {
                options |= WebAuthenticationOptions.UseHttpPost;
            }

            if (AuthUseCorporateNetworkCheckBox.IsChecked == true)
            {
                options |= WebAuthenticationOptions.UseCorporateNetwork;
            }

            if (AuthUseBrokerCheckBox.IsChecked == true)
            {
                options |= WebAuthenticationOptions.UseWebAuthenticationBroker;
            }

            var result = await broker.AuthenticateAsync(requestUri, callbackUri, options);
            _lastAuthStatus = $"Status={result.ResponseStatus}, ErrorDetail={result.ResponseErrorDetail}, Data={result.ResponseData ?? "<null>"}";
            AuthResultTextBlock.Text = $"Authentication result: {_lastAuthStatus}";
            AddLog($"AuthenticateAsync result: {_lastAuthStatus}");
        });
    }

}
