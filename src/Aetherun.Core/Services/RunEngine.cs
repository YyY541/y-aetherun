using Aetherun.Core.Emulators;
using Aetherun.Core.Models;

namespace Aetherun.Core.Services;

/// <summary>
/// 跑步引擎：按固定步长推进里程，同时驱动定位注入与步频注入。
/// 与界面完全解耦——界面只订阅 Snapshot 事件。
/// 与模拟器也解耦——只依赖 <see cref="IEmulatorController"/>，换模拟器不用改这里。
/// </summary>
public sealed class RunEngine
{
    private readonly IEmulatorController _emu;
    private readonly AdbClient? _adb;
    private readonly Func<DateTime> _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private CancellationTokenSource? _cts;
    private Task? _loop;

    public RunEngine(IEmulatorController emulator, AdbClient? adb = null, Func<DateTime>? clock = null)
    {
        _emu = emulator ?? throw new ArgumentNullException(nameof(emulator));
        _adb = adb;
        // 本地时间：界面上的「当前 HH:mm」「有效窗口」都按本地算，
        // 这里用 UtcNow 会让日志时间戳比界面早 8 小时。
        _clock = clock ?? (() => DateTime.Now);
    }

    public RunSnapshot Current { get; private set; } = RunSnapshot.Idle();

    public event EventHandler<RunSnapshot>? Snapshot;
    public event EventHandler<string>? Log;

    public bool IsBusy => _loop is { IsCompleted: false };

    public async Task StartAsync(RunConfig config, TrackLoop track, CancellationToken outer = default)
    {
        await _gate.WaitAsync(outer).ConfigureAwait(false);
        try
        {
            if (IsBusy) throw new InvalidOperationException("已经有跑步任务在运行");

            var cfg = config.Sanitized();
            _cts?.Dispose();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(outer);
            var ct = _cts.Token;

            _loop = Task.Run(() => LoopAsync(cfg, track, ct), CancellationToken.None);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync()
    {
        var cts = _cts;
        var loop = _loop;
        if (cts is null) return;

        try { cts.Cancel(); } catch { }
        if (loop is not null)
        {
            try { await loop.ConfigureAwait(false); } catch { }
        }
    }

    // 主循环 20ms 一拍，但界面不需要 50 次/秒。阶段性变化（Armed/Done/...）
    // 必须立刻送，Running 的数值帧每 100ms 送一次就够，否则属性通知会把 UI 线程
    // 排队排满，滑块和按钮都开始卡。
    private const double EmitIntervalSec = 0.10;
    private DateTime _lastEmit = DateTime.MinValue;

    private void Emit(RunSnapshot s)
    {
        var now = _clock();
        // 只有 Running 的数值帧需要限频；Armed/Done/Stopped/Error 是状态切换，
        // 丢掉一次界面就卡在旧阶段上，必须原样送出去。
        if (s.Phase == RunPhase.Running
            && (now - _lastEmit).TotalSeconds < EmitIntervalSec)
        {
            Current = s;   // 内部状态照样更新，只是不往界面推
            return;
        }

        _lastEmit = now;
        Current = s;
        Snapshot?.Invoke(this, s);
    }

    private void Say(string msg) => Log?.Invoke(this, msg);

    private async Task LoopAsync(RunConfig cfg, TrackLoop track, CancellationToken ct)
    {
        var phase = RunPhase.Armed;
        var start = _clock();
        double steps = 0;
        int fixes = 0;
        int missFixes = 0;
        var nextFixAt = start;
        GeoPoint last = track.At(0);

        Emit(new RunSnapshot
        {
            Phase = phase,
            Message = "钉住起点",
            TargetMeters = cfg.TargetMeters,
            Position = last,
            Timestamp = _clock(),
        });

        try
        {
            if (cfg.GeoEnabled)
            {
                var ok = await _emu.SetLocationAsync(last, ct).ConfigureAwait(false);
                Say(ok ? $"起点已钉住 {last}" : $"起点钉住失败，检查 {_emu.DisplayName} 路径与实例序号");
            }

            if (cfg.AutoStartApp && _adb is not null && _adb.IsConfigured)
            {
                Say("拉起运动世界");
                await _adb.ConnectAsync(ct).ConfigureAwait(false);
                await _adb.ForceStopAsync(cfg.PackageName, ct).ConfigureAwait(false);
                await Task.Delay(600, ct).ConfigureAwait(false);
                await _adb.LaunchAsync(cfg.PackageName, ct).ConfigureAwait(false);
                await Task.Delay(5000, ct).ConfigureAwait(false);

                bool tapped = await _adb.TapStartButtonAsync(ct).ConfigureAwait(false);
                Say(tapped ? "已点击开始" : "未找到开始按钮，请手动点击");
            }

            // 倒计时：每秒钉一次起点，避免 App 冷启动读到旧坐标。
            //
            // 倒计时会被「运动世界不在前台」卡住（CountdownHeld）：模拟器里
            // 只要把 App 切到后台，定位与计步就都不进账，倒计时跑完也是白跑。
            // 所以这里每 1.5 秒查一次前台包名，不在前台就暂停倒数并提示，
            // 期间再自动拉起一次；用户切回来倒数接着走。
            double heldSec = 0;
            bool relaunched = false;
            var nextProbeAt = start;
            bool? lastForeground = null;
            string lastPkg = "";

            while ((_clock() - start).TotalSeconds - heldSec < cfg.LeadSeconds)
            {
                ct.ThrowIfCancellationRequested();
                double remain = cfg.LeadSeconds - ((_clock() - start).TotalSeconds - heldSec);

                if (cfg.GeoEnabled && _clock() >= nextFixAt)
                {
                    await _emu.SetLocationAsync(last, ct).ConfigureAwait(false);
                    nextFixAt = _clock().AddSeconds(1);
                }

                // ---- 前台检测 ----
                bool canProbe = cfg.CheckForeground && _adb is not null && _adb.IsConfigured;
                if (canProbe && _clock() >= nextProbeAt)
                {
                    var fg = await _adb!.GetForegroundPackageAsync(ct).ConfigureAwait(false);
                    if (fg is not null)
                    {
                        bool ok = fg == cfg.PackageName;
                        if (lastForeground != ok || fg != lastPkg)
                        {
                            Say(ok
                                ? "运动世界已在前台，倒计时继续"
                                : $"运动世界不在前台（当前 {fg}），倒计时已暂停");
                            lastForeground = ok;
                            lastPkg = fg;
                        }

                        if (!ok && !relaunched)
                        {
                            // 只自动拉一次，反复拉起会把用户正在做的事打断
                            relaunched = true;
                            Say("尝试自动拉起运动世界");
                            await _adb.LaunchAsync(cfg.PackageName, ct).ConfigureAwait(false);
                        }
                    }
                    nextProbeAt = _clock().AddSeconds(1.5);
                }

                bool held = canProbe && lastForeground == false;
                if (held)
                {
                    heldSec += 0.1;
                    if (Math.Abs(heldSec % 1.0) < 0.051)
                    {
                        Say("请先在模拟器里打开运动世界并停到开始界面，倒计时会等你");
                    }
                }

                int left = (int)Math.Ceiling(Math.Max(0, remain));
                Emit(new RunSnapshot
                {
                    Phase = RunPhase.Armed,
                    Message = held
                        ? "运动世界不在前台，倒计时暂停"
                        : $"等待开始 {left} 秒",
                    TargetMeters = cfg.TargetMeters,
                    Position = last,
                    Timestamp = _clock(),
                    ForegroundOk = canProbe ? lastForeground : null,
                    ForegroundPackage = lastPkg,
                    CountdownHeld = held,
                    CountdownRemaining = Math.Max(0, remain),
                    CountdownTotal = cfg.LeadSeconds,
                });

                await Task.Delay(100, ct).ConfigureAwait(false);
            }

            // 倒计时走完时目标 App 一次都没到过前台 → 这一趟服务端大概率不计成绩。
            // 记在快照上，界面挂警告，别只写在日志里让用户漏掉。
            bool foregroundMissed = lastForeground == false;
            if (foregroundMissed)
            {
                Say("倒计时结束，但运动世界始终不在前台：本次可能不计成绩");
            }

            phase = RunPhase.Running;
            start = _clock();
            var runStart = start;
            nextFixAt = runStart;
            Say("开始走位");

            // 步频交给独立的泵：它自己一条 Task、按绝对时钟拍发，
            // 不再被定位注入（每秒一次的 ldconsole，最坏卡 4 秒）挡住。
            var pump = new StepPump(_emu, _clock);
            using var pumpCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var pumpTask = cfg.StepEnabled
                ? Task.Run(() => pump.RunAsync(cfg.CadencePerMin, cfg.GravityLow, cfg.GravityHigh,
                    pumpCts.Token), CancellationToken.None)
                : Task.CompletedTask;

            // 闭环：拿运动世界自己认的步数反过来调发送率。
            // 开环只能保证「我们发了多少拍」，保证不了「它认了多少步」。
            var gov = new CadenceGovernor(cfg.CadencePerMin);
            var feedback = _adb is not null && cfg.StepEnabled ? new StepFeedback(_adb) : null;
            var feedbackTask = feedback is not null
                ? Task.Run(() => FeedbackLoopAsync(feedback, gov, pump, pumpCts.Token), CancellationToken.None)
                : Task.CompletedTask;

            bool limitedWarned = false;

            try
            {
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                double elapsed = (_clock() - runStart).TotalSeconds;
                double dist = cfg.SpeedMps * elapsed;

                if (dist >= cfg.TargetMeters)
                {
                    Emit(new RunSnapshot
                    {
                        Phase = RunPhase.Done,
                        Message = "已达标",
                        DistanceMeters = cfg.TargetMeters,
                        TargetMeters = cfg.TargetMeters,
                        Steps = pump.Steps,
                        ElapsedSeconds = elapsed,
                        Position = last,
                        FixCount = fixes,
                        ForegroundMissed = foregroundMissed,
                        LiveCadence = pump.EffectiveCadence,
                        PumpLatencyMs = pump.LatencySec * 1000.0,
                        MaxCadence = pump.MaxCadence,
                        CadenceLimited = pump.Stats(gov.EffectiveTarget).Limited,
                        DetectedCadence = gov.Measured,
                        CadenceNote = gov.Describe(),
                        Timestamp = _clock(),
                    });
                    Say($"完成：{cfg.TargetMeters:F0} 米，{pump.Steps} 步"
                        + $"（发出 {pump.EffectiveCadence:F0} spm"
                        + (gov.Measured > 0 ? $"，App 认 {gov.Measured:F0} spm）" : "）"));
                    if (missFixes > 0) Say($"提示：有 {missFixes} 次定位注入失败，可能影响里程判定");
                    return;
                }

                // 定位：每秒一次
                if (cfg.GeoEnabled && _clock() >= nextFixAt)
                {
                    last = track.At(dist);
                    var ok = await _emu.SetLocationAsync(last, ct).ConfigureAwait(false);
                    if (ok) fixes++; else missFixes++;
                    // 以「这次注入完成之后」为基准，不要用调用前的时间算，
                    // 否则一次 300ms 的调用会让下一拍提前，长期偏快。
                    nextFixAt = _clock().AddSeconds(1);
                }

                // 步频：泵在另一条 Task 上按绝对时钟拍发，这里只读它记的步数。
                // 原来步频和定位挤在同一个循环里顺序 await，一次 ldconsole
                // 卡 4 秒就丢一大段拍子，设定 150 实际只剩 60 多。
                var stats = pump.Stats(gov.EffectiveTarget);
                steps = stats.Steps;

                if (stats.Limited && !limitedWarned)
                {
                    limitedWarned = true;
                    Say($"注入通道上限约 {stats.MaxCadence:F0} spm（单次 {stats.LatencyMs:F0} ms），"
                        + $"目标 {cfg.CadencePerMin:F0} spm 已尽力驱动");
                }

                Emit(new RunSnapshot
                {
                    Phase = phase,
                    Message = "跑步中",
                    DistanceMeters = dist,
                    TargetMeters = cfg.TargetMeters,
                    Steps = stats.Steps,
                    ElapsedSeconds = elapsed,
                    Position = last,
                    FixCount = fixes,
                    ForegroundMissed = foregroundMissed,
                    LiveCadence = stats.EffectiveCadence,
                    PumpLatencyMs = stats.LatencyMs,
                    MaxCadence = stats.MaxCadence,
                    CadenceLimited = stats.Limited,
                    DetectedCadence = gov.Measured,
                    CadenceNote = gov.Describe(),
                    Timestamp = _clock(),
                });

                await Task.Delay(20, ct).ConfigureAwait(false);
            }
            }
            finally
            {
                // 里程跑完/用户停止/异常都要把泵停掉，否则它会一直发重力事件，
                // 关掉界面后模拟器还在被灌数据。
                pumpCts.Cancel();
                try { await pumpTask.ConfigureAwait(false); } catch { }
                try { await feedbackTask.ConfigureAwait(false); } catch { }
            }
        }
        catch (OperationCanceledException)
        {
            Emit(new RunSnapshot
            {
                Phase = RunPhase.Stopped,
                Message = "已停止",
                DistanceMeters = Current.DistanceMeters,
                TargetMeters = cfg.TargetMeters,
                Steps = (int)steps,
                ElapsedSeconds = (_clock() - start).TotalSeconds,
                Position = last,
                FixCount = fixes,
                Timestamp = _clock(),
            });
            Say("用户停止");
        }
        catch (Exception ex)
        {
            Emit(new RunSnapshot
            {
                Phase = RunPhase.Error,
                Message = ex.Message,
                TargetMeters = cfg.TargetMeters,
                Position = last,
                FixCount = fixes,
                Timestamp = _clock(),
            });
            Say($"错误：{ex.Message}");
        }
    }
    /// <summary>
    /// 闭环采样：每 3 秒读一次运动世界的 logcat，拿它自报的累计步数差分出实测步频，
    /// 投给校正器，再把校正后的发送率写回泵。
    /// 单独一条 Task，因为它要跑 adb（几百毫秒），不能被它拖慢定位循环。
    /// </summary>
    private static async Task FeedbackLoopAsync(StepFeedback feedback, CadenceGovernor gov,
        StepPump pump, CancellationToken ct)
    {
        try
        {
            // 起跑先等一会：App 刚点开始还没出第一组日志，采样只会拿到空。
            await Task.Delay(4000, ct).ConfigureAwait(false);
            while (!ct.IsCancellationRequested)
            {
                if (await feedback.SampleAsync(ct).ConfigureAwait(false))
                {
                    gov.Observe(feedback.DetectedCadence);
                    pump.TargetCadence = gov.EffectiveTarget;
                }
                await Task.Delay(3000, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch { }
    }

}
