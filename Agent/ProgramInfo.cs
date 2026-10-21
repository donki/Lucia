using System.IO;
using System.Text.RegularExpressions;

namespace SocLucia.Agent;

/// <summary>
/// Mira si un fichero es un programa de Windows (PE) y de qué tipo: de consola (escribe en la salida) o
/// de ventana (GUI, no escribe nada en una orden). Sirve para que el agente no insista en leer la salida de
/// un programa con ventana —leer sus bytes y «descomprimirlos» no revela lo que hace— y proponga otra cosa.
/// Solo lee la cabecera PE, nunca ejecuta nada.
/// </summary>
public static class ProgramInfo
{
    public enum Kind { NotProgram, Console, Gui }

    /// <summary>El tipo de un fichero por su cabecera PE (el campo Subsystem); NotProgram si no es un .exe/.dll de Windows.</summary>
    public static Kind KindOf(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var r = new BinaryReader(stream);
            if (stream.Length < 0x40 || r.ReadUInt16() != 0x5A4D) return Kind.NotProgram;   // "MZ"
            stream.Position = 0x3C;
            var peOffset = r.ReadInt32();
            if (peOffset <= 0 || peOffset + 0x5E > stream.Length) return Kind.NotProgram;
            stream.Position = peOffset;
            if (r.ReadUInt32() != 0x00004550) return Kind.NotProgram;   // "PE\0\0"
            stream.Position = peOffset + 24;   // tras el COFF header: el optional header
            var magic = r.ReadUInt16();        // 0x10B PE32, 0x20B PE32+
            if (magic is not (0x10B or 0x20B)) return Kind.NotProgram;
            stream.Position = peOffset + 24 + 68;   // el campo Subsystem
            return r.ReadUInt16() switch { 3 => Kind.Console, 2 => Kind.Gui, _ => Kind.NotProgram };
        }
        catch (Exception)
        {
            return Kind.NotProgram;
        }
    }

    /// <summary>Las rutas de .exe o .dll que nombra una orden (entre comillas o sueltas), para avisar si son de ventana.</summary>
    public static IEnumerable<string> ExecutablesIn(string command)
        => Regex.Matches(command, @"(?:[A-Za-z]:\\[^""'|<>\r\n]*?|[^\s""'|<>]+)\.(?:exe|dll)", RegexOptions.IgnoreCase)
            .Select(m => m.Value.Trim('"', '\''))
            .Where(p => p.Length > 4)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Si una orden intenta sacarle partido a un programa de ventana (leer sus bytes, descomprimirlo…) y no da
    /// salida, el aviso que se añade para el modelo; cadena vacía si no aplica.
    /// </summary>
    public static string Note(string command, string output, string workingFolder)
    {
        if (output.Trim().Length > 0 && !output.TrimEnd().EndsWith("[exit code 0]", StringComparison.Ordinal)) return string.Empty;
        foreach (var raw in ExecutablesIn(command))
        {
            var path = Path.IsPathRooted(raw) ? raw : Path.Combine(workingFolder, raw);
            if (KindOf(path) != Kind.Gui) continue;
            return $"\n[note] `{Path.GetFileName(path)}` is a Windows GUI application (a window program), not a command-line tool: running it or reading its bytes produces no console output and will not reveal what it does. " +
                   "Do not keep running it through run_command. To work with it, drive its window (if a UI-automation tool is available), inspect it with a disassembler or `strings`, read its documentation, or ask the user what they expect.";
        }
        return string.Empty;
    }
}
