# AWall：供 AI 阅读的技术上下文

本文依据 2026-11-16 的项目源码整理。用途是帮助 AI 快速理解、修改和排查本项目；具体实现发生变化时，以源码为准并同步更新本文。逆向证据、迁移决策及实际验证结果见 [LIVELY-MIGRATION-PLAN.md](LIVELY-MIGRATION-PLAN.md)。

## 1. 项目定位与技术栈

AWall 是 Windows 动态桌面壁纸与静态锁屏快照原型，当前 README 标记版本为 0.2。

- 宿主：C#、.NET Framework 4.x、WPF，x64；没有 SDK 风格 csproj，也没有 npm 应用构建流程。
- 桌面集成：Win32 P/Invoke，将 WPF 窗口挂到 Explorer 的 WorkerW 桌面宿主。
- 播放：MP4 使用每屏独立 mpv 子进程，PKG 使用 Microsoft WebView2 WPF 控件。
- 场景引擎：本地固定版本 WebWallGL 3.0.2，兼容 Wallpaper Engine 场景格式。
- MP4：mpv JSON named-pipe IPC，静音、循环、cover 填充，`awall-ready.lua` 检测 playback-restart 首帧。
- 锁屏：当前后端截图 + PowerShell 调用 WinRT 设置图片。
- 无需运行 Wallpaper Engine；部分 PKG 依赖它安装目录里的共享纹理。素材只从本机读取。

## 2. 先读哪些文件

| 文件 | 职责 |
| --- | --- |
| `README.md` | 启动、功能边界、构建和自检入口 |
| `src/AWall.cs` | Item、Native、Player、MainWindow；UI、扫描、窗口切换、桌面挂载、暂停、测试 |
| `src/DesktopHost.cs` | Progman/WorkerW发现、健康检查、坐标转换、checked挂载与排序 |
| `src/MpvPlayer.cs` | MP4子进程、首帧、IPC、暂停、截图与退出回收 |
| `src/ScenePlayer.cs` | WebView2 生命周期、资源映射、消息桥接、诊断、截图 |
| `src/scene/index.html` | WebWallGL 初始化、场景暂停、鼠标位置、统计 |
| `src/scene/video.html` | 旧视频页面资源，当前MP4播放不走此入口 |
| `src/Set-LockImage.ps1` | WinRT 锁屏设置及结果退出码 |
| `build.ps1` | 系统 csc 编译及运行文件复制 |
| `COMPATIBILITY.md`、`mp4-compatibility-results.json` | 历史测试结果及其范围 |
| `THIRD-PARTY-NOTICES.md`、`vendor/` | 固定第三方依赖与许可证 |

`bin/` 是构建产物。修改 `src/scene` 后必须重新构建，运行时页面来自 `bin/scene`。优先修改自有宿主和入口；涉及第三方引擎修改时，先确认版本、许可和更新方式，不要把压缩 bundle 当作易维护的业务源码。

## 3. GPU 与 CPU 的分工

**设计上以 GPU 图形渲染为主，CPU 执行业务、脚本与资源处理。源码不能证明某次运行实际使用了哪块 GPU 或硬件解码器。**

| 路径 | 图形/视频工作 | CPU 工作与限制 |
| --- | --- | --- |
| PKG/松散场景 | WebView2 内 WebWallGL/WebGL，纹理、着色器、绘制与合成通常由 GPU 执行 | 文件读取、解析、JavaScript/SceneScript、部分动画与粒子逻辑等；具体分工取决于引擎实现 |
| MP4 | mpv视频输出与合成，`--hwdec=auto-safe`首选硬解 | 编码、驱动或运行环境不支持时可能软件解码，不能仅凭首帧证明硬解 |
| WPF/桌面窗口 | WPF 与系统桌面合成参与展示 | UI、定时器、Win32 调用、窗口管理 |

`ScenePlayer.Initialize()` 没有传入 `--disable-gpu`，也没有强制选择独显或集显。现有 `Stats()` 不提供实际 GPU 适配器、软件渲染回退或视频解码器类型。不要把“使用 WebGL”“存在 GPU 子进程”或“首帧成功”表述为硬件加速已被实测确认。

PKG 配置为 `fps:30`、`renderDpr:1`、`fit:'cover'`、`volume:0`、`scripts:true`、`audio:null`、`media:null`。30 FPS 是目标配置，不保证实际达到；MP4 不受此配置限制。每块显示器创建独立 Player 和相应后端，不共享已渲染画面，因此多屏会增加解码、绘制和内存成本。

## 4. 启动与素材模型

`MainWindow.Main()` 设置进程 DPI aware，使用 `Local\AWall.Desktop` 命名互斥体避免同一会话重复运行。普通关闭隐藏到托盘；退出需走 `Exit()`，释放播放器、托盘及系统事件订阅。

`Item` 字段为 `Title`、`Type`、`File`、`Preview`。`Supported` 仅检查 Type 为 `video` 或 `scene`，不是对编码或场景完整性的预验证。

- 默认递归扫描 `E:\Steam\steamapps\workshop\content\431960` 中的 `project.json`。
- 读取 title/type/file/preview，场景入口不存在而有 `scene.pkg` 时回退到该包。
- 扫描时校验素材入口规范化路径处于 project 所在目录内。
- 导入支持本地 `.mp4`、`.pkg`；文件夹选择和导入列表目前没有持久化保存逻辑。
- `web`、`application` 壁纸没有完整播放支持。

成功淡出后将当前 Item、手动暂停和全屏暂停设置写入 `%LOCALAPPDATA%\AWall\wallpaper-layout.json`。正常启动恢复最后成功布局，恢复失败只记录并允许用户重试；显式 Stop 清除布局，Exit 保留。测试模式使用 `wallpaper-layout-test.json`，不自动恢复或覆盖正式布局。素材目录与完整导入列表仍不持久化，成功布局只保存当前素材。

## 5. 桌面挂载与切换状态机

`Native.DesktopParent()` 委托 `DesktopHost.Parent()`。DesktopHost 按 Lively 的机制发现 Progman、DefView 和 WorkerW，发送 `0x052C(0xD,1)` 并区分 legacy/raised 桌面。AWall 使用共享 WorkerW 子宿主来维持 WPF、WebView2 和独立遮罩的Z顺序；这与本机Lively二进制raised分支直接挂Progman有适配差异，详见迁移计划。挂载核验真实父窗口，使用 MapWindowPoints 转换坐标，并检查定位结果。

`Player` 为无边框、不激活、不显示任务栏的 WPF 窗口。先在屏外创建可渲染的 staging HWND，初始化后端，真实首帧就绪后才挂载和定位到桌面。窗口就绪、内容就绪和挂载成功是不同阶段；挂载错误使事务回滚。

`MainWindow.Apply()` 的关键顺序：

1. 取消尚未完成的切换，保留当前有效壁纸。
2. 当前播放器转入 `previousPlayers`；并行截图，每屏先创建快照遮罩，首次启动使用系统静态背景，再暂停旧实例。
3. 为所有显示器创建屏外新 `players` 批次，等待真实首帧后挂载；全屏批次未就绪前保持遮罩。
4. 全部 Ready 后 Present 新窗口，再提升遮罩，350ms淡出后销毁遮罩及旧实例，并保存成功布局。
5. 任一失败或 60 秒切换超时，销毁新批次并恢复旧批次。

请求版本与批次引用比较用于拒绝过期回调；窗口替换/销毁通过 Dispatcher 延后，避免在 WebView2 消息回调中直接处理。WM_WINDOWPOSCHANGING 钩子维持staging窗口位置与排序。Stop在截图和初始化完成前使请求失效；已提交后端故障清理失效批次并报告可重试状态。修改切换流程时应保留这些时序约束。

WebView2 profile 按显示器分目录、按空闲 `slot-N` 分配。同一显示器的新旧活跃播放器不复用同一 profile，避免旧播放器暂停影响新播放器启动。Dispose 后释放 slot 占用。

## 6. WebView2 资源与消息协议

两个虚拟 HTTPS 主机：

- `https://awall.local/` → 程序目录 `scene/`，宿主自有页面和引擎脚本。
- `https://wallpaper.awall.local/` → 当前素材入口所在目录。

`/api/local-assets` 提供共享资源发现；`/api/local-assets/we/materials/index.json` 枚举 TEX 名称；`/api/local-assets/we/...` 读取共享素材。共享根路径目前硬编码在 `ScenePlayer.Assets`：`E:\Steam\steamapps\common\wallpaper_engine\assets`。不存在时部分场景可能缺纹理或产生兼容性警告；不能据此声称项目完全不依赖 Wallpaper Engine 安装文件。

网页 → C# 消息为 `{type, value}`：

| type | 行为 |
| --- | --- |
| `boot` | C# 回传 `{type:'load', video, pkg}`，文件名按素材类型设置 |
| `ready` | 设置 Ready、记录 ReadyInfo、应用待处理暂停状态并通知 UI |
| `error` | 设置 Failed，清除 Ready，记录 LastError |
| `diagnostic` | 记录 level/message；warn 按完整消息去重计数 |

C# → 页面还通过 ExecuteScriptAsync 调用 `awallPause(bool)`、`awallPointer(u,v)`、`awallStats()`。不要随意改变这些全局函数名或 JSON 字段。

场景页面通过 WebWallGL.httpSource 读取素材；PKG 场景重写 source 的包读取和 project 入口。mount 超时为45秒，完成后再等待两次requestAnimationFrame报告ready。MP4由MpvPlayer加载，启动与首帧总上限30秒，等待Lua的playback-restart标记。退出优先通过IPC发送quit，有限等待后回收本程序拥有的进程；统一等待者负责WaitForExit及释放Process，避免Dispose与监测竞争。

## 7. 暂停、输入与锁屏

- 2 秒 Tick 检测前台窗口是否覆盖任意显示器；默认启用全屏暂停，策略作用于全部播放器。
- 实际暂停条件为 `manualPause || locked || covered`。
- SessionSwitch处理锁定/解锁；显示器设备名、Bounds、主屏身份构成拓扑，变化后强制重建当前素材。
- TaskbarCreated通知使宿主失效并触发恢复；Tick保留周期健康检查，失效HWND需要重建，存活窗口可重新挂载。
- 鼠标定时器为 50 ms，只有光标所在显示器推送归一化位置；ScenePlayer 对视频、未就绪或暂停场景跳过输入。
- 未接入系统音频频谱、Now Playing、鼠标点击；不要用模拟音频数据宣称已支持系统声音响应。
- Snapshot按内容后端使用mpv screenshot-to-file或WebView2 CapturePreviewAsync。锁屏选主显示器播放器，调用PowerShell设置静态图片，不在Win+L界面播放动画。
- 锁屏脚本先尝试 TrySetLockScreenImageAsync；返回 false 时尝试 LockScreen.SetImageFileAsync。异常退出为 1，详情写入 lock-result.txt；不修改系统策略。

## 8. 安全边界与已知限制

宿主关闭开发者工具、默认菜单、缩放及本机 host objects；拒绝权限、下载和新窗口；导航仅允许 awall.local；资源请求允许两个本地主机及 data/blob/about，其余返回 403。共享资源接口有规范化路径边界检查。

这些限制不是对不可信素材的完整安全保证：场景脚本已启用，素材目录通过虚拟主机暴露。仅播放信任的本地素材。修改资源路由时避免扩大外网或本机目录访问范围。

WebWallGL 是兼容引擎，不能保证官方效果、模型、SceneScript 完整一致。首帧/非纯色截图/画面变化均不能证明视觉还原正确。尚无跨硬件性能基准，也没有当前 GPU/硬解状态的专门检测。程序不会自动退出 Wallpaper Engine、设置开机启动或上传素材。

## 9. 构建与验证

在项目根目录使用 Windows PowerShell：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

脚本使用 `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`，编译AWall.cs、DesktopHost.cs、ScenePlayer.cs和MpvPlayer.cs，引用WPF、WinForms、WebView2等程序集，再复制页面、脚本、DLL、mpv运行文件和许可证到bin。本机依赖在vendor中，构建无需联网；PKG运行需要WebView2 Runtime，公开源码检出还需按THIRD-PARTY-NOTICES补齐本机mpv运行文件。

运行测试会创建真实桌面壁纸并在结束时退出，且受单实例互斥体限制。运行前确认已有 AWall 实例已退出。

```powershell
.\bin\AWall.exe --self-test
.\bin\AWall.exe --self-test --test-file "C:\path\sample.mp4"
.\bin\AWall.exe --test-library
.\bin\AWall.exe --test-library --only=工坊目录ID1,工坊目录ID2
.\bin\AWall.exe --test-transitions
```

`--test-file` 本身不会开启测试模式，必须与 `--self-test` 或 `--test-library` 配合。self-test 约 8 秒后截图和记录状态。全库测试逐项等待就绪、检查截图像素范围、比较两个时刻 JPEG 哈希、检查暂停；视频另检查暂停时间停止和结尾循环。`frameChanged` 只是记录字段，当前不是失败断言。测试不验证所有显示器的视觉一致性，也不检测实际硬件加速。

针对改动选择验证：C# 改动先构建；播放改动验证首帧、暂停/恢复、视频循环；切换改动验证快速连续切换、失败回退、黑白闪屏和多屏；桌面挂载改动检查 Explorer 重启/显示器变化；锁屏操作会更改用户当前锁屏，需按实际任务范围决定是否执行。文档改动无需启动壁纸。

## 10. 日志与数据位置

运行数据根目录：`%LOCALAPPDATA%\AWall`。

| 路径 | 内容 |
| --- | --- |
| `AWall.log` | UI 状态和应用事件 |
| `scene-<素材父目录名>.log`、`video-<素材父目录名>.log` | READY、ERROR、场景诊断；直接导入文件时后缀不是文件名 |
| `WebView2/<显示器名>/slot-N` | WebView2 用户数据/profile |
| `wallpaper-layout.json` | 正式模式最后成功布局与暂停设置；Stop清除、Exit保留 |
| `wallpaper-layout-test.json` | 测试隔离布局，不覆盖正式布局 |
| `transitions.log`、`desktop-host.log`、`mpv-lifecycle.log` | 切换阶段、宿主挂载与mpv进程生命周期诊断 |
| `transition-test.json` | 切换集成测试结果 |
| `lock-<GUID>.jpg`、`lock-result.txt` | 锁屏快照与设置结果 |
| `test-frame.jpg`、`selftest.txt` | 单次自检结果 |
| `compatibility/results.json`、`compatibility/*.jpg` | 全库测试结果与截图 |
| `%TEMP%\AWall-error.log` | Dispatcher 未处理异常，位于数据根之外 |

排查时先确定素材类型、入口路径、显示器数量和错误日志，再定位页面或宿主。性能结论应包含素材、分辨率、显示器数、实际 FPS、CPU/GPU/内存指标及测量方式，不能由静态源码推导当前机器的占用率。

## 11. 后端初始化约束

MP4通过--wid嵌入Player HWND，ScenePlayer转发暂停、截图、统计与释放，视频不初始化WebView2。PKG继续独立CoreWebView2Environment.CreateAsync后EnsureCoreWebView2Async；当前无WarmEnvironment/Prewarm/ShutdownWarmup或环境预热复用。INIT日志记录创建环境、控制器和完整异常堆栈；单凭HRESULT不能确定COM内部原因。构建与实测结果统一记录在[LIVELY-MIGRATION-PLAN.md](LIVELY-MIGRATION-PLAN.md)，避免用本节历史描述覆盖当前实现。
