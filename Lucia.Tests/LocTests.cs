using System.Reflection;
using System.Text.RegularExpressions;
using SocLucia.Localization;

namespace SocLucia.Tests;

public class LocTests
{
    private static Dictionary<string, string> Table(string name) =>
        (Dictionary<string, string>)typeof(Loc).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

    [Fact]
    public void Spanish_and_English_have_the_same_keys()
    {
        var en = Table("English");
        var es = Table("Spanish");
        Assert.Empty(en.Keys.Except(es.Keys));
        Assert.Empty(es.Keys.Except(en.Keys));
        Assert.True(en.Count > 100);
    }

    [Fact]
    public void No_text_is_empty()
    {
        foreach (var (key, value) in Table("English").Concat(Table("Spanish")))
            Assert.False(string.IsNullOrWhiteSpace(value), key);
    }

    [Fact]
    public void Placeholders_match_between_languages()
    {
        var en = Table("English");
        var es = Table("Spanish");
        foreach (var key in en.Keys)
        {
            static string Holes(string s) => string.Join(",", Regex.Matches(s, @"\{\d+[^}]*\}").Select(m => m.Value).Distinct().Order());
            Assert.True(Holes(en[key]) == Holes(es[key]), $"{key}: «{en[key]}» / «{es[key]}»");
        }
    }

    [Fact]
    public void Every_key_used_in_code_and_xaml_exists()
    {
        var root = RepoRoot();
        var en = Table("English");
        var used = new HashSet<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                     .Where(f => (f.EndsWith(".cs") || f.EndsWith(".xaml")) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                                 && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains("Lucia.Tests") && !f.Contains("vscode")))
        {
            var text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, @"Loc\.(?:Get|Format)\(""([A-Za-z0-9_]+)""\s*[,)]"))
                used.Add(m.Groups[1].Value);
            if (!file.EndsWith(".xaml")) continue;
            foreach (Match m in Regex.Matches(text, @"\{loc:T\s+([A-Za-z0-9_]+)\}"))
                used.Add(m.Groups[1].Value);
        }
        Assert.NotEmpty(used);
        Assert.Empty(used.Where(k => !en.ContainsKey(k)));
    }

    [Fact]
    public void Every_resource_that_asks_permission_has_its_texts()
    {
        var en = Table("English");
        foreach (var r in Enum.GetValues<SocLucia.Agent.Resource>().Where(r => r != SocLucia.Agent.Resource.Free))
            foreach (var prefix in new[] { "Perm_", "PermHint_", "PermIntro_" })
                Assert.True(en.ContainsKey(prefix + r), prefix + r);
    }

    [Fact]
    public void Get_follows_the_language_and_falls_back()
    {
        var before = Loc.Language;
        var changes = 0;
        void OnChange() => changes++;
        Loc.LanguageChanged += OnChange;
        try
        {
            Loc.Use("es");
            Loc.Use("en");
            Assert.Equal("Close", Loc.Get("Close"));
            Loc.Use("es");
            Assert.Equal("Cancelar", Loc.Get("Cancel"));
            Assert.Equal("es", Loc.Language);
            Loc.Use("fr");          // no admitido: se queda como esta
            Assert.Equal("es", Loc.Language);
            Loc.Use("es");          // el mismo: no avisa
            Assert.Equal("NoExisteEstaClave", Loc.Get("NoExisteEstaClave"));
            Assert.True(changes >= 2);
            Assert.Contains("7", Loc.Format("EngineExited", 7));
        }
        finally
        {
            Loc.LanguageChanged -= OnChange;
            Loc.Use(before);
        }
    }

    [Fact]
    public void Markup_extension_returns_the_text()
    {
        Loc.Use("en");
        Assert.Equal("Close", new TExtension("Close").ProvideValue(null!));
        Assert.Equal(string.Empty, new TExtension().Key);
    }

    internal static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "sOCLucia.csproj")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new DirectoryNotFoundException("sOCLucia.csproj");
    }
}
