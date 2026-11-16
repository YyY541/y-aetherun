namespace Aetherun.Core.Models;

/// <summary>引擎向界面推送的一帧状态。</summary>
public sealed record RunSnapshot
{
    public RunPhase Phase { get; init; } = RunPhase.Idle;
    public string Message { get; init; } = "";
    public double DistanceMeters { get; init; }
    public double TargetMeters { get; init; }
    public int Steps { get; init; }
    public double ElapsedSeconds { get; init; }
    public GeoPoint? Position { get; init; }
    public int FixCount { get; init; }

    /// <summary>
    /// 倒计时期间目标 App 是否在前台。null = 没检查（adb 未配置或已进入跑步阶段）。
    /// </summary>
    public bool? ForegroundOk { get; init; }

    /// <summary>检测到的前台包名，用于日志与提示。</summary>
    public string ForegroundPackage { get; init; } = "";

    /// <summary>倒计时是否被「App 不在前台」卡住。</summary>
    public bool CountdownHeld { get; init; }

    /// <summary>
    /// 倒计时走完了、但目标 App 从头到尾没到过前台。
    /// 这种跑步服务端大概率不计成绩，界面要挂一条警告出来，不能只写在日志里。
    /// </summary>
    public bool ForegroundMissed { get; init; }

    /// <summary>
    /// 实测步频（最近 6 秒，不掺设定值）。<see cref="Cadence"/> 是全程平均值，
    /// 这条反映「此刻真的在跑多快」，卡住的时候能立刻看出来。
    /// </summary>
    public double LiveCadence { get; init; }

    /// <summary>步频注入单次调用的实测耗时（毫秒）。</summary>
    public double PumpLatencyMs { get; init; }

    /// <summary>这台机器通过 ldconsole 能达到的步频上限；0 表示还没测出来。</summary>
    public double MaxCadence { get; init; }

    /// <summary>设定的步频已经顶到机器上限、跑不满。</summary>
    public bool CadenceLimited { get; init; }

    /// <summary>
    /// 运动世界自己认的步频（从它日志里的累计步数差分得来）。
    /// 0 = 还没拿到样本。<see cref="LiveCadence"/> 是我们发出去的量，这个是它认的量。
    /// </summary>
    public double DetectedCadence { get; init; }

    /// <summary>闭环校正的一句话说明（目标 / 实测 / 当前发送率倍数）。</summary>
    public string CadenceNote { get; init; } = "";

    /// <summary>倒计时还剩多少秒（只在 Armed 阶段有意义）。</summary>
    public double CountdownRemaining { get; init; }

    /// <summary>这次倒计时的总秒数，用来画进度。</summary>
    public double CountdownTotal { get; init; }

    /// <summary>倒计时完成度 0~1。</summary>
    public double CountdownProgress => CountdownTotal <= 0
        ? 0
        : Math.Clamp(1 - CountdownRemaining / CountdownTotal, 0, 1);
    public DateTime Timestamp { get; init; } = DateTime.Now;

    public double DistanceKm => DistanceMeters / 1000.0;
    public double Progress => TargetMeters <= 0 ? 0 : Math.Clamp(DistanceMeters / TargetMeters, 0, 1);

    public double Cadence => ElapsedSeconds < 3 || Steps <= 0 ? 0 : Steps / ElapsedSeconds * 60.0;

    /// <summary>平均配速，秒/公里；数据不足返回 0。</summary>
    public double PaceSecondsPerKm =>
        DistanceMeters < 5 || ElapsedSeconds < 1 ? 0 : ElapsedSeconds / (DistanceMeters / 1000.0);

    /// <summary>阶段的中文短标签，给界面徽标用。</summary>
    public string PhaseText => Phase switch
    {
        RunPhase.Idle => "待机",
        RunPhase.Armed => "倒计时",
        RunPhase.Running => "跑步中",
        RunPhase.Done => "已完成",
        RunPhase.Stopped => "已停止",
        RunPhase.Error => "异常",
        _ => "未知"
    };

    /// <summary>里程文本，如 1.24 km。</summary>
    public string DistanceText => $"{DistanceKm:F2} km";

    /// <summary>用时文本，如 12:34。超过一小时会带小时。</summary>
    public string ElapsedText
    {
        get
        {
            int total = (int)Math.Max(0, ElapsedSeconds);
            int h = total / 3600;
            int m = total % 3600 / 60;
            int s = total % 60;
            return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m:00}:{s:00}";
        }
    }

    /// <summary>平均步频文本，如 112 spm。</summary>
    public string CadenceText => Cadence <= 0 ? "-- spm" : $"{Cadence:F0} spm";

    /// <summary>平均配速文本，如 6'12"/km。</summary>
    public string PaceText
    {
        get
        {
            double p = PaceSecondsPerKm;
            if (p <= 0) return "--'--\"";
            int total = (int)Math.Round(p);
            return $"{total / 60}'{total % 60:00}\"";
        }
    }

    public static RunSnapshot Idle() => new();
}
