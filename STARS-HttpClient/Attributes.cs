namespace STARSHttpClient;

/// <summary>このクラスを「対象名」として公開する。</summary>
[AttributeUsage(AttributeTargets.Class)]
public class TargetAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

/// <summary>このメソッドを「コマンド名」として公開する。</summary>
[AttributeUsage(AttributeTargets.Method)]
public class CommandAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
