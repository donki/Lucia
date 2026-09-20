using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SocLucia.Localization;
using SocLucia.Services;

namespace SocLucia.Agent;

/// <summary>
/// «La IA quiere usar esto»: el recurso (orden, fichero, direccion…), el detalle tal cual, el
/// porque que dio el modelo, y permitir o cancelar. Se puede dejar de preguntar por ese recurso en
/// la conversacion, o siempre (queda en Ajustes › Permisos). Constitucion de herramientas, 4: nada
/// de saltarse la confirmacion salvo decision explicita y consciente del usuario.
/// </summary>
public sealed class PermissionWindow : Window
{
    public enum Answer { Deny, Once, Conversation, Always }

    private readonly CheckBox _conversation;
    private readonly CheckBox _always;

    private PermissionWindow(Window owner, Resource resource, string detail, string? reason)
    {
        Owner = owner;
        Title = Loc.Get("PermTitle");
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)FindResource("PageBackground");
        SourceInitialized += (_, _) => ThemeManager.ApplyToWindow(this);

        var stack = new StackPanel();
        var intro = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("TextPrimary") };
        intro.Inlines.Add(new System.Windows.Documents.Run(Glyph(resource) + "  ") { FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), Foreground = (Brush)FindResource("Primary") });
        intro.Inlines.Add(Loc.Get("PermIntro_" + resource));
        stack.Children.Add(intro);
        if (!string.IsNullOrWhiteSpace(reason))
            stack.Children.Add(new TextBlock { Text = reason, TextWrapping = TextWrapping.Wrap, Style = (Style)FindResource("HintText"), Margin = new Thickness(0, 6, 0, 0) });
        if (detail.Length > 0)
            stack.Children.Add(new Border
            {
                Background = (Brush)FindResource("PageBackground"),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 10, 0, 0),
                Child = new TextBox
                {
                    Text = detail,
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
        if (resource is Resource.Commands or Resource.FilesRead or Resource.FilesWrite)
            stack.Children.Add(new TextBlock { Text = Loc.Format("CommandFolder", AppSettings.Current.WorkFolder), Style = (Style)FindResource("HintText"), Margin = new Thickness(0, 8, 0, 0) });
        _conversation = new CheckBox { Content = Loc.Get("PermConversation"), Style = (Style)FindResource("Check"), Margin = new Thickness(0, 10, 0, 0) };
        _always = new CheckBox { Content = Loc.Format("PermAlways", Loc.Get("Perm_" + resource)), Style = (Style)FindResource("Check"), Margin = new Thickness(0, 4, 0, 0) };
        stack.Children.Add(_conversation);
        stack.Children.Add(_always);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var cancel = new Button { Style = (Style)FindResource("GhostIconButton"), Content = "", ToolTip = Loc.Get("Cancel") };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        var allow = new Button { Style = (Style)FindResource("IconButton"), Content = "", ToolTip = Loc.Get("PermAllow") };
        allow.Click += (_, _) => { DialogResult = true; Close(); };
        buttons.Children.Add(cancel);
        buttons.Children.Add(allow);
        stack.Children.Add(buttons);

        Content = new Grid { Margin = new Thickness(14), Children = { new Border { Style = (Style)FindResource("Card"), Padding = new Thickness(18, 14, 18, 16), Child = stack } } };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { DialogResult = false; Close(); }
            else if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { DialogResult = true; Close(); }
        };
    }

    public static string Glyph(Resource resource) => resource switch
    {
        Resource.Commands => "",
        Resource.FilesRead => "",
        Resource.FilesWrite => "",
        Resource.Internet => "",
        Resource.Clipboard => "",
        Resource.Apps => "",
        _ => "",
    };

    public static Answer Ask(Window owner, Resource resource, string detail, string? reason)
    {
        var w = new PermissionWindow(owner, resource, detail, reason);
        if (w.ShowDialog() != true) return Answer.Deny;
        if (w._always.IsChecked == true) return Answer.Always;
        return w._conversation.IsChecked == true ? Answer.Conversation : Answer.Once;
    }
}
