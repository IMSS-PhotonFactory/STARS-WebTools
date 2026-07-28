using STARSHttpClient;

namespace STARSHttpClient.Targets;

/// <summary>
/// パラメータ付き・非同期メソッドの実装例。
/// </summary>
[Target("Echo")]
public class EchoTarget
{
    [Command("Say")]
    public object Say(string message, int repeat = 1) =>
        new { message = string.Concat(Enumerable.Repeat(message, Math.Max(1, repeat))) };

    [Command("SayAsync")]
    public async Task<object> SayAsync(string message)
    {
        await Task.Delay(10);
        return new { message, delayedMs = 10 };
    }
}
