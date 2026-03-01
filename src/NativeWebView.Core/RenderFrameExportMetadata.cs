using System.Text.Json;
using System.Security.Cryptography;

namespace NativeWebView.Core;

public sealed class NativeWebViewRenderFrameExportMetadata
{
    public string FormatVersion { get; init; } = NativeWebViewRenderFrameMetadataSerializer.CurrentFormatVersion;

    public DateTimeOffset ExportedAtUtc { get; init; }

    public NativeWebViewPlatform Platform { get; init; }

    public NativeWebViewRenderMode RenderMode { get; init; }

    public int RenderFramesPerSecond { get; init; }

    public bool IsUsingSyntheticFrameSource { get; init; }

    public string? RenderDiagnosticsMessage { get; init; }

    public Uri? CurrentUrl { get; init; }

    public long FrameId { get; init; }

    public DateTimeOffset CapturedAtUtc { get; init; }

    public NativeWebViewRenderFrameOrigin Origin { get; init; }

    public bool IsSynthetic { get; init; }

    public int PixelWidth { get; init; }

    public int PixelHeight { get; init; }

    public int BytesPerRow { get; init; }

    public NativeWebViewRenderPixelFormat PixelFormat { get; init; }

    public long PixelDataLength { get; init; }

    public string PixelDataSha256 { get; init; } = string.Empty;

    public NativeWebViewRenderStatistics Statistics { get; init; } =
        new NativeWebViewRenderStatistics(0, 0, 0, 0, 0, 0, 0, null, NativeWebViewRenderMode.Embedded, NativeWebViewRenderFrameOrigin.Unknown, null, null);
}

public static class NativeWebViewRenderFrameMetadataSerializer
{
    public const string CurrentFormatVersion = "2";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static NativeWebViewRenderFrameExportMetadata Create(
        NativeWebViewRenderFrame frame,
        NativeWebViewRenderStatistics statistics,
        NativeWebViewPlatform platform,
        NativeWebViewRenderMode renderMode,
        int renderFramesPerSecond,
        bool isUsingSyntheticFrameSource,
        string? renderDiagnosticsMessage,
        Uri? currentUrl)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(statistics);

        var integrity = ComputeIntegrity(frame);

        return new NativeWebViewRenderFrameExportMetadata
        {
            FormatVersion = CurrentFormatVersion,
            ExportedAtUtc = DateTimeOffset.UtcNow,
            Platform = platform,
            RenderMode = renderMode,
            RenderFramesPerSecond = renderFramesPerSecond,
            IsUsingSyntheticFrameSource = isUsingSyntheticFrameSource,
            RenderDiagnosticsMessage = renderDiagnosticsMessage,
            CurrentUrl = currentUrl,
            FrameId = frame.FrameId,
            CapturedAtUtc = frame.CapturedAtUtc,
            Origin = frame.Origin,
            IsSynthetic = frame.IsSynthetic,
            PixelWidth = frame.PixelWidth,
            PixelHeight = frame.PixelHeight,
            BytesPerRow = frame.BytesPerRow,
            PixelFormat = frame.PixelFormat,
            PixelDataLength = integrity.Length,
            PixelDataSha256 = integrity.Sha256,
            Statistics = statistics,
        };
    }

    public static async Task WriteToFileAsync(
        NativeWebViewRenderFrameExportMetadata metadata,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(fullPath);
        await JsonSerializer.SerializeAsync(stream, metadata, SerializerOptions, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<NativeWebViewRenderFrameExportMetadata> ReadFromFileAsync(
        string inputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(inputPath);
        await using var stream = File.OpenRead(fullPath);
        var metadata = await JsonSerializer
            .DeserializeAsync<NativeWebViewRenderFrameExportMetadata>(stream, SerializerOptions, cancellationToken)
            .ConfigureAwait(false);

        if (metadata is null)
        {
            throw new InvalidDataException($"Metadata file '{fullPath}' did not contain a valid metadata payload.");
        }

        return metadata;
    }

    public static bool TryVerifyIntegrity(
        NativeWebViewRenderFrame frame,
        NativeWebViewRenderFrameExportMetadata metadata,
        out string? errorMessage)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(metadata);

        try
        {
            if (!string.Equals(metadata.FormatVersion, CurrentFormatVersion, StringComparison.Ordinal))
            {
                errorMessage =
                    $"Unsupported metadata format version '{metadata.FormatVersion}'. Expected '{CurrentFormatVersion}'.";
                return false;
            }

            var integrity = ComputeIntegrity(frame);
            if (metadata.PixelDataLength != integrity.Length)
            {
                errorMessage = $"Integrity length mismatch. expected={integrity.Length}, actual={metadata.PixelDataLength}.";
                return false;
            }

            if (!string.Equals(metadata.PixelDataSha256, integrity.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = $"Integrity hash mismatch. expected={integrity.Sha256}, actual={metadata.PixelDataSha256}.";
                return false;
            }

            errorMessage = null;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    private static (int Length, string Sha256) ComputeIntegrity(NativeWebViewRenderFrame frame)
    {
        var bytesPerPixel = frame.PixelFormat switch
        {
            NativeWebViewRenderPixelFormat.Bgra8888Premultiplied => 4,
            _ => 0,
        };

        // For known formats, hash visible pixel bytes and ignore row-stride padding.
        var visibleRowBytes = bytesPerPixel > 0
            ? checked(frame.PixelWidth * bytesPerPixel)
            : frame.BytesPerRow;
        var rowByteCount = Math.Min(frame.BytesPerRow, visibleRowBytes);
        if (rowByteCount < 0)
        {
            throw new InvalidOperationException("Row byte count cannot be negative.");
        }

        var expectedBufferLength = checked(frame.BytesPerRow * frame.PixelHeight);
        if (frame.PixelData.Length < expectedBufferLength)
        {
            throw new InvalidOperationException(
                $"Pixel buffer length ({frame.PixelData.Length}) is smaller than the expected frame stride size ({expectedBufferLength}).");
        }

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        var length = 0;
        for (var row = 0; row < frame.PixelHeight; row++)
        {
            var rowOffset = checked(row * frame.BytesPerRow);
            hasher.AppendData(frame.PixelData, rowOffset, rowByteCount);
            length = checked(length + rowByteCount);
        }

        var hash = hasher.GetHashAndReset();
        return (length, Convert.ToHexString(hash));
    }
}
