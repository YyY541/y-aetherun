using System.Globalization;
using Aetherun.Core.Models;

namespace Aetherun.Core.Services;

/// <summary>ldconsole.exe 的命令封装。定位与传感器注入都走这里。</summary>
public sealed class LdConsoleClient
{
    private readonly IProcessRunner _runner;
    private readonly string _exe;
    private readonly bool _verifyPath;

    /// <param name="verifyPath">生产环境为 true（要求文件存在）；测试可置 false 以注入假执行器。</param>
    public LdConsoleClient(IProcessRunner runner, string exePath, int emulatorIndex = 0, bool verifyPath = true)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _exe = exePath ?? "";
        _verifyPath = verifyPath;
        EmulatorIndex = emulatorIndex;
    }

    public int EmulatorIndex { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_exe) && (!_verifyPath || File.Exists(_exe));

    /// <summary>--LLI 参数是「经度,纬度」，极易写反。</summary>
    public Task<ProcessResult> LocateAsync(GeoPoint p, CancellationToken ct = default)
        => Send($"locate --index {EmulatorIndex} --LLI {p.ToLli()}", ct);

    public Task<ProcessResult> GravityAsync(double z, CancellationToken ct = default)
    {
        // 同理：小数点必须是「.」，否则 --value 会被拆成四个分量
        var v = string.Format(CultureInfo.InvariantCulture, "0,0,{0:F2}", z);
        return Send($"action --index {EmulatorIndex} --key call.gravity --value {v}", ct);
    }

    public Task<ProcessResult> LaunchAsync(CancellationToken ct = default)
        => Send($"launch --index {EmulatorIndex}", ct);

    public Task<ProcessResult> ListAsync(CancellationToken ct = default)
        => Send("list2", ct);

    private Task<ProcessResult> Send(string args, CancellationToken ct)
    {
        if (!IsConfigured)
            return Task.FromResult(ProcessResult.Fail("ldconsole.exe 未配置或路径不存在"));
        return _runner.RunAsync(_exe, args, TimeSpan.FromSeconds(4), ct);
    }
}
