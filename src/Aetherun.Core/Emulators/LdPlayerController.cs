using Aetherun.Core.Models;
using Aetherun.Core.Services;

namespace Aetherun.Core.Emulators;

/// <summary>
/// 雷电模拟器（LDPlayer 9 / 14）。用 ldconsole.exe 改定位与重力。
/// 命令：
///   ldconsole locate --index N --LLI 经度,纬度     注意经度在前
///   ldconsole action --index N --key call.gravity --value 0,0,Z
/// </summary>
public sealed class LdPlayerController : IEmulatorController
{
    private readonly LdConsoleClient _client;

    public LdPlayerController(LdConsoleClient client)
    {
        _client = client;
        Index = client.EmulatorIndex;
    }

    public string DisplayName => "雷电模拟器";
    public int Index { get; }
    public bool IsConfigured => _client.IsConfigured;

    public async Task<bool> SetLocationAsync(GeoPoint point, CancellationToken ct = default)
    {
        var r = await _client.LocateAsync(point, ct).ConfigureAwait(false);
        return r.Ok;
    }

    public async Task<bool> SetGravityZAsync(double z, CancellationToken ct = default)
    {
        var r = await _client.GravityAsync(z, ct).ConfigureAwait(false);
        return r.Ok;
    }

    public async Task<string> DescribeAsync(CancellationToken ct = default)
    {
        var r = await _client.ListAsync(ct).ConfigureAwait(false);
        var text = r.StdOut.Trim();
        return text.Length == 0 ? (r.Ok ? "雷电实例列表为空" : r.StdErr.Trim()) : text;
    }

    public async Task<bool?> LaunchInstanceAsync(CancellationToken ct = default)
    {
        var r = await _client.LaunchAsync(ct).ConfigureAwait(false);
        return r.Ok;
    }
}
