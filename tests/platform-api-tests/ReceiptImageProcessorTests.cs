using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using TravelOS.PlatformApi.Security;
using Xunit;

namespace TravelOS.PlatformApi.Tests;

public sealed class ReceiptImageProcessorTests
{
    [Fact]
    public void TryNormalize_AcceptsValidPngAndReencodesItAsJpeg()
    {
        var source = CreatePng(32, 24);

        var accepted = ReceiptImageProcessor.TryNormalize(source, "image/png", out var normalized);

        Assert.True(accepted);
        Assert.NotNull(normalized);
        Assert.Equal(32, normalized.Width);
        Assert.Equal(24, normalized.Height);
        Assert.Equal("JPEG", Image.DetectFormat(normalized.Bytes).Name);

        using var decoded = Image.Load(normalized.Bytes);
        Assert.Equal(32, decoded.Width);
        Assert.Equal(24, decoded.Height);
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("image/webp")]
    public void TryNormalize_RejectsMalformedImageBodies(string contentType)
    {
        var malformed = contentType switch
        {
            "image/jpeg" => new byte[] { 0xFF, 0xD8, 0xFF, 0x00, 0x01 },
            "image/png" => new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A },
            _ => System.Text.Encoding.ASCII.GetBytes("RIFFxxxxWEBP")
        };

        Assert.False(ReceiptImageProcessor.TryNormalize(malformed, contentType, out var normalized));
        Assert.Null(normalized);
    }

    [Fact]
    public void TryNormalize_RejectsMimeTypeThatDoesNotMatchDecodedFormat()
    {
        var png = CreatePng(8, 8);

        Assert.False(ReceiptImageProcessor.TryNormalize(png, "image/jpeg", out var normalized));
        Assert.Null(normalized);
    }

    [Fact]
    public void TryNormalize_RejectsUnsupportedDeclaredTypes()
    {
        var png = CreatePng(8, 8);

        Assert.False(ReceiptImageProcessor.TryNormalize(png, "image/svg+xml", out var normalized));
        Assert.Null(normalized);
    }

    private static byte[] CreatePng(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        using var output = new MemoryStream();
        image.Save(output, new PngEncoder());
        return output.ToArray();
    }
}
