using System.Diagnostics;
using Aetherun.Core.Emulators;
using Aetherun.Core.Models;
using Aetherun.Core.Services;

namespace Aetherun.Desktop.Services;

/// <summary>桌面端对核心服务的聚合入口，界面只依赖它。</summary>
public sealed class AppShell
{
    public IProcessRunner Runner { get; } = new ProcessRunner();

    public string BaseDirectory { get; } = AppContext.BaseDirectory;

    /// <summary>雷电的管理工具；用户填了有效路径就用它，否则自动探测。</summary>
    public string? ResolveLdConsole(string? configured = null)
    {
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        foreach (var drive in new[] { "C", "D", "E", "F" })
        {
            foreach (var root in new[] { "leidian", "LDPlayer", "Program Files\\LDPlayer", "Program Files (x86)\\LDPlayer" })
            {
                foreach (var ver in new[] { "LDPlayer14", "LDPlayer12", "LDPlayer9", "" })
                {
                    var p = Path.Combine($"{drive}:\\", root, ver, "ldconsole.exe");
                    if (File.Exists(p)) return p;
                }
            }
        }

        var env = Environment.GetEnvironmentVariable("LDPLAYER_HOME");
        if (!string.IsNullOrWhiteSpace(env))
        {
            var p = Path.Combine(env, "ldconsole.exe");
            if (File.Exists(p)) return p;
        }
        return null;
    }

    /// <summary>找 adb：优先用模拟器同目录的，其次 PATH。</summary>
    public string? ResolveAdb(string? toolPath)
    {
        if (!string.IsNullOrWhiteSpace(toolPath))
        {
            var dir = Path.GetDirectoryName(toolPath);
            foreach (var d in new[] { dir, dir is null ? null : Path.GetDirectoryName(dir) })
            {
                if (d is null) continue;
                var p = Path.Combine(d, "adb.exe");
                if (File.Exists(p)) return p;
            }
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var seg in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var p = Path.Combine(seg.Trim(), "adb.exe");
                if (File.Exists(p)) return p;
            }
            catch { }
        }
        return null;
    }

    /// <summary>造出雷电控制器。</summary>
    public IEmulatorController CreateController(RunConfig cfg, string toolPath)
        => new LdPlayerController(new LdConsoleClient(Runner, toolPath, cfg.EmulatorIndex));

    public static EmulatorCapabilities Capabilities { get; } = EmulatorCapabilities.Full(
        "locate --LLI 经度,纬度，直接写 GPS HAL，不带 mock 标记",
        "action call.gravity 直接给加速度数值，精度 0.01");

    public AdbClient? CreateAdbClient(string? exe, string serial)
        => exe is null ? null : new AdbClient(Runner, exe, serial);

    public void OpenFolder(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch { }
    }
}
