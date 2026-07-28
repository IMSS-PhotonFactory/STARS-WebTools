using System.Reflection;

namespace STARSHttpClient;

public class CommandNotFoundException(string message) : Exception(message);

public class ParameterBindingException(string message) : Exception(message);

/// <summary>
/// [Target]/[Command] 属性が付与されたクラス・メソッドをアセンブリから収集し、
/// 「対象名」「コマンド名」を鍵にリフレクションで呼び出す。
/// </summary>
public class CommandDispatcher
{
    private readonly record struct CommandEntry(Type DeclaringType, MethodInfo Method);

    private readonly Dictionary<string, Dictionary<string, CommandEntry>> _targets =
        new(StringComparer.OrdinalIgnoreCase);

    public CommandDispatcher(Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
        {
            var targetAttr = type.GetCustomAttribute<TargetAttribute>();
            if (targetAttr is null) continue;

            var commands = new Dictionary<string, CommandEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                var commandAttr = method.GetCustomAttribute<CommandAttribute>();
                if (commandAttr is null) continue;
                commands[commandAttr.Name] = new CommandEntry(type, method);
            }

            if (commands.Count > 0)
            {
                _targets[targetAttr.Name] = commands;
            }
        }
    }

    /// <summary>登録済みの対象名とコマンド名の一覧（動作確認・デバッグ用）。</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ListTargets() =>
        _targets.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value.Keys.ToList());

    public async Task<object?> InvokeAsync(string targetName, string commandName, IReadOnlyDictionary<string, string> parameters)
    {
        if (!_targets.TryGetValue(targetName, out var commands))
        {
            throw new CommandNotFoundException($"対象名 '{targetName}' が見つかりません。");
        }

        if (!commands.TryGetValue(commandName, out var entry))
        {
            throw new CommandNotFoundException($"コマンド名 '{commandName}' が対象 '{targetName}' に見つかりません。");
        }

        var method = entry.Method;
        var args = BindParameters(method, parameters);
        var instance = method.IsStatic ? null : Activator.CreateInstance(entry.DeclaringType);

        object? rawResult;
        try
        {
            rawResult = method.Invoke(instance, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            // リフレクション経由の例外は InnerException に本来の例外が入るため、そちらを投げ直す
            throw ex.InnerException!;
        }

        if (rawResult is Task task)
        {
            await task;
            var resultProperty = task.GetType().GetProperty("Result");
            return task.GetType().IsGenericType ? resultProperty?.GetValue(task) : null;
        }

        return rawResult;
    }

    private static object?[] BindParameters(MethodInfo method, IReadOnlyDictionary<string, string> parameters)
    {
        var methodParams = method.GetParameters();
        var args = new object?[methodParams.Length];

        for (var i = 0; i < methodParams.Length; i++)
        {
            var p = methodParams[i];
            if (parameters.TryGetValue(p.Name!, out var rawValue))
            {
                args[i] = ConvertValue(rawValue, p.ParameterType, p.Name!);
            }
            else if (p.HasDefaultValue)
            {
                args[i] = p.DefaultValue;
            }
            else
            {
                throw new ParameterBindingException($"パラメータ '{p.Name}' が指定されていません。");
            }
        }

        return args;
    }

    private static object? ConvertValue(string raw, Type targetType, string paramName)
    {
        try
        {
            if (targetType == typeof(string)) return raw;
            if (targetType == typeof(bool)) return bool.Parse(raw);
            if (targetType.IsEnum) return Enum.Parse(targetType, raw, ignoreCase: true);
            if (targetType == typeof(DateTime)) return DateTime.Parse(raw);
            return Convert.ChangeType(raw, targetType);
        }
        catch (Exception ex)
        {
            throw new ParameterBindingException(
                $"パラメータ '{paramName}' の値 '{raw}' を {targetType.Name} に変換できません: {ex.Message}");
        }
    }
}
