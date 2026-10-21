using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using EasyGred.Controls;
using EasyGred.Core;

namespace EasyGred.Dialogs;

public sealed record NewImageResult(int Width, int Height, uint Background);

public sealed class NewImageDialog : DialogBase<NewImageResult>
{
    public NewImageDialog(int width, int height, uint secondary) : base("New Image")
    {
        var w = Number(width, 1, 16384);
        var h = Number(height, 1, 16384);
        var white = new RadioButton { Content = "White", IsChecked = true, GroupName = "bg" };
        var transparent = new RadioButton { Content = "Transparent", GroupName = "bg" };
        var secondaryBg = new RadioButton { Content = "Secondary color", GroupName = "bg" };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("80,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto") };
        AddRow(grid, 0, "Width (px):", w);
        AddRow(grid, 1, "Height (px):", h);

        var bg = new StackPanel { Spacing = 2, Margin = new Thickness(0, 8, 0, 0) };
        bg.Children.Add(new TextBlock { Text = "Background:" });
        bg.Children.Add(white);
        bg.Children.Add(transparent);
        bg.Children.Add(secondaryBg);

        var root = new StackPanel { Margin = new Thickness(14), Width = 260 };
        root.Children.Add(grid);
        root.Children.Add(bg);
        root.Children.Add(ButtonRow(
            ("OK", () => Accept(new NewImageResult((int)(w.Value ?? 1), (int)(h.Value ?? 1),
                transparent.IsChecked == true ? Px.Transparent : secondaryBg.IsChecked == true ? secondary : Px.White)), true),
            ("Cancel", () => Close(null), false)));
        Content = root;
    }

    internal static void AddRow(Grid grid, int row, string label, Control control)
    {
        var l = Label(label);
        control.Margin = new Thickness(0, 2);
        Grid.SetRow(l, row);
        Grid.SetRow(control, row);
        Grid.SetColumn(control, 1);
        grid.Children.Add(l);
        grid.Children.Add(control);
    }
}

public sealed record ResizeResult(int Width, int Height, bool Scale, bool Smooth, bool Centre);

/// <summary>Resize the canvas (crop/extend) or scale the picture.</summary>
public sealed class ResizeDialog : DialogBase<ResizeResult>
{
    private bool _syncing;

    public ResizeDialog(int width, int height) : base("Image Size")
    {
        var canvasMode = new RadioButton { Content = "Resize canvas (keep pixels, add/cut edges)", IsChecked = true, GroupName = "mode" };
        var scaleMode = new RadioButton { Content = "Scale picture", GroupName = "mode" };
        var w = Number(width, 1, 16384);
        var h = Number(height, 1, 16384);
        var percent = Number(100, 1, 3200);
        var keepAspect = new CheckBox { Content = "Keep aspect ratio", IsChecked = true };
        var smooth = new CheckBox { Content = "Smooth (off = crisp pixels)" };
        var centre = new CheckBox { Content = "Anchor in the centre (off = top-left)" };

        void FromPercent()
        {
            if (_syncing || percent.Value is not { } p) return;
            _syncing = true;
            w.Value = Math.Max(1, Math.Round(width * p / 100));
            h.Value = Math.Max(1, Math.Round(height * p / 100));
            _syncing = false;
        }
        void Linked(NumericUpDown changed, NumericUpDown other, int from, int to)
        {
            if (_syncing || keepAspect.IsChecked != true || changed.Value is not { } v) return;
            _syncing = true;
            other.Value = Math.Max(1, Math.Round(v * to / from));
            percent.Value = Math.Round(v * 100 / from);
            _syncing = false;
        }
        percent.ValueChanged += (_, _) => FromPercent();
        w.ValueChanged += (_, _) => Linked(w, h, width, height);
        h.ValueChanged += (_, _) => Linked(h, w, height, width);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("80,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto"), Margin = new Thickness(0, 8) };
        NewImageDialog.AddRow(grid, 0, "Width (px):", w);
        NewImageDialog.AddRow(grid, 1, "Height (px):", h);
        NewImageDialog.AddRow(grid, 2, "Percent:", percent);

        var root = new StackPanel { Margin = new Thickness(14), Spacing = 3, Width = 320 };
        root.Children.Add(new TextBlock { Text = $"Current size: {width} × {height} px" });
        root.Children.Add(canvasMode);
        root.Children.Add(scaleMode);
        root.Children.Add(grid);
        root.Children.Add(keepAspect);
        root.Children.Add(smooth);
        root.Children.Add(centre);
        root.Children.Add(new TextBlock { Text = "New canvas area uses the secondary color.", Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap });
        root.Children.Add(ButtonRow(
            ("OK", () => Accept(new ResizeResult((int)(w.Value ?? width), (int)(h.Value ?? height), scaleMode.IsChecked == true,
                smooth.IsChecked == true, centre.IsChecked == true)), true),
            ("Cancel", () => Close(null), false)));
        Content = root;
    }
}

public sealed record ReplaceResult(uint From, uint To, int Tolerance, bool AllLayers);

/// <summary>Bulk colour replace. With To set to transparent this doubles as "make colour transparent".</summary>
public sealed class ReplaceColorDialog : DialogBase<ReplaceResult>
{
    public ReplaceColorDialog(uint from, uint to, uint primary, uint secondary, IReadOnlyList<uint> palette, bool hasSelection, string title)
        : base(title)
    {
        var fromSw = new Swatch { Color = from, Width = 48, Height = 26, Cursor = new Cursor(StandardCursorType.Hand) };
        var toSw = new Swatch { Color = to, Width = 48, Height = 26, Cursor = new Cursor(StandardCursorType.Hand) };
        var fromHex = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Width = 90 };
        var toHex = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Width = 90 };
        void Sync()
        {
            fromHex.Text = Px.ToHex(fromSw.Color, true);
            toHex.Text = Px.ToHex(toSw.Color, true);
        }
        async void Edit(Swatch sw)
        {
            var c = await new ColorDialog(sw.Color, palette).ShowFor(this);
            if (c is { } v) { sw.Color = v; Sync(); }
        }
        fromSw.PointerPressed += (_, _) => Edit(fromSw);
        toSw.PointerPressed += (_, _) => Edit(toSw);

        Button Small(string text, Action a)
        {
            var b = new Button { Content = text, Classes = { "small" } };
            b.Click += (_, _) => { a(); Sync(); };
            return b;
        }

        var fromRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        fromRow.Children.Add(Label("Find:", 60));
        fromRow.Children.Add(fromSw);
        fromRow.Children.Add(fromHex);
        fromRow.Children.Add(Small("Primary", () => fromSw.Color = primary));
        fromRow.Children.Add(Small("Secondary", () => fromSw.Color = secondary));

        var toRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        toRow.Children.Add(Label("Replace:", 60));
        toRow.Children.Add(toSw);
        toRow.Children.Add(toHex);
        toRow.Children.Add(Small("Primary", () => toSw.Color = primary));
        toRow.Children.Add(Small("Transparent", () => toSw.Color = Px.Transparent));

        var tol = Number(0, 0, 255, 100);
        var tolRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        tolRow.Children.Add(Label("Tolerance:", 60));
        tolRow.Children.Add(tol);
        tolRow.Children.Add(new TextBlock { Text = "0 = exact color only", Foreground = Brushes.DimGray, VerticalAlignment = VerticalAlignment.Center });

        var allLayers = new CheckBox { Content = "All layers (otherwise the active layer)" };

        var root = new StackPanel { Margin = new Thickness(14), Spacing = 6 };
        root.Children.Add(fromRow);
        root.Children.Add(toRow);
        root.Children.Add(tolRow);
        root.Children.Add(allLayers);
        root.Children.Add(new TextBlock
        {
            Text = hasSelection ? "Only pixels inside the selection are changed." : "The whole image is changed. Select an area first to limit it.",
            Foreground = Brushes.DimGray,
        });
        root.Children.Add(ButtonRow(
            ("Replace", () => Accept(new ReplaceResult(fromSw.Color, toSw.Color, (int)(tol.Value ?? 0), allLayers.IsChecked == true)), true),
            ("Cancel", () => Close(null), false)));
        Content = root;
        Sync();
    }
}

public sealed class QualityDialog : DialogBase<SaveOptions>
{
    public QualityDialog(ImageFormat format) : base(format == ImageFormat.Webp ? "WebP Options" : "JPG Options")
    {
        var quality = new Slider { Minimum = 1, Maximum = 100, Value = 90, Width = 200 };
        var value = new TextBlock { Text = "90", Width = 30, VerticalAlignment = VerticalAlignment.Center };
        quality.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty) value.Text = ((int)quality.Value).ToString();
        };
        var lossless = new CheckBox { Content = "Lossless (exact pixels, keeps transparency perfectly)", IsChecked = true, IsVisible = format == ImageFormat.Webp };
        var qRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        qRow.Children.Add(Label("Quality:", 60));
        qRow.Children.Add(quality);
        qRow.Children.Add(value);
        lossless.IsCheckedChanged += (_, _) => qRow.IsEnabled = lossless.IsChecked != true;
        qRow.IsEnabled = format != ImageFormat.Webp;

        var root = new StackPanel { Margin = new Thickness(14), Spacing = 8 };
        root.Children.Add(lossless);
        root.Children.Add(qRow);
        if (format == ImageFormat.Jpeg)
            root.Children.Add(new TextBlock { Text = "JPG has no transparency - transparent areas become white.", Foreground = Brushes.DimGray });
        root.Children.Add(ButtonRow(
            ("Save", () => Accept(new SaveOptions((int)quality.Value, format == ImageFormat.Webp && lossless.IsChecked == true)), true),
            ("Cancel", () => Close(null), false)));
        Content = root;
    }
}
