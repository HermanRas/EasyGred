using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using EasyGred.Core;

namespace EasyGred.Controls;

public static class Checker
{
    private static readonly Dictionary<int, IBrush> Cache = new();

    /// <summary>Tiled light-gray/white checkerboard used behind transparent pixels.</summary>
    public static IBrush Brush(int cell = 8)
    {
        if (Cache.TryGetValue(cell, out var b)) return b;
        int size = cell * 2;
        var bmp = new WriteableBitmap(new PixelSize(size, size), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var fb = bmp.Lock())
        {
            unsafe
            {
                for (int y = 0; y < size; y++)
                {
                    var row = (uint*)((byte*)fb.Address + y * fb.RowBytes);
                    for (int x = 0; x < size; x++)
                        row[x] = (x / cell + y / cell) % 2 == 0 ? 0xFFFFFFFF : 0xFFCCCCCC;
                }
            }
        }
        b = new ImageBrush(bmp)
        {
            TileMode = TileMode.Tile,
            Stretch = Stretch.None,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top,
            DestinationRect = new RelativeRect(0, 0, size, size, RelativeUnit.Absolute),
        };
        Cache[cell] = b;
        return b;
    }

    public static Color ToColor(uint c) => Color.FromUInt32(c);
}

/// <summary>A colour well with a sunken bevel; shows a checkerboard through transparent colours.</summary>
public sealed class Swatch : Control
{
    public static readonly StyledProperty<uint> ColorProperty =
        AvaloniaProperty.Register<Swatch, uint>(nameof(Color), Px.Black);

    public static readonly StyledProperty<bool> SelectedProperty =
        AvaloniaProperty.Register<Swatch, bool>(nameof(Selected));

    static Swatch() => AffectsRender<Swatch>(ColorProperty, SelectedProperty);

    public uint Color
    {
        get => GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public bool Selected
    {
        get => GetValue(SelectedProperty);
        set => SetValue(SelectedProperty, value);
    }

    private static readonly IPen Dark = new Pen(new SolidColorBrush(Colors.Gray), 1);
    private static readonly IPen Black = new Pen(Brushes.Black, 1);
    private static readonly IPen White = new Pen(Brushes.White, 1);
    private static readonly IPen Light = new Pen(new SolidColorBrush(Avalonia.Media.Color.FromRgb(0xDF, 0xDF, 0xDF)), 1);

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 4 || h < 4) return;
        // sunken bevel: shadow top/left, light bottom/right (pixel-aligned with +0.5)
        ctx.DrawLine(Dark, new Point(0.5, h - 1), new Point(0.5, 0.5));
        ctx.DrawLine(Dark, new Point(0.5, 0.5), new Point(w - 1, 0.5));
        ctx.DrawLine(Black, new Point(1.5, h - 2), new Point(1.5, 1.5));
        ctx.DrawLine(Black, new Point(1.5, 1.5), new Point(w - 2, 1.5));
        ctx.DrawLine(White, new Point(0, h - 0.5), new Point(w, h - 0.5));
        ctx.DrawLine(White, new Point(w - 0.5, 0), new Point(w - 0.5, h));
        ctx.DrawLine(Light, new Point(1, h - 1.5), new Point(w - 1, h - 1.5));
        ctx.DrawLine(Light, new Point(w - 1.5, 1), new Point(w - 1.5, h - 1));

        var inner = new Rect(2, 2, w - 4, h - 4);
        if (Px.A(Color) < 255) ctx.FillRectangle(Checker.Brush(4), inner);
        ctx.FillRectangle(new SolidColorBrush(Checker.ToColor(Color)), inner);
        if (Px.A(Color) == 0)
            ctx.DrawLine(new Pen(Brushes.Red, 1), inner.BottomLeft, inner.TopRight);
        if (Selected)
            ctx.DrawRectangle(new Pen(Brushes.Black, 1, new DashStyle(new double[] { 1, 1 }, 0)), inner.Deflate(1.5));
    }
}
