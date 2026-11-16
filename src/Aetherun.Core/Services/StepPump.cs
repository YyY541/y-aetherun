using System.Diagnostics;
using Aetherun.Core.Emulators;

namespace Aetherun.Core.Services;

/// <summary>一帧步频注入的实测统计，界面用它解释「为什么没跑到设定步频」。</summary>
public readonly record struct StepPumpStats(
    int Steps,
    double EffectiveCadence,
    double LatencyMs,
    double MaxCadence,
    bool Limited);

/// <summary>
/// 步频注入泵。
///
/// 原来步频注入和定位注入挤在同一个 20ms 循环里顺序 await，一次
/// <c>ldconsole action --key call.gravity</c> 最坏能卡到 4 秒超时，卡住期间
/// 拍子全被 <c>catchUp &lt; 2</c> 的限次丢掉，于是设定 150 spm 实际只剩 60 多。
///
/// 这一版的做法：
///   · 独立 Task，按**绝对时钟**拍发，绝不被定位注入阻塞；
///   · 落后只补当前这一拍，永不补历史欠账（补账会形成正反馈，越追越慢）；
///   · 并发数按实测延迟自适应（2~6 个 call.gravity 同时在飞），
///     把进程启动的固定开销重叠掉，吞吐 = 并发数 / 单次延迟；
///   · 实测每次调用耗时并换算成「这台机器能跑到的步频上限」，
///     顶到上限就如实上报，不假装跑到了。
///
/// 步进方式不变：每半拍把重力 Z 轴在高低两个值之间翻一次，翻到高点记一步
/// —— 运动世界就是靠加速度计的这个起伏计步的。
/// </summary>
public sealed class StepPump
{
    private readonly IEmulatorController _emu;
    private readonly Func<DateTime> _clock;
    private readonly int _maxInFlight;

    private long _steps;
    private long _cadenceBits = BitConverter.DoubleToInt64Bits(0);
    private double _emaLatencySec;
    private int _inFlight;
    private readonly object _lock = new();
    private readonly Queue<double> _recentStepAt = new();

    /// <summary>一次注入最多等多久。超过就当作失败，不记步。</summary>
    private static readonly TimeSpan FireTimeout = TimeSpan.FromSeconds(4);

    public StepPump(IEmulatorController emu, Func<DateTime> clock, int maxInFlight = 8)
    {
        _emu = emu ?? throw new ArgumentNullException(nameof(emu));
        _clock = clock ?? (() => DateTime.Now);
        _maxInFlight = Math.Clamp(maxInFlight, 1, 12);
    }

    /// <summary>
    /// 当前发送率（步/分）。可以在跑动中被改写 —— 闭环校正就是把
    /// 「用户目标 × 校正倍数」写到这里，泵下一拍就按新速率走。
    /// </summary>
    public double TargetCadence
    {
        get => BitConverter.Int64BitsToDouble(Interlocked.Read(ref _cadenceBits));
        set => Interlocked.Exchange(ref _cadenceBits, BitConverter.DoubleToInt64Bits(value <= 0 ? 0 : value));
    }

    /// <summary>已经注入成功的步数。</summary>
    public int Steps => (int)Interlocked.Read(ref _steps);

    /// <summary>最近 6 秒的实测步频，没有任何平滑伪装。</summary>
    public double EffectiveCadence
    {
        get
        {
            lock (_lock)
            {
                if (_recentStepAt.Count < 2) return 0;
                double now = Now();
                double[] arr = _recentStepAt.ToArray();
                double window = Math.Min(6.0, now - arr[0]);
                if (window < 0.8) return 0;
                return (arr.Length - 1) / window * 60.0;
            }
        }
    }

    /// <summary>实测单次注入耗时（秒），指数滑动平均。</summary>
    public double LatencySec => _emaLatencySec;

    /// <summary>当前实际用到的并发数。</summary>
    public int InFlight => Volatile.Read(ref _inFlight);

    /// <summary>这台机器 + 这个模拟器理论上能跑到的步频上限（步/分）。</summary>
    public double MaxCadence
    {
        get
        {
            double lat = _emaLatencySec;
            if (lat <= 1e-4) return 600;   // 还没测出来，先不设限
            // 一步要两次调用（翻高、翻低），并发 _maxInFlight 时吞吐 = 并发 / 延迟
            return 30.0 / lat * _maxInFlight;
        }
    }

    /// <summary>为达到目标步频需要的并发数：并发 ≥ 目标调用率 × 单次延迟。</summary>
    private int ConcurrencyFor(double cadencePerMin)
    {
        double lat = _emaLatencySec;
        if (lat <= 1e-4) return 2;
        double callsPerSec = cadencePerMin / 30.0;          // 每秒调用次数
        int need = (int)Math.Ceiling(callsPerSec * lat) + 1; // +1 给排队留余量
        return Math.Clamp(need, 2, _maxInFlight);
    }

    public StepPumpStats Stats(double targetCadence)
    {
        double eff = EffectiveCadence;
        double max = MaxCadence;
        bool limited = _emaLatencySec > 1e-4 && targetCadence > max * 0.95;
        return new StepPumpStats(Steps, eff, _emaLatencySec * 1000.0, max, limited);
    }

    public async Task RunAsync(double cadencePerMin, double gravityLow, double gravityHigh,
        CancellationToken ct)
    {
        TargetCadence = cadencePerMin;
        if (TargetCadence <= 0) return;

        bool zUp = false;
        long t0 = Stopwatch.GetTimestamp();
        double nextBeatAt = 0;   // 相对 t0 的秒数

        while (!ct.IsCancellationRequested)
        {
            double elapsed = Stopwatch.GetElapsedTime(t0).TotalSeconds;

            if (nextBeatAt - elapsed > 0.0015)
            {
                // 没到下一拍：睡到该发的那一刻，但最多 20ms，
                // 保证取消能立刻生效、节拍也不因休眠粒度漂移。
                double waitMs = (nextBeatAt - elapsed) * 1000.0;
                if (waitMs > 1.5) await Task.Delay((int)Math.Ceiling(waitMs), ct).ConfigureAwait(false);
                else await Task.Yield();
                continue;
            }

            double cadence = TargetCadence;
            if (cadence <= 0) { await Task.Delay(20, ct).ConfigureAwait(false); continue; }

            int limit = ConcurrencyFor(cadence);
            while (Volatile.Read(ref _inFlight) >= limit && !ct.IsCancellationRequested)
                await Task.Delay(2, ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested) break;

            zUp = !zUp;
            double z = zUp ? gravityHigh : gravityLow;
            bool countStep = zUp;

            Interlocked.Increment(ref _inFlight);
            _ = FireAsync(z, countStep, Stopwatch.GetTimestamp());

            // 按当前速率推进半拍；落后就把下一拍定在「现在 + 半拍」，
            // 只补当前这一拍、永不补历史欠账（补账会越追越慢）。
            double half = 30.0 / cadence;
            nextBeatAt += half;
            if (nextBeatAt <= elapsed) nextBeatAt = elapsed + half;
        }
    }

    private double Now() => _clock().Ticks / (double)TimeSpan.TicksPerSecond;

    private async Task FireAsync(double z, bool countStep, long startedTick)
    {
        try
        {
            // 用独立超时：ldconsole 挂死时不能让整条泵停在这里。
            using var cts = new CancellationTokenSource(FireTimeout);
            bool ok = await _emu.SetGravityZAsync(z, cts.Token).ConfigureAwait(false);
            if (!ok) countStep = false;
        }
        catch
        {
            countStep = false;
        }
        finally
        {
            double sec = Stopwatch.GetElapsedTime(startedTick).TotalSeconds;
            // 4 秒超时那种异常样本会把平均值拉爆，超过 3 秒直接不采信
            if (sec < 3.0)
            {
                _emaLatencySec = _emaLatencySec <= 1e-6
                    ? sec
                    : _emaLatencySec * 0.78 + sec * 0.22;
            }

            if (countStep)
            {
                Interlocked.Increment(ref _steps);
                lock (_lock)
                {
                    double now = Now();
                    _recentStepAt.Enqueue(now);
                    while (_recentStepAt.Count > 0 && now - _recentStepAt.Peek() > 6.0)
                        _recentStepAt.Dequeue();
                }
            }

            Interlocked.Decrement(ref _inFlight);
        }
    }
}
