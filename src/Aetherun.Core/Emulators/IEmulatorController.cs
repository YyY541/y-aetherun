using Aetherun.Core.Models;

namespace Aetherun.Core.Emulators;

/// <summary>
/// 模拟器控制器的统一接口。所有模拟器差异都收敛在这里，
/// 引擎与界面只认这个接口，新增模拟器只需再实现一个。
/// </summary>
public interface IEmulatorController
{
    /// <summary>用于界面显示的名字，例如「雷电 14」。</summary>
    string DisplayName { get; }

    /// <summary>本控制器对应的模拟器序号（多开时用）。</summary>
    int Index { get; }

    /// <summary>工具路径是否有效（可执行文件存在且已配置）。</summary>
    bool IsConfigured { get; }

    /// <summary>把 Android GPS 定到指定点。返回是否成功。</summary>
    Task<bool> SetLocationAsync(GeoPoint point, CancellationToken ct = default);

    /// <summary>把重力传感器 Z 轴设到指定值（步频注入用）。</summary>
    Task<bool> SetGravityZAsync(double z, CancellationToken ct = default);

    /// <summary>列出可用实例，用于界面展示与自检。</summary>
    Task<string> DescribeAsync(CancellationToken ct = default);

    /// <summary>启动模拟器实例（可选能力，不支持返回 null）。</summary>
    Task<bool?> LaunchInstanceAsync(CancellationToken ct = default);
}
