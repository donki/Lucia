using System.Text.Json;
using System.Text.Json.Nodes;

if (int.TryParse(Environment.GetEnvironmentVariable("FAKE_MCP_EXIT"), out var code))
    return code;

var noTools = Environment.GetEnvironmentVariable("FAKE_MCP_NOTOOLS") is { Length: > 0 };
var @in = Console.In;
string? line;
while ((line = @in.ReadLine()) is not null)
{
    line = line.Trim();
    if (line.Length == 0 || line[0] != '{') continue;
    JsonObject msg;
    try { msg = (JsonObject)JsonNode.Parse(line)!; } catch { continue; }
    var method = msg["method"]?.GetValue<string>();
    var id = msg["id"];
    if (method == "initialize")
        Reply(id, new JsonObject { ["protocolVersion"] = "2025-06-18", ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() }, ["serverInfo"] = new JsonObject { ["name"] = "fake-mcp", ["version"] = "1.0" } });
    else if (method == "notifications/initialized")
        continue;   // notificacion: sin respuesta
    else if (method == "tools/list")
    {
        var tools = new JsonArray();
        if (!noTools)
        {
            tools.Add(new JsonObject { ["name"] = "echo", ["description"] = "Return the text in upper case", ["inputSchema"] = new JsonObject { ["$schema"] = "http://json-schema.org/draft-07/schema#", ["type"] = "object", ["properties"] = new JsonObject { ["text"] = new JsonObject { ["type"] = "string" } }, ["required"] = new JsonArray("text") } });
            tools.Add(new JsonObject { ["name"] = "boom", ["description"] = "Always fails", ["inputSchema"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() } });
        }
        Reply(id, new JsonObject { ["tools"] = tools });
    }
    else if (method == "tools/call")
    {
        var name = msg["params"]?["name"]?.GetValue<string>();
        if (name == "echo")
        {
            var text = msg["params"]?["arguments"]?["text"]?.GetValue<string>() ?? "";
            Reply(id, new JsonObject { ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text.ToUpperInvariant() }) });
        }
        else if (name == "boom")
            Reply(id, new JsonObject { ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "kaboom" }), ["isError"] = true });
        else
            Error(id, -32602, "unknown tool");
    }
    else if (id is not null)
        Error(id, -32601, "method not found");
}
return 0;

void Reply(JsonNode? id, JsonObject result)
    => Console.WriteLine(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["result"] = result }.ToJsonString());
void Error(JsonNode? id, int ecode, string message)
    => Console.WriteLine(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["error"] = new JsonObject { ["code"] = ecode, ["message"] = message } }.ToJsonString());
