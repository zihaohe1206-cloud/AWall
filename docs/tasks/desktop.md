# 桌面美化

## 状态

- 阶段：路线图第 2 步 · 自动回归已通过，待手动回归（第 1 步已完成）
- 已完成：分层目录、命令表、桌面模型（层 / 块 / 场景 / 呈现策略）、系统壁纸接口，已写入 [架构文档](../ARCHITECTURE.md)
- 第 2 步：播放、轮播、布局逻辑已移到 `Features/Wallpaper/WallpaperService`（三个 partial 文件），主窗口只调用它的方法、订阅它的事件。编译和 `--test-transitions`、`--test-library` 已通过，**手动回归未做**，见下方“验证”
- 已定（2026-11-19，用户授权按建议决定）：
  - 揭开的默认快捷键：`Ctrl+Alt+Space`，可在设置中改
  - “不接管资源管理器、默认不移动用户文件”定为架构约定（已写入架构文档）；第一版规则分区不提供“真的移动文件”的开关
  - `ext:` 外部数据源不提前，仍在第 7 步；第 6 步先做本机数据源
- 最后更新：2026-11-19

## 目标

把 AWall 从壁纸工具扩展成“平时它就是桌面，编辑时它是画布”：壁纸之上加分区、小组件等块，块订阅本机数据，整套桌面状态可以按场景切换，并可由命令行或 agent 控制。本页内容**均未实现**，除非条目注明已实现或已验证。

## 产品形态

### 桌面就是界面

- 不以主窗口为中心。主窗口只用来管理素材库和设置；对桌面的改造直接在桌面上完成。
- **编辑模式入口**：资源管理器桌面右键菜单里加一项“编辑桌面（AWall）”（注册表添加，不替换系统菜单）、快捷键、托盘菜单。
- **编辑模式中**右键归 AWall（块的设置、删除、换样式）；退出编辑模式后右键还给系统。
- 平时所有块都锁定，防止误拖；编辑模式下显示网格，拖动吸附、改尺寸、从块库添加。

### 发挥桌面自己的优势

桌面比网页和普通窗口多四个优势，块的设计都围绕它们：

- **一直在**：不用打开、不用切换，窗口一让开就能看到。适合放“扫一眼就够”的信息。
- **余光可读**：用大字、颜色、形状表达，不聚焦也能看懂，而不是密密麻麻的表格。
- **离本机近**：能读到网页读不到的数据（CPU、正在播放、文件夹、空闲时间），而且不出网。
- **被挡住就不该花钱**：窗口盖满时，桌面上所有东西停到接近零开销（见[架构文档](../ARCHITECTURE.md)第 4 节“开销原则”）。

另外一条底线：**不接管资源管理器**。不自己画系统图标，默认不移动用户的文件，分区只是“视图”。任何时候退出或崩溃，桌面都能恢复原样。

### 每层挂在哪个窗口下

| 层 | 父窗口 | 能否点击 | 说明 |
|---|---|---|---|
| `Wallpaper` | WorkerW（现有 `DesktopHost`） | 否 | 现有播放器 |
| `BelowIcons` | WorkerW，排在壁纸窗口之上 | 否，点击落到桌面 | 装饰类块，和壁纸一起被图标盖住 |
| （图标） | 资源管理器的 `SysListView32` | 是 | 不归 AWall 管，AWall 只控制显隐 |
| `AboveIcons` | `SHELLDLL_DefView` 的子窗口 | 是 | 在图标之上、所有应用窗口之下；Win+D 不会把它隐藏。DeskFrame 用的就是这种挂法 |
| 揭开（临时） | 暂时脱离 DefView 并置顶 | 是 | 见下方“揭开” |

`DesktopHost.Parent()` 在找 WorkerW 时已经拿到了 `defView`，挂到 DefView 只需要新增一个挂载入口，健康检查和 Explorer 重启后的重建可以复用。

### 分区（收纳格）

分区是 `AboveIcons` 层的原生块（`kind: native`），用 WPF 绘制，不开 WebView。

| 种类 | 显示什么 | 文件在哪 |
|---|---|---|
| 规则分区 | 桌面文件夹里符合规则的项：按类型、按时间（如今天新建）、按名称关键词 | 原地不动，只是归到分区里显示 |
| 文件夹分区 | 任意文件夹的实时镜像，例如“下载”或某个项目目录 | 在那个文件夹里 |
| 自由分区 | 用户拖进来的快捷方式和文件，手动排列 | 桌面文件夹（AWall 只记录归属） |

- 默认**不移动文件**。参考代码 Desktop Fences+ 的自动整理会真的 `File.Move` 桌面文件，AWall 只借它的规则匹配。
- 没被任何分区收下的文件进入一个可折叠的“其他”分区，保证文件不会“消失”。
- 右键弹出系统原生菜单，双击、拖放、重命名的行为和资源管理器一致。
- 分区可以收起成只剩标题栏；配色取自当前壁纸，换壁纸时跟着过渡。

### 图标显隐

- 双击桌面空白处：隐藏 / 显示系统图标和所有分区，只剩壁纸和其他块。点在图标上不触发。
- 可选自动隐藏：鼠标离开桌面一段时间后淡出，回到空白处时淡入。
- 安全规则：退出、停止桌面、Explorer 重启时一律恢复图标；启动时发现上次异常退出且图标仍隐藏，先恢复。

### 揭开

不用最小化窗口，按快捷键或把鼠标推到屏幕角，就把 `AboveIcons` 的块临时拿到所有窗口上面：背后的窗口变暗，块可以点、可以往分区里拖文件，Esc 或点空白处收回。这是桌面比“切换到另一个窗口”更快的地方。默认快捷键 `Ctrl+Alt+Space`，可配。

### 系统事件下的行为

| 场景 | 行为 |
|---|---|
| Win+D / 显示桌面 | `AboveIcons` 的块和图标一样留在桌面上 |
| 全屏 / 窗口盖满 / 锁屏 | 壁纸和块一起暂停，数据源停止采集；沿用“全屏应用时暂停”开关 |
| 用户离开 | 可选切到“待机场景”（例如大时钟 + 暗色壁纸），回来时切回 |
| 多显示器 | 布局按显示器保存（和壁纸一样按 DeviceName）；拔掉一块屏时，它上面的块暂时收起，不挤到其他屏 |
| 换壁纸 / 轮播 | 重新取色，分区和块的配色和壁纸淡出同步过渡 |
| Explorer 重启 | 沿用现有 TaskbarCreated 处理：重新挂载各层，恢复图标显隐状态 |

## 数据源频道

`data` 里填的是下面的频道名。数据源同时服务块和 HTML 壁纸：例如“用股票数据驱动的生成图”就是订阅了 `ext:` 频道的 HTML 壁纸或块。

| 频道 | 内容 | 采集方式 | 默认权限 |
|---|---|---|---|
| `time` | 时间、日期、时区变化 | 页面自己算，宿主只推时区变化 | 无需授权 |
| `theme` | 当前壁纸的主色、强调色、明暗 | 从壁纸预览图或切换时的截帧取色 | 无需授权 |
| `sys.*` | CPU、GPU、内存、磁盘、网速、电池 | 性能计数器（可复用 `PerfProbe` 的读法） | 首次放置时确认 |
| `media` | 正在播放的曲名、封面、进度，可控制播放 | Windows 媒体会话接口（WinRT） | 首次放置时确认 |
| `audio` | 系统音频频谱 | WASAPI 回环采集 | 首次放置时确认 |
| `idle` | 多久没动键鼠、是否锁屏 | `GetLastInputInfo`、会话事件 | 无需授权 |
| `folder:<路径>` | 某文件夹的文件列表和变化 | `FileSystemWatcher` | 按文件夹单独授权 |
| `ext:<名字>` | 外部数据：天气、行情、自己的服务 | 宿主按间隔请求用户登记的 HTTPS 地址 | 用户手动登记才存在 |
| `push:<名字>` | 其他程序或 agent 推进来的 JSON | 下方“对外开放”中的 named pipe / `--cmd` | 默认关闭 |

- **页面仍然不能联网**。`ext:` 的请求由宿主发出、地址由用户登记，页面只拿到结果 JSON。
- **按需采集**：一个频道没有可见的订阅者就不采集；块被挡住或桌面暂停时一并停。
- **授权跟着块走**：放置块时列出它要的频道，用户确认后记住，可随时撤销。
- `media` 是 WinRT 接口，C# 5 + csc 直接调用不方便，可以仿照 `Set-LockImage.ps1` 走 PowerShell 辅助脚本，或引用 `Windows.winmd`，实现时再定。

## 对外开放

顺序：本地 named pipe → `AWall.exe --cmd <命令> key=value`（第二个实例把命令转给正在运行的实例并打印结果）→ 在 CLI 外面包一层 MCP server。有了 CLI，agent 用 shell 就能控制 AWall；MCP 只是翻译层。

## 代码落点与参考代码

参考代码在本地 `research/desktop-refs/`（Git 忽略，索引见其中的 `INDEX.md`）。只从两个 MIT 项目借代码，借用时在 `THIRD-PARTY-NOTICES.md` 加署名，并改写成 C# 5；Lively 是 GPL-3.0，只看思路，用到的地方自己重写。

| 要做的事 | 落点 | 参考文件 | 许可证 / 用法 |
|---|---|---|---|
| `AboveIcons` 挂到 DefView | `Platform/DesktopHost` | `DeskFrame/frame-window/DeskFrameWindow.xaml.cs` 的 `SetAsDesktopChild` | MIT，移植 |
| 找桌面图标窗口、双击空白处判断 | `Platform/` 新增图标层 | `DeskFrame/workerw-icons/MainWindow.xaml.cs`（`HandleGlobalDoubleClick`、`IsDesktopIconHit`） | MIT，借用 |
| 显隐系统图标 | 同上 | `DesktopFencesPlus/hide-icons/DesktopIconManager.cs` | MIT，借用，加异常退出恢复 |
| 低级鼠标钩子（双击、屏幕角、自动隐藏） | `Platform/` | `DesktopFencesPlus/mouse-hook/DesktopMouseHook.cs` | MIT，借用 |
| 系统原生右键菜单 | `Platform/` | `DeskFrame/context-menu/ShellContextMenu.cs` | MIT，借用 |
| 分区：读图标、监听文件夹、拖动吸附 | `Features/Desktop/` + `UI/` | `DesktopFencesPlus/icons/IconManager.cs`、`DeskFrame/frame-window/DeskFrameWindow.xaml.cs` | MIT，借用 / 局部参考 |
| 文件夹分区 | `Features/Desktop/` | `DesktopFencesPlus/folder-portal/PortalFrameManager.cs` | MIT，参考结构 |
| 规则分区 | `Features/Desktop/` | `DesktopFencesPlus/auto-organize/AutoOrganizeManager.cs` | MIT，只借规则匹配，去掉 `File.Move` |
| 按壁纸取色 | `Features/Desktop/` | `DesktopFencesPlus/wallpaper-color/WallpaperColorManager.cs` | MIT，借算法；输入改为 AWall 自己的截帧 |
| 用户离开检测 | `Platform/` | `Lively/idle/SystemIdleUtil.cs` | GPL-3.0，只看思路，用 `GetLastInputInfo` 自写 |

已知风险：

- **揭开时换父**：`AboveIcons` 的窗口临时脱离 DefView、置顶、再放回。DeskFrame 解锁时也这么做，但带 WebView2 的块宿主换父后是否要重新通知位置、会不会闪烁，要先做小实验。备选是揭开时另开一层置顶窗口，用截图充当块。
- **半透明**：子窗口拿不到系统亚克力效果。分区用壁纸取色的半透明纯色；HTML 块能否透明背景要实测。
- **和第三方桌面工具冲突**：同时运行 Fences 一类软件时提示只开一个，和现在对 Wallpaper Engine 的处理一致。

## 路线图

1. ✅ 分层目录、命令表、桌面模型（层 / 块 / 场景 / 呈现策略）、系统壁纸接口。
2. 把播放控制器抽成 `WallpaperService`（每一步都跑 `--test-transitions` 和 `--test-library` 回归）。代码已写，待验证。
3. named pipe + `--cmd` 命令行。
4. 接通静态档和快照档：素材库支持图片类型；`present=interval` 的 HTML 定时离屏渲染并设成系统壁纸；按屏混合呈现。
5. 块宿主：先做三个小实验（挂到 DefView、带 WebView2 的窗口换父、HTML 块透明背景）；`AboveIcons` 挂到 DefView；分区（规则 / 文件夹 / 自由）、图标显隐与双击隐藏、原生右键菜单、按壁纸取色；`BelowIcons` 的块合成到一个共享 WebView2 页面；编辑模式；资源管理器右键菜单项；`--test-desktop` 自检。
    - 验收：Explorer 重启和异常退出后桌面完好；只有分区时 CPU 接近 0。
6. 数据源与场景：先接 `time` / `theme` / `sys` / `idle` / `folder`，再接 `media` / `audio`；场景切换（轮播改为场景内只换壁纸）、待机场景、揭开。
    - 验收：桌面被完全遮挡时，开销与壁纸暂停时持平。
7. 对外开放：`ext:` 外部数据源、`push:` 频道；MCP 桥接；AI 生成内容包的性能检查。

## 验证

每完成一步，在这里记录验收命令、结果和日志位置。

### 第 2 步：WallpaperService（待验证）

改动是纯搬移，行为应与之前完全一致。需要在 Windows 上依次完成：

1. `build-and-log.cmd`：编译通过，`build.log` 末尾为 `=== exit 0 ===`。
2. 退出正在运行的 AWall 后运行 `.\bin\AWall.exe --test-transitions`：`%LOCALAPPDATA%\AWall\transition-test.json` 中 `success` 为 `true`。
3. `.\bin\AWall.exe --test-library`：`compatibility/results.json` 中各项结果与重构前一致。
4. 手动：设壁纸、暂停 / 继续、托盘“下一张”、轮播开关、重启 AWall 后恢复布局、多屏“应用到”。

结果（2026-11-19 第一轮）：编译通过；`--test-library` 33 项全部通过；`--test-transitions` 前 3 项通过，PKG→视频失败。原因与重构无关：内置 MP4 播放器复用了刚释放的 WebView2 配置目录，旧浏览器进程未退出时报 0x8007139F。已改为等 `BrowserProcessExited`（最多 15 秒）再释放目录（`Render/ScenePlayer.cs`），待第二轮验证。

结果（2026-11-19 第二轮，干净 worktree）：

- 编译：先失败，`WeCandidate` 找不到。原因是 `.gitignore` 的 `library/` 在 Windows 上不区分大小写，把 `src/Core/Library/`（`Item.cs`、`LibraryScanner.cs`、`WeImport.cs`）一起忽略了，这三个文件从未入库，第一轮能编译只是因为主目录里有本地文件。规则改为 `/library/` 并补交这三个文件后，编译通过（两个既有警告）。
- `--test-transitions`：`success: true`，11 项全部通过，包括第一轮失败的 `scene-to-video`。
- `--test-library`：33 项全部通过，`frameChanged` 33 项，警告 0，与第一轮逐项一致；各类型平均内存、CPU 与第一轮差别在采样波动内。
- 手动回归（第 4 条）：未做。
