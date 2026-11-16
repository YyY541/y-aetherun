namespace Aetherun.Core.Services;

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr, bool TimedOut)
{
    public bool Ok => !TimedOut && ExitCode == 0;
    public static ProcessResult Fail(string msg) => new(-1, "", msg, false);
}

/// <summary>外部进程调用的抽象，便于测试与替换实现。</summary>
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(string fileName, string arguments, TimeSpan timeout, CancellationToken ct = default);
}
