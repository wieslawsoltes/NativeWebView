using NativeWebView.Controls;
using NativeWebView.Core;

namespace NativeWebView.Core.Tests;

public sealed class FaviconSupportTests
{
    [Fact]
    public void NativeWebViewFavicon_PreservesRawSvgPayload()
    {
        var uri = new Uri("https://example.com/favicon.svg");
        var bytes = "<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray();

        var favicon = new NativeWebViewFavicon(
            uri,
            "image/svg+xml",
            NativeWebViewFaviconFormat.Original,
            bytes);

        Assert.Equal(uri, favicon.Uri);
        Assert.Equal("image/svg+xml", favicon.ContentType);
        Assert.Equal(NativeWebViewFaviconFormat.Original, favicon.Format);
        Assert.Same(bytes, favicon.Bytes);
    }

    [Fact]
    public async Task NativeWebViewController_ForwardsFaviconProvider()
    {
        var backend = new TestFaviconBackend();
        using var controller = new NativeWebViewController(backend);
        var changedUri = default(Uri);
        controller.FaviconChanged += (_, e) => changedUri = e.Uri;

        backend.RaiseFaviconChanged();
        var favicon = await controller.GetFaviconAsync(NativeWebViewFaviconFormat.Original);

        Assert.Equal(backend.FaviconUri, changedUri);
        Assert.NotNull(favicon);
        Assert.Equal(NativeWebViewFaviconFormat.Original, favicon.Format);
        Assert.Equal("image/svg+xml", favicon.ContentType);
    }

    [Fact]
    public async Task NativeWebView_ControlForwardsFaviconProvider()
    {
        var backend = new TestFaviconBackend();
        using var webView = new NativeWebView.Controls.NativeWebView(backend);
        var changedUri = default(Uri);
        webView.FaviconChanged += (_, e) => changedUri = e.Uri;

        backend.RaiseFaviconChanged();
        var favicon = await webView.GetFaviconAsync();

        Assert.Equal(backend.FaviconUri, changedUri);
        Assert.NotNull(favicon);
        Assert.Equal("image/svg+xml", favicon.ContentType);
    }

    [Fact]
    public async Task NativeWebViewController_ReturnsNull_WhenBackendDoesNotProvideFavicons()
    {
        using var controller = new NativeWebViewController(new TestNoFaviconBackend());

        var favicon = await controller.GetFaviconAsync();

        Assert.Null(favicon);
    }

    private sealed class TestFaviconBackend
        : NativeWebViewBackendStubBase,
          INativeWebViewFaviconProvider
    {
        public readonly Uri FaviconUri = new("https://example.com/favicon.svg");
        private readonly byte[] _bytes = "<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray();

        public TestFaviconBackend()
            : base(
                NativeWebViewPlatform.Unknown,
                new WebViewPlatformFeatures(
                    NativeWebViewPlatform.Unknown,
                    NativeWebViewFeature.EmbeddedView |
                    NativeWebViewFeature.Favicon))
        {
        }

        public event EventHandler<NativeWebViewFaviconChangedEventArgs>? FaviconChanged;

        public Task<NativeWebViewFavicon?> GetFaviconAsync(
            NativeWebViewFaviconFormat format = NativeWebViewFaviconFormat.Original,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<NativeWebViewFavicon?>(
                new NativeWebViewFavicon(FaviconUri, "image/svg+xml", format, _bytes));
        }

        public void RaiseFaviconChanged()
        {
            FaviconChanged?.Invoke(this, new NativeWebViewFaviconChangedEventArgs(FaviconUri));
        }
    }

    private sealed class TestNoFaviconBackend : NativeWebViewBackendStubBase
    {
        public TestNoFaviconBackend()
            : base(
                NativeWebViewPlatform.Unknown,
                new WebViewPlatformFeatures(
                    NativeWebViewPlatform.Unknown,
                    NativeWebViewFeature.EmbeddedView))
        {
        }
    }
}
