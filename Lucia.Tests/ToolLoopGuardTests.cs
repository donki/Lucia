using SocLucia.Agent;
using SocLucia.Engine;

namespace SocLucia.Tests;

public class ToolLoopGuardTests
{
    private static ToolCall Call(string name, string args) => new() { Id = Guid.NewGuid().ToString("N"), Name = name, Arguments = args };

    private const string Failing = "{\"command\":\"Get-Date | | Out-Null\",\"reason\":\"probar\"}";
    private const string Error = "[exit code 1]\nAn empty pipe element is not allowed.\n    + CategoryInfo          : ParserError";

    [Fact]
    public void Una_accion_identica_que_fallo_no_se_repite_y_si_insiste_se_para()
    {
        var guard = new ToolLoopGuard(15);
        var first = Call("run_command", Failing);
        Assert.Equal(GuardVerdict.Run, guard.Before(first));
        Assert.False(guard.After(first, "run_command  Get-Date", Error));
        // La misma, con otros espacios, mayusculas y otro «porque»: es la misma.
        var again = Call("run_command", "{\"reason\":\"otra vez\",\"command\":\"get-date ||  out-null\"}");
        Assert.Equal(GuardVerdict.Repeat, guard.Before(again));
        Assert.Contains("An empty pipe element is not allowed", guard.RepeatAnswer(again));
        Assert.Contains("Do not repeat it unchanged", guard.RepeatAnswer(again));
        Assert.Equal(GuardVerdict.Stop, guard.Before(Call("run_command", Failing)));
        Assert.Equal(GuardStop.Repeats, guard.Stopped);
        Assert.Equal(1, guard.Calls);   // solo se ejecuto una vez
        Assert.Equal("run_command  Get-Date", guard.LastFailedLabel);
        Assert.Equal("An empty pipe element is not allowed.", guard.LastError);
    }

    [Fact]
    public void El_tope_de_acciones_por_respuesta_para()
    {
        var guard = new ToolLoopGuard(3);
        for (var i = 0; i < 3; i++)
        {
            var call = Call("system_info", "{}");   // identica pero bien: no es repeticion de un fallo
            Assert.Equal(GuardVerdict.Run, guard.Before(call));
            Assert.False(guard.After(call, "system_info", "Windows: 11"));
        }
        Assert.Equal(GuardVerdict.Stop, guard.Before(Call("system_info", "{}")));
        Assert.Equal(GuardStop.Cap, guard.Stopped);
        Assert.Equal(GuardVerdict.Stop, guard.Before(Call("list_folder", "{}")));   // parado, parado
        Assert.Equal(ToolLoopGuard.MinMaxCalls, new ToolLoopGuard(-3).MaxCalls);
        Assert.False(new ToolLoopGuard(5).Unlimited);
        Assert.Equal(ToolLoopGuard.MaxMaxCalls, new ToolLoopGuard(1000).MaxCalls);
    }

    [Fact]
    public void Tres_fallos_seguidos_de_la_misma_herramienta_paran_aunque_cambie_la_orden()
    {
        var guard = new ToolLoopGuard(15);
        for (var i = 1; i <= 2; i++)
        {
            var call = Call("read_file", $"{{\"path\":\"no-esta-{i}.txt\"}}");
            Assert.Equal(GuardVerdict.Run, guard.Before(call));
            Assert.False(guard.After(call, "read_file", "[error] Could not find file"));
        }
        // Una que va bien de otra herramienta no corta la racha de la primera…
        var other = Call("system_info", "{}");
        guard.Before(other);
        guard.After(other, "system_info", "ok");
        // …y una que va bien de la misma, si.
        var ok = Call("read_file", "{\"path\":\"si-esta.txt\"}");
        guard.Before(ok);
        guard.After(ok, "read_file", "contenido");
        var third = Call("read_file", "{\"path\":\"no-esta-3.txt\"}");
        guard.Before(third);
        Assert.False(guard.After(third, "read_file", "[error] Could not find file"));
        for (var i = 4; i <= 5; i++)
        {
            var call = Call("read_file", $"{{\"path\":\"no-esta-{i}.txt\"}}");
            Assert.Equal(GuardVerdict.Run, guard.Before(call));
            var stop = guard.After(call, "read_file", "[error] Could not find file");
            Assert.Equal(i == 5, stop);
        }
        Assert.Equal(GuardStop.Failures, guard.Stopped);
        Assert.Equal(GuardVerdict.Stop, guard.Before(Call("read_file", "{\"path\":\"x\"}")));
    }

    [Theory]
    [InlineData("[exit code 1]\nalgo", true)]
    [InlineData("[exit code -1]\nalgo", true)]
    [InlineData("[exit code 0]\ntodo bien", false)]
    [InlineData("[exit code 0]\nAt line:1 char:5\n+ CategoryInfo : ParserError", true)]
    [InlineData("[timed out after 3 minutes]\n[exit code -1]", true)]
    [InlineData("[error] Access denied", true)]
    [InlineData("Saved: C:\\docs\\a.md", false)]
    [InlineData("The user declined this action.", false)]
    public void Que_cuenta_como_fallo(string output, bool failure) => Assert.Equal(failure, ToolLoopGuard.IsFailure(output));

    [Fact]
    public void La_huella_ignora_el_porque_los_espacios_y_las_mayusculas()
    {
        Assert.Equal(ToolLoopGuard.Key(Call("run_command", "{\"command\":\"Get-Date\",\"reason\":\"a\"}")),
                     ToolLoopGuard.Key(Call("run_command", "{ \"reason\": \"b\", \"command\": \"get-date\" }")));
        Assert.NotEqual(ToolLoopGuard.Key(Call("run_command", "{\"command\":\"Get-Date\"}")),
                        ToolLoopGuard.Key(Call("run_command", "{\"command\":\"Get-Location\"}")));
        Assert.NotEqual(ToolLoopGuard.Key(Call("read_file", "{\"path\":\"a\"}")), ToolLoopGuard.Key(Call("list_folder", "{\"path\":\"a\"}")));
        Assert.Equal("x|nojson", ToolLoopGuard.Key(Call("x", "no json")));
    }

    [Fact]
    public void Sin_limite_no_para_por_numero_pero_si_por_repetir_lo_que_fallo()
    {
        var guard = new ToolLoopGuard(ToolLoopGuard.UnlimitedCalls);
        Assert.True(guard.Unlimited);
        for (var i = 0; i < 500; i++)
        {
            var call = Call("read_file", $$"""{"path":"f{{i}}.txt"}""");
            Assert.Equal(GuardVerdict.Run, guard.Before(call));
            Assert.False(guard.After(call, "read_file", "contenido"));
        }
        Assert.Equal(500, guard.Calls);
        Assert.Equal(GuardStop.None, guard.Stopped);
        // Los otros frenos siguen: tres fallos seguidos de la misma herramienta paran
        for (var i = 0; i < 3; i++)
        {
            var bad = Call("run_command", $$"""{"command":"roto {{i}}"}""");
            guard.Before(bad);
            guard.After(bad, "run_command", "[error] no existe");
        }
        Assert.Equal(GuardStop.Failures, guard.Stopped);
    }
}
