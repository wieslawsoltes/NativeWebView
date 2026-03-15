using NativeWebView.Core;
using NativeWebView.Interop;
using NativeWebView.Platform.Linux;
using NativeWebView.Platform.Windows;
using NativeWebView.Platform.macOS;

namespace NativeWebView.Core.Tests;

public sealed class DesktopBackendMatrixTests
{
    [Fact]
    public async Task WindowsBackend_MatrixSmokeTest()
    {
        await RunDesktopMatrixSmokeTestAsync(
            NativeWebViewPlatform.Windows,
            factory => factory.UseNativeWebViewWindows());
    }

    [Fact]
    public async Task MacOSBackend_MatrixSmokeTest()
    {
        await RunDesktopMatrixSmokeTestAsync(
            NativeWebViewPlatform.MacOS,
            factory => factory.UseNativeWebViewMacOS());
    }

    [Fact]
    public async Task LinuxBackend_MatrixSmokeTest()
    {
        await RunDesktopMatrixSmokeTestAsync(
            NativeWebViewPlatform.Linux,
            factory => factory.UseNativeWebViewLinux());
    }

    private static async Task RunDesktopMatrixSmokeTestAsync(
        NativeWebViewPlatform platform,
        Action<NativeWebViewBackendFactory> register)
    {
        var factory = new NativeWebViewBackendFactory();
        register(factory);
        var runInteractiveRuntimeChecks = ShouldRunDesktopRuntimeInteractionChecks(platform);

        Assert.True(factory.TryCreateNativeWebViewBackend(platform, out var webViewBackend));
        Assert.True(factory.TryCreateNativeWebDialogBackend(platform, out var dialogBackend));
        Assert.True(factory.TryCreateWebAuthenticationBrokerBackend(platform, out var authBackend));

        using (webViewBackend)
        using (dialogBackend)
        {
            Assert.True(webViewBackend.Features.Supports(NativeWebViewFeature.EmbeddedView));
            Assert.True(webViewBackend.Features.Supports(NativeWebViewFeature.Dialog));
            Assert.True(webViewBackend.Features.Supports(NativeWebViewFeature.AuthenticationBroker));

            var environmentRequestedCount = 0;
            var controllerRequestedCount = 0;
            webViewBackend.CoreWebView2EnvironmentRequested += (_, _) => environmentRequestedCount++;
            webViewBackend.CoreWebView2ControllerOptionsRequested += (_, _) => controllerRequestedCount++;

            await webViewBackend.InitializeAsync();
            Assert.Equal(1, environmentRequestedCount);
            Assert.Equal(1, controllerRequestedCount);

            var webViewHandleProvider = Assert.IsAssignableFrom<INativeWebViewPlatformHandleProvider>(webViewBackend);
            Assert.True(webViewHandleProvider.TryGetPlatformHandle(out var webViewPlatformHandle));
            Assert.True(webViewHandleProvider.TryGetViewHandle(out var webViewHandle));
            Assert.True(webViewHandleProvider.TryGetControllerHandle(out var controllerHandle));
            AssertValidHandle(webViewPlatformHandle);
            AssertValidHandle(webViewHandle);
            AssertValidHandle(controllerHandle);

            Assert.True(webViewBackend.TryGetCookieManager(out var cookieManager));
            Assert.NotNull(cookieManager);
            Assert.True(webViewBackend.TryGetCommandManager(out var commandManager));
            Assert.NotNull(commandManager);

            webViewBackend.Navigate("https://example.com/");
            if (runInteractiveRuntimeChecks)
            {
                await webViewBackend.ExecuteScriptAsync("1 + 1");
                await webViewBackend.PostWebMessageAsStringAsync("desktop-matrix");

                if (webViewBackend.Features.Supports(NativeWebViewFeature.DevTools))
                {
                    webViewBackend.OpenDevToolsWindow();
                }
            }

            var dialogHandleProvider = Assert.IsAssignableFrom<INativeWebDialogPlatformHandleProvider>(dialogBackend);
            Assert.True(dialogHandleProvider.TryGetPlatformHandle(out var dialogPlatformHandle));
            Assert.True(dialogHandleProvider.TryGetDialogHandle(out var dialogHandle));
            Assert.True(dialogHandleProvider.TryGetHostWindowHandle(out var hostHandle));
            AssertValidHandle(dialogPlatformHandle);
            AssertValidHandle(dialogHandle);
            AssertValidHandle(hostHandle);

            if (runInteractiveRuntimeChecks)
            {
                dialogBackend.Show(new NativeWebDialogShowOptions { Title = "Desktop Matrix" });
                dialogBackend.Navigate("https://example.com/dialog");
                await dialogBackend.ExecuteScriptAsync("window.location.href");
                await dialogBackend.PostWebMessageAsJsonAsync("{\"name\":\"desktop\"}");
                dialogBackend.Close();
            }
        }

        var authResult = await authBackend.AuthenticateAsync(
            new Uri("https://example.com/callback?state=desktop#result=success"),
            new Uri("https://example.com/callback?state=desktop#result=success"));

        Assert.Equal(WebAuthenticationStatus.Success, authResult.ResponseStatus);
        Assert.Equal("https://example.com/callback?state=desktop#result=success", authResult.ResponseData);

        authBackend.Dispose();
    }

    private static void AssertValidHandle(NativePlatformHandle handle)
    {
        Assert.NotEqual((nint)0, handle.Handle);
        Assert.False(string.IsNullOrWhiteSpace(handle.HandleDescriptor));
    }

    private static bool ShouldRunDesktopRuntimeInteractionChecks(NativeWebViewPlatform platform)
    {
        var overrideValue = Environment.GetEnvironmentVariable("NATIVEWEBVIEW_DESKTOP_RUNTIME_SMOKE");
        if (!string.IsNullOrWhiteSpace(overrideValue))
        {
            return bool.TryParse(overrideValue, out var enabled)
                ? enabled
                : string.Equals(overrideValue, "1", StringComparison.OrdinalIgnoreCase);
        }

        var isCi = string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Environment.GetEnvironmentVariable("CI"), "1", StringComparison.OrdinalIgnoreCase);

        return !isCi || platform == NativeWebViewPlatform.MacOS;
    }
}
