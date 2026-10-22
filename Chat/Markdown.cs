using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace SocLucia.Chat;

/// <summary>
/// Markdown (lo que devuelve un modelo de chat) a elementos de WPF: parrafos, titulos, listas
/// (anidadas, numeradas y de tareas), citas, tablas, reglas, bloques de codigo con boton de copiar,
/// y en linea negrita, cursiva, tachado, codigo y enlaces. Las direcciones web y las rutas de
/// ficheros o carpetas que existen en el PC salen como enlaces: un clic las abre. Suficiente para
/// leer respuestas; no pretende ser CommonMark.
/// </summary>
public static class Markdown
{
    /// <summary>
    /// Lo que va en linea, por orden de prioridad: codigo, imagen, enlace, enlace automatico, negrita,
    /// tachado, cursiva, direccion web, file:///, ruta entre comillas y ruta suelta (C:\…, C:/… o \\servidor\…).
    /// </summary>
    private static readonly Regex Inline = new(
        @"`(?<code>[^`]+)`"
        + @"|!\[(?<ialt>[^\]]*)\]\((?<iurl><[^>]+>|[^)\s]+)(?:\s+""[^""]*"")?\)"
        + @"|\[(?<ltext>[^\]]+)\]\((?<lurl><[^>]+>|[^)\s]+)(?:\s+""[^""]*"")?\)"
        + @"|<(?<auto>(?:https?|file|mailto):[^>\s]+)>"
        + @"|\*\*(?<b>.+?)\*\*|__(?<b2>.+?)__"
        + @"|~~(?<s>.+?)~~"
        + @"|(?<![\w*])\*(?<i>[^*\s](?:[^*]*[^*\s])?)\*(?![\w*])|(?<![\w_])_(?<i2>[^_\s](?:[^_]*[^_\s])?)_(?![\w_])"
        + @"|(?<url>https?://[^\s<>()\[\]""'`]*[^\s<>()\[\]""'`.,;:!?»”])"
        + @"|(?<furl>file:///[^\s<>""'`]+)"
        + @"|[""«](?<qpath>(?:[A-Za-z]:[\\/]|\\\\)[^""»\r\n]+)[""»]"
        + @"|(?<path>(?<![\w/\\])(?:[A-Za-z]:[\\/]|\\\\[\w.$-]+\\)[^\s<>""|*?`]*[^\s<>""|*?`.,;:!)»”'])",
        RegexOptions.Compiled);

    private static readonly Regex TableSeparator = new(@"^\s*\|?\s*:?-+:?\s*(\|\s*:?-+:?\s*)*\|?\s*$", RegexOptions.Compiled);
    private static readonly Regex Rule = new(@"^\s{0,3}([-*_])(\s*\1){2,}\s*$", RegexOptions.Compiled);
    private static readonly Regex Bullet = new(@"^(?<indent>\s*)(?<marker>[-*+]|\d+[.)])\s+(?<task>\[[ xX]\]\s+)?(?<text>.*)$", RegexOptions.Compiled);

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
            AddInlines(inlines, string.Join(" ", paragraph), fontSize, codeBackground, accent);
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
                AddInlines(inlines, heading.Groups[2].Value.TrimEnd('#', ' '), size, codeBackground, accent);
                panel.Children.Add(block);
                i++;
                continue;
            }
            if (Rule.IsMatch(line) && paragraph.Count == 0)
            {
                panel.Children.Add(new Border { Height = 1, Background = codeBackground, BorderBrush = accent, BorderThickness = new Thickness(0, 1, 0, 0), Opacity = 0.6, Margin = new Thickness(0, 6, 0, 10) });
                i++;
                continue;
            }
            // Tabla: una fila con barras seguida de la fila de guiones.
            if (line.Contains('|') && i + 1 < lines.Length && lines[i + 1].Contains('|') && TableSeparator.IsMatch(lines[i + 1]))
            {
                FlushParagraph();
                var rows = new List<string> { line };
                var separator = lines[i + 1];
                i += 2;
                while (i < lines.Length && lines[i].Contains('|') && lines[i].Trim().Length > 0)
                    rows.Add(lines[i++]);
                panel.Children.Add(Table(rows, separator, fontSize, foreground, codeBackground, accent));
                continue;
            }
            if (line.TrimStart().StartsWith('>'))
            {
                FlushParagraph();
                var quoted = new List<string>();
                while (i < lines.Length && lines[i].TrimStart().StartsWith('>'))
                {
                    var q = lines[i++].TrimStart()[1..];
                    quoted.Add(q.StartsWith(' ') ? q[1..] : q);
                }
                var inner = Render(string.Join("\n", quoted), fontSize, foreground, codeBackground, accent, copyLabel, saveLabel);
                panel.Children.Add(new Border { BorderBrush = accent, BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(10, 2, 0, 2), Margin = new Thickness(0, 0, 0, 8), Opacity = 0.9, Child = inner });
                continue;
            }
            var bullet = Bullet.Match(line);
            if (bullet.Success)
            {
                FlushParagraph();
                var marker = bullet.Groups["marker"].Value;
                var task = bullet.Groups["task"].Value.Trim();
                var depth = bullet.Groups["indent"].Value.Replace("\t", "    ").Length / 2;
                var item = new Grid { Margin = new Thickness(8 + 16 * Math.Min(depth, 6), 0, 0, 4) };
                item.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                item.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var symbol = task.Length > 0 ? (task is "[ ]" ? "☐" : "☑") : char.IsDigit(marker[0]) ? marker : depth > 0 ? "◦" : "•";
                var dot = new TextBlock { Text = symbol, FontSize = fontSize, Foreground = foreground, Margin = new Thickness(0, 0, 8, 0), MinWidth = fontSize * 1.1 };
                var (body, inlines) = Selectable(fontSize, foreground, new Thickness(0));
                AddInlines(inlines, bullet.Groups["text"].Value, fontSize, codeBackground, accent);
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
            IsDocumentEnabled = true,   // con esto los enlaces responden al clic aunque sea de solo lectura
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

    internal static void AddInlines(InlineCollection inlines, string text, double fontSize, Brush codeBackground, Brush accent)
    {
        var at = 0;
        for (var m = Inline.Match(text); m.Success; m = Inline.Match(text, at))
        {
            if (m.Index > at) inlines.Add(new Run(text[at..m.Index]));
            at = m.Index + m.Length;
            var g = m.Groups;
            if (g["path"].Success && LinkTarget(g["path"].Value, pathsOnly: false) is null && LongerPath(text, m.Index, at) is { } longer)
            {
                // Ruta con espacios sin comillas («C:\Program Files\…»): se alarga hasta la que existe.
                inlines.Add(Link(new Run(text[m.Index..longer.End]), longer.Target, accent));
                at = longer.End;
                continue;
            }
            if (g["code"].Success)
            {
                // Codigo en linea; si es una ruta que existe (los modelos suelen ponerlas asi), tambien es enlace.
                var code = new Run(g["code"].Value) { FontFamily = new FontFamily("Cascadia Mono, Consolas"), Background = codeBackground, FontSize = fontSize - 1 };
                inlines.Add(LinkTarget(g["code"].Value, pathsOnly: true) is { } target ? Link(code, target, accent) : code);
            }
            else if (g["ltext"].Success || g["iurl"].Success)
            {
                var label = g["ltext"].Success ? g["ltext"].Value : (g["ialt"].Value.Length > 0 ? "🖼 " + g["ialt"].Value : "🖼");
                var url = (g["lurl"].Success ? g["lurl"].Value : g["iurl"].Value).Trim('<', '>');
                var span = new Span();
                AddInlines(span.Inlines, label, fontSize, codeBackground, accent);
                inlines.Add(LinkTarget(url, pathsOnly: false) is { } target ? Link(span, target, accent) : span);
            }
            else if (g["auto"].Success || g["url"].Success || g["furl"].Success || g["qpath"].Success || g["path"].Success)
            {
                var raw = g["auto"].Success ? g["auto"].Value : g["url"].Success ? g["url"].Value : g["furl"].Success ? g["furl"].Value : g["qpath"].Success ? g["qpath"].Value : g["path"].Value;
                var shown = g["qpath"].Success ? m.Value : raw;   // la ruta entre comillas se enseña con sus comillas
                inlines.Add(LinkTarget(raw, pathsOnly: false) is { } target ? Link(new Run(shown), target, accent) : new Run(shown));
            }
            else if (g["b"].Success || g["b2"].Success)
            {
                var bold = new Bold();
                AddInlines(bold.Inlines, g["b"].Success ? g["b"].Value : g["b2"].Value, fontSize, codeBackground, accent);
                inlines.Add(bold);
            }
            else if (g["s"].Success)
            {
                var strike = new Span { TextDecorations = TextDecorations.Strikethrough };
                AddInlines(strike.Inlines, g["s"].Value, fontSize, codeBackground, accent);
                inlines.Add(strike);
            }
            else if (g["i"].Success || g["i2"].Success)
            {
                var italic = new Italic();
                AddInlines(italic.Inlines, g["i"].Success ? g["i"].Value : g["i2"].Value, fontSize, codeBackground, accent);
                inlines.Add(italic);
            }
            else
                inlines.Add(new Run(m.Value));
        }
        if (at < text.Length) inlines.Add(new Run(text[at..]));
    }

    /// <summary>
    /// Una ruta suelta que no existe tal cual puede seguir tras un espacio: se prueba alargandola palabra a
    /// palabra (hasta 8) y se queda la mas larga que existe, sin la puntuacion final.
    /// </summary>
    private static (int End, string Target)? LongerPath(string text, int start, int end)
    {
        (int, string)? best = null;
        var pos = end;
        for (var words = 0; words < 8 && pos < text.Length && text[pos] == ' '; words++)
        {
            var next = pos + 1;
            while (next < text.Length && !char.IsWhiteSpace(text[next]) && "<>\"|*?`".IndexOf(text[next]) < 0) next++;
            var candidate = text[start..next].TrimEnd('.', ',', ';', ':', '!', ')', '»', '”', '\'');
            if (candidate.Length == 0) break;
            if (LinkTarget(candidate, pathsOnly: true) is { } target) best = (start + candidate.Length, target);
            pos = next;
        }
        return best;
    }

    private static readonly Dictionary<string, bool> Exists = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A donde lleva un enlace: una direccion web (http, https, mailto) tal cual, o un fichero o carpeta
    /// que existe en este PC (ruta absoluta, file:/// o con «:linea» al final, que se quita). Lo que no
    /// existe no es enlace: un clic que no abre nada confunde mas que el texto plano.
    /// </summary>
    internal static string? LinkTarget(string raw, bool pathsOnly)
    {
        var value = raw.Trim();
        if (!pathsOnly && (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || value.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)))
            return Uri.TryCreate(value, UriKind.Absolute, out _) ? value : null;
        if (value.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !uri.IsFile) return null;
            value = uri.LocalPath;
        }
        if (!Regex.IsMatch(value, @"^(?:[A-Za-z]:[\\/]|\\\\[^\\]+\\)")) return null;
        if (value.IndexOfAny(System.IO.Path.GetInvalidPathChars()) >= 0) return null;
        foreach (var candidate in new[] { value, Regex.Replace(value, @"(?<=\.\w+):\d+(?::\d+)?$", string.Empty), value.TrimEnd('\\', '/') })
        {
            if (candidate.Length < 3) continue;
            bool found;
            lock (Exists)
            {
                if (!Exists.TryGetValue(candidate, out found))
                {
                    // Las rutas de red pueden tardar: solo se miran las locales (las de red se enlazan sin mirar).
                    // Solo se recuerda lo que existe: un fichero que la IA crea despues tiene que poder enlazarse.
                    found = candidate.StartsWith(@"\\", StringComparison.Ordinal) || System.IO.File.Exists(candidate) || System.IO.Directory.Exists(candidate);
                    if (Exists.Count > 2000) Exists.Clear();
                    if (found) Exists[candidate] = true;
                }
            }
            if (found) return candidate;
        }
        return null;
    }

    /// <summary>Olvida lo que se sabia de las rutas (un fichero que la IA acaba de crear ya existe).</summary>
    internal static void ForgetPaths()
    {
        lock (Exists) Exists.Clear();
    }

    private static Hyperlink Link(Inline content, string target, Brush accent)
    {
        var link = new Hyperlink(content) { Foreground = accent, Cursor = Cursors.Hand, ToolTip = target };
        link.Click += (_, _) => OpenLink(target);
        return link;
    }

    /// <summary>Abre el enlace con lo que tenga Windows para el (navegador, editor, Explorador…).</summary>
    internal static void OpenLink(string target)
    {
        try
        {
            Services.Desktop.Current.Open(target);
        }
        catch (Exception ex)
        {
            Services.DiagLog.Write("enlace", $"no se ha podido abrir {target}: {ex.Message}");
        }
    }

    private static UIElement Table(List<string> rows, string separator, double fontSize, Brush foreground, Brush codeBackground, Brush accent)
    {
        static List<string> Cells(string row)
        {
            var t = row.Trim();
            if (t.StartsWith('|')) t = t[1..];
            if (t.EndsWith('|') && !t.EndsWith("\\|")) t = t[..^1];
            return Regex.Split(t, @"(?<!\\)\|").Select(c => c.Trim().Replace("\\|", "|")).ToList();
        }
        var aligns = Cells(separator).Select(c => c.StartsWith(':') && c.EndsWith(':') ? TextAlignment.Center : c.EndsWith(':') ? TextAlignment.Right : TextAlignment.Left).ToList();
        var columns = Math.Max(aligns.Count, Cells(rows[0]).Count);
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 10), HorizontalAlignment = HorizontalAlignment.Left };
        for (var c = 0; c < columns; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (var r = 0; r < rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var cells = Cells(rows[r]);
            for (var c = 0; c < columns; c++)
            {
                // Un TextBlock (no la caja seleccionable): dentro de una columna automatica se mide por su texto.
                var box = new TextBlock
                {
                    FontSize = fontSize,
                    Foreground = foreground,
                    FontWeight = r == 0 ? FontWeights.SemiBold : FontWeights.Normal,
                    TextAlignment = c < aligns.Count ? aligns[c] : TextAlignment.Left,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 400,
                };
                AddInlines(box.Inlines, c < cells.Count ? cells[c] : string.Empty, fontSize, codeBackground, accent);
                var cell = new Border
                {
                    BorderBrush = codeBackground,
                    BorderThickness = new Thickness(c == 0 ? 1 : 0, r == 0 ? 1 : 0, 1, 1),
                    Background = r == 0 ? codeBackground : Brushes.Transparent,
                    Padding = new Thickness(8, 4, 8, 4),
                    Child = box,
                };
                Grid.SetRow(cell, r);
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }
        }
        return new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = grid };
    }

    private static UIElement CodeBlock(string code, string lang, double fontSize, Brush foreground, Brush background, Brush accent, string copyLabel, string? saveLabel)
    {
        var border = new Border { Background = background, CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 4, 0, 8) };
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var head = new DockPanel { LastChildFill = false };
        var copy = new Button { Content = "", ToolTip = copyLabel, Style = (Style)Application.Current.FindResource("GhostIconButton"), Width = 28, Height = 28, FontSize = 13 };
        copy.Click += (_, _) => { try { Services.Desktop.Current.SetClipboardText(code); } catch (Exception) { } };
        DockPanel.SetDock(copy, Dock.Right);
        head.Children.Add(copy);
        if (saveLabel is not null)
        {
            // Guardar el bloque como fichero, con la extension que pida el lenguaje.
            var save = new Button { Content = "\uE74E", ToolTip = saveLabel, Style = (Style)Application.Current.FindResource("GhostIconButton"), Width = 28, Height = 28, FontSize = 13 };
            save.Click += (_, _) =>
            {
                var ext = ExtensionFor(lang);
                if (Services.Desktop.Current.PickSave(null, "codigo" + ext, $"{(lang.Length > 0 ? lang : "txt")} (*{ext})|*{ext}|Todo|*.*", ext) is { } target)
                    try { System.IO.File.WriteAllText(target, code, new System.Text.UTF8Encoding(false)); } catch (Exception) { }
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
