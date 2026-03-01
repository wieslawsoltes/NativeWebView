namespace NativeWebView.Core;

public sealed class NativeWebViewBackendFactory
{
    private static readonly NativeWebViewFeature UnregisteredFeatureSet = NativeWebViewFeature.None;

    private readonly Dictionary<NativeWebViewPlatform, Func<INativeWebViewBackend>> _webViewBackends = new();
    private readonly Dictionary<NativeWebViewPlatform, Func<INativeWebDialogBackend>> _dialogBackends = new();
    private readonly Dictionary<NativeWebViewPlatform, Func<IWebAuthenticationBrokerBackend>> _authBackends = new();
    private readonly Dictionary<NativeWebViewPlatform, Func<NativeWebViewPlatformDiagnostics>> _diagnosticsProviders = new();
    private readonly object _gate = new();

    public NativeWebViewBackendFactory()
    {
        CapabilityRegistry = new NativeWebViewCapabilityRegistry();
    }

    public NativeWebViewCapabilityRegistry CapabilityRegistry { get; }

    public void RegisterNativeWebViewBackend(
        NativeWebViewPlatform platform,
        Func<INativeWebViewBackend> factory,
        IWebViewPlatformFeatures features)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(features);

        lock (_gate)
        {
            _webViewBackends[platform] = factory;
            CapabilityRegistry.Register(features);
        }
    }

    public void RegisterNativeWebDialogBackend(NativeWebViewPlatform platform, Func<INativeWebDialogBackend> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        lock (_gate)
        {
            _dialogBackends[platform] = factory;
        }
    }

    public void RegisterWebAuthenticationBrokerBackend(NativeWebViewPlatform platform, Func<IWebAuthenticationBrokerBackend> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        lock (_gate)
        {
            _authBackends[platform] = factory;
        }
    }

    public void RegisterPlatformDiagnostics(
        NativeWebViewPlatform platform,
        Func<NativeWebViewPlatformDiagnostics> diagnosticsProvider)
    {
        ArgumentNullException.ThrowIfNull(diagnosticsProvider);

        lock (_gate)
        {
            _diagnosticsProviders[platform] = diagnosticsProvider;
        }
    }

    public bool TryCreateNativeWebViewBackend(NativeWebViewPlatform platform, out INativeWebViewBackend backend)
    {
        lock (_gate)
        {
            if (_webViewBackends.TryGetValue(platform, out var factory))
            {
                backend = factory();
                return true;
            }
        }

        backend = new UnregisteredNativeWebViewBackend(platform, new WebViewPlatformFeatures(platform, UnregisteredFeatureSet));
        return false;
    }

    public bool TryCreateNativeWebDialogBackend(NativeWebViewPlatform platform, out INativeWebDialogBackend backend)
    {
        lock (_gate)
        {
            if (_dialogBackends.TryGetValue(platform, out var factory))
            {
                backend = factory();
                return true;
            }
        }

        backend = new UnregisteredNativeWebDialogBackend(platform, new WebViewPlatformFeatures(platform, UnregisteredFeatureSet));
        return false;
    }

    public bool TryCreateWebAuthenticationBrokerBackend(NativeWebViewPlatform platform, out IWebAuthenticationBrokerBackend backend)
    {
        lock (_gate)
        {
            if (_authBackends.TryGetValue(platform, out var factory))
            {
                backend = factory();
                return true;
            }
        }

        backend = new UnregisteredWebAuthenticationBrokerBackend(platform, new WebViewPlatformFeatures(platform, UnregisteredFeatureSet));
        return false;
    }

    public bool TryGetPlatformDiagnostics(NativeWebViewPlatform platform, out NativeWebViewPlatformDiagnostics diagnostics)
    {
        Func<NativeWebViewPlatformDiagnostics>? provider;

        lock (_gate)
        {
            _diagnosticsProviders.TryGetValue(platform, out provider);
        }

        if (provider is null)
        {
            diagnostics = new NativeWebViewPlatformDiagnostics(
                platform,
                "unregistered",
                [
                    new NativeWebViewDiagnosticIssue(
                        code: "platform.unregistered",
                        severity: NativeWebViewDiagnosticSeverity.Error,
                        message: $"No diagnostics provider is registered for platform '{platform}'.",
                        recommendation: "Register the matching platform module before requesting diagnostics.")
                ]);
            return false;
        }

        try
        {
            diagnostics = provider();
            return true;
        }
        catch (Exception ex)
        {
            diagnostics = new NativeWebViewPlatformDiagnostics(
                platform,
                "provider-failure",
                [
                    new NativeWebViewDiagnosticIssue(
                        code: "diagnostics.provider.failure",
                        severity: NativeWebViewDiagnosticSeverity.Error,
                        message: $"Diagnostics provider failed: {ex.GetType().Name}.",
                        recommendation: ex.Message)
                ]);
            return true;
        }
    }

    public NativeWebViewPlatformDiagnostics GetPlatformDiagnosticsOrDefault(NativeWebViewPlatform platform)
    {
        _ = TryGetPlatformDiagnostics(platform, out var diagnostics);
        return diagnostics;
    }
}
