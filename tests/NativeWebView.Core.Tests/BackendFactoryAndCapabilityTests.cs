using NativeWebView.Core;
using NativeWebView.Interop;
using NativeWebView.Platform.Android;
using NativeWebView.Platform.Browser;
using NativeWebView.Platform.Linux;
using NativeWebView.Platform.Windows;
using NativeWebView.Platform.iOS;

namespace NativeWebView.Core.Tests;

public sealed class BackendFactoryAndCapabilityTests
{
    [Fact]
    public void Register_WindowsModule_WorksForMultipleFactories()
    {
        var firstFactory = new NativeWebViewBackendFactory();
        var secondFactory = new NativeWebViewBackendFactory();

        NativeWebViewPlatformWindowsModule.Register(firstFactory);
        NativeWebViewPlatformWindowsModule.Register(secondFactory);

        var firstResult = firstFactory.TryCreateNativeWebViewBackend(NativeWebViewPlatform.Windows, out var firstBackend);
        var secondResult = secondFactory.TryCreateNativeWebViewBackend(NativeWebViewPlatform.Windows, out var secondBackend);

        Assert.True(firstResult);
        Assert.True(secondResult);
        Assert.Equal(NativeWebViewPlatform.Windows, firstBackend.Platform);
        Assert.Equal(NativeWebViewPlatform.Windows, secondBackend.Platform);
    }

    [Fact]
    public async Task UnregisteredWebViewBackend_ThrowsWhenEmbeddedViewIsNotSupported()
    {
        var factory = new NativeWebViewBackendFactory();
        var created = factory.TryCreateNativeWebViewBackend(NativeWebViewPlatform.Windows, out var backend);

        Assert.False(created);
        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => backend.InitializeAsync().AsTask());
        Assert.Throws<PlatformNotSupportedException>(() => backend.Navigate("https://example.com"));
    }

    [Fact]
    public async Task BrowserBackend_RaisesEnvironmentAndControllerEvents()
    {
        var factory = new NativeWebViewBackendFactory();
        factory.UseNativeWebViewBrowser();

        var created = factory.TryCreateNativeWebViewBackend(NativeWebViewPlatform.Browser, out var backend);
        Assert.True(created);

        var environmentRequestedCount = 0;
        var controllerRequestedCount = 0;

        backend.CoreWebView2EnvironmentRequested += (_, _) => environmentRequestedCount++;
        backend.CoreWebView2ControllerOptionsRequested += (_, _) => controllerRequestedCount++;

        await backend.InitializeAsync();

        Assert.Equal(1, environmentRequestedCount);
        Assert.Equal(1, controllerRequestedCount);
    }

    [Fact]
    public async Task DesktopRuntimeBackends_ApplyStoredInstanceConfigurationBeforePublicOptionHandlers()
    {
        var backends = new INativeWebViewBackend[]
        {
            new WindowsNativeWebViewBackend(),
            new LinuxNativeWebViewBackend(),
        };

        foreach (var backend in backends)
        {
            using (backend)
            {
                var configurationTarget = Assert.IsAssignableFrom<INativeWebViewInstanceConfigurationTarget>(backend);
                configurationTarget.ApplyInstanceConfiguration(new NativeWebViewInstanceConfiguration
                {
                    EnvironmentOptions =
                    {
                        UserDataFolder = $"/tmp/{backend.Platform.ToString().ToLowerInvariant()}/user-data",
                        Proxy = new NativeWebViewProxyOptions
                        {
                            Server = "http://configured-proxy.example.com:8080",
                        },
                    },
                    ControllerOptions =
                    {
                        ProfileName = $"{backend.Platform}-profile",
                    },
                });

                NativeWebViewEnvironmentOptions? capturedEnvironment = null;
                NativeWebViewControllerOptions? capturedController = null;

                backend.CoreWebView2EnvironmentRequested += (_, e) => capturedEnvironment = e.Options.Clone();
                backend.CoreWebView2ControllerOptionsRequested += (_, e) => capturedController = e.Options.Clone();

                await backend.InitializeAsync();

                Assert.NotNull(capturedEnvironment);
                Assert.NotNull(capturedController);
                Assert.Equal($"/tmp/{backend.Platform.ToString().ToLowerInvariant()}/user-data", capturedEnvironment!.UserDataFolder);
                Assert.Equal("http://configured-proxy.example.com:8080", capturedEnvironment.Proxy?.Server);
                Assert.Equal($"{backend.Platform}-profile", capturedController!.ProfileName);
            }
        }
    }

    [Fact]
    public async Task CompiledBackends_RejectInvalidJsonMessages()
    {
        var backends = new INativeWebViewBackend[]
        {
            new WindowsNativeWebViewBackend(),
            new LinuxNativeWebViewBackend(),
            new BrowserNativeWebViewBackend(),
        };

        foreach (var backend in backends)
        {
            using (backend)
            {
                var exception = await Assert.ThrowsAsync<ArgumentException>(
                    () => backend.PostWebMessageAsJsonAsync("{ invalid json }"));
                Assert.Equal("message", exception.ParamName);
            }
        }
    }

    [Fact]
    public void BrowserBackend_ExposesDistinctSyntheticHandlesPerInstance()
    {
        var factory = new NativeWebViewBackendFactory();
        factory.UseNativeWebViewBrowser();

        Assert.True(factory.TryCreateNativeWebViewBackend(NativeWebViewPlatform.Browser, out var firstBackend));
        Assert.True(factory.TryCreateNativeWebViewBackend(NativeWebViewPlatform.Browser, out var secondBackend));

        using (firstBackend)
        using (secondBackend)
        {
            var firstProvider = Assert.IsAssignableFrom<INativeWebViewPlatformHandleProvider>(firstBackend);
            var secondProvider = Assert.IsAssignableFrom<INativeWebViewPlatformHandleProvider>(secondBackend);

            Assert.True(firstProvider.TryGetPlatformHandle(out var firstPlatformHandle));
            Assert.True(firstProvider.TryGetViewHandle(out var firstViewHandle));
            Assert.True(firstProvider.TryGetControllerHandle(out var firstControllerHandle));
            Assert.True(secondProvider.TryGetPlatformHandle(out var secondPlatformHandle));
            Assert.True(secondProvider.TryGetViewHandle(out var secondViewHandle));
            Assert.True(secondProvider.TryGetControllerHandle(out var secondControllerHandle));

            Assert.NotEqual(firstPlatformHandle.Handle, secondPlatformHandle.Handle);
            Assert.NotEqual(firstViewHandle.Handle, secondViewHandle.Handle);
            Assert.NotEqual(firstControllerHandle.Handle, secondControllerHandle.Handle);
        }
    }

    [Fact]
    public void IOSDialogBackend_IsNotRegistered_AndFallbackThrowsOnShow()
    {
        var factory = new NativeWebViewBackendFactory();
        factory.UseNativeWebViewIOS();

        var created = factory.TryCreateNativeWebDialogBackend(NativeWebViewPlatform.IOS, out var dialogBackend);

        Assert.False(created);
        Assert.Throws<PlatformNotSupportedException>(() => dialogBackend.Show());
    }

    [Fact]
    public async Task AndroidBackend_RaisesEnvironmentAndControllerEvents()
    {
        var factory = new NativeWebViewBackendFactory();
        factory.UseNativeWebViewAndroid();

        var created = factory.TryCreateNativeWebViewBackend(NativeWebViewPlatform.Android, out var backend);
        Assert.True(created);
        Assert.True(factory.TryCreateWebAuthenticationBrokerBackend(NativeWebViewPlatform.Android, out var authBackend));
        Assert.False(factory.TryCreateNativeWebDialogBackend(NativeWebViewPlatform.Android, out var dialogBackend));

        var environmentRequestedCount = 0;
        var controllerRequestedCount = 0;

        backend.CoreWebView2EnvironmentRequested += (_, _) => environmentRequestedCount++;
        backend.CoreWebView2ControllerOptionsRequested += (_, _) => controllerRequestedCount++;

        await backend.InitializeAsync();

        Assert.Equal(1, environmentRequestedCount);
        Assert.Equal(1, controllerRequestedCount);

        authBackend.Dispose();
        dialogBackend.Dispose();
    }

    [Fact]
    public async Task PartialRegistration_FallbackDialogAndAuthRemainUnsupported()
    {
        var factory = new NativeWebViewBackendFactory();
        var declaredFeatures = new WebViewPlatformFeatures(
            NativeWebViewPlatform.Windows,
            NativeWebViewFeature.EmbeddedView |
            NativeWebViewFeature.Dialog |
            NativeWebViewFeature.AuthenticationBroker |
            NativeWebViewFeature.ScriptExecution |
            NativeWebViewFeature.WebMessageChannel);

        factory.RegisterNativeWebViewBackend(
            NativeWebViewPlatform.Windows,
            () => new UnregisteredNativeWebViewBackend(NativeWebViewPlatform.Windows, declaredFeatures),
            declaredFeatures);

        var dialogCreated = factory.TryCreateNativeWebDialogBackend(NativeWebViewPlatform.Windows, out var dialogBackend);
        using (dialogBackend)
        {
            Assert.False(dialogCreated);
            Assert.False(dialogBackend.Features.Supports(NativeWebViewFeature.Dialog));
            Assert.Throws<PlatformNotSupportedException>(() => dialogBackend.Show());
        }

        var authCreated = factory.TryCreateWebAuthenticationBrokerBackend(NativeWebViewPlatform.Windows, out var authBackend);
        using (authBackend)
        {
            Assert.False(authCreated);
            Assert.False(authBackend.Features.Supports(NativeWebViewFeature.AuthenticationBroker));

            var result = await authBackend.AuthenticateAsync(
                new Uri("https://example.com/auth"),
                new Uri("https://example.com/callback"));

            Assert.Equal(WebAuthenticationStatus.ErrorHttp, result.ResponseStatus);
            Assert.NotEqual(0, result.ResponseErrorDetail);
        }
    }
}
