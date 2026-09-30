using System.IO.Compression;
using System.Xml.Linq;
using SocLucia.Services;

namespace SocLucia.Tests;

public class DocumentIndexTests
{
    public DocumentIndexTests()
    {
        Sandbox.ResetSettings();
        Sandbox.ClearDocuments();
    }

    private static string Doc(string name, string content)
    {
        var path = Path.Combine(Sandbox.Documents, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Folder_is_the_setting_or_documents_lucia()
    {
        Assert.Equal(Sandbox.Documents, DocumentIndex.Folder);
        AppSettings.Current.DocumentsFolder = "";
        Assert.Equal(DocumentIndex.DefaultFolder, DocumentIndex.Folder);
        Assert.EndsWith(Path.Combine("Lucia"), DocumentIndex.DefaultFolder);
        AppSettings.Current.DocumentsFolder = Path.Combine(Sandbox.Root, "no-existe");
        Assert.Empty(DocumentIndex.Files());
        Assert.Equal(string.Empty, DocumentIndex.Prompt("lo que sea"));
    }

    [Fact]
    public void Files_filters_by_extension_and_size_and_sorts()
    {
        Doc("b.md", "x");
        Doc("A.TXT", "x");
        Doc("sub\\c.cs", "x");
        Doc("foto.png", "x");
        Doc("sin-extension", "x");
        File.WriteAllBytes(Path.Combine(Sandbox.Documents, "grande.txt"), new byte[4 * 1024 * 1024 + 1]);
        var names = DocumentIndex.Files().Select(f => DocumentIndex.Relative(f.FullName)).ToList();
        Assert.Equal(["A.TXT", "b.md", Path.Combine("sub", "c.cs")], names);
    }

    [Fact]
    public void Files_are_capped_at_300()
    {
        for (var i = 0; i < 305; i++) Doc($"n{i:000}.txt", "x");
        var files = DocumentIndex.Files();
        Assert.Equal(300, files.Count);
        Assert.Equal("n000.txt", files[0].Name);
    }

    [Fact]
    public void Read_text_of_plain_html_and_rtf()
    {
        Assert.Equal("tal cual <b>", DocumentIndex.ReadText(Doc("a.txt", "tal cual <b>")));
        var html = DocumentIndex.ReadText(Doc("p.HTML", "<style>x{}</style><SCRIPT>malo()</SCRIPT><p>Uno &lt;dos&gt;</p><div>tres</div>cuatro<br>cinco"));
        Assert.Equal("Uno <dos>\ntres\ncuatro\ncinco", html);
        var rtf = DocumentIndex.ReadText(Doc("r.rtf", @"{\rtf1\ansi {\b Negrita} normal\par segunda\pard tercera}"));
        Assert.Contains("Negrita normal", rtf);
        Assert.Contains("\n segunda", rtf);
        Assert.DoesNotContain("\\", rtf);
        Assert.DoesNotContain("{", rtf);
    }

    [Fact]
    public void Read_text_of_docx_keeps_paragraphs_tabs_and_entities()
    {
        var path = Path.Combine(Sandbox.Documents, "hecho.docx");
        using (var zip = new ZipArchive(File.Create(path), ZipArchiveMode.Create))
        using (var w = new StreamWriter(zip.CreateEntry("word/document.xml").Open()))
            w.Write("<w:document><w:body><w:p><w:r><w:t>Uno</w:t></w:r><w:r><w:tab/><w:t>A &amp; B</w:t></w:r></w:p><w:p><w:r><w:t>Dos</w:t></w:r></w:p></w:body></w:document>");
        Assert.Equal("Uno\tA & B\nDos", DocumentIndex.ReadText(path));

        var broken = Path.Combine(Sandbox.Documents, "roto.docx");
        using (var zip = new ZipArchive(File.Create(broken), ZipArchiveMode.Create))
            zip.CreateEntry("otra/cosa.xml");
        Assert.Throws<InvalidDataException>(() => DocumentIndex.ReadText(broken));
    }

    [Fact]
    public void Read_text_of_pdf_one_page_has_no_page_marks()
    {
        var path = Path.Combine(Sandbox.Documents, "uno.pdf");
        File.WriteAllBytes(path, PdfFactory.WithPages("Contrato de alquiler"));
        Assert.Equal("Contrato de alquiler", DocumentIndex.ReadText(path));
    }

    [Fact]
    public void Read_text_of_pdf_marks_pages_and_skips_empty_ones()
    {
        var path = Path.Combine(Sandbox.Documents, "varias.pdf");
        File.WriteAllBytes(path, PdfFactory.WithPages("Primera pagina", "", "Tercera pagina"));
        var text = DocumentIndex.ReadText(path);
        Assert.Equal("[page 1]\nPrimera pagina\n\n[page 3]\nTercera pagina".ReplaceLineEndings(), text.ReplaceLineEndings());
    }

    [Fact]
    public void Read_text_of_scanned_pdf_says_there_is_no_text()
    {
        var path = Path.Combine(Sandbox.Documents, "escaneado.pdf");
        File.WriteAllBytes(path, PdfFactory.WithPages("", ""));
        Assert.Equal("[This PDF (2 page(s)) has no text layer: it is probably scanned images, so its content cannot be read.]", DocumentIndex.ReadText(path));
    }

    [Fact]
    public void Read_text_of_a_pdf_with_more_than_1000_pages_reads_the_first_1000()
    {
        var path = Path.Combine(Sandbox.Documents, "enorme.pdf");
        File.WriteAllBytes(path, PdfFactory.WithPages(Enumerable.Range(1, 1002).Select(i => i == 1 || i == 1000 ? $"Pagina {i}" : "").ToArray()));
        var text = DocumentIndex.ReadText(path);
        Assert.Contains("[page 1000]", text);
        Assert.EndsWith("[… 2 more page(s) not read]", text);
    }

    [Fact]
    public void A_corrupt_pdf_throws_but_the_prompt_survives_it()
    {
        var path = Path.Combine(Sandbox.Documents, "roto.pdf");
        File.WriteAllText(path, "esto no es un PDF");
        Assert.ThrowsAny<Exception>(() => DocumentIndex.ReadText(path));
        Doc("bueno.md", "El presupuesto de marketing es de cien mil euros.");
        var prompt = DocumentIndex.Prompt("presupuesto marketing");
        Assert.Contains("bueno.md", prompt);
        Assert.DoesNotContain("[roto.pdf]", prompt);
    }

    [Fact]
    public void Prompt_without_useful_words_is_only_the_header()
    {
        Doc("a.md", "contenido cualquiera");
        var prompt = DocumentIndex.Prompt("¿qué es? me mi tu");
        Assert.StartsWith($"The user keeps documents in `{Sandbox.Documents}` (1 file(s)).", prompt);
        Assert.DoesNotContain("Excerpts", prompt);
    }

    [Fact]
    public void Prompt_needs_two_matching_words_when_the_question_has_two()
    {
        Doc("coches.md", "El coche rojo esta en el garaje.");
        Doc("casas.md", "La casa azul tiene jardin y garaje.");
        var prompt = DocumentIndex.Prompt("coche garaje");
        Assert.Contains("[coches.md]", prompt);
        Assert.DoesNotContain("[casas.md]", prompt);
        // Con una sola palabra util basta un acierto.
        Assert.Contains("[casas.md]", DocumentIndex.Prompt("jardin"));
        // Nada encaja: solo la cabecera.
        Assert.DoesNotContain("Excerpts", DocumentIndex.Prompt("submarino amarillo"));
    }

    [Fact]
    public void Prompt_ranks_by_hits_and_returns_at_most_four_excerpts()
    {
        for (var i = 0; i < 6; i++) Doc($"flojo{i}.md", "alfa beta relleno");
        Doc("fuerte.md", "alfa beta gamma delta");
        var prompt = DocumentIndex.Prompt("alfa beta gamma delta");
        var excerpts = prompt.Split('\n').Count(l => l.StartsWith('['));
        Assert.Equal(4, excerpts);
        var first = prompt.Split('\n').First(l => l.StartsWith('['));
        Assert.Equal("[fuerte.md]", first);
    }

    [Fact]
    public void Long_documents_are_chunked_with_overlap_and_cached_until_they_change()
    {
        var text = string.Concat(Enumerable.Range(0, 400).Select(i => $"w{i:0000} "));   // 2400 caracteres
        text = text.Replace("w0390", "zanahoria").Replace("w0391", "remolacha");
        var path = Doc("largo.md", text);
        var prompt = DocumentIndex.Prompt("zanahoria remolacha");
        Assert.Contains("zanahoria remolacha", prompt);
        // Trozos de 900 caracteres que se solapan 150: las palabras del final caen en los dos ultimos, no el principio del texto.
        Assert.Equal(2, prompt.Split("[largo.md]").Length - 1);
        Assert.DoesNotContain("w0000", prompt);

        // La cache se invalida al cambiar el fichero (longitud distinta).
        File.WriteAllText(path, "Ahora habla de berenjena y calabacin, nada mas.");
        Assert.DoesNotContain("[largo.md]", DocumentIndex.Prompt("zanahoria remolacha"));
        Assert.Contains("[largo.md]", DocumentIndex.Prompt("berenjena calabacin"));
    }

    [Fact]
    public void Empty_documents_have_no_chunks()
    {
        Doc("vacio.md", "");
        Assert.DoesNotContain("Excerpts", DocumentIndex.Prompt("algo concreto"));
    }
}

public class DocxTests
{
    private static XDocument Document(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        Assert.NotNull(zip.GetEntry("[Content_Types].xml"));
        Assert.NotNull(zip.GetEntry("_rels/.rels"));
        Assert.NotNull(zip.GetEntry("word/_rels/document.xml.rels"));
        Assert.NotNull(zip.GetEntry("word/styles.xml"));
        foreach (var e in zip.Entries)
            using (var s = e.Open()) XDocument.Load(s);   // todas las partes son XML bien formado
        using var stream = zip.GetEntry("word/document.xml")!.Open();
        return XDocument.Load(stream);
    }

    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [Fact]
    public void Headings_bullets_bold_and_blank_lines()
    {
        var path = Path.Combine(Sandbox.NewFolder("docx"), "a.docx");
        Docx.Write(path, "# Uno\r\n## Dos\n### Tres\n- punto\n* estrella\n\nTexto **negrita** y <menor> & mas");
        var paragraphs = Document(path).Descendants(W + "p").ToList();
        Assert.Equal(7, paragraphs.Count);
        string? Style(XElement p) => p.Element(W + "pPr")?.Element(W + "pStyle")?.Attribute(W + "val")?.Value;
        Assert.Equal(["Heading1", "Heading2", "Heading3", "ListBullet", "ListBullet", null, null], paragraphs.Select(Style).ToArray());
        Assert.Equal("Uno", paragraphs[0].Value);
        Assert.Equal("punto", paragraphs[3].Value);
        Assert.Empty(paragraphs[5].Nodes());
        var runs = paragraphs[6].Elements(W + "r").ToList();
        Assert.Equal(["Texto ", "negrita", " y <menor> & mas"], runs.Select(r => r.Value).ToArray());
        Assert.Equal([false, true, false], runs.Select(r => r.Element(W + "rPr")?.Element(W + "b") is not null).ToArray());
    }

    [Fact]
    public void Round_trip_through_the_document_reader()
    {
        var path = Path.Combine(Sandbox.NewFolder("docx"), "b.docx");
        Docx.Write(path, "# Carta\nEstimado **cliente**:\n  espacios al principio");
        Assert.Equal("Carta\nEstimado cliente:\nespacios al principio", DocumentIndex.ReadText(path));
    }

    [Fact]
    public void Empty_content_is_a_valid_document_with_one_empty_paragraph()
    {
        var path = Path.Combine(Sandbox.NewFolder("docx"), "c.docx");
        Docx.Write(path, "");
        Assert.Single(Document(path).Descendants(W + "p"));
    }
}
