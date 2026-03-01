namespace NativeWebView.Core;

public interface IWebViewPlatformFeatures
{
    NativeWebViewPlatform Platform { get; }

    NativeWebViewFeature Features { get; }

    bool Supports(NativeWebViewFeature feature);
}

public sealed class WebViewPlatformFeatures : IWebViewPlatformFeatures
{
    public static readonly WebViewPlatformFeatures Empty = new(NativeWebViewPlatform.Unknown, NativeWebViewFeature.None);

    public WebViewPlatformFeatures(NativeWebViewPlatform platform, NativeWebViewFeature features)
    {
        Platform = platform;
        Features = features;
    }

    public NativeWebViewPlatform Platform { get; }

    public NativeWebViewFeature Features { get; }

    public bool Supports(NativeWebViewFeature feature)
    {
        return (Features & feature) == feature;
    }
}

public sealed class NativeWebViewCapabilityRegistry
{
    private readonly Dictionary<NativeWebViewPlatform, IWebViewPlatformFeatures> _capabilities = new();
    private readonly object _gate = new();

    public void Register(IWebViewPlatformFeatures platformFeatures)
    {
        ArgumentNullException.ThrowIfNull(platformFeatures);

        lock (_gate)
        {
            _capabilities[platformFeatures.Platform] = platformFeatures;
        }
    }

    public bool TryGet(NativeWebViewPlatform platform, out IWebViewPlatformFeatures platformFeatures)
    {
        lock (_gate)
        {
            return _capabilities.TryGetValue(platform, out platformFeatures!);
        }
    }

    public IWebViewPlatformFeatures GetOrDefault(NativeWebViewPlatform platform)
    {
        return TryGet(platform, out var platformFeatures)
            ? platformFeatures
            : new WebViewPlatformFeatures(platform, NativeWebViewFeature.None);
    }
}
