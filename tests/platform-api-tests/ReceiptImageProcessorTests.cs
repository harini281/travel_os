using SkiaSharp;
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
