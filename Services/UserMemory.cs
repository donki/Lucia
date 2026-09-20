using System.IO;
using System.Text.Json;

namespace SocLucia.Services;

/// <summary>Algo que la IA ha aprendido del usuario y que se le recuerda en cada conversacion.</summary>
public sealed class Fact
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Text { get; set; } = string.Empty;
    public DateTimeOffset At { get; set; } = DateTimeOffset.Now;
}

/// <summary>
/// La memoria sobre el usuario: frases cortas («se llama Josep», «programa en C#», «prefiere
/// respuestas breves») que la IA guarda con la herramienta <c>remember</c> y que van en cada
/// conversacion. Vive en <c>memory.json</c>, solo en este PC, y en Ajustes se ve entera y se borra
/// (una a una o toda). Tope de 80 frases: lo mas antiguo cae.
/// </summary>
public static class UserMemory
{
    private const int Max = 80;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static List<Fact>? _facts;

    private static string File => Path.Combine(Paths.Root, "memory.json");

    public static IReadOnlyList<Fact> All => Load();

    public static bool Add(string text)
    {
        text = text.Trim().TrimEnd('.');
        if (text.Length < 3) return false;
        if (text.Length > 240) text = text[..240];
        var facts = Load();
        if (facts.Any(f => string.Equals(f.Text, text, StringComparison.OrdinalIgnoreCase)))
            return false;
        facts.Add(new Fact { Text = text });
        while (facts.Count > Max) facts.RemoveAt(0);
        Save();
        return true;
    }

    public static void Remove(string id)
    {
        Load().RemoveAll(f => f.Id == id);
        Save();
    }

    public static void Clear()
    {
        Load().Clear();
        Save();
    }

    /// <summary>Lo que se le cuenta al modelo, o vacio si no hay nada.</summary>
    public static string Prompt()
    {
        var facts = Load();
        if (facts.Count == 0) return string.Empty;
        return "What you know about the user (from earlier conversations):\n" + string.Join("\n", facts.Select(f => "- " + f.Text));
    }

    private static List<Fact> Load()
    {
        if (_facts is not null) return _facts;
        try
        {
            _facts = System.IO.File.Exists(File) ? JsonSerializer.Deserialize<List<Fact>>(System.IO.File.ReadAllText(File), Json) ?? [] : [];
        }
        catch (Exception) { _facts = []; }
        return _facts;
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Paths.Root);
            System.IO.File.WriteAllText(File, JsonSerializer.Serialize(_facts ?? [], Json));
        }
        catch (Exception) { }
    }
}
