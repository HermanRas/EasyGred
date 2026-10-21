using System.Runtime.InteropServices;
using SkiaSharp;

namespace EasyGred.Core;

public enum ImageFormat { Png, Bmp, Jpeg, Webp }

public sealed record SaveOptions(int Quality = 90, bool Lossless = true);

/// <summary>Loads BMP/PNG/JPG/WebP (and anything else Skia decodes); saves BMP/PNG/JPG/WebP.</summary>
public static class ImageIO
{
    public static readonly string[] OpenPatterns = { "*.png", "*.bmp", "*.jpg", "*.jpeg", "*.webp", "*.gif" };

    public static ImageFormat? FormatFromPath(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => ImageFormat.Png,
            ".bmp" => ImageFormat.Bmp,
            ".jpg" or ".jpeg" => ImageFormat.Jpeg,
            ".webp" => ImageFormat.Webp,
            _ => null,
        };

    public static (int Width, int Height, uint[] Pixels) Load(string path)
    {
        using var stream = File.OpenRead(path);
        return Load(stream);
    }

    public static (int Width, int Height, uint[] Pixels) Load(Stream stream)
    {
        using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException("Unsupported or corrupt image file.");
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        var pixels = new uint[info.Width * info.Height];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            var result = codec.GetPixels(info, handle.AddrOfPinnedObject());
            if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
                throw new InvalidDataException($"Could not decode image ({result}).");
        }
        finally
        {
            handle.Free();
        }
        return ApplyOrigin(codec.EncodedOrigin, info.Width, info.Height, pixels);
    }

    /// <summary>Applies the EXIF orientation that phone JPGs carry.</summary>
    private static (int, int, uint[]) ApplyOrigin(SKEncodedOrigin origin, int w, int h, uint[] px) => origin switch
    {
        SKEncodedOrigin.TopRight => (w, h, Raster.FlipH(px, w, h)),
        SKEncodedOrigin.BottomRight => (w, h, Raster.Rotate(px, w, h, 2)),
        SKEncodedOrigin.BottomLeft => (w, h, Raster.FlipV(px, w, h)),
        SKEncodedOrigin.LeftTop => (h, w, Raster.FlipH(Raster.Rotate(px, w, h, 1), h, w)),
        SKEncodedOrigin.RightTop => (h, w, Raster.Rotate(px, w, h, 1)),
        SKEncodedOrigin.RightBottom => (h, w, Raster.FlipV(Raster.Rotate(px, w, h, 1), h, w)),
        SKEncodedOrigin.LeftBottom => (h, w, Raster.Rotate(px, w, h, 3)),
        _ => (w, h, px),
    };

    public static void Save(string path, int w, int h, uint[] pixels, ImageFormat format, SaveOptions? options = null)
    {
        var bytes = Encode(w, h, pixels, format, options ?? new SaveOptions());
        // Write to a temp file first so a failed save never destroys the original.
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, bytes);
        File.Move(tmp, path, overwrite: true);
    }

    public static byte[] Encode(int w, int h, uint[] pixels, ImageFormat format, SaveOptions options)
    {
        if (format == ImageFormat.Bmp) return EncodeBmp(w, h, pixels);
        if (format == ImageFormat.Jpeg)
        {
            // JPG has no alpha: flatten onto white so transparent areas don't turn black.
            pixels = pixels.Select(p => Px.Over(Px.White, p)).ToArray();
        }

        var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            using var pixmap = new SKPixmap(info, handle.AddrOfPinnedObject(), w * 4);
            using var output = new SKDynamicMemoryWStream();
            bool ok = format switch
            {
                ImageFormat.Png => pixmap.Encode(output, new SKPngEncoderOptions(SKPngEncoderFilterFlags.AllFilters, 9)),
                ImageFormat.Jpeg => pixmap.Encode(output, new SKJpegEncoderOptions(Math.Clamp(options.Quality, 1, 100),
                    SKJpegEncoderDownsample.Downsample420, SKJpegEncoderAlphaOption.Ignore)),
                ImageFormat.Webp => pixmap.Encode(output, new SKWebpEncoderOptions(
                    options.Lossless ? SKWebpEncoderCompression.Lossless : SKWebpEncoderCompression.Lossy,
                    Math.Clamp(options.Quality, 1, 100))),
                _ => false,
            };
            if (!ok) throw new IOException($"Could not encode image as {format}.");
            using var data = output.DetachAsData();
            return data.ToArray();
        }
        finally
        {
            handle.Free();
        }
    }

    /// <summary>
    /// Writes a BMP: 24-bit when fully opaque (most compatible), otherwise 32-bit with a BITMAPV4
    /// header and an alpha mask so transparency survives.
    /// </summary>
    public static byte[] EncodeBmp(int w, int h, uint[] pixels)
    {
        bool hasAlpha = pixels.Any(p => p >> 24 != 255);
        int headerSize = hasAlpha ? 108 : 40;
        int rowBytes = hasAlpha ? w * 4 : (w * 3 + 3) & ~3;
        int dataOffset = 14 + headerSize;
        int fileSize = dataOffset + rowBytes * h;

        using var ms = new MemoryStream(fileSize);
        using var bw = new BinaryWriter(ms);
        bw.Write((byte)'B');
        bw.Write((byte)'M');
        bw.Write(fileSize);
        bw.Write(0);
        bw.Write(dataOffset);

        bw.Write(headerSize);
        bw.Write(w);
        bw.Write(h); // positive = bottom-up rows
        bw.Write((short)1);
        bw.Write((short)(hasAlpha ? 32 : 24));
        bw.Write(hasAlpha ? 3 : 0); // BI_BITFIELDS : BI_RGB
        bw.Write(rowBytes * h);
        bw.Write(2835); // 72 DPI
        bw.Write(2835);
        bw.Write(0);
        bw.Write(0);
        if (hasAlpha)
        {
            bw.Write(0x00FF0000u);
            bw.Write(0x0000FF00u);
            bw.Write(0x000000FFu);
            bw.Write(0xFF000000u);
            bw.Write(0x73524742); // 'sRGB'
            bw.Write(new byte[36 + 12]); // endpoints + gamma
        }

        var row = new byte[rowBytes];
        for (int y = h - 1; y >= 0; y--)
        {
            for (int x = 0; x < w; x++)
            {
                uint p = pixels[y * w + x];
                if (hasAlpha)
                {
                    BitConverter.TryWriteBytes(row.AsSpan(x * 4), p);
                }
                else
                {
                    row[x * 3] = Px.B(p);
                    row[x * 3 + 1] = Px.G(p);
                    row[x * 3 + 2] = Px.R(p);
                }
            }
            bw.Write(row);
        }
        bw.Flush();
        return ms.ToArray();
    }
}
