using System.Collections.ObjectModel;

namespace NativeWebView.Core;

public enum NativeWebViewPlatform
{
    Unknown = 0,
    Windows,
    MacOS,
    Linux,
    IOS,
    Android,
    Browser
}

[Flags]
public enum NativeWebViewFeature
{
    None = 0,
    EmbeddedView = 1 << 0,
    Dialog = 1 << 1,
    AuthenticationBroker = 1 << 2,
    DevTools = 1 << 3,
    ContextMenu = 1 << 4,
    StatusBar = 1 << 5,
    ZoomControl = 1 << 6,
    Printing = 1 << 7,
    PrintUi = 1 << 8,
    WebResourceRequestInterception = 1 << 9,
    NewWindowRequestInterception = 1 << 10,
    EnvironmentOptions = 1 << 11,
    ControllerOptions = 1 << 12,
    NativePlatformHandle = 1 << 13,
    CookieManager = 1 << 14,
    CommandManager = 1 << 15,
    CustomChrome = 1 << 16,
    WindowMoveResize = 1 << 17,
    ScriptExecution = 1 << 18,
    WebMessageChannel = 1 << 19,
    GpuSurfaceRendering = 1 << 20,
    OffscreenRendering = 1 << 21,
    RenderFrameCapture = 1 << 22,
    ProxyConfiguration = 1 << 23
}

[Flags]
public enum WebAuthenticationOptions
{
    None = 0,
    SilentMode = 1 << 0,
    UseTitle = 1 << 1,
    UseHttpPost = 1 << 2,
    UseCorporateNetwork = 1 << 3,
    UseWebAuthenticationBroker = 1 << 4
}

public enum WebAuthenticationStatus
{
    Success = 0,
    UserCancel = 1,
    ErrorHttp = 2
}

public enum NativeWebViewFocusMoveDirection
{
    Programmatic = 0,
    Next,
    Previous
}

public enum NativeWebViewRenderMode
{
    Embedded = 0,
    GpuSurface,
    Offscreen
}

public enum NativeWindowResizeEdge
{
    None = 0,
    Left,
    Top,
    Right,
    Bottom,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}

public enum NativeWebViewPrintStatus
{
    Success = 0,
    Failed,
    NotSupported
}

public sealed class NativeWebViewProxyOptions
{
    public string? Server { get; set; }

    public string? BypassList { get; set; }

    public string? AutoConfigUrl { get; set; }

    public NativeWebViewProxyOptions Clone()
    {
        return new NativeWebViewProxyOptions
        {
            Server = Server,
            BypassList = BypassList,
            AutoConfigUrl = AutoConfigUrl,
        };
    }

    public void ApplyTo(NativeWebViewProxyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.Server = Server;
        options.BypassList = BypassList;
        options.AutoConfigUrl = AutoConfigUrl;
    }
}

public enum NativeWebViewProxyKind
{
    HttpConnect = 0,
    Socks5,
    AutoConfigUrl
}

public sealed class NativeWebViewResolvedProxyConfiguration
{
    internal NativeWebViewResolvedProxyConfiguration(
        NativeWebViewProxyKind kind,
        string host,
        int port,
        bool useTls,
        string? username,
        string? password,
        string? autoConfigUrl,
        IReadOnlyList<string> excludedDomains)
    {
        Kind = kind;
        Host = host;
        Port = port;
        UseTls = useTls;
        Username = username;
        Password = password;
        AutoConfigUrl = autoConfigUrl;
        ExcludedDomains = excludedDomains;
    }

    public NativeWebViewProxyKind Kind { get; }

    public string Host { get; }

    public int Port { get; }

    public bool UseTls { get; }

    public string? Username { get; }

    public string? Password { get; }

    public string? AutoConfigUrl { get; }

    public IReadOnlyList<string> ExcludedDomains { get; }
}

public static class NativeWebViewProxyConfigurationResolver
{
    public static NativeWebViewResolvedProxyConfiguration? Resolve(NativeWebViewProxyOptions? options)
    {
        if (options is null)
        {
            return null;
        }

        var server = options.Server?.Trim();
        var autoConfigUrl = options.AutoConfigUrl?.Trim();

        if (string.IsNullOrWhiteSpace(server) && string.IsNullOrWhiteSpace(autoConfigUrl))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(server) && !string.IsNullOrWhiteSpace(autoConfigUrl))
        {
            throw new ArgumentException("Specify either Server or AutoConfigUrl, not both.", nameof(options));
        }

        var excludedDomains = ParseBypassList(options.BypassList);
        if (!string.IsNullOrWhiteSpace(autoConfigUrl))
        {
            if (!Uri.TryCreate(autoConfigUrl, UriKind.Absolute, out var pacUri) ||
                pacUri is null ||
                (pacUri.Scheme != Uri.UriSchemeHttp && pacUri.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException(
                    "AutoConfigUrl must be an absolute HTTP or HTTPS URI.",
                    nameof(options));
            }

            return new NativeWebViewResolvedProxyConfiguration(
                NativeWebViewProxyKind.AutoConfigUrl,
                host: string.Empty,
                port: 0,
                useTls: false,
                username: null,
                password: null,
                autoConfigUrl: pacUri.AbsoluteUri,
                excludedDomains: excludedDomains);
        }

        var normalizedServer = server!.Contains("://", StringComparison.Ordinal)
            ? server
            : $"http://{server}";

        if (!Uri.TryCreate(normalizedServer, UriKind.Absolute, out var serverUri) || serverUri is null)
        {
            throw new ArgumentException("Server must be a valid host[:port] or absolute proxy URI.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(serverUri.Host))
        {
            throw new ArgumentException("Server must include a host name.", nameof(options));
        }

        if (!string.IsNullOrEmpty(serverUri.Query) || !string.IsNullOrEmpty(serverUri.Fragment))
        {
            throw new ArgumentException("Server must not include query or fragment components.", nameof(options));
        }

        if (!string.IsNullOrEmpty(serverUri.AbsolutePath) && serverUri.AbsolutePath != "/")
        {
            throw new ArgumentException("Server must not include a path component.", nameof(options));
        }

        var (kind, defaultPort, useTls) = serverUri.Scheme.ToLowerInvariant() switch
        {
            "http" => (NativeWebViewProxyKind.HttpConnect, 80, false),
            "https" => (NativeWebViewProxyKind.HttpConnect, 443, true),
            "socks" => (NativeWebViewProxyKind.Socks5, 1080, false),
            "socks5" => (NativeWebViewProxyKind.Socks5, 1080, false),
            _ => throw new NotSupportedException(
                $"Proxy scheme '{serverUri.Scheme}' is not supported. Use http, https, socks, or socks5.")
        };

        var username = default(string);
        var password = default(string);
        if (!string.IsNullOrWhiteSpace(serverUri.UserInfo))
        {
            var separator = serverUri.UserInfo.IndexOf(':');
            if (separator < 0)
            {
                username = Uri.UnescapeDataString(serverUri.UserInfo);
            }
            else
            {
                username = Uri.UnescapeDataString(serverUri.UserInfo[..separator]);
                password = Uri.UnescapeDataString(serverUri.UserInfo[(separator + 1)..]);
            }
        }

        return new NativeWebViewResolvedProxyConfiguration(
            kind,
            serverUri.Host,
            serverUri.IsDefaultPort ? defaultPort : serverUri.Port,
            useTls,
            username,
            password,
            autoConfigUrl: null,
            excludedDomains);
    }

    private static IReadOnlyList<string> ParseBypassList(string? bypassList)
    {
        if (string.IsNullOrWhiteSpace(bypassList))
        {
            return Array.Empty<string>();
        }

        var tokens = bypassList.Split([';', ',', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return Array.Empty<string>();
        }

        var excludedDomains = new List<string>(tokens.Length);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var token in tokens)
        {
            var normalized = token.Trim();
            if (normalized.Length == 0)
            {
                continue;
            }

            if (normalized.StartsWith("*.", StringComparison.Ordinal))
            {
                normalized = normalized[2..];
            }
            else if (normalized.StartsWith(".", StringComparison.Ordinal))
            {
                normalized = normalized[1..];
            }

            if (seen.Add(normalized))
            {
                excludedDomains.Add(normalized);
            }
        }

        return excludedDomains;
    }
}

public sealed class NativeWebViewEnvironmentOptions
{
    public string? BrowserExecutableFolder { get; set; }

    public string? UserDataFolder { get; set; }

    public string? CacheFolder { get; set; }

    public string? CookieDataFolder { get; set; }

    public string? SessionDataFolder { get; set; }

    public string? Language { get; set; }

    public string? AdditionalBrowserArguments { get; set; }

    public string? TargetCompatibleBrowserVersion { get; set; }

    public bool AllowSingleSignOnUsingOSPrimaryAccount { get; set; }

    public NativeWebViewProxyOptions? Proxy { get; set; }

    public NativeWebViewEnvironmentOptions Clone()
    {
        var clone = new NativeWebViewEnvironmentOptions();
        ApplyTo(clone);
        return clone;
    }

    public void ApplyTo(NativeWebViewEnvironmentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.BrowserExecutableFolder = BrowserExecutableFolder;
        options.UserDataFolder = UserDataFolder;
        options.CacheFolder = CacheFolder;
        options.CookieDataFolder = CookieDataFolder;
        options.SessionDataFolder = SessionDataFolder;
        options.Language = Language;
        options.AdditionalBrowserArguments = AdditionalBrowserArguments;
        options.TargetCompatibleBrowserVersion = TargetCompatibleBrowserVersion;
        options.AllowSingleSignOnUsingOSPrimaryAccount = AllowSingleSignOnUsingOSPrimaryAccount;
        options.Proxy = Proxy?.Clone();
    }
}

public sealed class NativeWebViewControllerOptions
{
    public string? ProfileName { get; set; }

    public bool IsInPrivateModeEnabled { get; set; }

    public string? ScriptLocale { get; set; }

    public NativeWebViewControllerOptions Clone()
    {
        var clone = new NativeWebViewControllerOptions();
        ApplyTo(clone);
        return clone;
    }

    public void ApplyTo(NativeWebViewControllerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.ProfileName = ProfileName;
        options.IsInPrivateModeEnabled = IsInPrivateModeEnabled;
        options.ScriptLocale = ScriptLocale;
    }
}

public sealed class NativeWebViewInstanceConfiguration
{
    public NativeWebViewEnvironmentOptions EnvironmentOptions { get; set; } = new();

    public NativeWebViewControllerOptions ControllerOptions { get; set; } = new();

    public NativeWebViewInstanceConfiguration Clone()
    {
        return new NativeWebViewInstanceConfiguration
        {
            EnvironmentOptions = EnvironmentOptions?.Clone() ?? new NativeWebViewEnvironmentOptions(),
            ControllerOptions = ControllerOptions?.Clone() ?? new NativeWebViewControllerOptions(),
        };
    }

    public void ApplyEnvironmentOptions(NativeWebViewEnvironmentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        (EnvironmentOptions ?? new NativeWebViewEnvironmentOptions()).ApplyTo(options);
    }

    public void ApplyControllerOptions(NativeWebViewControllerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        (ControllerOptions ?? new NativeWebViewControllerOptions()).ApplyTo(options);
    }
}

public interface INativeWebViewInstanceConfigurationTarget
{
    void ApplyInstanceConfiguration(NativeWebViewInstanceConfiguration configuration);
}

public sealed class NativeWebViewPrintSettings
{
    public string? OutputPath { get; set; }

    public bool Landscape { get; set; }

    public bool BackgroundsEnabled { get; set; }
}

public sealed class NativeWebViewPrintResult
{
    public NativeWebViewPrintResult(NativeWebViewPrintStatus status, string? errorMessage = null)
    {
        Status = status;
        ErrorMessage = errorMessage;
    }

    public NativeWebViewPrintStatus Status { get; }

    public string? ErrorMessage { get; }
}

public sealed class NativeWebDialogShowOptions
{
    public string? Title { get; set; }

    public double Width { get; set; } = 1024;

    public double Height { get; set; } = 768;

    public double Left { get; set; }

    public double Top { get; set; }

    public bool CenterOnParent { get; set; } = true;
}

public sealed class WebAuthenticationResult
{
    public WebAuthenticationResult(WebAuthenticationStatus responseStatus, string? responseData = null, int responseErrorDetail = 0)
    {
        ResponseStatus = responseStatus;
        ResponseData = responseData;
        ResponseErrorDetail = responseErrorDetail;
    }

    public string? ResponseData { get; }

    public WebAuthenticationStatus ResponseStatus { get; }

    public int ResponseErrorDetail { get; }

    public static WebAuthenticationResult Success(string responseData)
    {
        return new WebAuthenticationResult(WebAuthenticationStatus.Success, responseData);
    }

    public static WebAuthenticationResult UserCancel()
    {
        return new WebAuthenticationResult(WebAuthenticationStatus.UserCancel);
    }

    public static WebAuthenticationResult Error(int errorDetail)
    {
        return new WebAuthenticationResult(WebAuthenticationStatus.ErrorHttp, responseErrorDetail: errorDetail);
    }
}

public interface INativeWebViewCommandManager
{
    bool TryExecute(string commandName, string? payload = null);
}

public interface INativeWebViewCookieManager
{
    Task<IReadOnlyDictionary<string, string>> GetCookiesAsync(Uri uri, CancellationToken cancellationToken = default);

    Task SetCookieAsync(Uri uri, string name, string value, CancellationToken cancellationToken = default);

    Task DeleteCookieAsync(Uri uri, string name, CancellationToken cancellationToken = default);
}

public enum NativeWebViewRenderPixelFormat
{
    Unknown = 0,
    Bgra8888Premultiplied = 1
}

public enum NativeWebViewRenderFrameOrigin
{
    Unknown = 0,
    NativeCapture = 1,
    SyntheticFallback = 2
}

public sealed class NativeWebViewRenderFrame
{
    public NativeWebViewRenderFrame(
        int pixelWidth,
        int pixelHeight,
        int bytesPerRow,
        NativeWebViewRenderPixelFormat pixelFormat,
        byte[] pixelData,
        bool isSynthetic = false,
        long frameId = 0,
        DateTimeOffset? capturedAtUtc = null,
        NativeWebViewRenderMode renderMode = NativeWebViewRenderMode.Embedded,
        NativeWebViewRenderFrameOrigin origin = NativeWebViewRenderFrameOrigin.Unknown)
    {
        if (pixelWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelWidth), pixelWidth, "Pixel width must be greater than zero.");
        }

        if (pixelHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelHeight), pixelHeight, "Pixel height must be greater than zero.");
        }

        if (bytesPerRow <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bytesPerRow), bytesPerRow, "Bytes per row must be greater than zero.");
        }

        ArgumentNullException.ThrowIfNull(pixelData);

        var minimumLength = (long)bytesPerRow * pixelHeight;
        if (pixelData.LongLength < minimumLength)
        {
            throw new ArgumentException(
                $"Pixel data length ({pixelData.LongLength}) is smaller than the required frame buffer size ({minimumLength}).",
                nameof(pixelData));
        }

        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        BytesPerRow = bytesPerRow;
        PixelFormat = pixelFormat;
        PixelData = pixelData;
        IsSynthetic = isSynthetic;
        FrameId = frameId;
        CapturedAtUtc = (capturedAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        RenderMode = renderMode;
        Origin = origin == NativeWebViewRenderFrameOrigin.Unknown && isSynthetic
            ? NativeWebViewRenderFrameOrigin.SyntheticFallback
            : origin;
    }

    public int PixelWidth { get; }

    public int PixelHeight { get; }

    public int BytesPerRow { get; }

    public NativeWebViewRenderPixelFormat PixelFormat { get; }

    public byte[] PixelData { get; }

    public bool IsSynthetic { get; }

    public long FrameId { get; }

    public DateTimeOffset CapturedAtUtc { get; }

    public NativeWebViewRenderMode RenderMode { get; }

    public NativeWebViewRenderFrameOrigin Origin { get; }
}

public sealed class NativeWebViewRenderFrameRequest
{
    public int PixelWidth { get; set; }

    public int PixelHeight { get; set; }
}

public interface INativeWebViewFrameSource
{
    bool SupportsRenderMode(NativeWebViewRenderMode renderMode);

    Task<NativeWebViewRenderFrame?> CaptureFrameAsync(
        NativeWebViewRenderMode renderMode,
        NativeWebViewRenderFrameRequest request,
        CancellationToken cancellationToken = default);
}

public interface INativeWebViewBackend : IDisposable
{
    NativeWebViewPlatform Platform { get; }

    IWebViewPlatformFeatures Features { get; }

    Uri? CurrentUrl { get; }

    bool IsInitialized { get; }

    bool CanGoBack { get; }

    bool CanGoForward { get; }

    bool IsDevToolsEnabled { get; set; }

    bool IsContextMenuEnabled { get; set; }

    bool IsStatusBarEnabled { get; set; }

    bool IsZoomControlEnabled { get; set; }

    double ZoomFactor { get; }

    string? HeaderString { get; }

    string? UserAgentString { get; }

    event EventHandler<CoreWebViewInitializedEventArgs>? CoreWebView2Initialized;

    event EventHandler<NativeWebViewNavigationStartedEventArgs>? NavigationStarted;

    event EventHandler<NativeWebViewNavigationCompletedEventArgs>? NavigationCompleted;

    event EventHandler<NativeWebViewMessageReceivedEventArgs>? WebMessageReceived;

    event EventHandler<NativeWebViewOpenDevToolsRequestedEventArgs>? OpenDevToolsRequested;

    event EventHandler<NativeWebViewDestroyRequestedEventArgs>? DestroyRequested;

    event EventHandler<NativeWebViewRequestCustomChromeEventArgs>? RequestCustomChrome;

    event EventHandler<NativeWebViewRequestParentWindowPositionEventArgs>? RequestParentWindowPosition;

    event EventHandler<NativeWebViewBeginMoveDragEventArgs>? BeginMoveDrag;

    event EventHandler<NativeWebViewBeginResizeDragEventArgs>? BeginResizeDrag;

    event EventHandler<NativeWebViewNewWindowRequestedEventArgs>? NewWindowRequested;

    event EventHandler<NativeWebViewResourceRequestedEventArgs>? WebResourceRequested;

    event EventHandler<NativeWebViewContextMenuRequestedEventArgs>? ContextMenuRequested;

    event EventHandler<NativeWebViewNavigationHistoryChangedEventArgs>? NavigationHistoryChanged;

    event EventHandler<CoreWebViewEnvironmentRequestedEventArgs>? CoreWebView2EnvironmentRequested;

    event EventHandler<CoreWebViewControllerOptionsRequestedEventArgs>? CoreWebView2ControllerOptionsRequested;

    ValueTask InitializeAsync(CancellationToken cancellationToken = default);

    void Navigate(string url);

    void Navigate(Uri uri);

    void Reload();

    void Stop();

    void GoBack();

    void GoForward();

    Task<string?> ExecuteScriptAsync(string script, CancellationToken cancellationToken = default);

    Task PostWebMessageAsJsonAsync(string message, CancellationToken cancellationToken = default);

    Task PostWebMessageAsStringAsync(string message, CancellationToken cancellationToken = default);

    void OpenDevToolsWindow();

    Task<NativeWebViewPrintResult> PrintAsync(NativeWebViewPrintSettings? settings = null, CancellationToken cancellationToken = default);

    Task<bool> ShowPrintUiAsync(CancellationToken cancellationToken = default);

    void SetZoomFactor(double zoomFactor);

    void SetUserAgent(string? userAgent);

    void SetHeader(string? header);

    bool TryGetCommandManager(out INativeWebViewCommandManager? commandManager);

    bool TryGetCookieManager(out INativeWebViewCookieManager? cookieManager);

    void MoveFocus(NativeWebViewFocusMoveDirection direction);
}

public interface INativeWebDialogBackend : IDisposable
{
    NativeWebViewPlatform Platform { get; }

    IWebViewPlatformFeatures Features { get; }

    bool IsVisible { get; }

    Uri? CurrentUrl { get; }

    bool CanGoBack { get; }

    bool CanGoForward { get; }

    bool IsDevToolsEnabled { get; set; }

    bool IsContextMenuEnabled { get; set; }

    bool IsStatusBarEnabled { get; set; }

    bool IsZoomControlEnabled { get; set; }

    double ZoomFactor { get; }

    string? HeaderString { get; }

    string? UserAgentString { get; }

    event EventHandler<EventArgs>? Shown;

    event EventHandler<EventArgs>? Closed;

    event EventHandler<NativeWebViewNavigationStartedEventArgs>? NavigationStarted;

    event EventHandler<NativeWebViewNavigationCompletedEventArgs>? NavigationCompleted;

    event EventHandler<NativeWebViewMessageReceivedEventArgs>? WebMessageReceived;

    event EventHandler<NativeWebViewNewWindowRequestedEventArgs>? NewWindowRequested;

    event EventHandler<NativeWebViewResourceRequestedEventArgs>? WebResourceRequested;

    event EventHandler<NativeWebViewContextMenuRequestedEventArgs>? ContextMenuRequested;

    void Show(NativeWebDialogShowOptions? options = null);

    void Close();

    void Move(double left, double top);

    void Resize(double width, double height);

    void Navigate(string url);

    void Navigate(Uri uri);

    void Reload();

    void Stop();

    void GoBack();

    void GoForward();

    Task<string?> ExecuteScriptAsync(string script, CancellationToken cancellationToken = default);

    Task PostWebMessageAsJsonAsync(string message, CancellationToken cancellationToken = default);

    Task PostWebMessageAsStringAsync(string message, CancellationToken cancellationToken = default);

    void OpenDevToolsWindow();

    Task<NativeWebViewPrintResult> PrintAsync(NativeWebViewPrintSettings? settings = null, CancellationToken cancellationToken = default);

    Task<bool> ShowPrintUiAsync(CancellationToken cancellationToken = default);

    void SetZoomFactor(double zoomFactor);

    void SetUserAgent(string? userAgent);

    void SetHeader(string? header);
}

public interface IWebAuthenticationBrokerBackend : IDisposable
{
    NativeWebViewPlatform Platform { get; }

    IWebViewPlatformFeatures Features { get; }

    Task<WebAuthenticationResult> AuthenticateAsync(
        Uri requestUri,
        Uri callbackUri,
        WebAuthenticationOptions options = WebAuthenticationOptions.None,
        CancellationToken cancellationToken = default);
}

internal sealed class EmptyReadOnlyDictionary : ReadOnlyDictionary<string, string>
{
    public static readonly EmptyReadOnlyDictionary Instance = new(new Dictionary<string, string>());

    private EmptyReadOnlyDictionary(IDictionary<string, string> dictionary)
        : base(dictionary)
    {
    }
}
