using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Aetherun.Core.Models;
using Aetherun.Core.Services;
using Aetherun.Desktop.Services;

namespace Aetherun.Desktop.ViewModels;

public enum NavKey
{
    Dashboard,
    Track,
    Splash,
    Settings,
    Logs,
    About
}

public sealed partial class MainViewModel : ViewModelBase
{
    private readonly AppShell _shell;
    private RunEngine? _engine;

    public MainViewModel(AppShell shell)
    {
        _shell = shell;
        // 上次关软件前填的路径、序列号、摆幅都存在这里，没有就用标准预设
        Config = ConfigStore.Load();
        // 跑道点位也可能被用户改过，存在 track.txt 里
        var savedTrack = ConfigStore.LoadTrackText();
        Track = (TrackLoopFactory.Parse(savedTrack) ?? TrackLoopFactory.Default());
        TrackPointsText = string.IsNullOrWhiteSpace(savedTrack)
            ? TrackLoopFactory.DefaultText()
            : savedTrack;

        Pages = new ObservableCollection<NavItem>
        {
            new(NavKey.Dashboard, "仪表盘", "实时里程与进度", "◎"),
            new(NavKey.Track, "跑道", "奶蛙预览与轨迹", "◈"),
            new(NavKey.Splash, "开屏动画", "预览与重播", "▶"),
            new(NavKey.Settings, "设置", "工具路径与通道", "⚙"),
            new(NavKey.Logs, "日志", "运行记录", "≡"),
            new(NavKey.About, "关于", "开发者与声明", "ⓘ"),
        };

        SelectedPage = Pages[0];
        Snapshot = RunSnapshot.Idle();
        StatusText = "待机";
        DetectedLdConsole = _shell.ResolveLdConsole(null) ?? "";
        DetectedAdb = _shell.ResolveAdb(DetectedLdConsole) ?? "";

        // 仪表盘上的三个可调参数，初值取当前配置
        _dashTargetKm = Config.TargetKm;
        _dashPace = PaceFromSpeed(Config.SpeedMps);
        _dashCadence = Config.CadencePerMin;

        _ldConsolePathText = string.IsNullOrWhiteSpace(Config.LdConsolePath)
            ? DetectedLdConsole
            : Config.LdConsolePath;
        _adbPathText = string.IsNullOrWhiteSpace(Config.AdbPath) ? DetectedAdb : Config.AdbPath;
        _emulatorIndexText = Config.EmulatorIndex.ToString();
        _leadSecondsText = Config.LeadSeconds.ToString();
        _gravityLowText = Config.GravityLow.ToString("F1");
        _gravityHighText = Config.GravityHigh.ToString("F1");
        _adbSerialText = Config.AdbSerial;
        _packageNameText = Config.PackageName;
        _geoEnabled = Config.GeoEnabled;
        _stepEnabled = Config.StepEnabled;
        _autoStartApp = Config.AutoStartApp;
        _checkForeground = Config.CheckForeground;

        // 「当前时间是否在有效窗口内」用 DateTime.Now 现算，
        // 不刷新的话设置页会一直显示打开程序那一刻的时间。半分钟一次足够。
        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _clock.Tick += (_, _) =>
        {
            OnPropertyChanged(nameof(WindowNote));
            OnPropertyChanged(nameof(InRunWindow));
        };
        _clock.Start();

        // 自动探测到的路径一并存进配置：下次即使用户没手动填，启动就能直接用
        if (string.IsNullOrWhiteSpace(Config.LdConsolePath) && !string.IsNullOrWhiteSpace(DetectedLdConsole))
        {
            Config.LdConsolePath = DetectedLdConsole;
            SaveConfig();
        }
    }

    /// <summary>配置落盘。改一个值就存一次，文件很小，不值得做脏检查。</summary>
    private void SaveConfig() => ConfigStore.Save(Config);

    private readonly DispatcherTimer? _clock;

    public ObservableCollection<NavItem> Pages { get; }
    public ObservableCollection<string> LogLines { get; } = new();

    /// <summary>日志条数，日志页右上角徽标显示。</summary>
    public string LogCount => $"{LogLines.Count} 行";

    public RunConfig Config { get; }
    public TrackLoop Track { get; private set; }
    public RunSnapshot Snapshot { get; private set; }

    [ObservableProperty] private NavItem _selectedPage;
    [ObservableProperty] private string _statusText = "待机";
    [ObservableProperty] private string _detectedLdConsole = "";
    [ObservableProperty] private string _detectedAdb = "";
    [ObservableProperty] private string _trackPointsText = "";
    [ObservableProperty] private bool _busy;

    // ==== 仪表盘内联参数 ====
    // 三个值都由滑轮改，范围写死在这里 —— 用户没法敲出越界数字
    public const double DashTargetMin = RunConfig.MinTargetKm;
    public const double DashTargetMax = RunConfig.MaxTargetKm;
    public const double DashTargetStep = 0.1;

    public const double DashPaceMin = RunConfig.MinPaceMinPerKm;   // 2 分/公里
    public const double DashPaceMax = RunConfig.MaxPaceMinPerKm;   // 10 分/公里
    public const double DashPaceStep = 0.01;

    public const double DashCadenceMin = RunConfig.MinCadence;
    public const double DashCadenceMax = RunConfig.MaxCadence;
    public const double DashCadenceStep = 5;

    [ObservableProperty] private double _dashTargetKm = 2.0;
    [ObservableProperty] private double _dashCadence = 110;

    /// <summary>配速（分钟每公里）。界面只让用户改配速，速度由它换算。</summary>
    [ObservableProperty] private double _dashPace = 6.83;

    /// <summary>配速（分/公里）换算速度（m/s）。</summary>
    public static double SpeedFromPace(double paceMinPerKm) =>
        paceMinPerKm <= 0 ? RunConfig.MaxSpeedMps : 1000.0 / (paceMinPerKm * 60.0);

    /// <summary>速度（m/s）换算配速（分/公里）。</summary>
    public static double PaceFromSpeed(double speedMps) =>
        speedMps <= 0 ? RunConfig.MaxPaceMinPerKm : 1000.0 / speedMps / 60.0;

    /// <summary>当前配速换算出来的速度，引擎用这个。</summary>
    public double DashSpeed => SpeedFromPace(DashPace);

    /// <summary>把仪表盘上的目标距离与配速折算成预计用时。</summary>
    public string EtaText
    {
        get
        {
            double sec = DashTargetKm * 1000 / DashSpeed;
            int total = (int)Math.Round(sec);
            return $"{total / 60} 分 {total % 60:00} 秒";
        }
    }

    /// <summary>预计总步数，方便判断步频是否够用。</summary>
    public string EstStepsText
    {
        get
        {
            double sec = DashTargetKm * 1000 / DashSpeed;
            return ((int)Math.Round(sec * DashCadence / 60.0)).ToString();
        }
    }

    /// <summary>滑轮右侧的小字：把配速说成速度，用户更容易对照服务端提示。</summary>
    public string SpeedHintText => $"{DashSpeed:F2} m/s";

    /// <summary>展示用的版本号，关于页右上角。</summary>
    public string VersionText => "v" + (typeof(MainViewModel).Assembly
        .GetName().Version?.ToString(3) ?? "2.5.0");

    /// <summary>有效时间窗说明，仪表盘上常显。</summary>
    public string WindowNote => RunWindow.Describe(DateTime.Now);

    /// <summary>当前是否在有效时间段内。</summary>
    public bool InRunWindow => RunWindow.IsOpen(DateTime.Now);

    /// <summary>主按钮文字：跟着阶段走，用户一眼知道现在卡在哪一步。</summary>
    public string StartLabel => Snapshot.Phase switch
    {
        RunPhase.Armed => "倒计时中…",
        RunPhase.Running => "正在跑…",
        RunPhase.Done => "再跑一次",
        RunPhase.Stopped => "再跑一次",
        RunPhase.Error => "重试",
        _ => "开始跑步"
    };

    /// <summary>操作条左侧竖条的颜色 = 当前阶段色，比文字更快被眼睛抓到。</summary>
    public IBrush PhaseAccent => StatusBrush;

    /// <summary>操作条右侧「下一步」提示，随阶段变化。</summary>
    public string NextStepHint => Snapshot.Phase switch
    {
        RunPhase.Idle => "点开始跑步，倒计时内到运动世界按开始",
        RunPhase.Armed => Snapshot.ForegroundOk == false
            ? "先在模拟器里打开运动世界"
            : "倒计时快结束了，准备在 App 里按开始",
        RunPhase.Running => "跑完在 App 里提交成绩",
        RunPhase.Done => "成绩已提交，可以在 App 里核对",
        RunPhase.Stopped => "已停止，可在运动世界重新发起一次",
        RunPhase.Error => "看日志排查，或点环境自检",
        _ => ""
    };

    /// <summary>阶段徽标颜色：和顶栏状态灯同一套。</summary>
    public IBrush PhaseBrush => StatusBrush;

    /// <summary>倒计时期间的前台提示；没在检测时为空串。</summary>
    public string ForegroundHint => Snapshot.Phase == RunPhase.Armed && Snapshot.ForegroundOk is bool
        ? (Snapshot.ForegroundOk.Value
            ? "运动世界已在前台"
            : $"倒计时暂停：请打开运动世界{(Snapshot.ForegroundPackage.Length > 0 ? $"（当前 {Snapshot.ForegroundPackage}）" : "")}")
        : "";

    public bool ForegroundWarn => Snapshot.Phase == RunPhase.Armed && Snapshot.ForegroundOk == false;

    /// <summary>前台正常时在状态卡露一行轻提示（倒计时期间才显示）。</summary>
    public bool ForegroundOkVisible => Snapshot.Phase == RunPhase.Armed && Snapshot.ForegroundOk == true;

    /// <summary>跑完之后才发现「倒计时期间 App 一直没在前台」的补救提示。</summary>
    public bool ShowMissedWarning => Snapshot.ForegroundMissed
        && (Snapshot.Phase is RunPhase.Running or RunPhase.Done);

    public string MissedWarningText =>
        "这一趟倒计时期间运动世界始终没到前台，服务端很可能不计成绩。建议停掉重跑，"
        + "并确认「自动拉起并点击开始」或手动把 App 停在开始界面。";

    public IBrush ForegroundBrush => ForegroundWarn
        ? new SolidColorBrush(Color.Parse("#FBBF24"))
        : new SolidColorBrush(Color.Parse("#4ADE80"));

    // ===== 倒计时面板（仪表盘顶部那张） =====

    /// <summary>倒计时期间为 true，面板才显示。</summary>
    public bool IsArmed => Snapshot.Phase == RunPhase.Armed;

    /// <summary>倒计时大数字。被前台卡住时倒数不动，数字本身就是证据。</summary>
    public string CountdownText => Snapshot.Phase == RunPhase.Armed
        ? ((int)Math.Ceiling(Math.Max(0, Snapshot.CountdownRemaining))).ToString()
        : "--";

    public double CountdownProgress => Snapshot.CountdownProgress;

    /// <summary>倒计时面板的标题：卡住、就位、准备中各一句。</summary>
    public string CountdownHeadline
    {
        get
        {
            if (Snapshot.Phase != RunPhase.Armed) return "待机";
            if (Snapshot.CountdownHeld) return "倒计时已暂停 · 等你打开运动世界";
            if (Snapshot.ForegroundOk == true) return "运动世界已就位 · 等你按下开始";
            return "准备中 · 正在钉住起点";
        }
    }

    /// <summary>操作条右侧那句提示，跟着阶段变。</summary>
    public string ActionHint => NextStepHint;

    /// <summary>顶栏状态灯颜色：跑起来绿、达标蓝、出错红、其余灰。</summary>
    public IBrush StatusBrush => Snapshot.Phase switch
    {
        RunPhase.Running => new SolidColorBrush(Color.Parse("#4ADE80")),
        RunPhase.Armed => new SolidColorBrush(Color.Parse("#E0C060")),
        RunPhase.Done => new SolidColorBrush(Color.Parse("#38BDF8")),
        RunPhase.Error => new SolidColorBrush(Color.Parse("#F87171")),
        RunPhase.Stopped => new SolidColorBrush(Color.Parse("#8896A9")),
        _ => new SolidColorBrush(Color.Parse("#5A6879")),
    };

    public string DistanceText => Snapshot.DistanceKm.ToString("F2");
    /// <summary>待机时显示即将要跑的目标，跑起来后跟着引擎走。</summary>
    public string TargetText => Snapshot.TargetMeters > 0
        ? (Snapshot.TargetMeters / 1000.0).ToString("F2")
        : Math.Clamp(DashTargetKm, RunConfig.MinTargetKm, RunConfig.MaxTargetKm).ToString("F1");
    public string StepsText => Snapshot.Steps.ToString();
    public string TimeText => FormatTime(Snapshot.ElapsedSeconds);
    /// <summary>
    /// 仪表盘上的步频。跑动中优先显示**最近 6 秒的实测值**——全程平均会
    /// 把卡顿藏起来，用户看到 150 却实际只有个位数就是这么来的。
    /// </summary>
    public string CadenceText
    {
        get
        {
            // 跑动中优先显示运动世界自己认的步频（闭环实测），那才是用户
            // 在 App 里看到的数字；拿不到样本时退回我们发出的实际速率。
            if (Snapshot.Phase == RunPhase.Running)
            {
                if (Snapshot.DetectedCadence > 0) return Snapshot.DetectedCadence.ToString("F0");
                if (Snapshot.LiveCadence > 0) return Snapshot.LiveCadence.ToString("F0");
            }
            return Snapshot.Cadence <= 0 ? "--" : Snapshot.Cadence.ToString("F0");
        }
    }

    /// <summary>步频闭环/上限的说明，跑动中显示，平时为空。</summary>
    public string CadenceHintText
    {
        get
        {
            if (!string.IsNullOrEmpty(Snapshot.CadenceNote)
                && Snapshot.Phase is RunPhase.Running or RunPhase.Done)
                return Snapshot.CadenceNote;
            return Snapshot.CadenceLimited
                ? $"上限 {Snapshot.MaxCadence:F0} spm · 单次注入 {Snapshot.PumpLatencyMs:F0} ms"
                : "";
        }
    }

    /// <summary>LIMIT 角标只在真的顶到注入通道上限时挂。</summary>
    public bool ShowCadenceHint => Snapshot.CadenceLimited;

    /// <summary>闭环说明（目标 / App 实测 / 发送率倍数）在跑动中显示在步频下面。</summary>
    public bool ShowCadenceNote => !string.IsNullOrEmpty(Snapshot.CadenceNote)
        && Snapshot.Phase is RunPhase.Running or RunPhase.Done;

    public string CadenceNoteText => Snapshot.CadenceNote;
    public string PaceText => FormatPace(Snapshot.PaceSecondsPerKm);
    public string ProgressText => (Snapshot.Progress * 100).ToString("F0") + " %";

    /// <summary>奶蛙预览的右上角角标：待机时显示将要用的步频。</summary>
    public string CadencePreviewText => Snapshot.Cadence > 0
        ? $"{Snapshot.Cadence:F0} spm"
        : $"{DashCadence:F0} spm";

    /// <summary>剩余距离，跑完显示「已完成」。</summary>
    public string RemainingText
    {
        get
        {
            double left = Snapshot.TargetMeters - Snapshot.DistanceMeters;
            if (Snapshot.TargetMeters <= 0) return "--";
            return left <= 0 ? "0 m" : left.ToString("F0") + " m";
        }
    }
    public double ProgressRingOffset => 2 * Math.PI * 78 * (1 - Snapshot.Progress);

    private static string FormatTime(double sec)
    {
        if (sec < 0) sec = 0;
        int total = (int)Math.Round(sec);
        return $"{total / 60}:{total % 60:00}";
    }

    private static string FormatPace(double secPerKm)
    {
        if (secPerKm <= 0) return "--'--\"";
        int m = (int)(secPerKm / 60);
        int s = (int)Math.Round(secPerKm - m * 60);
        if (s == 60) { m++; s = 0; }
        return $"{m}'{s:00}\"";
    }

    partial void OnDashTargetKmChanged(double value)
    {
        Config.TargetKm = value;
        SaveConfig();
        OnPropertyChanged(nameof(EtaText));
        OnPropertyChanged(nameof(EstStepsText));
        if (!Busy) OnPropertyChanged(nameof(TargetText));
    }

    partial void OnDashPaceChanged(double value)
    {
        Config.SpeedMps = SpeedFromPace(value);
        SaveConfig();
        OnPropertyChanged(nameof(DashSpeed));
        OnPropertyChanged(nameof(EtaText));
        OnPropertyChanged(nameof(EstStepsText));
        OnPropertyChanged(nameof(SpeedHintText));
    }

    partial void OnDashCadenceChanged(double value)
    {
        Config.CadencePerMin = value;
        SaveConfig();
        OnPropertyChanged(nameof(EstStepsText));
        OnPropertyChanged(nameof(CadencePreviewText));
        OnPropertyChanged(nameof(CadenceHintText));
        OnPropertyChanged(nameof(ShowCadenceHint));
        OnPropertyChanged(nameof(ShowCadenceNote));
        OnPropertyChanged(nameof(CadenceNoteText));
    }

    partial void OnSelectedPageChanged(NavItem value)
    {
        OnPropertyChanged(nameof(IsDashboard));
        OnPropertyChanged(nameof(IsTrack));
        OnPropertyChanged(nameof(IsSplash));
        OnPropertyChanged(nameof(IsSettings));
        OnPropertyChanged(nameof(IsLogs));
        OnPropertyChanged(nameof(IsAbout));
    }

    /// <summary>注入通道说明，设置页显示。</summary>
    public string ChannelNote =>
        AppShell.Capabilities.GeoNote + "\n" + AppShell.Capabilities.StepNote;

    /// <summary>引擎是否正在跑，供 3D 场景决定是否播放动画。</summary>
    public bool IsRunning => Snapshot.Phase == RunPhase.Running;

    /// <summary>已跑圈数（含小数），跑道页右上角显示。</summary>
    public string LapText => Track.Perimeter <= 0
        ? "0.00 圈"
        : $"{(Snapshot.DistanceMeters / Track.Perimeter):F2} 圈";

    /// <summary>当前保留的点位数量，点位编辑页徽标显示。</summary>
    public string TrackPointCount => $"{Track.Points.Count} 点";

    public bool IsDashboard => SelectedPage.Key == NavKey.Dashboard;
    public bool IsTrack => SelectedPage.Key == NavKey.Track;
    public bool IsSplash => SelectedPage.Key == NavKey.Splash;
    public bool IsSettings => SelectedPage.Key == NavKey.Settings;
    public bool IsLogs => SelectedPage.Key == NavKey.Logs;
    public bool IsAbout => SelectedPage.Key == NavKey.About;

    [RelayCommand]
    private void Navigate(NavItem? item)
    {
        if (item is not null) SelectedPage = item;
    }

    /// <summary>仪表盘上的三个预设，一键把三个参数同时改掉。</summary>
    [RelayCommand]
    private void PresetSlow() => ApplyPreset("slow");

    [RelayCommand]
    private void PresetStandard() => ApplyPreset("standard");

    [RelayCommand]
    private void PresetFast() => ApplyPreset("fast");

    [RelayCommand]
    private void ApplyPreset(string name)
    {
        var p = RunConfig.Preset(name);
        DashTargetKm = p.TargetKm;
        // 配速吸附到滑轮刻度上，避免点完预设指针停在两个刻度之间
        double pace = PaceFromSpeed(p.SpeedMps);
        DashPace = Math.Round(pace / DashPaceStep, MidpointRounding.AwayFromZero) * DashPaceStep;
        DashCadence = p.CadencePerMin;
        AppendLog($"预设已切换：{p.TargetKm:F1} km / {p.SpeedMps:F2} m/s（{RunConfig.PaceLabel(p.SpeedMps)}/km）/ {p.CadencePerMin:F0} 步每分");
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        if (Busy) return;

        // 以用户填的路径为准：原来只认自动探测到的 DetectedLdConsole，
        // 于是把路径改到另一个雷电安装目录后，实际跑的仍是旧那份。
        var toolPath = string.IsNullOrWhiteSpace(Config.LdConsolePath)
            ? DetectedLdConsole
            : Config.LdConsolePath.Trim().Trim('"');

        if (string.IsNullOrWhiteSpace(toolPath) || !File.Exists(toolPath))
        {
            AppendLog($"ldconsole.exe 路径无效：{(string.IsNullOrWhiteSpace(toolPath) ? "（未填写）" : toolPath)}");
            AppendLog("请到「设置」里填写 ldconsole.exe 的完整路径后重试");
            SelectedPage = Pages.First(p => p.Key == NavKey.Settings);
            StatusText = "路径无效";
            OnPropertyChanged(nameof(StatusBrush));
            OnPropertyChanged(nameof(PhaseBrush));
            return;
        }
        DetectedLdConsole = toolPath;

        // 校园跑只在 06:00–22:00 记成绩，窗口外一律不开跑，避免白跑一趟
        var now = DateTime.Now;
        if (!RunWindow.IsOpen(now))
        {
            var wait = RunWindow.UntilOpen(now);
            AppendLog($"当前 {now:HH:mm} 不在有效时间 {RunWindow.Label} 内，服务端不会记成绩");
            AppendLog($"距离开窗还有 {(int)wait.TotalHours} 小时 {wait.Minutes} 分");
            StatusText = "不在有效时间";
            OnPropertyChanged(nameof(StatusBrush));
            OnPropertyChanged(nameof(PhaseBrush));
            return;
        }

        // 开跑前把当前配置（含三个勾选项）落盘：勾选是直接改 Config 的，
        // 没有 partial 回调，只能在这里补存一次。
        SaveConfig();

        var cfg = Config.Sanitized();
        var controller = _shell.CreateController(cfg, toolPath);
        var adbExe = string.IsNullOrWhiteSpace(Config.AdbPath) ? DetectedAdb : Config.AdbPath.Trim().Trim('"');
        var adb = _shell.CreateAdbClient(
            string.IsNullOrWhiteSpace(adbExe) || !File.Exists(adbExe) ? null : adbExe, cfg.AdbSerial);

        Busy = true;
        StatusText = "启动中";
        OnPropertyChanged(nameof(StatusBrush));
        OnPropertyChanged(nameof(PhaseBrush));
        OnPropertyChanged(nameof(PhaseAccent));
        OnPropertyChanged(nameof(StartLabel));

        // 上一次的引擎不再需要了，先把事件摘掉再换新的，
        // 不然连跑两次之后旧引擎（可能还在收尾）也会往界面推状态。
        if (_engine is not null)
        {
            _engine.Snapshot -= OnEngineSnapshot;
            _engine.Log -= OnEngineLog;
        }

        _engine = new RunEngine(controller, adb);
        _engine.Snapshot += OnEngineSnapshot;
        _engine.Log += OnEngineLog;

        if (cfg.AutoStartApp && adb is null)
            AppendLog("自动拉起已关闭：没找到 adb.exe，跑步开始后请在模拟器里手动点开始");
        else if (cfg.AutoStartApp && adb is not null && !adb.IsConfigured)
            AppendLog("自动拉起已关闭：adb 路径无效，请在「设置」里改");

        AppendLog($"后端：{controller.DisplayName}（实例 {cfg.EmulatorIndex}）");
        AppendLog($"开始：{cfg.TargetKm:F2} km / 配速 {RunConfig.PaceLabel(cfg.SpeedMps)}/km（{cfg.SpeedMps:F2} m/s）/ {cfg.CadencePerMin:F0} 步每分");

        try
        {
            await _engine.StartAsync(cfg, Track);
        }
        catch (Exception ex)
        {
            // 原来这里只把 Busy 放掉，顶栏状态还停在「启动中」，
            // 状态灯也是橙的，看着像还在启动。
            AppendLog("启动失败：" + ex.Message);
            Busy = false;
            StatusText = "启动失败";
            OnPropertyChanged(nameof(StatusBrush));
        OnPropertyChanged(nameof(PhaseBrush));
        OnPropertyChanged(nameof(PhaseAccent));
        OnPropertyChanged(nameof(StartLabel));
        }
    }

    private void OnEngineSnapshot(object? sender, RunSnapshot s) => OnSnapshot(s);

    private void OnEngineLog(object? sender, string message) => AppendLog(message);

    private void OnSnapshot(RunSnapshot s)
    {
        // RunEngine 的循环跑在 Task.Run 线程上，事件直接回来时改的是
        // ObservableCollection 与绑定属性——那会在 UI 线程之外动集合，
        // 轻则界面不刷新，重则 collection modified 崩掉。统一回到 UI 线程。
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnSnapshot(s));
            return;
        }

        Snapshot = s;

        StatusText = s.Phase switch
        {
            RunPhase.Armed => s.Message,
            RunPhase.Running => "跑步中",
            RunPhase.Done => "已达标",
            RunPhase.Stopped => "已停止",
            RunPhase.Error => "错误：" + s.Message,
            _ => "待机"
        };

        if (s.Phase is RunPhase.Done or RunPhase.Stopped or RunPhase.Error)
        {
            Busy = false;
        }

        OnPropertyChanged(nameof(Snapshot));
        OnPropertyChanged(nameof(DistanceText));
        OnPropertyChanged(nameof(TargetText));
        OnPropertyChanged(nameof(StepsText));
        OnPropertyChanged(nameof(TimeText));
        OnPropertyChanged(nameof(CadenceText));
        OnPropertyChanged(nameof(CadenceHintText));
        OnPropertyChanged(nameof(ShowCadenceHint));
        OnPropertyChanged(nameof(ShowCadenceNote));
        OnPropertyChanged(nameof(CadenceNoteText));
        OnPropertyChanged(nameof(PaceText));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(CadencePreviewText));
        OnPropertyChanged(nameof(CadenceHintText));
        OnPropertyChanged(nameof(ShowCadenceHint));
        OnPropertyChanged(nameof(ShowCadenceNote));
        OnPropertyChanged(nameof(CadenceNoteText));
        OnPropertyChanged(nameof(RemainingText));
        OnPropertyChanged(nameof(ProgressRingOffset));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(LapText));
        OnPropertyChanged(nameof(StatusBrush));
        OnPropertyChanged(nameof(PhaseBrush));
        OnPropertyChanged(nameof(PhaseAccent));
        OnPropertyChanged(nameof(StartLabel));
        OnPropertyChanged(nameof(ForegroundHint));
        OnPropertyChanged(nameof(ForegroundWarn));
        OnPropertyChanged(nameof(ForegroundOkVisible));
        OnPropertyChanged(nameof(NextStepHint));
        OnPropertyChanged(nameof(StartLabel));
        OnPropertyChanged(nameof(PhaseAccent));
        OnPropertyChanged(nameof(ShowMissedWarning));
        OnPropertyChanged(nameof(ForegroundBrush));
        OnPropertyChanged(nameof(IsArmed));
        OnPropertyChanged(nameof(CountdownText));
        OnPropertyChanged(nameof(CountdownProgress));
        OnPropertyChanged(nameof(CountdownHeadline));
    }

    /// <summary>按页面键跳转，给外部（截图工具、测试）用，也给自己用。</summary>
    public void NavigateTo(NavKey key)
    {
        var page = Pages.FirstOrDefault(p => p.Key == key);
        if (page is not null) SelectedPage = page;
    }

    /// <summary>截图 / 测试用：直接把一份快照塞进来渲染。</summary>
    public void ShowPreview(RunSnapshot snapshot, string status)
    {
        Snapshot = snapshot;
        StatusText = status;
        OnPropertyChanged(nameof(Snapshot));
        OnPropertyChanged(nameof(DistanceText));
        OnPropertyChanged(nameof(TargetText));
        OnPropertyChanged(nameof(StepsText));
        OnPropertyChanged(nameof(TimeText));
        OnPropertyChanged(nameof(CadenceText));
        OnPropertyChanged(nameof(CadenceHintText));
        OnPropertyChanged(nameof(ShowCadenceHint));
        OnPropertyChanged(nameof(ShowCadenceNote));
        OnPropertyChanged(nameof(CadenceNoteText));
        OnPropertyChanged(nameof(PaceText));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(RemainingText));
        OnPropertyChanged(nameof(ProgressRingOffset));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(LapText));
        OnPropertyChanged(nameof(StatusBrush));
        OnPropertyChanged(nameof(PhaseBrush));
        OnPropertyChanged(nameof(PhaseAccent));
        OnPropertyChanged(nameof(StartLabel));
        OnPropertyChanged(nameof(IsArmed));
        OnPropertyChanged(nameof(CountdownText));
        OnPropertyChanged(nameof(CountdownProgress));
        OnPropertyChanged(nameof(CountdownHeadline));
        OnPropertyChanged(nameof(ForegroundHint));
        OnPropertyChanged(nameof(ForegroundWarn));
        OnPropertyChanged(nameof(ForegroundBrush));
        OnPropertyChanged(nameof(NextStepHint));
    }

    // ==== 设置页的数字输入 ====
    // TextBox 直接绑 int/double 时，用户把内容删空那一瞬间会触发转换异常（界面报红、
    // 值不生效）。这里用字符串代理，解析成功才写回配置，写不进去就保持原值。

    // ldconsole.exe 路径：设置页里手填的必须真的生效。
    // 原来框绑 Config.LdConsolePath、启动却只用自动探测值，手填等于白填。
    [ObservableProperty] private string _ldConsolePathText = "";

    // adb.exe 同理：设置页里能改，就必须记住改了以后的值，
    // 原来直接绑 DetectedAdb（纯运行时状态），重启就丢回自动探测结果。
    [ObservableProperty] private string _adbPathText = "";

    partial void OnAdbPathTextChanged(string value)
    {
        var path = (value ?? "").Trim().Trim('"');
        Config.AdbPath = path;
        SaveConfig();
        if (path.Length > 0 && File.Exists(path)) DetectedAdb = path;
    }

    partial void OnLdConsolePathTextChanged(string value)
    {
        var path = (value ?? "").Trim().Trim('"');
        Config.LdConsolePath = path;
        SaveConfig();

        if (path.Length == 0) return;
        if (File.Exists(path))
        {
            DetectedLdConsole = path;
            DetectedAdb = _shell.ResolveAdb(path) ?? DetectedAdb;
        }
    }

    [ObservableProperty] private string _emulatorIndexText = "0";
    [ObservableProperty] private string _leadSecondsText = "15";
    [ObservableProperty] private string _gravityLowText = "5.5";
    [ObservableProperty] private string _gravityHighText = "14.0";

    partial void OnEmulatorIndexTextChanged(string value)
    {
        if (int.TryParse(value, out var v) && v >= 0) { Config.EmulatorIndex = v; SaveConfig(); }
    }

    partial void OnLeadSecondsTextChanged(string value)
    {
        if (int.TryParse(value, out var v) && v is >= 0 and <= 120) { Config.LeadSeconds = v; SaveConfig(); }
    }

    partial void OnGravityLowTextChanged(string value)
    {
        if (double.TryParse(value, out var v) && v is >= 0 and <= 8.5) { Config.GravityLow = v; SaveConfig(); }
    }

    partial void OnGravityHighTextChanged(string value)
    {
        if (double.TryParse(value, out var v) && v is >= 11.5 and <= 30) { Config.GravityHigh = v; SaveConfig(); }
    }

    // 序列号 / 包名 / 三个开关原来直接绑 Config.Xxx（RunConfig 没实现
    // INotifyPropertyChanged），改完既不落盘也不刷新，重启就回到旧值。
    // 统一走字符串/布尔代理，改一次存一次。
    [ObservableProperty] private string _adbSerialText = "127.0.0.1:5555";
    [ObservableProperty] private string _packageNameText = "";
    [ObservableProperty] private bool _geoEnabled = true;
    [ObservableProperty] private bool _stepEnabled = true;
    [ObservableProperty] private bool _autoStartApp = true;

    partial void OnAdbSerialTextChanged(string value)
    {
        var serial = (value ?? "").Trim();
        // 删空时不要写空串：空序列号会让 adb -s " " 整条命令失效
        Config.AdbSerial = serial.Length == 0 ? "127.0.0.1:5555" : serial;
        SaveConfig();
    }

    partial void OnPackageNameTextChanged(string value)
    {
        var pkg = (value ?? "").Trim();
        Config.PackageName = pkg.Length == 0 ? "com.zjwh.android_wh_physicalfitness" : pkg;
        SaveConfig();
    }

    partial void OnGeoEnabledChanged(bool value) { Config.GeoEnabled = value; SaveConfig(); }
    partial void OnStepEnabledChanged(bool value) { Config.StepEnabled = value; SaveConfig(); }
    partial void OnAutoStartAppChanged(bool value) { Config.AutoStartApp = value; SaveConfig(); }

    [ObservableProperty] private bool _checkForeground = true;
    partial void OnCheckForegroundChanged(bool value) { Config.CheckForeground = value; SaveConfig(); }

    /// <summary>关窗口时调用：停掉跑步循环、停掉时钟计时器。</summary>
    public async Task ShutdownAsync()
    {
        _clock?.Stop();
        try { if (_engine is not null) await _engine.StopAsync(); } catch { }
    }

    /// <summary>打开程序目录（侧栏与设置页都用）。</summary>
    [RelayCommand]
    private void OpenFolder() => _shell.OpenFolder(_shell.BaseDirectory);

    /// <summary>关于页里的「看开屏动画」按钮。</summary>
    [RelayCommand]
    private void GoSplash() => NavigateTo(NavKey.Splash);

    /// <summary>
    /// 倒计时面板上的「打开运动世界」：直接用 adb 把 App 拉到前台。
    /// 用户嫌自己找图标麻烦时点这个，点完前台检测会立刻认出来、倒计时继续。
    /// </summary>
    [RelayCommand]
    private async Task LaunchAppAsync()
    {
        var cfg = Config.Sanitized();
        var exe = string.IsNullOrWhiteSpace(Config.AdbPath) ? DetectedAdb : Config.AdbPath.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
        {
            AppendLog("打开运动世界失败：没找到 adb.exe，请到「设置」里填路径");
            return;
        }

        var adb = _shell.CreateAdbClient(exe, cfg.AdbSerial);
        if (adb is null || !adb.IsConfigured)
        {
            AppendLog("打开运动世界失败：adb 未配置");
            return;
        }

        AppendLog($"正在拉起 {cfg.PackageName}");
        try
        {
            await adb.ConnectAsync(CancellationToken.None);
            var r = await adb.LaunchAsync(cfg.PackageName, CancellationToken.None);
            AppendLog(r.Ok ? "已发送启动命令，等它到前台" : "启动命令没成功，手动点一下图标");
        }
        catch (Exception ex)
        {
            AppendLog("打开运动世界异常：" + ex.Message);
        }
    }

    /// <summary>重新探测 ldconsole / adb，并列出实例。</summary>
    [RelayCommand]
    private async Task RedetectAsync()
    {
        var hint = !string.IsNullOrWhiteSpace(Config.LdConsolePath)
            ? Config.LdConsolePath
            : DetectedLdConsole;

        var found = _shell.ResolveLdConsole(string.IsNullOrWhiteSpace(hint) ? null : hint);
        if (found is not null)
        {
            DetectedLdConsole = found;
            Config.LdConsolePath = found;
            LdConsolePathText = found;
            SaveConfig();
        }
        DetectedAdb = _shell.ResolveAdb(
            string.IsNullOrWhiteSpace(Config.AdbPath) ? DetectedLdConsole : Config.AdbPath) ?? "";
        if (string.IsNullOrWhiteSpace(Config.AdbPath)) AdbPathText = DetectedAdb;

        AppendLog($"ldconsole：{(string.IsNullOrEmpty(DetectedLdConsole) ? "未找到" : DetectedLdConsole)}");
        AppendLog($"adb：{(string.IsNullOrEmpty(DetectedAdb) ? "未找到" : DetectedAdb)}");

        if (string.IsNullOrEmpty(DetectedLdConsole)) return;

        try
        {
            var ctrl = _shell.CreateController(Config.Sanitized(), DetectedLdConsole);
            var info = await ctrl.DescribeAsync(CancellationToken.None);
            if (string.IsNullOrWhiteSpace(info))
                AppendLog("实例列表为空，模拟器可能没启动");
            else
                foreach (var line in info.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    AppendLog("实例 " + line.Trim());
        }
        catch (Exception ex)
        {
            AppendLog("列出实例失败：" + ex.Message);
        }
    }

    /// <summary>停止当前跑步。</summary>
    [RelayCommand]
    private async Task StopAsync()
    {
        if (_engine is null || !Busy) return;
        await _engine.StopAsync();
        StatusText = "已停止";
        Busy = false;
        OnPropertyChanged(nameof(StatusBrush));
        OnPropertyChanged(nameof(PhaseBrush));
        OnPropertyChanged(nameof(PhaseAccent));
        OnPropertyChanged(nameof(StartLabel));
    }

    /// <summary>把跑道页里编辑的点位应用上去。</summary>
    [RelayCommand]
    private void ApplyTrack()
    {
        var loop = TrackLoopFactory.Parse(TrackPointsText);
        if (loop is null)
        {
            AppendLog("点位格式不对：至少要 3 行，每行「纬度,经度」");
            return;
        }
        Track = loop;
        TrackPointsText = TrackLoopFactory.ToText(loop);
        ConfigStore.SaveTrackText(TrackLoopFactory.ToText(loop));
        OnPropertyChanged(nameof(TrackPointCount));
        OnPropertyChanged(nameof(Track));
        OnPropertyChanged(nameof(LapText));
        AppendLog($"跑道已更新：{loop.Count} 个点，周长 {loop.Perimeter:F1} 米");
    }

    /// <summary>恢复内置的操场点位。</summary>
    [RelayCommand]
    private void ResetTrack()
    {
        TrackPointsText = TrackLoopFactory.DefaultText();
        ApplyTrack();
    }

    /// <summary>清空日志（日志页右上角按钮）。</summary>
    [RelayCommand]
    private void ClearLog()
    {
        LogLines.Clear();
        OnPropertyChanged(nameof(LogCount));
    }

    /// <summary>把整段日志复制到剪贴板，方便发 issue。</summary>
    [RelayCommand]
    private async Task CopyLogAsync()
    {
        var text = string.Join(Environment.NewLine, LogLines);
        if (text.Length == 0) return;

        var clipboard = (Avalonia.Application.Current?.ApplicationLifetime
            as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow?.Clipboard;
        if (clipboard is null) return;

        try
        {
            // Avalonia 12 去掉了 IClipboard.SetTextAsync，改成塞一个 DataTransfer
            var data = new Avalonia.Input.DataTransfer();
            data.Add(Avalonia.Input.DataTransferItem.CreateText(text));
            await clipboard.SetDataAsync(data);
            AppendLog($"日志已复制（{LogLines.Count} 行）");
        }
        catch
        {
            AppendLog("复制失败，剪贴板被别的程序占用");
        }
    }

    private void AppendLog(string line)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => AppendLog(line));
            return;
        }

        LogLines.Add($"{DateTime.Now:HH:mm:ss}  {line}");
        while (LogLines.Count > 500) LogLines.RemoveAt(0);
        OnPropertyChanged(nameof(LogLines));
        OnPropertyChanged(nameof(LogCount));
    }
}

public sealed record NavItem(NavKey Key, string Title, string Subtitle, string Glyph = "·");
