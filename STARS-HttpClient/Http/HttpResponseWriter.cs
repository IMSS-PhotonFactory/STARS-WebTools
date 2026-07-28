using System.Text;

namespace STARSHttpClient.Http;

public static class HttpResponseWriter
{
    private static readonly Dictionary<int, string> ReasonPhrases = new()
    {
        [200] = "OK",
        [201] = "Created",
        [204] = "No Content",
        [400] = "Bad Request",
        [404] = "Not Found",
        [405] = "Method Not Allowed",
        [500] = "Internal Server Error",
    };

    public static async Task WriteAsync(Stream stream, SimpleHttpResponse response, CancellationToken ct)
    {
        var reasonPhrase = ReasonPhrases.GetValueOrDefault(response.StatusCode, "Unknown");

        var head = new StringBuilder();
        head.Append("HTTP/1.1 ").Append(response.StatusCode).Append(' ').Append(reasonPhrase).Append("\r\n");
        head.Append("Content-Type: ").Append(response.ContentType).Append("\r\n");
        head.Append("Content-Length: ").Append(response.Body.Length).Append("\r\n");
        head.Append("Connection: close\r\n"); // keep-alive非対応の簡易実装のため毎回切断する

        foreach (var (name, value) in response.Headers)
        {
            head.Append(name).Append(": ").Append(value).Append("\r\n");
        }

        head.Append("\r\n");

        await stream.WriteAsync(Encoding.UTF8.GetBytes(head.ToString()), ct);
        if (response.Body.Length > 0)
        {
            await stream.WriteAsync(response.Body, ct);
        }
        await stream.FlushAsync(ct);
    }
}
