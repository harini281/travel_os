using SkiaSharp;

namespace TravelOS.PlatformApi.Security;

public sealed record NormalizedReceiptImage(byte[] Bytes, int Width, int Height);

public static class ReceiptImageProcessor
{
    public const int MaximumDimension = 8_000;
    public const long MaximumPixels = 16_000_000;
    public const long MaximumDecodedBytes = 128L * 1024 * 1024;

    /// <summary>
    /// Fully decodes a supported single-frame JPEG, PNG or WebP and re-encodes it as
    /// JPEG. The encoded dimensions and decoded allocation budget are checked before
    /// allocating the output bitmap. Re-encoding removes source metadata and trailing
    /// content after the image payload.
    /// </summary>
    public static bool TryNormalize(
        byte[] source,
        string declaredContentType,
        out NormalizedReceiptImage? normalized)
    {
        normalized = null;
        try
        {
            using var data = SKData.CreateCopy(source);
            using var codec = SKCodec.Create(data);
            if (codec is null)
                return false;

            var expectedFormat = declaredContentType switch
            {
                "image/jpeg" => SKEncodedImageFormat.Jpeg,
                "image/png" => SKEncodedImageFormat.Png,
                "image/webp" => SKEncodedImageFormat.Webp,
                _ => (SKEncodedImageFormat?)null
            };

            // SkiaSharp reports zero additional frames for a static image;
            // animated images report one or more additional frames.
            if (!expectedFormat.HasValue ||
                codec.EncodedFormat != expectedFormat.Value ||
                codec.FrameCount > 0)
                return false;

            var sourceInfo = codec.Info;
            if (sourceInfo.Width <= 0 || sourceInfo.Height <= 0 ||
                sourceInfo.Width > MaximumDimension ||
                sourceInfo.Height > MaximumDimension ||
                (long)sourceInfo.Width * sourceInfo.Height > MaximumPixels)
                return false;

            var outputInfo = new SKImageInfo(
                sourceInfo.Width, sourceInfo.Height,
                SKColorType.Bgra8888, SKAlphaType.Premul);

            if (outputInfo.BytesSize <= 0 ||
                outputInfo.BytesSize > MaximumDecodedBytes)
                return false;

            // SKBitmap.Decode uses the platform codec's supported pixel format
            // conversion. Input dimensions are bounded above before this allocation.
            using var bitmap = SKBitmap.Decode(data);
            if (bitmap is null ||
                bitmap.Width != sourceInfo.Width ||
                bitmap.Height != sourceInfo.Height ||
                bitmap.GetPixels() == IntPtr.Zero)
                return false;

            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 85);
            if (encoded is null || encoded.Size <= 0)
                return false;

            var bytes = encoded.ToArray();
            if (bytes.Length == 0)
                return false;

            normalized = new NormalizedReceiptImage(bytes, sourceInfo.Width, sourceInfo.Height);
            return true;
        }
        catch (SKException)
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
