using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace SocLucia.Controls;

/// <summary>
/// Indicador de espera de toda la aplicacion: el arco que gira de la burbuja de respuesta (colores del
/// tema, claro y oscuro) y un texto que dice que esta haciendo. Regla: lo que tarda mas de ~300 ms lo
/// enseña, y la ventana nunca parece muerta. Para no parpadear con lo rapido, aparece solo si la espera
/// pasa de <see cref="Delay"/>.
/// </summary>
public sealed class BusyIndicator : StackPanel
{
    /// <summary>Lo que tiene que durar una espera para que se vea el indicador.</summary>
    public static TimeSpan Delay { get; internal set; } = TimeSpan.FromMilliseconds(300);

    private readonly TextBlock _text;
    private readonly DispatcherTimer _timer;
    private int _busy;

    public BusyIndicator()
    {
        Orientation = Orientation.Horizontal;
        Visibility = Visibility.Collapsed;
        Margin = new Thickness(0, 4, 0, 4);
        Children.Add(Spinner(16));
        _text = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        _text.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        Children.Add(_text);
        _timer = new DispatcherTimer { Interval = Delay };
        _timer.Tick += (_, _) => { _timer.Stop(); if (_busy > 0) Visibility = Visibility.Visible; };
    }

    /// <summary>Si hay algo en marcha (aunque aun no se vea).</summary>
    public bool IsBusy => _busy > 0;

    public string Text => _text.Text;

    /// <summary>Empieza una espera con su texto; se ve a los <see cref="Delay"/> si sigue en marcha. Se pueden anidar.</summary>
    public void Start(string text)
    {
        _text.Text = text;
        if (_busy++ > 0) return;
        if (Delay <= TimeSpan.Zero) { Visibility = Visibility.Visible; return; }
        _timer.Interval = Delay;
        _timer.Start();
    }

    /// <summary>Cambia el texto de la espera en marcha (por ejemplo, el progreso).</summary>
    public void Update(string text) => _text.Text = text;

    public void Stop()
    {
        if (_busy == 0 || --_busy > 0) return;
        _timer.Stop();
        Visibility = Visibility.Collapsed;
    }

    /// <summary>El arco que gira sobre su pista, del tamaño pedido (el mismo en toda la aplicacion).</summary>
    public static FrameworkElement Spinner(double size)
    {
        var track = new Ellipse { Width = size, Height = size, StrokeThickness = 2 };
        track.SetResourceReference(Shape.StrokeProperty, "Separator");
        var arc = new Ellipse
        {
            Width = size,
            Height = size,
            StrokeThickness = 2,
            StrokeDashCap = PenLineCap.Round,
            // En unidades del grosor: el perimetro es pi * (size - 2) / 2; el arco, algo mas de un cuarto.
            StrokeDashArray = [Math.PI * (size - 2) / 2 * 0.3, 1000],
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        arc.SetResourceReference(Shape.StrokeProperty, "PrimaryText");
        var rotate = new RotateTransform();
        arc.RenderTransform = rotate;
        // Con las animaciones de Windows apagadas (accesibilidad) el arco se queda quieto; el texto sigue diciendo que hace.
        if (SystemParameters.ClientAreaAnimation)
            rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever });
        var spinner = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        spinner.Children.Add(track);
        spinner.Children.Add(arc);
        return spinner;
    }
}
