# AWall：供 AI 阅读的技术上下文

本文依据 2026-11-16 的项目源码整理。用途是帮助 AI 快速理解、修改和排查本项目；具体实现发生变化时，以源码为准并同步更新本文。本文记录技术事实和维护建议，不代表已执行其中的测试。

## 1. 项目定位与技术栈

AWall 是 Windows 动态桌面壁纸与静态锁屏快照原型，当前 README 标记版本为 0.2。

- 宿主：C#、.NET Framework 4.x、WPF，x64；没有 SDK 风格 csproj，也没有 npm 应用构建流程。
- 桌面集成：Win32 P/Invoke，将 WPF 窗口挂到 Explorer 的 WorkerW 桌面宿主。
- 播放：Microsoft WebView2 WPF 控件。
- 场景引擎：本地固定版本 WebWallGL 3.0.2，兼容 Wallpaper Engine 场景格式。
- MP4：HTML `<video>`，静音、循环、cover 填充。
- 锁屏：WebView2 截图 + PowerShell 调用 WinRT 设置图片。
- 无需运行 Wallpaper Engine；部分 PKG 依赖它安装目录里的共享纹理。素材只从本机读取。

## 2. 先读哪些文件

| 文件 | 职责 |
| --- | --- |
| `README.md` | 启动、功能边界、构建和自检入口 |
| `src/AWall.cs` | Item、Native、Player、MainWindow；UI、扫描、窗口切换、桌面挂载、暂停、测试 |
| `src/ScenePlayer.cs` | WebView2 生命周期、资源映射、消息桥接、诊断、截图 |
| `src/scene/index.html` | WebWallGL 初始化、场景暂停、鼠标位置、统计 |
| `src/scene/video.html` | MP4 加载、首帧、暂停、循环、统计与错误 |
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
| MP4 | WebView2/Chromium 视频管线，支持时使用硬件解码及 GPU 合成 | 编码、驱动或运行环境不支持时可能软件解码；HTML video 不保证硬解 |
| WPF/桌面窗口 | WPF 与系统桌面合成参与展示 | UI、定时器、Win32 调用、窗口管理 |

`ScenePlayer.Initialize()` 没有传入 `--disable-gpu`，也没有强制选择独显或集显。现有 `Stats()` 不提供实际 GPU 适配器、软件渲染回退或视频解码器类型。不要把“使用 WebGL”“存在 GPU 子进程”或“首帧成功”表述为硬件加速已被实测确认。

PKG 配置为 `fps:30`、`renderDpr:1`、`fit:'cover'`、`volume:0`、`scripts:true`、`audio:null`、`media:null`。30 FPS 是目标配置，不保证实际达到；MP4 不受此配置限制。每块显示器创建独立 Player/WebView2，不是共享一份已渲染画面，因此多屏会增加解码、绘制和内存成本。

## 4. 启动与素材模型

`MainWindow.Main()` 设置进程 DPI aware，使用 `Local\AWall.Desktop` 命名互斥体避免同一会话重复运行。普通关闭隐藏到托盘；退出需走 `Exit()`，释放播放器、托盘及系统事件订阅。

`Item` 字段为 `Title`、`Type`、`File`、`Preview`。`Supported` 仅检查 Type 为 `video` 或 `scene`，不是对编码或场景完整性的预验证。

- 默认递归扫描 `E:\Steam\steamapps\workshop\content\431960` 中的 `project.json`。
- 读取 title/type/file/preview，场景入口不存在而有 `scene.pkg` 时回退到该包。
- 扫描时校验素材入口规范化路径处于 project 所在目录内。
- 导入支持本地 `.mp4`、`.pkg`；文件夹选择和导入列表目前没有持久化保存逻辑。
- `web`、`application` 壁纸没有完整播放支持。

## 5. 桌面挂载与切换状态机

`Native.DesktopParent()` 查找 Progman，通过消息 `0x52c` 请求桌面宿主，再寻找 WorkerW。有效宿主句柄被缓存，避免每次创建窗口都扰动桌面。

`Player` 为无边框、不激活、不显示任务栏的 WPF 窗口。创建 HWND 后先挂载、定位，再初始化 WebView2；窗口样式包含 child/no-activate/transparent。显示器坐标转换为桌面父窗口坐标。

`MainWindow.Apply()` 的关键顺序：

1. 取消尚未完成的切换，保留当前有效壁纸。
2. 当前播放器转入 `previousPlayers`，为所有显示器创建新 `players` 批次。
3. 新窗口显示在旧窗口下方，保持可渲染，等待全部首帧就绪。
4. 全部 Ready 后 Present 新窗口，再关闭旧窗口。
5. 任一失败或 60 秒切换超时，销毁新批次并恢复旧批次。

批次引用比较用于拒绝过期回调；窗口替换/销毁通过 Dispatcher 延后，避免在 WebView2 消息回调中直接处理。`presented` 与 WM_WINDOWPOSCHANGING 钩子防止 staging 窗口被 WPF 意外抬到旧壁纸上方。修改切换流程时应保留这些时序约束。

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

场景页面通过 WebWallGL.httpSource 读取素材；PKG 场景重写 source 的包读取和 project 入口。mount 超时为 45 秒，完成后再等待两次 requestAnimationFrame 才报告 ready。MP4 加载超时为 30 秒，优先使用 requestVideoFrameCallback，再等待两次 requestAnimationFrame 报告首帧。

## 7. 暂停、输入与锁屏

- 2 秒 Tick 检测前台窗口是否覆盖任意显示器；默认启用全屏暂停，策略作用于全部播放器。
- 实际暂停条件为 `manualPause || locked || covered`。
- SessionSwitch 处理锁定/解锁；显示器拓扑改变后重新 Apply 当前素材。
- Tick 检查桌面父 HWND 是否仍有效，失效时尝试 Attach。
- 鼠标定时器为 50 ms，只有光标所在显示器推送归一化位置；ScenePlayer 对视频、未就绪或暂停场景跳过输入。
- 未接入系统音频频谱、Now Playing、鼠标点击；不要用模拟音频数据宣称已支持系统声音响应。
- Snapshot 使用 WebView2 CapturePreviewAsync 输出 JPEG。锁屏选主显示器播放器，调用 PowerShell 设置静态图片，不在 Win+L 界面播放动画。
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

脚本使用 `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`，直接编译两个 C# 文件，引用 WPF、WinForms、WebView2 等程序集，再复制页面、脚本、DLL 和许可证到 bin。依赖已在 vendor 中，构建无需联网；运行需要 WebView2 Runtime。

运行测试会创建真实桌面壁纸并在结束时退出，且受单实例互斥体限制。运行前确认已有 AWall 实例已退出。

```powershell
.\bin\AWall.exe --self-test
.\bin\AWall.exe --self-test --test-file "C:\path\sample.mp4"
.\bin\AWall.exe --test-library
.\bin\AWall.exe --test-library --only=工坊目录ID1,工坊目录ID2
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
| `lock-<GUID>.jpg`、`lock-result.txt` | 锁屏快照与设置结果 |
| `test-frame.jpg`、`selftest.txt` | 单次自检结果 |
| `compatibility/results.json`、`compatibility/*.jpg` | 全库测试结果与截图 |
| `%TEMP%\AWall-error.log` | Dispatcher 未处理异常，位于数据根之外 |

排查时先确定素材类型、入口路径、显示器数量和错误日志，再定位页面或宿主。性能结论应包含素材、分辨率、显示器数、实际 FPS、CPU/GPU/内存指标及测量方式，不能由静态源码推导当前机器的占用率。

## 2026-11-16 切换后端更新

MP4 已改为 mpv 子进程，通过 --wid 嵌入 Player HWND，ScenePlayer 负责转发暂停、截图、统计与释放。视频不再初始化 WebView2；保留的 video.html 是旧后端资源。mpv 由 build.ps1 从 vendor/mpv 复制到输出目录，包含 awall-ready.lua；缺少后端会触发原有切换回退。PKG 继续 WebWallGL。每屏两份独立 WebView2 环境在空闲时预热，通过屏外空白控制器保持浏览器存活，不共享正在播放壁纸的 profile。新播放器与旧画面截图并行，首帧和遮罩双重就绪后才淡入。以上更新优先于本文前面的旧播放路径描述。

PKG 0x8007139F 回归修复优先说明：已移除 WarmEnvironment/Prewarm/ShutdownWarmup 和对应宿主调用，恢复原有独立 CoreWebView2Environment.CreateAsync 后 EnsureCoreWebView2Async 的路径。预热机制当前已禁用。INIT 日志记录创建环境、控制器和完整异常堆栈。没有实机验证，错误 HRESULT 只能定位到初始化失败，不能据此确定具体 COM 内部原因。
