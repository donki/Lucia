using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using SocLucia.Localization;

namespace SocLucia.Controls;

/// <summary>
/// Casilla de contraseña con el boton del ojo para verla (constitucion General 6.6; la misma que RC
/// Manager). Por dentro son un PasswordBox y un TextBox superpuestos: WPF no deja quitar los puntos a
/// un PasswordBox, asi que el ojo enseña el TextBox con el mismo texto.
/// </summary>
public sealed class RevealPasswordBox : Grid
{
    private readonly PasswordBox _hidden = new();
    private readonly TextBox _shown = new() { Visibility = Visibility.Collapsed };
    private readonly ToggleButton _eye = new();
    private bool _syncing;

    public RevealPasswordBox()
    {
        _hidden.Style = (Style)FindResource("PasswordField");
        _shown.Style = (Style)FindResource("Field");
        _hidden.Padding = _shown.Padding = new Thickness(8, 0, 34, 0);
        _eye.Style = EyeStyle();
        _eye.Content = "";
        _eye.FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        _eye.SetResourceReference(Control.ForegroundProperty, "TextSecondary");
        _eye.Width = _eye.Height = 26;
        _eye.FontSize = 15;
        _eye.HorizontalAlignment = HorizontalAlignment.Right;
        _eye.VerticalAlignment = VerticalAlignment.Center;
        _eye.Margin = new Thickness(0, 0, 4, 0);
        _eye.Focusable = false;
        _eye.ToolTip = Loc.Get("ShowPassword");
        _eye.Checked += (_, _) => Reveal(true);
        _eye.Unchecked += (_, _) => Reveal(false);
        _hidden.PasswordChanged += (_, _) => { if (!_syncing) { _syncing = true; _shown.Text = _hidden.Password; _syncing = false; } };
        _shown.TextChanged += (_, _) => { if (!_syncing) { _syncing = true; _hidden.Password = _shown.Text; _syncing = false; } };
        // Lo escrito se aplica al salir de la casilla (General 6.8), sea cual sea la de las dos que tenga el foco.
        _hidden.LostFocus += (_, e) => Left?.Invoke(this, e);
        _shown.LostFocus += (_, e) => Left?.Invoke(this, e);
        Children.Add(_hidden);
        Children.Add(_shown);
        Children.Add(_eye);
    }

    /// <summary>Al salir de la casilla (de cualquiera de las dos).</summary>
    public event RoutedEventHandler? Left;

    public bool Revealed => _eye.IsChecked == true;

    public string Password
    {
        get => _hidden.Password;
        set => _hidden.Password = value;
    }

    /// <summary>Enseña u oculta el texto (lo que hace el ojo).</summary>
    public void Toggle() => _eye.IsChecked = !Revealed;

    private void Reveal(bool show)
    {
        _eye.Content = show ? "" : "";
        _eye.ToolTip = Loc.Get(show ? "HidePassword" : "ShowPassword");
        _shown.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        _hidden.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
    }

    private static Style EyeStyle()
    {
        var style = new Style(typeof(ToggleButton));
        style.Setters.Add(new Setter(BackgroundProperty, System.Windows.Media.Brushes.Transparent));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(CursorProperty, System.Windows.Input.Cursors.Hand));
        var template = new ControlTemplate(typeof(ToggleButton));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, System.Windows.Media.Brushes.Transparent);
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        return style;
    }
}
