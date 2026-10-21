using System.Net.Http.Headers;
using System.Text;
using SocLucia.Editor;
using SocLucia.Engine;
using SocLucia.Services;

namespace SocLucia.Tests;

/// <summary>
/// La puerta de editores de punta a punta: un cliente de verdad por HTTP, la puerta reenvia al motor
/// (llama-server de mentira y red de mentira) y aqui se mira lo que le llega al motor: el tope de
/// tokens de Ajustes si el editor no trae el suyo, el del editor recortado al contexto si lo trae.
/// </summary>
public sealed class DoorForwardTests : UiTest
{
    private readonly EditorDoor _door = new(App.Engine);
    private readonly HttpClient _client = new();

    public DoorForwardTests()
    {
        var s = AppSettings.Current;
        s.EditorDoor.Enabled = true;
        s.EditorDoor.Port = Sandbox.FreePort();
        _door.Apply();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", s.EditorDoor.Token);
    }

    public override void Dispose()
    {
        _door.Dispose();
        _client.Dispose();
        AppSettings.Current.EditorDoor.Enabled = false;
        base.Dispose();
    }

    private async Task<int> Post(string path, string json)
    {
        using var response = await _client.PostAsync($"http://127.0.0.1:{AppSettings.Current.EditorDoor.Port}{path}", new StringContent(json, Encoding.UTF8, "application/json"));
        await response.Content.ReadAsStringAsync();
        return (int)response.StatusCode;
    }

    [Fact]
    public async Task Sin_tope_del_editor_va_el_de_Ajustes_y_con_tope_se_respeta_hasta_el_contexto()
    {
        AppSettings.Current.MaxAnswerTokens = 1500;
        AppSettings.Current.ContextTokens = 4096;
        Assert.Equal(200, await Post("/v1/chat/completions", """{"messages":[{"role":"user","content":"hola"}],"stream":true}"""));
        Assert.Equal(200, await Post("/v1/chat/completions", """{"messages":[],"max_tokens":300}"""));
        Assert.Equal(200, await Post("/v1/chat/completions", """{"messages":[],"max_tokens":100000}"""));
        Assert.Equal(200, await Post("/v1/chat/completions", """{"messages":[],"chat_template_kwargs":{"enable_thinking":true}}"""));
        var sent = Net.ChatRequests.Select(r => r["max_tokens"]!.GetValue<int>()).ToList();
        Assert.Equal([1500, 300, 4096, 4096], sent);   // el de Ajustes; el suyo; el suyo recortado al contexto; razonando, el triple (recortado)
        Assert.False(Net.ChatRequests[0]["chat_template_kwargs"]!["enable_thinking"]!.GetValue<bool>());
        Assert.Contains("/v1/chat/completions: acabada", File.ReadAllText(DiagLog.Current));
    }
}
