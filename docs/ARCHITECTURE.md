# AWall 架构

AWall 是给 Windows 桌面套上的一层：壁纸和桌面美化是两条产品线，共用一套模型、一套命令和一个常驻宿主。本文档只记录**已经实现或已拍板的**架构与约定；桌面美化中尚未实现的部分见 [桌面美化任务](tasks/desktop.md)。

## 1. 桌面模型：层、块和场景

| 概念 | 说明 | 代码 |
|---|---|---|
| 层 `DesktopLayer` | `Wallpaper`（最底层，Windows 所说的背景）、`BelowIcons`（图标下方的装饰，点击穿透到桌面）、`AboveIcons`（可交互的块，如分区、可点击的小组件） | `Features/Desktop/DesktopModel.cs` |
| 块 | 桌面上的一个东西：时钟、分区、数据生成图……每个块是一个**内容包** | `BlockManifest` |
| 场景 `DesktopScene` | 一整套桌面状态（每块屏的壁纸 + 放了哪些块、放在哪），用户在工作 / 游戏 / 夜间之间切换；可按时间、前台程序或命令切换 | `DesktopScene` / `PlacedBlock` |

现有的 MP4 / PKG / HTML 壁纸就是 `Wallpaper` 层的实时内容；AI 生成的 HTML 壁纸已经是内容包的雏形。

目前只有数据模型和清单校验（`Features/Desktop/DesktopModel.cs`），还没有宿主加载块，也没有数据源。桌面美化的产品形态、交互、挂载方案和路线图都还是计划，见 [桌面美化任务](tasks/desktop.md)；实现并经用户验收后再写回本文档。

## 2. 能静则静：三档呈现

开销主要来自“画面怎么渲染”，而不是窗口挂在哪一层。所以宿主按内容的变化频率选择最便宜的呈现方式（`PresentPolicy.Choose`）：

| 内容 `PresentMode` | 呈现方式 `PresentRoute` | 空闲开销 |
|---|---|---|
| `Static` 静态图片 | `SystemWallpaper`：通过官方 `IDesktopWallpaper` 接口设成 Windows 系统壁纸，由资源管理器 / DWM 绘制，AWall 不参与渲染 | ≈ 0 |
| `Interval` 几分钟变一次（数据生成图、天气、日历） | `SnapshotToSystemWallpaper`：每次更新时离屏渲染成 PNG → 设成系统壁纸 → 立刻释放渲染器 | 只在更新那一下 |
| `Realtime` 视频 / shader / 动画网页 | `LiveWindow`：WorkerW 下的桌面窗口 + 暂停 / 降帧策略（现有播放器） | 持续占用 |

壁纸已接通三档（`WallpaperService.RouteOf`，实现在 `Features/Wallpaper/WallpaperService.Static.cs`）：

| 素材 | 呈现档 | 说明 |
|---|---|---|
| 图片（`library/wallpapers/image/`，jpg / jpeg / png / bmp） | `SystemWallpaper` | 文件本身交给 Windows |
| HTML，`<head>` 里写 `<meta name="awall-present" content="interval=300">` | `SnapshotToSystemWallpaper` | 每 N 秒（≥ 60）离屏渲染一次 |
| HTML，`content="static"` | `SnapshotToSystemWallpaper` | 只渲染一次 |
| 其他 HTML、MP4、PKG | `LiveWindow` | 现有播放器 |

规则：

- 只有 `Wallpaper` 层、非交互的块才走系统壁纸；`BelowIcons` / `AboveIcons` 的块总是 `LiveWindow`。
- 设置系统壁纸会写文件、可能带淡入动画，所以快照档最短间隔是 **60 秒**（`PresentPolicy.MinSnapshotSeconds`），更快的更新自动降为实时档。
- **每块屏单独选档**：切换计划里，实时档的屏走原来的播放器流程；静态 / 快照档的屏没有播放器。这些屏同样先盖上旧画面，等图片准备好、所有新播放器就绪后，统一提交：先把图片设成该屏的系统壁纸，关掉该屏的旧播放器，再和实时屏一起淡出遮罩。任何一屏失败都整体回滚，系统壁纸只在提交时才改。
- **快照**：用一个从不挂到桌面的离屏播放器，按显示器的实际像素大小渲染。首帧到达后等 1 秒让开场动画稳定，截 PNG 存到 `%LOCALAPPDATA%\AWall\snapshots\`（每屏只保留当前一张），然后立刻关闭播放器。暂停、锁屏或桌面被窗口盖满时不刷新。快照在页面没有预览图时兼作素材库预览（同名 `.png`）。
- 所有屏都是静态档时，不预启动 PKG 待命播放器（AWall 不留任何浏览器进程），下一次切到 PKG 时冷启动。
- `system-wallpaper.set` 走同一条切换流程，可以只设一块屏，其他屏的动态壁纸不受影响。停止壁纸后系统壁纸保持最后一张。
- 系统壁纸的实现在 `Platform/SystemWallpaper.cs`（按显示器边界匹配 `Forms.Screen`）。过渡遮罩里的“旧画面”优先读该屏自己的系统壁纸（`StartupBackdrop`）。

## 3. 内容包格式（`awall.json`，草案）

一个块就是一个文件夹，里面放 `awall.json` 和入口文件。人写、从别处导入、AI 生成都用同一个格式。

```json
{
  "id": "clock",
  "name": "时钟",
  "version": "1.0",
  "kind": "web",
  "entry": "index.html",
  "layer": "belowIcons",
  "present": "realtime",
  "fps": 1,
  "interval": 300,
  "size": { "width": 320, "height": 160 },
  "anchor": "top-right",
  "data": ["time"],
  "interactive": false
}
```

| 字段 | 取值 | 说明 |
|---|---|---|
| `id` | 文件名安全的字符串 | 必填，唯一 |
| `kind` | `web` / `image` / `native` | `native` 是 AWall 内置的原生块（时钟、文字、系统状态），不需要 entry |
| `entry` | 包内相对路径 | 不能跳出包文件夹；`web` 默认为 `index.html` |
| `layer` | `wallpaper` / `belowIcons` / `aboveIcons` | 默认 `belowIcons` |
| `present` | `static` / `interval` / `realtime` | 决定呈现档位；`image` 不能是 `realtime` |
| `fps` | 1–60 | 实时档的帧率上限，由宿主统一调度（时钟 1 就够） |
| `interval` | 秒 | 快照档的更新间隔 |
| `size` / `anchor` | 像素 / 九宫格位置 | 默认放置方式，用户可以在编辑模式里拖动 |
| `data` | 数据源名列表 | 由宿主拉取后推送；**块自己不发网络请求** |
| `interactive` | bool | 需要点击的块必须放在 `aboveIcons` 层 |

HTML 块沿用现有 HTML 壁纸的约定：注入 `web-shim.js`（帧率上限、暂停、光标、错误上报、`window.awall`），用 `awall-properties` 声明可调参数，外网请求默认拦截（jsdelivr / unpkg / cdnjs 库文件例外，首次下载后缓存）。校验逻辑在 `BlockManifest.Load` / `Validate`。

## 4. 开销原则

1. **所有 HTML 块共用一个 WebView2 环境**，最好合成到同一个页面里（每个块是一个容器或 iframe），而不是一个块一个 WebView。这一条对内存影响最大。
2. **简单的块用原生实现**，只有需要复杂动画的块才用 HTML。
3. **暂停策略统一放在宿主**：全屏程序、桌面被完全挡住、锁屏、用电池时，自动暂停或降帧。
4. **刷新频率在 manifest 里声明**，由宿主统一调度。
5. **数据源只拉一次再分发**给所有订阅它的块。
6. 每个块的 CPU / GPU / 内存都能在自检里单独看到（扩展现有的 `PerfProbe`）；AI 生成的包先过一遍性能检查再安装。

## 5. 代码分层

```
App ──► UI ──► Features ──► Render / Platform ──► Core
         └───────────────► Core
```

依赖只能从左往右：上层可以用下层，下层不知道上层存在。

| 层 | 目录 | 负责 | 不允许 |
|---|---|---|---|
| App | `src/App` | 进程入口、单实例、路径、对外命令入口（`Program`、`AppPaths`、`CommandPipe`、`AWall.manifest`）；`src/Cli` 是单独编译的 `AWallCmd.exe` 入口 | 业务逻辑 |
| Core | `src/Core` | 纯数据与规则：素材模型与扫描（`Item`、`LibraryScanner`、`WeImport`）、设置（`AppSettings`）、布局模型（`SavedLayout`）、命令总线（`CommandBus`） | 引用 UI、Render、Platform；调用 Windows 窗口 API |
| Platform | `src/Platform` | Windows 相关能力：桌面宿主挂载（`DesktopHost`）、Win32 声明（`Native`）、系统壁纸（`SystemWallpaper`）、开机启动（`Autostart`）、锁屏（`LockScreen` + `Set-LockImage.ps1`）、系统静态背景截图（`StartupBackdrop`） | 引用 UI |
| Render | `src/Render` | 产生像素：WebView2 场景宿主（`ScenePlayer` + `scene/`）、mpv（`MpvPlayer`）、桌面播放窗口（`Player`）、切换遮罩（`TransitionCover`）、CDN 缓存（`WebCdn`） | 引用 UI |
| Features | `src/Features` | 产品线：`Wallpaper`（播放控制器 `WallpaperService`：每屏计划、切换遮罩与回滚、PKG 同页切换、预热播放器、暂停规则、桌面恢复、轮播、布局保存；HTML 参数 `WebProperties`）、`Desktop`（层 / 块 / 场景模型、呈现策略） | 引用 UI |
| UI | `src/UI` | 展示和转发：主窗口（按关注点拆成 partial）、托盘、主题、参数窗口、导入窗口 | 自己实现业务规则；绕过命令表 |
| Diagnostics | `src/Diagnostics` | 自检和性能测量（`PerfProbe`、`MainWindow.SelfTest`） | 被正式功能依赖 |

### 规则

- **命令优先**：新功能先在命令表里注册（`UI/MainWindow/MainWindow.Commands.cs`），按钮和托盘只调用命令。
- **一个文件一个关注点**；主窗口按 `Commands / Settings / Rotation / Tray / Library / Playback / Layout` 拆成 partial，其中 `Playback / Rotation / Layout` 只是转发到 `WallpaperService` 的薄层。
- **界面只订阅状态**：`WallpaperService` 不认识主窗口，通过事件（`Status`、`Changed`、`RotationChanged`、`DisplaysChanged`、`LibraryChanged`、`PreviewSaved`、`Restoring`）通知界面；素材库列表通过 `Library` 委托读取。
- **不接管资源管理器**：不自己画系统图标，默认不移动用户文件（分区只是视图），退出或崩溃后桌面必须能恢复原样。
- 编译器是 .NET Framework 4.x 自带的 C# 5：不能用字符串插值、`?.`、`nameof`、表达式体成员、`out var`。
- 源码文件用 UTF-8（带 BOM）。
- 只编译 `src/<层>/` 下的文件；`src/` 顶层的旧文件不参与编译（`tools/Finish-Restructure.ps1` 会把它们移进 `垃圾`）。

### 目前已知的例外（下一步要还的债）

- `Item` 为了缩略图引用了 WPF 的 `BitmapImage`；缩略图加载应移到 UI。
- `ScenePlayer`、`MpvPlayer`、`WebCdn`、`DesktopHost`、`WebProperties` 仍各自计算 `%LOCALAPPDATA%\AWall` 或 `library` 路径，应改用 `AppPaths`。
- 目前所有类型都在 `AWall` 命名空间里；分层靠目录和上面的规则保证，以后可以按层加子命名空间。

## 6. 命令层

所有可见操作都是一个有名字的命令，参数是 `key=value`。处理函数在 UI 线程运行；每次执行都记入 `%LOCALAPPDATA%\AWall\commands.log`。

| 命令 | 参数 | 说明 |
|---|---|---|
| `app.show` / `app.exit` / `app.commands` | | 打开窗口 / 退出 / 列出全部命令 |
| `wallpaper.status` | | 每块显示器的壁纸、暂停与加载状态 |
| `wallpaper.apply` | `file=<路径或标题> [display=all\|1..n]` | 设为动态壁纸（显示器按“应用到”从左到右编号） |
| `wallpaper.pause` / `resume` / `toggle-pause` | | 暂停 / 继续 |
| `wallpaper.next` | | 轮播到下一张 |
| `wallpaper.stop` | | 停止并清除恢复布局 |
| `wallpaper.lock-snapshot` | | 主显示器当前画面设为锁屏图片 |
| `library.scan` / `library.list` | | 重新扫描 / 列出素材 |
| `rotation.set` | `[on=on\|off] [minutes=5\|15\|30\|60\|180] [order=sequential\|random]` | 轮播设置 |
| `autostart.set` | `on=on\|off` | 开机自动启动 |
| `system-wallpaper.set` | `file=<图片> [display=all\|1..n]` | 静态档：任意位置的图片设为 Windows 系统壁纸，AWall 不参与渲染；其他屏不变 |

`wallpaper.status` 的每块显示器带 `route`：`live` / `system-wallpaper` / `snapshot`。

### 对外入口：命令管道与命令行

外部程序通过本地 named pipe 调用同一张命令表（`App/CommandPipe.cs`）：

- **管道**：`\\.\pipe\AWall.Cmd.S<会话号>`，只在拥有单实例互斥量的正式实例里开启（自检模式不开）。访问控制只允许当前用户，拒绝网络登录。
- **协议**：一次连接一个请求。客户端写一行 UTF-8 JSON `{"command":"wallpaper.apply","args":{"file":"...","display":"2"}}`，服务端回一行 `{"ok":true,"message":"...","data":...}`。请求最长 65536 字符；参数值一律转成字符串。
- **执行**：管道线程收到请求后用 `Dispatcher.Invoke` 切到 UI 线程执行命令并序列化结果，最长等 30 秒，超时回“AWall 正忙”。执行同样记入 `commands.log`；管道自身的错误记入 `command-pipe.log`。
- **命令行**：`CommandLine.Run` 是唯一的命令行实现，有两个入口：控制台程序 `AWallCmd.exe`（`src/Cli/`，由 `build.ps1` 单独编译，只带 `CommandPipe.cs`、`AppPaths.cs`、`CommandBus.cs`）和 `AWall.exe --cmd`（窗口程序，先附着调用方的控制台；PowerShell / cmd 不等待窗口程序，所以终端里用 `AWallCmd.exe`）。

MCP 桥接尚未实现，见[任务页](tasks/desktop.md)路线图第 7 步。
