using System.Text;

namespace STARSHttpClient.Http;

/// <summary>
/// ソケットの生ストリームから HTTP/1.1 リクエストを最小限パースする。
/// chunked転送やkeep-aliveには対応しない簡易実装。
/// </summary>
public static class HttpRequestParser
{
    public static async Task<SimpleHttpRequest?> ParseAsync(Stream stream, CancellationToken ct)
    {
        var requestLine = await ReadLineAsync(stream, ct);
        if (string.IsNullOrEmpty(requestLine))
        {
            return null; // クライアントが接続を閉じた
        }

        var parts = requestLine.Split(' ', 3);
        if (parts.Length < 2)
        {
            throw new InvalidDataException($"不正なリクエスト行です: {requestLine}");
        }

        var method = parts[0];
        var (path, query) = ParseTarget(parts[1]);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            var line = await ReadLineAsync(stream, ct);
            if (string.IsNullOrEmpty(line)) break; // 空行 = ヘッダー終端

            var colonIndex = line.IndexOf(':');
            if (colonIndex < 0) continue;
            headers[line[..colonIndex].Trim()] = line[(colonIndex + 1)..].Trim();
        }

        var body = Array.Empty<byte>();
        if (headers.TryGetValue("Content-Length", out var lengthText) &&
            int.TryParse(lengthText, out var length) && length > 0)
        {
            body = new byte[length];
            var readTotal = 0;
            while (readTotal < length)
            {
                var read = await stream.ReadAsync(body.AsMemory(readTotal, length - readTotal), ct);
                if (read == 0) throw new IOException("ボディ受信中に接続が切断されました。");
                readTotal += read;
            }
        }

        return new SimpleHttpRequest
        {
            HttpMethod = method,
            Path = path,
            QueryString = query,
            Headers = headers,
            Body = body,
        };
    }

    private static (string Path, Dictionary<string, string> Query) ParseTarget(string rawTarget)
    {
        var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var questionIndex = rawTarget.IndexOf('?');
        var path = questionIndex >= 0 ? rawTarget[..questionIndex] : rawTarget;

        if (questionIndex >= 0)
        {
            var queryString = rawTarget[(questionIndex + 1)..];
            foreach (var pair in queryString.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eqIndex = pair.IndexOf('=');
                var rawKey = eqIndex >= 0 ? pair[..eqIndex] : pair;
                var rawValue = eqIndex >= 0 ? pair[(eqIndex + 1)..] : string.Empty;
                var key = Uri.UnescapeDataString(rawKey.Replace('+', ' '));
                var value = Uri.UnescapeDataString(rawValue.Replace('+', ' '));
                query[key] = value;
            }
        }

        return (Uri.UnescapeDataString(path), query);
    }

    /// <summary>
    /// 1バイトずつ読み CRLF を検出する簡易実装。localhost でのひな形用途を想定しスループットは優先しない。
    /// </summary>
    private static async Task<string> ReadLineAsync(Stream stream, CancellationToken ct)
    {
        var buffer = new List<byte>();
        var single = new byte[1];

        while (true)
        {
            var read = await stream.ReadAsync(single.AsMemory(0, 1), ct);
            if (read == 0)
            {
                return buffer.Count == 0 ? string.Empty : Encoding.UTF8.GetString(buffer.ToArray());
            }

            if (single[0] == (byte)'\n')
            {
                if (buffer.Count > 0 && buffer[^1] == (byte)'\r')
                {
                    buffer.RemoveAt(buffer.Count - 1);
                }
                return Encoding.UTF8.GetString(buffer.ToArray());
            }

            buffer.Add(single[0]);
        }
    }
}
