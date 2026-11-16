using System.Diagnostics;
using System.Text;

namespace Aetherun.Core.Services;

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(string fileName, string arguments, TimeSpan timeout, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return ProcessResult.Fail("未指定可执行文件");

        var psi = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        try
        {
            using var p = new Process { StartInfo = psi };
            if (!p.Start())
                return ProcessResult.Fail("进程启动失败");

            var so = p.StandardOutput.ReadToEndAsync(ct);
            var se = p.StandardError.ReadToEndAsync(ct);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);

            bool finished;
            try
            {
                await p.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                finished = true;
            }
            catch (OperationCanceledException)
            {
                finished = false;
                try { p.Kill(entireProcessTree: true); } catch { }
            }

            string outText = "", errText = "";
            try { outText = await so.ConfigureAwait(false); } catch { }
            try { errText = await se.ConfigureAwait(false); } catch { }

            if (!finished)
                return new ProcessResult(-2, outText, errText, true);

            return new ProcessResult(p.ExitCode, outText, errText, false);
        }
        catch (Exception ex)
        {
            return ProcessResult.Fail(ex.Message);
        }
    }
}
