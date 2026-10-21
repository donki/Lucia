using SocLucia.Agent;

namespace SocLucia.Tests;

public class ProgramInfoTests
{
    /// <summary>Un PE minimo (MZ + cabecera PE) con el subsistema que se pida: 2 = ventana, 3 = consola.</summary>
    private static string Pe(ushort subsystem, ushort magic = 0x10B)
    {
        var buf = new byte[0x200];
        buf[0] = (byte)'M'; buf[1] = (byte)'Z';
        const int pe = 0x80;
        BitConverter.GetBytes(pe).CopyTo(buf, 0x3C);
        buf[pe] = (byte)'P'; buf[pe + 1] = (byte)'E';
        BitConverter.GetBytes(magic).CopyTo(buf, pe + 24);
        BitConverter.GetBytes(subsystem).CopyTo(buf, pe + 24 + 68);
        var path = Path.Combine(Sandbox.NewFolder("pe"), $"prog-{subsystem}-{magic}.exe");
        File.WriteAllBytes(path, buf);
        return path;
    }

    [Fact]
    public void Distingue_ventana_de_consola_y_lo_que_no_es_programa()
    {
        Assert.Equal(ProgramInfo.Kind.Gui, ProgramInfo.KindOf(Pe(2)));
        Assert.Equal(ProgramInfo.Kind.Console, ProgramInfo.KindOf(Pe(3)));
        Assert.Equal(ProgramInfo.Kind.Gui, ProgramInfo.KindOf(Pe(2, 0x20B)));   // PE32+
        var text = Path.Combine(Sandbox.NewFolder("pe"), "no.txt");
        File.WriteAllText(text, "hola");
        Assert.Equal(ProgramInfo.Kind.NotProgram, ProgramInfo.KindOf(text));
        Assert.Equal(ProgramInfo.Kind.NotProgram, ProgramInfo.KindOf(Path.Combine(Sandbox.Root, "no-esta.exe")));
    }

    [Fact]
    public void Saca_las_rutas_de_ejecutables_de_una_orden()
    {
        var found = ProgramInfo.ExecutablesIn("Get-Content \"D:\\Herramientas\\prog.exe\" -Raw; .\\otra.exe | foo.dll").ToList();
        Assert.Contains("D:\\Herramientas\\prog.exe", found);
        Assert.Contains(".\\otra.exe", found);
        Assert.Contains("foo.dll", found);
        Assert.Empty(ProgramInfo.ExecutablesIn("Get-Date | Measure-Object"));
    }

    [Fact]
    public void Avisa_de_un_programa_de_ventana_cuando_la_orden_no_da_salida()
    {
        var gui = Pe(2);
        var folder = Path.GetDirectoryName(gui)!;
        var note = ProgramInfo.Note($"[System.IO.File]::ReadAllBytes(\"{gui}\")", "[exit code 0]\n", folder);
        Assert.Contains("GUI application", note);
        Assert.Contains("will not reveal what it does", note);
        Assert.Contains(Path.GetFileName(gui), note);
        // Con salida de verdad no avisa; una orden sin exe tampoco; una de consola tampoco.
        Assert.Equal(string.Empty, ProgramInfo.Note($"& \"{gui}\"", "[exit code 0]\nalgo que imprimio\n", folder));
        Assert.Equal(string.Empty, ProgramInfo.Note("Get-Date", "[exit code 0]\n", folder));
        Assert.Equal(string.Empty, ProgramInfo.Note($"& \"{Pe(3)}\"", "[exit code 0]\n", folder));
    }
}
