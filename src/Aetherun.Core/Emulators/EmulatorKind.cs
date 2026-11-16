namespace Aetherun.Core.Emulators;

/// <summary>
/// 后端类型。当前只支持雷电模拟器；保留枚举是为了以后接入别的模拟器时
/// 不用再改一遍引擎与配置结构。
/// </summary>
public enum EmulatorKind
{
    /// <summary>雷电模拟器 9 / 12 / 14，走 ldconsole.exe。</summary>
    LdPlayer = 0,
}

public static class EmulatorKinds
{
    public static string Label(EmulatorKind kind) => kind switch
    {
        EmulatorKind.LdPlayer => "雷电模拟器",
        _ => kind.ToString(),
    };

    public static IReadOnlyList<EmulatorKind> All { get; } = new[]
    {
        EmulatorKind.LdPlayer,
    };
}
