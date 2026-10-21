using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace EasyGred.Dialogs;

/// <summary>Fixed-size modal dialog with OK/Cancel handling via Enter/Escape.</summary>
public abstract class DialogBase<T> : Window
{
    protected T? Result { get; set; }

    protected DialogBase(string title)
    {
        Title = title;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Close(default(T));
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    public Task<T?> ShowFor(Window owner) => ShowDialog<T?>(owner);

    protected void Accept(T value) => Close(value);

    protected static StackPanel ButtonRow(params (string Text, Action Click, bool IsDefault)[] buttons)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 6,
            Margin = new Thickness(0, 10, 0, 0),
        };
        foreach (var (text, click, isDefault) in buttons)
        {
            var b = new Button { Content = text, MinWidth = 75, IsDefault = isDefault };
            b.Click += (_, _) => click();
            row.Children.Add(b);
        }
        return row;
    }

    protected static TextBlock Label(string text, double width = 0) =>
        new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Width = width > 0 ? width : double.NaN };

    protected static NumericUpDown Number(double value, double min, double max, double width = 90) => new()
    {
        Value = (decimal)value,
        Minimum = (decimal)min,
        Maximum = (decimal)max,
        Increment = 1,
        FormatString = "0",
        Width = width,
        ParsingNumberStyle = System.Globalization.NumberStyles.Integer,
    };
}

public sealed class MessageDialog : DialogBase<string>
{
    private MessageDialog(string title, string message, string[] buttons) : base(title)
    {
        var panel = new StackPanel { Margin = new Thickness(16), MaxWidth = 460 };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(ButtonRow(buttons.Select((b, i) => (b, (Action)(() => Accept(b)), i == 0)).ToArray()));
        Content = panel;
    }

    /// <summary>Shows a message and returns the clicked button text (null if closed).</summary>
    public static Task<string?> Show(Window owner, string title, string message, params string[] buttons) =>
        new MessageDialog(title, message, buttons.Length == 0 ? new[] { "OK" } : buttons).ShowFor(owner);
}
