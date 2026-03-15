using NativeWebView.Dialog;
using NativeWebView.Platform.Android;
using NativeWebView.Platform.Browser;
using NativeWebView.Platform.Linux;
using NativeWebView.Platform.Windows;
using NativeWebView.Platform.iOS;
using NativeWebView.Platform.macOS;

namespace NativeWebView.Core.Tests;

public sealed class ProxyConfigurationSupportTests
{
    [Fact]
    public void PlatformSupportMatrix_DescribesRuntimeSupportTruthfully()
    {
        var windows = NativeWebViewProxyPlatformSupportMatrix.Get(NativeWebViewPlatform.Windows);
        Assert.Equal(NativeWebViewProxyPlatformCapability.PerInstance, windows.PlatformCapability);
        Assert.Equal(NativeWebViewProxyRepositorySupport.ContractOnly, windows.RepositorySupport);
        Assert.False(windows.SupportsPerInstanceRuntimeApplication);

        var macOS = NativeWebViewProxyPlatformSupportMatrix.Get(NativeWebViewPlatform.MacOS);
        Assert.Equal(NativeWebViewProxyPlatformCapability.PerInstance, macOS.PlatformCapability);
        Assert.Equal(NativeWebViewProxyRepositorySupport.RuntimeApplied, macOS.RepositorySupport);
        Assert.True(macOS.SupportsPerInstanceRuntimeApplication);
        Assert.Equal("macOS 14.0", macOS.MinimumPlatformVersion);

        var linux = NativeWebViewProxyPlatformSupportMatrix.Get(NativeWebViewPlatform.Linux);
        Assert.Equal(NativeWebViewProxyPlatformCapability.PerInstance, linux.PlatformCapability);
        Assert.Equal(NativeWebViewProxyRepositorySupport.ContractOnly, linux.RepositorySupport);

        var ios = NativeWebViewProxyPlatformSupportMatrix.Get(NativeWebViewPlatform.IOS);
        Assert.Equal(NativeWebViewProxyPlatformCapability.PerInstance, ios.PlatformCapability);
        Assert.Equal(NativeWebViewProxyRepositorySupport.ContractOnly, ios.RepositorySupport);
        Assert.Equal("iOS 17.0", ios.MinimumPlatformVersion);

        var android = NativeWebViewProxyPlatformSupportMatrix.Get(NativeWebViewPlatform.Android);
        Assert.Equal(NativeWebViewProxyPlatformCapability.ApplicationWide, android.PlatformCapability);
        Assert.Equal(NativeWebViewProxyRepositorySupport.ContractOnly, android.RepositorySupport);

        var browser = NativeWebViewProxyPlatformSupportMatrix.Get(NativeWebViewPlatform.Browser);
        Assert.Equal(NativeWebViewProxyPlatformCapability.Unsupported, browser.PlatformCapability);
        Assert.Equal(NativeWebViewProxyRepositorySupport.Unsupported, browser.RepositorySupport);
    }

    [Fact]
    public void Resolve_HttpProxy_WithBypassList_ReturnsNormalizedConfiguration()
    {
        var resolved = NativeWebViewProxyConfigurationResolver.Resolve(new NativeWebViewProxyOptions
        {
            Server = "https://user:pass@proxy.example.com:8443",
            BypassList = "*.internal.example.com; localhost ; .contoso.test",
        });

        Assert.NotNull(resolved);
        Assert.Equal(NativeWebViewProxyKind.HttpConnect, resolved!.Kind);
        Assert.Equal("proxy.example.com", resolved.Host);
        Assert.Equal(8443, resolved.Port);
        Assert.True(resolved.UseTls);
        Assert.Equal("user", resolved.Username);
        Assert.Equal("pass", resolved.Password);
        Assert.Equal(new[] { "internal.example.com", "localhost", "contoso.test" }, resolved.ExcludedDomains);
    }

    [Fact]
    public void Resolve_SocksProxy_ReturnsSocks5Configuration()
    {
        var resolved = NativeWebViewProxyConfigurationResolver.Resolve(new NativeWebViewProxyOptions
        {
            Server = "socks5://proxy.example.com",
        });

        Assert.NotNull(resolved);
        Assert.Equal(NativeWebViewProxyKind.Socks5, resolved!.Kind);
        Assert.Equal("proxy.example.com", resolved.Host);
        Assert.Equal(1080, resolved.Port);
        Assert.False(resolved.UseTls);
    }

    [Fact]
    public void Resolve_AutoConfigUrl_ReturnsPacConfiguration()
    {
        var resolved = NativeWebViewProxyConfigurationResolver.Resolve(new NativeWebViewProxyOptions
        {
            AutoConfigUrl = "https://example.com/proxy.pac",
            BypassList = "localhost",
        });

        Assert.NotNull(resolved);
        Assert.Equal(NativeWebViewProxyKind.AutoConfigUrl, resolved!.Kind);
        Assert.Equal("https://example.com/proxy.pac", resolved.AutoConfigUrl);
        Assert.Equal(new[] { "localhost" }, resolved.ExcludedDomains);
    }

    [Fact]
    public void Resolve_ServerAndAutoConfigUrlTogether_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => NativeWebViewProxyConfigurationResolver.Resolve(new NativeWebViewProxyOptions
        {
            Server = "http://proxy.example.com:8080",
            AutoConfigUrl = "https://example.com/proxy.pac",
        }));

        Assert.Equal("options", exception.ParamName);
    }

    [Fact]
    public void WindowsProxyArgumentsBuilder_BuildsManualProxyArguments()
    {
        var arguments = NativeWebViewWindowsProxyArgumentsBuilder.Build(new NativeWebViewProxyOptions
        {
            Server = "https://proxy.example.com:8443",
            BypassList = "*.internal.example.com;localhost",
        });

        Assert.Equal(
            "--proxy-server=\"http=https://proxy.example.com:8443;https=https://proxy.example.com:8443\" --proxy-bypass-list=\"internal.example.com;localhost\"",
            arguments);
    }

    [Fact]
    public void WindowsProxyArgumentsBuilder_BuildsPacArguments()
    {
        var arguments = NativeWebViewWindowsProxyArgumentsBuilder.Build(new NativeWebViewProxyOptions
        {
            AutoConfigUrl = "https://example.com/proxy.pac",
            BypassList = "localhost",
        });

        Assert.Equal("--proxy-pac-url=\"https://example.com/proxy.pac\"", arguments);
    }

    [Fact]
    public void WindowsProxyArgumentsBuilder_Merge_AppendsGeneratedArguments()
    {
        var arguments = NativeWebViewWindowsProxyArgumentsBuilder.Merge(
            "--disable-features=Foo",
            new NativeWebViewProxyOptions
            {
                Server = "socks5://proxy.example.com:1080",
            });

        Assert.Equal("--disable-features=Foo --proxy-server=\"socks5://proxy.example.com:1080\"", arguments);
    }

    [Fact]
    public void WindowsProxyArgumentsBuilder_Merge_ReplacesExistingProxySwitches()
    {
        var arguments = NativeWebViewWindowsProxyArgumentsBuilder.Merge(
            "--disable-features=Foo --proxy-server=\"http=old:80;https=old:80\" --proxy-bypass-list=\"localhost\"",
            new NativeWebViewProxyOptions
            {
                Server = "http://proxy.example.com:8080",
                BypassList = "intranet",
            });

        Assert.Equal(
            "--disable-features=Foo --proxy-server=\"http=http://proxy.example.com:8080;https=http://proxy.example.com:8080\" --proxy-bypass-list=\"intranet\"",
            arguments);
    }

    [Fact]
    public void WindowsProxyArgumentsBuilder_RejectsEmbeddedCredentials()
    {
        Assert.Throws<NotSupportedException>(() => NativeWebViewWindowsProxyArgumentsBuilder.Build(new NativeWebViewProxyOptions
        {
            Server = "http://user:pass@proxy.example.com:8080",
        }));
    }

    [Fact]
    public void LinuxProxySettingsBuilder_BuildsExplicitProxySettings()
    {
        var settings = NativeWebViewLinuxProxySettingsBuilder.Build(new NativeWebViewProxyOptions
        {
            Server = "socks5://proxy.example.com:1080",
            BypassList = "*.internal.example.com;localhost",
        });

        Assert.NotNull(settings);
        Assert.Equal("socks://proxy.example.com:1080", settings!.DefaultProxyUri);
        Assert.Equal(new[] { "internal.example.com", "localhost" }, settings.IgnoreHosts);
    }

    [Fact]
    public void LinuxProxySettingsBuilder_RejectsPacAndCredentials()
    {
        Assert.Throws<NotSupportedException>(() => NativeWebViewLinuxProxySettingsBuilder.Build(new NativeWebViewProxyOptions
        {
            AutoConfigUrl = "https://example.com/proxy.pac",
        }));

        Assert.Throws<NotSupportedException>(() => NativeWebViewLinuxProxySettingsBuilder.Build(new NativeWebViewProxyOptions
        {
            Server = "http://user:pass@proxy.example.com:8080",
        }));
    }

    [Fact]
    public void OnlyMacOSBackend_AdvertisesPerInstanceProxyConfiguration()
    {
        var macOsProxySupport = OperatingSystem.IsMacOSVersionAtLeast(14);
        var cases = new (NativeWebViewPlatform Platform, Action<NativeWebViewBackendFactory> Register, bool Expected)[]
        {
            (NativeWebViewPlatform.Windows, static factory => factory.UseNativeWebViewWindows(), false),
            (NativeWebViewPlatform.MacOS, static factory => factory.UseNativeWebViewMacOS(), macOsProxySupport),
            (NativeWebViewPlatform.Linux, static factory => factory.UseNativeWebViewLinux(), false),
            (NativeWebViewPlatform.IOS, static factory => factory.UseNativeWebViewIOS(), false),
            (NativeWebViewPlatform.Android, static factory => factory.UseNativeWebViewAndroid(), false),
            (NativeWebViewPlatform.Browser, static factory => factory.UseNativeWebViewBrowser(), false),
        };

        foreach (var (platform, register, expected) in cases)
        {
            var factory = new NativeWebViewBackendFactory();
            register(factory);

            Assert.True(factory.TryCreateNativeWebViewBackend(platform, out var backend));
            using (backend)
            {
                Assert.Equal(expected, backend.Features.Supports(NativeWebViewFeature.ProxyConfiguration));
            }
        }
    }

    [Fact]
    public void NativeWebDialog_InstanceConfiguration_IsForwardedBeforeShow()
    {
        var backend = new ConfigurableDialogBackend();
        using var dialog = new NativeWebDialog(backend);

        dialog.InstanceConfiguration.EnvironmentOptions.Proxy = new NativeWebViewProxyOptions
        {
            Server = "http://dialog-proxy.example.com:8080",
            BypassList = "localhost",
        };

        dialog.Show();

        Assert.NotNull(backend.LastConfiguration);
        Assert.Equal("http://dialog-proxy.example.com:8080", backend.LastConfiguration!.EnvironmentOptions.Proxy?.Server);
        Assert.Equal("localhost", backend.LastConfiguration.EnvironmentOptions.Proxy?.BypassList);
    }

    [Fact]
    public void NativeWebDialog_InstanceConfiguration_Assignment_IsCloned()
    {
        var backend = new ConfigurableDialogBackend();
        using var dialog = new NativeWebDialog(backend);

        var configuration = new NativeWebViewInstanceConfiguration();
        configuration.EnvironmentOptions.Proxy = new NativeWebViewProxyOptions
        {
            Server = "http://initial.example.com:8080",
        };

        dialog.InstanceConfiguration = configuration;
        configuration.EnvironmentOptions.Proxy.Server = "http://mutated.example.com:9090";

        dialog.Show();

        Assert.NotNull(backend.LastConfiguration);
        Assert.Equal("http://initial.example.com:8080", backend.LastConfiguration!.EnvironmentOptions.Proxy?.Server);
    }

    private sealed class ConfigurableDialogBackend : NativeWebDialogBackendStubBase, INativeWebViewInstanceConfigurationTarget
    {
        public ConfigurableDialogBackend()
            : base(
                NativeWebViewPlatform.MacOS,
                new WebViewPlatformFeatures(
                    NativeWebViewPlatform.MacOS,
                    NativeWebViewFeature.Dialog |
                    NativeWebViewFeature.ProxyConfiguration))
        {
        }

        public NativeWebViewInstanceConfiguration? LastConfiguration { get; private set; }

        public void ApplyInstanceConfiguration(NativeWebViewInstanceConfiguration configuration)
        {
            LastConfiguration = configuration.Clone();
        }
    }
}
