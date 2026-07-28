namespace STARSHttpClient.Http;

/// <summary>ソケットへ書き出す前のHTTPレスポンスをメモリ上に保持する。</summary>
public class SimpleHttpResponse
{
    public int StatusCode { get; set; } = 200;
    public string ContentType { get; set; } = "application/json";
    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
    public byte[] Body { get; set; } = [];
}
