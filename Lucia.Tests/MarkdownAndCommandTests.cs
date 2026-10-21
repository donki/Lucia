using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using SocLucia.Agent;
using SocLucia.Chat;

namespace SocLucia.Tests;

public class MarkdownTests
{
    private static Panel Render(string text, string? save = null) =>
        Markdown.Render(text, 14, Brushes.Black, Brushes.LightGray, Brushes.Blue, "Copiar", save);

    private static string TextOf(RichTextBox box) => new TextRange(box.Document.ContentStart, box.Document.ContentEnd).Text.TrimEnd('\r', '\n');

    [Fact]
    public void Paragraphs_headings_and_lists() => Sandbox.Sta(() =>
    {
        var panel = Render("# Titulo\r\nlinea uno\nlinea dos\n\n- punto\n2) segundo\n\n\n###### pequeño");
        Assert.Equal(5, panel.Children.Count);
        var title = (RichTextBox)panel.Children[0];
        Assert.Equal("Titulo", TextOf(title));
        Assert.Equal(FontWeights.SemiBold, title.FontWeight);
        Assert.Equal(14 + 4 * 1.5 + 1, title.FontSize);
        Assert.Equal("linea uno linea dos", TextOf((RichTextBox)panel.Children[1]));
        var bullet = (Grid)panel.Children[2];
        Assert.Equal("•", ((TextBlock)bullet.Children[0]).Text);
        Assert.Equal("punto", TextOf((RichTextBox)bullet.Children[1]));
        Assert.Equal("2)", ((TextBlock)((Grid)panel.Children[3]).Children[0]).Text);
        var small = (RichTextBox)panel.Children[4];
        Assert.Equal(15, small.FontSize);   // nivel 6: sin aumento salvo el punto
        Assert.Equal(0, small.Margin.Bottom);   // el ultimo bloque no deja margen debajo
        return 0;
    });

    [Fact]
    public void Inline_bold_italic_and_code() => Sandbox.Sta(() =>
    {
        var box = (RichTextBox)Render("uno **dos** *tres* `cuatro` fin").Children[0];
        var inlines = ((Paragraph)box.Document.Blocks.FirstBlock).Inlines.ToList();
        Assert.Contains(inlines, i => i is Bold b && new TextRange(b.ContentStart, b.ContentEnd).Text == "dos");
        Assert.Contains(inlines, i => i is Italic it && new TextRange(it.ContentStart, it.ContentEnd).Text == "tres");
        var code = inlines.OfType<Run>().Single(r => r.Text == "cuatro");
        Assert.Equal(Brushes.LightGray, code.Background);
        Assert.Equal(13, code.FontSize);
        Assert.EndsWith(" fin", TextOf(box));
        return 0;
    });

    [Fact]
    public void Empty_text_renders_nothing() => Sandbox.Sta(() =>
    {
        Assert.Empty(Render("").Children);
        Assert.Empty(Render("\n\n  \n").Children);
        return 0;
    });

    [Fact]
    public void Selectable_boxes_are_read_only_and_follow_their_width() => Sandbox.Sta(() =>
    {
        var (box, inlines) = Markdown.Selectable(12, Brushes.Red, new Thickness(1, 2, 3, 4), FontWeights.Bold);
        Assert.True(box.IsReadOnly);
        Assert.Equal(FontWeights.Bold, box.FontWeight);
        Assert.Equal(new Thickness(1, 2, 3, 4), box.Margin);
        inlines.Add(new Run("hola"));
        Assert.Equal("hola", TextOf(box));
        box.Measure(new Size(300, 100));
        box.Arrange(new Rect(0, 0, 300, 100));
        box.UpdateLayout();
        Assert.InRange(box.Document.PageWidth, 250, 300);   // el ancho de la caja menos su relleno interno
        return 0;
    });

    [Fact]
    public void Code_blocks_keep_their_text_language_and_buttons() => Ui.Run(() =>
    {
        // Los bloques de codigo buscan el estilo del boton en los recursos de la aplicacion: la App de
        // verdad del arnes de ventanas. Crear aqui otra Application tumbaba el banco entero si esta
        // prueba salia antes que las de ventanas (solo puede haber una por proceso).
        var panel = Render("antes\n```python\nprint('hola')\n  x = 1\n```\ndespues", save: "Guardar");
        Assert.Equal(3, panel.Children.Count);
        var grid = (Grid)((Border)panel.Children[1]).Child;
        var head = (DockPanel)grid.Children[0];
        Assert.Equal(3, head.Children.Count);   // copiar, guardar y el lenguaje
        Assert.Equal("python", ((TextBlock)head.Children[2]).Text);
        Assert.Equal("print('hola')\n  x = 1", ((TextBox)grid.Children[1]).Text);

        // Sin cerrar (el modelo se quedo a medias) y sin guardar ni lenguaje.
        var open = Render("```\na\nb");
        var openGrid = (Grid)((Border)open.Children[0]).Child;
        Assert.Single(((DockPanel)openGrid.Children[0]).Children);
        Assert.Equal("a\nb", ((TextBox)openGrid.Children[1]).Text);
        return 0;
    });

    [Theory]
    [InlineData("C#", ".cs")]
    [InlineData("Python", ".py")]
    [InlineData("xaml", ".xaml")]
    [InlineData("XML", ".xml")]
    [InlineData("pwsh", ".ps1")]
    [InlineData("zsh", ".sh")]
    [InlineData("dockerfile", "")]
    [InlineData("", ".txt")]
    [InlineData("cobol", ".txt")]
    public void Save_extension_follows_the_language(string lang, string ext)
    {
        var method = typeof(Markdown).GetMethod("ExtensionFor", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.Equal(ext, method.Invoke(null, [lang]));
    }
}

public class CommandToolTests
{
    [Fact]
    public async Task Captures_stdout_stderr_and_exit_code_in_utf8()
    {
        var folder = Sandbox.NewFolder("orden");
        var r = await CommandTool.RunAsync("Write-Output 'ñandú'; [Console]::Error.WriteLine('fallo'); exit 7", folder, CancellationToken.None);
        Assert.False(r.TimedOut);
        Assert.Equal(7, r.ExitCode);
        Assert.Contains("ñandú", r.Output);
        Assert.Contains("fallo", r.Output);
    }

    [Fact]
    public async Task Runs_in_the_given_folder_or_the_profile_when_it_does_not_exist()
    {
        var folder = Sandbox.NewFolder("cwd");
        var here = await CommandTool.RunAsync("(Get-Location).Path", folder, CancellationToken.None);
        Assert.Equal(folder, here.Output.Trim());
        var fallback = await CommandTool.RunAsync("(Get-Location).Path", Path.Combine(folder, "no-existe"), CancellationToken.None);
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), fallback.Output.Trim());
    }

    [Fact]
    public async Task Long_output_is_cut_in_the_middle()
    {
        var r = await CommandTool.RunAsync("1..3000 | ForEach-Object { 'linea ' + $_ }", Sandbox.Root, CancellationToken.None);
        Assert.Contains("…[salida recortada]…", r.Output);
        Assert.StartsWith("linea 1" + Environment.NewLine, r.Output);
        Assert.EndsWith("linea 3000" + Environment.NewLine, r.Output);
        Assert.True(r.Output.Length < 12_100);
    }

    [Fact]
    public async Task Cancelling_kills_the_process_and_throws()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var started = DateTime.UtcNow;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CommandTool.RunAsync("Start-Sleep -Seconds 60", Sandbox.Root, cts.Token));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(30));
    }
}
