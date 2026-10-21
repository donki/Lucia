using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SocLucia.Services.Mlflow;

/// <summary>Un error del servidor MLflow: el estado HTTP y su <c>error_code</c> (RESOURCE_DOES_NOT_EXIST…).</summary>
public sealed class MlflowException(int status, string? code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string? Code { get; } = code;
}

/// <summary>Lo que dice la prueba de conexion.</summary>
public enum MlflowCheck { Ok, OkWillCreate, BadAddress, Unreachable, Timeout, Unauthorized, Forbidden, NotMlflow, ServerError }

/// <summary>
/// La API REST de seguimiento de MLflow (<c>/api/2.0/mlflow/…</c>), la misma de cualquier servidor
/// de MLflow y de Databricks, sin Python ni librerias: experimentos, ejecuciones (runs), lotes de
/// metricas, parametros y etiquetas, y ficheros por el proxy de artefactos del servidor cuando lo
/// tiene. Autenticacion: Bearer si hay token (MLFLOW_TRACKING_TOKEN); si no, basica con usuario y
/// contraseña (MLFLOW_TRACKING_USERNAME/PASSWORD). Los secretos solo viajan en la cabecera.
/// </summary>
public sealed class MlflowApi
{
    private readonly HttpClient _http;
    private readonly Uri _base;
    private readonly AuthenticationHeaderValue? _auth;

    public MlflowApi(HttpClient http, Uri trackingUri, string? username, string? password, string? token)
    {
        _http = http;
        _base = new Uri(trackingUri.AbsoluteUri.TrimEnd('/') + "/");
        if (!string.IsNullOrEmpty(token))
            _auth = new AuthenticationHeaderValue("Bearer", token);
        else if (!string.IsNullOrEmpty(username))
            _auth = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}")));
    }

    /// <summary>Una direccion valida: absoluta, http o https, con servidor.</summary>
    public static bool TryUri(string? text, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(text) || !Uri.TryCreate(text.Trim(), UriKind.Absolute, out var parsed)) return false;
        if (parsed.Scheme is not ("http" or "https") || parsed.Host.Length == 0) return false;
        uri = parsed;
        return true;
    }

    private async Task<JsonNode?> SendAsync(HttpMethod method, string path, JsonNode? body, CancellationToken cancel)
    {
        using var request = new HttpRequestMessage(method, new Uri(_base, path));
        if (_auth is not null) request.Headers.Authorization = _auth;
        if (body is not null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, cancel).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw Error(response.StatusCode, text);
        if (text.Length == 0) return null;
        try { return JsonNode.Parse(text); }
        catch (JsonException) { throw new MlflowException((int)response.StatusCode, "NOT_JSON", "the server did not answer with JSON"); }
    }

    private static MlflowException Error(HttpStatusCode status, string text)
    {
        string? code = null, message = null;
        try
        {
            if (JsonNode.Parse(text) is JsonObject o)
            {
                code = o["error_code"]?.GetValue<string>();
                message = o["message"]?.GetValue<string>();
            }
        }
        catch (Exception) { }
        return new MlflowException((int)status, code, message ?? $"HTTP {(int)status}");
    }

    /// <summary>El id del experimento con ese nombre, o null si no existe.</summary>
    public async Task<string?> FindExperimentAsync(string name, CancellationToken cancel)
    {
        try
        {
            var node = await SendAsync(HttpMethod.Get, "api/2.0/mlflow/experiments/get-by-name?experiment_name=" + Uri.EscapeDataString(name), null, cancel).ConfigureAwait(false);
            return node?["experiment"]?["experiment_id"]?.GetValue<string>() ?? throw new MlflowException(200, "NOT_MLFLOW", "no experiment in the answer");
        }
        catch (MlflowException ex) when (ex.Code == "RESOURCE_DOES_NOT_EXIST" || (ex.Status == 404 && ex.Code is not null))
        {
            return null;
        }
    }

    /// <summary>El id del experimento; si no existe, se crea (y si otro lo crea a la vez, se vuelve a buscar).</summary>
    public async Task<string> EnsureExperimentAsync(string name, CancellationToken cancel)
    {
        if (await FindExperimentAsync(name, cancel).ConfigureAwait(false) is { } id) return id;
        try
        {
            var node = await SendAsync(HttpMethod.Post, "api/2.0/mlflow/experiments/create", new JsonObject { ["name"] = name }, cancel).ConfigureAwait(false);
            return node?["experiment_id"]?.GetValue<string>() ?? throw new MlflowException(200, "NOT_MLFLOW", "no experiment_id");
        }
        catch (MlflowException ex) when (ex.Code == "RESOURCE_ALREADY_EXISTS")
        {
            return await FindExperimentAsync(name, cancel).ConfigureAwait(false) ?? throw new MlflowException(ex.Status, ex.Code, ex.Message);
        }
    }

    public async Task<(string RunId, string? ArtifactUri)> CreateRunAsync(string experimentId, string runName, DateTimeOffset start, IEnumerable<KeyValuePair<string, string>> tags, CancellationToken cancel)
    {
        var body = new JsonObject
        {
            ["experiment_id"] = experimentId,
            ["run_name"] = runName,
            ["start_time"] = start.ToUnixTimeMilliseconds(),
            ["tags"] = Pairs(tags.Append(new("mlflow.runName", runName))),
        };
        var node = await SendAsync(HttpMethod.Post, "api/2.0/mlflow/runs/create", body, cancel).ConfigureAwait(false);
        var info = node?["run"]?["info"];
        var id = info?["run_id"]?.GetValue<string>() ?? info?["run_uuid"]?.GetValue<string>() ?? throw new MlflowException(200, "NOT_MLFLOW", "no run_id");
        return (id, info?["artifact_uri"]?.GetValue<string>());
    }

    /// <summary>Una metrica: nombre, valor, momento y paso (el numero de pregunta).</summary>
    public sealed record Metric(string Key, double Value, DateTimeOffset At, long Step);

    /// <summary>Limites del log-batch de MLflow por llamada.</summary>
    public const int MaxMetrics = 1000, MaxParams = 100, MaxTags = 100, MaxValue = 5000;

    /// <summary>El cuerpo de <c>runs/log-batch</c> (recorta valores largos a lo que admite MLflow).</summary>
    public static JsonObject BatchBody(string runId, IEnumerable<Metric> metrics, IEnumerable<KeyValuePair<string, string>> parameters, IEnumerable<KeyValuePair<string, string>> tags)
    {
        var m = new JsonArray();
        foreach (var x in metrics)
        {
            if (double.IsNaN(x.Value) || double.IsInfinity(x.Value)) continue;
            m.Add(new JsonObject { ["key"] = x.Key, ["value"] = x.Value, ["timestamp"] = x.At.ToUnixTimeMilliseconds(), ["step"] = x.Step });
        }
        return new JsonObject { ["run_id"] = runId, ["metrics"] = m, ["params"] = Pairs(parameters), ["tags"] = Pairs(tags) };
    }

    private static JsonArray Pairs(IEnumerable<KeyValuePair<string, string>> pairs)
        => new(pairs.Select(p => (JsonNode)new JsonObject { ["key"] = p.Key, ["value"] = Cut(p.Value) }).ToArray());

    public static string Cut(string value) => value.Length <= MaxValue ? value : value[..(MaxValue - 1)] + "…";

    public Task LogBatchAsync(string runId, IEnumerable<Metric> metrics, IEnumerable<KeyValuePair<string, string>> parameters, IEnumerable<KeyValuePair<string, string>> tags, CancellationToken cancel)
        => SendAsync(HttpMethod.Post, "api/2.0/mlflow/runs/log-batch", BatchBody(runId, metrics, parameters, tags), cancel);

    public Task FinishRunAsync(string runId, DateTimeOffset end, string status, CancellationToken cancel)
        => SendAsync(HttpMethod.Post, "api/2.0/mlflow/runs/update", new JsonObject { ["run_id"] = runId, ["status"] = status, ["end_time"] = end.ToUnixTimeMilliseconds() }, cancel);

    /// <summary>
    /// Sube un fichero a la ejecucion por el proxy de artefactos (<c>mlflow server</c> con
    /// <c>--serve-artifacts</c>, lo normal desde MLflow 2). Solo si su artifact_uri es
    /// <c>mlflow-artifacts:</c>; si el servidor no lo admite, devuelve false (no es un error).
    /// </summary>
    public async Task<bool> UploadArtifactAsync(string? artifactUri, string fileName, byte[] content, CancellationToken cancel)
    {
        const string scheme = "mlflow-artifacts:";
        if (artifactUri is null || !artifactUri.StartsWith(scheme, StringComparison.Ordinal)) return false;
        var path = artifactUri[scheme.Length..];
        // «mlflow-artifacts://servidor:puerto/ruta»: el servidor sobra (se sube al mismo de siempre).
        if (path.StartsWith("//", StringComparison.Ordinal) && Uri.TryCreate("http:" + path, UriKind.Absolute, out var full)) path = full.AbsolutePath;
        path = path.TrimStart('/');
        if (path.StartsWith("api/2.0/mlflow-artifacts/artifacts/", StringComparison.Ordinal)) path = path["api/2.0/mlflow-artifacts/artifacts/".Length..];
        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri(_base, "api/2.0/mlflow-artifacts/artifacts/" + path.TrimEnd('/') + "/" + Uri.EscapeDataString(fileName)))
        {
            Content = new ByteArrayContent(content) { Headers = { ContentType = new MediaTypeHeaderValue("text/plain") { CharSet = "utf-8" } } },
        };
        if (_auth is not null) request.Headers.Authorization = _auth;
        using var response = await _http.SendAsync(request, cancel).ConfigureAwait(false);
        if (response.IsSuccessStatusCode) return true;
        if ((int)response.StatusCode is 404 or 405 or 501 or 503) return false;
        throw Error(response.StatusCode, await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false));
    }

    /// <summary>
    /// La prueba de conexion: se busca el experimento (sin crear nada). Dice si se llega, si las
    /// credenciales valen, si es un servidor MLflow y si el experimento existe o se creara.
    /// </summary>
    public static async Task<(MlflowCheck Result, string? Detail)> CheckAsync(HttpClient http, string? trackingUri, string experiment, string? username, string? password, string? token, CancellationToken cancel)
    {
        if (!TryUri(trackingUri, out var uri)) return (MlflowCheck.BadAddress, null);
        var api = new MlflowApi(http, uri, username, password, token);
        try
        {
            var id = await api.FindExperimentAsync(experiment, cancel).ConfigureAwait(false);
            return (id is null ? MlflowCheck.OkWillCreate : MlflowCheck.Ok, id);
        }
        catch (MlflowException ex)
        {
            return ex.Status switch
            {
                401 => (MlflowCheck.Unauthorized, null),
                403 => (MlflowCheck.Forbidden, null),
                >= 500 => (MlflowCheck.ServerError, $"HTTP {ex.Status}"),
                _ => (MlflowCheck.NotMlflow, $"HTTP {ex.Status}"),
            };
        }
        catch (TaskCanceledException) when (!cancel.IsCancellationRequested)
        {
            return (MlflowCheck.Timeout, null);
        }
        catch (HttpRequestException ex)
        {
            return (MlflowCheck.Unreachable, ex.HttpRequestError.ToString());
        }
    }
}
