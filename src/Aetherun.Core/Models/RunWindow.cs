using System.Globalization;

namespace Aetherun.Core.Models;

/// <summary>
/// 校园跑的有效时间窗口：每天 06:00–22:00。窗口外服务端不接受成绩，
/// 所以引擎在开跑前就挡掉，避免白跑一趟。
/// </summary>
public static class RunWindow
{
    /// <summary>窗口开始的小时（含）。</summary>
    public const int FromHour = 6;

    /// <summary>窗口结束的小时（不含）。</summary>
    public const int ToHour = 22;

    /// <summary>6:00 到 22:00 之间才算有效。</summary>
    public static bool IsOpen(DateTime local) => local.Hour >= FromHour && local.Hour < ToHour;

    public static string Label => $"{FromHour:00}:00–{ToHour:00}:00";

    /// <summary>给用户看的说明，包含当前是否有效。</summary>
    public static string Describe(DateTime local) =>
        IsOpen(local)
            ? $"有效时间 {Label}，现在可以跑"
            : $"有效时间 {Label}，当前 {local:HH:mm} 不在时段内";

    /// <summary>离下一次开窗还有多久（已开窗返回 0）。</summary>
    public static TimeSpan UntilOpen(DateTime local)
    {
        if (IsOpen(local)) return TimeSpan.Zero;
        var open = local.Date.AddHours(FromHour);
        if (local.Hour >= ToHour) open = open.AddDays(1);
        return open - local;
    }

    /// <summary>解析 "6" / "06:00" / "6:30" 这类输入，失败回退默认值。</summary>
    public static int ParseHour(string? text, int fallback)
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;
        var head = text.Split(':', '-', '：')[0].Trim();
        return int.TryParse(head, NumberStyles.Integer, CultureInfo.InvariantCulture, out var h)
               && h is >= 0 and <= 24
            ? h
            : fallback;
    }
}
