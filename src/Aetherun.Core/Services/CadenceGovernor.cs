namespace Aetherun.Core.Services;

/// <summary>
/// 步频闭环校正器。
///
/// 问题：我们发多少拍 ≠ 运动世界认多少步。中间隔着它自己的采样率、滤波、
/// 最小步间隔，所以设定 90 实测 67、设定 120 实测 90、设定 150 只剩 55。
/// 开环发拍永远修不掉这个偏差 —— 必须拿它自己算出来的步数当反馈，反过来调发送率。
///
/// 做法：比例校正，但改的是「发送率倍数」而不是直接改目标。
///   · 每收到一个实测样本，按误差比例调整 <see cref="Correction"/>；
///   · 单次调整幅度限幅，避免振荡；
///   · 上下限 [0.55, 3.2]，防止把泵推到无意义的空转；
///   · 连续多次推到上限而实测仍不涨，判定为「这台机器到顶了」，锁定倍数并如实上报。
/// </summary>
public sealed class CadenceGovernor
{
    private const double MinCorrection = 0.55;
    private const double MaxCorrection = 4.0;
    private const int SaturateSamples = 3;

    private int _weakSamples;

    public CadenceGovernor(double targetCadence)
    {
        Target = targetCadence <= 0 ? 0 : targetCadence;
    }

    /// <summary>用户设定的步频（步/分）。</summary>
    public double Target { get; }

    /// <summary>当前发送率倍数。1.0 = 不校正。</summary>
    public double Correction { get; private set; } = 1.0;

    /// <summary>最近一次实测（App 自报，步/分）。0 表示还没有样本。</summary>
    public double Measured { get; private set; }

    /// <summary>已判定为「推到上限也上不去」。</summary>
    public bool Saturated { get; private set; }

    /// <summary>实际要发给泵的步频。</summary>
    public double EffectiveTarget => Target <= 0 ? 0 : Target * Correction;

    /// <summary>投一个实测样本进去（步/分）。样本太小直接忽略，否则会被噪声带跑。</summary>
    public void Observe(double measuredCadence)
    {
        if (Target <= 0 || Saturated) return;
        if (measuredCadence < 8) return;      // 差分样本太小，不可信

        Measured = measuredCadence;
        double ratio = measuredCadence / Target;

        // 已经推到上限还差得远 → 记一次弱样本
        if (ratio < 0.75 && Correction > MaxCorrection * 0.85)
        {
            if (++_weakSamples >= SaturateSamples) Saturated = true;
        }
        else if (ratio >= 0.9)
        {
            _weakSamples = 0;
        }

        // 误差比例校正：差 25% 就多推约 15%，单次最多 ±12%/16%
        double step = Math.Clamp((1.0 - ratio) * 0.62, -0.12, 0.16);
        Correction = Math.Clamp(Correction * (1.0 + step), MinCorrection, MaxCorrection);
    }

    /// <summary>给界面看的一句话说明。</summary>
    public string Describe()
    {
        if (Target <= 0) return "步频已关闭";
        if (Measured <= 0) return $"目标 {Target:F0} spm · 等待实测样本";
        if (Saturated)
            return $"目标 {Target:F0} spm · 实测 {Measured:F0} spm · 这台机器已到上限（发送率 ×{Correction:F2}）";
        return $"目标 {Target:F0} spm · 实测 {Measured:F0} spm · 发送率 ×{Correction:F2}";
    }
}
