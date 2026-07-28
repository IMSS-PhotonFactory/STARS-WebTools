using STARSHttpClient;

namespace STARSHttpClient.Targets;

/// <summary>
/// 「対象名」= System として公開される実装例。
/// [Command] を付けたメソッドが「コマンド名」で呼び出される。
/// </summary>
[Target("System")]
public class SystemTarget
{
    [Command("Ping")]
    public object Ping() => new { status = "ok" };

    [Command("Time")]
    public object GetTime() => new { now = DateTime.Now };
}
