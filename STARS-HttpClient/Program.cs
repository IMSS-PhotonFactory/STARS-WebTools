using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using STARSHttpClient.Http;

namespace STARSHttpClient;

internal static class Program
{
    private const string TargetKey = "node";
    private const string CommandKey = "command";
    private const string TimeoutKey = "timeout";
    private const int Port = 9901;

    private static StarsDispacher starsdispacher = new StarsDispacher();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static async Task Main(string[] args)
    {
        if(!starsdispacher.IsReady)
        {
            Console.WriteLine("STARS connection is not ready.");
            return;
        }

        var listener = new TcpListener(IPAddress.Any, Port);
        listener.Start();
        Console.WriteLine($"Listening on http://localhost:{Port}/ (Ctrl+C to stop)");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
            listener.Stop();
        };

        while (!cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cts.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                break;
            }

            _ = HandleConnectionAsync(client, cts.Token);
        }
    }

    private static async Task HandleConnectionAsync(TcpClient client, CancellationToken ct)
    {
        using var stream = client.GetStream();

        SimpleHttpRequest? request;
        try
        {
            request = await HttpRequestParser.ParseAsync(stream, ct);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return;
        }

        if (request is null) return; // クライアントが接続を閉じた

        var response = new SimpleHttpResponse();

        try
        {
            var segments = request.Path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length == 1 && string.Equals(segments[0], "StarsApi", StringComparison.OrdinalIgnoreCase))
            {
                await RouteExecute(request, response);
            }
            else if (segments.Length == 2 && string.Equals(segments[0], "StarsApi", StringComparison.OrdinalIgnoreCase) && string.Equals(segments[1], "GetValue", StringComparison.OrdinalIgnoreCase))
            {
                await RouteGetValue(request, response);
            }
            else
            {
                WriteJson(response, HttpStatusCode.NotFound, new { error = "Not Found" });
            }
        }
        catch (JsonException)
        {
            WriteJson(response, HttpStatusCode.BadRequest, new { error = "Invalid JSON body" });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            WriteJson(response, HttpStatusCode.InternalServerError, new { error = "Internal Server Error" });
        }

        try
        {
            await HttpResponseWriter.WriteAsync(stream, response, ct);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
        }
    }

    private static async Task RouteExecute(SimpleHttpRequest request, SimpleHttpResponse response)
    {
        if (request.HttpMethod is not ("GET" or "POST"))
        {
            WriteJson(response, HttpStatusCode.MethodNotAllowed, new { error = "Method Not Allowed" });
            return;
        }

        // GET はクエリ文字列、POST はJSONボディからパラメータを収集する（両方指定時はボディを優先）
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in request.QueryString)
        {
            parameters[key] = value;
        }

        if (request.HttpMethod == "POST" && request.HasBody)
        {
            using var doc = JsonDocument.Parse(request.BodyText);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                parameters[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                    ? prop.Value.GetString()!
                    : prop.Value.GetRawText();
            }
        }

        if (!parameters.TryGetValue(TargetKey, out var targetName) || string.IsNullOrWhiteSpace(targetName))
        {
            WriteJson(response, HttpStatusCode.BadRequest, new { error = $"'{TargetKey}' is required." });
            return;
        }

        if (!parameters.TryGetValue(CommandKey, out var commandName) || string.IsNullOrWhiteSpace(commandName))
        {
            WriteJson(response, HttpStatusCode.BadRequest, new { error = $"'{CommandKey}' is required." });
            return;
        }

        if (!parameters.TryGetValue(TimeoutKey, out var timeoutStr) || !int.TryParse(timeoutStr, out var timeout))
        {
            timeout = 5000;
        }

        var extraParams = parameters
            .Where(kv => kv.Key != TargetKey && kv.Key != CommandKey)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        try
        {
            var result = await starsdispacher.InvokeAsync(targetName, commandName, extraParams, timeout);
            WriteJson(response, HttpStatusCode.OK, new Dictionary<string, object?>
            {
                [TargetKey] = targetName,
                [CommandKey] = commandName,
                ["result"] = result,
            });
        }
        catch (CommandNotFoundException ex)
        {
            WriteJson(response, HttpStatusCode.NotFound, new { error = ex.Message });
        }
        catch (ParameterBindingException ex)
        {
            WriteJson(response, HttpStatusCode.BadRequest, new { error = ex.Message });
        }
    }

    private static async Task RouteGetValue(SimpleHttpRequest request, SimpleHttpResponse response)
    {
        if (request.HttpMethod is not ("GET" or "POST"))
        {
            WriteJson(response, HttpStatusCode.MethodNotAllowed, new { error = "Method Not Allowed" });
            return;
        }

        // GET はクエリ文字列、POST はJSONボディからパラメータを収集する（両方指定時はボディを優先）
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in request.QueryString)
        {
            parameters[key] = value;
        }

        if (request.HttpMethod == "POST" && request.HasBody)
        {
            using var doc = JsonDocument.Parse(request.BodyText);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                parameters[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                    ? prop.Value.GetString()!
                    : prop.Value.GetRawText();
            }
        }

        if (!parameters.TryGetValue(TargetKey, out var targetName) || string.IsNullOrWhiteSpace(targetName))
        {
            WriteJson(response, HttpStatusCode.BadRequest, new { error = $"'{TargetKey}' is required." });
            return;
        }

        var commandName = "GetValue";

        if (!parameters.TryGetValue(TimeoutKey, out var timeoutStr) || !int.TryParse(timeoutStr, out var timeout))
        {
            timeout = 5000;
        }

        var extraParams = parameters
            .Where(kv => kv.Key != TargetKey && kv.Key != CommandKey)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        try
        {
            var result = await starsdispacher.InvokeAsync(targetName, commandName, extraParams, timeout);
            WriteJson(response, HttpStatusCode.OK, new Dictionary<string, object?>
            {
                [TargetKey] = targetName,
                [CommandKey] = commandName,
                ["result"] = result,
            });
        }
        catch (CommandNotFoundException ex)
        {
            WriteJson(response, HttpStatusCode.NotFound, new { error = ex.Message });
        }
        catch (ParameterBindingException ex)
        {
            WriteJson(response, HttpStatusCode.BadRequest, new { error = ex.Message });
        }
    }

    private static T? ReadJson<T>(SimpleHttpRequest request)
    {
        if (!request.HasBody) return default;
        return JsonSerializer.Deserialize<T>(request.BodyText, JsonOptions);
    }

    private static void WriteJson(SimpleHttpResponse response, HttpStatusCode statusCode, object payload)
    {
        response.StatusCode = (int)statusCode;
        response.ContentType = "application/json";
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        response.Body = Encoding.UTF8.GetBytes(json);
    }
}
