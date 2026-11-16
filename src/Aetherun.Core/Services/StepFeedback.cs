using System.Text.RegularExpressions;

namespace Aetherun.Core.Services;

/// <summary>
/// 从 <c>adb logcat</c> 里读运动世界自己算出来的步数，用来做步频的闭环校正。
///
/// 为什么需要它：注入通道（<c>ldconsole action --key call.gravity</c>）是「我们发多少」，
/// 而运动世界显示的是「它自己认了多少」，两者之间隔着它的采样率、滤波和最小步间隔。
/// 只发不看，设定 120 实测 90 这种偏差永远修不掉 —— 必须拿它的数当反馈。
///
/// 从 dex 里翻出来的日志键（<c>ys_dex.bin</c>，均在 64490600 偏移附近）：
///   step_final_raw / step_final_capped / step_source_count_raw /
///   step_source_detector_raw / step_source_accelerate / step_delta / step_json
/// 其中累计量是 <c>step_final_raw</c> 与 <c>step_source_count_raw</c>，取差分即得速率。
///
/// 读不到就如实返回 null，调用方退回开环——不猜、不编。
/// </summary>
public sealed partial class StepFeedback
{
    private readonly AdbClient _adb;
    private long _lastRaw = -1;
    private DateTime _lastAt;

    public StepFeedback(AdbClient adb)
    {
        _adb = adb ?? throw new ArgumentNullException(nameof(adb));
    }

    /// <summary>最近一次采样算出的实测步频（步/分）；样本不足返回 0。</summary>
    public double DetectedCadence { get; private set; }

    /// <summary>运动世界自报的累计步数；没读到过返回 -1。</summary>
    public long RawSteps => _lastRaw;

    /// <summary>最近一次采样的说明，界面直接显示（含读不到的原因）。</summary>
    public string Note { get; private set; } = "尚未采样";

    /// <summary>
    /// 抓一次日志并更新速率。返回值表示这次是否拿到了可用的新样本。
    /// </summary>
    public async Task<bool> SampleAsync(CancellationToken ct = default)
    {
        // -d 打出当前缓冲就退出；-t 400 只取尾部，避免整段日志拖慢一次采样。
        var r = await _adb.LogcatTailAsync(400, ct).ConfigureAwait(false);
        if (!r.Ok || r.StdOut.Length == 0)
        {
            Note = "logcat 不可用（adb 未连接或没有日志权限）";
            return false;
        }

        long? raw = ParseTotalSteps(r.StdOut);
        if (raw is null)
        {
            Note = "日志里没有计步字段（App 可能没在跑，或版本没打这组日志）";
            return false;
        }

        var now = DateTime.UtcNow;
        double detected = 0;
        if (_lastRaw >= 0 && raw.Value >= _lastRaw)
        {
            double dt = (now - _lastAt).TotalSeconds;
            if (dt >= 0.9)
            {
                double delta = raw.Value - _lastRaw;
                detected = delta / dt * 60.0;
            }
        }

        bool fresh = detected > 0;
        if (fresh) DetectedCadence = detected;
        _lastRaw = raw.Value;
        _lastAt = now;
        Note = fresh
            ? $"实测 {detected:F0} spm（App 累计 {raw.Value} 步）"
            : $"App 累计 {raw.Value} 步（等待下一次差分）";
        return fresh;
    }

    /// <summary>
    /// 从一段日志里取出「App 自报的累计步数」。取最大值，因为同名键可能出现多次。
    /// 单独抽成静态方法是为了能脱离 adb 直接测。
    /// </summary>
    public static long? ParseTotalSteps(string log)
    {
        if (string.IsNullOrEmpty(log)) return null;

        long best = -1;
        foreach (Match m in TotalStepLine().Matches(log))
        {
            if (long.TryParse(m.Groups["n"].Value, out long v) && v > best) best = v;
        }
        if (best >= 0) return best;

        // 退一步：只有 step_delta（每次记的增量）时，把增量累加成总数。
        long sum = 0;
        bool any = false;
        foreach (Match m in DeltaLine().Matches(log))
        {
            if (long.TryParse(m.Groups["n"].Value, out long v) && v >= 0 && v < 100)
            {
                sum += v;
                any = true;
            }
        }
        return any ? sum : null;
    }

    [GeneratedRegex(@"(?:step_final_raw|step_source_count_raw|step_source_accelerate)\s*[=:]\s*(?<n>\d{1,7})")]
    private static partial Regex TotalStepLine();

    [GeneratedRegex(@"step_delta\s*[=:]\s*(?<n>\d{1,3})")]
    private static partial Regex DeltaLine();
}
