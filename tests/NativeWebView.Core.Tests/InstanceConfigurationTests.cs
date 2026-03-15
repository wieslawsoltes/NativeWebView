using NativeWebView.Core;
using NativeWebView.Platform.Windows;
using NativeWebViewControl = NativeWebView.Controls.NativeWebView;

namespace NativeWebView.Core.Tests;

public sealed class InstanceConfigurationTests
{
    [Fact]
    public async Task NativeWebView_InstanceConfiguration_IsolatedPerControlInstance()
    {
        var shared = new NativeWebViewInstanceConfiguration();
        shared.EnvironmentOptions.UserDataFolder = "/tmp/shared/user-data";
        shared.EnvironmentOptions.CacheFolder = "/tmp/shared/cache";
        shared.EnvironmentOptions.CookieDataFolder = "/tmp/shared/cookies";
        shared.EnvironmentOptions.SessionDataFolder = "/tmp/shared/session";
        shared.EnvironmentOptions.Language = "en-US";
        shared.EnvironmentOptions.Proxy = new NativeWebViewProxyOptions
        {
            Server = "http://shared-proxy:8080",
            BypassList = "localhost;127.0.0.1",
            AutoConfigUrl = "https://example.com/shared.pac",
        };
        shared.ControllerOptions.ProfileName = "shared-profile";
        shared.ControllerOptions.ScriptLocale = "en-US";

        using var first = new NativeWebViewControl(new WindowsNativeWebViewBackend())
        {
            InstanceConfiguration = shared,
        };

        using var second = new NativeWebViewControl(new WindowsNativeWebViewBackend())
        {
            InstanceConfiguration = shared,
        };

        first.InstanceConfiguration.EnvironmentOptions.UserDataFolder = "/tmp/first/user-data";
        first.InstanceConfiguration.EnvironmentOptions.CacheFolder = "/tmp/first/cache";
        first.InstanceConfiguration.EnvironmentOptions.CookieDataFolder = "/tmp/first/cookies";
        first.InstanceConfiguration.EnvironmentOptions.SessionDataFolder = "/tmp/first/session";
        first.InstanceConfiguration.EnvironmentOptions.Proxy = new NativeWebViewProxyOptions
        {
            Server = "http://first-proxy:8080",
            BypassList = "localhost",
            AutoConfigUrl = "https://example.com/first.pac",
        };
        first.InstanceConfiguration.ControllerOptions.ProfileName = "first-profile";
        first.InstanceConfiguration.ControllerOptions.IsInPrivateModeEnabled = true;
        first.InstanceConfiguration.ControllerOptions.ScriptLocale = "pl-PL";

        second.InstanceConfiguration.EnvironmentOptions.UserDataFolder = "/tmp/second/user-data";
        second.InstanceConfiguration.EnvironmentOptions.CacheFolder = "/tmp/second/cache";
        second.InstanceConfiguration.EnvironmentOptions.CookieDataFolder = "/tmp/second/cookies";
        second.InstanceConfiguration.EnvironmentOptions.SessionDataFolder = "/tmp/second/session";
        second.InstanceConfiguration.EnvironmentOptions.Proxy = new NativeWebViewProxyOptions
        {
            Server = "http://second-proxy:9090",
            BypassList = "localhost;intranet",
            AutoConfigUrl = "https://example.com/second.pac",
        };
        second.InstanceConfiguration.ControllerOptions.ProfileName = "second-profile";
        second.InstanceConfiguration.ControllerOptions.ScriptLocale = "de-DE";

        shared.EnvironmentOptions.UserDataFolder = "/tmp/shared/mutated";
        shared.EnvironmentOptions.Proxy!.Server = "http://shared-mutated:9999";
        shared.ControllerOptions.ProfileName = "shared-mutated-profile";

        NativeWebViewEnvironmentOptions? firstEnvironment = null;
        NativeWebViewControllerOptions? firstController = null;
        first.CoreWebView2EnvironmentRequested += (_, e) => firstEnvironment = e.Options.Clone();
        first.CoreWebView2ControllerOptionsRequested += (_, e) => firstController = e.Options.Clone();

        NativeWebViewEnvironmentOptions? secondEnvironment = null;
        NativeWebViewControllerOptions? secondController = null;
        second.CoreWebView2EnvironmentRequested += (_, e) => secondEnvironment = e.Options.Clone();
        second.CoreWebView2ControllerOptionsRequested += (_, e) => secondController = e.Options.Clone();

        await first.InitializeAsync();
        await second.InitializeAsync();

        Assert.NotNull(firstEnvironment);
        Assert.NotNull(firstController);
        Assert.NotNull(secondEnvironment);
        Assert.NotNull(secondController);

        Assert.Equal("/tmp/first/user-data", firstEnvironment!.UserDataFolder);
        Assert.Equal("/tmp/first/cache", firstEnvironment.CacheFolder);
        Assert.Equal("/tmp/first/cookies", firstEnvironment.CookieDataFolder);
        Assert.Equal("/tmp/first/session", firstEnvironment.SessionDataFolder);
        Assert.Equal("http://first-proxy:8080", firstEnvironment.Proxy?.Server);
        Assert.Equal("localhost", firstEnvironment.Proxy?.BypassList);
        Assert.Equal("https://example.com/first.pac", firstEnvironment.Proxy?.AutoConfigUrl);
        Assert.Equal("first-profile", firstController!.ProfileName);
        Assert.True(firstController.IsInPrivateModeEnabled);
        Assert.Equal("pl-PL", firstController.ScriptLocale);

        Assert.Equal("/tmp/second/user-data", secondEnvironment!.UserDataFolder);
        Assert.Equal("/tmp/second/cache", secondEnvironment.CacheFolder);
        Assert.Equal("/tmp/second/cookies", secondEnvironment.CookieDataFolder);
        Assert.Equal("/tmp/second/session", secondEnvironment.SessionDataFolder);
        Assert.Equal("http://second-proxy:9090", secondEnvironment.Proxy?.Server);
        Assert.Equal("localhost;intranet", secondEnvironment.Proxy?.BypassList);
        Assert.Equal("https://example.com/second.pac", secondEnvironment.Proxy?.AutoConfigUrl);
        Assert.Equal("second-profile", secondController!.ProfileName);
        Assert.False(secondController.IsInPrivateModeEnabled);
        Assert.Equal("de-DE", secondController.ScriptLocale);

        Assert.NotSame(firstEnvironment.Proxy, secondEnvironment.Proxy);
        Assert.NotEqual(shared.EnvironmentOptions.UserDataFolder, firstEnvironment.UserDataFolder);
        Assert.NotEqual(shared.EnvironmentOptions.UserDataFolder, secondEnvironment.UserDataFolder);
        Assert.NotEqual(shared.ControllerOptions.ProfileName, firstController.ProfileName);
        Assert.NotEqual(shared.ControllerOptions.ProfileName, secondController.ProfileName);
    }

    [Fact]
    public async Task NativeWebView_InstanceConfiguration_IsAppliedBeforePublicOptionHandlers()
    {
        using var control = new NativeWebViewControl(new WindowsNativeWebViewBackend());
        control.InstanceConfiguration.EnvironmentOptions.UserDataFolder = "/tmp/ordered/user-data";
        control.InstanceConfiguration.EnvironmentOptions.Proxy = new NativeWebViewProxyOptions
        {
            Server = "http://ordered-proxy:8080",
        };
        control.InstanceConfiguration.ControllerOptions.ProfileName = "ordered-profile";

        NativeWebViewEnvironmentOptions? capturedEnvironment = null;
        NativeWebViewControllerOptions? capturedController = null;

        control.CoreWebView2EnvironmentRequested += (_, e) =>
        {
            capturedEnvironment = e.Options.Clone();
            e.Options.Language = "fr-FR";
        };

        control.CoreWebView2ControllerOptionsRequested += (_, e) =>
        {
            capturedController = e.Options.Clone();
            e.Options.ScriptLocale = "fr-FR";
        };

        await control.InitializeAsync();

        Assert.NotNull(capturedEnvironment);
        Assert.Equal("/tmp/ordered/user-data", capturedEnvironment!.UserDataFolder);
        Assert.Equal("http://ordered-proxy:8080", capturedEnvironment.Proxy?.Server);

        Assert.NotNull(capturedController);
        Assert.Equal("ordered-profile", capturedController!.ProfileName);
    }
}
