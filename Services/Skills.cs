using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace SocLucia.Services;

/// <summary>
/// Una habilidad: instrucciones empaquetadas que el modelo puede descubrir y cargar cuando una tarea
/// encaja (como las «skills» de los agentes). Es una carpeta con un <c>SKILL.md</c>: una cabecera con
/// <c>name</c> y <c>description</c> y, debajo, el cuerpo con las instrucciones. El cuerpo se lee a
/// demanda (no ocupa contexto hasta que se usa).
/// </summary>
public sealed record Skill(string Name, string Description, string Folder, bool Enabled)
{
    public string SkillFile => Path.Combine(Folder, "SKILL.md");
}

/// <summary>
/// Las habilidades instaladas, en <c>…\sOCLucia\skills</c> (una carpeta por habilidad). Se descubren
/// del disco; que estén encendidas o no se guarda en los ajustes (<see cref="AppSettings.DisabledSkills"/>).
/// Se pueden instalar desde una carpeta, un .zip o una dirección de internet. Fuera de la interfaz para
/// poder probarlo sin ventana (constitución 8.6).
/// </summary>
public static class SkillStore
{
    /// <summary>Carpeta de las habilidades (dentro de la raíz de datos, respeta el arenero SOC_LUCIA_ROOT).</summary>
    public static string Folder => Path.Combine(Paths.Root, "skills");

    /// <summary>Cómo se baja un .zip de una dirección de internet; las pruebas lo sustituyen para no salir a la red.</summary>
    public static Func<string, CancellationToken, Task<byte[]>> Fetcher { get; set; } = DefaultFetchAsync;

    private static async Task<byte[]> DefaultFetchAsync(string url, CancellationToken cancel)
        => await Engine.Downloader.Http.GetByteArrayAsync(url, cancel);

    /// <summary>El nombre de carpeta a partir del nombre de la habilidad (minúsculas, sin rarezas).</summary>
    public static string Slug(string name)
    {
        var slug = new string((name ?? string.Empty).Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        slug = Regex.Replace(slug, "-+", "-").Trim('-');
        return slug.Length == 0 ? "skill" : slug;
    }

    /// <summary>Todas las habilidades que hay en disco, con su estado encendido/apagado de los ajustes.</summary>
    public static List<Skill> All()
    {
        var list = new List<Skill>();
        if (!Directory.Exists(Folder)) return list;
        var disabled = AppSettings.Current.DisabledSkills;
        foreach (var dir in Directory.GetDirectories(Folder).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var file = Path.Combine(dir, "SKILL.md");
            if (!File.Exists(file)) continue;
            var (name, description, _) = Parse(SafeRead(file), Path.GetFileName(dir));
            var enabled = !disabled.Contains(name, StringComparer.OrdinalIgnoreCase);
            list.Add(new Skill(name, description, dir, enabled));
        }
        return list;
    }

    /// <summary>Las habilidades encendidas (las que se le ofrecen al modelo).</summary>
    public static List<Skill> Enabled() => All().Where(s => s.Enabled).ToList();

    public static Skill? Find(string name)
    {
        name = (name ?? string.Empty).Trim();
        return All().FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)
                                      || string.Equals(Path.GetFileName(s.Folder), Slug(name), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>El cuerpo (las instrucciones) de una habilidad; null si no existe.</summary>
    public static string? Read(string name)
    {
        var skill = Find(name);
        if (skill is null) return null;
        var (_, _, body) = Parse(SafeRead(skill.SkillFile), skill.Name);
        return body;
    }

    public static void SetEnabled(string name, bool enabled)
    {
        var skill = Find(name);
        if (skill is null) return;
        var disabled = AppSettings.Current.DisabledSkills;
        disabled.RemoveAll(d => string.Equals(d, skill.Name, StringComparison.OrdinalIgnoreCase));
        if (!enabled) disabled.Add(skill.Name);
        AppSettings.Current.Save();
    }

    public static void Remove(string name)
    {
        var skill = Find(name);
        if (skill is null) return;
        try { Directory.Delete(skill.Folder, recursive: true); } catch (Exception) { }
        AppSettings.Current.DisabledSkills.RemoveAll(d => string.Equals(d, skill.Name, StringComparison.OrdinalIgnoreCase));
        AppSettings.Current.Save();
    }

    /// <summary>Escribe (o rehace) una habilidad que trae la propia Lucia (p. ej. la de REA).</summary>
    public static Skill WriteBuiltin(string name, string markdown)
    {
        var dir = Path.Combine(Folder, Slug(name));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"), markdown, new UTF8Encoding(false));
        return Find(name) ?? new Skill(name, string.Empty, dir, true);
    }

    /// <summary>
    /// Instala una habilidad desde una carpeta, un fichero SKILL.md, un .zip o una dirección http(s) a un
    /// .zip. Comprueba que trae un SKILL.md y que la licencia, si la declara, permite uso comercial
    /// (constitución: solo MIT-compatible). Devuelve la habilidad y un texto para el usuario/modelo.
    /// </summary>
    public static async Task<(Skill? Skill, string Message)> InstallAsync(string source, CancellationToken cancel)
    {
        source = (source ?? string.Empty).Trim().Trim('"');
        if (source.Length == 0) return (null, "[error] a source (folder, .zip or URL) is required");
        string staging = Path.Combine(Path.GetTempPath(), "lucia-skill-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(staging);
            if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            {
                var bytes = await Fetcher(source, cancel);
                var zip = Path.Combine(staging, "skill.zip");
                await File.WriteAllBytesAsync(zip, bytes, cancel);
                ZipFile.ExtractToDirectory(zip, Path.Combine(staging, "unzip"));
                File.Delete(zip);
            }
            else if (File.Exists(source) && source.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                ZipFile.ExtractToDirectory(source, Path.Combine(staging, "unzip"));
            else if (File.Exists(source) && Path.GetFileName(source).Equals("SKILL.md", StringComparison.OrdinalIgnoreCase))
                File.Copy(source, Path.Combine(staging, "SKILL.md"));
            else if (Directory.Exists(source))
                CopyDir(source, staging);
            else
                return (null, "[error] source not found: use a folder with a SKILL.md, a .zip, a SKILL.md file, or an http(s) address to a .zip");

            var skillFile = FindSkillFile(staging);
            if (skillFile is null)
                return (null, "[error] no SKILL.md found in the source");

            var text = SafeRead(skillFile);
            var (name, _, _) = Parse(text, Path.GetFileName(Path.GetDirectoryName(skillFile)!));
            var license = FrontMatter(text, "license");
            if (!LicenseOk(license))
                return (null, $"[error] the skill declares a license that is not clearly for commercial use («{license}»); not installed");

            var target = Path.Combine(Folder, Slug(name));
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
            CopyDir(Path.GetDirectoryName(skillFile)!, target);
            var skill = Find(name);
            return (skill, skill is null ? "[error] could not read the installed skill" : $"Skill '{skill.Name}' installed: {skill.Description}. Use it with use_skill.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return (null, "[error] could not install the skill: " + ex.Message);
        }
        finally
        {
            try { Directory.Delete(staging, recursive: true); } catch (Exception) { }
        }
    }

    /// <summary>La lista de habilidades encendidas para el modelo (nombre: descripción), o vacío si no hay.</summary>
    public static string Prompt()
    {
        var skills = Enabled();
        if (skills.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        sb.AppendLine("You have skills (packaged instructions). When the task matches one, load it with `use_skill` and follow it. Available skills:");
        foreach (var s in skills) sb.AppendLine($"- {s.Name}: {s.Description}");
        return sb.ToString().Trim();
    }

    /// <summary>Texto de list_skills para el modelo: cada habilidad, su estado y cómo añadir más.</summary>
    public static string ListText()
    {
        var skills = All();
        var sb = new StringBuilder();
        if (skills.Count == 0) sb.AppendLine("No skills installed yet.");
        foreach (var s in skills)
            sb.AppendLine($"- {s.Name} ({(s.Enabled ? "on" : "off")}): {s.Description}");
        sb.AppendLine("Load one with use_skill(name). Add one with install_skill (a folder, a .zip, a SKILL.md, or an http(s) address to a .zip).");
        return sb.ToString().Trim();
    }

    // ---- analizar el SKILL.md ----

    /// <summary>Lee el nombre, la descripción y el cuerpo de un SKILL.md (cabecera «---» opcional).</summary>
    public static (string Name, string Description, string Body) Parse(string text, string fallbackName)
    {
        text = (text ?? string.Empty).Replace("\r\n", "\n");
        var name = fallbackName;
        var description = string.Empty;
        var body = text.Trim();
        var m = Regex.Match(text, @"\A\s*---\n(.*?)\n---\n?(.*)\z", RegexOptions.Singleline);
        if (m.Success)
        {
            var header = m.Groups[1].Value;
            body = m.Groups[2].Value.Trim();
            name = Field(header, "name") is { Length: > 0 } n ? n : fallbackName;
            description = Field(header, "description");
        }
        if (description.Length == 0)
        {
            // Sin descripción en la cabecera: la primera línea con texto del cuerpo (sin «#»).
            var first = body.Split('\n').Select(l => l.TrimStart('#', ' ').Trim()).FirstOrDefault(l => l.Length > 0) ?? string.Empty;
            description = first.Length > 160 ? first[..160] + "…" : first;
        }
        return (name.Trim(), description.Trim(), body);
    }

    /// <summary>Un campo de la cabecera de un SKILL.md ya extraído (o vacío).</summary>
    public static string FrontMatter(string text, string key)
    {
        text = (text ?? string.Empty).Replace("\r\n", "\n");
        var m = Regex.Match(text, @"\A\s*---\n(.*?)\n---", RegexOptions.Singleline);
        return m.Success ? Field(m.Groups[1].Value, key) : string.Empty;
    }

    private static string Field(string header, string key)
    {
        var m = Regex.Match(header, $@"(?im)^\s*{Regex.Escape(key)}\s*:\s*(.+?)\s*$");
        return m.Success ? m.Groups[1].Value.Trim().Trim('"', '\'') : string.Empty;
    }

    /// <summary>¿La licencia declarada (si la hay) permite uso comercial? Vacío = no se sabe, se acepta.</summary>
    public static bool LicenseOk(string license)
    {
        if (string.IsNullOrWhiteSpace(license)) return true;
        var l = license.ToLowerInvariant();
        string[] banned = ["noncommercial", "non-commercial", "no commercial", "-nc", "cc-by-nc", "gpl", "agpl", "proprietary", "all rights reserved"];
        return !banned.Any(b => l.Contains(b));
    }

    private static string? FindSkillFile(string root)
    {
        var direct = Path.Combine(root, "SKILL.md");
        if (File.Exists(direct)) return direct;
        return Directory.EnumerateFiles(root, "SKILL.md", SearchOption.AllDirectories).FirstOrDefault();
    }

    private static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(from, to));
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, file.Replace(from, to), overwrite: true);
    }

    private static string SafeRead(string path)
    {
        try { return File.ReadAllText(path); } catch (Exception) { return string.Empty; }
    }
}
