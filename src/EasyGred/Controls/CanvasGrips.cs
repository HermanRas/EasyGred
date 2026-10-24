using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering;

namespace EasyGred.Controls;

/// <summary>
/// Paint-style canvas size handles on the right edge, bottom edge and bottom-right corner of the
/// picture. Dragging one shows a dashed outline of the new size; releasing raises
/// <see cref="ResizeRequested"/>. Only the handles take pointer input, so drawing near the edge is unaffected.
/// </summary>
public sealed class CanvasGrips : Control, ICustomHitTest
{
    public const int MaxSize = 16384;
    private const double G = 7;

    [Flags]
    private enum Grip { None = 0, Right = 1, Bottom = 2, Corner = Right | Bottom }

    private readonly CanvasView _view;
    private Grip _drag;
    private Point _grab;
    private int _startW, _startH, _newW, _newH;

    /// <summary>The size being previewed while dragging.</summary>
    public event Action<int, int>? Previewing;
    /// <summary>The new canvas size when a drag ends with a change.</summary>
    public event Action<int, int>? ResizeRequested;

    public CanvasGrips(CanvasView view)
    {
        _view = view;
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        _view.PropertyChanged += (_, e) =>
        {
            if (e.Property != BoundsProperty) return;
            InvalidateMeasure();
            InvalidateVisual();
        };
    }

    private double DocW => _view.Doc.Width * _view.Zoom;
    private double DocH => _view.Doc.Height * _view.Zoom;

    // While dragging the control grows with the outline, so the scroll area grows too.
    protected override Size MeasureOverride(Size availableSize) => _drag == Grip.None
        ? new Size(DocW + G, DocH + G)
        : new Size(Math.Max(DocW, _newW * _view.Zoom) + G, Math.Max(DocH, _newH * _view.Zoom) + G);

    private Rect GripRect(Grip g) => g switch
    {
        Grip.Right => new Rect(DocW, Math.Floor((DocH - G) / 2), G, G),
        Grip.Bottom => new Rect(Math.Floor((DocW - G) / 2), DocH, G, G),
        _ => new Rect(DocW, DocH, G, G),
    };

    private Grip GripAt(Point p)
    {
        foreach (var g in new[] { Grip.Corner, Grip.Right, Grip.Bottom })
            if (GripRect(g).Contains(p)) return g;
        return Grip.None;
    }

    public bool HitTest(Point point) => _drag != Grip.None || GripAt(point) != Grip.None;

    private static readonly IBrush GripFill = new SolidColorBrush(Color.FromRgb(0, 0, 128));
    private static readonly IPen GripPen = new Pen(Brushes.White, 1);
    private static readonly IPen OutlineWhite = new Pen(Brushes.White, 1);
    private static readonly IPen OutlineDash = new Pen(Brushes.Black, 1, new DashStyle(new double[] { 3, 3 }, 0));

    public override void Render(DrawingContext ctx)
    {
        foreach (var g in new[] { Grip.Right, Grip.Bottom, Grip.Corner })
        {
            var r = GripRect(g);
            ctx.FillRectangle(GripFill, r);
            ctx.DrawRectangle(GripPen, r.Deflate(0.5));
        }
        if (_drag == Grip.None) return;
        double z = _view.Zoom;
        var outline = new Rect(0.5, 0.5, Math.Max(1, _newW * z - 1), Math.Max(1, _newH * z - 1));
        ctx.DrawRectangle(OutlineWhite, outline);
        ctx.DrawRectangle(OutlineDash, outline);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var g = GripAt(e.GetPosition(this));
        if (g == Grip.None) return;
        _drag = g;
        _grab = e.GetPosition(this);
        _newW = _startW = _view.Doc.Width;
        _newH = _startH = _view.Doc.Height;
        e.Pointer.Capture(this);
        e.Handled = true;
        Previewing?.Invoke(_newW, _newH);
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (_drag == Grip.None)
        {
            Cursor = new Cursor(GripAt(p) switch
            {
                Grip.Right => StandardCursorType.SizeWestEast,
                Grip.Bottom => StandardCursorType.SizeNorthSouth,
                _ => StandardCursorType.BottomRightCorner,
            });
            return;
        }
        double z = _view.Zoom;
        if (_drag.HasFlag(Grip.Right)) _newW = Math.Clamp(_startW + (int)Math.Round((p.X - _grab.X) / z), 1, MaxSize);
        if (_drag.HasFlag(Grip.Bottom)) _newH = Math.Clamp(_startH + (int)Math.Round((p.Y - _grab.Y) / z), 1, MaxSize);
        Previewing?.Invoke(_newW, _newH);
        InvalidateMeasure();
        InvalidateVisual();
        // Scroll along when dragging past the visible area.
        this.BringIntoView(new Rect(p.X - G, p.Y - G, G * 2, G * 2));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag == Grip.None) return;
        var changed = _newW != _startW || _newH != _startH;
        EndDrag();
        e.Pointer.Capture(null);
        if (changed) ResizeRequested?.Invoke(_newW, _newH);
        else Previewing?.Invoke(_startW, _startH);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_drag == Grip.None) return;
        EndDrag();
        Previewing?.Invoke(_startW, _startH);
    }

    private void EndDrag()
    {
        _drag = Grip.None;
        InvalidateMeasure();
        InvalidateVisual();
    }
}
