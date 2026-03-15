using NativeWebView.Core;
using NativeWebView.Interop;

namespace NativeWebView.Dialog;

public sealed class NativeWebDialog : IDisposable
{
    private readonly NativeWebDialogController _controller;
    private NativeWebViewInstanceConfiguration _instanceConfiguration;

    public NativeWebDialog()
        : this(CreateDefaultBackend())
    {
    }

    public NativeWebDialog(INativeWebDialogBackend backend)
    {
        _controller = new NativeWebDialogController(backend);
        _instanceConfiguration = new NativeWebViewInstanceConfiguration();
        ApplyInstanceConfigurationToBackend();
    }

    public NativeWebViewPlatform Platform => _controller.Platform;

    public IWebViewPlatformFeatures Features => _controller.Features;

    public NativeWebComponentState LifecycleState => _controller.State;

    public NativeWebViewInstanceConfiguration InstanceConfiguration
    {
        get => _instanceConfiguration;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _instanceConfiguration = value.Clone();
            ApplyInstanceConfigurationToBackend();
        }
    }

    public bool IsVisible => _controller.IsVisible;

    public Uri? CurrentUrl => _controller.CurrentUrl;

    public bool CanGoBack => _controller.CanGoBack;

    public bool CanGoForward => _controller.CanGoForward;

    public bool IsDevToolsEnabled
    {
        get => _controller.IsDevToolsEnabled;
        set => _controller.IsDevToolsEnabled = value;
    }

    public bool IsContextMenuEnabled
    {
        get => _controller.IsContextMenuEnabled;
        set => _controller.IsContextMenuEnabled = value;
    }

    public bool IsStatusBarEnabled
    {
        get => _controller.IsStatusBarEnabled;
        set => _controller.IsStatusBarEnabled = value;
    }

    public bool IsZoomControlEnabled
    {
        get => _controller.IsZoomControlEnabled;
        set => _controller.IsZoomControlEnabled = value;
    }

    public double ZoomFactor => _controller.ZoomFactor;

    public string? HeaderString => _controller.HeaderString;

    public string? UserAgentString => _controller.UserAgentString;

    public event EventHandler<EventArgs>? Shown
    {
        add => _controller.Shown += value;
        remove => _controller.Shown -= value;
    }

    public event EventHandler<EventArgs>? Closed
    {
        add => _controller.Closed += value;
        remove => _controller.Closed -= value;
    }

    public event EventHandler<NativeWebViewNavigationStartedEventArgs>? NavigationStarted
    {
        add => _controller.NavigationStarted += value;
        remove => _controller.NavigationStarted -= value;
    }

    public event EventHandler<NativeWebViewNavigationCompletedEventArgs>? NavigationCompleted
    {
        add => _controller.NavigationCompleted += value;
        remove => _controller.NavigationCompleted -= value;
    }

    public event EventHandler<NativeWebViewMessageReceivedEventArgs>? WebMessageReceived
    {
        add => _controller.WebMessageReceived += value;
        remove => _controller.WebMessageReceived -= value;
    }

    public event EventHandler<NativeWebViewNewWindowRequestedEventArgs>? NewWindowRequested
    {
        add => _controller.NewWindowRequested += value;
        remove => _controller.NewWindowRequested -= value;
    }

    public event EventHandler<NativeWebViewResourceRequestedEventArgs>? WebResourceRequested
    {
        add => _controller.WebResourceRequested += value;
        remove => _controller.WebResourceRequested -= value;
    }

    public event EventHandler<NativeWebViewContextMenuRequestedEventArgs>? ContextMenuRequested
    {
        add => _controller.ContextMenuRequested += value;
        remove => _controller.ContextMenuRequested -= value;
    }

    public void Show(NativeWebDialogShowOptions? options = null)
    {
        ApplyInstanceConfigurationToBackend();
        _controller.Show(options);
    }

    public void Close()
    {
        _controller.Close();
    }

    public void Move(double left, double top)
    {
        _controller.Move(left, top);
    }

    public void Resize(double width, double height)
    {
        _controller.Resize(width, height);
    }

    public void Navigate(string url)
    {
        ApplyInstanceConfigurationToBackend();
        _controller.Navigate(url);
    }

    public void Navigate(Uri uri)
    {
        ApplyInstanceConfigurationToBackend();
        _controller.Navigate(uri);
    }

    public void Reload()
    {
        _controller.Reload();
    }

    public void Stop()
    {
        _controller.Stop();
    }

    public void GoBack()
    {
        _controller.GoBack();
    }

    public void GoForward()
    {
        _controller.GoForward();
    }

    public Task<string?> ExecuteScriptAsync(string script, CancellationToken cancellationToken = default)
    {
        return _controller.ExecuteScriptAsync(script, cancellationToken);
    }

    public Task PostWebMessageAsJsonAsync(string message, CancellationToken cancellationToken = default)
    {
        return _controller.PostWebMessageAsJsonAsync(message, cancellationToken);
    }

    public Task PostWebMessageAsStringAsync(string message, CancellationToken cancellationToken = default)
    {
        return _controller.PostWebMessageAsStringAsync(message, cancellationToken);
    }

    public void OpenDevToolsWindow()
    {
        _controller.OpenDevToolsWindow();
    }

    public Task<NativeWebViewPrintResult> PrintAsync(NativeWebViewPrintSettings? settings = null, CancellationToken cancellationToken = default)
    {
        return _controller.PrintAsync(settings, cancellationToken);
    }

    public Task<bool> ShowPrintUiAsync(CancellationToken cancellationToken = default)
    {
        return _controller.ShowPrintUiAsync(cancellationToken);
    }

    public void SetZoomFactor(double zoomFactor)
    {
        _controller.SetZoomFactor(zoomFactor);
    }

    public void SetUserAgent(string? userAgent)
    {
        _controller.SetUserAgent(userAgent);
    }

    public void SetHeader(string? header)
    {
        _controller.SetHeader(header);
    }

    public bool TryGetPlatformHandle(out NativePlatformHandle handle)
    {
        if (_controller.TryGetBackend<INativeWebDialogPlatformHandleProvider>(out var provider) &&
            provider.TryGetPlatformHandle(out handle))
        {
            return true;
        }

        handle = default;
        return false;
    }

    public bool TryGetDialogHandle(out NativePlatformHandle handle)
    {
        if (_controller.TryGetBackend<INativeWebDialogPlatformHandleProvider>(out var provider) &&
            provider.TryGetDialogHandle(out handle))
        {
            return true;
        }

        handle = default;
        return false;
    }

    public bool TryGetHostWindowHandle(out NativePlatformHandle handle)
    {
        if (_controller.TryGetBackend<INativeWebDialogPlatformHandleProvider>(out var provider) &&
            provider.TryGetHostWindowHandle(out handle))
        {
            return true;
        }

        handle = default;
        return false;
    }

    public void Dispose()
    {
        _controller.Dispose();
    }

    private void ApplyInstanceConfigurationToBackend()
    {
        if (_controller.TryGetBackend<INativeWebViewInstanceConfigurationTarget>(out var target))
        {
            target.ApplyInstanceConfiguration(_instanceConfiguration.Clone());
        }
    }

    private static INativeWebDialogBackend CreateDefaultBackend()
    {
        NativeWebViewRuntime.EnsureCurrentPlatformRegistered();
        NativeWebViewRuntime.Factory.TryCreateNativeWebDialogBackend(NativeWebViewRuntime.CurrentPlatform, out var backend);
        return backend;
    }
}
