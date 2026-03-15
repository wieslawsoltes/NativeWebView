using System.Text.RegularExpressions;

namespace NativeWebView.Core;

public enum NativeWebViewProxyPlatformCapability
{
    Unsupported = 0,
    ApplicationWide,
    PerInstance,
}

public enum NativeWebViewProxyRepositorySupport
{
    Unsupported = 0,
    ContractOnly,
    RuntimeApplied,
}

public sealed class NativeWebViewProxyPlatformSupport
{
    internal NativeWebViewProxyPlatformSupport(
        NativeWebViewPlatform platform,
        NativeWebViewProxyPlatformCapability platformCapability,
        NativeWebViewProxyRepositorySupport repositorySupport,
        string summary,
        string? minimumPlatformVersion = null)
    {
        Platform = platform;
        PlatformCapability = platformCapability;
        RepositorySupport = repositorySupport;
        Summary = summary;
        MinimumPlatformVersion = minimumPlatformVersion;
    }

    public NativeWebViewPlatform Platform { get; }

    public NativeWebViewProxyPlatformCapability PlatformCapability { get; }

    public NativeWebViewProxyRepositorySupport RepositorySupport { get; }

    public string Summary { get; }

    public string? MinimumPlatformVersion { get; }

    public bool SupportsPerInstanceRuntimeApplication =>
        PlatformCapability == NativeWebViewProxyPlatformCapability.PerInstance &&
        RepositorySupport == NativeWebViewProxyRepositorySupport.RuntimeApplied;
}

public static class NativeWebViewProxyPlatformSupportMatrix
{
    public static NativeWebViewProxyPlatformSupport Get(NativeWebViewPlatform platform)
    {
        return platform switch
        {
            NativeWebViewPlatform.Windows => new NativeWebViewProxyPlatformSupport(
                platform,
                NativeWebViewProxyPlatformCapability.PerInstance,
                NativeWebViewProxyRepositorySupport.ContractOnly,
                "WebView2 can accept proxy configuration through AdditionalBrowserArguments, but the Windows backend in this repo is still a stub."),
            NativeWebViewPlatform.MacOS => new NativeWebViewProxyPlatformSupport(
                platform,
                NativeWebViewProxyPlatformCapability.PerInstance,
                NativeWebViewProxyRepositorySupport.RuntimeApplied,
                "NativeWebView and NativeWebDialog apply explicit per-instance proxy servers on macOS 14+; PAC remains contract-only.",
                minimumPlatformVersion: "macOS 14.0"),
            NativeWebViewPlatform.Linux => new NativeWebViewProxyPlatformSupport(
                platform,
                NativeWebViewProxyPlatformCapability.PerInstance,
                NativeWebViewProxyRepositorySupport.ContractOnly,
                "WebKitGTK can apply proxy settings through WebsiteDataManager, but the Linux backend in this repo is still a stub."),
            NativeWebViewPlatform.IOS => new NativeWebViewProxyPlatformSupport(
                platform,
                NativeWebViewProxyPlatformCapability.PerInstance,
                NativeWebViewProxyRepositorySupport.ContractOnly,
                "WebKit exposes per-instance proxy configuration on iOS 17+, but this repo does not yet have a real iOS hosting/backend path.",
                minimumPlatformVersion: "iOS 17.0"),
            NativeWebViewPlatform.Android => new NativeWebViewProxyPlatformSupport(
                platform,
                NativeWebViewProxyPlatformCapability.ApplicationWide,
                NativeWebViewProxyRepositorySupport.ContractOnly,
                "AndroidX WebKit proxy override is app-wide rather than per-WebView, and this repo does not integrate that process-wide API."),
            NativeWebViewPlatform.Browser => new NativeWebViewProxyPlatformSupport(
                platform,
                NativeWebViewProxyPlatformCapability.Unsupported,
                NativeWebViewProxyRepositorySupport.Unsupported,
                "The browser-hosted implementation does not have a web-platform API for applying engine-level per-instance proxy settings."),
            _ => new NativeWebViewProxyPlatformSupport(
                platform,
                NativeWebViewProxyPlatformCapability.Unsupported,
                NativeWebViewProxyRepositorySupport.Unsupported,
                "Proxy runtime support is unknown for this platform."),
        };
    }
}

public sealed class NativeWebViewLinuxProxySettings
{
    internal NativeWebViewLinuxProxySettings(string defaultProxyUri, IReadOnlyList<string> ignoreHosts)
    {
        DefaultProxyUri = defaultProxyUri;
        IgnoreHosts = ignoreHosts;
    }

    public string DefaultProxyUri { get; }

    public IReadOnlyList<string> IgnoreHosts { get; }
}

public static class NativeWebViewLinuxProxySettingsBuilder
{
    public static NativeWebViewLinuxProxySettings? Build(NativeWebViewProxyOptions? options)
    {
        var resolved = NativeWebViewProxyConfigurationResolver.Resolve(options);
        if (resolved is null)
        {
            return null;
        }

        if (resolved.Kind == NativeWebViewProxyKind.AutoConfigUrl)
        {
            throw new NotSupportedException(
                "WebKitGTK custom proxy settings do not expose a direct PAC mapping through this helper. Use an explicit proxy server or a backend-specific integration.");
        }

        if (!string.IsNullOrWhiteSpace(resolved.Username) || !string.IsNullOrWhiteSpace(resolved.Password))
        {
            throw new NotSupportedException(
                "WebKitGTK proxy authentication is not represented by this helper. Use a backend-specific integration for authenticated proxies.");
        }

        return new NativeWebViewLinuxProxySettings(
            CreateLinuxProxyUri(resolved),
            resolved.ExcludedDomains);
    }

    private static string CreateLinuxProxyUri(NativeWebViewResolvedProxyConfiguration resolved)
    {
        var scheme = resolved.Kind switch
        {
            NativeWebViewProxyKind.HttpConnect when resolved.UseTls => "https",
            NativeWebViewProxyKind.HttpConnect => "http",
            NativeWebViewProxyKind.Socks5 => "socks",
            _ => throw new NotSupportedException($"Proxy kind '{resolved.Kind}' is not supported for WebKitGTK helper output."),
        };

        return $"{scheme}://{resolved.Host}:{resolved.Port}";
    }
}

public static class NativeWebViewWindowsProxyArgumentsBuilder
{
    private static readonly Regex ExistingProxySwitchPattern = new(
        @"(?:^|\s)(--proxy-server=(?:""(?:\\.|[^""])*""|[^\s]+)|--proxy-bypass-list=(?:""(?:\\.|[^""])*""|[^\s]+)|--proxy-pac-url=(?:""(?:\\.|[^""])*""|[^\s]+)|--proxy-auto-detect\b|--no-proxy-server\b)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string? Build(NativeWebViewProxyOptions? options)
    {
        var resolved = NativeWebViewProxyConfigurationResolver.Resolve(options);
        if (resolved is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(resolved.Username) || !string.IsNullOrWhiteSpace(resolved.Password))
        {
            throw new NotSupportedException(
                "Chromium command-line proxy settings do not support embedded proxy credentials. Use the normal WebView2 proxy authentication flow.");
        }

        var arguments = new List<string>();

        if (resolved.Kind == NativeWebViewProxyKind.AutoConfigUrl)
        {
            arguments.Add($"--proxy-pac-url={QuoteSwitchValue(resolved.AutoConfigUrl!)}");
            return string.Join(' ', arguments);
        }

        arguments.Add($"--proxy-server={QuoteSwitchValue(CreateChromiumProxyServerValue(resolved))}");

        if (resolved.ExcludedDomains.Count > 0)
        {
            arguments.Add($"--proxy-bypass-list={QuoteSwitchValue(string.Join(';', resolved.ExcludedDomains))}");
        }

        return string.Join(' ', arguments);
    }

    public static string? Merge(string? existingArguments, NativeWebViewProxyOptions? options)
    {
        var proxyArguments = Build(options);
        if (string.IsNullOrWhiteSpace(proxyArguments))
        {
            return string.IsNullOrWhiteSpace(existingArguments)
                ? null
                : existingArguments;
        }

        var cleanedExistingArguments = StripExistingProxyArguments(existingArguments);
        if (string.IsNullOrWhiteSpace(cleanedExistingArguments))
        {
            return proxyArguments;
        }

        return $"{cleanedExistingArguments} {proxyArguments}";
    }

    private static string CreateChromiumProxyServerValue(NativeWebViewResolvedProxyConfiguration resolved)
    {
        var proxyUri = resolved.Kind switch
        {
            NativeWebViewProxyKind.HttpConnect when resolved.UseTls => $"https://{resolved.Host}:{resolved.Port}",
            NativeWebViewProxyKind.HttpConnect => $"http://{resolved.Host}:{resolved.Port}",
            NativeWebViewProxyKind.Socks5 => $"socks5://{resolved.Host}:{resolved.Port}",
            _ => throw new NotSupportedException($"Proxy kind '{resolved.Kind}' is not supported for Chromium arguments."),
        };

        return resolved.Kind == NativeWebViewProxyKind.HttpConnect
            ? $"http={proxyUri};https={proxyUri}"
            : proxyUri;
    }

    private static string QuoteSwitchValue(string value)
    {
        var escaped = value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

        return $"\"{escaped}\"";
    }

    private static string? StripExistingProxyArguments(string? existingArguments)
    {
        if (string.IsNullOrWhiteSpace(existingArguments))
        {
            return null;
        }

        var withoutProxySwitches = ExistingProxySwitchPattern.Replace(existingArguments, " ");
        var normalizedWhitespace = Regex.Replace(withoutProxySwitches, @"\s+", " ", RegexOptions.CultureInvariant).Trim();
        return normalizedWhitespace.Length == 0
            ? null
            : normalizedWhitespace;
    }
}
