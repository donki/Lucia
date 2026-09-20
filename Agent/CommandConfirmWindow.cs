using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SocLucia.Localization;
using SocLucia.Services;

namespace SocLucia.Agent;

/// <summary>
/// «La IA quiere ejecutar esto»: la orden tal cual, el porque que dio el modelo, y ejecutar o
/// cancelar. Con la casilla de «no volver a preguntar en esta conversacion» el usuario asume el
/// resto de ordenes de esa conversacion (constitucion de herramientas, 4: nada de saltarse la
/// confirmacion salvo decision explicita y consciente).
/// </summary>
public sealed class CommandConfirmWindow : Window
{
    private readonly CheckBox _auto;

    private CommandConfirmWindow(Window owner, string command, string? reason)
    {
        Owner = owner;
        Title = Loc.Get("CommandTitle");
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)FindResource("PageBackground");
        SourceInitialized += (_, _) => ThemeManager.ApplyToWindow(this);

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = Loc.Get("CommandIntro"), TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("TextPrimary") });
        if (!string.IsNullOrWhiteSpace(reason))
            stack.Children.Add(new TextBlock { Text = reason, TextWrapping = TextWrapping.Wrap, Style = (Style)FindResource("HintText"), Margin = new Thickness(0, 6, 0, 0) });
        stack.Children.Add(new Border
        {
            Background = (Brush)FindResource("PageBackground"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 10, 0, 0),
            Child = new TextBox
            {
                Text = command,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = (Brush)FindResource("TextPrimary"),
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontSize = 13,
                MaxHeight = 220,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            },
        });
        stack.Children.Add(new TextBlock { Text = Loc.Format("CommandFolder", AppSettings.Current.WorkFolder), Style = (Style)FindResource("HintText"), Margin = new Thickness(0, 8, 0, 0) });
        _auto = new CheckBox { Content = Loc.Get("CommandAutoApprove"), Style = (Style)FindResource("Check"), Margin = new Thickness(0, 10, 0, 0) };
        stack.Children.Add(_auto);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var cancel = new Button { Style = (Style)FindResource("GhostIconButton"), Content = "", ToolTip = Loc.Get("Cancel") };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        var run = new Button { Style = (Style)FindResource("IconButton"), Content = "", ToolTip = Loc.Get("CommandRun") };
        run.Click += (_, _) => { DialogResult = true; Close(); };
        buttons.Children.Add(cancel);
        buttons.Children.Add(run);
        stack.Children.Add(buttons);

        Content = new Grid { Margin = new Thickness(14), Children = { new Border { Style = (Style)FindResource("Card"), Padding = new Thickness(18, 14, 18, 16), Child = stack } } };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { DialogResult = false; Close(); }
            else if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { DialogResult = true; Close(); }
        };
    }

    /// <summary>true si se ejecuta; «autoApprove» si ademas no hay que volver a preguntar en esta conversacion.</summary>
    public static (bool Run, bool AutoApprove) Ask(Window owner, string command, string? reason)
    {
        var w = new CommandConfirmWindow(owner, command, reason);
        var run = w.ShowDialog() == true;
        return (run, run && w._auto.IsChecked == true);
    }
}
