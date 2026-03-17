using System.Runtime.InteropServices;
using NativeWebView.Core;
using NativeWebView.Platform.Windows;

namespace NativeWebView.Core.Tests;

public sealed class WindowsControllerOptionsTests
{
    [Fact]
    public void RequiresRuntimeEnvironmentOptions_ReturnsFalse_ForNullOrDefaultOptions()
    {
        Assert.False(WindowsNativeWebViewBackend.RequiresRuntimeEnvironmentOptions(null));
        Assert.False(WindowsNativeWebViewBackend.RequiresRuntimeEnvironmentOptions(new NativeWebViewEnvironmentOptions()));
        Assert.False(WindowsNativeWebViewBackend.RequiresRuntimeEnvironmentOptions(new NativeWebViewEnvironmentOptions
        {
            AdditionalBrowserArguments = " ",
            Language = string.Empty,
            TargetCompatibleBrowserVersion = " ",
            Proxy = new NativeWebViewProxyOptions(),
        }));
    }

    [Fact]
    public void RequiresRuntimeEnvironmentOptions_ReturnsTrue_WhenAnyEnvironmentOptionIsCustomized()
    {
        Assert.True(WindowsNativeWebViewBackend.RequiresRuntimeEnvironmentOptions(new NativeWebViewEnvironmentOptions
        {
            AdditionalBrowserArguments = "--disable-gpu",
        }));

        Assert.True(WindowsNativeWebViewBackend.RequiresRuntimeEnvironmentOptions(new NativeWebViewEnvironmentOptions
        {
            AllowSingleSignOnUsingOSPrimaryAccount = true,
        }));

        Assert.True(WindowsNativeWebViewBackend.RequiresRuntimeEnvironmentOptions(new NativeWebViewEnvironmentOptions
        {
            Language = "en-US",
        }));

        Assert.True(WindowsNativeWebViewBackend.RequiresRuntimeEnvironmentOptions(new NativeWebViewEnvironmentOptions
        {
            TargetCompatibleBrowserVersion = "120.0.0.0",
        }));

        Assert.True(WindowsNativeWebViewBackend.RequiresRuntimeEnvironmentOptions(new NativeWebViewEnvironmentOptions
        {
            Proxy = new NativeWebViewProxyOptions
            {
                Server = "http://127.0.0.1:8888",
            },
        }));
    }

    [Fact]
    public void ShouldRetryEnvironmentCreationWithoutOptions_ReturnsTrue_ForInvalidEnvironmentOptionFailures()
    {
        var options = new NativeWebViewEnvironmentOptions
        {
            AdditionalBrowserArguments = "--disable-gpu",
        };

        Assert.True(WindowsNativeWebViewBackend.ShouldRetryEnvironmentCreationWithoutOptions(
            options,
            new ArgumentException("invalid options")));

        Assert.True(WindowsNativeWebViewBackend.ShouldRetryEnvironmentCreationWithoutOptions(
            options,
            new COMException("invalid options", unchecked((int)0x80070057))));
    }

    [Fact]
    public void ShouldRetryEnvironmentCreationWithoutOptions_ReturnsFalse_ForOtherFailuresOrDefaultOptions()
    {
        Assert.False(WindowsNativeWebViewBackend.ShouldRetryEnvironmentCreationWithoutOptions(
            new NativeWebViewEnvironmentOptions(),
            new ArgumentException("default options should not trigger retry")));

        Assert.False(WindowsNativeWebViewBackend.ShouldRetryEnvironmentCreationWithoutOptions(
            new NativeWebViewEnvironmentOptions
            {
                Language = "en-US",
            },
            new InvalidOperationException("different failure")));
    }

    [Fact]
    public void RequiresRuntimeControllerOptions_ReturnsFalse_ForNullOrDefaultOptions()
    {
        Assert.False(WindowsNativeWebViewBackend.RequiresRuntimeControllerOptions(null));
        Assert.False(WindowsNativeWebViewBackend.RequiresRuntimeControllerOptions(new NativeWebViewControllerOptions()));
        Assert.False(WindowsNativeWebViewBackend.RequiresRuntimeControllerOptions(new NativeWebViewControllerOptions
        {
            ProfileName = " ",
            ScriptLocale = string.Empty,
        }));
    }

    [Fact]
    public void RequiresRuntimeControllerOptions_ReturnsTrue_WhenAnyControllerOptionIsCustomized()
    {
        Assert.True(WindowsNativeWebViewBackend.RequiresRuntimeControllerOptions(new NativeWebViewControllerOptions
        {
            ProfileName = "sample-profile",
        }));

        Assert.True(WindowsNativeWebViewBackend.RequiresRuntimeControllerOptions(new NativeWebViewControllerOptions
        {
            IsInPrivateModeEnabled = true,
        }));

        Assert.True(WindowsNativeWebViewBackend.RequiresRuntimeControllerOptions(new NativeWebViewControllerOptions
        {
            ScriptLocale = "en-US",
        }));
    }

    [Fact]
    public void ShouldRetryControllerCreationWithoutOptions_ReturnsTrue_ForInvalidControllerOptionFailures()
    {
        var options = new NativeWebViewControllerOptions
        {
            ProfileName = "sample-profile",
        };

        Assert.True(WindowsNativeWebViewBackend.ShouldRetryControllerCreationWithoutOptions(
            options,
            new ArgumentException("invalid options")));

        Assert.True(WindowsNativeWebViewBackend.ShouldRetryControllerCreationWithoutOptions(
            options,
            new COMException("invalid options", unchecked((int)0x80070057))));
    }

    [Fact]
    public void ShouldRetryControllerCreationWithoutOptions_ReturnsFalse_ForOtherFailuresOrDefaultOptions()
    {
        Assert.False(WindowsNativeWebViewBackend.ShouldRetryControllerCreationWithoutOptions(
            new NativeWebViewControllerOptions(),
            new ArgumentException("default options should not trigger retry")));

        Assert.False(WindowsNativeWebViewBackend.ShouldRetryControllerCreationWithoutOptions(
            new NativeWebViewControllerOptions
            {
                ScriptLocale = "en-US",
            },
            new InvalidOperationException("different failure")));
    }

    [Fact]
    public void IsTransientControllerCreationFailure_ReturnsTrue_ForInvalidArgumentFailures()
    {
        Assert.True(WindowsNativeWebViewBackend.IsTransientControllerCreationFailure(
            new ArgumentException("invalid handle")));

        Assert.True(WindowsNativeWebViewBackend.IsTransientControllerCreationFailure(
            new COMException("invalid handle", unchecked((int)0x80070057))));
    }

    [Fact]
    public void IsTransientControllerCreationFailure_ReturnsFalse_ForNonTransientFailures()
    {
        Assert.False(WindowsNativeWebViewBackend.IsTransientControllerCreationFailure(
            new InvalidOperationException("runtime missing")));
    }

    [Fact]
    public void IsTransientEnvironmentCreationFailure_ReturnsTrue_ForInvalidArgumentFailures()
    {
        Assert.True(WindowsNativeWebViewBackend.IsTransientEnvironmentCreationFailure(
            new ArgumentException("invalid options")));

        Assert.True(WindowsNativeWebViewBackend.IsTransientEnvironmentCreationFailure(
            new COMException("invalid options", unchecked((int)0x80070057))));
    }

    [Fact]
    public void IsTransientEnvironmentCreationFailure_ReturnsFalse_ForNonTransientFailures()
    {
        Assert.False(WindowsNativeWebViewBackend.IsTransientEnvironmentCreationFailure(
            new InvalidOperationException("runtime missing")));
    }

    [Fact]
    public void AttachControllerOptionsFallbackExceptionContext_PreservesOriginalException()
    {
        var originalException = new ArgumentException("initial invalid options");
        var fallbackException = new InvalidOperationException("fallback failed");

        WindowsNativeWebViewBackend.AttachControllerOptionsFallbackExceptionContext(
            fallbackException,
            originalException);

        var stored = Assert.IsType<ArgumentException>(
            fallbackException.Data[WindowsNativeWebViewBackend.ControllerOptionsFallbackOriginalExceptionDataKey]);
        Assert.Same(originalException, stored);
    }

    [Fact]
    public void ResolveInitializationNavigationTarget_PrefersPendingNavigationOverRuntimeSource()
    {
        var pendingNavigationUri = new Uri("http://127.0.0.1:5000/integration/bridge.html?kind=dialog");
        var runtimeCurrentUri = new Uri("about:blank");

        var result = WindowsNativeWebViewBackend.ResolveInitializationNavigationTarget(
            pendingNavigationUri,
            runtimeCurrentUri);

        Assert.Equal(pendingNavigationUri, result);
    }

    [Fact]
    public void ResolveInitializationNavigationTarget_FallsBackToRuntimeSource_WhenPendingNavigationIsMissing()
    {
        var runtimeCurrentUri = new Uri("about:blank");

        Assert.Equal(
            runtimeCurrentUri,
            WindowsNativeWebViewBackend.ResolveInitializationNavigationTarget(null, runtimeCurrentUri));
        Assert.Null(WindowsNativeWebViewBackend.ResolveInitializationNavigationTarget(null, null));
    }
}
