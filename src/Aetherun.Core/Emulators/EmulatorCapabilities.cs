namespace Aetherun.Core.Emulators;

/// <summary>
/// 某个模拟器后端能做什么、不能做什么。
/// 界面和文档都按这个显示，避免用户以为「装不上就是程序坏了」。
/// </summary>
/// <param name="CanGeo">能否改 Android GPS（不是只改模拟器自己地图上的标记）。</param>
/// <param name="CanStep">能否伪造成加速度，让 App 计步。</param>
/// <param name="CanAutoStart">能否自动拉起模拟器实例。</param>
/// <param name="StepNote">计步通道的实现方式与已知限制，直接显示给用户。</param>
/// <param name="GeoNote">定位通道的实现方式与已知限制。</param>
public sealed record EmulatorCapabilities(
    bool CanGeo,
    bool CanStep,
    bool CanAutoStart,
    string GeoNote,
    string StepNote)
{
    public static EmulatorCapabilities Full(string geoNote, string stepNote)
        => new(true, true, true, geoNote, stepNote);

    public static EmulatorCapabilities GeoOnly(string geoNote)
        => new(true, false, false, geoNote, "该模拟器没有命令行加速度接口，只能改定位");
}
