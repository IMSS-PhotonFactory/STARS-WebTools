using System.Text;

namespace STARSHttpClient.Http;

/// <summary>TcpListener 上で自前パースしたHTTPリクエストを表す。</summary>
public class SimpleHttpRequest
{
    public required string HttpMethod { get; init; }
    public required string Path { get; init; }
    public required IReadOnlyDictionary<string, string> QueryString { get; init; }
    public required IReadOnlyDictionary<string, string> Headers { get; init; }
    public required byte[] Body { get; init; }

    /// <summary>ボディはUTF-8前提で扱う（簡易実装のため charset は考慮しない）。</summary>
    public string BodyText => Body.Length == 0 ? string.Empty : Encoding.UTF8.GetString(Body);

    public bool HasBody => Body.Length > 0;
}
