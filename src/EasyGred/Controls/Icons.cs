using Avalonia.Controls;
using Avalonia.Media;
using Shape = Avalonia.Controls.Shapes.Path;

namespace EasyGred.Controls;

public enum Tool
{
    FreeSelect, RectSelect,
    Eraser, Fill,
    Picker, Magnifier,
    Pencil, Brush,
    Airbrush, Text,
    Line, Rectangle,
    Ellipse, Replace,
}

/// <summary>Original 16×16 vector icons for the tool box.</summary>
public static class Icons
{
    public static string Tooltip(Tool t) => t switch
    {
        Tool.FreeSelect => "Free-form select (S)",
        Tool.RectSelect => "Select (S)",
        Tool.Eraser => "Eraser - paints transparent (E)",
        Tool.Fill => "Fill with color (F)",
        Tool.Picker => "Pick color (I)",
        Tool.Magnifier => "Magnifier (M)",
        Tool.Pencil => "Pencil (P)",
        Tool.Brush => "Brush (B)",
        Tool.Airbrush => "Airbrush (A)",
        Tool.Text => "Text (T)",
        Tool.Line => "Line (L)",
        Tool.Rectangle => "Rectangle (R)",
        Tool.Ellipse => "Ellipse (O)",
        Tool.Replace => "Color replace - click a color to swap it everywhere (C)",
        _ => t.ToString(),
    };

    public static Control For(Tool t)
    {
        var c = new Canvas { Width = 16, Height = 16 };
        void P(string data, IBrush? fill, IBrush? stroke, double thickness = 1, double[]? dash = null) =>
            c.Children.Add(new Shape
            {
                Data = Geometry.Parse(data),
                Fill = fill,
                Stroke = stroke,
                StrokeThickness = thickness,
                StrokeDashArray = dash == null ? null : new Avalonia.Collections.AvaloniaList<double>(dash),
                StrokeLineCap = PenLineCap.Square,
            });

        var k = Brushes.Black;
        var gray = new SolidColorBrush(Color.Parse("#808080"));
        switch (t)
        {
            case Tool.FreeSelect:
                P("M3,9 C2,4 8,1.5 12,3.5 C15.5,5.5 13.5,10.5 9,11.5 C6,12.5 7,14.5 4.5,14", null, k, 1, new double[] { 2, 2 });
                break;
            case Tool.RectSelect:
                P("M2.5,3.5 H13.5 V12.5 H2.5 Z", null, k, 1, new double[] { 2, 2 });
                break;
            case Tool.Eraser:
                P("M2,11 L8,5 L13,10 L9,14 L5,14 Z", new SolidColorBrush(Color.Parse("#F4A6C0")), k);
                P("M5,8 L10,13", null, k);
                break;
            case Tool.Fill:
                P("M3,8 L8,3 L13,8 L8,13 Z", Brushes.White, k);
                P("M3,8 L13,8 L8,13 Z", new SolidColorBrush(Color.Parse("#2060D0")), null);
                P("M13.5,9 C15,11.5 15,13 13.5,13.5 C12,13 12,11.5 13.5,9 Z", new SolidColorBrush(Color.Parse("#2060D0")), null);
                break;
            case Tool.Picker:
                P("M10.5,1.5 L14.5,5.5 L12.5,7.5 L8.5,3.5 Z", k, k);
                P("M9.5,4.5 L3,11 L2.5,13.5 L5,13 L11.5,6.5", Brushes.White, k);
                break;
            case Tool.Magnifier:
                P("M6.5,2 A4.5,4.5 0 1 1 6.49,2 Z", new SolidColorBrush(Color.Parse("#C8E0FF")), k);
                P("M10,10 L14.5,14.5", null, k, 2.5);
                break;
            case Tool.Pencil:
                P("M2.5,13.5 L3.5,10.5 L11,3 L13,5 L5.5,12.5 Z", new SolidColorBrush(Color.Parse("#F2C230")), k);
                P("M2.5,13.5 L3.5,10.5 L5.5,12.5 Z", k, k);
                break;
            case Tool.Brush:
                P("M14,2 L8.5,8", null, new SolidColorBrush(Color.Parse("#8B5A2B")), 2);
                P("M8.5,7.5 C5.5,7.5 4,10 2.5,14 C6,13 9,11.5 8.5,7.5 Z", k, k);
                break;
            case Tool.Airbrush:
                P("M8,6 H13 V14 H8 Z", gray, k);
                P("M9.5,3.5 H11.5 V6 H9.5 Z", k, null);
                P("M2,3 h1 v1 h-1z M4,5 h1 v1 h-1z M2,7 h1 v1 h-1z M5,2 h1 v1 h-1z M3,10 h1 v1 h-1z M6,8 h1 v1 h-1z", k, null);
                break;
            case Tool.Text:
                c.Children.Add(new TextBlock { Text = "A", FontFamily = new FontFamily("serif"), FontWeight = FontWeight.Bold, FontSize = 15, Width = 16, TextAlignment = TextAlignment.Center, Foreground = k, Margin = new Avalonia.Thickness(0, -2, 0, 0) });
                break;
            case Tool.Line:
                P("M2.5,13.5 L13.5,2.5", null, k, 1.5);
                break;
            case Tool.Rectangle:
                P("M2.5,4.5 H13.5 V11.5 H2.5 Z", null, k);
                break;
            case Tool.Ellipse:
                P("M8,4 A6,4 0 1 1 7.99,4 Z", null, k);
                break;
            case Tool.Replace:
                P("M1.5,1.5 H7.5 V7.5 H1.5 Z", new SolidColorBrush(Color.Parse("#D03030")), k);
                P("M8.5,8.5 H14.5 V14.5 H8.5 Z", new SolidColorBrush(Color.Parse("#30A040")), k);
                P("M4,10 Q4,13 7,13 M5.5,11.5 L7,13 L5.5,14.5", null, k);
                break;
        }
        return c;
    }
}
