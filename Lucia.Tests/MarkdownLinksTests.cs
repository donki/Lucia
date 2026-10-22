using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using SocLucia.Chat;
using SocLucia.Services;

namespace SocLucia.Tests;

/// <summary>El Markdown ampliado: enlaces (web y a ficheros que existen), tablas, citas, reglas, tareas, anidadas y tachado.</summary>
public class MarkdownLinksTests
{
    private static Panel Render(string text) => Markdown.Render(text, 14, Brushes.Black, Brushes.LightGray, Brushes.Blue, "Copiar");

    private static List<Inline> InlinesOf(UIElement element) => ((Paragraph)((RichTextBox)element).Document.Blocks.FirstBlock).Inlines.ToList();

    private static string TextOf(TextElement e) => new TextRange(e.ContentStart, e.ContentEnd).Text;

    [Fact]
    public void Direcciones_web_y_enlaces_markdown_son_enlaces_con_su_destino()
    {
        Sandbox.Sta(() =>
        {
            var links = InlinesOf(Render("Mira [la web](https://example.com/a?b=1), https://socratic.example/x. y <mailto:yo@example.com> o [roto](nada) fin").Children[0]).OfType<Hyperlink>().ToList();
            Assert.Equal(3, links.Count);
            Assert.Equal(("la web", "https://example.com/a?b=1"), (TextOf(links[0]), links[0].ToolTip));
            Assert.Equal("https://socratic.example/x", links[1].ToolTip);   // el punto final no es parte de la direccion
            Assert.Equal("mailto:yo@example.com", links[2].ToolTip);
            Assert.Equal(Brushes.Blue, links[0].Foreground);
            return 0;
        });
    }

    [Fact]
    public void Las_rutas_que_existen_son_enlaces_y_al_hacer_clic_se_abren()
    {
        var folder = Sandbox.NewFolder("enlaces con espacio");
        var file = Path.Combine(folder, "notas.md");
        File.WriteAllText(file, "# hola");
        var missing = Path.Combine(folder, "no-existe.txt");
        var desktop = Desktop.Current;
        var fake = new FakeDesktop();
        Desktop.Current = fake;
        try
        {
            Sandbox.Sta(() =>
            {
                var text = $"Guardado en `{file}`, abre \"{folder}\" o {file}:12. Ademas {missing} y [aqui](<{file}>) y [web]({new Uri(file).AbsoluteUri}).";
                var inlines = InlinesOf(Render(text).Children[0]);
                var links = inlines.OfType<Hyperlink>().ToList();
                Assert.Equal([file, folder, file, file, file], links.Select(l => (string)l.ToolTip).ToArray());
                // El codigo en linea sigue con su aspecto de codigo dentro del enlace
                Assert.Equal(Brushes.LightGray, ((Run)links[0].Inlines.FirstInline).Background);
                // La que no existe se queda como texto
                Assert.DoesNotContain(links, l => TextOf(l).Contains("no-existe"));
                Assert.Contains("no-existe.txt", string.Concat(inlines.OfType<Run>().Select(r => r.Text)));
                links[1].DoClick();
                Assert.Equal([folder], fake.Opened);
                return 0;
            });
            // Un fichero que aparece despues (lo acaba de crear la IA) ya se enlaza
            File.WriteAllText(missing, "x");
            Sandbox.Sta(() =>
            {
                Assert.Single(InlinesOf(Render(missing).Children[0]).OfType<Hyperlink>());
                return 0;
            });
            // Si abrirlo falla, no revienta
            fake.OpenError = new InvalidOperationException("no");
            Markdown.OpenLink(file);
        }
        finally
        {
            Desktop.Current = desktop;
            fake.Dispose();
        }
    }

    [Theory]
    [InlineData("ftp://x/y", null)]
    [InlineData("relativa/a.md", null)]
    [InlineData("file://servidor-que-no-hay", null)]
    [InlineData("C:\\<mal>\\a", null)]
    [InlineData("\\\\servidor\\compartida\\a.txt", "\\\\servidor\\compartida\\a.txt")]   // de red: sin mirar (puede tardar)
    public void Lo_que_no_es_web_ni_ruta_existente_no_es_enlace(string raw, string? expected)
        => Assert.Equal(expected, Markdown.LinkTarget(raw, pathsOnly: false));

    [Fact]
    public void Codigo_en_linea_que_no_es_ruta_no_es_enlace_aunque_sea_una_web()
        => Assert.Null(Markdown.LinkTarget("https://example.com", pathsOnly: true));

    [Fact]
    public void Tachado_negrita_con_cursiva_y_guiones_bajos()
    {
        Sandbox.Sta(() =>
        {
            var inlines = InlinesOf(Render("~~viejo~~ **muy _bonito_** __fuerte__ _suave_ y nombre_de_variable").Children[0]);
            var strike = inlines.OfType<Span>().Single(s => s.TextDecorations == TextDecorations.Strikethrough);
            Assert.Equal("viejo", TextOf(strike));
            var bold = inlines.OfType<Bold>().ToList();
            Assert.Equal(2, bold.Count);
            Assert.Single(bold[0].Inlines.OfType<Italic>());
            Assert.Single(inlines.OfType<Italic>());
            Assert.EndsWith("nombre_de_variable", string.Concat(inlines.OfType<Run>().Select(r => r.Text)));   // los guiones bajos dentro de palabra no son cursiva
            return 0;
        });
    }

    [Fact]
    public void Tablas_con_cabecera_alineacion_y_barras_escapadas()
    {
        Sandbox.Sta(() =>
        {
            var panel = Render("Antes\n\n| Nombre | Precio | Nota |\n|:---|---:|:-:|\n| **Uno** | 10 | a \\| b |\n| Dos | 20 |\n\nDespues");
            Assert.Equal(3, panel.Children.Count);
            var grid = (Grid)((ScrollViewer)panel.Children[1]).Content;
            Assert.Equal(3, grid.ColumnDefinitions.Count);
            Assert.Equal(3, grid.RowDefinitions.Count);
            TextBlock Cell(int r, int c) => (TextBlock)grid.Children.OfType<Border>().Single(b => Grid.GetRow(b) == r && Grid.GetColumn(b) == c).Child;
            Assert.Equal(FontWeights.SemiBold, Cell(0, 0).FontWeight);
            Assert.Equal(TextAlignment.Right, Cell(1, 1).TextAlignment);
            Assert.Equal(TextAlignment.Center, Cell(1, 2).TextAlignment);
            Assert.Single(Cell(1, 0).Inlines.OfType<Bold>());
            Assert.Equal("a | b", string.Concat(Cell(1, 2).Inlines.OfType<Run>().Select(r => r.Text)));
            Assert.Empty(Cell(2, 2).Inlines);   // fila corta: celda vacia
            // Se mide por su texto, no se queda en una columna de una letra
            grid.Measure(new Size(2000, 2000));
            Assert.True(Cell(0, 0).DesiredSize.Width > 40);
            return 0;
        });
    }

    [Fact]
    public void Citas_reglas_tareas_y_listas_anidadas()
    {
        Sandbox.Sta(() =>
        {
            var panel = Render("> cita **fuerte**\n> segunda\n\n---\n- [ ] por hacer\n- [x] hecho\n  - dentro\n1. uno\n### Fin ###");
            var quote = (Border)panel.Children[0];
            Assert.Equal(Brushes.Blue, quote.BorderBrush);
            Assert.Equal("cita fuerte segunda", new TextRange(((RichTextBox)((Panel)quote.Child).Children[0]).Document.ContentStart, ((RichTextBox)((Panel)quote.Child).Children[0]).Document.ContentEnd).Text.TrimEnd());
            Assert.Equal(1, ((Border)panel.Children[1]).Height);
            Assert.Equal("☐", ((TextBlock)((Grid)panel.Children[2]).Children[0]).Text);
            Assert.Equal("☑", ((TextBlock)((Grid)panel.Children[3]).Children[0]).Text);
            var nested = (Grid)panel.Children[4];
            Assert.Equal("◦", ((TextBlock)nested.Children[0]).Text);
            Assert.True(nested.Margin.Left > ((Grid)panel.Children[3]).Margin.Left);
            Assert.Equal("1.", ((TextBlock)((Grid)panel.Children[5]).Children[0]).Text);
            Assert.Equal("Fin", new TextRange(((RichTextBox)panel.Children[6]).Document.ContentStart, ((RichTextBox)panel.Children[6]).Document.ContentEnd).Text.TrimEnd());
            return 0;
        });
    }

    [Fact]
    public void Imagenes_como_enlace_con_su_texto()
    {
        Sandbox.Sta(() =>
        {
            var link = InlinesOf(Render("![un gato](https://example.com/gato.png)").Children[0]).OfType<Hyperlink>().Single();
            Assert.Equal("🖼 un gato", TextOf(link));
            Assert.Equal("https://example.com/gato.png", link.ToolTip);
            return 0;
        });
    }
}
