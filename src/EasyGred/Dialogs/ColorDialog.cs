using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using EasyGred.Controls;
using EasyGred.Core;

namespace EasyGred.Dialogs;

/// <summary>Colour picker: hue/saturation field, brightness bar, RGBA sliders and hex entry.</summary>
public sealed class ColorDialog : DialogBase<uint?>
{
    private readonly SpectrumField _spectrum = new() { Width = 220, Height = 160 };
    private readonly ValueBar _valueBar = new() { Width = 18, Height = 160 };
    private readonly Swatch _newSwatch = new() { Width = 60, Height = 36 };
    private readonly TextBox _hex = new() { Width = 100 };
    private readonly (Slider Slider, NumericUpDown Box)[] _channels = new (Slider, NumericUpDown)[4];

    private uint _color;
    private double _h, _s, _v;
    private bool _updating;

    public ColorDialog(uint initial, IReadOnlyList<uint> basicColors, string title = "Edit Colors") : base(title)
    {
        // Left: basic colours
        var basics = new WrapPanel { Width = 8 * 22 };
        foreach (var c in basicColors)
        {
            var sw = new Swatch { Color = c, Width = 20, Height = 18, Margin = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand) };
            sw.PointerPressed += (_, _) => SetColor(sw.Color);
            basics.Children.Add(sw);
        }
        var left = new StackPanel { Spacing = 4 };
        left.Children.Add(new TextBlock { Text = "Basic colors:" });
        left.Children.Add(basics);
        var transparent = new Button { Content = "Transparent", Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        transparent.Click += (_, _) => SetColor(Px.WithAlpha(_color, 0));
        left.Children.Add(transparent);
        var opaque = new Button { Content = "Fully opaque", HorizontalAlignment = HorizontalAlignment.Left };
        opaque.Click += (_, _) => SetColor(Px.WithAlpha(_color, 255));
        left.Children.Add(opaque);

        // Right: spectrum + value bar + preview
        _spectrum.Changed += (h, s) =>
        {
            _h = h;
            _s = s;
            if (_v < 0.05) _v = 1;
            SetColor(Px.FromHsv(_h, _s, _v, Px.A(_color)), fromHsv: true);
        };
        _valueBar.Changed += v =>
        {
            _v = v;
            SetColor(Px.FromHsv(_h, _s, _v, Px.A(_color)), fromHsv: true);
        };
        var pickers = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        pickers.Children.Add(Sunken(_spectrum));
        pickers.Children.Add(Sunken(_valueBar));

        var oldSwatch = new Swatch { Color = initial, Width = 60, Height = 36, Cursor = new Cursor(StandardCursorType.Hand) };
        oldSwatch.PointerPressed += (_, _) => SetColor(initial);
        var preview = new StackPanel { Spacing = 2 };
        preview.Children.Add(new TextBlock { Text = "New" });
        preview.Children.Add(_newSwatch);
        preview.Children.Add(new TextBlock { Text = "Current" });
        preview.Children.Add(oldSwatch);
        pickers.Children.Add(preview);

        // Channel rows
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("60,*,Auto"), Margin = new Thickness(0, 8, 0, 0) };
        string[] names = { "Red:", "Green:", "Blue:", "Alpha:" };
        for (int i = 0; i < 4; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var label = Label(names[i]);
            var slider = new Slider { Minimum = 0, Maximum = 255, Width = 170, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0) };
            var box = Number(0, 0, 255, 100);
            box.Margin = new Thickness(0, 2);
            int ch = i;
            slider.PropertyChanged += (_, e) =>
            {
                if (e.Property == Slider.ValueProperty && !_updating) SetChannel(ch, (int)Math.Round(slider.Value));
            };
            box.ValueChanged += (_, _) =>
            {
                if (!_updating && box.Value is { } v) SetChannel(ch, (int)v);
            };
            Grid.SetRow(label, i);
            Grid.SetRow(slider, i);
            Grid.SetColumn(slider, 1);
            Grid.SetRow(box, i);
            Grid.SetColumn(box, 2);
            grid.Children.Add(label);
            grid.Children.Add(slider);
            grid.Children.Add(box);
            _channels[i] = (slider, box);
        }

        var hexRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 6, 0, 0) };
        hexRow.Children.Add(Label("Hex:", 54));
        hexRow.Children.Add(_hex);
        hexRow.Children.Add(new TextBlock { Text = "#RRGGBB or #RRGGBBAA", Foreground = Brushes.DimGray, VerticalAlignment = VerticalAlignment.Center });
        _hex.TextChanged += (_, _) =>
        {
            if (!_updating && Px.TryParseHex(_hex.Text, out var c)) SetColor(c, fromHex: true);
        };

        var right = new StackPanel();
        right.Children.Add(pickers);
        right.Children.Add(grid);
        right.Children.Add(hexRow);
        right.Children.Add(ButtonRow(("OK", () => Accept(_color), true), ("Cancel", () => Close(null), false)));

        var root = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Margin = new Thickness(12) };
        root.Children.Add(left);
        root.Children.Add(right);
        Content = root;

        Px.ToHsv(initial, out _h, out _s, out _v);
        SetColor(initial);
    }

    private static Border Sunken(Control c) => new()
    {
        BorderBrush = Brushes.Gray,
        BorderThickness = new Thickness(1, 1, 0, 0),
        Child = new Border { BorderBrush = Brushes.White, BorderThickness = new Thickness(0, 0, 1, 1), Child = c },
    };

    private void SetChannel(int ch, int value)
    {
        uint c = _color;
        c = ch switch
        {
            0 => Px.Pack(Px.A(c), value, Px.G(c), Px.B(c)),
            1 => Px.Pack(Px.A(c), Px.R(c), value, Px.B(c)),
            2 => Px.Pack(Px.A(c), Px.R(c), Px.G(c), value),
            _ => Px.WithAlpha(c, value),
        };
        SetColor(c);
    }

    private void SetColor(uint c, bool fromHsv = false, bool fromHex = false)
    {
        _color = c;
        if (!fromHsv)
        {
            Px.ToHsv(c, out var h, out var s, out var v);
            // Keep hue/saturation when they become undefined (grays, black).
            if (s > 0 && v > 0) _h = h;
            if (v > 0) _s = s;
            _v = v;
        }
        _updating = true;
        int[] values = { Px.R(c), Px.G(c), Px.B(c), Px.A(c) };
        for (int i = 0; i < 4; i++)
        {
            _channels[i].Slider.Value = values[i];
            _channels[i].Box.Value = values[i];
        }
        if (!fromHex) _hex.Text = Px.ToHex(c, Px.A(c) != 255);
        _newSwatch.Color = c;
        _spectrum.SetMarker(_h, _s);
        _valueBar.SetState(_h, _s, _v);
        _updating = false;
    }

    /// <summary>Hue left→right, saturation top (full) → bottom (none), at full brightness.</summary>
    private sealed class SpectrumField : Control
    {
        public event Action<double, double>? Changed;
        private double _h, _s;
        private static readonly IBrush Hues = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new(Colors.Red, 0), new(Colors.Yellow, 1 / 6.0), new(Colors.Lime, 2 / 6.0), new(Colors.Cyan, 3 / 6.0),
                new(Colors.Blue, 4 / 6.0), new(Colors.Magenta, 5 / 6.0), new(Colors.Red, 1),
            },
        };
        private static readonly IBrush Fade = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = new GradientStops { new(Color.FromArgb(0, 255, 255, 255), 0), new(Colors.White, 1) },
        };

        public SpectrumField() => Cursor = new Cursor(StandardCursorType.Cross);

        public void SetMarker(double h, double s)
        {
            _h = h;
            _s = s;
            InvalidateVisual();
        }

        public override void Render(DrawingContext ctx)
        {
            var r = new Rect(Bounds.Size);
            ctx.FillRectangle(Hues, r);
            ctx.FillRectangle(Fade, r);
            double x = _h / 360 * r.Width, y = (1 - _s) * r.Height;
            var pen = new Pen(Brushes.Black, 2);
            ctx.DrawLine(pen, new Point(x - 8, y), new Point(x - 3, y));
            ctx.DrawLine(pen, new Point(x + 3, y), new Point(x + 8, y));
            ctx.DrawLine(pen, new Point(x, y - 8), new Point(x, y - 3));
            ctx.DrawLine(pen, new Point(x, y + 3), new Point(x, y + 8));
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            e.Pointer.Capture(this);
            Pick(e.GetPosition(this));
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) Pick(e.GetPosition(this));
        }

        private void Pick(Point p) => Changed?.Invoke(
            Math.Clamp(p.X / Bounds.Width, 0, 0.9999) * 360,
            1 - Math.Clamp(p.Y / Bounds.Height, 0, 1));
    }

    /// <summary>Brightness: top = full, bottom = black, for the current hue/saturation.</summary>
    private sealed class ValueBar : Control
    {
        public event Action<double>? Changed;
        private double _h, _s, _v;

        public ValueBar() => Cursor = new Cursor(StandardCursorType.Hand);

        public void SetState(double h, double s, double v)
        {
            (_h, _s, _v) = (h, s, v);
            InvalidateVisual();
        }

        public override void Render(DrawingContext ctx)
        {
            var r = new Rect(Bounds.Size);
            ctx.FillRectangle(new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = new GradientStops { new(Checker.ToColor(Px.FromHsv(_h, _s, 1)), 0), new(Colors.Black, 1) },
            }, r);
            double y = (1 - _v) * r.Height;
            ctx.DrawRectangle(new Pen(Brushes.White, 1), new Rect(0.5, y - 2, r.Width - 1, 4));
            ctx.DrawRectangle(new Pen(Brushes.Black, 1), new Rect(-0.5, y - 3, r.Width + 1, 6));
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            e.Pointer.Capture(this);
            Pick(e.GetPosition(this));
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) Pick(e.GetPosition(this));
        }

        private void Pick(Point p) => Changed?.Invoke(1 - Math.Clamp(p.Y / Bounds.Height, 0, 1));
    }
}
