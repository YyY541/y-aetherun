# 构建与自检

## 需要什么

- **.NET 8 SDK**（8.0.4xx 或更新）。`dotnet --version` 能打出版本号就行。
- Windows 上跑界面要 **Windows 10/11 x64**；只跑自检（`tools/Verify`）在 Linux / macOS 上也能跑。
- 仓库自带全部依赖声明，`dotnet restore` 会自动从 NuGet 拉 Avalonia 12.1.6 等包。

## 目录结构

```
Aetherun.sln
Directory.Build.props          版本号、通用属性
src/Aetherun.Core/             纯逻辑：跑道几何、引擎、注入通道客户端
  Emulators/                   ldconsole / adb 的薄封装
  Models/                      RunConfig、RunSnapshot、TrackLoop
  Services/                    RunEngine、StepPump、StepFeedback、CadenceGovernor
src/Aetherun.Desktop/          Avalonia 界面
  Controls/                    TrackScene3D、MeshSprite（软光栅）、SplashPlayer
  ViewModels/                  MainViewModel
  Views/                       Dashboard / Track / Settings / Logs / About
  Assets/                      splash 帧序列、奶蛙网格、图标、开场音
tests/Aetherun.Core.Tests/     xUnit
tools/Verify/                  287 项自检（含奶蛙实心核对）
tools/Shot/                    离屏截图
tools/mesh/export_mesh.py      glb → NWM1 网格导出脚本
docs/screenshots/              README 里引用的界面图
```

## 编译

```bash
dotnet build -c Debug
```

只想编译界面工程：

```bash
dotnet build src/Aetherun.Desktop/Aetherun.Desktop.csproj -c Release
```

## 跑起来

```bash
dotnet run --project src/Aetherun.Desktop
```

首次启动会在 `%APPDATA%\Y-Aetherun\` 生成 `config.json` 与 `track.txt`（Linux/macOS 上是 `~/.config/Y-Aetherun/`）。

## 自检

```bash
dotnet run --project tools/Verify -c Debug
```

最后一行是 `RESULT pass=N fail=0`。它覆盖跑道几何、配置范围与迁移、引擎状态机、步频泵的延迟/并发回归、步频闭环校正、日志解析，以及**奶蛙渲染**：

- 源码里必须存在 `zb[`（确认没退回按颜色分批的画家算法）
- 三档网格的顶点/三角数下限
- 把 0°/90°/180°/270° 四个朝向各光栅化一次，逐像素数「被不透明像素围住的空洞」，空洞率必须 ≤ 0.5%

`tools/Verify` 会初始化 Avalonia（加载资源与 `WriteableBitmap` 需要），初始化后立刻把
`SynchronizationContext` 置空 —— 否则 `SetupWithoutStarting` 装上的 UI 同步上下文会让后面
引擎段的 `async/await` 永远排不上，整个自检卡死在半路。

## 出截图

```bash
dotnet run --project tools/Shot -c Debug -- ./shots
```

会写 `01-dashboard.png` … `08-splash-overlay.png`、`frog-{0,90,180,270}.png`，以及
`raster-{0,90,180,270}.png`（奶蛙的裸光栅，带 alpha，不含背景与阴影，用来核对有没有空隙）。
不传输出目录时写到可执行文件旁边的 `shots/`。

## 打包单文件 exe

```bash
dotnet publish src/Aetherun.Desktop/Aetherun.Desktop.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -o publish/win-x64
```

产物 `publish/win-x64/Aetherun.exe` 约 64 MB（自带 .NET 运行时，目标机不用装 SDK）。
根目录 `dotnet publish` 会因为 `tests/` 里的测试工程报 `NETSDK1098`（测试工程不是应用宿主），
这是预期行为，指定 `src/Aetherun.Desktop` 单个工程就干净了。

## 重新生成奶蛙网格

网格是从 `naiwa.glb` 离线烘的，仓库里已经带了烘好的三个 `.bin`，一般不用重跑。
要改密度或换了模型时：

```bash
pip install numpy Pillow
python3 tools/mesh/export_mesh.py /path/to/naiwa.glb \
  --out-dir src/Aetherun.Desktop/Assets/frog
```

不传 `--cell` 会一次产出三档：`naiwa_hi.bin`（cell=120，50855 顶点）、
`naiwa_mid.bin`（64，15918 顶点）、`naiwa_lo.bin`（30，3551 顶点）。
`--cell N --name x.bin` 可以只烘一档。

脚本做的事：解析 glb → **把节点上的四元数旋转烘进顶点**（这个模型是 Z-up，靠绕 X +90° 才站起来，
漏了这步导出的网格是躺着的）→ 按格子聚类合并顶点 → 按格子求平均颜色（颜色从贴图按 UV 采样）→
去重三角 → 归一化（脚贴 y=0、水平居中、身高 1.0）→ 写 `NWM1`。

`NWM1` 格式很简单，规范写在脚本头部注释里。
