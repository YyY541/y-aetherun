using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Aetherun.Desktop.Controls;

/// <summary>
/// 奶蛙的实时建模渲染（纯 CPU 软件光栅，带深度缓冲，无 GPU 依赖）。
///
/// 为什么不用图集：图集是 256×256 的位图，放大到预览区三百多像素就糊；
/// 这里逐像素光栅化三角形，任意尺寸都是锐的。
///
/// 为什么不用「按颜色分批的矢量填充」：那是画家算法 + 按颜色合并批次，
/// 同一批里混着远近不同的三角形，后绘制的那一批会把本该被挡住的部分盖到前面，
/// 看起来就是**半透明、能看穿**。这里改成真正的 z-buffer：
/// 每个像素按深度比一次，近的赢，实心不透。
///
/// 网格来自离线导出的 NWM1 二进制（导出脚本见 tools/mesh/export_mesh.py）：
///   · 顶点已归一化：脚在 y=0、水平居中、身高 = 1.0
///   · 颜色是从原贴图逐顶点采样的
///   · 模型是站立的（导出时应用了原 GLB 节点上那个绕 X +90° 的四元数）
///
/// 朝向约定：脸朝 +Z，相机在 −Z 侧看向 +Z。
/// 所以 yaw = 0 是正面、180 是背面；绕 Y 转 θ 后脸的朝向是 (sinθ, 0, cosθ)。
/// </summary>
public static class MeshSprite
{
    public sealed class Mesh
    {
        public float[] Verts = Array.Empty<float>();   // x,y,z 连排
        public float[] Cols = Array.Empty<float>();    // r,g,b 连排
        public int[] Tris = Array.Empty<int>();        // 三个顶点索引连排
        public int Count => Verts.Length / 3;
        public int TriCount => Tris.Length / 3;

        /// <summary>绕 Y 轴转一圈时最大的水平半径（模型单位，身高 = 1）。</summary>
        public double Radius
        {
            get
            {
                double r = 0.1;
                for (int i = 0; i < Count; i++)
                {
                    double x = Verts[i * 3], z = Verts[i * 3 + 2];
                    double d = Math.Sqrt(x * x + z * z);
                    if (d > r) r = d;
                }
                return r;
            }
        }

        private double _radiusCached;
        public double RadiusCached => _radiusCached > 0 ? _radiusCached : (_radiusCached = Radius);
    }

    /// <summary>画质档：按要画多高自动挑网格密度。</summary>
    public enum Tier
    {
        /// <summary>按像素高度自动选（预览用高模、跑道用低模）。</summary>
        Auto = 0,
        Low = 1,
        Mid = 2,
        High = 3,
    }

    private static Mesh? _hi, _mid, _lo;
    private static bool _hiTried, _midTried, _loTried;

    /// <summary>预览用的高模（50855 顶点 / 103790 三角，2.4 MB）。</summary>
    public static Mesh? Hi
    {
        get
        {
            if (!_hiTried) { _hiTried = true; Load("avares://Aetherun/Assets/frog/naiwa_hi.bin", ref _hi); }
            return _hi;
        }
    }

    /// <summary>中等密度（15918 顶点 / 32483 三角），拖动时用。</summary>
    public static Mesh? Mid
    {
        get
        {
            if (!_midTried) { _midTried = true; Load("avares://Aetherun/Assets/frog/naiwa_mid.bin", ref _mid); }
            return _mid;
        }
    }

    /// <summary>跑道上用的低模（3551 顶点 / 7200 三角）。</summary>
    public static Mesh? Lo
    {
        get
        {
            if (!_loTried) { _loTried = true; Load("avares://Aetherun/Assets/frog/naiwa_lo.bin", ref _lo); }
            return _lo;
        }
    }

    private static Mesh? Pick(Tier tier, int pxH) => PickResolved(ref tier, pxH);

    private static Mesh? PickResolved(ref Tier tier, int pxH)
    {
        switch (tier)
        {
            case Tier.High: return Hi;
            case Tier.Mid: return Mid;
            case Tier.Low: return Lo;
            default: break;
        }
        // 体积不是问题（用户明确说了「别怕文件体积过大」），所以门槛压低：
        // 只要画到 90 像素高就用中等密度，避免跑道上那只出现棱角
        if (pxH >= 300 && Hi is not null) { tier = Tier.High; return Hi; }
        if (pxH >= 90 && Mid is not null) { tier = Tier.Mid; return Mid; }
        tier = Mid is not null ? Tier.Mid : Tier.Low;
        return Mid ?? Lo ?? Hi;
    }

    private static void Load(string uri, ref Mesh? slot)
    {
        try
        {
            using var s = AssetLoader.Open(new Uri(uri));
            using var br = new BinaryReader(s);
            var magic = br.ReadBytes(4);
            if (magic.Length < 4 || magic[0] != (byte)'N' || magic[3] != (byte)'1') return;
            int nv = (int)br.ReadUInt32();
            int nt = (int)br.ReadUInt32();
            if (nv <= 0 || nt <= 0 || nv > 400000 || nt > 900000) return;

            var m = new Mesh
            {
                Verts = new float[nv * 3],
                Cols = new float[nv * 3],
                Tris = new int[nt * 3],
            };
            var vbuf = br.ReadBytes(nv * 12);
            var cbuf = br.ReadBytes(nv * 12);
            var tbuf = br.ReadBytes(nt * 12);
            if (vbuf.Length != nv * 12 || cbuf.Length != nv * 12 || tbuf.Length != nt * 12) return;
            Buffer.BlockCopy(vbuf, 0, m.Verts, 0, vbuf.Length);
            Buffer.BlockCopy(cbuf, 0, m.Cols, 0, cbuf.Length);
            Buffer.BlockCopy(tbuf, 0, m.Tris, 0, tbuf.Length);
            slot = m;
        }
        catch
        {
            // 资源缺失不是致命错误：界面退化成不画人物，别让整个窗口起不来
        }
    }

    public static int VertexCount(bool hi) => (hi ? Hi : Lo)?.Count ?? 0;
    public static int TriangleCount(bool hi) => (hi ? Hi : Lo)?.TriCount ?? 0;
    public static int VertexCount(Tier tier) => Pick(tier, tier == Tier.Auto ? 480 : 480)?.Count ?? 0;
    public static int TriangleCount(Tier tier) => Pick(tier, tier == Tier.Auto ? 480 : 480)?.TriCount ?? 0;

    // ================= 光栅化缓存 =================
    //
    // 转角与尺寸相同的图只烘一次。超采样 2 倍再缩小绘制，得到抗锯齿边缘；
    // 缓存按字节数封顶，拖动时会淘汰最久没用的那几张。

    private const double YawStep = 3.0;      // 120 个朝向
    private const double PitchStep = 6.0;    // 0..48 度共 9 档
    private const double SuperSample = 2.5;  // 2.5× 超采样，边缘更干净
    private const long CacheBytes = 192L * 1024 * 1024;

    private sealed class Raster
    {
        public WriteableBitmap? Img;
        public int W, H;
        public long Bytes;
    }

    private static readonly Dictionary<(int Yaw, int Pitch, int Tier, int PxH), Raster> Cache = new();
    private static readonly Queue<(int, int, int, int)> CacheOrder = new();
    private static long _cacheBytes;

    /// <summary>缓存的图数，自检用。</summary>
    public static int CachedBatches => Cache.Count;

    /// <summary>最近一次光栅化花的时间（毫秒），自检用。</summary>
    public static double LastBuildMs { get; private set; }

    /// <summary>清掉所有缓存（自检/换模时用）。</summary>
    public static void ClearCache()
    {
        foreach (var kv in Cache) kv.Value.Img?.Dispose();
        Cache.Clear();
        CacheOrder.Clear();
        _cacheBytes = 0;
    }

    private static Raster? Get(Mesh mesh, int tier, double yawDeg, double pitchDeg, int pxH)
    {
        int yawBucket = (int)Math.Round((((yawDeg % 360.0) + 360.0) % 360.0) / YawStep);
        yawBucket %= (int)(360.0 / YawStep);
        int pitchBucket = (int)Math.Round(Math.Clamp(pitchDeg, 0, 48) / PitchStep);
        var key = (yawBucket, pitchBucket, tier, pxH);

        if (Cache.TryGetValue(key, out var hit)) return hit;

        Raster? built;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try { built = Build(mesh, yawBucket * YawStep, pitchBucket * PitchStep, pxH); }
        catch { built = null; }
        finally { LastBuildMs = sw.Elapsed.TotalMilliseconds; }
        if (built is null) return null;

        while (_cacheBytes + built.Bytes > CacheBytes && CacheOrder.Count > 0)
        {
            var oldest = CacheOrder.Dequeue();
            if (Cache.Remove(oldest, out var dead))
            {
                _cacheBytes -= dead.Bytes;
                dead.Img?.Dispose();
            }
        }
        Cache[key] = built;
        CacheOrder.Enqueue(key);
        _cacheBytes += built.Bytes;
        return built;
    }

    /// <summary>
    /// 逐三角形光栅化一张图。深度缓冲保证近的盖住远的 —— 这就是「实心不透」的关键：
    /// 之前按颜色分批绘制，同批里远近混排，远的会画到近的上面，看上去就是半透明。
    /// </summary>
    private static Raster? Build(Mesh mesh, double yawDeg, double pitchDeg, int pxH)
    {
        double radius = mesh.RadiusCached;
        // 超采样后的画布：高 = pxH，宽按模型绕一圈的最大直径留一点余量
        int h = pxH;
        int w = Math.Max(4, (int)Math.Round(pxH * radius * 2.0 * 1.06));
        if ((long)w * h > 24_000_000) return null;   // 防御：异常尺寸不做

        var img = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Premul);

        var px = new int[w * h];
        var zb = new float[w * h];
        for (int i = 0; i < zb.Length; i++) zb[i] = float.PositiveInfinity;

        // 相机固定在 −Z 侧朝 +Z 看（与离线烘焙一致），而模型的脸朝 +Z，
        // 所以补 180° 让 yaw = 0 真的是正面。
        double yaw = (yawDeg + 180.0) * Math.PI / 180.0;
        double cyw = Math.Cos(yaw), syw = Math.Sin(yaw);
        double pitch = pitchDeg * Math.PI / 180.0;
        double cp = Math.Cos(pitch), sp = Math.Sin(pitch);

        int n = mesh.Count;
        var sx = new double[n];
        var sy = new double[n];
        var sz = new double[n];
        var vx = new double[n];
        var vy = new double[n];
        var vz = new double[n];

        double scale = h - 1;         // 模型身高 1.0 正好铺满画布高度
        double cx = w / 2.0;

        for (int i = 0; i < n; i++)
        {
            double x = mesh.Verts[i * 3];
            double y = mesh.Verts[i * 3 + 1];
            double z = mesh.Verts[i * 3 + 2];

            // 绕 Y 转：脸朝 +Z，转过 yaw 后法线变 (sinθ, 0, cosθ)
            double rx = x * cyw + z * syw;
            double rz = -x * syw + z * cyw;

            vx[i] = rx;
            vy[i] = y * cp + rz * sp;      // 世界「向上」分量
            vz[i] = rz * cp - y * sp;      // 越大越远

            sx[i] = cx + rx * scale;
            sy[i] = (1.0 - vy[i]) * scale;  // 屏幕 y 向下
            sz[i] = vz[i];
        }

        // 光从左上偏前来，跟界面整体的方向感一致
        double lx = -0.42, ly = 0.72, lz = -0.55;
        double ln = Math.Sqrt(lx * lx + ly * ly + lz * lz);
        lx /= ln; ly /= ln; lz /= ln;

        int nt = mesh.TriCount;
        for (int t = 0; t < nt; t++)
        {
            int a = mesh.Tris[t * 3], b = mesh.Tris[t * 3 + 1], c = mesh.Tris[t * 3 + 2];

            double ax = sx[a], ay = sy[a], az = sz[a];
            double bx = sx[b], by = sy[b], bz = sz[b];
            double cx2 = sx[c], cy2 = sy[c], cz2 = sz[c];

            // 面积为零的退化三角形直接跳过（顶点聚类会产生一些）
            double area = (bx - ax) * (cy2 - ay) - (by - ay) * (cx2 - ax);
            if (Math.Abs(area) < 1e-9) continue;

            // 面法线（视图空间），双面都受光：绕序不可靠，用绝对值
            double e1x = vx[b] - vx[a], e1y = vy[b] - vy[a], e1z = vz[b] - vz[a];
            double e2x = vx[c] - vx[a], e2y = vy[c] - vy[a], e2z = vz[c] - vz[a];
            double nx = e1y * e2z - e1z * e2y;
            double ny = e1z * e2x - e1x * e2z;
            double nz = e1x * e2y - e1y * e2x;
            double nn = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            double shade = 0.62;
            if (nn > 1e-12)
            {
                double dot = Math.Abs((nx * lx + ny * ly + nz * lz) / nn);
                shade = 0.52 + 0.52 * dot;
            }

            double r = (mesh.Cols[a * 3] + mesh.Cols[b * 3] + mesh.Cols[c * 3]) / 3.0 * shade;
            double g2 = (mesh.Cols[a * 3 + 1] + mesh.Cols[b * 3 + 1] + mesh.Cols[c * 3 + 1]) / 3.0 * shade;
            double b2 = (mesh.Cols[a * 3 + 2] + mesh.Cols[b * 3 + 2] + mesh.Cols[c * 3 + 2]) / 3.0 * shade;
            int cr = (int)Math.Clamp(Math.Round(r * 255.0), 0, 255);
            int cg = (int)Math.Clamp(Math.Round(g2 * 255.0), 0, 255);
            int cb = (int)Math.Clamp(Math.Round(b2 * 255.0), 0, 255);
            int packed = unchecked((int)(0xFF000000u | ((uint)cr << 16) | ((uint)cg << 8) | (uint)cb));

            int x0 = Math.Max(0, (int)Math.Floor(Math.Min(ax, Math.Min(bx, cx2))));
            int x1 = Math.Min(w - 1, (int)Math.Ceiling(Math.Max(ax, Math.Max(bx, cx2))));
            int y0 = Math.Max(0, (int)Math.Floor(Math.Min(ay, Math.Min(by, cy2))));
            int y1 = Math.Min(h - 1, (int)Math.Ceiling(Math.Max(ay, Math.Max(by, cy2))));
            if (x0 > x1 || y0 > y1) continue;

            double inv = 1.0 / area;
            for (int y = y0; y <= y1; y++)
            {
                double py = y + 0.5;
                int row = y * w;
                for (int x = x0; x <= x1; x++)
                {
                    double pxc = x + 0.5;
                    // 重心坐标（除以带符号面积，两种绕序都成立）
                    double w0 = ((bx - pxc) * (cy2 - py) - (by - py) * (cx2 - pxc)) * inv;
                    if (w0 < -1e-6) continue;
                    double w1 = ((cx2 - pxc) * (ay - py) - (cy2 - py) * (ax - pxc)) * inv;
                    if (w1 < -1e-6) continue;
                    double w2 = 1.0 - w0 - w1;
                    if (w2 < -1e-6) continue;

                    double z = w0 * az + w1 * bz + w2 * cz2;
                    int idx = row + x;
                    if (z >= zb[idx]) continue;      // 更远的像素不画 → 近的永远盖住远的
                    zb[idx] = (float)z;
                    px[idx] = packed;
                }
            }
        }

        // 封孔：三角形之间的亚像素缝隙会留下 1~2 像素的空点，放大后就是「透光」。
        // 把「四周有 ≥5 个不透明邻居」的空点用邻居颜色填上，多迭代几轮把缝隙吃掉。
        SealHoles(px, zb, w, h);

        try
        {
            using var fb = img.Lock();
            if (fb.RowBytes == w * 4)
            {
                Marshal.Copy(px, 0, fb.Address, px.Length);
            }
            else
            {
                for (int y = 0; y < h; y++)
                    Marshal.Copy(px, y * w, fb.Address + y * fb.RowBytes, w);
            }
        }
        catch
        {
            img.Dispose();
            return null;
        }

        return new Raster { Img = img, W = w, H = h, Bytes = (long)w * h * 4 + 64 };
    }

    /// <summary>
    /// 把模型画到 <paramref name="foot"/>（脚底中心），高 <paramref name="height"/> 像素。
    /// <paramref name="yawDeg"/> 0 = 正面朝镜头，180 = 背对。
    /// 网格密度按 <paramref name="tier"/> 选，<see cref="Tier.Auto"/> 时按像素高度自动挑。
    /// </summary>
    public static bool Draw(DrawingContext ctx, Point foot, double height, double yawDeg,
        double pitchDeg = 12.0, Tier tier = Tier.Auto, double superSample = SuperSample)
    {
        if (height < 4) return false;

        int pxH = Math.Clamp((int)Math.Round(height * Math.Clamp(superSample, 1.0, 3.0)), 8, 2400);
        var resolved = tier;
        var mesh = PickResolved(ref resolved, (int)Math.Round(height));
        if (mesh is null) return false;

        var ras = Get(mesh, (int)resolved, yawDeg, pitchDeg, pxH);
        if (ras?.Img is null) return false;

        double dispW = height * ras.W / (double)ras.H;

        // 接地阴影：模型边缘在浅底上会发虚，脚下压一层才像站在地上
        double shadeW = Math.Min(dispW * 0.66, height * 0.62);
        double shadeH = Math.Max(2.0, height * 0.055);
        ctx.DrawEllipse(new SolidColorBrush(Color.Parse("#05080C"), 0.34), null,
            new Point(foot.X, foot.Y - shadeH * 0.22), shadeW / 2, shadeH / 2);

        var src = new Rect(0, 0, ras.W, ras.H);
        var dest = new Rect(foot.X - dispW / 2, foot.Y - height, dispW, height);
        using (ctx.PushRenderOptions(new RenderOptions
               {
                   BitmapInterpolationMode = BitmapInterpolationMode.HighQuality,
               }))
        {
            ctx.DrawImage(ras.Img, src, dest);
        }
        return true;
    }

    /// <summary>兼容旧调用：hi = true 走高模。</summary>
    public static bool Draw(DrawingContext ctx, Point foot, double height, double yawDeg,
        double pitchDeg, bool hi)
        => Draw(ctx, foot, height, yawDeg, pitchDeg, hi ? Tier.High : Tier.Low, SuperSample);

    /// <summary>
    /// 把内部缝隙填实。<paramref name="px"/> 里 0 表示没被任何三角形覆盖；
    /// 只要一个空点周围 8 邻域里有 5 个以上已覆盖的像素，就用它们的平均色补上。
    /// 迭代 3 轮足够吃掉 1~2 像素宽的缝，轮廓边缘的空点因邻居不足不会被误填。
    /// </summary>
    private static void SealHoles(int[] px, float[] zb, int w, int h)
    {
        var filled = new int[w * h];
        for (int i = 0; i < px.Length; i++) filled[i] = px[i] == 0 ? 0 : 1;

        for (int pass = 0; pass < 3; pass++)
        {
            bool any = false;
            for (int y = 1; y < h - 1; y++)
            {
                int row = y * w;
                for (int x = 1; x < w - 1; x++)
                {
                    int i = row + x;
                    if (filled[i] != 0) continue;
                    int hits = 0, r = 0, g = 0, b = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int nrow = (y + dy) * w;
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int j = nrow + x + dx;
                            if (filled[j] == 0) continue;
                            hits++;
                            int c = px[j];
                            r += (c >> 16) & 0xFF;
                            g += (c >> 8) & 0xFF;
                            b += c & 0xFF;
                        }
                    }
                    if (hits < 5) continue;
                    int cr = r / hits, cg = g / hits, cb = b / hits;
                    // 深度取邻居里最小的，保证补出来的像素不会反过来盖住前景
                    float z = float.PositiveInfinity;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int nrow = (y + dy) * w;
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int j = nrow + x + dx;
                            if (filled[j] != 0 && zb[j] < z) z = zb[j];
                        }
                    }
                    px[i] = unchecked((int)(0xFF000000u | ((uint)cr << 16) | ((uint)cg << 8) | (uint)cb));
                    zb[i] = z;
                    filled[i] = 2;   // 本轮刚填的，下一轮才当邻居用；免得一圈圈往外长
                    any = true;
                }
            }
            if (!any) break;
            for (int i = 0; i < filled.Length; i++) if (filled[i] == 2) filled[i] = 1;
        }
    }

    /// <summary>自检用：把某个朝向的光栅结果直接编码成 PNG，便于逐像素核对有没有透。</summary>
    public static byte[]? DebugRasterPng(double yawDeg, double pitchDeg, int pxH, Tier tier = Tier.High)
    {
        var resolved = tier;
        var mesh = PickResolved(ref resolved, pxH);
        if (mesh is null) return null;
        var ras = Get(mesh, (int)resolved, yawDeg, pitchDeg, pxH);
        if (ras?.Img is null) return null;
        using var ms = new MemoryStream();
        ras.Img.Save(ms, new PngBitmapEncoderOptions());
        return ms.ToArray();
    }

    /// <summary>
    /// 跑道上用：左半边面向用户（yaw 0）、右半边背对（yaw 180），中间平滑过渡。
    /// x 是跑道中心为原点的横向米数，halfX 是场地半宽。
    /// </summary>
    public static double YawForTrackPosition(double x, double halfX)
    {
        double t = halfX <= 1e-6 ? 0 : Math.Clamp(x / halfX, -1.0, 1.0);
        return 90.0 * (1.0 + t);
    }

    /// <summary>预览用：把 0..1 的滑动条位置换成 0..360 的 yaw。</summary>
    public static double YawFromSlider(double t) => Math.Clamp(t, 0, 1) * 360.0;
}
