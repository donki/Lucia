using System.Text.Json.Nodes;
using SocLucia.Agent;
using SocLucia.Services;

namespace SocLucia.Tests;

public class ToolBoxTests
{
    public ToolBoxTests()
    {
        Sandbox.ResetSettings();
        Sandbox.ClearDocuments();
    }

    private static JsonObject Args(params (string Key, string Value)[] pairs)
    {
        var o = new JsonObject();
        foreach (var (k, v) in pairs) o[k] = v;
        return o;
    }

    private static Task<string> Run(string tool, params (string, string)[] args)
        => ToolBox.Find(tool)!.Run(Args(args), CancellationToken.None);

    // ------------------------------------------------------------------ catalogo y definiciones

    [Fact]
    public void Names_are_unique_and_findable()
    {
        var all = ToolBox.All.Concat(ToolBox.Free).Append(ToolBox.Info).ToList();
        Assert.Equal(all.Count, all.Select(t => t.Name).Distinct().Count());
        foreach (var t in all)
            Assert.Same(t, ToolBox.Find(t.Name));
        Assert.Null(ToolBox.Find("no_such_tool"));
    }

    [Fact]
    public void Every_tool_declares_its_parameters_as_required_strings_plus_optional_reason()
    {
        foreach (var t in ToolBox.All.Concat(ToolBox.Free).Append(ToolBox.Info))
        {
            Assert.Equal("object", t.Parameters["type"]!.GetValue<string>());
            var props = t.Parameters["properties"]!.AsObject();
            Assert.True(props.ContainsKey("reason"), t.Name);
            foreach (var (_, p) in props) Assert.Equal("string", p!["type"]!.GetValue<string>());
            var required = t.Parameters["required"]?.AsArray().Select(n => n!.GetValue<string>()).ToList() ?? [];
            Assert.DoesNotContain("reason", required);
            Assert.All(required, r => Assert.True(props.ContainsKey(r)));
            Assert.False(string.IsNullOrWhiteSpace(t.Description));
        }
        Assert.Null(ToolBox.Info.Parameters["required"]);
    }

    [Fact]
    public void Definitions_leave_out_denied_resources_and_memory_when_off()
    {
        var s = Sandbox.ResetSettings();
        static List<string> Names(JsonArray defs) => defs.Select(d => d!["function"]!["name"]!.GetValue<string>()).ToList();

        var all = Names(ToolBox.Definitions(s));
        Assert.Contains(ToolBox.RunCommand, all);
        Assert.Contains(ToolBox.SystemInfo, all);
        Assert.Contains(ToolBox.Remember, all);
        Assert.Contains(ToolBox.WebSearch, all);
        Assert.All(ToolBox.Definitions(s), d => Assert.Equal("function", d!["type"]!.GetValue<string>()));

        s.SetPermission(Resource.Commands, Permission.Deny);
        s.InternetAccess = false;
        s.MemoryEnabled = false;
        var some = Names(ToolBox.QuestionDefinitions(s));
        Assert.DoesNotContain(ToolBox.RunCommand, some);
        Assert.DoesNotContain(ToolBox.WebSearch, some);
        Assert.DoesNotContain(ToolBox.FetchUrl, some);
        Assert.DoesNotContain(ToolBox.Remember, some);
        Assert.Contains(ToolBox.ReadFile, some);
        Assert.Contains(ToolBox.ListDocuments, some);

        // Las definiciones son copias: tocarlas no cambia la herramienta.
        var defs = ToolBox.Definitions(s);
        defs[0]!["function"]!["parameters"]!["type"] = "cambiado";
        Assert.Equal("object", ToolBox.All[1].Parameters["type"]!.GetValue<string>());
    }

    [Fact]
    public void Prompts_mention_the_work_folder_and_allowed_tools()
    {
        var s = Sandbox.ResetSettings();
        s.SetPermission(Resource.Apps, Permission.Deny);
        var system = ToolBox.SystemPrompt(s);
        Assert.Contains(Sandbox.Work, system);
        Assert.Contains("`run_command`", system);
        Assert.Contains("`system_info`", system);
        Assert.DoesNotContain("`open_item`", system);
        Assert.Contains(Sandbox.Work, ToolBox.QuestionPrompt(s));
        Assert.Contains("web_search", ToolBox.InternetPrompt);
    }

    [Fact]
    public void Context_prompt_carries_memory_and_documents()
    {
        var s = Sandbox.ResetSettings();
        UserMemory.Clear();
        Assert.Contains("remember", ToolBox.ContextPrompt(s, "hola"));
        Assert.DoesNotContain("What you know", ToolBox.ContextPrompt(s, "hola"));
        UserMemory.Add("Se llama Zacarias");
        File.WriteAllText(Path.Combine(Sandbox.Documents, "receta.md"), "La paella valenciana lleva arroz, pollo, conejo y garrofo.");
        var prompt = ToolBox.ContextPrompt(s, "¿qué lleva la paella valenciana?");
        Assert.Contains("Se llama Zacarias", prompt);
        Assert.Contains("receta.md", prompt);
        s.MemoryEnabled = false;
        Assert.DoesNotContain("Zacarias", ToolBox.ContextPrompt(s, "paella valenciana"));
        Sandbox.ClearDocuments();
        Assert.Equal(string.Empty, ToolBox.ContextPrompt(s, "nada"));
        UserMemory.Clear();
    }

    // ------------------------------------------------------------------ Detail (lo que se enseña al pedir permiso)

    [Fact]
    public void Detail_shows_the_relevant_argument_per_resource()
    {
        Assert.Equal("dir", ToolBox.Find(ToolBox.RunCommand)!.Detail(Args(("command", "dir"))));
        Assert.Equal("a.txt", ToolBox.Find(ToolBox.ReadFile)!.Detail(Args(("path", "a.txt"))));
        Assert.Equal("C:\\x", ToolBox.Find(ToolBox.FindFiles)!.Detail(Args(("folder", "C:\\x"), ("pattern", "*.cs"))));
        Assert.Equal("gatos", ToolBox.Find(ToolBox.WebSearch)!.Detail(Args(("query", "gatos"))));
        Assert.Equal("https://a.b", ToolBox.Find(ToolBox.FetchUrl)!.Detail(Args(("url", "https://a.b"))));
        Assert.Equal("notepad", ToolBox.Find(ToolBox.OpenItem)!.Detail(Args(("target", "notepad"))));
        Assert.Equal("hola", ToolBox.Find(ToolBox.WriteClipboard)!.Detail(Args(("text", "hola"))));
        Assert.Equal(string.Empty, ToolBox.Find(ToolBox.ReadClipboard)!.Detail(Args()));
        Assert.Equal("dato", ToolBox.Find(ToolBox.Remember)!.Detail(Args(("fact", "dato"))));
        Assert.Equal("Resumen · daily 09:00", ToolBox.Find(ToolBox.ScheduleTask)!.Detail(Args(("title", "Resumen"), ("when", "daily 09:00"))));
        Assert.Equal("ab12", ToolBox.Find(ToolBox.DeleteTask)!.Detail(Args(("id", "ab12"))));
        Assert.Equal("x.md", ToolBox.Find(ToolBox.SaveDocument)!.Detail(Args(("name", "x.md"))));
        Assert.Equal(string.Empty, ToolBox.Find(ToolBox.SystemInfo)!.Detail(Args()));
        Assert.Equal(string.Empty, new Tool("x", (Resource)99, "", new JsonObject(), (_, _) => Task.FromResult("")).Detail(Args()));
    }

    // ------------------------------------------------------------------ ficheros

    [Fact]
    public async Task Write_then_read_a_relative_file_in_the_work_folder()
    {
        var result = await Run(ToolBox.WriteFile, ("path", "sub\\nota.txt"), ("content", "hola ñandú"));
        var path = Path.Combine(Sandbox.Work, "sub", "nota.txt");
        Assert.Contains(path, result);
        Assert.Equal("hola ñandú", File.ReadAllText(path));
        Assert.NotEqual(0xEF, File.ReadAllBytes(path)[0]);   // sin BOM
        Assert.Equal("hola ñandú", await Run(ToolBox.ReadFile, ("path", "\"sub\\nota.txt\"")));
        Assert.Equal("hola ñandú", await Run(ToolBox.ReadFile, ("path", path)));
    }

    [Fact]
    public async Task Read_file_expands_environment_variables_and_rejects_empty_paths()
    {
        var file = Path.Combine(Sandbox.Root, "env.txt");
        File.WriteAllText(file, "variable");
        Environment.SetEnvironmentVariable("LUCIA_TEST_DIR", Sandbox.Root);
        try { Assert.Equal("variable", await Run(ToolBox.ReadFile, ("path", "%LUCIA_TEST_DIR%\\env.txt"))); }
        finally { Environment.SetEnvironmentVariable("LUCIA_TEST_DIR", null); }
        await Assert.ThrowsAsync<ArgumentException>(() => Run(ToolBox.ReadFile, ("path", "   ")));
        await Assert.ThrowsAsync<ArgumentException>(() => ToolBox.Find(ToolBox.ReadFile)!.Run(new JsonObject(), CancellationToken.None));
        await Assert.ThrowsAsync<FileNotFoundException>(() => Run(ToolBox.ReadFile, ("path", "no-existe.txt")));
    }

    [Fact]
    public async Task Long_files_are_cut_in_the_middle()
    {
        var text = new string('a', 7000) + new string('z', 7000);
        File.WriteAllText(Path.Combine(Sandbox.Work, "largo.txt"), text);
        var read = await Run(ToolBox.ReadFile, ("path", "largo.txt"));
        Assert.StartsWith(new string('a', 6000), read);
        Assert.EndsWith(new string('z', 6000), read);
        Assert.Contains("…[cut]…", read);
        Assert.True(read.Length < text.Length);
        var exact = new string('b', 12_000);
        File.WriteAllText(Path.Combine(Sandbox.Work, "justo.txt"), exact);
        Assert.Equal(exact, await Run(ToolBox.ReadFile, ("path", "justo.txt")));
    }

    [Fact]
    public async Task Read_file_extracts_pdf_and_docx()
    {
        var pdf = Path.Combine(Sandbox.Work, "doc.pdf");
        File.WriteAllBytes(pdf, PdfFactory.WithPages("Factura numero 42"));
        Assert.Contains("Factura numero 42", await Run(ToolBox.ReadFile, ("path", "doc.pdf")));
        var docx = Path.Combine(Sandbox.Work, "doc.DOCX");
        Docx.Write(docx, "# Titulo\nCuerpo del texto");
        var text = await Run(ToolBox.ReadFile, ("path", "doc.DOCX"));
        Assert.Contains("Titulo", text);
        Assert.Contains("Cuerpo del texto", text);
    }

    [Fact]
    public async Task List_folder_shows_dirs_first_then_files_with_sizes()
    {
        var folder = Sandbox.NewFolder("lista");
        Directory.CreateDirectory(Path.Combine(folder, "Zeta"));
        Directory.CreateDirectory(Path.Combine(folder, "alfa"));
        File.WriteAllText(Path.Combine(folder, "b.txt"), "12345");
        File.WriteAllText(Path.Combine(folder, "A.txt"), "1");
        var lines = (await Run(ToolBox.ListFolder, ("path", folder))).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("size (bytes)  name", lines[0]);
        Assert.Equal("[dir]  alfa", lines[1]);
        Assert.Equal("[dir]  Zeta", lines[2]);
        Assert.EndsWith("A.txt", lines[3]);
        Assert.Matches(@"^\s+5  b\.txt$", lines[4]);
        Assert.Equal("[empty folder]", await Run(ToolBox.ListFolder, ("path", Sandbox.NewFolder("vacia"))));
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => Run(ToolBox.ListFolder, ("path", Path.Combine(folder, "no"))));
    }

    [Fact]
    public async Task Find_files_is_recursive_case_insensitive_and_capped()
    {
        var folder = Sandbox.NewFolder("buscar");
        Directory.CreateDirectory(Path.Combine(folder, "a", "b"));
        File.WriteAllText(Path.Combine(folder, "a", "b", "Informe.PDF"), "");
        File.WriteAllText(Path.Combine(folder, "otro.txt"), "");
        var found = await Run(ToolBox.FindFiles, ("folder", folder), ("pattern", "*.pdf"));
        Assert.Contains("Informe.PDF", found);
        Assert.DoesNotContain("otro.txt", found);
        Assert.Equal("[no matches]", await Run(ToolBox.FindFiles, ("folder", folder), ("pattern", "*.xyz")));
        var all = await Run(ToolBox.FindFiles, ("folder", folder), ("pattern", ""));
        Assert.Contains("otro.txt", all);

        var many = Sandbox.NewFolder("muchos");
        for (var i = 0; i < 205; i++) File.WriteAllText(Path.Combine(many, $"f{i}.log"), "");
        var capped = await Run(ToolBox.FindFiles, ("folder", many), ("pattern", "*.log"));
        Assert.EndsWith("…[more]" + Environment.NewLine, capped);
        Assert.Equal(201, capped.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length);
    }

    // ------------------------------------------------------------------ documentos

    [Fact]
    public async Task Save_list_and_read_documents()
    {
        Assert.StartsWith("[no documents in ", await Run(ToolBox.ListDocuments));
        var saved = await Run(ToolBox.SaveDocument, ("name", " notas "), ("content", "primera linea"));
        Assert.Equal("Saved: " + Path.Combine(Sandbox.Documents, "notas.md"), saved);
        await Run(ToolBox.SaveDocument, ("name", "informe.docx"), ("content", "# Informe\nTodo **bien**"));
        var list = await Run(ToolBox.ListDocuments);
        Assert.Contains("notas.md", list);
        Assert.Contains("informe.docx", list);
        Assert.Equal("primera linea", await Run(ToolBox.ReadDocument, ("name", "NOTAS.md")));
        Assert.Contains("Todo bien", await Run(ToolBox.ReadDocument, ("name", "informe.docx")));

        Directory.CreateDirectory(Path.Combine(Sandbox.Documents, "sub"));
        File.WriteAllText(Path.Combine(Sandbox.Documents, "sub", "dentro.txt"), "anidado");
        Assert.Equal("anidado", await Run(ToolBox.ReadDocument, ("name", "sub\\dentro.txt")));
        Assert.Equal("anidado", await Run(ToolBox.ReadDocument, ("name", "dentro.txt")));
        await Assert.ThrowsAsync<FileNotFoundException>(() => Run(ToolBox.ReadDocument, ("name", "falta.md")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("..\\fuera.md")]
    [InlineData("a/b.md")]
    [InlineData("a:b.md")]
    [InlineData("..")]
    public async Task Save_document_rejects_bad_names(string name)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Run(ToolBox.SaveDocument, ("name", name), ("content", "x")));
        Assert.Empty(Directory.GetFiles(Sandbox.Root, "fuera.md"));
    }

    // ------------------------------------------------------------------ memoria, tareas, varios

    [Fact]
    public async Task Remember_saves_once_and_respects_the_switch()
    {
        UserMemory.Clear();
        Assert.Equal("Saved.", await Run(ToolBox.Remember, ("fact", "Le gusta el cafe solo.")));
        Assert.Equal("Already known.", await Run(ToolBox.Remember, ("fact", "le gusta el cafe solo")));
        Assert.Equal("Already known.", await Run(ToolBox.Remember, ("fact", "a")));   // demasiado corto: no se guarda
        AppSettings.Current.MemoryEnabled = false;
        Assert.Equal("Memory is turned off in the app settings.", await Run(ToolBox.Remember, ("fact", "Otra cosa")));
        Assert.Single(UserMemory.All);
        UserMemory.Clear();
    }

    [Fact]
    public async Task Schedule_list_and_delete_tasks_through_the_tools()
    {
        foreach (var t in Scheduler.All.ToList()) Scheduler.Remove(t.Id);
        Assert.Equal("[no scheduled tasks]", await Run(ToolBox.ListTasks));
        var result = await Run(ToolBox.ScheduleTask, ("title", "Resumen"), ("prompt", "Resume las noticias"), ("when", "every 2h"));
        Assert.StartsWith("Scheduled [", result);
        var id = Scheduler.All.Single().Id;
        Assert.Contains($"[{id}] Resumen", await Run(ToolBox.ListTasks));
        Scheduler.SetEnabled(id, false);
        Assert.EndsWith("— off", await Run(ToolBox.ListTasks));
        Assert.Equal("Deleted.", await Run(ToolBox.DeleteTask, ("id", $"  {id} ")));
        Assert.Empty(Scheduler.All);
        await Assert.ThrowsAsync<ArgumentException>(() => Run(ToolBox.ScheduleTask, ("title", "x"), ("prompt", "y"), ("when", "mañana")));
    }

    [Fact]
    public async Task Internet_tools_reject_bad_input_without_going_online()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Run(ToolBox.WebSearch, ("query", "  ")));
        await Assert.ThrowsAsync<ArgumentException>(() => Run(ToolBox.FetchUrl, ("url", "file:///C:/Windows/win.ini")));
        await Assert.ThrowsAsync<ArgumentException>(() => Run(ToolBox.FetchUrl, ("url", "no es una url")));
        await Assert.ThrowsAsync<ArgumentException>(() => Run(ToolBox.FetchUrl, ("url", "ftp://example.com/x")));
    }

    [Fact]
    public async Task Fetch_url_reduces_html_to_text_and_hides_binaries()
    {
        using var server = new LocalServer(ctx => ctx.Request.Url!.AbsolutePath switch
        {
            "/page" => LocalServer.Write(ctx, 200, "text/html; charset=utf-8",
                "<html><head><style>p{}</style><script>alert(1)</script></head><body><h1>Título</h1><p>Uno &amp; dos</p>\n\n\n<div>Tres</div><br/>fin</body></html>"),
            "/data" => LocalServer.Write(ctx, 200, "application/json", "{\"a\":1}"),
            "/bin" => LocalServer.Write(ctx, 200, "application/octet-stream", "\0\0\0"),
            _ => LocalServer.Write(ctx, 404, "text/plain", "missing"),
        });
        var page = await Run(ToolBox.FetchUrl, ("url", server.BaseUrl + "/page"));
        Assert.StartsWith("[200]\n", page);
        Assert.Contains("Título", page);
        Assert.Contains("Uno & dos", page);
        Assert.Contains("Tres", page);
        Assert.DoesNotContain("alert", page);
        Assert.DoesNotContain("<", page);
        Assert.DoesNotContain("\n\n\n", page);
        Assert.Equal("[200]\n{\"a\":1}", await Run(ToolBox.FetchUrl, ("url", server.BaseUrl + "/data")));
        Assert.Equal("[200] application/octet-stream, 3 bytes (binary content not shown)", await Run(ToolBox.FetchUrl, ("url", server.BaseUrl + "/bin")));
        Assert.Equal("[404]\nmissing", await Run(ToolBox.FetchUrl, ("url", server.BaseUrl + "/nada")));
    }

    [Fact]
    public void Search_results_unwrap_addresses_skip_ads_and_clean_text()
    {
        static string Result(string href, string title, string snippet) =>
            $"<div><a rel=\"nofollow\" class=\"result__a\" href=\"{href}\">{title}</a><div><a class=\"result__snippet\" href=\"x\">{snippet}</a></div></div>\n";
        var html = Result("//duckduckgo.com/l/?uddg=https%3A%2F%2Fes.wikipedia.org%2Fwiki%2FPaella&amp;rut=abc", "<b>Paella</b> - Wikipedia", "La  paella es un plato &amp; arroz")
                   + Result("https://duckduckgo.com/y.js?ad=1", "Anuncio", "compra")
                   + Result("https://ejemplo.com/receta", "Receta", "Paso <b>1</b>");
        var text = ToolBox.SearchResultsText(html);
        Assert.Equal(
            "1. Paella - Wikipedia\n   https://es.wikipedia.org/wiki/Paella\n   La paella es un plato & arroz" + Environment.NewLine +
            "2. Receta\n   https://ejemplo.com/receta\n   Paso 1" + Environment.NewLine, text);
        Assert.Equal("[no results]", ToolBox.SearchResultsText("<html>nada</html>"));
        var many = string.Concat(Enumerable.Range(0, 12).Select(i => Result($"https://r{i}.com", $"T{i}", "s")));
        Assert.StartsWith("8. T7", ToolBox.SearchResultsText(many).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)[^1]);
    }

    [Fact]
    public async Task Generate_image_refuses_an_empty_prompt_before_touching_the_engine()
    {
        Assert.Equal("[error] empty prompt", await Run(ToolBox.GenerateImage, ("prompt", "  ")));
        ToolBox.ImageProgress = null;
        Assert.Null(ToolBox.ImageProgress);
    }

    [Fact]
    public async Task System_info_describes_this_pc()
    {
        var info = await ToolBox.Info.Run(new JsonObject(), CancellationToken.None);
        Assert.Contains("Windows:", info);
        Assert.Contains("Processor:", info);
        Assert.Contains("Now:", info);
        Assert.Contains(Sandbox.Work, info);
    }

    [Fact]
    public async Task Run_command_runs_powershell_in_the_work_folder()
    {
        var result = await Run(ToolBox.RunCommand, ("command", "Write-Output (Get-Location).Path; exit 3"));
        Assert.StartsWith("[exit code 3]\n", result);
        Assert.Contains(Sandbox.Work, result);
    }

    [Fact]
    public void Log_appends_one_line_per_action()
    {
        var log = Path.Combine(Paths.Logs, "actions.log");
        var before = File.Exists(log) ? File.ReadAllLines(log).Length : 0;
        ToolBox.Log(ToolBox.Find(ToolBox.RunCommand)!, "linea1\nlinea2", "allowed");
        var lines = File.ReadAllLines(log);
        Assert.Equal(before + 1, lines.Length);
        Assert.EndsWith("run_command [allowed] linea1 linea2", lines[^1]);
    }
}
