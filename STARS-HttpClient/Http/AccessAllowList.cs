using System.Net;
using System.Text.RegularExpressions;

namespace STARSHttpClient.Http;

/// <summary>
/// 実行ファイルと同じフォルダにある allowlist.txt を用いて、
/// 接続元 IP アドレスの許可判定を行う。
/// allowlist.txt には IP アドレス、ワイルドカード(*) を含む IP アドレス、または FQDN を1行ずつ記載する。
/// (# で始まる行および空行は無視される)
/// allowlist.txt が存在しない場合は、すべての接続を許可する。
/// </summary>
public static class AccessAllowList
{
    private static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "allowlist.txt");
    private static readonly object SyncRoot = new();

    private static List<string>? _cachedEntries;
    private static bool _cachedFileExists;
    private static DateTime _cachedWriteTimeUtc;

    public static bool IsAllowed(IPAddress? remoteAddress)
    {
        if (remoteAddress is null) return false;

        var entries = LoadEntries();
        if (entries is null) return true; // allowlist.txt が存在しない場合は全ての接続を許可する
        if (entries.Count == 0) return false;

        var normalizedRemote = Normalize(remoteAddress);

        var normalizedRemoteText = normalizedRemote.ToString();

        foreach (var entry in entries)
        {
            if (entry.Contains('*'))
            {
                if (IsWildcardMatch(entry, normalizedRemoteText))
                {
                    return true;
                }
                continue;
            }

            if (IPAddress.TryParse(entry, out var entryAddress))
            {
                if (Normalize(entryAddress).Equals(normalizedRemote))
                {
                    return true;
                }
                continue;
            }

            // IP アドレスとして解釈できない場合は FQDN として名前解決する
            try
            {
                var resolved = Dns.GetHostAddresses(entry);
                if (resolved.Any(addr => Normalize(addr).Equals(normalizedRemote)))
                {
                    return true;
                }
            }
            catch
            {
                // 名前解決失敗時はそのエントリを無視する
            }
        }

        return false;
    }

    private static IPAddress Normalize(IPAddress address)
    {
        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }

    private static bool IsWildcardMatch(string pattern, string value)
    {
        var regexPattern = "^" + string.Join(".*", pattern.Split('*').Select(Regex.Escape)) + "$";
        return Regex.IsMatch(value, regexPattern, RegexOptions.IgnoreCase);
    }

    private static List<string>? LoadEntries()
    {
        lock (SyncRoot)
        {
            if (!File.Exists(FilePath))
            {
                _cachedEntries = null;
                _cachedFileExists = false;
                return null;
            }

            var writeTimeUtc = File.GetLastWriteTimeUtc(FilePath);
            if (_cachedFileExists && _cachedEntries is not null && writeTimeUtc == _cachedWriteTimeUtc)
            {
                return _cachedEntries;
            }

            _cachedEntries = File.ReadAllLines(FilePath)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.StartsWith('#'))
                .ToList();
            _cachedWriteTimeUtc = writeTimeUtc;
            _cachedFileExists = true;

            return _cachedEntries;
        }
    }
}
