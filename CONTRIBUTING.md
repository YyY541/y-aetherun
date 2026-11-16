# 贡献指南

## 提之前先看一眼

- 有 bug 或想法，开 issue 说明**看到了什么、期望什么、怎么复现**。附上 `日志` 页的截图或 `%APPDATA%\Y-Aetherun\` 下的 `config.json` 会比一句「用不了」有用得多。
- 改动先开 issue 聊一下再动手，避免写完发现方向不对。
- 提 PR 前先在本地跑通 `dotnet build -c Debug` 与 `dotnet run --project tools/Verify -c Debug`，要求 `RESULT pass=N fail=0`。

## 代码风格

- .NET 8 / C# 12，`Nullable` 开着。新增的可空字段要能过编译警告。
- 界面用 Avalonia 12。**不要在 `Render` 里做重计算**：`MeshSprite` 有光栅缓存（按 yaw/pitch/密度/像素高做键，192 MB 上限），新画的东西也请走缓存。
- 注释写「为什么」，不写「是什么」。这个仓库里凡是看起来奇怪的写法，都在注释里留了原因（比如 `MeshSprite` 为什么不用背面剔除、`GeoAt` 的 `Frac`/`At` 单位坑、`Verify` 为什么要清 `SynchronizationContext`），改动时请把原因一并更新。

## 改动会碰到的自检

`tools/Verify` 里有一批断言是拿来防回退的，改到相关代码时它们会红，这是设计如此：

| 断言 | 防的是什么 |
| --- | --- |
| `raster uses per-pixel depth buffer` | 别把奶蛙渲染退回「按颜色分批 + 画家算法」，那会半透明 |
| `mesh tier {High,Mid,Low} is dense enough` | 别为了省体积把网格降到看不清 |
| `frog solid at {0,90,180,270}deg` | 逐像素数空隙，空洞率必须 ≤ 0.5% |
| `preset slow/fast/standard …` | 三档配速与步频的换算口径 |
| 步频泵回归（150 spm @ 20ms / 180ms、卡顿不爆量） | 别把拍发改回同步 `await` |
| 步频闭环回归（上推 / 回退 / 饱和 / 噪声样本） | 别把 `CadenceGovernor` 的比例校正改坏 |
| `overlay is a control, not a window` 等 | 开屏遮罩必须盖满整个应用 |

## 不要提交的东西

`.gitignore` 已经挡掉了 `bin/ obj/ publish/ dist/ shots/`，另外注意：

- 别把 `%APPDATA%\Y-Aetherun\` 里的 `config.json` / `track.txt` 拷进仓库，那里面有本机路径与坐标。
- 别把打包好的 `Y·Aetherun.exe` 或 zip 提交上来，走 Releases。
- 新增二进制资源（贴图、音频、网格）请控制在几 MB 内；单个超过 50 MB 的文件 GitHub 会拒绝。

## 许可

提交 PR 即表示同意以 [MIT](LICENSE) 授权你的改动。
