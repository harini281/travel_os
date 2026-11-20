using SkiaSharp;
using System.Text;
using TravelOS.PlatformApi.Security;
using Xunit;

namespace TravelOS.PlatformApi.Tests;

public sealed class ReceiptImageProcessorTests
{
    [Theory]
    [InlineData(SKEncodedImageFormat.Jpeg, "image/jpeg")]
    [InlineData(SKEncodedImageFormat.Png, "image/png")]
    [InlineData(SKEncodedImageFormat.Webp, "image/webp")]
    public void TryNormalize_AcceptsSupportedImageAndReencodesAsJpeg(
        SKEncodedImageFormat sourceFormat, string contentType)
    {
        var source = CreateImage(sourceFormat, 32, 24);

        var accepted = ReceiptImageProcessor.TryNormalize(source, contentType, out var normalized);

        Assert.True(accepted);
        Assert.NotNull(normalized);
        Assert.Equal(32, normalized.Width);
        Assert.Equal(24, normalized.Height);

        using var encodedData = SKData.CreateCopy(normalized.Bytes);
        using var codec = SKCodec.Create(encodedData);
        Assert.NotNull(codec);
        Assert.Equal(SKEncodedImageFormat.Jpeg, codec.EncodedFormat);

        using var decoded = SKBitmap.Decode(normalized.Bytes);
        Assert.NotNull(decoded);
        Assert.Equal(32, decoded.Width);
        Assert.Equal(24, decoded.Height);
    }

    [Theory]
    [InlineData("image/jpeg", new byte[] { 0xFF, 0xD8, 0xFF, 0x00, 0x01 })]
    [InlineData("image/png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })]
    [InlineData("image/webp", new byte[] { 0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50 })]
    public void TryNormalize_RejectsMalformedImageBodies(string contentType, byte[] malformed)
    {
        Assert.False(ReceiptImageProcessor.TryNormalize(malformed, contentType, out var normalized));
        Assert.Null(normalized);
    }

    [Fact]
    public void TryNormalize_RejectsMimeTypeThatDoesNotMatchDecodedFormat()
    {
        var png = CreateImage(SKEncodedImageFormat.Png, 8, 8);

        Assert.False(ReceiptImageProcessor.TryNormalize(png, "image/jpeg", out var normalized));
        Assert.Null(normalized);
    }

    [Fact]
    public void TryNormalize_RejectsUnsupportedDeclaredTypes()
    {
        var png = CreateImage(SKEncodedImageFormat.Png, 8, 8);

        Assert.False(ReceiptImageProcessor.TryNormalize(png, "image/svg+xml", out var normalized));
        Assert.Null(normalized);
    }

    [Fact]
    public void TryNormalize_RejectsAnImageAboveTheMaximumDimension()
    {
        var source = CreateImage(SKEncodedImageFormat.Png, ReceiptImageProcessor.MaximumDimension + 1, 1);

        Assert.False(ReceiptImageProcessor.TryNormalize(source, "image/png", out var normalized));
        Assert.Null(normalized);
    }

    [Fact]
    public void TryNormalize_RejectsAnImageAboveTheMaximumPixelCount()
    {
        // 4,001 x 4,000 is just above the 16-million-pixel budget.
        // The compressed PNG stays small; the decoder must reject it from metadata
        // before allocating a decoded pixel buffer.
        var source = CreateImage(SKEncodedImageFormat.Png, 4_001, 4_000);

        Assert.False(ReceiptImageProcessor.TryNormalize(source, "image/png", out var normalized));
        Assert.Null(normalized);
    }

    [Fact]
    public void TryNormalize_ReencodingDiscardsTrailingPayloadBytes()
    {
        var png = CreateImage(SKEncodedImageFormat.Png, 32, 24);
        var trailing = Encoding.UTF8.GetBytes("TRAILING-MALICIOUS-PAYLOAD");
        var source = png.Concat(trailing).ToArray();

        Assert.True(ReceiptImageProcessor.TryNormalize(source, "image/png", out var normalized));
        Assert.NotNull(normalized);
        Assert.Equal(-1, normalized.Bytes.AsSpan().IndexOf(trailing));
        using var data = SKData.CreateCopy(normalized.Bytes);
        using var codec = SKCodec.Create(data);
        Assert.Equal(SKEncodedImageFormat.Jpeg, codec!.EncodedFormat);
    }

    [Fact]
    public void TryNormalize_ReencodingDiscardsExifMetadata()
    {
        var jpeg = CreateImage(SKEncodedImageFormat.Jpeg, 32, 24);
        var exifSignature = Encoding.ASCII.GetBytes("Exif\0\0");
        var exifPayload = exifSignature.Concat(new byte[]
        {
            0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00
        }).ToArray();
        var segmentLength = checked((ushort)(exifPayload.Length + 2));
        var app1Segment = new byte[4 + exifPayload.Length];
        app1Segment[0] = 0xFF;
        app1Segment[1] = 0xE1;
        app1Segment[2] = (byte)(segmentLength >> 8);
        app1Segment[3] = (byte)(segmentLength & 0xFF);
        exifPayload.CopyTo(app1Segment, 4);

        // Insert an EXIF APP1 marker immediately after the JPEG SOI marker.
        var source = new byte[jpeg.Length + app1Segment.Length];
        jpeg.AsSpan(0, 2).CopyTo(source);
        app1Segment.CopyTo(source, 2);
        jpeg.AsSpan(2).CopyTo(source.AsSpan(2 + app1Segment.Length));
        Assert.NotEqual(-1, source.AsSpan().IndexOf(exifSignature));

        Assert.True(ReceiptImageProcessor.TryNormalize(source, "image/jpeg", out var normalized));
        Assert.NotNull(normalized);
        Assert.Equal(-1, normalized.Bytes.AsSpan().IndexOf(exifSignature));

        using var normalizedData = SKData.CreateCopy(normalized.Bytes);
        using var normalizedCodec = SKCodec.Create(normalizedData);
        Assert.NotNull(normalizedCodec);
        Assert.Equal(SKEncodedImageFormat.Jpeg, normalizedCodec.EncodedFormat);
    }

    private static byte[] CreateImage(SKEncodedImageFormat format, int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(format, 95);
        Assert.NotNull(encoded);
        return encoded.ToArray();
    }
}
