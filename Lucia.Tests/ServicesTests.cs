using System.Globalization;
using System.Reflection;
using System.Text.Json;
using SocLucia.Agent;
using SocLucia.Chat;
using SocLucia.Services;

namespace SocLucia.Tests;

public class SchedulerTests
{
    public SchedulerTests()
    {
        foreach (var t in Scheduler.All.ToList()) Scheduler.Remove(t.Id);
    }

    private static readonly CultureInfo Es = new("es-ES");
    private static readonly CultureInfo En = new("en-US");

    [Theory]
    [InlineData("every 30m", 30)]
    [InlineData("EVERY 45 min", 45)]
    [InlineData("every 2h", 120)]
    [InlineData("every 1 hora", 60)]
    [InlineData("every 3 horas", 180)]
    [InlineData("every 10 minutos", 10)]
    [InlineData("every 0m", 1)]
    public void Every_parses_minutes_and_hours(string when, int minutes)
    {
        var before = DateTimeOffset.Now;
        var t = Scheduler.Add(" Titulo ", " prompt ", when);
        Assert.Equal("every", t.Kind);
        Assert.Equal(minutes, t.IntervalMinutes);
        Assert.Equal("Titulo", t.Title);
        Assert.Equal("prompt", t.Prompt);
        Assert.InRange(t.NextRun, before.AddMinutes(minutes).AddSeconds(-1), DateTimeOffset.Now.AddMinutes(minutes).AddSeconds(1));
        Assert.Equal($"every {minutes} min", t.Describe(En));
        Assert.Equal($"cada {minutes} min", t.Describe(Es));
    }

    [Fact]
    public void Daily_weekly_and_once_parse_their_times()
    {
        var daily = Scheduler.Add("a", "b", "daily 9:05");
        Assert.Equal(("daily", new TimeSpan(9, 5, 0)), (daily.Kind, daily.Time));
        Assert.Equal("daily at 09:05", daily.Describe(En));
        Assert.Equal("cada día a las 09:05", daily.Describe(Es));

        var weekly = Scheduler.Add("a", "b", "weekly friday 18:30");
        Assert.Equal((DayOfWeek.Friday, new TimeSpan(18, 30, 0)), (weekly.Day, weekly.Time));
        Assert.Equal("Friday 18:30", weekly.Describe(En));
        Assert.Equal("viernes 18:30", weekly.Describe(Es));
        Assert.Equal(DayOfWeek.Monday, Scheduler.Add("a", "b", "weekly lun 08:00").Day);

        var once = Scheduler.Add("a", "b", "once 2030-01-02 07:08");
        Assert.Equal(new DateOnly(2030, 1, 2), once.Date);
        Assert.Equal(new DateTimeOffset(new DateTime(2030, 1, 2, 7, 8, 0)), once.NextRun);
        Assert.Equal("1/2/2030 07:08", once.Describe(En));
        Assert.Equal(new DateOnly(2031, 5, 6), Scheduler.Add("a", "b", "2031-05-06 10:00").Date);   // «once» es opcional
        Assert.Equal(5, Scheduler.All.Count);
    }

    [Theory]
    [InlineData("mon", DayOfWeek.Monday)]
    [InlineData("tue", DayOfWeek.Tuesday)]
    [InlineData("mar", DayOfWeek.Tuesday)]
    [InlineData("wed", DayOfWeek.Wednesday)]
    [InlineData("mie", DayOfWeek.Wednesday)]
    [InlineData("miércoles", DayOfWeek.Wednesday)]
    [InlineData("thu", DayOfWeek.Thursday)]
    [InlineData("jue", DayOfWeek.Thursday)]
    [InlineData("fri", DayOfWeek.Friday)]
    [InlineData("vie", DayOfWeek.Friday)]
    [InlineData("sat", DayOfWeek.Saturday)]
    [InlineData("sab", DayOfWeek.Saturday)]
    [InlineData("sábado", DayOfWeek.Saturday)]
    [InlineData("sun", DayOfWeek.Sunday)]
    [InlineData("domingo", DayOfWeek.Sunday)]
    public void Weekly_understands_english_and_spanish_days(string day, DayOfWeek expected)
        => Assert.Equal(expected, Scheduler.Add("a", "b", $"weekly {day} 10:00").Day);

    [Theory]
    [InlineData("")]
    [InlineData("tomorrow")]
    [InlineData("daily 9")]
    [InlineData("every m")]
    [InlineData("weekly xyz 10:00")]
    [InlineData("daily 24:00")]
    [InlineData("daily 9:60")]
    [InlineData("weekly mon 25:00")]
    [InlineData("once 2030-13-01 10:00")]
    [InlineData("once 2030-02-30 10:00")]
    [InlineData("once 2030-01-01 24:30")]
    public void Bad_when_is_rejected_and_nothing_is_saved(string when)
    {
        Assert.ThrowsAny<ArgumentException>(() => Scheduler.Add("a", "b", when));
        Assert.Empty(Scheduler.All);
    }

    [Fact]
    public void Next_for_daily_and_weekly()
    {
        var from = new DateTimeOffset(new DateTime(2026, 9, 29, 10, 0, 0));   // martes
        var daily = new ScheduledTask { Kind = "daily", Time = new TimeSpan(9, 0, 0) };
        Assert.Equal(new DateTime(2026, 9, 30, 9, 0, 0), Scheduler.Next(daily, from).DateTime);
        daily.Time = new TimeSpan(11, 0, 0);
        Assert.Equal(new DateTime(2026, 9, 29, 11, 0, 0), Scheduler.Next(daily, from).DateTime);
        daily.Time = new TimeSpan(10, 0, 0);   // justo ahora: la siguiente es mañana
        Assert.Equal(new DateTime(2026, 9, 30, 10, 0, 0), Scheduler.Next(daily, from).DateTime);

        var weekly = new ScheduledTask { Kind = "weekly", Day = DayOfWeek.Tuesday, Time = new TimeSpan(9, 0, 0) };
        Assert.Equal(new DateTime(2026, 10, 6, 9, 0, 0), Scheduler.Next(weekly, from).DateTime);
        weekly.Time = new TimeSpan(12, 0, 0);
        Assert.Equal(new DateTime(2026, 9, 29, 12, 0, 0), Scheduler.Next(weekly, from).DateTime);
        weekly.Day = DayOfWeek.Monday;
        Assert.Equal(new DateTime(2026, 10, 5, 12, 0, 0), Scheduler.Next(weekly, from).DateTime);

        Assert.Equal(from.AddMinutes(15), Scheduler.Next(new ScheduledTask { Kind = "every", IntervalMinutes = 15 }, from));
    }

    [Fact]
    public void Due_moves_repeating_tasks_forward_and_turns_off_one_shots()
    {
        var once = Scheduler.Add("una", "p", "once 2000-01-01 00:00");
        var every = Scheduler.Add("cada", "p", "every 5m");
        var later = DateTimeOffset.Now.AddMinutes(6);
        var due = Scheduler.Due(later);
        Assert.Equal(["una", "cada"], due.Select(t => t.Title).ToArray());
        Assert.False(once.Enabled);
        Assert.Equal(later, once.LastRun);
        Assert.Equal(later.AddMinutes(5), every.NextRun);
        Assert.Empty(Scheduler.Due(later));

        Scheduler.Ran(every, "hilo1");
        Assert.Equal("hilo1", every.LastThreadId);

        // Volver a encender una que ya paso la reprograma; una que no existe no hace nada.
        Scheduler.SetEnabled(once.Id, true);
        Assert.True(once.Enabled);
        Scheduler.SetEnabled("no-existe", true);
        Scheduler.SetEnabled(every.Id, false);
        Assert.Empty(Scheduler.Due(later.AddDays(1)).Where(t => t.Id == every.Id));
    }

    [Fact]
    public void Tasks_survive_a_reload_from_disk()
    {
        var t = Scheduler.Add("persistida", "p", "daily 07:00");
        var file = Path.Combine(Paths.Root, "tasks.json");
        Assert.Contains("persistida", File.ReadAllText(file));
        ResetCache(typeof(Scheduler), "_tasks");
        Assert.Equal(t.Id, Scheduler.All.Single().Id);

        File.WriteAllText(file, "{ roto");
        ResetCache(typeof(Scheduler), "_tasks");
        Assert.Empty(Scheduler.All);
    }

    internal static void ResetCache(Type type, string field) => type.GetField(field, BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, null);
}

public class UserMemoryTests
{
    public UserMemoryTests() => UserMemory.Clear();

    [Fact]
    public void Add_trims_dedupes_and_cuts()
    {
        Assert.True(UserMemory.Add("  Vive en Barcelona.  "));
        Assert.Equal("Vive en Barcelona", UserMemory.All.Single().Text);
        Assert.False(UserMemory.Add("vive en barcelona"));
        Assert.False(UserMemory.Add("ab."));
        Assert.True(UserMemory.Add(new string('x', 300)));
        Assert.Equal(240, UserMemory.All[^1].Text.Length);
        Assert.Equal(8, UserMemory.All[0].Id.Length);
    }

    [Fact]
    public void Keeps_the_last_80_facts()
    {
        for (var i = 0; i < 85; i++) UserMemory.Add($"Dato numero {i}");
        Assert.Equal(80, UserMemory.All.Count);
        Assert.Equal("Dato numero 5", UserMemory.All[0].Text);
    }

    [Fact]
    public void Prompt_lists_facts_and_remove_forgets_one()
    {
        Assert.Equal(string.Empty, UserMemory.Prompt());
        UserMemory.Add("Programa en C#");
        UserMemory.Add("Prefiere respuestas breves");
        Assert.Equal("What you know about the user (from earlier conversations):\n- Programa en C#\n- Prefiere respuestas breves", UserMemory.Prompt());
        UserMemory.Remove(UserMemory.All[0].Id);
        Assert.Equal("Prefiere respuestas breves", UserMemory.All.Single().Text);
    }

    [Fact]
    public void Survives_a_reload_and_a_corrupt_file()
    {
        UserMemory.Add("Tiene dos gatos");
        SchedulerTests.ResetCache(typeof(UserMemory), "_facts");
        Assert.Equal("Tiene dos gatos", UserMemory.All.Single().Text);
        File.WriteAllText(Path.Combine(Paths.Root, "memory.json"), "[{");
        SchedulerTests.ResetCache(typeof(UserMemory), "_facts");
        Assert.Empty(UserMemory.All);
    }
}

public class ThreadStoreTests
{
    [Fact]
    public void Save_load_and_delete_a_conversation_with_attachments()
    {
        var thread = new ChatThread { Title = "Prueba", WorkMode = true, Unattended = true };
        thread.Messages.Add(new StoredMessage { Role = "user", Content = "hola" });
        var source = Path.Combine(Sandbox.NewFolder("adjuntos"), "foto.JPG");
        File.WriteAllBytes(source, [1, 2, 3]);
        var image = ThreadStore.Attach(thread, source);
        Assert.True(image.IsImage);
        Assert.Equal("foto.JPG", image.Name);
        Assert.Equal(3, image.Bytes);
        Assert.StartsWith(ThreadStore.FilesFolder(thread), image.Path);
        var pasted = ThreadStore.AttachImage(thread, [9, 9]);
        Assert.Equal(("imagen.png", true, 2L), (pasted.Name, pasted.IsImage, pasted.Bytes));
        thread.Messages[0].Attachments = [image, pasted];

        var before = thread.UpdatedAt;
        ThreadStore.Save(thread);
        Assert.True(thread.UpdatedAt >= before);
        var loaded = ThreadStore.LoadAll().Single(t => t.Id == thread.Id);
        Assert.Equal("Prueba", loaded.Title);
        Assert.True(loaded.WorkMode);
        Assert.False(loaded.Unattended);   // no se guarda
        Assert.Equal(2, loaded.Messages[0].Attachments!.Count);

        ThreadStore.Delete(thread);
        Assert.DoesNotContain(ThreadStore.LoadAll(), t => t.Id == thread.Id);
        Assert.False(Directory.Exists(ThreadStore.FilesFolder(thread)));
        ThreadStore.Delete(thread);   // otra vez: no falla
    }

    [Fact]
    public void Load_all_sorts_by_last_update_and_skips_broken_files()
    {
        var old = new ChatThread { Title = "vieja" };
        var recent = new ChatThread { Title = "nueva" };
        ThreadStore.Save(old);
        Thread.Sleep(20);
        ThreadStore.Save(recent);
        File.WriteAllText(Path.Combine(Paths.Threads, "roto.json"), "{ no");
        var all = ThreadStore.LoadAll();
        Assert.True(all.FindIndex(t => t.Id == recent.Id) < all.FindIndex(t => t.Id == old.Id));
        File.Delete(Path.Combine(Paths.Threads, "roto.json"));
        ThreadStore.Delete(old);
        ThreadStore.Delete(recent);
    }

    [Theory]
    [InlineData("  Hola  \nsegunda linea", "Hola")]
    [InlineData("", "")]
    public void Title_is_the_first_line(string text, string title) => Assert.Equal(title, ThreadStore.TitleFrom(text));

    [Fact]
    public void Long_titles_are_cut_to_58_characters()
    {
        var title = ThreadStore.TitleFrom(new string('a', 56) + " " + new string('b', 20));
        Assert.Equal(new string('a', 56) + "…", title);
        Assert.Equal(new string('c', 60), ThreadStore.TitleFrom(new string('c', 60)));
    }

    [Theory]
    [InlineData("a.png", true)]
    [InlineData("a.WEBP", true)]
    [InlineData("a.gif", true)]
    [InlineData("a.pdf", false)]
    [InlineData("sin", false)]
    public void Image_files_by_extension(string path, bool image) => Assert.Equal(image, ThreadStore.IsImageFile(path));
}

public class AppSettingsTests
{
    [Fact]
    public void Permissions_default_to_ask_and_free_and_internet_are_special()
    {
        var s = new AppSettings();
        Assert.Equal(Permission.Ask, s.PermissionFor(Resource.Commands));
        Assert.Equal(Permission.Allow, s.PermissionFor(Resource.Free));
        Assert.Equal(Permission.Allow, s.PermissionFor(Resource.Internet));
        s.InternetAccess = false;
        Assert.Equal(Permission.Deny, s.PermissionFor(Resource.Internet));
        s.SetPermission(Resource.Commands, Permission.Allow);
        Assert.Equal(Permission.Allow, s.PermissionFor(Resource.Commands));
        s.SetPermission(Resource.Commands, Permission.Ask);
        Assert.Empty(s.Permissions);
        s.Permissions["Apps"] = "basura";
        Assert.Equal(Permission.Ask, s.PermissionFor(Resource.Apps));
    }

    [Fact]
    public void Remember_replaces_by_path_ignoring_case_and_forget_removes()
    {
        var s = new AppSettings();
        s.Remember(new InstalledModel { Name = "A", Path = "C:\\m\\a.gguf" });
        s.Remember(new InstalledModel { Name = "B", Path = "c:\\M\\A.GGUF" });
        Assert.Equal("B", s.Installed.Single().Name);
        s.Forget("C:\\M\\a.gguf");
        Assert.Empty(s.Installed);
    }

    [Fact]
    public void Has_model_needs_an_existing_file()
    {
        var s = new AppSettings();
        Assert.False(s.HasModel);
        s.ModelPath = Path.Combine(Sandbox.Root, "no.gguf");
        Assert.False(s.HasModel);
        var file = Path.Combine(Sandbox.NewFolder("m"), "si.gguf");
        File.WriteAllText(file, "");
        s.ModelPath = file;
        Assert.True(s.HasModel);
    }

    [Fact]
    public void Save_and_load_round_trip_in_the_sandbox_and_a_token_is_created()
    {
        var current = Sandbox.ResetSettings();
        current.EditorDoor.Token = "";
        current.FontSize = 17;
        current.Save();
        Assert.True(File.Exists(Paths.SettingsFile));
        Assert.StartsWith(Sandbox.Root, Paths.SettingsFile);
        AppSettings.Load();
        Assert.Equal(17, AppSettings.Current.FontSize);
        Assert.Equal(32, AppSettings.Current.EditorDoor.Token.Length);
        Assert.Contains(AppSettings.Current.EditorDoor.Token, File.ReadAllText(Paths.SettingsFile));

        File.WriteAllText(Paths.SettingsFile, "no es json");
        AppSettings.Load();
        Assert.Equal(14, AppSettings.Current.FontSize);   // ajustes por defecto
        File.Delete(Paths.SettingsFile);
        Sandbox.ResetSettings();
    }

    [Fact]
    public void Defaults_are_sensible()
    {
        var s = new AppSettings();
        Assert.Equal(8192, s.ContextTokens);
        Assert.Equal(2048, s.MaxAnswerTokens);
        Assert.False(s.EditorDoor.Enabled);
        Assert.Equal(41417, s.EditorDoor.Port);
        Assert.True(s.MemoryEnabled);
        Assert.NotEqual(EditorDoorSettings.NewToken(), EditorDoorSettings.NewToken());
        Assert.Equal("CatalogHf", new PendingDownload().Blurb);
        var json = JsonSerializer.Serialize(s);
        Assert.DoesNotContain("\"ModelPath\"", JsonSerializer.Serialize(s, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull }));
        Assert.Contains("EditorDoor", json);
    }
}

public class PathsTests
{
    [Fact]
    public void Everything_lives_under_the_root()
    {
        Assert.Equal(Sandbox.Root, Paths.Root);
        Assert.Equal(Path.Combine(Sandbox.Root, "models"), Paths.DefaultModels);
        Assert.Equal(Path.Combine(Sandbox.Root, "engine"), Paths.Engine);
        Assert.Equal(Path.Combine(Sandbox.Root, "threads"), Paths.Threads);
        Assert.Equal(Path.Combine(Sandbox.Root, "logs", "engine.log"), Paths.EngineLog);
        Assert.Equal(Path.Combine(Sandbox.Root, "settings.json"), Paths.SettingsFile);
    }

    [Fact]
    public void Models_follows_the_setting_and_ensure_creates_folders()
    {
        Sandbox.ResetSettings();
        Assert.Equal(Paths.DefaultModels, Paths.Models);
        var other = Path.Combine(Sandbox.Root, "otros-modelos");
        AppSettings.Current.ModelsFolder = other;
        Assert.Equal(other, Paths.Models);
        Paths.Ensure();
        Assert.True(Directory.Exists(other));
        Assert.True(Directory.Exists(Paths.Engine));
        Assert.True(Directory.Exists(Paths.Logs));
        AppSettings.Current.ModelsFolder = null;
    }
}
