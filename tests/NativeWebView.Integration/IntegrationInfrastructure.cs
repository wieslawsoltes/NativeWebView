using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NativeWebView.Core;

namespace NativeWebView.Integration;

public static class IntegrationPlatformContext
{
    public const string ResultPrefix = "NATIVEWEBVIEW_INTEGRATION_RESULT: ";
    public const string ArtifactDirectoryEnvironmentVariable = "NATIVEWEBVIEW_INTEGRATION_ARTIFACTS_DIR";

    public static string? BrowserEntryUrl { get; set; }

    public static Action<string>? ExternalLogger { get; set; }

    public static string GetArtifactsDirectory(NativeWebViewPlatform platform)
    {
        var configured = Environment.GetEnvironmentVariable(ArtifactDirectoryEnvironmentVariable);
        var root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Path.GetTempPath(), "nativewebview-integration", platform.ToString().ToLowerInvariant())
            : configured;

        Directory.CreateDirectory(root);
        return root;
    }

    public static Uri GetBrowserBaseUri()
    {
        if (!Uri.TryCreate(BrowserEntryUrl, UriKind.Absolute, out var entryUri) || entryUri is null)
        {
            throw new InvalidOperationException("Browser integration entry URL is unavailable.");
        }

        return new Uri($"{entryUri.Scheme}://{entryUri.Authority}/", UriKind.Absolute);
    }
}

internal static class IntegrationLog
{
    public static void Write(string message)
    {
        Console.WriteLine(message);
        IntegrationPlatformContext.ExternalLogger?.Invoke(message);
    }

    public static void PublishResult(IntegrationRunResult result)
    {
        var json = JsonSerializer.Serialize(result, IntegrationJsonSerializerContext.Default.IntegrationRunResult);
        Write($"{IntegrationPlatformContext.ResultPrefix}{json}");
    }
}

internal sealed class IntegrationRunResult
{
    public string Platform { get; init; } = string.Empty;

    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset CompletedAtUtc { get; set; }

    public List<IntegrationScenarioResult> Scenarios { get; } = [];

    public bool Passed => Scenarios.All(static scenario => scenario.Passed);
}

internal sealed class IntegrationScenarioResult
{
    public string Name { get; init; } = string.Empty;

    public bool Passed { get; set; }

    public string Details { get; set; } = string.Empty;

    public List<string> Evidence { get; } = [];
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(IntegrationRunResult))]
internal sealed partial class IntegrationJsonSerializerContext : JsonSerializerContext
{
}

internal sealed class IntegrationPageCatalog : IAsyncDisposable
{
    private IntegrationPageCatalog(Uri baseUri, IntegrationLoopbackServer? loopbackServer)
    {
        BaseUri = baseUri;
        LoopbackServer = loopbackServer;

        WebViewPageUri = new Uri(baseUri, "integration/bridge.html?kind=webview");
        DialogPageUri = new Uri(baseUri, "integration/bridge.html?kind=dialog");
        AuthCallbackUri = new Uri(baseUri, "integration/auth-callback.html");

        var callbackTarget = new Uri($"{AuthCallbackUri.AbsoluteUri}?token=integration-ok");
        var authRequestPath = $"integration/auth-start.html?redirect={Uri.EscapeDataString(callbackTarget.AbsoluteUri)}";
        AuthRequestUri = new Uri(baseUri, authRequestPath);
    }

    public Uri BaseUri { get; }

    public Uri WebViewPageUri { get; }

    public Uri DialogPageUri { get; }

    public Uri AuthRequestUri { get; }

    public Uri AuthCallbackUri { get; }

    private IntegrationLoopbackServer? LoopbackServer { get; }

    public static async Task<IntegrationPageCatalog> CreateAsync(NativeWebViewPlatform platform, CancellationToken cancellationToken)
    {
        if (platform == NativeWebViewPlatform.Browser)
        {
            return new IntegrationPageCatalog(IntegrationPlatformContext.GetBrowserBaseUri(), loopbackServer: null);
        }

        var loopbackServer = await IntegrationLoopbackServer.StartAsync(cancellationToken).ConfigureAwait(false);
        return new IntegrationPageCatalog(loopbackServer.BaseUri, loopbackServer);
    }

    public async ValueTask DisposeAsync()
    {
        if (LoopbackServer is not null)
        {
            await LoopbackServer.DisposeAsync().ConfigureAwait(false);
        }
    }
}

internal sealed class IntegrationLoopbackServer : IAsyncDisposable
{
    private const string HtmlContentType = "text/html; charset=utf-8";

    private static readonly IReadOnlyDictionary<string, string> ResourceMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["/integration/bridge.html"] = "NativeWebView.Integration.Assets.bridge.html",
        ["/integration/auth-start.html"] = "NativeWebView.Integration.Assets.auth-start.html",
        ["/integration/auth-callback.html"] = "NativeWebView.Integration.Assets.auth-callback.html",
    };

    private static readonly byte[] NotFoundPayload = Encoding.UTF8.GetBytes("<!DOCTYPE html><html><body>Not Found</body></html>");
    private static readonly Assembly ResourceAssembly = typeof(IntegrationLoopbackServer).Assembly;

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _acceptLoopTask;

    private IntegrationLoopbackServer(TcpListener listener)
    {
        _listener = listener;
        BaseUri = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/", UriKind.Absolute);
        _acceptLoopTask = AcceptLoopAsync();
    }

    public Uri BaseUri { get; }

    public static Task<IntegrationLoopbackServer> StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return Task.FromResult(new IntegrationLoopbackServer(listener));
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        _listener.Stop();

        try
        {
            await _acceptLoopTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _shutdown.Dispose();
        }
    }

    private async Task AcceptLoopAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            TcpClient? client = null;

            try
            {
                client = await _listener.AcceptTcpClientAsync(_shutdown.Token).ConfigureAwait(false);
                _ = HandleClientAsync(client, _shutdown.Token);
            }
            catch (OperationCanceledException)
            {
                client?.Dispose();
                break;
            }
            catch (ObjectDisposedException)
            {
                client?.Dispose();
                break;
            }
        }
    }

    private static async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var _ = client;
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, leaveOpen: true);

        var requestLine = await reader.ReadLineAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(requestLine))
        {
            return;
        }

        string? headerLine;
        do
        {
            headerLine = await reader.ReadLineAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        while (!string.IsNullOrEmpty(headerLine));

        var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            await WriteResponseAsync(stream, 400, HtmlContentType, NotFoundPayload, cancellationToken).ConfigureAwait(false);
            return;
        }

        var method = parts[0];
        var target = parts[1];

        if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase))
        {
            await WriteResponseAsync(stream, 405, HtmlContentType, NotFoundPayload, cancellationToken).ConfigureAwait(false);
            return;
        }

        var path = ParsePath(target);
        if (!ResourceMap.TryGetValue(path, out var resourceName))
        {
            await WriteResponseAsync(stream, 404, HtmlContentType, NotFoundPayload, cancellationToken).ConfigureAwait(false);
            return;
        }

        var payload = await ReadResourceAsync(resourceName, cancellationToken).ConfigureAwait(false);
        await WriteResponseAsync(
                stream,
                200,
                HtmlContentType,
                string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase) ? [] : payload,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static string ParsePath(string requestTarget)
    {
        if (Uri.TryCreate($"http://127.0.0.1{requestTarget}", UriKind.Absolute, out var requestUri) &&
            requestUri is not null)
        {
            return requestUri.AbsolutePath;
        }

        var queryIndex = requestTarget.IndexOf('?', StringComparison.Ordinal);
        return queryIndex >= 0
            ? requestTarget[..queryIndex]
            : requestTarget;
    }

    private static async Task<byte[]> ReadResourceAsync(string resourceName, CancellationToken cancellationToken)
    {
        await using var stream = ResourceAssembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' was not found.");

        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream, cancellationToken).ConfigureAwait(false);
        return memoryStream.ToArray();
    }

    private static async Task WriteResponseAsync(
        NetworkStream stream,
        int statusCode,
        string contentType,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        var reasonPhrase = statusCode switch
        {
            200 => "OK",
            400 => "Bad Request",
            404 => "Not Found",
            405 => "Method Not Allowed",
            _ => "Error",
        };

        var headers = new StringBuilder()
            .Append("HTTP/1.1 ").Append(statusCode).Append(' ').AppendLine(reasonPhrase)
            .Append("Content-Type: ").AppendLine(contentType)
            .Append("Content-Length: ").AppendLine(payload.Length.ToString())
            .AppendLine("Cache-Control: no-store, max-age=0")
            .AppendLine("Connection: close")
            .AppendLine();

        var headerBytes = Encoding.ASCII.GetBytes(headers.ToString());
        await stream.WriteAsync(headerBytes, cancellationToken).ConfigureAwait(false);

        if (payload.Length > 0)
        {
            await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        }

        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
