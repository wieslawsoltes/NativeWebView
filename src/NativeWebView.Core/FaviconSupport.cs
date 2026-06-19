using System.Net.Http.Headers;
using System.Text.Json;

namespace NativeWebView.Core;

public static class NativeWebViewFaviconSupport
{
    private const int MaxDownloadedFaviconBytes = 1024 * 1024;

    private const string ResolveFaviconScript = """
        (() => {
          const links = Array.from(document.querySelectorAll('link[rel]'));
          const candidates = links
            .map(link => ({
              rel: String(link.getAttribute('rel') || '').toLowerCase(),
              href: link.href || link.getAttribute('href') || '',
              type: String(link.getAttribute('type') || '').toLowerCase()
            }))
            .filter(link => link.href && (
              link.rel.split(/\s+/).includes('icon') ||
              link.rel.includes('shortcut icon') ||
              link.rel.includes('apple-touch-icon') ||
              link.type === 'image/svg+xml'
            ));

          candidates.sort((left, right) => {
            const leftSvg = left.type === 'image/svg+xml' || /\.svg(?:[?#]|$)/i.test(left.href);
            const rightSvg = right.type === 'image/svg+xml' || /\.svg(?:[?#]|$)/i.test(right.href);
            if (leftSvg !== rightSvg) {
              return leftSvg ? -1 : 1;
            }

            const leftApple = left.rel.includes('apple-touch-icon');
            const rightApple = right.rel.includes('apple-touch-icon');
            if (leftApple !== rightApple) {
              return leftApple ? 1 : -1;
            }

            return 0;
          });

          if (candidates.length > 0) {
            return candidates[0].href;
          }

          if (location && location.origin && location.origin !== 'null') {
            return new URL('/favicon.ico', location.href).href;
          }

          return '';
        })();
        """;

    private static readonly HttpClient HttpClient = new();

    public static bool IsSvgFaviconUri(Uri? uri)
    {
        if (uri is null)
        {
            return false;
        }

        return uri.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<Uri?> ResolveDeclaredFaviconUriAsync(
        Func<string, CancellationToken, Task<string?>> executeScriptAsync,
        Uri? baseUri,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executeScriptAsync);
        cancellationToken.ThrowIfCancellationRequested();

        var result = await executeScriptAsync(ResolveFaviconScript, cancellationToken).ConfigureAwait(false);
        var value = ParseScriptStringResult(result);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri;
        }

        if (baseUri is not null &&
            baseUri.IsAbsoluteUri &&
            Uri.TryCreate(baseUri, value, out var relativeUri))
        {
            return relativeUri;
        }

        return null;
    }

    public static async Task<NativeWebViewFavicon?> DownloadFaviconAsync(
        Uri? uri,
        NativeWebViewFaviconFormat format,
        CancellationToken cancellationToken = default)
    {
        if (!CanDownload(uri))
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await HttpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        if (response.Content.Headers.ContentLength > MaxDownloadedFaviconBytes)
        {
            return null;
        }

        var contentType = NormalizeContentType(response.Content.Headers.ContentType);
        if (!IsCompatibleDownloadedFormat(format, uri, contentType))
        {
            return null;
        }

        var bytes = await ReadContentWithLimitAsync(response.Content, cancellationToken).ConfigureAwait(false);
        if (bytes.Length == 0)
        {
            return null;
        }

        return new NativeWebViewFavicon(uri, contentType, ResolveDownloadedFormat(format, uri, contentType), bytes);
    }

    public static NativeWebViewFavicon? CreateFallbackFavicon(
        Uri? uri,
        NativeWebViewFaviconFormat format,
        byte[]? bytes,
        string? contentType = null)
    {
        if (bytes is null || bytes.Length == 0)
        {
            return null;
        }

        return new NativeWebViewFavicon(uri, contentType, format, bytes);
    }

    public static string GetContentType(NativeWebViewFaviconFormat format)
    {
        return format switch
        {
            NativeWebViewFaviconFormat.Jpeg => "image/jpeg",
            NativeWebViewFaviconFormat.Png => "image/png",
            _ => "application/octet-stream",
        };
    }

    private static NativeWebViewFaviconFormat ResolveDownloadedFormat(
        NativeWebViewFaviconFormat requestedFormat,
        Uri? uri,
        string? contentType)
    {
        if (requestedFormat != NativeWebViewFaviconFormat.Original)
        {
            return requestedFormat;
        }

        if (string.Equals(contentType, "image/svg+xml", StringComparison.OrdinalIgnoreCase) ||
            IsSvgFaviconUri(uri))
        {
            return NativeWebViewFaviconFormat.Original;
        }

        if (string.Equals(contentType, "image/jpeg", StringComparison.OrdinalIgnoreCase))
        {
            return NativeWebViewFaviconFormat.Jpeg;
        }

        if (string.Equals(contentType, "image/png", StringComparison.OrdinalIgnoreCase))
        {
            return NativeWebViewFaviconFormat.Png;
        }

        return NativeWebViewFaviconFormat.Original;
    }

    private static bool IsCompatibleDownloadedFormat(
        NativeWebViewFaviconFormat requestedFormat,
        Uri? uri,
        string? contentType)
    {
        if (requestedFormat == NativeWebViewFaviconFormat.Original)
        {
            return true;
        }

        if (IsSvgFaviconUri(uri) ||
            string.Equals(contentType, "image/svg+xml", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (requestedFormat == NativeWebViewFaviconFormat.Png)
        {
            return string.IsNullOrWhiteSpace(contentType) ||
                   string.Equals(contentType, "image/png", StringComparison.OrdinalIgnoreCase) ||
                   uri?.AbsolutePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) == true ||
                   uri?.AbsolutePath.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) == true;
        }

        return string.IsNullOrWhiteSpace(contentType) ||
               string.Equals(contentType, "image/jpeg", StringComparison.OrdinalIgnoreCase) ||
               uri?.AbsolutePath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) == true ||
               uri?.AbsolutePath.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string? NormalizeContentType(MediaTypeHeaderValue? contentType)
    {
        return string.IsNullOrWhiteSpace(contentType?.MediaType)
            ? null
            : contentType.MediaType;
    }

    private static bool CanDownload(Uri? uri)
    {
        return uri is not null &&
               uri.IsAbsoluteUri &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static string? ParseScriptStringResult(string? result)
    {
        if (string.IsNullOrWhiteSpace(result) || result == "null")
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<string>(result);
        }
        catch (JsonException)
        {
            return result;
        }
    }

    private static async Task<byte[]> ReadContentWithLimitAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var responseStream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var memory = new MemoryStream();
        var buffer = new byte[8192];

        while (true)
        {
            var read = await responseStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (memory.Length + read > MaxDownloadedFaviconBytes)
            {
                return Array.Empty<byte>();
            }

            memory.Write(buffer, 0, read);
        }

        return memory.ToArray();
    }
}
