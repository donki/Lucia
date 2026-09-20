using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace SocLucia.Chat;

/// <summary>
/// Markdown de andar por casa (lo que devuelve un modelo de chat) a elementos de WPF: parrafos,
/// titulos, listas, bloques de codigo con boton de copiar, y negrita, cursiva y codigo en linea.
/// Suficiente para leer respuestas; no pretende ser CommonMark.
/// </summary>
public static class Markdown
{
    private static readonly Regex Inline = new(@"(\*\*[^*]+\*\*|`[^`]+`|\*[^*]+\*)", RegexOptions.Compiled);

    public static Panel Render(string text, double fontSize, Brush foreground, Brush codeBackground, Brush accent, string copyLabel, string? saveLabel = null)
    {
        var panel = new StackPanel();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var i = 0;
        var paragraph = new List<string>();

        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            var (block, inlines) = Selectable(fontSize, foreground, new Thickness(0, 0, 0, 8));
            AddInlines(inlines, string.Join(" ", paragraph), fontSize, codeBackground);
            panel.Children.Add(block);
            paragraph.Clear();
        }

        while (i < lines.Length)
        {
            var line = lines[i];
            if (line.TrimStart().StartsWith("```"))
            {
                FlushParagraph();
                var lang = line.Trim()[3..].Trim();
                var code = new List<string>();
                i++;
                while (i < lines.Length && !lines[i].TrimStart().StartsWith("```"))
                    code.Add(lines[i++]);
                i++;   // la valla de cierre (o el final si el modelo se quedo a medias)
                panel.Children.Add(CodeBlock(string.Join("\n", code), lang, fontSize, foreground, codeBackground, accent, copyLabel, saveLabel));
                continue;
            }
            var heading = Regex.Match(line, @"^(#{1,6})\s+(.*)$");
            if (heading.Success)
            {
                FlushParagraph();
                var level = heading.Groups[1].Value.Length;
                var size = fontSize + Math.Max(0, 5 - level) * 1.5 + 1;
                var (block, inlines) = Selectable(size, foreground, new Thickness(0, 6, 0, 6), FontWeights.SemiBold);
                AddInlines(inlines, heading.Groups[2].Value, size, codeBackground);
                panel.Children.Add(block);
                i++;
                continue;
            }
            var bullet = Regex.Match(line, @"^\s*([-*+]|\d+[.)])\s+(.*)$");
            if (bullet.Success)
            {
                FlushParagraph();
                var marker = bullet.Groups[1].Value;
                var item = new Grid { Margin = new Thickness(8, 0, 0, 4) };
                item.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                item.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var dot = new TextBlock { Text = char.IsDigit(marker[0]) ? marker : "•", FontSize = fontSize, Foreground = foreground, Margin = new Thickness(0, 0, 8, 0) };
                var (body, inlines) = Selectable(fontSize, foreground, new Thickness(0));
                AddInlines(inlines, bullet.Groups[2].Value, fontSize, codeBackground);
                Grid.SetColumn(body, 1);
                item.Children.Add(dot);
                item.Children.Add(body);
                panel.Children.Add(item);
                i++;
                continue;
            }
            if (line.Trim().Length == 0)
            {
                FlushParagraph();
                i++;
                continue;
            }
            paragraph.Add(line.Trim());
            i++;
        }
        FlushParagraph();
        if (panel.Children.Count > 0 && panel.Children[^1] is FrameworkElement last)
            last.Margin = new Thickness(last.Margin.Left, last.Margin.Top, last.Margin.Right, 0);
        return panel;
    }

    /// <summary>
    /// Un parrafo que se puede seleccionar y copiar: un RichTextBox de solo lectura sin marco, con
    /// un FlowDocument de un solo parrafo (los Inlines de negrita/cursiva/codigo sirven igual).
    /// </summary>
    public static (RichTextBox Box, InlineCollection Inlines) Selectable(double fontSize, Brush foreground, Thickness margin, FontWeight? weight = null)
    {
        var paragraph = new Paragraph { Margin = new Thickness(0), TextAlignment = TextAlignment.Left };
        var document = new FlowDocument(paragraph) { PagePadding = new Thickness(0), FontSize = fontSize, Foreground = foreground, TextAlignment = TextAlignment.Left };
        var box = new RichTextBox
        {
            Document = document,
            IsReadOnly = true,
            IsDocumentEnabled = true,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = foreground,
            FontSize = fontSize,
            Padding = new Thickness(0),
            Margin = margin,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        if (weight is { } w) box.FontWeight = w;
        // Sin esto el documento se cree ancho infinito y no ajusta las lineas al ancho de la burbuja.
        box.SizeChanged += (_, e) => document.PageWidth = Math.Max(10, e.NewSize.Width);
        return (box, paragraph.Inlines);
    }

    private static void AddInlines(InlineCollection inlines, string text, double fontSize, Brush codeBackground)
    {
        var parts = Inline.Split(text);
        foreach (var part in parts)
        {
            if (part.Length == 0) continue;
            if (part.StartsWith("**") && part.EndsWith("**") && part.Length > 4)
                inlines.Add(new Bold(new Run(part[2..^2])));
            else if (part.StartsWith('`') && part.EndsWith('`') && part.Length > 2)
                inlines.Add(new Run(part[1..^1]) { FontFamily = new FontFamily("Cascadia Mono, Consolas"), Background = codeBackground, FontSize = fontSize - 1 });
            else if (part.StartsWith('*') && part.EndsWith('*') && part.Length > 2)
                inlines.Add(new Italic(new Run(part[1..^1])));
            else
                inlines.Add(new Run(part));
        }
    }

    private static UIElement CodeBlock(string code, string lang, double fontSize, Brush foreground, Brush background, Brush accent, string copyLabel, string? saveLabel)
    {
        var border = new Border { Background = background, CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 4, 0, 8) };
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var head = new DockPanel { LastChildFill = false };
        var copy = new Button { Content = "", ToolTip = copyLabel, Style = (Style)Application.Current.FindResource("GhostIconButton"), Width = 28, Height = 28, FontSize = 13 };
        copy.Click += (_, _) => { try { Clipboard.SetText(code); } catch (Exception) { } };
        DockPanel.SetDock(copy, Dock.Right);
        head.Children.Add(copy);
        if (saveLabel is not null)
        {
            // Guardar el bloque como fichero, con la extension que pida el lenguaje.
            var save = new Button { Content = "\uE74E", ToolTip = saveLabel, Style = (Style)Application.Current.FindResource("GhostIconButton"), Width = 28, Height = 28, FontSize = 13 };
            save.Click += (_, _) =>
            {
                var ext = ExtensionFor(lang);
                var dialog = new Microsoft.Win32.SaveFileDialog { FileName = "codigo" + ext, Filter = $"{(lang.Length > 0 ? lang : "txt")} (*{ext})|*{ext}|Todo|*.*", DefaultExt = ext };
                if (dialog.ShowDialog() == true)
                    try { System.IO.File.WriteAllText(dialog.FileName, code, new System.Text.UTF8Encoding(false)); } catch (Exception) { }
            };
            DockPanel.SetDock(save, Dock.Right);
            head.Children.Add(save);
        }
        if (lang.Length > 0)
            head.Children.Add(new TextBlock { Text = lang, FontSize = 11, Foreground = accent, VerticalAlignment = VerticalAlignment.Center });
        var body = new TextBox
        {
            Text = code,
            IsReadOnly = true,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = foreground,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = fontSize - 1,
            TextWrapping = TextWrapping.NoWrap,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(0),
        };
        Grid.SetRow(body, 1);
        grid.Children.Add(head);
        grid.Children.Add(body);
        border.Child = grid;
        return border;
    }

    private static string ExtensionFor(string lang) => lang.ToLowerInvariant() switch
    {
        "cs" or "csharp" or "c#" => ".cs", "python" or "py" => ".py", "js" or "javascript" => ".js", "ts" or "typescript" => ".ts",
        "html" => ".html", "css" => ".css", "json" => ".json", "xml" or "xaml" => "." + lang.ToLowerInvariant(), "yaml" or "yml" => ".yml",
        "ps1" or "powershell" or "pwsh" => ".ps1", "bash" or "sh" or "shell" or "zsh" => ".sh", "bat" or "cmd" => ".cmd", "sql" => ".sql",
        "java" => ".java", "kotlin" or "kt" => ".kt", "swift" => ".swift", "go" => ".go", "rust" or "rs" => ".rs", "c" => ".c", "cpp" or "c++" => ".cpp", "h" => ".h",
        "php" => ".php", "rb" or "ruby" => ".rb", "md" or "markdown" => ".md", "csv" => ".csv", "toml" => ".toml", "ini" => ".ini", "dockerfile" => "",
        _ => ".txt",
    };
}
