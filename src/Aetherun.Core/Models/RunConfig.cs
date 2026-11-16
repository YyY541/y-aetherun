namespace Aetherun.Core.Models;

public enum RunPhase
{
    Idle,
    Armed,
    Running,
    Done,
    Stopped,
    Error
}

/// <summary>一次跑步的全部可调参数。界面上的仪表盘直接改这里。</summary>
public sealed class RunConfig
{
    /// <summary>目标公里数的下限 / 上限。校园跑上限就是 2 km，再多系统也不认。</summary>
    public const double MinTargetKm = 0.5;
    public const double MaxTargetKm = 2.0;

    /// <summary>配速（分钟/公里）的合法区间：2 分到 10 分。</summary>
    public const double MinPaceMinPerKm = 2.0;
    public const double MaxPaceMinPerKm = 10.0;

    /// <summary>由配速区间换算出来的速度区间：10 分/公里 → 1.667 m/s，2 分/公里 → 8.333 m/s。</summary>
    public const double MinSpeedMps = 1000.0 / (MaxPaceMinPerKm * 60.0);
    public const double MaxSpeedMps = 1000.0 / (MinPaceMinPerKm * 60.0);

    public const int MinCadence = 60;
    public const int MaxCadence = 300;

    /// <summary>
    /// 配置结构版本。老版本的 config.json 里存着过时的默认值（比如倒计时 20 秒），
    /// 光改代码里的默认值不会影响已经落盘的旧文件，所以靠这个版本号做一次迁移。
    /// </summary>
    public const int CurrentSchema = 3;

    public double TargetKm { get; set; } = 2.0;
    public double SpeedMps { get; set; } = 2.44;
    public double CadencePerMin { get; set; } = 110;
    public int LeadSeconds { get; set; } = 15;
    public int EmulatorIndex { get; set; }
    public double GravityLow { get; set; } = 5.5;
    public double GravityHigh { get; set; } = 14.0;

    /// <summary>ldconsole.exe 的完整路径。</summary>
    public string LdConsolePath { get; set; } = "";

    /// <summary>adb.exe 的完整路径（留空则自动探测）。</summary>
    public string AdbPath { get; set; } = "";

    public string AdbSerial { get; set; } = "127.0.0.1:5555";
    public string PackageName { get; set; } = "com.zjwh.android_wh_physicalfitness";

    public bool GeoEnabled { get; set; } = true;
    public bool StepEnabled { get; set; } = true;
    public bool AutoStartApp { get; set; } = true;

    /// <summary>倒计时期间检测「运动世界是否在前台」，不在就暂停倒数。</summary>
    public bool CheckForeground { get; set; } = true;

    public int SchemaVersion { get; set; } = CurrentSchema;

    public double TargetMeters => TargetKm * 1000.0;

    public RunConfig Sanitized()
    {
        var c = (RunConfig)MemberwiseClone();
        c.TargetKm = Math.Clamp(c.TargetKm, MinTargetKm, MaxTargetKm);
        c.SpeedMps = Math.Clamp(c.SpeedMps, MinSpeedMps, MaxSpeedMps);
        c.CadencePerMin = Math.Clamp(c.CadencePerMin, MinCadence, MaxCadence);
        c.LeadSeconds = Math.Clamp(c.LeadSeconds, 0, 120);
        c.EmulatorIndex = Math.Max(0, c.EmulatorIndex);
        c.GravityLow = Math.Clamp(c.GravityLow, 0.0, 8.5);
        c.GravityHigh = Math.Clamp(c.GravityHigh, 11.5, 30.0);
        c.LdConsolePath ??= "";
        c.AdbPath ??= "";
        // 空序列号会让 adb -s "" 整条命令失效，空包名会让 am force-stop 报错，
        // 都在这里补回默认值。
        if (string.IsNullOrWhiteSpace(c.AdbSerial)) c.AdbSerial = "127.0.0.1:5555";
        if (string.IsNullOrWhiteSpace(c.PackageName)) c.PackageName = "com.zjwh.android_wh_physicalfitness";
        return c;
    }

    public RunConfig Clone() => (RunConfig)MemberwiseClone();

    /// <summary>预设：慢走 / 标准 / 快走。</summary>
    public static RunConfig Preset(string name) => name switch
    {
        // 三个档位按用户给的真人配速定：
        //   慢走 8'00"/km = 2.083 m/s、标准 6'00"/km = 2.778 m/s、快走 4'00"/km = 4.167 m/s
        // 步频同理拉开：慢走 90、标准 120、快走 150 步每分。
        "slow" => new RunConfig { SpeedMps = 2.083, CadencePerMin = 90, TargetKm = 2.0 },
        "fast" => new RunConfig { SpeedMps = 4.167, CadencePerMin = 150, TargetKm = 2.0 },
        _ => new RunConfig { SpeedMps = 2.778, CadencePerMin = 120, TargetKm = 2.0 }
    };

    /// <summary>把旧版本留下的过时默认值迁到当前口径。只动「还是出厂值」的项，用户改过的不碰。</summary>
    public RunConfig Migrated()
    {
        var c = (RunConfig)MemberwiseClone();
        if (c.SchemaVersion < 3 && c.LeadSeconds == 20) c.LeadSeconds = 15;
        c.SchemaVersion = CurrentSchema;
        return c;
    }

    /// <summary>速度换算成配速秒数（每公里）。</summary>
    public static double PaceSecondsPerKm(double speedMps) =>
        speedMps <= 0 ? 0 : 1000.0 / speedMps;

    /// <summary>把速度说成人话：6'50"/km。</summary>
    public static string PaceLabel(double speedMps)
    {
        double sec = PaceSecondsPerKm(speedMps);
        if (sec <= 0) return "--'--\"";
        int m = (int)(sec / 60);
        int s = (int)Math.Round(sec - m * 60);
        if (s == 60) { m++; s = 0; }
        return $"{m}'{s:00}\"";
    }
}
