using NativeWebView.Core;

namespace NativeWebView.Core.Tests;

public sealed class DiagnosticsHostPlatformOverrideTests
{
    [Fact]
    public void IsEffectiveHostPlatform_WithoutOverride_UsesActualHostPlatform()
    {
        Assert.True(NativeWebViewDiagnosticsHostPlatformOverride.IsEffectiveHostPlatform(
            NativeWebViewPlatform.MacOS,
            overrideValue: null,
            actualHostPlatform: NativeWebViewPlatform.MacOS));

        Assert.False(NativeWebViewDiagnosticsHostPlatformOverride.IsEffectiveHostPlatform(
            NativeWebViewPlatform.Windows,
            overrideValue: null,
            actualHostPlatform: NativeWebViewPlatform.MacOS));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("none")]
    [InlineData("neutral")]
    public void IsEffectiveHostPlatform_UnknownOverride_ForcesMismatch(string overrideValue)
    {
        Assert.False(NativeWebViewDiagnosticsHostPlatformOverride.IsEffectiveHostPlatform(
            NativeWebViewPlatform.MacOS,
            overrideValue,
            actualHostPlatform: NativeWebViewPlatform.MacOS));
    }

    [Fact]
    public void IsEffectiveHostPlatform_OverrideMustMatchActualHostPlatform()
    {
        Assert.False(NativeWebViewDiagnosticsHostPlatformOverride.IsEffectiveHostPlatform(
            NativeWebViewPlatform.Windows,
            overrideValue: "windows",
            actualHostPlatform: NativeWebViewPlatform.MacOS));

        Assert.True(NativeWebViewDiagnosticsHostPlatformOverride.IsEffectiveHostPlatform(
            NativeWebViewPlatform.Windows,
            overrideValue: "windows",
            actualHostPlatform: NativeWebViewPlatform.Windows));
    }

    [Fact]
    public void IsEffectiveHostPlatform_InvalidOverride_IsIgnored()
    {
        Assert.True(NativeWebViewDiagnosticsHostPlatformOverride.IsEffectiveHostPlatform(
            NativeWebViewPlatform.MacOS,
            overrideValue: "typo",
            actualHostPlatform: NativeWebViewPlatform.MacOS));
    }
}
