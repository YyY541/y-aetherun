using System.Text.RegularExpressions;

namespace Aetherun.Core.Services;

/// <summary>adb 命令封装：拉起/停止 App、点开始按钮。</summary>
public sealed partial class AdbClient
{
    private readonly IProcessRunner _runner;
    private readonly string _exe;
    private readonly bool _verifyPath;

    public AdbClient(IProcessRunner runner, string exePath, string serial, bool verifyPath = true)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _exe = exePath ?? "";
        _verifyPath = verifyPath;
        Serial = serial ?? "127.0.0.1:5555";
    }

    public string Serial { get; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_exe) && (!_verifyPath || File.Exists(_exe));

    private Task<ProcessResult> Shell(string args, CancellationToken ct, int timeoutSec = 10)
        => IsConfigured
            ? _runner.RunAsync(_exe, $"-s {Serial} {args}", TimeSpan.FromSeconds(timeoutSec), ct)
            : Task.FromResult(ProcessResult.Fail("adb 未配置"));

    public Task<ProcessResult> ConnectAsync(CancellationToken ct = default)
        => IsConfigured
            ? _runner.RunAsync(_exe, $"connect {Serial}", TimeSpan.FromSeconds(8), ct)
            : Task.FromResult(ProcessResult.Fail("adb 未配置"));

    public Task<ProcessResult> ForceStopAsync(string package, CancellationToken ct = default)
        => Shell($"shell am force-stop {package}", ct);

    public Task<ProcessResult> LaunchAsync(string package, CancellationToken ct = default)
        => Shell($"shell monkey -p {package} -c android.intent.category.LAUNCHER 1", ct);

    public async Task<bool> TapStartButtonAsync(CancellationToken ct = default)
    {
        var dump = await Shell("shell uiautomator dump /sdcard/_r2k.xml", ct, 15).ConfigureAwait(false);
        if (!dump.Ok) return false;

        var xml = await Shell("shell cat /sdcard/_r2k.xml", ct, 15).ConfigureAwait(false);
        if (!xml.Ok || xml.StdOut.Length == 0) return false;

        var hit = FindStartNode(xml.StdOut);
        if (hit is null) return false;

        var (x, y) = hit.Value;
        var tap = await Shell($"shell input tap {x} {y}", ct).ConfigureAwait(false);
        return tap.Ok;
    }

    /// <summary>
    /// 抓 logcat 的尾部若干行。步频闭环要用运动世界自己算的步数，
    /// 只能从它的日志里读（<c>step_final_raw</c> 一族）。
    /// </summary>
    public Task<ProcessResult> LogcatTailAsync(int lines = 400, CancellationToken ct = default)
        => Shell($"logcat -d -t {Math.Clamp(lines, 50, 3000)} -v brief", ct, 12);

    /// <summary>读当前前台应用包名；读不到返回 null（不代表不在前台）。</summary>
    public async Task<string?> GetForegroundPackageAsync(CancellationToken ct = default)
    {
        // dumpsys window 比 dumpsys activity 快一半，且 Android 10+ 都还在。
        // 输出形如：mCurrentFocus=Window{8f2a1b u0 com.zjwh.android_wh_physicalfitness/...}
        var r = await Shell("shell dumpsys window", ct, 8).ConfigureAwait(false);
        if (!r.Ok || r.StdOut.Length == 0) return null;

        return ParseForeground(r.StdOut);
    }

    /// <summary>
    /// 从 <c>dumpsys window</c> 的输出里解析前台包名。
    /// 单独抽出来是为了能脱离 adb 直接测：真机上动不了的解析逻辑最容易悄悄写错。
    /// </summary>
    public static string? ParseForeground(string dumpsys)
    {
        if (string.IsNullOrEmpty(dumpsys)) return null;

        var m = FocusLine().Match(dumpsys);
        if (!m.Success) return null;

        string token = m.Groups[1].Value.Trim();
        int slash = token.IndexOf('/');
        if (slash > 0) token = token[..slash];
        // 系统弹窗、输入法、状态栏这些不是「用户打开了 App」
        if (token.Length == 0) return null;
        if (token is "android" or "com.android.systemui") return null;
        if (token.StartsWith("com.android.inputmethod", StringComparison.Ordinal)) return null;
        return token;
    }

    [GeneratedRegex(@"mCurrentFocus=Window\{[^}]*?\s([A-Za-z0-9_.]+/[A-Za-z0-9_.$]+)\}")]
    private static partial Regex FocusLine();

    [GeneratedRegex("<node[^>]*clickable=\"true\"[^>]*>")]
    private static partial Regex ClickableNode();

    [GeneratedRegex("bounds=\"\\[(\\d+),(\\d+)\\]\\[(\\d+),(\\d+)\\]\"")]
    private static partial Regex BoundsRegex();

    // 原来的正则末尾多了一个 ]，要求文案后面紧跟右方括号，
    // 而 node 里 text="" 后面通常是空格或下一个属性 → 永远匹配不到，
    // 表现为「自动点击开始」从来没成功过。
    [GeneratedRegex("(?:text|content-desc)=\"([^\"]*)\"")]
    private static partial Regex LabelRegex();

    /// <summary>在 uiautomator dump 里找「开始」按钮中心点。</summary>
    public static (int X, int Y)? FindStartNode(string xml)
    {
        if (string.IsNullOrEmpty(xml)) return null;

        foreach (Match m in ClickableNode().Matches(xml))
        {
            string node = m.Value;

            // 一个 node 里 text 与 content-desc 可能同时存在，且 text 常为空；
            // 只要任意一个带「开始/出发/立即」就算命中。
            string? label = null;
            foreach (Match lm in LabelRegex().Matches(node))
            {
                string v = lm.Groups[1].Value;
                if (v.Length == 0) continue;
                if (!v.Contains("开始") && !v.Contains("出发") && !v.Contains("立即")) continue;
                if (v.Contains("开始时间") || v.Contains("开始日期")) continue;
                label = v;
                break;
            }
            if (label is null) continue;

            var bm = BoundsRegex().Match(node);
            if (!bm.Success) continue;

            int x1 = int.Parse(bm.Groups[1].Value);
            int y1 = int.Parse(bm.Groups[2].Value);
            int x2 = int.Parse(bm.Groups[3].Value);
            int y2 = int.Parse(bm.Groups[4].Value);

            return ((x1 + x2) / 2, (y1 + y2) / 2);
        }
        return null;
    }
}
