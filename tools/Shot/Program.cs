using System;
using System.IO;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Avalonia.Media.Imaging;
using Aetherun.Desktop;
using Aetherun.Desktop.Services;
using Aetherun.Desktop.ViewModels;
using Aetherun.Desktop.Views;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var outDir = args.FirstOrDefault(a => !a.StartsWith("-"))
                     ?? Path.Combine(AppContext.BaseDirectory, "shots");
        Directory.CreateDirectory(outDir);

        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont()
            .SetupWithoutStarting();

        var shell = new AppShell();
        var vm = new MainViewModel(shell);
        var win = new MainWindow { DataContext = vm, Width = 1180, Height = 760 };
        win.Show();

        // 主窗一显示就自带开屏遮罩（真实启动就是这个效果）。
        // 先跑一次 layout 让可视树建好，再把它摘掉，
        // 否则后面每一页都被这块遮罩盖着（拍出来全是开屏帧）。
        Relayout(win);
        var rootHost = win.GetVisualDescendants().OfType<Avalonia.Controls.Grid>()
            .FirstOrDefault(g => g.Name == "RootHost");
        if (rootHost is not null)
            foreach (var ov in rootHost.Children.OfType<Aetherun.Desktop.Views.SplashOverlay>().ToList())
                rootHost.Children.Remove(ov);
        Console.WriteLine("   [Boot] RootHost=" + (rootHost is null ? "NOT FOUND" : rootHost.Children.Count + " children"));

        Relayout(win);
        Pump();
        // 开屏遮罩默认盖在最上层（真实启动就是这样），截图各页前先收起来
        var splashLayer = win.GetVisualDescendants()
            .OfType<Aetherun.Desktop.Views.SplashOverlay>().FirstOrDefault();
        if (splashLayer is not null)
        {
            Console.WriteLine($"   [Overlay] initial bounds={splashLayer.Bounds} visible={splashLayer.IsVisible}");
            splashLayer.IsVisible = false;
            Relayout(win);
        }

        Capture(win, Path.Combine(outDir, "01-dashboard.png"));

        // running state preview
        var snap = new Aetherun.Core.Models.RunSnapshot
        {
            Phase = Aetherun.Core.Models.RunPhase.Running,
            Message = "跑步中",
            DistanceMeters = 1284.5,
            TargetMeters = 2200,
            Steps = 968,
            ElapsedSeconds = 527,
            FixCount = 527
        };
        vm.ShowPreview(snap, "跑步中");
        Pump();
        Capture(win, Path.Combine(outDir, "02-running.png"));

        // 倒计时（Armed）状态：验证那条横幅真的出来了
        var armed = new Aetherun.Core.Models.RunSnapshot
        {
            Phase = Aetherun.Core.Models.RunPhase.Armed,
            Message = "等待开始 14 秒",
            TargetMeters = 2000,
            CountdownRemaining = 14,
            CountdownTotal = 20,
            ForegroundOk = false,
            ForegroundPackage = "com.android.launcher3",
            CountdownHeld = true,
        };
        vm.ShowPreview(armed, "等待开始 14 秒");
        Relayout(win);
        Capture(win, Path.Combine(outDir, "09-countdown.png"));
        var banner = win.GetVisualDescendants().OfType<Avalonia.Controls.Border>()
            .FirstOrDefault(b => b.IsVisible && b.Bounds.Height > 60 && b.Bounds.Height < 130
                                 && b.Classes.Contains("card"));
        Console.WriteLine($"   [Armed] banner={(banner is null ? "NOT FOUND" : banner.Bounds.ToString())}");

        // 漏检警告（跑完但 App 从没到前台）
        var missed = new Aetherun.Core.Models.RunSnapshot
        {
            Phase = Aetherun.Core.Models.RunPhase.Done,
            Message = "已达标",
            DistanceMeters = 2000,
            TargetMeters = 2000,
            Steps = 2100,
            ElapsedSeconds = 900,
            FixCount = 900,
            ForegroundMissed = true,
        };
        vm.ShowPreview(missed, "已完成");
        Relayout(win);
        Capture(win, Path.Combine(outDir, "10-missed.png"));
        var warn = win.GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>()
            .FirstOrDefault(t => t.Text == "这次可能不计成绩");
        Console.WriteLine($"   [Missed] warn={(warn is null ? "NOT FOUND" : "visible")}");

        vm.ShowPreview(new Aetherun.Core.Models.RunSnapshot(), "待机");

        vm.NavigateTo(NavKey.Track);
        Relayout(win);
        Dump(win, "Track");
        Capture(win, Path.Combine(outDir, "03-track.png"));

        vm.NavigateTo(NavKey.Splash);
        Relayout(win);
        Dump(win, "Splash");
        // 头几帧是全黑的，跳到中段才有画面
        foreach (var spl in win.GetVisualDescendants().OfType<Aetherun.Desktop.Controls.SplashPlayer>())
            if (spl.IsVisible) { spl.Seek(150); spl.Pause(); }
        Pump();
        Capture(win, Path.Combine(outDir, "06-splash.png"));

        vm.NavigateTo(NavKey.Settings);
        Relayout(win);
        Dump(win, "Settings");
        Capture(win, Path.Combine(outDir, "04-settings.png"));

        vm.NavigateTo(NavKey.Logs);
        Relayout(win);
        Dump(win, "Logs");
        Capture(win, Path.Combine(outDir, "05-logs.png"));

        vm.NavigateTo(NavKey.About);
        Relayout(win);
        Dump(win, "About");
        Capture(win, Path.Combine(outDir, "07-about.png"));

        // 滑轮自检：把三个 ValueDial 的实际位置与值打出来，方便核对渲染
        vm.NavigateTo(NavKey.Dashboard);
        Relayout(win);
        foreach (var c in win.GetVisualDescendants())
        {
            if (c is Aetherun.Desktop.Controls.ValueDial d)
                Console.WriteLine($"   [Dial] bounds={d.Bounds} value={d.Value:F2} min={d.Minimum} max={d.Maximum} step={d.Step}");
        }

        // 3D 场景自检：同一控件在推进时间/里程后必须画出不同的一帧
        vm.NavigateTo(NavKey.Track);
        Relayout(win);
        // 仪表盘上还有一个 Compact 的小场景，页面切到跑道页后它只是隐藏、
        // 仍在可视化树里。取错那个就会拍到「不动的一帧」，所以按可见性挑。
        var scene = win.GetVisualDescendants()
            .OfType<Aetherun.Desktop.Controls.TrackScene3D>()
            .Where(x => x.IsEffectivelyVisible)
            .OrderByDescending(x => x.Bounds.Width * x.Bounds.Height)
            .FirstOrDefault();
        if (scene is null)
        {
            Console.WriteLine("   [Scene] NOT FOUND");
        }
        else
        {
            Console.WriteLine($"   [Scene] bounds={scene.Bounds} track={(scene.Track?.Count ?? 0)} pts"
                              + $" perimeter={(scene.Track?.Perimeter ?? 0):F1}");
            var hashes = new List<string>();
            foreach (var km in new[] { 0.0, 0.6, 1.2, 1.9 })
            {
                scene.DistanceMeters = km * 1000;
                for (int i = 0; i < 40; i++) scene.Advance(0.016);   // 让平滑与拖影跑起来
                var h = HashControl(scene, Path.Combine(outDir, $"scene-{km:F1}.png"));
                hashes.Add(h);
                var df = scene.DebugFrog;
                Console.WriteLine($"   [Scene] {km:F1}km dd={scene.DebugDisplayDistance:F1} md5={h}"
                    + (df is null ? " frog=NONE"
                       : $" frog=({df.Value.X:F1},{df.Value.Y:F1}) yaw={df.Value.Yaw:F0} h={df.Value.Height:F0}")
                    + $" | {scene.DebugFrogDetail}");
            }
            Console.WriteLine("   [Scene] distinct=" + hashes.Distinct().Count() + "/" + hashes.Count);
        }

        // 奶蛙预览自检：转四个角度必须画出四张不同的图
        vm.NavigateTo(NavKey.Track);
        Relayout(win);
        var frog = win.GetVisualDescendants().OfType<Aetherun.Desktop.Controls.FrogPreview>().FirstOrDefault();
        if (frog is null)
        {
            Console.WriteLine("   [Frog] NOT FOUND");
        }
        else
        {
            Console.WriteLine($"   [Frog] bounds={frog.Bounds} pitch={frog.Pitch:F0}");
            var fh = new List<string>();
            foreach (var deg in new double[] { 0, 90, 180, 270 })
            {
                frog.Yaw = deg;
                Pump();
                var h = HashControl(frog, Path.Combine(outDir, $"frog-{deg:F0}.png"));
                fh.Add(h);
                Console.WriteLine($"   [Frog] yaw={deg:F0} md5={h}");
            }
            Console.WriteLine("   [Frog] distinct=" + fh.Distinct().Count() + "/" + fh.Count);
        }

        // 各页 3D 场景的实际尺寸
        foreach (var page in new[] { NavKey.Dashboard, NavKey.Track })
        {
            vm.NavigateTo(page);
            Relayout(win);
            foreach (var sc in win.GetVisualDescendants().OfType<Aetherun.Desktop.Controls.TrackScene3D>())
            {
                var tl = sc.TranslatePoint(new Point(0, 0), win);
                Console.WriteLine($"   [SceneSize] {page} compact={sc.Compact} vis={sc.IsVisible} at={tl} size={sc.Bounds.Width}x{sc.Bounds.Height}");
            }
        }

        // 开屏遮罩：现在是主窗最上层的一层，单独出一张验证帧解码
        var host = rootHost;
        if (host is not null)
        {
            foreach (var ov in host.Children.OfType<Aetherun.Desktop.Views.SplashOverlay>().ToList())
                host.Children.Remove(ov);
            var ovl = new Aetherun.Desktop.Views.SplashOverlay();
            host.Children.Add(ovl);
            Relayout(win);
            for (int i = 0; i < 12; i++)
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Thread.Sleep(25);
            }
            var sp = ovl.GetVisualDescendants().OfType<Aetherun.Desktop.Controls.SplashPlayer>().FirstOrDefault();
            if (sp is not null) { sp.Seek(150); sp.Pause(); }
            Relayout(win);
            for (int i = 0; i < 6; i++)
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Thread.Sleep(25);
            }
            Console.WriteLine($"   [Overlay] frame={sp?.FrameIndex} progress={sp?.Progress:F2} bounds={ovl.Bounds}");
            Capture(win, Path.Combine(outDir, "08-splash-overlay.png"));
        }

        // 奶蛙建模：确认 NWM1 资源在 headless 下能加载，并按 0/90/180/270 出四张
        Console.WriteLine($"   [Mesh] hi={Aetherun.Desktop.Controls.MeshSprite.VertexCount(true)}v"
                          + $"/{Aetherun.Desktop.Controls.MeshSprite.TriangleCount(true)}t"
                          + $" lo={Aetherun.Desktop.Controls.MeshSprite.VertexCount(false)}v"
                          + $"/{Aetherun.Desktop.Controls.MeshSprite.TriangleCount(false)}t");
        foreach (var fp in win.GetVisualDescendants().OfType<Aetherun.Desktop.Controls.FrogPreview>())
            Console.WriteLine($"   [FrogPreview] bounds={fp.Bounds} vis={fp.IsVisible} yaw={fp.Yaw:F0} pitch={fp.Pitch:F0}");

        // 单独把预览控件按四个朝向出图，核对「0 正面 / 90 侧 / 180 背面 / 270 另一侧」
        {
            var solo = new Avalonia.Controls.Border
            {
                Width = 336,
                Height = 440,
                Background = Avalonia.Media.Brushes.Black,
                Child = new Aetherun.Desktop.Controls.FrogPreview { Yaw = 0, Pitch = 10 },
            };
            var w2 = new Window { Width = 336, Height = 440, Content = solo };
            w2.Show();
            Relayout(w2);
            var pv = (Aetherun.Desktop.Controls.FrogPreview)solo.Child;
            foreach (var deg in new[] { 0, 90, 180, 270 })
            {
                pv.Yaw = deg;
                Relayout(w2);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Thread.Sleep(30);
                Capture(w2, Path.Combine(outDir, $"frog-{deg}.png"));
            }
            Console.WriteLine($"   [Mesh] batches={Aetherun.Desktop.Controls.MeshSprite.CachedBatches}");
            // 直接把光栅结果落盘：这张图不含背景、不含阴影，纯净地看建模本身有没有洞
            foreach (var deg in new[] { 0, 90, 180, 270 })
            {
                var png = Aetherun.Desktop.Controls.MeshSprite.DebugRasterPng(deg, 10, 524);
                if (png is null) { Console.WriteLine($"   [Raster] yaw={deg} null"); continue; }
                var rp = Path.Combine(outDir, $"raster-{deg}.png");
                File.WriteAllBytes(rp, png);
                Console.WriteLine($"   [Raster] yaw={deg} {png.Length}B -> {rp}");
            }
            w2.Close();
        }

        Console.WriteLine("captured 8 frames");
    }

    private static void Dump(Window win, string label)
    {
        foreach (var c in win.GetVisualDescendants())
        {
            if (c is Control ctl && ctl.GetType().Name.EndsWith("View"))
                Console.WriteLine("   [" + label + "] " + ctl.GetType().Name + " IsVisible=" + ctl.IsVisible
                    + " bounds=" + ctl.Bounds);
        }
    }

    private static void Pump()
    {
        for (int i = 0; i < 12; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Thread.Sleep(16);
        }
    }

    private static void Relayout(Window win)
    {
        // IsVisible 变化后必须强制一次 measure/arrange，否则新页 bounds 仍是 0
        win.InvalidateMeasure();
        win.InvalidateArrange();
        win.Measure(new Size(win.Width, win.Height));
        win.Arrange(new Rect(0, 0, win.Width, win.Height));
        win.UpdateLayout();
        Pump();
    }

    private static string HashControl(Control ctl, string path)
    {
        try
        {
            int w = (int)Math.Max(ctl.Bounds.Width, 1);
            int h = (int)Math.Max(ctl.Bounds.Height, 1);
            using var rtb = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96));
            rtb.Render(ctl);
            rtb.Save(path);
            var bytes = File.ReadAllBytes(path);
            return Convert.ToHexString(System.Security.Cryptography.MD5.HashData(bytes)).ToLowerInvariant()[..12];
        }
        catch (Exception ex)
        {
            return "ERR:" + ex.Message;
        }
    }

    private static void Capture(Window win, string path)
    {
        try
        {
            int w = (int)Math.Max(win.Width, win.Bounds.Width);
            int h = (int)Math.Max(win.Height, win.Bounds.Height);
            using var rtb = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96));
            rtb.Render(win);
            rtb.Save(path);
            Console.WriteLine("  " + path);
        }
        catch (Exception ex)
        {
            Console.WriteLine("  FAIL " + path + " :: " + ex.Message);
        }
    }
}
