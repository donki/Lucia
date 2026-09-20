using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SocLucia.Services;

/// <summary>Una tarea programada: un prompt que se manda solo a una hora, cada dia, cada semana o cada tantos minutos.</summary>
public sealed class ScheduledTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Title { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    /// <summary>«once», «daily», «weekly», «every».</summary>
    public string Kind { get; set; } = "once";
    /// <summary>Hora del dia (once/daily/weekly).</summary>
    public TimeSpan Time { get; set; }
    /// <summary>Dia (weekly) o fecha (once).</summary>
    public DayOfWeek Day { get; set; }
    public DateOnly Date { get; set; }
    /// <summary>Minutos entre ejecuciones (every).</summary>
    public int IntervalMinutes { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTimeOffset NextRun { get; set; }
    public DateTimeOffset? LastRun { get; set; }
    public string? LastThreadId { get; set; }

    public string Describe(CultureInfo culture) => Kind switch
    {
        "daily" => $"{Loc("daily", culture)} {Time:hh\\:mm}",
        "weekly" => $"{culture.DateTimeFormat.GetDayName(Day)} {Time:hh\\:mm}",
        "every" => $"{Loc("every", culture)} {IntervalMinutes} min",
        _ => $"{Date.ToString("d", culture)} {Time:hh\\:mm}",
    };

    private static string Loc(string key, CultureInfo culture) => (key, culture.TwoLetterISOLanguageName) switch
    {
        ("daily", "es") => "cada día a las",
        ("daily", _) => "daily at",
        ("every", "es") => "cada",
        (_, _) => "every",
    };
}

/// <summary>
/// Las tareas programadas viven en <c>tasks.json</c>; un temporizador de la ventana principal
/// pregunta cada medio minuto cuales tocan y las manda como conversaciones nuevas («⏰ titulo»).
/// Se crean desde el chat (herramienta <c>schedule_task</c>) o se borran desde Ajustes. Las
/// ejecuciones son desatendidas: las herramientas que piden permiso se rechazan; las que estan en
/// «Siempre» funcionan.
/// </summary>
public static class Scheduler
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static List<ScheduledTask>? _tasks;

    private static string File => Path.Combine(Paths.Root, "tasks.json");

    public static IReadOnlyList<ScheduledTask> All => Load();

    /// <summary>
    /// Crea una tarea a partir de un «cuando» en texto: «once 2026-09-21 09:00», «daily 09:00»,
    /// «weekly mon 09:00» (mon..sun o lun..dom) o «every 30m» / «every 2h».
    /// </summary>
    public static ScheduledTask Add(string title, string prompt, string when)
    {
        var task = new ScheduledTask { Title = title.Trim(), Prompt = prompt.Trim() };
        var w = when.Trim().ToLowerInvariant();
        Match m;
        if ((m = Regex.Match(w, @"^every\s+(\d+)\s*(m|min|minutes?|minutos?|h|hours?|horas?)$")).Success)
        {
            task.Kind = "every";
            var n = int.Parse(m.Groups[1].Value);
            task.IntervalMinutes = Math.Max(1, m.Groups[2].Value.StartsWith('h') ? n * 60 : n);
        }
        else if ((m = Regex.Match(w, @"^daily\s+(\d{1,2}):(\d{2})$")).Success)
        {
            task.Kind = "daily";
            task.Time = new TimeSpan(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), 0);
        }
        else if ((m = Regex.Match(w, @"^weekly\s+([a-z]{3})\w*\s+(\d{1,2}):(\d{2})$")).Success)
        {
            task.Kind = "weekly";
            task.Day = DayFrom(m.Groups[1].Value);
            task.Time = new TimeSpan(int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value), 0);
        }
        else if ((m = Regex.Match(w, @"^(?:once\s+)?(\d{4})-(\d{2})-(\d{2})\s+(\d{1,2}):(\d{2})$")).Success)
        {
            task.Kind = "once";
            task.Date = new DateOnly(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
            task.Time = new TimeSpan(int.Parse(m.Groups[4].Value), int.Parse(m.Groups[5].Value), 0);
        }
        else
            throw new ArgumentException("when must be: once YYYY-MM-DD HH:MM | daily HH:MM | weekly mon HH:MM | every 30m");
        task.NextRun = Next(task, DateTimeOffset.Now);
        Load().Add(task);
        Save();
        return task;
    }

    public static void Remove(string id) { Load().RemoveAll(t => t.Id == id); Save(); }

    public static void SetEnabled(string id, bool enabled)
    {
        var t = Load().FirstOrDefault(x => x.Id == id);
        if (t is null) return;
        t.Enabled = enabled;
        if (enabled && t.NextRun <= DateTimeOffset.Now) t.NextRun = Next(t, DateTimeOffset.Now);
        Save();
    }

    /// <summary>Las que tocan ahora; se dejan ya apuntadas a la siguiente vez (las de una vez se apagan).</summary>
    public static List<ScheduledTask> Due(DateTimeOffset now)
    {
        var due = Load().Where(t => t.Enabled && t.NextRun <= now).ToList();
        foreach (var t in due)
        {
            t.LastRun = now;
            if (t.Kind == "once") t.Enabled = false;
            else t.NextRun = Next(t, now);
        }
        if (due.Count > 0) Save();
        return due;
    }

    public static void Ran(ScheduledTask task, string threadId)
    {
        task.LastThreadId = threadId;
        Save();
    }

    public static DateTimeOffset Next(ScheduledTask t, DateTimeOffset from)
    {
        switch (t.Kind)
        {
            case "every":
                return from.AddMinutes(t.IntervalMinutes);
            case "daily":
            {
                var next = from.Date.Add(t.Time);
                if (next <= from.DateTime) next = next.AddDays(1);
                return new DateTimeOffset(next);
            }
            case "weekly":
            {
                var next = from.Date.Add(t.Time);
                while (next.DayOfWeek != t.Day || next <= from.DateTime) next = next.AddDays(1);
                return new DateTimeOffset(next);
            }
            default:
                return new DateTimeOffset(t.Date.ToDateTime(TimeOnly.FromTimeSpan(t.Time)));
        }
    }

    private static DayOfWeek DayFrom(string s) => s switch
    {
        "mon" or "lun" => DayOfWeek.Monday, "tue" or "mar" => DayOfWeek.Tuesday, "wed" or "mie" or "mié" => DayOfWeek.Wednesday,
        "thu" or "jue" => DayOfWeek.Thursday, "fri" or "vie" => DayOfWeek.Friday, "sat" or "sab" or "sáb" => DayOfWeek.Saturday,
        "sun" or "dom" => DayOfWeek.Sunday, _ => throw new ArgumentException("day must be mon..sun"),
    };

    private static List<ScheduledTask> Load()
    {
        if (_tasks is not null) return _tasks;
        try { _tasks = System.IO.File.Exists(File) ? JsonSerializer.Deserialize<List<ScheduledTask>>(System.IO.File.ReadAllText(File), Json) ?? [] : []; }
        catch (Exception) { _tasks = []; }
        return _tasks;
    }

    private static void Save()
    {
        try { Directory.CreateDirectory(Paths.Root); System.IO.File.WriteAllText(File, JsonSerializer.Serialize(_tasks ?? [], Json)); }
        catch (Exception) { }
    }
}
