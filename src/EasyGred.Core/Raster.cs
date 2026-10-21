namespace EasyGred.Core;

public enum ShapeFill { Outline, OutlineAndFill, Fill }

/// <summary>
/// Pixel algorithms on straight-alpha uint buffers. Drawing tools replace pixels (no blending),
/// so painting with a transparent color erases — the usual pixel-editor behaviour.
/// </summary>
public static class Raster
{
    // ---------- drawing ----------

    public static void Stamp(uint[] px, int w, int h, int cx, int cy, int size, uint color, bool round)
    {
        if (size <= 1)
        {
            if ((uint)cx < (uint)w && (uint)cy < (uint)h) px[cy * w + cx] = color;
            return;
        }
        int off = size / 2;
        double c = (size - 1) / 2.0, r2 = size * size / 4.0 + 0.25;
        for (int dy = 0; dy < size; dy++)
        {
            int y = cy - off + dy;
            if ((uint)y >= (uint)h) continue;
            for (int dx = 0; dx < size; dx++)
            {
                int x = cx - off + dx;
                if ((uint)x >= (uint)w) continue;
                if (round && size > 2 && (dx - c) * (dx - c) + (dy - c) * (dy - c) > r2) continue;
                px[y * w + x] = color;
            }
        }
    }

    public static void Line(uint[] px, int w, int h, int x0, int y0, int x1, int y1, int size, uint color, bool round)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        while (true)
        {
            Stamp(px, w, h, x0, y0, size, color, round);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    public static void FillRect(uint[] px, int w, int h, int x0, int y0, int x1, int y1, uint color)
    {
        Order(ref x0, ref x1);
        Order(ref y0, ref y1);
        x0 = Math.Max(0, x0); y0 = Math.Max(0, y0);
        x1 = Math.Min(w - 1, x1); y1 = Math.Min(h - 1, y1);
        for (int y = y0; y <= y1; y++)
            Array.Fill(px, color, y * w + x0, Math.Max(0, x1 - x0 + 1));
    }

    public static void Rectangle(uint[] px, int w, int h, int x0, int y0, int x1, int y1,
        int lineWidth, ShapeFill mode, uint outline, uint fill)
    {
        Order(ref x0, ref x1);
        Order(ref y0, ref y1);
        if (mode == ShapeFill.Fill)
        {
            FillRect(px, w, h, x0, y0, x1, y1, outline);
            return;
        }
        int lw = Math.Max(1, lineWidth);
        if (mode == ShapeFill.OutlineAndFill)
            FillRect(px, w, h, x0 + lw, y0 + lw, x1 - lw, y1 - lw, fill);
        FillRect(px, w, h, x0, y0, x1, Math.Min(y1, y0 + lw - 1), outline);
        FillRect(px, w, h, x0, Math.Max(y0, y1 - lw + 1), x1, y1, outline);
        FillRect(px, w, h, x0, y0, Math.Min(x1, x0 + lw - 1), y1, outline);
        FillRect(px, w, h, Math.Max(x0, x1 - lw + 1), y0, x1, y1, outline);
    }

    public static void Ellipse(uint[] px, int w, int h, int x0, int y0, int x1, int y1,
        int lineWidth, ShapeFill mode, uint outline, uint fill)
    {
        Order(ref x0, ref x1);
        Order(ref y0, ref y1);
        double cx = (x0 + x1 + 1) / 2.0, cy = (y0 + y1 + 1) / 2.0;
        double rx = (x1 - x0 + 1) / 2.0, ry = (y1 - y0 + 1) / 2.0;
        int lw = Math.Max(1, lineWidth);
        double irx = rx - lw, iry = ry - lw;
        for (int y = Math.Max(0, y0); y <= Math.Min(h - 1, y1); y++)
        for (int x = Math.Max(0, x0); x <= Math.Min(w - 1, x1); x++)
        {
            double nx = x + 0.5 - cx, ny = y + 0.5 - cy;
            if (nx * nx / (rx * rx) + ny * ny / (ry * ry) > 1.0) continue;
            bool inner = irx > 0 && iry > 0 && nx * nx / (irx * irx) + ny * ny / (iry * iry) <= 1.0;
            if (mode == ShapeFill.Fill) px[y * w + x] = outline;
            else if (!inner) px[y * w + x] = outline;
            else if (mode == ShapeFill.OutlineAndFill) px[y * w + x] = fill;
        }
    }

    public static void Spray(uint[] px, int w, int h, int cx, int cy, int radius, int dots, uint color, Random rng)
    {
        for (int i = 0; i < dots; i++)
        {
            double a = rng.NextDouble() * Math.PI * 2, r = Math.Sqrt(rng.NextDouble()) * radius;
            int x = cx + (int)Math.Round(Math.Cos(a) * r), y = cy + (int)Math.Round(Math.Sin(a) * r);
            if ((uint)x < (uint)w && (uint)y < (uint)h) px[y * w + x] = color;
        }
    }

    // ---------- fill & replace ----------

    /// <summary>Contiguous (4-way) flood fill. Returns the number of pixels changed.</summary>
    public static int FloodFill(uint[] px, int w, int h, int x, int y, uint color, int tolerance)
    {
        if ((uint)x >= (uint)w || (uint)y >= (uint)h) return 0;
        uint target = px[y * w + x];
        if (tolerance == 0 && target == color) return 0;

        var visited = new bool[px.Length];
        var stack = new Stack<(int X, int Y)>();
        stack.Push((x, y));
        int count = 0;
        while (stack.Count > 0)
        {
            var (sx, sy) = stack.Pop();
            int row = sy * w;
            int lx = sx;
            while (lx > 0 && !visited[row + lx - 1] && Px.Matches(px[row + lx - 1], target, tolerance)) lx--;
            int rx = sx;
            while (rx < w - 1 && !visited[row + rx + 1] && Px.Matches(px[row + rx + 1], target, tolerance)) rx++;
            for (int i = lx; i <= rx; i++)
            {
                int idx = row + i;
                if (visited[idx] || !Px.Matches(px[idx], target, tolerance)) continue;
                visited[idx] = true;
                px[idx] = color;
                count++;
                if (sy > 0 && !visited[idx - w] && Px.Matches(px[idx - w], target, tolerance)) stack.Push((i, sy - 1));
                if (sy < h - 1 && !visited[idx + w] && Px.Matches(px[idx + w], target, tolerance)) stack.Push((i, sy + 1));
            }
        }
        return count;
    }

    /// <summary>Replaces every pixel matching <paramref name="from"/> (within tolerance). Returns the count.</summary>
    public static int ReplaceColor(uint[] px, uint from, uint to, int tolerance, Func<int, bool>? inScope = null)
    {
        int count = 0;
        for (int i = 0; i < px.Length; i++)
        {
            if (!Px.Matches(px[i], from, tolerance)) continue;
            if (inScope != null && !inScope(i)) continue;
            px[i] = to;
            count++;
        }
        return count;
    }

    // ---------- geometry ----------

    public static uint[] FlipH(uint[] px, int w, int h)
    {
        var r = new uint[px.Length];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            r[y * w + x] = px[y * w + (w - 1 - x)];
        return r;
    }

    public static uint[] FlipV(uint[] px, int w, int h)
    {
        var r = new uint[px.Length];
        for (int y = 0; y < h; y++) Array.Copy(px, (h - 1 - y) * w, r, y * w, w);
        return r;
    }

    /// <summary>Rotates clockwise by 90° steps. The result is h×w for odd step counts.</summary>
    public static uint[] Rotate(uint[] px, int w, int h, int quarterTurns)
    {
        quarterTurns = ((quarterTurns % 4) + 4) % 4;
        if (quarterTurns == 0) return (uint[])px.Clone();
        if (quarterTurns == 2)
        {
            var r180 = (uint[])px.Clone();
            Array.Reverse(r180);
            return r180;
        }
        var r = new uint[px.Length];
        int nw = h;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            // CW: (x,y) -> (h-1-y, x); CCW: (x,y) -> (y, w-1-x)
            int idx = quarterTurns == 1 ? x * nw + (h - 1 - y) : (w - 1 - x) * nw + y;
            r[idx] = px[y * w + x];
        }
        return r;
    }

    /// <summary>Copies a region into a new buffer; areas outside the source become <paramref name="fill"/>.</summary>
    public static uint[] Crop(uint[] px, int w, int h, int x, int y, int cw, int ch, uint fill = Px.Transparent)
    {
        var r = new uint[cw * ch];
        if (fill != 0) Array.Fill(r, fill);
        for (int ry = 0; ry < ch; ry++)
        {
            int sy = y + ry;
            if ((uint)sy >= (uint)h) continue;
            int sx0 = Math.Max(0, x), sx1 = Math.Min(w, x + cw);
            if (sx1 > sx0) Array.Copy(px, sy * w + sx0, r, ry * cw + (sx0 - x), sx1 - sx0);
        }
        return r;
    }

    public static uint[] ResizeNearest(uint[] px, int w, int h, int nw, int nh)
    {
        var r = new uint[nw * nh];
        for (int y = 0; y < nh; y++)
        {
            int sy = Math.Min(h - 1, (int)((y + 0.5) * h / nh));
            for (int x = 0; x < nw; x++)
                r[y * nw + x] = px[sy * w + Math.Min(w - 1, (int)((x + 0.5) * w / nw))];
        }
        return r;
    }

    /// <summary>Smooth resize: bilinear when enlarging, box-average when shrinking (alpha-weighted).</summary>
    public static uint[] ResizeSmooth(uint[] px, int w, int h, int nw, int nh)
    {
        var r = new uint[nw * nh];
        double scaleX = (double)w / nw, scaleY = (double)h / nh;
        for (int y = 0; y < nh; y++)
        for (int x = 0; x < nw; x++)
        {
            double a = 0, rr = 0, gg = 0, bb = 0, wsum = 0;
            if (scaleX <= 1 && scaleY <= 1)
            {
                double fx = (x + 0.5) * scaleX - 0.5, fy = (y + 0.5) * scaleY - 0.5;
                int ix = (int)Math.Floor(fx), iy = (int)Math.Floor(fy);
                double tx = fx - ix, ty = fy - iy;
                for (int j = 0; j <= 1; j++)
                for (int i = 0; i <= 1; i++)
                {
                    int sx = Math.Clamp(ix + i, 0, w - 1), sy = Math.Clamp(iy + j, 0, h - 1);
                    double wt = (i == 0 ? 1 - tx : tx) * (j == 0 ? 1 - ty : ty);
                    Accumulate(px[sy * w + sx], wt, ref a, ref rr, ref gg, ref bb);
                    wsum += wt;
                }
            }
            else
            {
                int sx0 = (int)(x * scaleX), sx1 = Math.Max(sx0 + 1, Math.Min(w, (int)Math.Ceiling((x + 1) * scaleX)));
                int sy0 = (int)(y * scaleY), sy1 = Math.Max(sy0 + 1, Math.Min(h, (int)Math.Ceiling((y + 1) * scaleY)));
                for (int sy = sy0; sy < sy1; sy++)
                for (int sx = sx0; sx < sx1; sx++)
                {
                    Accumulate(px[Math.Min(h - 1, sy) * w + Math.Min(w - 1, sx)], 1, ref a, ref rr, ref gg, ref bb);
                    wsum += 1;
                }
            }
            if (a <= 0) continue;
            r[y * nw + x] = Px.Pack((int)Math.Round(a / wsum), (int)Math.Round(rr / a), (int)Math.Round(gg / a), (int)Math.Round(bb / a));
        }
        return r;
    }

    private static void Accumulate(uint c, double wt, ref double a, ref double r, ref double g, ref double b)
    {
        double ca = Px.A(c) * wt;
        a += ca;
        r += Px.R(c) * ca;
        g += Px.G(c) * ca;
        b += Px.B(c) * ca;
    }

    /// <summary>Blends <paramref name="src"/> (sw×sh) onto <paramref name="dst"/> at (x,y).</summary>
    public static void BlendOnto(uint[] dst, int dw, int dh, uint[] src, int sw, int sh, int x, int y)
    {
        for (int sy = 0; sy < sh; sy++)
        {
            int dy = y + sy;
            if ((uint)dy >= (uint)dh) continue;
            for (int sx = 0; sx < sw; sx++)
            {
                int dx = x + sx;
                if ((uint)dx >= (uint)dw) continue;
                dst[dy * dw + dx] = Px.Over(dst[dy * dw + dx], src[sy * sw + sx]);
            }
        }
    }

    private static void Order(ref int a, ref int b)
    {
        if (a > b) (a, b) = (b, a);
    }
}
