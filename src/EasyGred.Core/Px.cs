using System.Globalization;

namespace EasyGred.Core;

/// <summary>
/// Pixel helpers. A pixel is a uint laid out as 0xAARRGGBB (straight, non-premultiplied alpha),
/// which is BGRA byte order in little-endian memory.
/// </summary>
public static class Px
{
    public const uint Transparent = 0x00000000;
    public const uint Black = 0xFF000000;
    public const uint White = 0xFFFFFFFF;

    public static uint Pack(int a, int r, int g, int b) =>
        (uint)((a & 0xFF) << 24 | (r & 0xFF) << 16 | (g & 0xFF) << 8 | (b & 0xFF));

    public static byte A(uint c) => (byte)(c >> 24);
    public static byte R(uint c) => (byte)(c >> 16);
    public static byte G(uint c) => (byte)(c >> 8);
    public static byte B(uint c) => (byte)c;

    public static uint WithAlpha(uint c, int a) => (c & 0x00FFFFFF) | (uint)(a & 0xFF) << 24;

    /// <summary>Largest per-channel difference (alpha included). 0 = identical.</summary>
    public static int Distance(uint a, uint b)
    {
        // Fully transparent pixels are equal regardless of their hidden RGB.
        if (A(a) == 0 && A(b) == 0) return 0;
        int d = Math.Abs(A(a) - A(b));
        d = Math.Max(d, Math.Abs(R(a) - R(b)));
        d = Math.Max(d, Math.Abs(G(a) - G(b)));
        return Math.Max(d, Math.Abs(B(a) - B(b)));
    }

    public static bool Matches(uint a, uint b, int tolerance) =>
        tolerance <= 0 ? a == b || (A(a) == 0 && A(b) == 0) : Distance(a, b) <= tolerance;

    /// <summary>Source-over blend of two straight-alpha colors.</summary>
    public static uint Over(uint dst, uint src)
    {
        int sa = A(src);
        if (sa == 255) return src;
        if (sa == 0) return dst;
        int da = A(dst);
        int dw = (da * (255 - sa) + 127) / 255;
        int oa = sa + dw;
        if (oa == 0) return Transparent;
        int r = (R(src) * sa + R(dst) * dw + oa / 2) / oa;
        int g = (G(src) * sa + G(dst) * dw + oa / 2) / oa;
        int b = (B(src) * sa + B(dst) * dw + oa / 2) / oa;
        return Pack(oa, r, g, b);
    }

    public static uint Premultiply(uint c)
    {
        int a = A(c);
        if (a == 255) return c;
        if (a == 0) return 0;
        return Pack(a, (R(c) * a + 127) / 255, (G(c) * a + 127) / 255, (B(c) * a + 127) / 255);
    }

    /// <summary>Source-over for premultiplied colors.</summary>
    public static uint OverPremul(uint dst, uint src)
    {
        int sa = A(src);
        if (sa == 255) return src;
        if (sa == 0) return dst;
        int inv = 255 - sa;
        return Pack(
            sa + (A(dst) * inv + 127) / 255,
            R(src) + (R(dst) * inv + 127) / 255,
            G(src) + (G(dst) * inv + 127) / 255,
            B(src) + (B(dst) * inv + 127) / 255);
    }

    public static string ToHex(uint c, bool withAlpha) =>
        withAlpha ? $"#{R(c):X2}{G(c):X2}{B(c):X2}{A(c):X2}" : $"#{R(c):X2}{G(c):X2}{B(c):X2}";

    /// <summary>Parses #RGB, #RRGGBB or #RRGGBBAA (the # is optional).</summary>
    public static bool TryParseHex(string? s, out uint color)
    {
        color = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = s.Trim().TrimStart('#');
        if (s.Length == 3) s = string.Concat(s[0], s[0], s[1], s[1], s[2], s[2]);
        if (s.Length == 6) s += "FF";
        if (s.Length != 8 || !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
            return false;
        // v is RRGGBBAA
        color = (v >> 8) | (v & 0xFF) << 24;
        return true;
    }

    public static void ToHsv(uint c, out double h, out double s, out double v)
    {
        double r = R(c) / 255.0, g = G(c) / 255.0, b = B(c) / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double d = max - min;
        v = max;
        s = max == 0 ? 0 : d / max;
        if (d == 0) h = 0;
        else if (max == r) h = 60 * (((g - b) / d + 6) % 6);
        else if (max == g) h = 60 * ((b - r) / d + 2);
        else h = 60 * ((r - g) / d + 4);
    }

    public static uint FromHsv(double h, double s, double v, int alpha = 255)
    {
        h = ((h % 360) + 360) % 360;
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        (double r, double g, double b) = (int)(h / 60) switch
        {
            0 => (c, x, 0d),
            1 => (x, c, 0d),
            2 => (0d, c, x),
            3 => (0d, x, c),
            4 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return Pack(alpha, (int)Math.Round((r + m) * 255), (int)Math.Round((g + m) * 255), (int)Math.Round((b + m) * 255));
    }
}
