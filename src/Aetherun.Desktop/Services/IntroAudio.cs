using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Aetherun.Desktop.Controls;
using Avalonia.Platform;

namespace Aetherun.Desktop.Services;

/// <summary>
/// 开屏动画的配乐。不引第三方音频库：Windows 上用 winmm 的 PlaySound，
/// 其它平台静默跳过（动画照样播）。
/// </summary>
public static class IntroAudio
{
    private const uint SndAsync = 0x0001;
    private const uint SndNoDefault = 0x0002;
    private const uint SndFilename = 0x00020000;

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool PlaySound(string? sound, IntPtr module, uint flags);

    [SupportedOSPlatform("windows")]
    private static void PlayWin(string path)
        => PlaySound(path, IntPtr.Zero, SndAsync | SndNoDefault | SndFilename);

    [SupportedOSPlatform("windows")]
    private static void StopWin()
        => PlaySound(null, IntPtr.Zero, 0);

    private static string? _cachedPath;

    /// <summary>把内嵌的 wav 落到临时目录，返回路径（只做一次）。</summary>
    private static string? Materialize()
    {
        if (_cachedPath is not null && File.Exists(_cachedPath)) return _cachedPath;

        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://Aetherun/Assets/audio/intro.wav"));
            string path = Path.Combine(Path.GetTempPath(), "aetherun-intro.wav");
            using (var fs = File.Create(path))
                stream.CopyTo(fs);
            _cachedPath = path;
            return path;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>异步起播；失败就当没有声音，不影响动画。</summary>
    public static void Play()
    {
        if (!OperatingSystem.IsWindows()) return;
        string? path = Materialize();
        if (path is null) return;

        try { PlayWin(path); } catch { /* 没有声卡/被占用就算了 */ }
    }

    /// <summary>停掉（跳过开屏时调用）。</summary>
    public static void Stop()
    {
        if (!OperatingSystem.IsWindows()) return;
        try { StopWin(); } catch { }
    }

    /// <summary>动画总时长，供进度条对齐。</summary>
    public static TimeSpan Duration => TimeSpan.FromSeconds((double)SplashPlayer.FrameCount / SplashPlayer.Fps);
}
