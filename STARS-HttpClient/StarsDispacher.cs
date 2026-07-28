using STARS;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace STARSHttpClient
{
     internal class StarsDispacher
     {
        StarsInterface stars;
        ConcurrentDictionary<string, TaskCompletionSource<string?>> waitHandleDict = new ConcurrentDictionary<string, TaskCompletionSource<string?>>();

        string starsNode = "webapi";

        public bool IsReady
        {
            get
            {
                if (stars != null)
                {
                    return stars.IsConnected;
                }
                return false;
            }
        }

        public StarsDispacher()
        {
            stars = new StarsInterface(starsNode, "127.0.0.1", $"{starsNode}.key", 6057) { KeyWord = "stars" };
            stars.DataReceived += Stars_DataReceived;

            try
            {
                stars.Connect(true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.Message);
            }
        }

        private void Stars_DataReceived(object? sender, StarsCbArgs e)
        {
            string key = $"{e.to}|{e.command}";
            if (waitHandleDict.TryGetValue(key, out var tcs))
            {
                tcs.TrySetResult(e.parameters);
            }
        }

        public async Task<string?> InvokeAsync(string targetName, string commandName, IReadOnlyDictionary<string, string> parameters, int timeout = 5000)
        {
            string sender = $"{starsNode}.{DateTime.Now.ToString("yyyyMMddHHmmssfff")}.{Guid.NewGuid():N}";
            string key = $"{sender}|@{commandName}";
            var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            waitHandleDict[key] = tcs;

            try
            {
                stars.Send(sender, targetName, commandName);

                var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeout));
                if (completed != tcs.Task)
                {
                    return $"Er: Timeout {timeout} ms";
                }

                return await tcs.Task;
            }
            finally
            {
                waitHandleDict.TryRemove(key, out _);
            }
        }

     }

    public class CommandNotFoundException(string message) : Exception(message);

    public class ParameterBindingException(string message) : Exception(message);

    //    public async Task<object?> InvokeAsync(string targetName, string commandName, IReadOnlyDictionary<string, string> parameters)
    //    {
    //        if (!_targets.TryGetValue(targetName, out var commands))
    //        {
    //            throw new CommandNotFoundException($"対象名 '{targetName}' が見つかりません。");
    //        }

    //        if (!commands.TryGetValue(commandName, out var entry))
    //        {
    //            throw new CommandNotFoundException($"コマンド名 '{commandName}' が対象 '{targetName}' に見つかりません。");
    //        }


}
