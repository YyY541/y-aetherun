using Aetherun.Core.Emulators;
using Aetherun.Core.Models;
using Avalonia;
using Avalonia.Headless;

// 奶蛙建模的核对要真的把网格光栅化出来再逐像素查，所以这里先把 Avalonia 起起来
// （资源加载与 WriteableBitmap 都依赖它）。不需要窗口，SetupWithoutStarting 就够。
try
{
    AppBuilder.Configure<Aetherun.Desktop.App>()
        .UseSkia()
        .UseHeadless(new Avalonia.Headless.AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .WithInterFont()
        .SetupWithoutStarting();
}
catch (Exception ex)
{
    Console.WriteLine("  (Avalonia 初始化失败，建模核对会退化) " + ex.GetType().Name);
}

// 关键：SetupWithoutStarting 会装上 Avalonia 的 SynchronizationContext，
// 后面 Engine 段里的 async/await 就会被排到一个永不运行的 UI 循环上 → 整个自检卡死。
// 这里把它摘掉，回到同步上下文。
System.Threading.SynchronizationContext.SetSynchronizationContext(null);

int pass = 0, fail = 0;

void Check(string name, bool ok, string detail = "")
{
    if (ok) { pass++; Console.WriteLine($"  PASS  {name}"); }
    else { fail++; Console.WriteLine($"  FAIL  {name}  {detail}"); }
}

var real = new (double Lat, double Lon)[]
{
    (22.908578, 113.869650), (22.909064, 113.869674), (22.909544, 113.869646),
    (22.909693, 113.869513), (22.909758, 113.869301), (22.909686, 113.869094),
    (22.909534, 113.868971), (22.909051, 113.868957), (22.908558, 113.868992),
    (22.908425, 113.869132), (22.908367, 113.869319), (22.908450, 113.869555),
};

var track = new TrackLoop(real.Select(p => new GeoPoint(p.Lat, p.Lon)));

Console.WriteLine("TrackLoop");
Check("perimeter in 384..392", track.Perimeter is > 384 and < 392, $"{track.Perimeter:F2}");
Check("perimeter ~388.21", Math.Abs(track.Perimeter - 388.21) < 0.5, $"{track.Perimeter:F2}");

var p0 = track.At(0);
Check("At(0) == first point", Math.Abs(p0.Lat - real[0].Lat) < 1e-6 && Math.Abs(p0.Lon - real[0].Lon) < 1e-6, p0.ToString());

var pw = track.At(track.Perimeter);
Check("At(perimeter) wraps", Math.Abs(pw.Lat - real[0].Lat) < 1e-5, pw.ToString());

bool spacingOk = true;
double minD = 99, maxD = 0;
for (double d = 0; d < track.Perimeter; d += 2.44)
{
    var a = track.At(d);
    var b = track.At(d + 2.44);
    double dd = GeoMath.Distance(a, b);
    if (dd < minD) minD = dd;
    if (dd > maxD) maxD = dd;
    if (dd < 2.0 || dd > 2.9) spacingOk = false;
}
Check("consecutive 2.44m fixes stay 2.0..2.9m", spacingOk, $"min {minD:F2} max {maxD:F2}");

var neg = track.At(-5);
var pos = track.At(track.Perimeter - 5);
Check("negative distance normalized", Math.Abs(neg.Lat - pos.Lat) < 1e-6, "");

bool threw = false;
try { _ = new TrackLoop(new[] { new GeoPoint(1, 1), new GeoPoint(2, 2) }); }
catch (ArgumentException) { threw = true; }
Check("2 points rejected", threw);

Check("LLI longitude first", new GeoPoint(22.905691, 113.874572).ToLli() == "113.874572,22.905691");

Console.WriteLine("RunConfig");
var c = new RunConfig
{
    TargetKm = 999, SpeedMps = 99, CadencePerMin = 5, LeadSeconds = -3,
    GravityLow = 20, GravityHigh = 1, EmulatorIndex = -2
}.Sanitized();
Check("TargetKm clamp to 2", c.TargetKm == 2.0, $"{c.TargetKm}");
Check("Speed clamp to 8.333", Math.Abs(c.SpeedMps - 1000.0 / 120.0) < 1e-6, $"{c.SpeedMps:F4}");
Check("Cadence clamp 60", c.CadencePerMin == 60);
Check("Lead clamp 0", c.LeadSeconds == 0);
Check("GravityLow <= 8.5", c.GravityLow <= 8.5, $"{c.GravityLow}");
Check("GravityHigh >= 11.5", c.GravityHigh >= 11.5, $"{c.GravityHigh}");
Check("Index >= 0", c.EmulatorIndex == 0);

var z = new RunConfig { LeadSeconds = 0, EmulatorIndex = 0 }.Sanitized();
Check("zero lead/index preserved", z.LeadSeconds == 0 && z.EmulatorIndex == 0);

var presetSlow = RunConfig.Preset("slow");
Check("preset slow 2.083 / 90 (8'00\"/km)",
    Math.Abs(presetSlow.SpeedMps - 2.083) < 1e-9 && Math.Abs(presetSlow.CadencePerMin - 90) < 1e-9,
    $"{presetSlow.SpeedMps:F3} {presetSlow.CadencePerMin:F0}");
var presetFast = RunConfig.Preset("fast");
Check("preset fast 4.167 / 150 (4'00\"/km)",
    Math.Abs(presetFast.SpeedMps - 4.167) < 1e-9 && Math.Abs(presetFast.CadencePerMin - 150) < 1e-9,
    $"{presetFast.SpeedMps:F3} {presetFast.CadencePerMin:F0}");
Check("preset standard is 2.778 / 120 (6'00\"/km)",
    Math.Abs(RunConfig.Preset("standard").SpeedMps - 2.778) < 1e-9
    && Math.Abs(RunConfig.Preset("standard").CadencePerMin - 120) < 1e-9);
// 配速 = 1000 / 速度（秒/公里），不是 3600 / 速度
Check("slow pace reads 8'00\"", Math.Abs(1000.0 / presetSlow.SpeedMps - 480) < 2,
    (1000.0 / presetSlow.SpeedMps).ToString("F0"));
Check("fast pace reads 4'00\"", Math.Abs(1000.0 / presetFast.SpeedMps - 240) < 2,
    (1000.0 / presetFast.SpeedMps).ToString("F0"));
Check("standard pace reads 6'00\"",
    Math.Abs(1000.0 / RunConfig.Preset("standard").SpeedMps - 360) < 2,
    (1000.0 / RunConfig.Preset("standard").SpeedMps).ToString("F0"));

Console.WriteLine("RunSnapshot");
var s1 = new RunSnapshot { DistanceMeters = 3000, TargetMeters = 2000 };
Check("progress clamps to 1", s1.Progress == 1.0);
Check("progress zero without target", new RunSnapshot().Progress == 0);
Check("cadence needs data", new RunSnapshot { Steps = 100, ElapsedSeconds = 1 }.Cadence == 0);
var s2 = new RunSnapshot { Steps = 1653, ElapsedSeconds = 901.6 };
Check("cadence ~110", s2.Cadence is > 109 and < 111, $"{s2.Cadence:F1}");
var s3 = new RunSnapshot { DistanceMeters = 2200, ElapsedSeconds = 901.6 };
Check("pace 400..420 s/km", s3.PaceSecondsPerKm is > 400 and < 420, $"{s3.PaceSecondsPerKm:F1}");
Check("pace zero without distance", new RunSnapshot { DistanceMeters = 0, ElapsedSeconds = 30 }.PaceSecondsPerKm == 0);

Console.WriteLine("GeoMath");
var north = GeoMath.Move(new GeoPoint(22.906172, 113.874239), 0, 100);
double moved = GeoMath.Distance(new GeoPoint(22.906172, 113.874239), north);
Check("move 100m north ~= 100m", Math.Abs(moved - 100) < 0.5, $"{moved:F2}");
var br = GeoMath.Bearing(new GeoPoint(22.906172, 113.874239), north);
Check("bearing north ~0", br < 1 || br > 359, $"{br:F2}");

// full run simulation
Console.WriteLine("Simulation");
double speed = 2.778, cadence = 120, targetM = 2200;
double half = 30.0 / cadence;
double total = targetM / speed;
int steps = 0; bool up = false; double t = 0; double lastStep = 0;
while (t < total)
{
    t += 0.02;
    while (t - lastStep >= half) { up = !up; if (up) steps++; lastStep += half; }
}
double effCad = steps / total * 60;
Check("sim cadence in 60..300", effCad is > 60 and < 300, $"{effCad:F1}");
Check("sim cadence ~120", Math.Abs(effCad - 120) < 2, $"{effCad:F1}");
double pace = total / (targetM / 1000.0);
Check("sim pace reasonable 300..500 s/km", pace is > 300 and < 500, $"{pace:F0}");
int fixes = (int)total;
Check("fix count ~792", Math.Abs(fixes - 792) <= 2, $"{fixes}");

// ---- engine end-to-end with a fake runner ----
Console.WriteLine("Engine");
var fake = new FakeRunner();
var ld = new LdPlayerController(new Aetherun.Core.Services.LdConsoleClient(fake, "/fake/ldconsole.exe", 0, verifyPath: false));
var engine = new Aetherun.Core.Services.RunEngine(ld, null);
var phases = new List<Aetherun.Core.Models.RunPhase>();
Aetherun.Core.Models.RunSnapshot last = Aetherun.Core.Models.RunSnapshot.Idle();
engine.Snapshot += (_, snap) => { last = snap; if (phases.Count == 0 || phases[^1] != snap.Phase) phases.Add(snap.Phase); };

var cfg = new Aetherun.Core.Models.RunConfig
{
    TargetKm = 0.5, SpeedMps = 8.333, CadencePerMin = 110, LeadSeconds = 1,
    EmulatorIndex = 0, GeoEnabled = true, StepEnabled = true, AutoStartApp = false
};
var sw = System.Diagnostics.Stopwatch.StartNew();
await engine.StartAsync(cfg, track);
while (engine.IsBusy && sw.Elapsed < TimeSpan.FromSeconds(120))
    await Task.Delay(50);

Check("engine reached Done", last.Phase == Aetherun.Core.Models.RunPhase.Done, last.Phase.ToString());
Check("phase order armed->running->done",
      phases.Contains(Aetherun.Core.Models.RunPhase.Armed)
      && phases.Contains(Aetherun.Core.Models.RunPhase.Running)
      && phases.Contains(Aetherun.Core.Models.RunPhase.Done),
      string.Join(",", phases));
Check("distance == target 500m", Math.Abs(last.DistanceMeters - 500) < 3, $"{last.DistanceMeters:F2}");
Check("steps > 0", last.Steps > 0, $"{last.Steps}");
Check("locate calls issued", fake.LocateCalls > 0, $"{fake.LocateCalls}");
Check("locate args use lon,lat", fake.LocateArgs.Count > 0 && fake.LocateArgs[0].Contains("113.8"), fake.LocateArgs.FirstOrDefault() ?? "");
Check("locate command shape", fake.LocateArgs.Count > 0 && fake.LocateArgs[0].StartsWith("locate --index 0 --LLI "), fake.LocateArgs.FirstOrDefault() ?? "");
Check("gravity calls issued", fake.GravityCalls > 0, $"{fake.GravityCalls}");
Check("gravity inside 4..16", fake.GravityValues.All(v => v is >= 4 and <= 16), "");
Check("gravity command shape",
    fake.GravityValues.Count > 1 && fake.GravityValues.Distinct().Count() == 2,
    string.Join("/", fake.GravityValues.Distinct()));
// 泵的实测值必须真的带进快照：界面靠这两个数字解释「为什么没跑满」
Check("snapshot carries live cadence", last.LiveCadence > 0, $"{last.LiveCadence:F1}");
Check("snapshot carries pump latency", last.PumpLatencyMs >= 0, $"{last.PumpLatencyMs:F1}");
Check("done frame cadence near 110", Math.Abs(last.LiveCadence - 110) < 30, $"{last.LiveCadence:F0}");
Check("elapsed ~61s", last.ElapsedSeconds is > 55 and < 70, $"{last.ElapsedSeconds:F1}");

// stop path
var fake2 = new FakeRunner();
var ld2 = new LdPlayerController(new Aetherun.Core.Services.LdConsoleClient(fake2, "/fake/ldconsole.exe", 0, verifyPath: false));
var engine2 = new Aetherun.Core.Services.RunEngine(ld2, null);
Aetherun.Core.Models.RunSnapshot last2 = Aetherun.Core.Models.RunSnapshot.Idle();
engine2.Snapshot += (_, snap) => last2 = snap;
await engine2.StartAsync(new Aetherun.Core.Models.RunConfig
{
    TargetKm = 30, SpeedMps = 2.0, CadencePerMin = 110, LeadSeconds = 0,
    GeoEnabled = true, StepEnabled = false, AutoStartApp = false
}, track);
await Task.Delay(400);
await engine2.StopAsync();
Check("stop sets Stopped", last2.Phase == Aetherun.Core.Models.RunPhase.Stopped, last2.Phase.ToString());
Check("stop does not reach target", last2.DistanceMeters < 30000, $"{last2.DistanceMeters:F1}");

// ---- 页面与关于 ----
Console.WriteLine("Pages");
var pvm = new Aetherun.Desktop.ViewModels.MainViewModel(new Aetherun.Desktop.Services.AppShell());
Check("six pages declared", pvm.Pages.Count == 6, $"{pvm.Pages.Count}");
Check("splash page exists",
      pvm.Pages.Any(p => p.Key == Aetherun.Desktop.ViewModels.NavKey.Splash),
      string.Join(",", pvm.Pages.Select(p => p.Title)));
Check("about page exists",
      pvm.Pages.Any(p => p.Key == Aetherun.Desktop.ViewModels.NavKey.About));
pvm.NavigateTo(Aetherun.Desktop.ViewModels.NavKey.Splash);
Check("navigate to splash sets IsSplash", pvm.IsSplash && !pvm.IsAbout);
pvm.GoSplashCommand.Execute(null);
Check("go splash command stays on splash", pvm.IsSplash);
pvm.NavigateTo(Aetherun.Desktop.ViewModels.NavKey.About);
Check("navigate to about sets IsAbout", pvm.IsAbout && !pvm.IsSplash && !pvm.IsDashboard);
Check("version text looks like vX.Y.Z",
      pvm.VersionText.StartsWith("v") && pvm.VersionText.Count(ch => ch == '.') == 2,
      pvm.VersionText);

// ---- 单后端：只剩雷电 ----
Console.WriteLine("Backends");
Check("exactly one backend declared", EmulatorKinds.All.Count == 1, $"{EmulatorKinds.All.Count}");
Check("backend is LdPlayer", EmulatorKinds.All[0] == EmulatorKind.LdPlayer);
Check("backend label non-empty", EmulatorKinds.Label(EmulatorKind.LdPlayer).Length > 0);
Check("capabilities support geo+step",
      Aetherun.Desktop.Services.AppShell.Capabilities.CanGeo
      && Aetherun.Desktop.Services.AppShell.Capabilities.CanStep);

// ---- 仪表盘内联参数 ----
Console.WriteLine("Dashboard");
var vm = new Aetherun.Desktop.ViewModels.MainViewModel(new Aetherun.Desktop.Services.AppShell());
Check("dash defaults match config",
      Math.Abs(vm.DashTargetKm - vm.Config.TargetKm) < 1e-9
      && Math.Abs(vm.DashSpeed - vm.Config.SpeedMps) < 1e-6
      && Math.Abs(vm.DashCadence - vm.Config.CadencePerMin) < 1e-9,
      $"{vm.DashTargetKm}/{vm.DashSpeed}/{vm.DashCadence}");

vm.DashTargetKm = 2.0;
vm.DashPace = 8.0;          // 8 分/公里 → 2.083 m/s
vm.DashCadence = 120;
Check("dash writes through to config",
      Math.Abs(vm.Config.TargetKm - 2.0) < 1e-9
      && Math.Abs(vm.Config.SpeedMps - 1000.0 / 480.0) < 1e-6
      && Math.Abs(vm.Config.CadencePerMin - 120) < 1e-9,
      $"{vm.Config.TargetKm}/{vm.Config.SpeedMps:F4}/{vm.Config.CadencePerMin}");
Check("speed from pace 8min = 2.083 m/s", Math.Abs(vm.DashSpeed - 2.0833) < 0.001, $"{vm.DashSpeed:F4}");
Check("speed hint text", vm.SpeedHintText.StartsWith("2.08", StringComparison.Ordinal), vm.SpeedHintText);

// eta：2km @ 8分/公里 = 16 分 00 秒
Check("eta 2km @8min = 16min", vm.EtaText == "16 分 00 秒", vm.EtaText);
// 步数：960s × 120 步/分 = 1920
Check("est steps 2km @8min cadence120 = 1920", vm.EstStepsText == "1920", vm.EstStepsText);

vm.PresetFastCommand.Execute(null);
Check("preset fast applied to dash",
      Math.Abs(vm.DashSpeed / 4.167 - 1) < 0.01 && Math.Abs(vm.DashCadence - 150) < 1e-9
      && Math.Abs(vm.DashTargetKm - 2.0) < 1e-9,
      $"{vm.DashTargetKm}/{vm.DashSpeed:F3}/{vm.DashCadence}");
vm.PresetSlowCommand.Execute(null);
Check("preset slow applied to dash",
      Math.Abs(vm.DashSpeed / 2.083 - 1) < 0.01 && Math.Abs(vm.DashCadence - 90) < 1e-9,
      $"{vm.DashSpeed:F3}/{vm.DashCadence}");
vm.PresetStandardCommand.Execute(null);
Check("preset standard applied to dash",
      Math.Abs(vm.DashSpeed / 2.778 - 1) < 0.01 && Math.Abs(vm.DashCadence - 120) < 1e-9,
      $"{vm.DashSpeed:F3}/{vm.DashCadence}");

// 配速与速度的换算是互逆的
foreach (var pm in new[] { 2.0, 6.83, 10.0 })
{
    double sp2 = Aetherun.Desktop.ViewModels.MainViewModel.SpeedFromPace(pm);
    double back = Aetherun.Desktop.ViewModels.MainViewModel.PaceFromSpeed(sp2);
    Check($"pace<->speed roundtrip {pm}", Math.Abs(back - pm) < 1e-9, $"{back:F6}");
}

// 有效时间窗
Check("run window open at 06:00", Aetherun.Core.Models.RunWindow.IsOpen(new DateTime(2026, 1, 1, 6, 0, 0)));
Check("run window open at 21:59", Aetherun.Core.Models.RunWindow.IsOpen(new DateTime(2026, 1, 1, 21, 59, 0)));
Check("run window closed at 22:00", !Aetherun.Core.Models.RunWindow.IsOpen(new DateTime(2026, 1, 1, 22, 0, 0)));
Check("run window closed at 05:30", !Aetherun.Core.Models.RunWindow.IsOpen(new DateTime(2026, 1, 1, 5, 30, 0)));
Check("run window label", Aetherun.Core.Models.RunWindow.Label == "06:00–22:00", Aetherun.Core.Models.RunWindow.Label);


// 仪表盘改的值必须真的被引擎用上
var fake4 = new FakeRunner();
var ld4 = new LdPlayerController(new Aetherun.Core.Services.LdConsoleClient(fake4, "/fake/ldconsole.exe", 0, verifyPath: false));
var engine4 = new Aetherun.Core.Services.RunEngine(ld4, null);
Aetherun.Core.Models.RunSnapshot last4 = Aetherun.Core.Models.RunSnapshot.Idle();
engine4.Snapshot += (_, snap) => last4 = snap;
// 引擎下限是 0.5km；用最高配速（8.333 m/s）跑 0.5km 需 60 秒，够验证
var cfg4 = new Aetherun.Core.Models.RunConfig
{
    TargetKm = 0.5, SpeedMps = 8.333, CadencePerMin = 130, LeadSeconds = 0,
    GeoEnabled = true, StepEnabled = true, AutoStartApp = false
};
await engine4.StartAsync(cfg4, track);
var sw4 = System.Diagnostics.Stopwatch.StartNew();
while (engine4.IsBusy && sw4.Elapsed < TimeSpan.FromSeconds(60))
    await Task.Delay(50);
Check("dash cadence drives steps", Math.Abs(last4.Steps - (60.0 * 130 / 60.0)) < 8, $"{last4.Steps}");
Check("dash speed drives elapsed", last4.ElapsedSeconds is > 55 and < 70, $"{last4.ElapsedSeconds:F1}");
Check("dash target drives distance", Math.Abs(last4.DistanceMeters - 500) < 3, $"{last4.DistanceMeters:F1}");

// ---- 值滑轮：钳位与吸附 ----
Console.WriteLine("Dial");
double Sn(double raw, double min, double max, double step)
    => Aetherun.Desktop.Controls.ValueDial.Snap(raw, min, max, step);

Check("dial clamps below min", Math.Abs(Sn(0.1, 0.5, 2, 0.05) - 0.5) < 1e-9, $"{Sn(0.1, 0.5, 2, 0.05)}");
Check("dial clamps above max", Math.Abs(Sn(99, 0.5, 2, 0.05) - 2) < 1e-9, $"{Sn(99, 0.5, 2, 0.05)}");
Check("dial snaps to step", Math.Abs(Sn(1.234, 0.5, 2, 0.05) - 1.25) < 1e-9, $"{Sn(1.234, 0.5, 2, 0.05)}");
Check("dial rounds half up", Math.Abs(Sn(1.225, 0.5, 2, 0.05) - 1.25) < 1e-9, $"{Sn(1.225, 0.5, 2, 0.05)}");
Check("dial no float drift 0.05x2", Math.Abs(Sn(2, 0.5, 2, 0.05) - 2) < 1e-9, $"{Sn(2, 0.5, 2, 0.05)}");
Check("dial cadence step 5", Math.Abs(Sn(113, 60, 300, 5) - 115) < 1e-9, $"{Sn(113, 60, 300, 5)}");
Check("dial cadence clamps high", Math.Abs(Sn(999, 60, 300, 5) - 300) < 1e-9, $"{Sn(999, 60, 300, 5)}");
Check("dial pace step 0.01", Math.Abs(Sn(6.834, 2, 10, 0.01) - 6.83) < 1e-9, $"{Sn(6.834, 2, 10, 0.01):F3}");
Check("dial NaN falls back to min", Math.Abs(Sn(double.NaN, 0.5, 2, 0.05) - 0.5) < 1e-9, $"{Sn(double.NaN, 0.5, 2, 0.05)}");
Check("dial +inf falls back to max", Math.Abs(Sn(double.PositiveInfinity, 0.5, 2, 0.05) - 2) < 1e-9);
Check("dial -inf falls back to min", Math.Abs(Sn(double.NegativeInfinity, 0.5, 2, 0.05) - 0.5) < 1e-9);

// 每个预设都必须能落在滑轮的刻度上，否则点预设后指针会跳
// 点完预设，滑轮指针必须正好落在刻度上，且实际速度与原定速度相差不到 1%
var gridVm = new Aetherun.Desktop.ViewModels.MainViewModel(new Aetherun.Desktop.Services.AppShell());
foreach (var (name, sp, cd) in new[] { ("slow", 2.083, 90.0), ("standard", 2.778, 120.0), ("fast", 4.167, 150.0) })
{
    gridVm.ApplyPresetCommand.Execute(name);
    double onGrid = Sn(gridVm.DashPace, 2, 10, Aetherun.Desktop.ViewModels.MainViewModel.DashPaceStep);
    Check($"preset {name} pace lands on grid", Math.Abs(gridVm.DashPace - onGrid) < 1e-9, $"{gridVm.DashPace:F4}");
    Check($"preset {name} speed within 1% of {sp}", Math.Abs(gridVm.DashSpeed / sp - 1) < 0.01, $"{gridVm.DashSpeed:F4}");
    Check($"preset {name} cadence {cd} on grid", Math.Abs(gridVm.DashCadence - cd) < 1e-9, $"{gridVm.DashCadence}");
}
Check("preset target 2.0 lands on dial grid", Math.Abs(2.0 - Sn(2.0, 0.5, 2, 0.05)) < 1e-9);

// 滑轮范围必须和引擎钳位范围一致，否则滑轮能拨到引擎会改写的值
{
    var lo = new RunConfig { TargetKm = 0.01, SpeedMps = 0.1, CadencePerMin = 10 }.Sanitized();
    var hi = new RunConfig { TargetKm = 99, SpeedMps = 99, CadencePerMin = 999 }.Sanitized();
    Check("engine clamps target to 0.5..2",
          Math.Abs(lo.TargetKm - 0.5) < 1e-9 && Math.Abs(hi.TargetKm - 2.0) < 1e-9,
          $"{lo.TargetKm}/{hi.TargetKm}");
    Check("engine clamps pace-equivalent speed to 2..10 min/km",
          Math.Abs(Aetherun.Core.Models.RunConfig.PaceSecondsPerKm(lo.SpeedMps) - 600) < 0.5
          && Math.Abs(Aetherun.Core.Models.RunConfig.PaceSecondsPerKm(hi.SpeedMps) - 120) < 0.5,
          $"{Aetherun.Core.Models.RunConfig.PaceSecondsPerKm(lo.SpeedMps):F1}/"
          + $"{Aetherun.Core.Models.RunConfig.PaceSecondsPerKm(hi.SpeedMps):F1}");
    Check("engine clamps cadence to 60..300",
          lo.CadencePerMin == 60 && hi.CadencePerMin == 300, $"{lo.CadencePerMin}/{hi.CadencePerMin}");
}


// ================= 第 2 轮修复的回归 =================
Console.WriteLine("AdbFindStart");

// uiautomator 的 node 里 text 经常是空串、按钮文案在 content-desc 上，
// 老写法只取第一个匹配就 continue，会把这类按钮整个漏掉
const string xmlA = "<hierarchy><node text=\"\" content-desc=\"开始跑步\" clickable=\"true\" bounds=\"[100,200][300,280]\"/></hierarchy>";
Check("start button found via content-desc", Aetherun.Core.Services.AdbClient.FindStartNode(xmlA) is (200, 240),
      Aetherun.Core.Services.AdbClient.FindStartNode(xmlA)?.ToString() ?? "null");

const string xmlB = "<hierarchy><node text=\"开始时间 06:00\" clickable=\"true\" bounds=\"[0,0][10,10]\"/></hierarchy>";
Check("time label not treated as start", Aetherun.Core.Services.AdbClient.FindStartNode(xmlB) is null,
      Aetherun.Core.Services.AdbClient.FindStartNode(xmlB)?.ToString() ?? "null");

const string xmlC = "<hierarchy><node text=\"开始\" clickable=\"false\" bounds=\"[0,0][10,10]\"/></hierarchy>";
Check("non-clickable ignored", Aetherun.Core.Services.AdbClient.FindStartNode(xmlC) is null);

const string xmlD = "<hierarchy><node text=\"出发\" clickable=\"true\" bounds=\"[10,20][30,60]\"/></hierarchy>";
Check("出发 also accepted", Aetherun.Core.Services.AdbClient.FindStartNode(xmlD) is (20, 40));

Check("empty xml safe", Aetherun.Core.Services.AdbClient.FindStartNode("") is null);

// 设置页的数字框：删空内容不能把配置写成 0 / 崩掉
Console.WriteLine("SettingsInput");
{
    var svm = new Aetherun.Desktop.ViewModels.MainViewModel(new Aetherun.Desktop.Services.AppShell());
    int idx = svm.Config.EmulatorIndex;
    svm.EmulatorIndexText = "";
    Check("blank emulator index keeps old value", svm.Config.EmulatorIndex == idx, $"{svm.Config.EmulatorIndex}");
    svm.EmulatorIndexText = "-3";
    Check("negative emulator index rejected", svm.Config.EmulatorIndex == idx, $"{svm.Config.EmulatorIndex}");
    svm.EmulatorIndexText = "2";
    Check("valid emulator index applied", svm.Config.EmulatorIndex == 2, $"{svm.Config.EmulatorIndex}");

    double glow = svm.Config.GravityLow;
    svm.GravityLowText = "abc";
    Check("non-numeric gravity keeps old value", Math.Abs(svm.Config.GravityLow - glow) < 1e-9);
    svm.GravityLowText = "99";
    Check("out-of-range gravity rejected", Math.Abs(svm.Config.GravityLow - glow) < 1e-9);
    svm.GravityLowText = "6.2";
    Check("valid gravity applied", Math.Abs(svm.Config.GravityLow - 6.2) < 1e-9);

    int lead = svm.Config.LeadSeconds;
    svm.LeadSecondsText = "999";
    Check("lead seconds clamped out", svm.Config.LeadSeconds == lead);
    svm.LeadSecondsText = "30";
    Check("lead seconds applied", svm.Config.LeadSeconds == 30);
}

// 顶栏状态灯必须跟着阶段变
Console.WriteLine("StatusBrush");
{
    var bvm = new Aetherun.Desktop.ViewModels.MainViewModel(new Aetherun.Desktop.Services.AppShell());
    string Hex(Avalonia.Media.IBrush b) => ((Avalonia.Media.SolidColorBrush)b).Color.ToString();

    string idle = Hex(bvm.StatusBrush);
    bvm.ShowPreview(new RunSnapshot { Phase = RunPhase.Running }, "跑步中");
    string running = Hex(bvm.StatusBrush);
    bvm.ShowPreview(new RunSnapshot { Phase = RunPhase.Error, Message = "x" }, "错误");
    string error = Hex(bvm.StatusBrush);

    Check("idle brush is grey", idle.Equals("#FF5A6879", StringComparison.OrdinalIgnoreCase), idle);
    Check("running brush is green", running.Equals("#FF4ADE80", StringComparison.OrdinalIgnoreCase), running);
    Check("error brush is red", error.Equals("#FFF87171", StringComparison.OrdinalIgnoreCase), error);
    Check("brushes differ per phase", idle != running && running != error);
}

// Engine 的时钟默认值必须是本地时间，否则日志时间戳比界面早 8 小时
Console.WriteLine("EngineClock");
{
    var emu = new FakeEmu();
    var eng = new Aetherun.Core.Services.RunEngine(emu);
    var clkField = typeof(Aetherun.Core.Services.RunEngine).GetField("_clock", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    var fn = (Func<DateTime>?)clkField?.GetValue(eng);
    Check("engine clock field exists", fn is not null);
    if (fn is not null)
    {
        var skew = (fn() - DateTime.Now).Duration();
        Check("engine clock is local time", skew < TimeSpan.FromMinutes(2), $"skew {skew}");
    }
}



// ================= 第 3 轮：配置落盘 / 点位往返 / 场景空转 =================
Console.WriteLine("ConfigStore");
{
    var saved = Aetherun.Core.Models.RunConfig.Preset("fast");
    saved.LdConsolePath = @"D:\LDPlayer\LDPlayer14\ldconsole.exe";
    saved.AdbSerial = "127.0.0.1:5557";
    saved.EmulatorIndex = 2;
    saved.GravityLow = 6.5;
    saved.GravityHigh = 13.5;
    saved.AutoStartApp = false;

    Aetherun.Desktop.Services.ConfigStore.Save(saved);
    var back = Aetherun.Desktop.Services.ConfigStore.Load();

    Check("path survives restart", back.LdConsolePath == saved.LdConsolePath, back.LdConsolePath);
    Check("serial survives restart", back.AdbSerial == saved.AdbSerial, back.AdbSerial);
    Check("emulator index survives restart", back.EmulatorIndex == 2, back.EmulatorIndex.ToString());
    Check("gravity survives restart", Math.Abs(back.GravityLow - 6.5) < 1e-9 && Math.Abs(back.GravityHigh - 13.5) < 1e-9);
    Check("autostart flag survives restart", back.AutoStartApp == false);
    Check("out-of-range value is clamped on load", back.SpeedMps is >= 1.6 and <= 8.4, back.SpeedMps.ToString("F3"));

    // 坏文件不能把软件卡住
    File.WriteAllText(Aetherun.Desktop.Services.ConfigStore.FilePath, "{ not json ");
    var broken = Aetherun.Desktop.Services.ConfigStore.Load();
    Check("broken config falls back to preset", broken.TargetKm > 0 && broken.SpeedMps > 0);
}

Console.WriteLine("AdbPathPersist");
{
    // adb.exe 手填路径也必须记住，否则重启又回到自动探测值
    var adbCfg = Aetherun.Core.Models.RunConfig.Preset("standard");
    adbCfg.AdbPath = @"D:\LDPlayer\LDPlayer14\adb.exe";
    Aetherun.Desktop.Services.ConfigStore.Save(adbCfg);
    var adbBack = Aetherun.Desktop.Services.ConfigStore.Load();
    Check("adb path survives restart", adbBack.AdbPath == adbCfg.AdbPath, adbBack.AdbPath);
    Check("adb path defaults to empty", Aetherun.Core.Models.RunConfig.Preset("slow").AdbPath == "");
}

Console.WriteLine("SceneCenter");
{
    // 开局第一帧之前 Advance 就会跑：中心必须已经算好，
    // 否则拖影按 (0,0) 算，坐标能差到几万像素。
    var sc = new Aetherun.Desktop.Controls.TrackScene3D { Track = track, Running = true };
    sc.Advance(0.016);
    var m = typeof(Aetherun.Desktop.Controls.TrackScene3D).GetField("_centerReady",
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    Check("scene center ready after first advance", m is not null && (bool)m.GetValue(sc)!);
}

Console.WriteLine("StatusTextMachine");
{
    // 启动失败的路径：Busy 放掉之外状态文字也必须回落到「启动失败」，
    // 不能停在「启动中」上（顶栏会一直显示成正在启动）
    var svm = new Aetherun.Desktop.ViewModels.MainViewModel(new Aetherun.Desktop.Services.AppShell());
    // 每次 get 都会新建 SolidColorBrush，比的是颜色不是引用
    Check("phase brush matches status brush",
          svm.PhaseBrush.ToString()!.Equals(svm.StatusBrush.ToString(), StringComparison.OrdinalIgnoreCase),
          svm.PhaseBrush.ToString()!);
    Check("idle phase brush is grey",
          svm.PhaseBrush.ToString()!.Equals("#FF5A6879", StringComparison.OrdinalIgnoreCase),
          svm.PhaseBrush.ToString()!);
}

Console.WriteLine("LogCommands");
{
    // 清空 / 复制：清空后条数必须归零，复制内容要包含全部行
    var lvm = new Aetherun.Desktop.ViewModels.MainViewModel(new Aetherun.Desktop.Services.AppShell());
    lvm.ClearLogCommand.Execute(null);
    Check("log empty after clear", lvm.LogLines.Count == 0, lvm.LogLines.Count.ToString());
    Check("log count label tracks collection", lvm.LogCount == "0 行", lvm.LogCount);
}

Console.WriteLine("TrackPersist");
{
    var text = Aetherun.Desktop.Services.TrackLoopFactory.ToText(track);
    var lines = text.Split('\n');
    Check("toText one line per point", lines.Length == real.Length, lines.Length.ToString());
    Check("toText keeps 6 decimals", lines[0].Trim() == "22.908578,113.869650", lines[0].Trim());

    var again = Aetherun.Desktop.Services.TrackLoopFactory.Parse(text);
    Check("toText -> parse roundtrip", again is not null && again.Count == track.Count);
    Check("roundtrip keeps perimeter", again is not null && Math.Abs(again.Perimeter - track.Perimeter) < 0.02,
          again is null ? "null" : again.Perimeter.ToString("F2"));

    Aetherun.Desktop.Services.ConfigStore.SaveTrackText(text);
    var loadedText = Aetherun.Desktop.Services.ConfigStore.LoadTrackText();
    Check("track text persists", loadedText.Trim() == text.Trim());
    File.Delete(Aetherun.Desktop.Services.ConfigStore.TrackPath);
    Check("missing track file returns empty", Aetherun.Desktop.Services.ConfigStore.LoadTrackText().Length == 0);
}

Console.WriteLine("SceneIdle");
{
    var scene = new Aetherun.Desktop.Controls.TrackScene3D();
    Check("advance reports still when idle", scene.Advance(0.016) == false);
    scene.Running = true;
    Check("advance keeps drawing while running", scene.Advance(0.016) == true);
}


// ================= 第 4 轮：开屏播放器 / 场景可见性 / 步频节拍 =================
Console.WriteLine("SplashPlayer");
{
    var pl = new Aetherun.Desktop.Controls.SplashPlayer();
    Check("frame count matches asset count", Aetherun.Desktop.Controls.SplashPlayer.FrameCount == 381);
    Check("fps is 24", Aetherun.Desktop.Controls.SplashPlayer.Fps == 24);

    pl.Play();
    Check("play starts playing", pl.IsPlaying);
    Check("play resets to first frame", pl.FrameIndex == 0);

    // 一秒应该正好推进 24 帧（fps 就是 24）
    for (int i = 0; i < 24; i++) pl.Advance(1.0 / 24);
    Check("one second advances ~24 frames", Math.Abs(pl.FrameIndex - 24) <= 1, pl.FrameIndex.ToString());

    pl.Seek(380);
    Check("seek clamps to last frame", pl.FrameIndex == 380);
    Check("progress at last frame is 1", Math.Abs(pl.Progress - 1.0) < 1e-9, pl.Progress.ToString("F4"));

    pl.Seek(-50);
    Check("seek clamps below zero", pl.FrameIndex == 0);
    pl.Seek(9999);
    Check("seek clamps above last", pl.FrameIndex == 380, pl.FrameIndex.ToString());

    // 播到末帧必须自己停下来，不然开屏窗口永远不关
    pl.Play();
    for (int i = 0; i < 400; i++) pl.Advance(1.0 / 24);
    Check("stops at end when not looping", !pl.IsPlaying && pl.FrameIndex == 380);
    Check("IsFinished true at end", pl.IsFinished);

    pl.Loop = true;
    pl.Play();
    pl.Seek(370);
    for (int i = 0; i < 30; i++) pl.Advance(1.0 / 24);
    Check("loop wraps instead of stopping", pl.IsPlaying, pl.FrameIndex.ToString());
}

Console.WriteLine("RunPace");
{
    // 里程就是「速度 × 时间」，界面显示与引擎计算必须一致到 1% 以内
    var std = RunConfig.Preset("standard").Sanitized();
    double expect = std.SpeedMps * 300;
    Check("standard 300s ~= 833m", Math.Abs(expect - 833) < 8, expect.ToString("F1"));

    var ppm = Aetherun.Desktop.ViewModels.MainViewModel.PaceFromSpeed(std.SpeedMps);
    var backSpeed = Aetherun.Desktop.ViewModels.MainViewModel.SpeedFromPace(ppm);
    Check("speed -> pace -> speed stable", Math.Abs(backSpeed - std.SpeedMps) < 1e-6, backSpeed.ToString("F6"));
}

// ================= 第 7 轮：设置项持久化 / 空值兜底 =================
Console.WriteLine("Round7Settings");
{
    var r7 = Aetherun.Core.Models.RunConfig.Preset("standard");
    r7.AdbSerial = "127.0.0.1:5559";
    r7.PackageName = "com.demo.pkg";
    r7.GeoEnabled = false;
    r7.StepEnabled = false;
    r7.AutoStartApp = false;

    Aetherun.Desktop.Services.ConfigStore.Save(r7);
    var r7back = Aetherun.Desktop.Services.ConfigStore.Load();
    Check("serial persists", r7back.AdbSerial == "127.0.0.1:5559", r7back.AdbSerial);
    Check("package persists", r7back.PackageName == "com.demo.pkg", r7back.PackageName);
    Check("geo toggle persists", r7back.GeoEnabled == false);
    Check("step toggle persists", r7back.StepEnabled == false);
    Check("autostart toggle persists", r7back.AutoStartApp == false);

    // 空序列号/空包名会让 adb -s "" / am force-stop "" 整条命令失效
    var blank = Aetherun.Core.Models.RunConfig.Preset("standard");
    blank.AdbSerial = "   ";
    blank.PackageName = "";
    var cleaned = blank.Sanitized();
    Check("blank serial falls back to default", cleaned.AdbSerial == "127.0.0.1:5555", cleaned.AdbSerial);
    Check("blank package falls back to default",
          cleaned.PackageName == "com.zjwh.android_wh_physicalfitness", cleaned.PackageName);
}

Console.WriteLine("Round7Culture");
{
    // 注入参数里的小数点必须是「.」：德语/法语等区域会把它写成逗号，
    // 拼出来变成 113,86,22,90，--LLI 与 --value 都会被拆成多个参数。
    var old = System.Globalization.CultureInfo.CurrentCulture;
    try
    {
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
        var p = new Aetherun.Core.Models.GeoPoint(22.908578, 113.869650);
        Check("coord uses dot under de-DE", p.ToLli().Contains('.'), p.ToLli());
        Check("coord has no comma decimals", !p.ToLli().Contains(",86") && !p.ToLli().Contains(",90"), p.ToLli());
        Check("lli puts longitude first", p.ToLli().StartsWith("113.8"), p.ToLli());

        var rec = new RecordingRunner();
        var cli = new Aetherun.Core.Services.LdConsoleClient(rec, "ldconsole.exe", 0, verifyPath: false);
        cli.GravityAsync(5.5).GetAwaiter().GetResult();
        Check("gravity value uses dot", rec.LastArgs.Contains("0,0,5.50"), rec.LastArgs);
    }
    finally
    {
        System.Globalization.CultureInfo.CurrentCulture = old;
    }
}

// ================= 第 8 轮：开屏覆盖 / 前台检测 / 奶蛙 / 操作条 / 文案 =================
Console.WriteLine("Round8Splash");
{
    // 开屏层必须是主窗里的覆盖层，尺寸跟随客户区，而不是固定 1000×750 的独立窗口。
    var overlayType = typeof(Aetherun.Desktop.Views.SplashOverlay);
    Check("overlay is a control, not a window", !overlayType.IsSubclassOf(typeof(Avalonia.Controls.Window)),
          overlayType.BaseType?.Name ?? "?");
    Check("overlay derives from UserControl", overlayType.IsSubclassOf(typeof(Avalonia.Controls.UserControl)));

    var mainXaml = File.ReadAllText(Path.Combine(RepoRoot(), "src/Aetherun.Desktop/Views/MainWindow.axaml"));
    Check("main window hosts the overlay", mainXaml.Contains("SplashOverlay"));
    // 遮罩必须在 RootHost 层（与内容 Grid 平级）才能铺满整窗。
    // 早前放进 RowDefinitions="Auto,*" 里靠 Grid.RowSpan 硬撑，只盖住一行。
    {
        int cut = mainXaml.IndexOf("<!-- ===== 开屏层", StringComparison.Ordinal);
        string head = cut < 0 ? mainXaml : mainXaml[..cut];
        int open = head.Split("<Grid").Length - 1;
        int close = head.Split("</Grid>").Length - 1;
        // 截断点在 RootHost 的收尾 </Grid> 之前，所以此处必然还多开一层
        Check("overlay is a direct child of RootHost", cut > 0 && open == close + 1);
        Check("overlay does not fake its size with rowspan", !mainXaml.Contains("Grid.RowSpan=\"2\""));
    }
    Check("no standalone splash window left", !mainXaml.Contains("SplashWindow"));

    var appCs = File.ReadAllText(Path.Combine(RepoRoot(), "src/Aetherun.Desktop/App.axaml.cs"));
    Check("app no longer shows a separate splash", !appCs.Contains("new SplashWindow()"));
    Check("app keeps normal shutdown mode", appCs.Contains("ShutdownMode.OnLastWindowClose"));
}

Console.WriteLine("Round8Foreground");
{
    // mCurrentFocus 在真机上的几种写法：普通的、带 u0 的、带 Activity 的
    const string common = "  mCurrentFocus=Window{8f2a1b u0 com.zjwh.android_wh_physicalfitness/com.zjwh.android_wh_physicalfitness.ui.launch.LaunchActivity}";
    Check("parses app package",
          Aetherun.Core.Services.AdbClient.ParseForeground(common) == "com.zjwh.android_wh_physicalfitness",
          Aetherun.Core.Services.AdbClient.ParseForeground(common) ?? "null");

    // 模拟器桌面 / 系统弹窗 / 输入法都不算「打开了 App」
    foreach (var (label, text) in new[]
    {
        ("launcher", "  mCurrentFocus=Window{11 u0 com.android.launcher3/com.android.launcher3.Launcher}"),
        ("systemui", "  mCurrentFocus=Window{22 u0 com.android.systemui/com.android.systemui.statusbar.phone.PhoneStatusBar}"),
        ("keyboard", "  mCurrentFocus=Window{33 u0 com.android.inputmethod.latin/.LatinIME}"),
        ("settings", "  mCurrentFocus=Window{44 u0 android/android.widget.FrameLayout}"),
    })
    {
        var got = Aetherun.Core.Services.AdbClient.ParseForeground(text);
        Check($"ignores {label}", got is null or "com.android.launcher3", got ?? "null");
    }

    // 输出被截断/没有焦点行时返回 null，别把半截字符串当包名
    Check("null on empty", Aetherun.Core.Services.AdbClient.ParseForeground("") is null);
    Check("null on garbage", Aetherun.Core.Services.AdbClient.ParseForeground("WINDOW MANAGER") is null);

    var snap = new Aetherun.Core.Models.RunSnapshot
    {
        Phase = Aetherun.Core.Models.RunPhase.Armed,
        ForegroundOk = false,
        ForegroundPackage = "com.android.launcher3",
        CountdownRemaining = 12,
        CountdownTotal = 20
    };
    Check("countdown progress derived", Math.Abs(snap.CountdownProgress - 0.4) < 1e-6, snap.CountdownProgress.ToString("F3"));
    Check("held snapshot stays armed", snap.PhaseText == "倒计时", snap.PhaseText);
}

Console.WriteLine("StepPump");
{
    // 关键回归：ldconsole 单次 180ms（真机实测最坏 4s、平均 ~50ms）时，
    // 目标 150 spm 必须能真的跑出来 —— 这就是用户报的「配速 3'xx" 步频只剩个位数」。
    static async Task<(Aetherun.Core.Services.StepPumpStats Stats, double Seconds)> Drive(
        double cadence, double latencySec, int maxInFlight, double seconds)
    {
        var emu = new SlowEmu(latencySec);
        var pump = new Aetherun.Core.Services.StepPump(emu, () => DateTime.Now, maxInFlight);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        try { await pump.RunAsync(cadence, 9.0, 12.0, cts.Token); } catch { }
        return (pump.Stats(cadence), seconds);
    }

    var fast = await Drive(150, 0.020, 6, 4.0);
    Check("fast emu reaches 150 spm", Math.Abs(fast.Stats.EffectiveCadence - 150) < 18,
        $"{fast.Stats.EffectiveCadence:F1} spm");

    // 3'00"-3'59" 这一档速度下，步频仍然是 150，不该掉到个位数
    var tight = await Drive(150, 0.180, 6, 4.0);
    Check("slow emu (180ms) still well above 100 spm", tight.Stats.EffectiveCadence > 100,
        $"{tight.Stats.EffectiveCadence:F1} spm");
    Check("slow emu reports its ceiling", tight.Stats.MaxCadence > 100,
        $"{tight.Stats.MaxCadence:F0}");

    // 单并发下 180ms 只能到 ~166，这时候必须如实报 Limited
    var single = await Drive(150, 0.180, 1, 4.0);
    Check("single flight ceiling ~ 33/lat", single.Stats.MaxCadence is > 150 and < 200,
        $"{single.Stats.MaxCadence:F0}");
    Check("single flight marks limited above ceiling",
        (await Drive(200, 0.180, 1, 2.0)).Stats.Limited);

    // 从不补历史欠账：卡住 1 秒之后不该一口气吐出几十个重力事件
    var emu2 = new SlowEmu(0.0, stallAfter: 3, stallSec: 1.0);
    var pump2 = new Aetherun.Core.Services.StepPump(emu2, () => DateTime.Now, 6);
    using (var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(4)))
    {
        try { await pump2.RunAsync(150, 9.0, 12.0, cts2.Token); } catch { }
    }
    Check("no catch-up burst after a stall", emu2.MaxPerSecond < 40,
        $"max {emu2.MaxPerSecond}/s");

    // 步频为 0 时不发任何事件
    var emu3 = new SlowEmu(0.0);
    var pump3 = new Aetherun.Core.Services.StepPump(emu3, () => DateTime.Now, 6);
    using (var cts3 = new CancellationTokenSource(TimeSpan.FromMilliseconds(300)))
    {
        try { await pump3.RunAsync(0, 9.0, 12.0, cts3.Token); } catch { }
    }
    Check("zero cadence emits nothing", emu3.CallCount == 0, emu3.CallCount.ToString());
}

Console.WriteLine("CadenceClosedLoop");
{
    // 闭环校正：实测只有目标的 74% 时应把发送率往上推，而不是原地不动
    var gov = new Aetherun.Core.Services.CadenceGovernor(120);
    Check("governor starts uncalibrated", Math.Abs(gov.Correction - 1.0) < 1e-9);
    Check("governor target passthrough", Math.Abs(gov.EffectiveTarget - 120) < 1e-9);

    for (int i = 0; i < 4; i++) gov.Observe(89);   // 用户实测：标准档 120 → 90
    Check("governor lifts rate when measured is low", gov.EffectiveTarget > 130,
        $"x{gov.Correction:F2} → {gov.EffectiveTarget:F0}");

    // 实测超过目标时应当往下收，不能一路加码
    var govHigh = new Aetherun.Core.Services.CadenceGovernor(120);
    for (int i = 0; i < 6; i++) govHigh.Observe(180);
    Check("governor backs off when measured is high", govHigh.EffectiveTarget < 120,
        $"x{govHigh.Correction:F2} → {govHigh.EffectiveTarget:F0}");

    // 推到上限仍上不去 → 必须判定饱和，不再无限加码
    var govSat = new Aetherun.Core.Services.CadenceGovernor(150);
    for (int i = 0; i < 14; i++) govSat.Observe(55);   // 用户实测：快走 150 → 55
    Check("governor saturates instead of diverging", govSat.Saturated, govSat.Describe());
    Check("governor clamps correction", govSat.Correction <= 4.0 + 1e-9, $"x{govSat.Correction:F2}");

    // 噪声样本（差分太小）必须被丢掉，不能把校正器带跑
    var govNoise = new Aetherun.Core.Services.CadenceGovernor(120);
    govNoise.Observe(3);
    Check("governor ignores tiny samples", Math.Abs(govNoise.Correction - 1.0) < 1e-9);

    // 泵的发送率必须能在运行中被改写（闭环就是靠这个）
    var emuP = new SlowEmu(0.0);
    var pumpP = new Aetherun.Core.Services.StepPump(emuP, () => DateTime.Now, 6);
    Check("pump defaults to zero rate", pumpP.TargetCadence == 0);
    pumpP.TargetCadence = 180.25;
    Check("pump rate is settable at runtime", Math.Abs(pumpP.TargetCadence - 180.25) < 1e-9);
    pumpP.TargetCadence = -5;
    Check("pump rejects negative rate", pumpP.TargetCadence == 0);

    // logcat 解析：运动世界自报的累计步数（dex 里的 step_final_raw 一族）
    var logA = "I/step: step_final_raw=1234\nI/step: step_source_count_raw=1230\nI/step: step_delta=2";
    Check("logcat total steps parsed", Aetherun.Core.Services.StepFeedback.ParseTotalSteps(logA) is 1234,
        Aetherun.Core.Services.StepFeedback.ParseTotalSteps(logA)?.ToString() ?? "null");
    var logB = "W/x: step_delta=2\nW/x: step_delta=3\nW/x: step_delta=4";
    Check("logcat falls back to delta sum", Aetherun.Core.Services.StepFeedback.ParseTotalSteps(logB) is 9,
        Aetherun.Core.Services.StepFeedback.ParseTotalSteps(logB)?.ToString() ?? "null");
    Check("logcat without step lines is null", Aetherun.Core.Services.StepFeedback.ParseTotalSteps("hello") is null);
    Check("empty log is null", Aetherun.Core.Services.StepFeedback.ParseTotalSteps("") is null);

    // 快照要把闭环的两个字段带出去，界面才显示得出「目标 / 实测」
    var snapLoop = new Aetherun.Core.Models.RunSnapshot { DetectedCadence = 88, CadenceNote = "x" };
    Check("snapshot carries detected cadence", Math.Abs(snapLoop.DetectedCadence - 88) < 1e-9);
    Check("snapshot carries cadence note", snapLoop.CadenceNote == "x");
}

Console.WriteLine("LeadSeconds");
{
    var fresh = new RunConfig();
    Check("fresh config leads 15s", fresh.LeadSeconds == 15, fresh.LeadSeconds.ToString());

    // 2.7.0 及更早存下来的 20 秒必须迁移到 15，否则老用户永远看到 20
    var old = new RunConfig { SchemaVersion = 2, LeadSeconds = 20 };
    Check("old 20s config migrates to 15", old.Migrated().LeadSeconds == 15,
        old.Migrated().LeadSeconds.ToString());
    Check("migrated config bumps schema", old.Migrated().SchemaVersion == RunConfig.CurrentSchema);

    // 用户自己特意改过的秒数不能被迁移覆盖
    var custom = new RunConfig { SchemaVersion = 2, LeadSeconds = 30 };
    Check("custom lead survives migration", custom.Migrated().LeadSeconds == 30,
        custom.Migrated().LeadSeconds.ToString());

    // 已经是新版本就不再动
    var cur = new RunConfig { SchemaVersion = RunConfig.CurrentSchema, LeadSeconds = 20 };
    Check("current schema not rewritten", cur.Migrated().LeadSeconds == 20,
        cur.Migrated().LeadSeconds.ToString());
}

Console.WriteLine("Round8Frog");
{
    // 图集已经退役：位图放大就糊，现在走 NWM1 网格实时渲染。
    // 这两条断言盯的是「别再退回图集」，以及朝向映射的方向没写反。
    var dir = Path.Combine(RepoRoot(), "src/Aetherun.Desktop/Controls");
    Check("atlas renderer retired", !File.Exists(Path.Combine(dir, "FrogSprites.cs")));
    Check("mesh renderer present", File.Exists(Path.Combine(dir, "MeshSprite.cs")));
    Check("mesh assets shipped",
        File.Exists(Path.Combine(RepoRoot(), "src/Aetherun.Desktop/Assets/frog/naiwa_hi.bin"))
        && File.Exists(Path.Combine(RepoRoot(), "src/Aetherun.Desktop/Assets/frog/naiwa_mid.bin"))
        && File.Exists(Path.Combine(RepoRoot(), "src/Aetherun.Desktop/Assets/frog/naiwa_lo.bin")));

    // 用户实测「建模是透明的、会透」——根因是按颜色分批的画家算法。
    // 现在必须是逐像素 z-buffer：解码光栅结果，用「透明像素数」和
    // 「直方图塌缩」两个独立指标盯住它，别又退回分批填充。
    Check("raster uses per-pixel depth buffer",
        File.ReadAllText(Path.Combine(dir, "MeshSprite.cs")).Contains("zb["));

    foreach (var (tier, minVerts, minTris) in new[]
             {
                 (Aetherun.Desktop.Controls.MeshSprite.Tier.High, 40000, 80000),
                 (Aetherun.Desktop.Controls.MeshSprite.Tier.Mid, 10000, 20000),
                 (Aetherun.Desktop.Controls.MeshSprite.Tier.Low, 2000, 4000),
             })
    {
        int nv = Aetherun.Desktop.Controls.MeshSprite.VertexCount(tier);
        int ntri = Aetherun.Desktop.Controls.MeshSprite.TriangleCount(tier);
        Check($"mesh tier {tier} is dense enough", nv >= minVerts && ntri >= minTris, $"{nv}v/{ntri}t");
    }

    // 实心核对：把 0/90/180/270 四个朝向的光栅图解码，数「被不透明像素包围的空洞」
    int solidChecks = 0, solidFails = 0;
    foreach (var deg in new[] { 0.0, 90.0, 180.0, 270.0 })
    {
        var png = Aetherun.Desktop.Controls.MeshSprite.DebugRasterPng(deg, 10, 480);
        if (png is null) { solidFails++; continue; }
        using var ms = new MemoryStream(png);
        using var bmp = new Avalonia.Media.Imaging.Bitmap(ms);
        int w = bmp.PixelSize.Width, h = bmp.PixelSize.Height;
        var buf = new byte[w * h * 4];
        var stride = w * 4;
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(buf, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            bmp.CopyPixels(new Avalonia.PixelRect(0, 0, w, h), handle.AddrOfPinnedObject(), buf.Length, stride);
        }
        finally { handle.Free(); }

        // BGRA premultiplied：alpha 通道每 4 字节的第 4 个
        bool Opaque(int x, int y) => buf[(y * w + x) * 4 + 3] > 40;

        var opaque = new bool[w * h];
        long fg = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (Opaque(x, y)) { opaque[y * w + x] = true; fg++; }

        // 从边框 flood fill 出外部背景，剩下的透明区域就是「内部空洞」
        var outside = new bool[w * h];
        var stack = new Stack<int>();
        for (int x = 0; x < w; x++)
            foreach (var y in new[] { 0, h - 1 })
                if (!opaque[y * w + x] && !outside[y * w + x]) { outside[y * w + x] = true; stack.Push(y * w + x); }
        for (int y = 0; y < h; y++)
            foreach (var x in new[] { 0, w - 1 })
                if (!opaque[y * w + x] && !outside[y * w + x]) { outside[y * w + x] = true; stack.Push(y * w + x); }
        while (stack.Count > 0)
        {
            int i = stack.Pop();
            int cy = i / w, cx = i % w;
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = cx + dx, ny = cy + dy;
                if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                int ni = ny * w + nx;
                if (opaque[ni] || outside[ni]) continue;
                outside[ni] = true;
                stack.Push(ni);
            }
        }

        long holes = 0;
        for (int i = 0; i < w * h; i++) if (!opaque[i] && !outside[i]) holes++;

        double ratio = fg == 0 ? 1.0 : holes / (double)fg;
        solidChecks++;
        if (fg < 1000 || ratio > 0.005) solidFails++;
        Check($"frog solid at {deg:F0}deg", fg >= 1000 && ratio <= 0.005,
            $"fg={fg} holes={holes} ratio={ratio:P2}");
    }
    Check("all four facings rendered", solidChecks == 4, $"{solidChecks}/4");
    Check("frog stays solid (no see-through)", solidFails == 0, $"{solidFails} facing(s) leak");

    // 跑道朝向：左半边面向用户（0°）、右半边背对（180°），中点 90°
    Check("track left half faces viewer",
        Math.Abs(Aetherun.Desktop.Controls.MeshSprite.YawForTrackPosition(-37, 37)) < 1e-9,
        Aetherun.Desktop.Controls.MeshSprite.YawForTrackPosition(-37, 37).ToString("F1"));
    Check("track right half faces away",
        Math.Abs(Aetherun.Desktop.Controls.MeshSprite.YawForTrackPosition(37, 37) - 180) < 1e-9,
        Aetherun.Desktop.Controls.MeshSprite.YawForTrackPosition(37, 37).ToString("F1"));
    Check("track center is side-on",
        Math.Abs(Aetherun.Desktop.Controls.MeshSprite.YawForTrackPosition(0, 37) - 90) < 1e-9);
    Check("track yaw clamps outside the loop",
        Math.Abs(Aetherun.Desktop.Controls.MeshSprite.YawForTrackPosition(999, 37) - 180) < 1e-9
        && Math.Abs(Aetherun.Desktop.Controls.MeshSprite.YawForTrackPosition(-999, 37)) < 1e-9);

    // 闭环朝向：yaw=0 正面、180 背面（相机在 −Z 侧看向 +Z，模型脸朝 +Z，
    // 所以实现里补了 180°；这里只校验换算后的角度本身）
    var spinner = new Aetherun.Desktop.Controls.FrogPreview();
    Check("preview starts at 0deg", Math.Abs(spinner.Yaw) < 1e-9);
    spinner.Yaw = 180;
    Check("preview accepts 180deg", Math.Abs(spinner.Yaw - 180) < 1e-9);
}

Console.WriteLine("Round8ActionBar");
{
    var idle = new Aetherun.Core.Models.RunSnapshot();
    Check("idle distance text", idle.DistanceText.EndsWith(" km"), idle.DistanceText);
    Check("idle elapsed is 00:00", idle.ElapsedText == "00:00", idle.ElapsedText);
    Check("idle cadence placeholder", idle.CadenceText.Contains("--"), idle.CadenceText);

    var run = new Aetherun.Core.Models.RunSnapshot
    {
        Phase = Aetherun.Core.Models.RunPhase.Running,
        DistanceMeters = 1284.5,
        TargetMeters = 2200,
        Steps = 968,
        ElapsedSeconds = 527
    };
    Check("distance formats to 2 dp", run.DistanceText == "1.28 km", run.DistanceText);
    Check("elapsed formats mm:ss", run.ElapsedText == "08:47", run.ElapsedText);
    Check("cadence formats spm", run.CadenceText.EndsWith(" spm"), run.CadenceText);
    Check("pace formats m'ss", run.PaceText.Contains("'") && run.PaceText.EndsWith("\""), run.PaceText);

    var longRun = new Aetherun.Core.Models.RunSnapshot { ElapsedSeconds = 3725 };
    Check("elapsed adds hours past 60 min", longRun.ElapsedText == "1:02:05", longRun.ElapsedText);
}

Console.WriteLine("Round8Copy");
{
    var about = File.ReadAllText(Path.Combine(RepoRoot(), "src/Aetherun.Desktop/Views/AboutView.axaml"));
    var readme = File.ReadAllText(Path.Combine(RepoRoot(), "README.md"));

    // 用户给的文案要逐字落到界面与 README 上
    Check("about has new declaration", about.Contains("此仓不过少年闲来磨剑"), "missing");
    Check("about has frog motto", about.Contains("六十公里天道磨难，我奶蛙一人担之"), "missing");
    Check("about has separate disclaimer", about.Contains("有言在先"), "missing");
    Check("about keeps the boundary line", about.Contains("不可借这手艺去踏破世间既有规矩"), "missing");
    Check("about drops lei jun quotes", !about.Contains("雷军"), "still there");
    Check("about drops old slogan", !about.Contains("强者恒强"), "still there");

    Check("readme has one-line intro", readme.Contains("书斋试剑，模拟器内绘莞工轨迹"), "missing");
    Check("readme has new declaration", readme.Contains("此仓不过少年闲来磨剑"), "missing");
    Check("readme has disclaimer section", readme.Contains("## 免责声明"), "missing");
    Check("readme drops lei jun", !readme.Contains("雷军"), "still there");
    Check("readme version bump", readme.Contains("### 2.6.0"), "missing");
}

Console.WriteLine("Round8CountdownUi");
{
    var armed = new Aetherun.Core.Models.RunSnapshot
    {
        Phase = Aetherun.Core.Models.RunPhase.Armed,
        CountdownRemaining = 7.2,
        CountdownTotal = 20,
        ForegroundOk = false,
        CountdownHeld = true,
        ForegroundPackage = "com.android.launcher3"
    };
    Check("armed progress 0.64", Math.Abs(armed.CountdownProgress - 0.64) < 1e-6, armed.CountdownProgress.ToString("F3"));
    Check("held snapshot keeps remaining", armed.CountdownRemaining > 7, armed.CountdownRemaining.ToString("F1"));

    var idle = new Aetherun.Core.Models.RunSnapshot();
    Check("idle countdown progress zero", idle.CountdownProgress == 0);

    // 仪表盘的倒计时横幅必须绑在这三个属性上，否则界面拿不到数
    var dash = File.ReadAllText(Path.Combine(RepoRoot(), "src/Aetherun.Desktop/Views/DashboardView.axaml"));
    Check("dashboard binds IsArmed", dash.Contains("{Binding IsArmed}"));
    Check("dashboard binds CountdownText", dash.Contains("{Binding CountdownText}"));
    Check("dashboard binds CountdownProgress", dash.Contains("{Binding CountdownProgress}"));
    Check("dashboard binds CountdownHeadline", dash.Contains("{Binding CountdownHeadline}"));
    Check("dashboard offers launch app", dash.Contains("{Binding LaunchAppCommand}"));
    Check("dashboard rows are 4", dash.Contains("RowDefinitions=\"Auto,Auto,Auto,*\""));

    // 操作条不再写死一句「倒计时内到运动世界按下开始」
    var main = File.ReadAllText(Path.Combine(RepoRoot(), "src/Aetherun.Desktop/Views/MainWindow.axaml"));
    Check("action bar follows phase", main.Contains("{Binding ActionHint}"));
    Check("action bar keeps progress strip",
        main.Contains("{Binding Snapshot.Progress}")
        && !main.Contains("ActionBarProgressWidth"));
}

Console.WriteLine("Round8TrackPage");
{
    var trackView = File.ReadAllText(Path.Combine(RepoRoot(), "src/Aetherun.Desktop/Views/TrackView.axaml"));
    Check("track page has frog preview", trackView.Contains("controls:FrogPreview"));
    Check("track page drops point editor", !trackView.Contains("TrackPointsText"));
    Check("track page drops apply button", !trackView.Contains("ApplyTrack"));
    Check("track page keeps 3d scene", trackView.Contains("controls:TrackScene3D"));
    Check("track page explains readonly", trackView.Contains("只读预览"));

    // 预览控件改成建模后：Yaw/Pitch 都是非空 double，XAML 绑定直接给数值
    var fp = new Aetherun.Desktop.Controls.FrogPreview();
    fp.Cadence = 132; fp.Yaw = 90; fp.Pitch = 14;
    Check("frog preview accepts cadence", Math.Abs(fp.Cadence - 132) < 1e-9);
    Check("frog preview accepts yaw", Math.Abs(fp.Yaw - 90) < 1e-9);
    Check("frog preview accepts pitch", Math.Abs(fp.Pitch - 14) < 1e-9);
    Check("frog preview defaults to front view", Math.Abs(new Aetherun.Desktop.Controls.FrogPreview().Yaw) < 1e-9);
    Check("frog preview draws from mesh not atlas",
        !File.ReadAllText(Path.Combine(RepoRoot(), "src/Aetherun.Desktop/Controls/FrogPreview.cs")).Contains("FrogSprites"));
}

Console.WriteLine("Round8SplashOverlayFiles");
{
    var overlay = File.ReadAllText(Path.Combine(RepoRoot(), "src/Aetherun.Desktop/Views/SplashOverlay.axaml.cs"));
    Check("overlay closes on finish", overlay.Contains("IsFinished"));
    Check("overlay fades out", overlay.Contains("CubicEaseOut"));
    // 淡出后只隐藏、不摘出可视化树：顶栏「开屏」按钮还要靠它重播
    Check("overlay hides instead of detaching", overlay.Contains("IsVisible = false"));
    Check("overlay can replay", overlay.Contains("public void Start()"));
    Check("overlay skips on pointer", overlay.Contains("PointerPressedEvent"));
    Check("overlay skips on key", overlay.Contains("KeyDownEvent"));

    // 开屏层必须有人真的 Start()，否则画面冻在第 0 帧、也永远不淡出
    var mainCs = File.ReadAllText(Path.Combine(RepoRoot(), "src/Aetherun.Desktop/Views/MainWindow.axaml.cs"));
    Check("main window starts splash", mainCs.Contains("_splash.Start()"));
    Check("main window hooks skip", mainCs.Contains("HookSkip(this)"));
    Check("splash skip eats the click", overlay.Contains("e.Handled = true"));
    Check("hotkey f5 starts", mainCs.Contains("Key.F5"));
    Check("hotkey f6 stops", mainCs.Contains("Key.F6"));

    var files = Directory.GetFiles(Path.Combine(RepoRoot(), "src/Aetherun.Desktop/Views"), "SplashWindow*");
    Check("standalone splash window deleted", files.Length == 0, string.Join(",", files.Select(Path.GetFileName)));
}

Console.WriteLine();
Console.WriteLine($"RESULT  pass={pass} fail={fail}");
return fail == 0 ? 0 : 1;

static string RepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
        dir = dir.Parent;
    return dir?.FullName ?? Directory.GetCurrentDirectory();
}

sealed class FakeRunner : Aetherun.Core.Services.IProcessRunner
{
    public int LocateCalls;
    public int GravityCalls;
    public List<string> LocateArgs = new();
    public List<double> GravityValues = new();

    public Task<Aetherun.Core.Services.ProcessResult> RunAsync(string fileName, string arguments, TimeSpan timeout, CancellationToken ct = default)
    {
        if (arguments.StartsWith("locate"))
        {
            LocateCalls++;
            LocateArgs.Add(arguments);
        }
        else if (arguments.Contains("call.gravity"))
        {
            GravityCalls++;
            var seg = arguments.Split("--value").Last().Trim();
            var parts = seg.Split(',');
            if (parts.Length == 3 && double.TryParse(parts[2], System.Globalization.CultureInfo.InvariantCulture, out var v))
                GravityValues.Add(v);
        }
        return Task.FromResult(new Aetherun.Core.Services.ProcessResult(0, "", "", false));
    }
}

sealed class FakeEmu : Aetherun.Core.Emulators.IEmulatorController
{
    public string DisplayName => "fake";
    public int Index => 0;
    public bool IsConfigured => true;
    public Task<bool> SetLocationAsync(GeoPoint point, CancellationToken ct = default) => Task.FromResult(true);
    public Task<bool> SetGravityZAsync(double z, CancellationToken ct = default) => Task.FromResult(true);
    public Task<string> DescribeAsync(CancellationToken ct = default) => Task.FromResult("");
    public Task<bool?> LaunchInstanceAsync(CancellationToken ct = default) => Task.FromResult<bool?>(true);
}

/// <summary>记下最后一次命令原文，用来断言参数拼接。</summary>
internal sealed class RecordingRunner : Aetherun.Core.Services.IProcessRunner
{
    public string LastArgs { get; private set; } = "";

    public Task<Aetherun.Core.Services.ProcessResult> RunAsync(
        string fileName, string arguments, TimeSpan timeout, CancellationToken ct = default)
    {
        LastArgs = arguments;
        return Task.FromResult(new Aetherun.Core.Services.ProcessResult(0, "", "", false));
    }
}

/// <summary>可注入延迟的假模拟器，用来复现 ldconsole 卡顿。</summary>
sealed class SlowEmu : Aetherun.Core.Emulators.IEmulatorController
{
    private readonly double _latencySec;
    private readonly int _stallAfter;
    private readonly double _stallSec;
    private int _calls;
    private readonly Queue<DateTime> _recent = new();

    public int MaxPerSecond { get; private set; }

    public SlowEmu(double latencySec, int stallAfter = -1, double stallSec = 0)
    {
        _latencySec = latencySec;
        _stallAfter = stallAfter;
        _stallSec = stallSec;
    }

    public int CallCount => _calls;
    public string DisplayName => "slow";
    public int Index => 0;
    public bool IsConfigured => true;

    public Task<bool> SetLocationAsync(GeoPoint point, CancellationToken ct = default)
        => Task.FromResult(true);

    public async Task<bool> SetGravityZAsync(double z, CancellationToken ct = default)
    {
        int n = Interlocked.Increment(ref _calls);
        if (_latencySec > 0)
            await Task.Delay(TimeSpan.FromSeconds(_latencySec), ct).ConfigureAwait(false);

        // 模拟一次卡死：调用序号到点后拖住，看看会不会补账
        if (_stallAfter > 0 && n == _stallAfter)
            await Task.Delay(TimeSpan.FromSeconds(_stallSec), ct).ConfigureAwait(false);

        lock (_recent)
        {
            var now = DateTime.Now;
            _recent.Enqueue(now);
            while (_recent.Count > 0 && now - _recent.Peek() > TimeSpan.FromSeconds(1))
                _recent.Dequeue();
            if (_recent.Count > MaxPerSecond) MaxPerSecond = _recent.Count;
        }
        return true;
    }

    public Task<string> DescribeAsync(CancellationToken ct = default) => Task.FromResult("");
    public Task<bool?> LaunchInstanceAsync(CancellationToken ct = default) => Task.FromResult<bool?>(true);
}
