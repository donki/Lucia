using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SocAiChat.Localization;
using SocAiChat.Services;

namespace SocAiChat;

/// <summary>Los dialogos pequeños —una linea de texto, una confirmacion y un aviso— con el aspecto de la aplicacion (constitucion 6.2).</summary>
public sealed class PromptWindow : Window
{
    private readonly TextBox? _text;

    private PromptWindow(Window owner, string title, string message, string? initial, bool confirm, bool alert)
    {
        Owner = owner;
        Title = title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (System.Windows.Media.Brush)FindResource("PageBackground");
        SourceInitialized += (_, _) => ThemeManager.ApplyToWindow(this);

        var card = new Border { Style = (Style)FindResource("Card"), Padding = new Thickness(18, 14, 18, 16) };
        var stack = new StackPanel();
        card.Child = stack;
        stack.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = (System.Windows.Media.Brush)FindResource("TextPrimary") });
        if (!confirm && !alert)
        {
            _text = new TextBox { Style = (Style)FindResource("Field"), Text = initial ?? string.Empty, Margin = new Thickness(0, 10, 0, 0) };
            stack.Children.Add(_text);
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        if (!alert)
        {
            var cancel = new Button { Style = (Style)FindResource("GhostIconButton"), Content = "", ToolTip = Loc.Get("Cancel") };
            cancel.Click += (_, _) => { DialogResult = false; Close(); };
            buttons.Children.Add(cancel);
        }
        var ok = new Button { Style = (Style)FindResource(confirm ? "DangerIconButton" : "IconButton"), Content = confirm ? "" : "", ToolTip = Loc.Get(alert ? "Ok" : confirm ? "Yes" : "Ok") };
        ok.Click += (_, _) => { DialogResult = true; Close(); };
        buttons.Children.Add(ok);
        stack.Children.Add(buttons);
        Content = new Grid { Margin = new Thickness(14), Children = { card } };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { DialogResult = false; Close(); }
            else if (e.Key == Key.Enter && (_text is null || !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))) { DialogResult = true; Close(); }
        };
        Loaded += (_, _) => { _text?.Focus(); _text?.SelectAll(); };
    }

    public static string? Ask(Window owner, string title, string message, string? initial = null)
    {
        var w = new PromptWindow(owner, title, message, initial, confirm: false, alert: false);
        return w.ShowDialog() == true ? w._text!.Text : null;
    }

    public static bool Confirm(Window owner, string title, string message) =>
        new PromptWindow(owner, title, message, null, confirm: true, alert: false).ShowDialog() == true;

    public static void Alert(Window owner, string title, string message) =>
        new PromptWindow(owner, title, message, null, confirm: false, alert: true).ShowDialog();
}
