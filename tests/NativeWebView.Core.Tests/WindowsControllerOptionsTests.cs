using System.Runtime.InteropServices;
using NativeWebView.Core;
using NativeWebView.Platform.Windows;

namespace NativeWebView.Core.Tests;

public sealed class WindowsControllerOptionsTests
{
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
}
