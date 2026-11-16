using System.Text.Json;
using Aetherun.Core.Models;

namespace Aetherun.Desktop.Services;

/// <summary>
/// 配置落盘。原来所有设置在关掉软件后就没了——用户在「设置」里填的
/// ldconsole.exe 路径、adb 序列号、重力摆幅，下次启动全回到默认值，
/// 于是每次都得重新填一遍。这里存到 %APPDATA%\Y-Aetherun\config.json。
/// 读取失败一律回退默认值，不因为一个坏文件就打不开软件。
/// </summary>
public static class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static string Directory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Y-Aetherun");

    public static string FilePath => Path.Combine(Directory, "config.json");

    public static RunConfig Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return RunConfig.Preset("standard");
            var json = File.ReadAllText(FilePath);
            var cfg = JsonSerializer.Deserialize<RunConfig>(json, Options);
            if (cfg is null) return RunConfig.Preset("standard");

            // 先按版本迁移（例如 2.7.0 之前倒计时默认 20 秒 → 15 秒），
            // 再过一遍 Sanitized：存下来的值可能来自旧版本，区间也更宽。
            return cfg.Migrated().Sanitized();
        }
        catch
        {
            return RunConfig.Preset("standard");
        }
    }

    public static string TrackPath => Path.Combine(Directory, "track.txt");

    /// <summary>读用户改过的跑道点位；没有或读不出来就用内置操场。</summary>
    public static string LoadTrackText()
    {
        try
        {
            if (File.Exists(TrackPath))
            {
                var text = File.ReadAllText(TrackPath);
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
        }
        catch { }
        return "";
    }

    public static void SaveTrackText(string text)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(TrackPath, text);
        }
        catch { }
    }

    public static void Save(RunConfig cfg)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var json = JsonSerializer.Serialize(cfg.Sanitized(), Options);
            // 先写临时文件再替换，避免掉电/被杀时留下半截 json
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch
        {
            // 存不下就算了，不能因为写文件失败让软件崩
        }
    }
}
