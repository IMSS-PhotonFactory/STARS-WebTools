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
    private const string ParameterKey = "param";
    private const string TimeoutKey = "timeout";

    private static StarsDispacher starsdispacher;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static async Task Main(string[] args)
    {
        ApplicationConfig config;
        
        var configFilename = "config.json";
        if (args.Length > 0)
        {
            configFilename = args[0] + ".json";
        }

        var configfile = Path.Combine(AppContext.BaseDirectory, configFilename);
        if (File.Exists(configfile))
        {
            try
            {
                using (var sr = new StreamReader(configfile))
                {
                    config = JsonSerializer.Deserialize<ApplicationConfig>(sr.ReadToEnd());
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not read config file [{configfile}].\r\n{ex.Message}");
                return;
            }
        }
        else
        {
            config = new ApplicationConfig();
            using (var sw = new StreamWriter(configfile, false))
            {
                sw.WriteLine(JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
                sw.Close();
            }

            Console.WriteLine($"Could not find config file [{configfile}]. New config file created. Please restart application.");
            return;
        }

        starsdispacher = new StarsDispacher(config.StarsConf);
        if (!starsdispacher.IsReady)
        {
            Console.WriteLine("STARS connection is not ready.");
            return;
        }

        var listener = new TcpListener(IPAddress.Any, config.HttpPort);
        listener.Start();
        Console.WriteLine($"Listening on http://localhost:{config.HttpPort}/ (Ctrl+C to stop)");

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
                await RouteExecute(request, response, ExecuteMode.Free);
            }
            else if (segments.Length == 2 && string.Equals(segments[0], "StarsApi", StringComparison.OrdinalIgnoreCase) && string.Equals(segments[1], "GetValue", StringComparison.OrdinalIgnoreCase))
            {
                await RouteExecute(request, response, ExecuteMode.GetValue);
            }
            else if (segments.Length == 2 && string.Equals(segments[0], "StarsApi", StringComparison.OrdinalIgnoreCase) && string.Equals(segments[1], "SetValue", StringComparison.OrdinalIgnoreCase))
            {
                await RouteExecute(request, response, ExecuteMode.SetValue);
            }
            else
            {
                WriteJson(response, HttpStatusCode.NotFound, new { error = "Path Not Found" });
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

    private static async Task RouteExecute(SimpleHttpRequest request, SimpleHttpResponse response, ExecuteMode mode)
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
            WriteJson(response, HttpStatusCode.BadRequest, new {
                node = "",
                command = "",
                result = $"parameter {TargetKey} is required.",
                errorflag = true
            });
            return;
        }

        if(mode == ExecuteMode.GetValue)
        {
            parameters[CommandKey] = "GetValue";
        }
        else if (mode == ExecuteMode.SetValue)
        {
            parameters[CommandKey] = "SetValue";
        }

        if (!parameters.TryGetValue(CommandKey, out var commandName) || string.IsNullOrWhiteSpace(commandName))
        {
            WriteJson(response, HttpStatusCode.BadRequest, new {
                node = targetName,
                command = "",
                result = $"parameter {CommandKey} is required.",
                errorflag = true
            });
            return;
        }

        if (!parameters.TryGetValue(ParameterKey, out var parameterValue) || string.IsNullOrWhiteSpace(parameterValue))
        {
            if (mode == ExecuteMode.SetValue)
            {
                WriteJson(response, HttpStatusCode.BadRequest, new {
                    node = targetName,
                    command = commandName,
                    result = $"parameter {ParameterKey} is required.",
                    errorflag = true
                });
                return;
            }
        }
        if(parameterValue is not null)
        {
            parameterValue = parameterValue.Trim();
        }
        
        if (!parameters.TryGetValue(TimeoutKey, out var timeoutStr) || !int.TryParse(timeoutStr, out var timeout))
        {
            timeout = 5000;
        }

        var extraParams = parameters
            .Where(kv => kv.Key != TargetKey && kv.Key != CommandKey && kv.Key != ParameterKey)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        try
        {
            var result = await starsdispacher.InvokeAsync(targetName, commandName, parameterValue, extraParams, timeout);
            var errFlg = false;
            if(result.Contains("Er:"))
            {
                errFlg = true;
            }
            WriteJson(response, HttpStatusCode.OK, new Dictionary<string, object?>
            {
                [TargetKey] = targetName,
                [CommandKey] = commandName,
                ["result"] = result,
                ["errorflag"] = errFlg
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

public class ApplicationConfig
{
    public StarsConfig StarsConf { get; set; } = new();
    public int HttpPort { get; set; } = 9901;
}

public class  StarsConfig
{
    public string StarsNode { get; set; } = "webapi";
    public string StarsHost { get; set; } = "127.0.0.1";
    public int StarsPort { get; set; } = 6057;
    public string StarsKey { get; set; } = "webapi.key";
    public string StarsKeyword { get; set; } = "stars";
    public bool UseStarsKeyword { get; set; } = true;
}

internal enum ExecuteMode
{
    GetValue,
    SetValue,
    Free
}