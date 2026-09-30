using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace SocLucia.Tests;

/// <summary>PDF sinteticos con PdfPig: una pagina por texto; un texto vacio es una pagina sin capa de texto (como un escaneado).</summary>
internal static class PdfFactory
{
    public static byte[] WithPages(params string[] pages)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var text in pages)
        {
            var page = builder.AddPage(PageSize.A4);
            if (text.Length > 0)
                page.AddText(text, 12, new PdfPoint(50, 750), font);
        }
        return builder.Build();
    }
}
