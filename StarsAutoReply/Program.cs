using STARS;

namespace StarsAutoReply
{
    internal class Program
    {
        internal static StarsInterface? stars;

        static string nodename = "autorep";
        static string hostname = "127.0.0.1";
        static int port = 6057;
        static string keyword = "stars";

        static async Task Main(string[] args)
        {
            stars = new StarsInterface(nodename, hostname, "", port) { KeyWord = keyword };
            stars.DataReceived += Stars_DataReceived;
            try
            {
                stars.Connect(true);

            }
            catch (Exception)
            {
                Console.WriteLine("StarsInterface connection failed. Please check your settings.");
                return;
            }

            Console.WriteLine($"STARS Auto Replay Executed (Ctrl+C to stop)");

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
                stars.Disconnect();
            };

            try
            {
                await Task.Delay(Timeout.Infinite, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Ctrl+C による正常終了
            }
        }

        private static void Stars_DataReceived(object? sender, StarsCbArgs e)
        {
            if(!e.to.StartsWith(nodename, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            int randomNumber = Random.Shared.Next(0, 10001);

            if(e.parameters.Length > 0)
            {
                stars.Send(e.to, e.from, $"@{e.command} {e.parameters.Trim()} Ok:");
            }
            else if(e.command == "GetValue")
            {
                stars.Send(e.to, e.from, $"@{e.command} {randomNumber}");
            }
            else
            {
                stars.Send(e.to, e.from, $"@{e.command} Ok:");
            }
        }
    }
}
