using System.Collections.ObjectModel;
using System.Text.Json;

namespace NativeWebView.Core;

internal static class NativeWebViewBackendSupport
{
    public static readonly INativeWebViewCommandManager NoopCommandManagerInstance = new NoopCommandManager();
    public static readonly INativeWebViewCookieManager NoopCookieManagerInstance = new NoopCookieManager();

    public static string NormalizeJsonMessagePayload(string message, string paramName = "message")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message, paramName);

        try
        {
            using var document = JsonDocument.Parse(message);
            return document.RootElement.GetRawText();
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("JSON message payload must be valid JSON.", paramName, ex);
        }
    }

    public static NativeWebViewRenderFrame CreateSyntheticRenderFrame(
        NativeWebViewPlatform platform,
        Uri? currentUrl,
        ref long frameSequence,
        NativeWebViewRenderMode renderMode,
        int pixelWidth,
        int pixelHeight)
    {
        const int bytesPerPixel = 4;
        var normalizedWidth = Math.Max(1, pixelWidth);
        var normalizedHeight = Math.Max(1, pixelHeight);
        var bytesPerRow = normalizedWidth * bytesPerPixel;
        var pixelData = new byte[bytesPerRow * normalizedHeight];

        var frameId = Interlocked.Increment(ref frameSequence);
        var sequence = (int)(frameId & int.MaxValue);
        var urlHash = currentUrl is null
            ? platform.GetHashCode()
            : StringComparer.Ordinal.GetHashCode(currentUrl.AbsoluteUri);

        var hashOffset = Math.Abs(urlHash % 256);
        var stripePeriod = 28 + Math.Abs(urlHash % 16);

        for (var y = 0; y < normalizedHeight; y++)
        {
            var row = y * bytesPerRow;
            for (var x = 0; x < normalizedWidth; x++)
            {
                var offset = row + (x * bytesPerPixel);
                var stripe = ((x + y + sequence) / stripePeriod) & 1;
                var blue = (byte)((x + hashOffset + sequence) & 0xFF);
                var green = (byte)((y + (hashOffset / 2) + (sequence * 3)) & 0xFF);
                var redBase = (byte)(((x ^ y) + hashOffset + (sequence * 2)) & 0xFF);
                var red = stripe == 0 ? redBase : (byte)(255 - redBase);

                pixelData[offset + 0] = blue;
                pixelData[offset + 1] = green;
                pixelData[offset + 2] = red;
                pixelData[offset + 3] = 255;
            }
        }

        return new NativeWebViewRenderFrame(
            normalizedWidth,
            normalizedHeight,
            bytesPerRow,
            NativeWebViewRenderPixelFormat.Bgra8888Premultiplied,
            pixelData,
            isSynthetic: true,
            frameId: frameId,
            capturedAtUtc: DateTimeOffset.UtcNow,
            renderMode: renderMode,
            origin: NativeWebViewRenderFrameOrigin.SyntheticFallback);
    }

    private sealed class NoopCommandManager : INativeWebViewCommandManager
    {
        public bool TryExecute(string commandName, string? payload = null)
        {
            _ = commandName;
            _ = payload;
            return false;
        }
    }

    private sealed class NoopCookieManager : INativeWebViewCookieManager
    {
        public Task<IReadOnlyDictionary<string, string>> GetCookiesAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(uri);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyDictionary<string, string>>(EmptyReadOnlyDictionary.Instance);
        }

        public Task SetCookieAsync(Uri uri, string name, string value, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(uri);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(value);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task DeleteCookieAsync(Uri uri, string name, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(uri);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
