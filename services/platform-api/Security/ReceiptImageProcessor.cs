using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;

namespace TravelOS.PlatformApi.Security;

public sealed record NormalizedReceiptImage(byte[] Bytes, int Width, int Height);

public static class ReceiptImageProcessor
{
    public const int MaximumDimension = 8_000;
    public const long MaximumPixels = 16_000_000;

    /// <summary>
    /// Fully decodes an uploaded JPEG, PNG or WebP and re-encodes it as JPEG.
    /// Re-encoding removes source metadata and trailing/polyglot payloads.
    /// Dimensions are inspected before pixel allocation to reduce decompression-bomb risk.
    /// </summary>
    public static bool TryNormalize(
        byte[] source,
        string declaredContentType,
        out NormalizedReceiptImage? normalized)
    {
        normalized = null;
        try
        {
            var format = Image.DetectFormat(source);
            var expectedFormat = declaredContentType switch
            {
                "image/jpeg" => "JPEG",
                "image/png" => "PNG",
                "image/webp" => "WEBP",
                _ => null
            };

            if (format is null ||
                !string.Equals(format.Name, expectedFormat, StringComparison.OrdinalIgnoreCase))
                return false;

            var info = Image.Identify(source);
            if (info is null ||
                info.Width <= 0 || info.Height <= 0 ||
                info.Width > MaximumDimension || info.Height > MaximumDimension ||
                (long)info.Width * info.Height > MaximumPixels)
                return false;

            using var image = Image.Load(source);
            if (image.Width != info.Width || image.Height != info.Height)
                return false;

            using var output = new MemoryStream();
            image.Save(output, new JpegEncoder { Quality = 85 });
            normalized = new NormalizedReceiptImage(output.ToArray(), image.Width, image.Height);
            return normalized.Bytes.Length > 0;
        }
        catch (UnknownImageFormatException)
        {
            return false;
        }
        catch (InvalidImageContentException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
