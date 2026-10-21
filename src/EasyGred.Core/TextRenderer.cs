using System.Runtime.InteropServices;
using SkiaSharp;

namespace EasyGred.Core;

public sealed record TextStyle(string Family, float SizePx, bool Bold, bool Italic, bool Underline, bool Antialias);

/// <summary>Renders text to a straight-alpha pixel buffer whose top-left is the top of the first line.</summary>
public static class TextRenderer
{
    public static (int Width, int Height, uint[] Pixels) Render(string text, TextStyle style, uint color)
    {
        using var typeface = SKTypeface.FromFamilyName(style.Family,
                                 style.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
                                 SKFontStyleWidth.Normal,
                                 style.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright)
                             ?? SKTypeface.Default;
        using var font = new SKFont(typeface, style.SizePx)
        {
            Edging = style.Antialias ? SKFontEdging.Antialias : SKFontEdging.Alias,
            Subpixel = false,
            Hinting = SKFontHinting.Normal,
        };
        using var paint = new SKPaint
        {
            Color = new SKColor(Px.R(color), Px.G(color), Px.B(color), Px.A(color)),
            IsAntialias = style.Antialias,
            Typeface = typeface,
            TextSize = style.SizePx,
        };

        var lines = text.Replace("\r", "").Split('\n');
        var metrics = font.Metrics;
        float lineHeight = font.Spacing;
        float ascent = -metrics.Ascent;
        float slack = style.Italic ? style.SizePx * 0.3f : 2;
        int width = Math.Max(1, (int)Math.Ceiling(lines.Max(l => paint.MeasureText(l)) + slack));
        int height = Math.Max(1, (int)Math.Ceiling(lineHeight * lines.Length + metrics.Descent * 0.25f));

        var premulInfo = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(premulInfo);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            for (int i = 0; i < lines.Length; i++)
            {
                float baseline = ascent + i * lineHeight;
                canvas.DrawText(lines[i], 0, baseline, font, paint);
                if (style.Underline && lines[i].Length > 0)
                {
                    float pos = metrics.UnderlinePosition ?? style.SizePx * 0.1f;
                    float thick = Math.Max(1, metrics.UnderlineThickness ?? style.SizePx / 14f);
                    canvas.DrawRect(0, baseline + pos, paint.MeasureText(lines[i]), thick, paint);
                }
            }
        }

        var pixels = new uint[width * height];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            var straight = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            bitmap.PeekPixels().ReadPixels(straight, handle.AddrOfPinnedObject(), width * 4, 0, 0);
        }
        finally
        {
            handle.Free();
        }
        return (width, height, pixels);
    }
}
