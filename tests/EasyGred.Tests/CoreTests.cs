using EasyGred.Core;

namespace EasyGred.Tests;

public class PxTests
{
    [Theory]
    [InlineData("#FF8000", 0xFFFF8000u)]
    [InlineData("ff8000", 0xFFFF8000u)]
    [InlineData("#F80", 0xFFFF8800u)]
    [InlineData("#11223380", 0x80112233u)]
    public void ParsesHex(string hex, uint expected)
    {
        Assert.True(Px.TryParseHex(hex, out var c));
        Assert.Equal(expected, c);
    }

    [Fact]
    public void HexRoundTrip() => Assert.Equal("#11223380", Px.ToHex(0x80112233u, true));

    [Fact]
    public void RejectsBadHex() => Assert.False(Px.TryParseHex("#12345", out _));

    [Fact]
    public void HsvRoundTrip()
    {
        uint c = Px.Pack(255, 200, 100, 50);
        Px.ToHsv(c, out var h, out var s, out var v);
        Assert.Equal(c, Px.FromHsv(h, s, v));
    }

    [Fact]
    public void OverWithOpaqueSourceReplaces() => Assert.Equal(0xFF00FF00u, Px.Over(0xFFFF0000u, 0xFF00FF00u));

    [Fact]
    public void OverHalfAlphaOnOpaqueMixes()
    {
        uint r = Px.Over(Px.White, Px.Pack(128, 0, 0, 0));
        Assert.Equal(255, Px.A(r));
        Assert.InRange(Px.R(r), 126, 128);
    }
}

public class RasterTests
{
    private static uint[] Blank(int w, int h, uint c = Px.White) => Enumerable.Repeat(c, w * h).ToArray();

    [Fact]
    public void FloodFillStopsAtBorder()
    {
        var px = Blank(5, 5);
        for (int y = 0; y < 5; y++) px[y * 5 + 2] = Px.Black; // vertical wall at x=2
        int n = Raster.FloodFill(px, 5, 5, 0, 0, 0xFFFF0000u, 0);
        Assert.Equal(10, n);
        Assert.Equal(0xFFFF0000u, px[4 * 5 + 1]);
        Assert.Equal(Px.White, px[4]);
    }

    [Fact]
    public void FloodFillWithToleranceAndSameColorTerminates()
    {
        var px = Blank(4, 4);
        px[5] = Px.Pack(255, 250, 250, 250);
        int n = Raster.FloodFill(px, 4, 4, 0, 0, Px.White, 10);
        Assert.Equal(16, n);
        Assert.All(px, p => Assert.Equal(Px.White, p));
    }

    [Fact]
    public void ReplaceColorRespectsScope()
    {
        var px = Blank(4, 1);
        int n = Raster.ReplaceColor(px, Px.White, Px.Transparent, 0, i => i < 2);
        Assert.Equal(2, n);
        Assert.Equal(Px.Transparent, px[1]);
        Assert.Equal(Px.White, px[2]);
    }

    [Fact]
    public void RotateClockwiseMovesTopLeftToTopRight()
    {
        // 3x2 image, mark (0,0)
        var px = new uint[6];
        px[0] = 1;
        var r = Raster.Rotate(px, 3, 2, 1); // now 2 wide, 3 tall
        Assert.Equal(1u, r[1]); // (1,0)
        var back = Raster.Rotate(r, 2, 3, 3);
        Assert.Equal(px, back);
    }

    [Fact]
    public void FourQuarterTurnsAreIdentity()
    {
        var px = Enumerable.Range(0, 12).Select(i => (uint)i).ToArray();
        var r = Raster.Rotate(px, 4, 3, 1);
        r = Raster.Rotate(r, 3, 4, 1);
        r = Raster.Rotate(r, 4, 3, 1);
        r = Raster.Rotate(r, 3, 4, 1);
        Assert.Equal(px, r);
    }

    [Fact]
    public void CropOutsideIsTransparent()
    {
        var px = Blank(2, 2);
        var r = Raster.Crop(px, 2, 2, 1, 1, 2, 2);
        Assert.Equal(new[] { Px.White, 0u, 0u, 0u }, r);
    }

    [Fact]
    public void RectangleOutlineLeavesInteriorUntouched()
    {
        var px = Blank(5, 5, 0);
        Raster.Rectangle(px, 5, 5, 0, 0, 4, 4, 1, ShapeFill.Outline, Px.Black, Px.White);
        Assert.Equal(Px.Black, px[0]);
        Assert.Equal(0u, px[2 * 5 + 2]);
        Assert.Equal(16, px.Count(p => p == Px.Black));
    }

    [Fact]
    public void ResizeNearestDoublesPixels()
    {
        var px = new uint[] { 1, 2, 3, 4 };
        var r = Raster.ResizeNearest(px, 2, 2, 4, 4);
        Assert.Equal(new uint[] { 1, 1, 2, 2 }, r.Take(4));
    }
}

public class SelectionTests
{
    [Fact]
    public void LiftMoveCommit()
    {
        var layer = Enumerable.Repeat(Px.White, 16).ToArray();
        layer[0] = Px.Black;
        var sel = Selection.FromRect(0, 0, 0, 0, 4, 4)!;
        sel.Lift(layer, 4, 4);
        Assert.Equal(Px.Transparent, layer[0]);
        sel.X = 3; sel.Y = 3;
        sel.Commit(layer, 4, 4, transparentPaste: true);
        Assert.Equal(Px.Black, layer[15]);
    }

    [Fact]
    public void PolygonMaskSelectsTriangleInterior()
    {
        var sel = Selection.FromPolygon(new List<(double, double)> { (0, 0), (10, 0), (0, 10) }, 20, 20)!;
        Assert.True(sel.Contains(1, 1));
        Assert.False(sel.Contains(9, 9));
    }

    [Fact]
    public void RectOutlineHasFourEdges() => Assert.Equal(4, Selection.FromRect(1, 1, 3, 3, 10, 10)!.OutlineSegments().Count);
}

public class ImageIOTests
{
    private static uint[] Sample() => new[] { 0xFFFF0000u, 0x8000FF00u, 0x00000000u, 0xFF0000FFu, Px.White, Px.Black };

    [Theory]
    [InlineData(ImageFormat.Png)]
    [InlineData(ImageFormat.Bmp)]
    [InlineData(ImageFormat.Webp)]
    public void LosslessRoundTripKeepsAlpha(ImageFormat format)
    {
        var bytes = ImageIO.Encode(3, 2, Sample(), format, new SaveOptions(100, true));
        var (w, h, px) = ImageIO.Load(new MemoryStream(bytes));
        Assert.Equal((3, 2), (w, h));
        var expected = Sample();
        for (int i = 0; i < px.Length; i++)
        {
            if (Px.A(expected[i]) == 0) Assert.Equal(0, Px.A(px[i]));
            else Assert.True(Px.Distance(expected[i], px[i]) <= 1, $"pixel {i}: {px[i]:X8} vs {expected[i]:X8}");
        }
    }

    [Fact]
    public void OpaqueBmpIs24Bit()
    {
        var bytes = ImageIO.EncodeBmp(2, 1, new[] { Px.White, Px.Black });
        Assert.Equal(24, BitConverter.ToInt16(bytes, 28));
        var (_, _, px) = ImageIO.Load(new MemoryStream(bytes));
        Assert.Equal(new[] { Px.White, Px.Black }, px);
    }

    [Fact]
    public void JpegFlattensTransparencyToWhite()
    {
        var bytes = ImageIO.Encode(8, 8, new uint[64], ImageFormat.Jpeg, new SaveOptions(95));
        var (_, _, px) = ImageIO.Load(new MemoryStream(bytes));
        Assert.All(px, p => Assert.True(Px.Distance(p, Px.White) <= 2));
    }

    [Fact]
    public void TextRendersOpaquePixelsWhenAliased()
    {
        var (w, h, px) = TextRenderer.Render("Hi", new TextStyle("DejaVu Sans", 20, false, false, false, false), Px.Black);
        Assert.True(w > 5 && h > 10);
        Assert.Contains(px, p => p == Px.Black);
        Assert.All(px, p => Assert.True(p == Px.Black || p == 0));
    }
}
