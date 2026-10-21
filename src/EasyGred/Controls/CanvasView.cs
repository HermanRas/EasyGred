using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using EasyGred.Core;

namespace EasyGred.Controls;

public sealed class ToolSettings
{
    public int BrushSize { get; set; } = 3;
    public bool RoundBrush { get; set; } = true;
    public int EraserSize { get; set; } = 8;
    public int AirbrushSize { get; set; } = 8;
    public int LineWidth { get; set; } = 1;
    public ShapeFill ShapeFill { get; set; } = ShapeFill.Outline;
    public int Tolerance { get; set; }
    public bool Contiguous { get; set; } = true;
    public bool TransparentPaste { get; set; } = true;

    public string FontFamily { get; set; } = OperatingSystem.IsWindows() ? "Arial" : "DejaVu Sans";
    public float FontSize { get; set; } = 16;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public bool TextAntialias { get; set; }
    public bool TextOpaque { get; set; }

    public TextStyle TextStyle => new(FontFamily, FontSize, Bold, Italic, Underline, TextAntialias);
}

/// <summary>
/// The drawing surface: renders the document (with checkerboard, grid and selection outline)
/// and turns pointer input into tool operations. Editing commands live in CanvasView.Commands.cs.
/// </summary>
public sealed partial class CanvasView : Control
{
    public static readonly double[] ZoomLevels = { 0.125, 0.25, 0.5, 1, 2, 3, 4, 6, 8, 12, 16, 24, 32 };

    public Document Doc { get; private set; } = Document.CreateBlank(640, 480, Px.White);
    public History History { get; } = new();
    public Selection? Selection { get; private set; }
    public ToolSettings Settings { get; } = new();

    /// <summary>Raised after pixels, size, layers or selection change.</summary>
    public event Action? DocumentChanged;
    public event Action? ColorsChanged;
    public event Action<int, int>? HoverChanged;
    public event Action<int, int>? TextRequested;
    public event Action<double, Point>? ZoomRequested;
    public event Action? PickedColor;
    public event Action? ToolChanged;

    private uint _primary = Px.Black, _secondary = Px.White;
    public uint Primary { get => _primary; set { _primary = value; ColorsChanged?.Invoke(); } }
    public uint Secondary { get => _secondary; set { _secondary = value; ColorsChanged?.Invoke(); } }

    private Tool _tool = Tool.Pencil;
    public Tool Tool
    {
        get => _tool;
        set
        {
            if (_tool == value) return;
            if (value is not (Tool.RectSelect or Tool.FreeSelect or Tool.Replace)) CommitSelection();
            _tool = value;
            UpdateCursor(null);
            InvalidateVisual();
            ToolChanged?.Invoke();
        }
    }

    private double _zoom = 1;
    public double Zoom
    {
        get => _zoom;
        set
        {
            _zoom = Math.Clamp(value, ZoomLevels[0], ZoomLevels[^1]);
            RenderOptions.SetBitmapInterpolationMode(this, _zoom >= 1 ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality);
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    private bool _showGrid;
    public bool ShowGrid { get => _showGrid; set { _showGrid = value; InvalidateVisual(); } }

    private WriteableBitmap? _bitmap;
    private uint[] _composite = Array.Empty<uint>();
    private bool _pixelsDirty = true;
    private readonly DispatcherTimer _antsTimer;
    private readonly DispatcherTimer _sprayTimer;
    private readonly Random _rng = new();
    private int _antsPhase;
    private (int X, int Y)? _hover;

    public CanvasView()
    {
        ClipToBounds = true;
        Focusable = true;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
        _antsTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Background, (_, _) =>
        {
            if (Selection == null && _selDrag == SelDrag.None) return;
            _antsPhase = (_antsPhase + 1) % 8;
            InvalidateVisual();
        });
        _antsTimer.Start();
        _sprayTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(30), DispatcherPriority.Input, (_, _) => SprayTick());
        UpdateCursor(null);
    }

    /// <summary>Marks the composite stale and repaints. <paramref name="resized"/> also re-measures.</summary>
    public void Refresh(bool resized = false)
    {
        _pixelsDirty = true;
        if (resized) InvalidateMeasure();
        InvalidateVisual();
        DocumentChanged?.Invoke();
    }

    protected override Size MeasureOverride(Size availableSize) => new(Doc.Width * Zoom, Doc.Height * Zoom);

    // ---------------- rendering ----------------

    private void UpdateBitmap()
    {
        int w = Doc.Width, h = Doc.Height;
        if (_bitmap == null || _bitmap.PixelSize.Width != w || _bitmap.PixelSize.Height != h)
        {
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            _composite = new uint[w * h];
        }
        Doc.CompositePremul(_composite, Selection);
        using var fb = _bitmap.Lock();
        unsafe
        {
            fixed (uint* src = _composite)
            {
                for (int y = 0; y < h; y++)
                    Buffer.MemoryCopy(src + y * w, (byte*)fb.Address + y * fb.RowBytes, fb.RowBytes, w * 4);
            }
        }
        _pixelsDirty = false;
    }

    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), 1);
    private static readonly IPen AntsWhite = new Pen(Brushes.White, 1);
    private static readonly IPen CursorPen = new Pen(new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)), 1);

    public override void Render(DrawingContext ctx)
    {
        if (_pixelsDirty || _bitmap == null) UpdateBitmap();
        double z = Zoom;
        var dest = new Rect(0, 0, Doc.Width * z, Doc.Height * z);
        ctx.FillRectangle(Checker.Brush(8), dest);
        ctx.DrawImage(_bitmap!, new Rect(0, 0, Doc.Width, Doc.Height), dest);

        if (ShowGrid && z >= 4)
        {
            for (int x = 1; x < Doc.Width; x++) ctx.DrawLine(GridPen, new Point(x * z + 0.5, 0), new Point(x * z + 0.5, dest.Height));
            for (int y = 1; y < Doc.Height; y++) ctx.DrawLine(GridPen, new Point(0, y * z + 0.5), new Point(dest.Width, y * z + 0.5));
        }

        var ants = new Pen(Brushes.Black, 1, new DashStyle(new double[] { 4, 4 }, _antsPhase));
        if (Selection != null)
        {
            using (ctx.PushTransform(Matrix.CreateTranslation(Selection.X * z, Selection.Y * z)))
            {
                var geo = SelectionGeometry();
                ctx.DrawGeometry(null, AntsWhite, geo);
                ctx.DrawGeometry(null, ants, geo);
            }
        }

        if (_selDrag == SelDrag.NewRect)
        {
            var r = new Rect(Math.Min(_startX, _lastX) * z + 0.5, Math.Min(_startY, _lastY) * z + 0.5,
                (Math.Abs(_lastX - _startX) + 1) * z - 1, (Math.Abs(_lastY - _startY) + 1) * z - 1);
            ctx.DrawRectangle(AntsWhite, r);
            ctx.DrawRectangle(ants, r);
        }
        else if (_selDrag == SelDrag.Lasso && _lasso.Count > 1)
        {
            var g = new StreamGeometry();
            using (var sg = g.Open())
            {
                sg.BeginFigure(new Point(_lasso[0].X * z, _lasso[0].Y * z), false);
                foreach (var p in _lasso.Skip(1)) sg.LineTo(new Point(p.X * z, p.Y * z));
                sg.EndFigure(false);
            }
            ctx.DrawGeometry(null, AntsWhite, g);
            ctx.DrawGeometry(null, ants, g);
        }

        // Footprint preview for size-based tools.
        if (_hover is { } hv && !_down && Tool is Tool.Eraser or Tool.Brush && (z >= 2 || Tool == Tool.Eraser))
        {
            int size = Tool == Tool.Eraser ? Settings.EraserSize : Settings.BrushSize;
            int off = size / 2;
            ctx.DrawRectangle(CursorPen, new Rect((hv.X - off) * z + 0.5, (hv.Y - off) * z + 0.5, size * z - 1, size * z - 1));
        }
    }

    private Geometry? _selGeo;
    private (Selection?, int, double) _selGeoKey;

    private Geometry SelectionGeometry()
    {
        var key = (Selection, Selection!.ShapeVersion, Zoom);
        if (_selGeo != null && _selGeoKey == key) return _selGeo;
        var g = new StreamGeometry();
        double z = Zoom;
        using (var sg = g.Open())
        {
            foreach (var (x0, y0, x1, y1) in Selection.OutlineSegments())
            {
                // Inset right/bottom edges by half a pixel so they stay inside the selection.
                double ax = x0 * z + (x0 == 0 ? 0.5 : 0), ay = y0 * z + (y0 == 0 ? 0.5 : 0);
                double bx = x1 * z - (x1 == Selection.W && x0 == x1 ? 0.5 : 0), by = y1 * z - (y1 == Selection.H && y0 == y1 ? 0.5 : 0);
                if (x0 == x1) ax = bx = x0 == Selection.W ? x0 * z - 0.5 : x0 * z + 0.5;
                if (y0 == y1) ay = by = y0 == Selection.H ? y0 * z - 0.5 : y0 * z + 0.5;
                sg.BeginFigure(new Point(ax, ay), false);
                sg.LineTo(new Point(bx, by));
                sg.EndFigure(false);
            }
        }
        _selGeo = g;
        _selGeoKey = key;
        return g;
    }

    // ---------------- input ----------------

    private enum SelDrag { None, NewRect, Lasso, Move }

    private bool _down, _right, _shift;
    private int _startX, _startY, _lastX, _lastY, _grabX, _grabY;
    private uint[]? _base;
    private SelDrag _selDrag;
    private readonly List<(double X, double Y)> _lasso = new();

    private uint PaintColor => _right ? Secondary : Primary;
    private uint OtherColor => _right ? Primary : Secondary;

    private (int X, int Y) ToDoc(Point p) => ((int)Math.Floor(p.X / Zoom), (int)Math.Floor(p.Y / Zoom));

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var props = e.GetCurrentPoint(this).Properties;
        if (!props.IsLeftButtonPressed && !props.IsRightButtonPressed) return;
        if (_down) return;
        Focus();
        _right = props.IsRightButtonPressed && !props.IsLeftButtonPressed;
        _shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var pos = e.GetPosition(this);
        var (x, y) = ToDoc(pos);
        _startX = _lastX = x;
        _startY = _lastY = y;
        _down = true;
        e.Pointer.Capture(this);
        e.Handled = true;
        BeginTool(x, y, pos);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);
        var (x, y) = ToDoc(pos);
        _hover = (x, y);
        HoverChanged?.Invoke(x, y);
        if (_down)
        {
            _shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            ContinueTool(x, y, pos);
            _lastX = x;
            _lastY = y;
        }
        else
        {
            UpdateCursor((x, y));
            if (Tool is Tool.Eraser or Tool.Brush) InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_down) return;
        var (x, y) = ToDoc(e.GetPosition(this));
        _down = false;
        e.Pointer.Capture(null);
        EndTool(x, y);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (!_down) return;
        _down = false;
        EndTool(_lastX, _lastY);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = null;
        HoverChanged?.Invoke(-1, -1);
        InvalidateVisual();
    }

    private void UpdateCursor((int X, int Y)? at)
    {
        bool overSel = at is { } p && Selection != null && Tool is Tool.RectSelect or Tool.FreeSelect && Selection.Contains(p.X, p.Y);
        Cursor = new Cursor(overSel ? StandardCursorType.SizeAll : Tool switch
        {
            Tool.Text => StandardCursorType.Ibeam,
            Tool.Magnifier => StandardCursorType.Hand,
            _ => StandardCursorType.Cross,
        });
    }

    private uint[] Layer => Doc.Active.Pixels;

    private void BeginTool(int x, int y, Point pos)
    {
        int w = Doc.Width, h = Doc.Height;
        switch (Tool)
        {
            case Tool.Pencil:
            case Tool.Brush:
            case Tool.Eraser:
                CommitSelection();
                History.Push(Doc);
                Stroke(x, y, x, y);
                break;

            case Tool.Airbrush:
                CommitSelection();
                History.Push(Doc);
                SprayTick();
                _sprayTimer.Start();
                break;

            case Tool.Fill:
            {
                CommitSelection();
                if (!Doc.InBounds(x, y)) break;
                History.Push(Doc);
                int changed = Settings.Contiguous
                    ? Raster.FloodFill(Layer, w, h, x, y, PaintColor, Settings.Tolerance)
                    : Raster.ReplaceColor(Layer, Layer[y * w + x], PaintColor, Settings.Tolerance);
                if (changed == 0) History.DiscardLast();
                Refresh();
                break;
            }

            case Tool.Replace:
            {
                if (!Doc.InBounds(x, y)) break;
                CommitSelection(keepShape: true);
                History.Push(Doc);
                int changed = Raster.ReplaceColor(Layer, Layer[y * w + x], PaintColor, Settings.Tolerance, Selection?.ScopeFor(w));
                if (changed == 0) History.DiscardLast();
                Refresh();
                break;
            }

            case Tool.Picker:
                if (Doc.InBounds(x, y))
                {
                    if (_right) Secondary = Layer[y * w + x];
                    else Primary = Layer[y * w + x];
                    PickedColor?.Invoke();
                }
                _down = false;
                break;

            case Tool.Magnifier:
            {
                int i = Array.FindIndex(ZoomLevels, l => l >= Zoom - 1e-9);
                double next = _right ? ZoomLevels[Math.Max(0, i - 1)] : ZoomLevels[Math.Min(ZoomLevels.Length - 1, i + 1)];
                ZoomRequested?.Invoke(next, new Point(pos.X / Zoom, pos.Y / Zoom));
                _down = false;
                break;
            }

            case Tool.Line:
            case Tool.Rectangle:
            case Tool.Ellipse:
                CommitSelection();
                History.Push(Doc);
                _base = (uint[])Layer.Clone();
                DrawShape(x, y);
                break;

            case Tool.RectSelect:
            case Tool.FreeSelect:
                if (Selection != null && Selection.Contains(x, y))
                {
                    if (!Selection.IsFloating)
                    {
                        History.Push(Doc);
                        Selection.Lift(Layer, w, h);
                    }
                    _grabX = x - Selection.X;
                    _grabY = y - Selection.Y;
                    _selDrag = SelDrag.Move;
                    Refresh();
                }
                else
                {
                    CommitSelection();
                    _selDrag = Tool == Tool.RectSelect ? SelDrag.NewRect : SelDrag.Lasso;
                    _lasso.Clear();
                    _lasso.Add((pos.X / Zoom, pos.Y / Zoom));
                    InvalidateVisual();
                }
                break;

            case Tool.Text:
                CommitSelection();
                _down = false;
                if (Doc.InBounds(x, y)) TextRequested?.Invoke(x, y);
                break;
        }
    }

    private void ContinueTool(int x, int y, Point pos)
    {
        switch (Tool)
        {
            case Tool.Pencil:
            case Tool.Brush:
            case Tool.Eraser:
                Stroke(_lastX, _lastY, x, y);
                break;
            case Tool.Line:
            case Tool.Rectangle:
            case Tool.Ellipse:
                DrawShape(x, y);
                break;
            case Tool.RectSelect:
            case Tool.FreeSelect:
                if (_selDrag == SelDrag.Move && Selection != null)
                {
                    Selection.X = x - _grabX;
                    Selection.Y = y - _grabY;
                    Refresh();
                }
                else if (_selDrag == SelDrag.Lasso)
                {
                    var last = _lasso[^1];
                    var p = (pos.X / Zoom, pos.Y / Zoom);
                    if (Math.Abs(p.Item1 - last.X) + Math.Abs(p.Item2 - last.Y) >= 0.5) _lasso.Add(p);
                    InvalidateVisual();
                }
                else InvalidateVisual();
                break;
        }
    }

    private void EndTool(int x, int y)
    {
        _sprayTimer.Stop();
        _base = null;
        switch (_selDrag)
        {
            case SelDrag.NewRect:
                Selection = Selection.FromRect(_startX, _startY, x, y, Doc.Width, Doc.Height);
                break;
            case SelDrag.Lasso:
                Selection = Selection.FromPolygon(_lasso, Doc.Width, Doc.Height);
                _lasso.Clear();
                break;
        }
        _selDrag = SelDrag.None;
        Refresh();
    }

    private void Stroke(int x0, int y0, int x1, int y1)
    {
        var (size, color, round) = Tool switch
        {
            Tool.Pencil => (1, PaintColor, false),
            Tool.Eraser => (Settings.EraserSize, Px.Transparent, false),
            _ => (Settings.BrushSize, PaintColor, Settings.RoundBrush),
        };
        Raster.Line(Layer, Doc.Width, Doc.Height, x0, y0, x1, y1, size, color, round);
        Refresh();
    }

    private void SprayTick()
    {
        if (!_down) { _sprayTimer.Stop(); return; }
        int r = Math.Max(2, Settings.AirbrushSize);
        Raster.Spray(Layer, Doc.Width, Doc.Height, _lastX, _lastY, r, Math.Max(3, r), PaintColor, _rng);
        Refresh();
    }

    private void DrawShape(int x, int y)
    {
        if (_base == null) return;
        Array.Copy(_base, Layer, _base.Length);
        int dx = x - _startX, dy = y - _startY;
        if (_shift)
        {
            if (Tool == Tool.Line)
            {
                // Snap to horizontal, vertical or 45°.
                int ax = Math.Abs(dx), ay = Math.Abs(dy);
                if (ay * 2 < ax) y = _startY;
                else if (ax * 2 < ay) x = _startX;
                else
                {
                    int m = Math.Max(ax, ay);
                    x = _startX + Math.Sign(dx) * m;
                    y = _startY + Math.Sign(dy) * m;
                }
            }
            else
            {
                int m = Math.Max(Math.Abs(dx), Math.Abs(dy));
                x = _startX + (dx < 0 ? -m : m);
                y = _startY + (dy < 0 ? -m : m);
            }
        }
        int w = Doc.Width, h = Doc.Height;
        switch (Tool)
        {
            case Tool.Line:
                Raster.Line(Layer, w, h, _startX, _startY, x, y, Settings.LineWidth, PaintColor, Settings.LineWidth > 2);
                break;
            case Tool.Rectangle:
                Raster.Rectangle(Layer, w, h, _startX, _startY, x, y, Settings.LineWidth, Settings.ShapeFill, PaintColor, OtherColor);
                break;
            case Tool.Ellipse:
                Raster.Ellipse(Layer, w, h, _startX, _startY, x, y, Settings.LineWidth, Settings.ShapeFill, PaintColor, OtherColor);
                break;
        }
        Refresh();
    }
}
