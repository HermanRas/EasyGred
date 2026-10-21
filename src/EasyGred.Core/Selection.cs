namespace EasyGred.Core;

/// <summary>
/// A rectangular or free-form selection. While not floating it only marks pixels of the active layer;
/// once lifted (moved, pasted, transformed) its pixels live in <see cref="Floating"/> until committed.
/// </summary>
public sealed class Selection
{
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; private set; }
    public int H { get; private set; }

    /// <summary>W*H mask (non-zero = selected); null means the whole rectangle.</summary>
    public byte[]? Mask { get; private set; }

    /// <summary>W*H pixels once the selection has been lifted off the layer.</summary>
    public uint[]? Floating { get; private set; }

    /// <summary>Bumped whenever the shape (not the position) changes, so outlines can be cached.</summary>
    public int ShapeVersion { get; private set; }

    public bool IsFloating => Floating != null;

    private Selection(int x, int y, int w, int h, byte[]? mask)
    {
        X = x; Y = y; W = w; H = h; Mask = mask;
    }

    public static Selection? FromRect(int x0, int y0, int x1, int y1, int docW, int docH)
    {
        int left = Math.Clamp(Math.Min(x0, x1), 0, docW), right = Math.Clamp(Math.Max(x0, x1) + 1, 0, docW);
        int top = Math.Clamp(Math.Min(y0, y1), 0, docH), bottom = Math.Clamp(Math.Max(y0, y1) + 1, 0, docH);
        if (right - left < 1 || bottom - top < 1) return null;
        return new Selection(left, top, right - left, bottom - top, null);
    }

    /// <summary>Builds a selection from a polygon (document coordinates), sampling pixel centres (even-odd rule).</summary>
    public static Selection? FromPolygon(IReadOnlyList<(double X, double Y)> pts, int docW, int docH)
    {
        if (pts.Count < 3) return null;
        int left = Math.Clamp((int)Math.Floor(pts.Min(p => p.X)), 0, docW);
        int right = Math.Clamp((int)Math.Ceiling(pts.Max(p => p.X)), 0, docW);
        int top = Math.Clamp((int)Math.Floor(pts.Min(p => p.Y)), 0, docH);
        int bottom = Math.Clamp((int)Math.Ceiling(pts.Max(p => p.Y)), 0, docH);
        int w = right - left, h = bottom - top;
        if (w < 1 || h < 1) return null;

        var mask = new byte[w * h];
        var xs = new List<double>();
        bool any = false;
        for (int row = 0; row < h; row++)
        {
            double cy = top + row + 0.5;
            xs.Clear();
            for (int i = 0; i < pts.Count; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % pts.Count];
                if ((a.Y <= cy && b.Y > cy) || (b.Y <= cy && a.Y > cy))
                    xs.Add(a.X + (cy - a.Y) / (b.Y - a.Y) * (b.X - a.X));
            }
            xs.Sort();
            for (int k = 0; k + 1 < xs.Count; k += 2)
            {
                int from = Math.Max(0, (int)Math.Ceiling(xs[k] - 0.5) - left);
                int to = Math.Min(w - 1, (int)Math.Floor(xs[k + 1] - 0.5) - left);
                for (int col = from; col <= to; col++) { mask[row * w + col] = 1; any = true; }
            }
        }
        return any ? new Selection(left, top, w, h, mask) : null;
    }

    /// <summary>A floating selection holding pasted pixels.</summary>
    public static Selection FromPixels(int x, int y, int w, int h, uint[] pixels) =>
        new(x, y, w, h, null) { Floating = pixels };

    public bool InMask(int lx, int ly) =>
        (uint)lx < (uint)W && (uint)ly < (uint)H && (Mask == null || Mask[ly * W + lx] != 0);

    public bool Contains(int docX, int docY) => InMask(docX - X, docY - Y);

    /// <summary>Returns a predicate over layer indices (y*docW+x) for pixels inside the (non-floating) selection.</summary>
    public Func<int, bool> ScopeFor(int docW) => i => Contains(i % docW, i / docW);

    /// <summary>Copies the selected pixels out of a layer (unselected = transparent).</summary>
    public uint[] Extract(uint[] layer, int docW, int docH)
    {
        if (Floating != null) return (uint[])Floating.Clone();
        var result = new uint[W * H];
        for (int ly = 0; ly < H; ly++)
        for (int lx = 0; lx < W; lx++)
        {
            int dx = X + lx, dy = Y + ly;
            if (InMask(lx, ly) && (uint)dx < (uint)docW && (uint)dy < (uint)docH)
                result[ly * W + lx] = layer[dy * docW + dx];
        }
        return result;
    }

    /// <summary>Lifts the selected pixels off the layer, leaving transparency behind.</summary>
    public void Lift(uint[] layer, int docW, int docH)
    {
        if (Floating != null) return;
        Floating = Extract(layer, docW, docH);
        Clear(layer, docW, docH);
    }

    /// <summary>Sets the selected layer pixels to transparent.</summary>
    public void Clear(uint[] layer, int docW, int docH)
    {
        for (int ly = 0; ly < H; ly++)
        for (int lx = 0; lx < W; lx++)
        {
            int dx = X + lx, dy = Y + ly;
            if (InMask(lx, ly) && (uint)dx < (uint)docW && (uint)dy < (uint)docH)
                layer[dy * docW + dx] = Px.Transparent;
        }
    }

    /// <summary>Writes floating pixels back into the layer. Transparent mode blends; opaque mode replaces.</summary>
    public void Commit(uint[] layer, int docW, int docH, bool transparentPaste)
    {
        if (Floating == null) return;
        var fl = Floating;
        ForEachMasked(docW, docH, (di, si) =>
            layer[di] = transparentPaste ? Px.Over(layer[di], fl[si]) : fl[si]);
        Floating = null;
    }

    internal void ForEachFloatingPixel(int docW, int docH, Action<int, uint> action)
    {
        if (Floating == null) return;
        var fl = Floating;
        ForEachMasked(docW, docH, (di, si) => { if (fl[si] >> 24 != 0) action(di, fl[si]); });
    }

    private void ForEachMasked(int docW, int docH, Action<int, int> action)
    {
        int x0 = Math.Max(0, X), y0 = Math.Max(0, Y);
        int x1 = Math.Min(docW, X + W), y1 = Math.Min(docH, Y + H);
        for (int dy = y0; dy < y1; dy++)
        for (int dx = x0; dx < x1; dx++)
        {
            int lx = dx - X, ly = dy - Y;
            if (Mask == null || Mask[ly * W + lx] != 0) action(dy * docW + dx, ly * W + lx);
        }
    }

    /// <summary>Applies a pixel transform (flip/rotate) to the floating content and mask.</summary>
    public void TransformFloating(Func<uint[], int, int, (uint[] Pixels, int W, int H)> transform)
    {
        if (Floating == null) return;
        var (pixels, w, h) = transform(Floating, W, H);
        if (Mask != null)
        {
            var asPixels = Mask.Select(m => (uint)m).ToArray();
            Mask = transform(asPixels, W, H).Pixels.Select(v => (byte)v).ToArray();
        }
        // Keep the selection centred while width/height swap.
        X += (W - w) / 2;
        Y += (H - h) / 2;
        Floating = pixels;
        W = w;
        H = h;
        ShapeVersion++;
    }

    /// <summary>
    /// Outline edges (in local pixel units) between selected and unselected pixels, merged into runs.
    /// </summary>
    public List<(int X0, int Y0, int X1, int Y1)> OutlineSegments()
    {
        var segs = new List<(int, int, int, int)>();
        if (Mask == null)
        {
            segs.Add((0, 0, W, 0));
            segs.Add((W, 0, W, H));
            segs.Add((0, H, W, H));
            segs.Add((0, 0, 0, H));
            return segs;
        }
        // Horizontal edges at row boundary y (between rows y-1 and y).
        for (int y = 0; y <= H; y++)
        {
            int start = -1;
            for (int x = 0; x <= W; x++)
            {
                bool edge = x < W && InMask(x, y - 1) != InMask(x, y);
                if (edge && start < 0) start = x;
                else if (!edge && start >= 0) { segs.Add((start, y, x, y)); start = -1; }
            }
        }
        // Vertical edges at column boundary x.
        for (int x = 0; x <= W; x++)
        {
            int start = -1;
            for (int y = 0; y <= H; y++)
            {
                bool edge = y < H && InMask(x - 1, y) != InMask(x, y);
                if (edge && start < 0) start = y;
                else if (!edge && start >= 0) { segs.Add((x, start, x, y)); start = -1; }
            }
        }
        return segs;
    }
}
