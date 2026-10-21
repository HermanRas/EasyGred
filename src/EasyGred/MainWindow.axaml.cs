using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using EasyGred.Controls;
using EasyGred.Core;
using EasyGred.Dialogs;

namespace EasyGred;

public partial class MainWindow : Window
{
    private const string AppName = "EasyGred";
    private const double CanvasMargin = 6;

    // Classic 2×14 desktop palette (plain RGB values).
    private readonly uint[] _palette =
    {
        0xFF000000, 0xFF808080, 0xFF800000, 0xFF808000, 0xFF008000, 0xFF008080, 0xFF000080,
        0xFF800080, 0xFF808040, 0xFF004040, 0xFF0080FF, 0xFF004080, 0xFF8000FF, 0xFF804000,
        0xFFFFFFFF, 0xFFC0C0C0, 0xFFFF0000, 0xFFFFFF00, 0xFF00FF00, 0xFF00FFFF, 0xFF0000FF,
        0xFFFF00FF, 0xFFFFFF80, 0xFF00FF80, 0xFF80FFFF, 0xFF8080FF, 0xFFFF0080, 0xFFFF8040,
    };

    private readonly Dictionary<Tool, ToggleButton> _toolButtons = new();
    private readonly Dictionary<ImageFormat, SaveOptions> _saveOptions = new();
    private Tool _toolBeforePicker = Tool.Pencil;
    private (int W, int H, uint[] Pixels)? _clipboard;
    private TextBox? _textEditor;
    private (int X, int Y) _textAt;
    private string _layerSignature = "";
    private bool _closeConfirmed;
    private readonly string? _startupFile;

    public MainWindow() : this(null) { }

    public MainWindow(string? startupFile)
    {
        _startupFile = startupFile;
        InitializeComponent();
        Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://EasyGred/Assets/icons/EasyGred-256x256.png")));

        BuildToolbox();
        BuildPalette();

        View.DocumentChanged += OnDocumentChanged;
        View.ColorsChanged += UpdateColors;
        View.ToolChanged += OnToolChanged;
        View.HoverChanged += (x, y) => PosText.Text = x >= 0 && View.Doc.InBounds(x, y) ? $"{x}, {y} px" : "";
        View.TextRequested += BeginText;
        View.ZoomRequested += (z, docPoint) => SetZoom(z, docPoint);
        View.PickedColor += () => View.Tool = _toolBeforePicker;

        PrimarySwatch.PointerPressed += async (_, _) => await EditColor(primary: true);
        SecondarySwatch.PointerPressed += async (_, _) => await EditColor(primary: false);
        TransparentSwatch.PointerPressed += (_, e) => PickSwatch(Px.Transparent, e);

        LayerList.SelectionChanged += (_, _) =>
        {
            if (LayerList.SelectedIndex >= 0) View.SelectLayer(View.Doc.Layers.Count - 1 - LayerList.SelectedIndex);
        };
        LayerName.LostFocus += (_, _) => RenameLayer();
        LayerName.KeyDown += (_, e) => { if (e.Key == Key.Enter) { RenameLayer(); View.Focus(); } };

        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        Scroller.AddHandler(PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, (_, e) =>
            e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None);
        AddHandler(DragDrop.DropEvent, OnDrop);
        Closing += OnClosing;
        Opened += async (_, _) =>
        {
            if (_startupFile != null && File.Exists(_startupFile)) await OpenPath(_startupFile);
        };

        OnToolChanged();
        UpdateColors();
        OnDocumentChanged();
    }

    // ======================= tool box & options =======================

    private void BuildToolbox()
    {
        foreach (Tool t in Enum.GetValues<Tool>())
        {
            var b = new ToggleButton { Classes = { "tool" }, Content = Icons.For(t) };
            ToolTip.SetTip(b, Icons.Tooltip(t));
            b.Click += (_, _) => SelectTool(t);
            _toolButtons[t] = b;
            ToolGrid.Children.Add(b);
        }
    }

    private void SelectTool(Tool t)
    {
        if (t == Tool.Picker && View.Tool != Tool.Picker) _toolBeforePicker = View.Tool;
        View.Tool = t;
        OnToolChanged();
    }

    private void OnToolChanged()
    {
        if (View.Tool != Tool.Text) CommitText();
        foreach (var (t, b) in _toolButtons) b.IsChecked = t == View.Tool;
        BuildOptions();
        HintText.Text = Hint(View.Tool);
    }

    private static string Hint(Tool t) => t switch
    {
        Tool.RectSelect or Tool.FreeSelect => "Drag to select. Drag inside the selection to move it. Del clears, Ctrl+C copies, Esc deselects.",
        Tool.Eraser => "Drag to erase to transparent.",
        Tool.Fill => "Click to fill an area. Left: primary color, right: secondary color.",
        Tool.Picker => "Click to pick a color. Left: primary, right: secondary.",
        Tool.Magnifier => "Left click zooms in, right click zooms out. Ctrl+wheel also zooms.",
        Tool.Pencil => "Draw single pixels. Left: primary color, right: secondary color.",
        Tool.Brush => "Paint with the brush. Left: primary color, right: secondary color.",
        Tool.Airbrush => "Hold to spray.",
        Tool.Text => "Click to place text. Ctrl+Enter or click elsewhere to apply, Esc to cancel.",
        Tool.Line => "Drag to draw a line. Hold Shift to snap to 45°.",
        Tool.Rectangle or Tool.Ellipse => "Drag to draw. Hold Shift for a square/circle. Fill uses the other color.",
        Tool.Replace => "Click a color to replace it everywhere (or inside the selection) with the primary color.",
        _ => "",
    };

    private void BuildOptions()
    {
        var p = OptionsPanel;
        p.Children.Clear();
        var s = View.Settings;

        void Caption(string text) => p.Children.Add(new TextBlock { Text = text, Classes = { "caption" } });
        void Note(string text) => p.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, FontSize = 11 });
        void Size(string label, int value, int max, Action<int> set)
        {
            p.Children.Add(new TextBlock { Text = label });
            var n = new NumericUpDown { Value = value, Minimum = 1, Maximum = max, Increment = 1, FormatString = "0", Width = 110 };
            n.ValueChanged += (_, _) => { if (n.Value is { } v) { set((int)v); View.InvalidateVisual(); } };
            p.Children.Add(n);
        }
        void Check(string label, bool value, Action<bool> set)
        {
            var c = new CheckBox { Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, IsChecked = value };
            c.IsCheckedChanged += (_, _) => set(c.IsChecked == true);
            p.Children.Add(c);
        }
        void Tolerance()
        {
            p.Children.Add(new TextBlock { Text = "Tolerance (0-255)" });
            var n = new NumericUpDown { Value = s.Tolerance, Minimum = 0, Maximum = 255, Increment = 4, FormatString = "0", Width = 110 };
            n.ValueChanged += (_, _) => { if (n.Value is { } v) s.Tolerance = (int)v; };
            p.Children.Add(n);
        }

        switch (View.Tool)
        {
            case Tool.Pencil:
                Caption("Pencil");
                Note("Always 1 pixel.");
                break;
            case Tool.Brush:
                Caption("Brush");
                Size("Size (px)", s.BrushSize, 128, v => s.BrushSize = v);
                Check("Round tip", s.RoundBrush, v => s.RoundBrush = v);
                break;
            case Tool.Eraser:
                Caption("Eraser");
                Size("Size (px)", s.EraserSize, 256, v => s.EraserSize = v);
                Note("Erases to transparent.");
                break;
            case Tool.Airbrush:
                Caption("Airbrush");
                Size("Radius (px)", s.AirbrushSize, 128, v => s.AirbrushSize = v);
                break;
            case Tool.Line:
                Caption("Line");
                Size("Width (px)", s.LineWidth, 64, v => s.LineWidth = v);
                break;
            case Tool.Rectangle:
            case Tool.Ellipse:
            {
                Caption(View.Tool == Tool.Rectangle ? "Rectangle" : "Ellipse");
                Size("Line width (px)", s.LineWidth, 64, v => s.LineWidth = v);
                foreach (var (mode, label) in new[] { (ShapeFill.Outline, "Outline"), (ShapeFill.OutlineAndFill, "Outline + fill"), (ShapeFill.Fill, "Filled") })
                {
                    var r = new RadioButton { Content = label, GroupName = "shapefill", IsChecked = s.ShapeFill == mode };
                    r.IsCheckedChanged += (_, _) => { if (r.IsChecked == true) s.ShapeFill = mode; };
                    p.Children.Add(r);
                }
                break;
            }
            case Tool.Fill:
                Caption("Fill");
                Tolerance();
                Check("Contiguous (off = every matching pixel)", s.Contiguous, v => s.Contiguous = v);
                break;
            case Tool.Replace:
                Caption("Color replace");
                Tolerance();
                Note("Ctrl+H opens the full Replace Color dialog.");
                break;
            case Tool.RectSelect:
            case Tool.FreeSelect:
            {
                Caption("Selection");
                Check("See-through when moving/pasting", s.TransparentPaste, v => s.TransparentPaste = v);
                var crop = new Button { Content = "Crop", HorizontalAlignment = HorizontalAlignment.Stretch };
                crop.Click += (_, _) => View.CropToSelection();
                p.Children.Add(crop);
                break;
            }
            case Tool.Text:
            {
                Caption("Text");
                var fonts = FontManager.Current.SystemFonts.Select(f => f.Name).Distinct().OrderBy(n => n).ToList();
                if (!fonts.Contains(s.FontFamily)) fonts.Insert(0, s.FontFamily);
                var family = new ComboBox { ItemsSource = fonts, SelectedItem = s.FontFamily, Width = 112, MaxDropDownHeight = 400 };
                family.SelectionChanged += (_, _) => { if (family.SelectedItem is string f) { s.FontFamily = f; StyleTextEditor(); } };
                p.Children.Add(family);
                Size("Size (px)", (int)s.FontSize, 400, v => { s.FontSize = v; StyleTextEditor(); });
                var styles = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
                ToggleButton Style(string text, bool value, Action<bool> set, FontWeight weight, FontStyle style)
                {
                    var t = new ToggleButton { Content = new TextBlock { Text = text, FontWeight = weight, FontStyle = style }, IsChecked = value, Classes = { "small" }, Width = 28 };
                    t.IsCheckedChanged += (_, _) => { set(t.IsChecked == true); StyleTextEditor(); };
                    return t;
                }
                styles.Children.Add(Style("B", s.Bold, v => s.Bold = v, FontWeight.Bold, FontStyle.Normal));
                styles.Children.Add(Style("I", s.Italic, v => s.Italic = v, FontWeight.Normal, FontStyle.Italic));
                styles.Children.Add(Style("U", s.Underline, v => s.Underline = v, FontWeight.Normal, FontStyle.Normal));
                p.Children.Add(styles);
                Check("Smooth edges", s.TextAntialias, v => s.TextAntialias = v);
                Check("Background (secondary color)", s.TextOpaque, v => s.TextOpaque = v);
                break;
            }
            case Tool.Picker:
                Caption("Pick color");
                Note("Returns to the previous tool after picking.");
                break;
            case Tool.Magnifier:
            {
                Caption("Zoom");
                var levels = new WrapPanel();
                foreach (var z in new[] { 1, 2, 4, 8, 16 })
                {
                    var b = new Button { Content = $"{z}x", Classes = { "small" }, Margin = new Thickness(1) };
                    b.Click += (_, _) => SetZoom(z);
                    levels.Children.Add(b);
                }
                p.Children.Add(levels);
                break;
            }
        }
    }

    // ======================= colours =======================

    private void BuildPalette()
    {
        for (int i = 0; i < _palette.Length; i++)
        {
            int index = i;
            var sw = new Swatch { Color = _palette[i], Width = 20, Height = 20, Cursor = new Cursor(StandardCursorType.Hand) };
            sw.PointerPressed += async (_, e) =>
            {
                if (e.ClickCount == 2 && e.GetCurrentPoint(sw).Properties.IsLeftButtonPressed)
                {
                    // Double-click edits this palette slot.
                    var c = await new ColorDialog(sw.Color, _palette).ShowFor(this);
                    if (c is not { } v) return;
                    _palette[index] = v;
                    sw.Color = v;
                    View.Primary = v;
                    return;
                }
                PickSwatch(sw.Color, e);
            };
            PaletteGrid.Children.Add(sw);
        }
    }

    private void PickSwatch(uint color, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed) View.Secondary = color;
        else View.Primary = color;
    }

    private void UpdateColors()
    {
        PrimarySwatch.Color = View.Primary;
        SecondarySwatch.Color = View.Secondary;
        PrimaryHex.Text = Px.ToHex(View.Primary, Px.A(View.Primary) != 255);
        SecondaryHex.Text = Px.ToHex(View.Secondary, Px.A(View.Secondary) != 255);
        StyleTextEditor();
    }

    private async Task EditColor(bool primary)
    {
        var c = await new ColorDialog(primary ? View.Primary : View.Secondary, _palette,
            primary ? "Edit Primary Color" : "Edit Secondary Color").ShowFor(this);
        if (c is not { } v) return;
        if (primary) View.Primary = v;
        else View.Secondary = v;
    }

    private async void OnEditPrimary(object? s, RoutedEventArgs e) => await EditColor(true);
    private async void OnEditSecondary(object? s, RoutedEventArgs e) => await EditColor(false);
    private void OnSwapColors(object? s, RoutedEventArgs e) => View.SwapColors();

    private void OnResetColors(object? s, RoutedEventArgs e)
    {
        View.Primary = Px.Black;
        View.Secondary = Px.White;
    }

    // ======================= document state =======================

    private void OnDocumentChanged()
    {
        var doc = View.Doc;
        Title = $"{(doc.FilePath != null ? Path.GetFileName(doc.FilePath) : "Untitled")}{(doc.Dirty ? " *" : "")} - {AppName}";
        SizeText.Text = $"{doc.Width} × {doc.Height}";
        SelText.Text = View.Selection is { } sel ? $"Sel {sel.W} × {sel.H}" : "";
        ZoomText.Text = $"{View.Zoom * 100:0.#}%";
        RefreshLayers();
    }

    private void RefreshLayers()
    {
        var doc = View.Doc;
        var sig = string.Join("|", doc.Layers.Select(l => $"{l.Name}:{l.Visible}")) + "#" + doc.ActiveIndex;
        if (sig == _layerSignature) return;
        _layerSignature = sig;

        var items = new List<Control>();
        for (int i = doc.Layers.Count - 1; i >= 0; i--)
        {
            int index = i;
            var layer = doc.Layers[i];
            var visible = new CheckBox { IsChecked = layer.Visible };
            ToolTip.SetTip(visible, "Visible");
            visible.IsCheckedChanged += (_, _) => View.SetLayerVisible(index, visible.IsChecked == true);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            row.Children.Add(visible);
            row.Children.Add(new TextBlock { Text = layer.Name, VerticalAlignment = VerticalAlignment.Center });
            items.Add(row);
        }
        LayerList.ItemsSource = items;
        LayerList.SelectedIndex = doc.Layers.Count - 1 - doc.ActiveIndex;
        LayerName.Text = doc.Active.Name;
    }

    private void RenameLayer()
    {
        var name = LayerName.Text?.Trim();
        if (string.IsNullOrEmpty(name) || name == View.Doc.Active.Name) return;
        View.Doc.Active.Name = name;
        View.Doc.Dirty = true;
        View.Refresh();
    }

    // ======================= files =======================

    private static readonly FilePickerFileType PngType = new("PNG image") { Patterns = new[] { "*.png" } };
    private static readonly FilePickerFileType BmpType = new("BMP image") { Patterns = new[] { "*.bmp" } };
    private static readonly FilePickerFileType JpgType = new("JPG image") { Patterns = new[] { "*.jpg", "*.jpeg" } };
    private static readonly FilePickerFileType WebpType = new("WebP image") { Patterns = new[] { "*.webp" } };

    private static string Extension(ImageFormat f) => f switch
    {
        ImageFormat.Bmp => ".bmp",
        ImageFormat.Jpeg => ".jpg",
        ImageFormat.Webp => ".webp",
        _ => ".png",
    };

    private async Task<bool> ConfirmDiscard()
    {
        CommitText();
        if (!View.Doc.Dirty) return true;
        var name = View.Doc.FilePath != null ? Path.GetFileName(View.Doc.FilePath) : "Untitled";
        var answer = await MessageDialog.Show(this, AppName, $"Save changes to {name}?", "Save", "Don't Save", "Cancel");
        return answer switch
        {
            "Save" => await Save(),
            "Don't Save" => true,
            _ => false,
        };
    }

    private async void OnNew(object? s, RoutedEventArgs e)
    {
        if (!await ConfirmDiscard()) return;
        var r = await new NewImageDialog(View.Doc.Width, View.Doc.Height, View.Secondary).ShowFor(this);
        if (r == null) return;
        View.LoadDocument(Document.CreateBlank(r.Width, r.Height, r.Background));
        FitAfterLoad();
    }

    private async void OnOpen(object? s, RoutedEventArgs e)
    {
        if (!await ConfirmDiscard()) return;
        var path = await PickOpenPath("Open Image");
        if (path != null) await OpenPath(path);
    }

    private async Task<string?> PickOpenPath(string title)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Images") { Patterns = ImageIO.OpenPatterns },
                PngType, BmpType, JpgType, WebpType,
                new FilePickerFileType("All files") { Patterns = new[] { "*" } },
            },
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    private async Task OpenPath(string path)
    {
        try
        {
            var (w, h, px) = await Task.Run(() => ImageIO.Load(path));
            var doc = Document.FromPixels(w, h, px);
            doc.FilePath = path;
            View.LoadDocument(doc);
            FitAfterLoad();
        }
        catch (Exception ex)
        {
            await MessageDialog.Show(this, "Could not open", $"{Path.GetFileName(path)}\n\n{ex.Message}");
        }
    }

    private async void OnSave(object? s, RoutedEventArgs e) => await Save();
    private async void OnSaveAs(object? s, RoutedEventArgs e) => await SaveAs(null, asCopy: false);
    private async void OnExportWebp(object? s, RoutedEventArgs e) => await SaveAs(ImageFormat.Webp, asCopy: true);

    private async Task<bool> Save()
    {
        var path = View.Doc.FilePath;
        if (path == null || ImageIO.FormatFromPath(path) == null) return await SaveAs(null, asCopy: false);
        return await SaveTo(path, askOptions: false, asCopy: false);
    }

    private async Task<bool> SaveAs(ImageFormat? format, bool asCopy)
    {
        var current = View.Doc.FilePath;
        format ??= current != null ? ImageIO.FormatFromPath(current) ?? ImageFormat.Png : ImageFormat.Png;
        var types = new List<FilePickerFileType> { PngType, BmpType, JpgType, WebpType };
        var preferred = format switch { ImageFormat.Bmp => BmpType, ImageFormat.Jpeg => JpgType, ImageFormat.Webp => WebpType, _ => PngType };
        types.Remove(preferred);
        types.Insert(0, preferred);

        var baseName = current != null ? Path.GetFileNameWithoutExtension(current) : "untitled";
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = asCopy ? "Export Copy" : "Save As",
            SuggestedFileName = baseName + Extension(format.Value),
            DefaultExtension = Extension(format.Value).TrimStart('.'),
            FileTypeChoices = types,
            ShowOverwritePrompt = true,
            SuggestedStartLocation = current != null ? await StorageProvider.TryGetFolderFromPathAsync(Path.GetDirectoryName(current)!) : null,
        });
        var path = file?.TryGetLocalPath();
        if (path == null) return false;
        if (ImageIO.FormatFromPath(path) == null) path += Extension(format.Value);
        return await SaveTo(path, askOptions: true, asCopy);
    }

    private async Task<bool> SaveTo(string path, bool askOptions, bool asCopy)
    {
        CommitText();
        View.CommitSelection();
        var format = ImageIO.FormatFromPath(path) ?? ImageFormat.Png;
        if (!_saveOptions.TryGetValue(format, out var options) || askOptions)
        {
            options = new SaveOptions();
            if (format is ImageFormat.Jpeg or ImageFormat.Webp)
            {
                var chosen = await new QualityDialog(format).ShowFor(this);
                if (chosen == null) return false;
                options = chosen;
            }
            _saveOptions[format] = options;
        }

        try
        {
            var doc = View.Doc;
            var pixels = doc.Flatten();
            await Task.Run(() => ImageIO.Save(path, doc.Width, doc.Height, pixels, format, options));
            if (!asCopy)
            {
                doc.FilePath = path;
                doc.Dirty = false;
            }
            OnDocumentChanged();
            HintText.Text = $"Saved {Path.GetFileName(path)}" + (doc.Layers.Count > 1 ? " (layers merged in the file)" : "");
            return true;
        }
        catch (Exception ex)
        {
            await MessageDialog.Show(this, "Could not save", $"{path}\n\n{ex.Message}");
            return false;
        }
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var path = e.Data.GetFiles()?.FirstOrDefault()?.TryGetLocalPath();
        if (path == null) return;
        if (await ConfirmDiscard()) await OpenPath(path);
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed || (!View.Doc.Dirty && _textEditor == null)) return;
        e.Cancel = true;
        if (await ConfirmDiscard())
        {
            _closeConfirmed = true;
            Close();
        }
    }

    private void OnExit(object? s, RoutedEventArgs e) => Close();

    // ======================= edit =======================

    private void OnUndo(object? s, RoutedEventArgs e)
    {
        CancelText();
        View.Undo();
    }

    private void OnRedo(object? s, RoutedEventArgs e)
    {
        CancelText();
        View.Redo();
    }

    private async void OnCopy(object? s, RoutedEventArgs e) => await Copy();

    private async void OnCut(object? s, RoutedEventArgs e)
    {
        if (View.Selection == null) return;
        await Copy();
        View.DeleteSelection();
    }

    private async Task Copy()
    {
        var (w, h, px) = View.CopySelection();
        _clipboard = (w, h, px);
        try
        {
            if (Clipboard == null) return;
            var png = ImageIO.Encode(w, h, px, ImageFormat.Png, new SaveOptions());
            var data = new DataObject();
            data.Set("image/png", png);
            data.Set("PNG", png);
            await Clipboard.SetDataObjectAsync(data);
        }
        catch
        {
            // System clipboard images are best-effort; the internal clipboard still works.
        }
        HintText.Text = $"Copied {w} × {h}";
    }

    private async void OnPaste(object? s, RoutedEventArgs e)
    {
        var image = await ReadSystemClipboard() ?? _clipboard;
        if (image is not { } img) return;
        PasteImage(img.W, img.H, (uint[])img.Pixels.Clone());
    }

    private async Task<(int W, int H, uint[] Pixels)?> ReadSystemClipboard()
    {
        try
        {
            if (Clipboard == null) return null;
            var formats = await Clipboard.GetFormatsAsync();
            foreach (var f in new[] { "image/png", "PNG" })
            {
                if (!formats.Contains(f)) continue;
                if (await Clipboard.GetDataAsync(f) is byte[] bytes && bytes.Length > 0)
                    return ImageIO.Load(new MemoryStream(bytes));
            }
        }
        catch
        {
            // fall back to the internal clipboard
        }
        return null;
    }

    private async void OnPasteFromFile(object? s, RoutedEventArgs e)
    {
        var path = await PickOpenPath("Paste From File");
        if (path == null) return;
        try
        {
            var (w, h, px) = await Task.Run(() => ImageIO.Load(path));
            PasteImage(w, h, px);
        }
        catch (Exception ex)
        {
            await MessageDialog.Show(this, "Could not open", ex.Message);
        }
    }

    private void PasteImage(int w, int h, uint[] px)
    {
        CommitText();
        var (vx, vy) = VisibleTopLeft();
        View.Paste(w, h, px, vx, vy);
        OnToolChanged();
    }

    private (int X, int Y) VisibleTopLeft() =>
        ((int)Math.Max(0, (Scroller.Offset.X - CanvasMargin) / View.Zoom), (int)Math.Max(0, (Scroller.Offset.Y - CanvasMargin) / View.Zoom));

    private void OnDelete(object? s, RoutedEventArgs e) => View.DeleteSelection();

    private void OnSelectAll(object? s, RoutedEventArgs e)
    {
        CommitText();
        View.SelectAll();
        OnToolChanged();
    }

    private void OnDeselect(object? s, RoutedEventArgs e) => View.CommitSelection();

    // ======================= view / zoom =======================

    private void SetZoom(double zoom, Point? docAnchor = null)
    {
        var old = View.Zoom;
        var viewport = Scroller.Viewport;
        var anchor = docAnchor ?? new Point(
            (Scroller.Offset.X + viewport.Width / 2 - CanvasMargin) / old,
            (Scroller.Offset.Y + viewport.Height / 2 - CanvasMargin) / old);
        View.Zoom = zoom;
        Scroller.UpdateLayout();
        Scroller.Offset = new Vector(
            Math.Max(0, anchor.X * View.Zoom + CanvasMargin - viewport.Width / 2),
            Math.Max(0, anchor.Y * View.Zoom + CanvasMargin - viewport.Height / 2));
        ZoomText.Text = $"{View.Zoom * 100:0.#}%";
        StyleTextEditor();
    }

    private void StepZoom(int direction, Point? anchor = null)
    {
        var levels = CanvasView.ZoomLevels;
        int i = Array.FindIndex(levels, l => l >= View.Zoom - 1e-9);
        if (i < 0) i = levels.Length - 1;
        SetZoom(levels[Math.Clamp(i + direction, 0, levels.Length - 1)], anchor);
    }

    private void OnZoomIn(object? s, RoutedEventArgs e) => StepZoom(1);
    private void OnZoomOut(object? s, RoutedEventArgs e) => StepZoom(-1);
    private void OnZoomActual(object? s, RoutedEventArgs e) => SetZoom(1);
    private void OnZoomFit(object? s, RoutedEventArgs e) => SetZoom(FitZoom(allowEnlarge: true));

    private double FitZoom(bool allowEnlarge)
    {
        var vp = Scroller.Viewport;
        if (vp.Width < 10 || vp.Height < 10) return 1;
        double fit = Math.Min((vp.Width - CanvasMargin * 2) / View.Doc.Width, (vp.Height - CanvasMargin * 2) / View.Doc.Height);
        if (!allowEnlarge) fit = Math.Min(fit, 1);
        return CanvasView.ZoomLevels.LastOrDefault(l => l <= fit, CanvasView.ZoomLevels[0]);
    }

    /// <summary>Opens at 100%, or zoomed out just enough when the image is bigger than the window.</summary>
    private void FitAfterLoad() => Dispatcher.UIThread.Post(() =>
    {
        View.Zoom = FitZoom(allowEnlarge: false);
        Scroller.Offset = default;
        OnDocumentChanged();
    }, DispatcherPriority.Loaded);

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        e.Handled = true;
        var p = e.GetPosition(View);
        StepZoom(e.Delta.Y > 0 ? 1 : -1, new Point(p.X / View.Zoom, p.Y / View.Zoom));
    }

    private void OnToggleGrid(object? s, RoutedEventArgs e)
    {
        View.ShowGrid = !View.ShowGrid;
        GridMenu.IsChecked = View.ShowGrid;
        if (View.ShowGrid && View.Zoom < 4) HintText.Text = "The pixel grid shows from 400% zoom.";
    }

    private void OnToggleLayers(object? s, RoutedEventArgs e)
    {
        LayersPanel.IsVisible = !LayersPanel.IsVisible;
        LayersMenu.IsChecked = LayersPanel.IsVisible;
        _layerSignature = "";
        RefreshLayers();
    }

    // ======================= image =======================

    private void DoTransform(ImageTransform t)
    {
        CommitText();
        View.ApplyTransform(t);
    }

    private void OnFlipH(object? s, RoutedEventArgs e) => DoTransform(ImageTransform.FlipH);
    private void OnFlipV(object? s, RoutedEventArgs e) => DoTransform(ImageTransform.FlipV);
    private void OnRotateRight(object? s, RoutedEventArgs e) => DoTransform(ImageTransform.RotateRight);
    private void OnRotateLeft(object? s, RoutedEventArgs e) => DoTransform(ImageTransform.RotateLeft);
    private void OnRotate180(object? s, RoutedEventArgs e) => DoTransform(ImageTransform.Rotate180);

    private void OnCrop(object? s, RoutedEventArgs e)
    {
        if (View.Selection == null)
        {
            HintText.Text = "Select an area first, then crop.";
            return;
        }
        View.CropToSelection();
    }

    private async void OnResize(object? s, RoutedEventArgs e)
    {
        CommitText();
        var r = await new ResizeDialog(View.Doc.Width, View.Doc.Height).ShowFor(this);
        if (r == null || (r.Width == View.Doc.Width && r.Height == View.Doc.Height)) return;
        if (r.Scale) View.ScaleImage(r.Width, r.Height, r.Smooth);
        else View.ResizeCanvas(r.Width, r.Height, r.Centre);
    }

    private async void OnReplaceColor(object? s, RoutedEventArgs e) =>
        await ReplaceColor(View.Primary, View.Secondary, "Replace Color");

    private async void OnMakeTransparent(object? s, RoutedEventArgs e) =>
        await ReplaceColor(View.Primary, Px.Transparent, "Make Color Transparent");

    private async Task ReplaceColor(uint from, uint to, string title)
    {
        CommitText();
        var r = await new ReplaceColorDialog(from, to, View.Primary, View.Secondary, _palette, View.Selection != null, title).ShowFor(this);
        if (r == null) return;
        int n = View.ReplaceColor(r.From, r.To, r.Tolerance, r.AllLayers);
        HintText.Text = n == 0 ? "No matching pixels found." : $"Replaced {n:N0} pixels.";
    }

    private void OnClearImage(object? s, RoutedEventArgs e)
    {
        CommitText();
        View.ClearImage();
    }

    // ======================= layers =======================

    private void OnLayerAdd(object? s, RoutedEventArgs e)
    {
        View.LayerOp(d => d.AddLayer($"Layer {d.Layers.Count + 1}"));
        if (!LayersPanel.IsVisible) OnToggleLayers(s, e);
    }

    private void OnLayerDuplicate(object? s, RoutedEventArgs e) => View.LayerOp(d => d.DuplicateLayer());
    private void OnLayerDelete(object? s, RoutedEventArgs e) => View.LayerOp(d => d.DeleteLayer());
    private void OnLayerUp(object? s, RoutedEventArgs e) => View.LayerOp(d => d.MoveLayer(1));
    private void OnLayerDown(object? s, RoutedEventArgs e) => View.LayerOp(d => d.MoveLayer(-1));
    private void OnLayerMerge(object? s, RoutedEventArgs e) => View.LayerOp(d => d.MergeDown());
    private void OnLayerFlatten(object? s, RoutedEventArgs e) => View.LayerOp(d => d.FlattenLayers());

    // ======================= text tool =======================

    private void BeginText(int x, int y)
    {
        CommitText();
        _textAt = (x, y);
        _textEditor = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            Padding = new Thickness(0),
            MinWidth = 24,
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.DodgerBlue,
            Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            CaretBrush = Brushes.Black,
        };
        // Tunnel so Ctrl+Enter / Esc are seen before the TextBox turns Enter into a newline.
        _textEditor.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape) { CancelText(); e.Handled = true; }
            else if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { CommitText(); e.Handled = true; }
        }, RoutingStrategies.Tunnel);
        Overlay.Children.Add(_textEditor);
        StyleTextEditor();
        Dispatcher.UIThread.Post(() => _textEditor?.Focus(), DispatcherPriority.Input);
    }

    private void StyleTextEditor()
    {
        if (_textEditor == null) return;
        var s = View.Settings;
        double z = View.Zoom;
        _textEditor.FontFamily = new FontFamily(s.FontFamily);
        _textEditor.FontSize = Math.Max(4, s.FontSize * z);
        _textEditor.FontWeight = s.Bold ? FontWeight.Bold : FontWeight.Normal;
        _textEditor.FontStyle = s.Italic ? FontStyle.Italic : FontStyle.Normal;
        _textEditor.Foreground = new SolidColorBrush(Checker.ToColor(Px.WithAlpha(View.Primary, Math.Max((int)Px.A(View.Primary), 160))));
        Canvas.SetLeft(_textEditor, _textAt.X * z - 1);
        Canvas.SetTop(_textEditor, _textAt.Y * z - 1);
    }

    private void CommitText()
    {
        if (_textEditor == null) return;
        var text = _textEditor.Text ?? "";
        var at = _textAt;
        RemoveTextEditor();
        View.CommitText(at.X, at.Y, text);
    }

    private void CancelText() => RemoveTextEditor();

    private void RemoveTextEditor()
    {
        if (_textEditor == null) return;
        Overlay.Children.Remove(_textEditor);
        _textEditor = null;
        View.Focus();
    }

    // ======================= keyboard =======================

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        // Let text boxes (text tool, layer name, option fields) handle their own keys.
        if (FocusManager?.GetFocusedElement() is TextBox) return;

        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var none = new RoutedEventArgs();
        Action? action = (ctrl, shift, e.Key) switch
        {
            (true, false, Key.N) => () => OnNew(this, none),
            (true, false, Key.O) => () => OnOpen(this, none),
            (true, false, Key.S) => () => OnSave(this, none),
            (true, true, Key.S) => () => OnSaveAs(this, none),
            (true, false, Key.Z) => () => OnUndo(this, none),
            (true, true, Key.Z) or (true, false, Key.Y) => () => OnRedo(this, none),
            (true, false, Key.X) => () => OnCut(this, none),
            (true, false, Key.C) => () => OnCopy(this, none),
            (true, false, Key.V) => () => OnPaste(this, none),
            (true, false, Key.A) => () => OnSelectAll(this, none),
            (true, false, Key.G) => () => OnToggleGrid(this, none),
            (true, false, Key.L) => () => OnToggleLayers(this, none),
            (true, true, Key.L) => () => OnLayerAdd(this, none),
            (true, false, Key.E) => () => OnResize(this, none),
            (true, false, Key.H) => () => OnReplaceColor(this, none),
            (true, false, Key.T) => () => OnMakeTransparent(this, none),
            (true, false, Key.R) => () => OnRotateRight(this, none),
            (true, true, Key.R) => () => OnRotateLeft(this, none),
            (true, true, Key.X) => () => OnCrop(this, none),
            (true, true, Key.N) => () => OnClearImage(this, none),
            (true, _, Key.OemPlus or Key.Add) => () => StepZoom(1),
            (true, _, Key.OemMinus or Key.Subtract) => () => StepZoom(-1),
            (true, _, Key.D0 or Key.NumPad0) => () => SetZoom(1),
            (true, _, Key.D9 or Key.NumPad9) => () => OnZoomFit(this, none),
            (false, _, Key.Delete) => () => View.DeleteSelection(),
            (false, _, Key.Escape) => () => View.CommitSelection(),
            (false, _, Key.F1) => () => OnShortcuts(this, none),
            (false, false, Key.X) => () => View.SwapColors(),
            (false, false, Key.D) => () => OnResetColors(this, none),
            (false, false, Key.S) => () => SelectTool(View.Tool == Tool.RectSelect ? Tool.FreeSelect : Tool.RectSelect),
            (false, false, Key.E) => () => SelectTool(Tool.Eraser),
            (false, false, Key.F) => () => SelectTool(Tool.Fill),
            (false, false, Key.I) => () => SelectTool(Tool.Picker),
            (false, false, Key.M) => () => SelectTool(Tool.Magnifier),
            (false, false, Key.P) => () => SelectTool(Tool.Pencil),
            (false, false, Key.B) => () => SelectTool(Tool.Brush),
            (false, false, Key.A) => () => SelectTool(Tool.Airbrush),
            (false, false, Key.T) => () => SelectTool(Tool.Text),
            (false, false, Key.L) => () => SelectTool(Tool.Line),
            (false, false, Key.R) => () => SelectTool(Tool.Rectangle),
            (false, false, Key.O) => () => SelectTool(Tool.Ellipse),
            (false, false, Key.C) => () => SelectTool(Tool.Replace),
            _ => null,
        };
        if (action == null) return;
        e.Handled = true;
        action();
    }

    // ======================= help =======================

    private async void OnShortcuts(object? s, RoutedEventArgs e) => await MessageDialog.Show(this, "Keyboard Shortcuts",
        "Tools:  S select (again = free-form)   P pencil   B brush   E eraser   F fill   I pick color\n" +
        "            T text   L line   R rectangle   O ellipse   A airbrush   C color replace   M magnifier\n\n" +
        "Colors:  X swap primary/secondary   D reset to black/white\n" +
        "             Left click a swatch = primary, right click = secondary, double-click = edit\n\n" +
        "File:  Ctrl+N new   Ctrl+O open   Ctrl+S save   Ctrl+Shift+S save as\n" +
        "Edit:  Ctrl+Z undo   Ctrl+Y redo   Ctrl+X/C/V cut/copy/paste   Ctrl+A select all\n" +
        "          Del clear selection   Esc deselect\n" +
        "Image:  Ctrl+R / Ctrl+Shift+R rotate   Ctrl+Shift+X crop   Ctrl+E image size\n" +
        "            Ctrl+H replace color   Ctrl+T make color transparent\n" +
        "View:  Ctrl+wheel or Ctrl +/- zoom   Ctrl+0 100%   Ctrl+9 fit   Ctrl+G grid   Ctrl+L layers");

    private async void OnAbout(object? s, RoutedEventArgs e) => await MessageDialog.Show(this, $"About {AppName}",
        $"{AppName} - Easy Graphix Edit  v{typeof(MainWindow).Assembly.GetName().Version?.ToString(3)}\n\n" +
        "A small, portable picture editor for Windows and Linux.\n" +
        "Opens and saves BMP, PNG, JPG and WebP.");
}
