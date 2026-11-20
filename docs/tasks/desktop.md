# 桌面美化

## 状态

- 阶段：第 5 步（块宿主）拆成 5.1–5.5（见路线图）。5.1“桌面层基础”、5.2“分区”、5.3“双击隐藏与按壁纸取色”已实现；5.3 已由用户验收（2026-11-20），第 3、4、5.1、5.2 步**待用户验收**
- 5.3：独立线程上的低级鼠标钩子，双击空白桌面在“干净桌面”和正常状态之间切换；分区配色按每块屏的当前画面取色，换壁纸后渐变过去。新增命令 `desktop.clean`、`desktop.double-click`、`fence.theme`，托盘里有开关。约定写入[架构文档](../ARCHITECTURE.md#分区fences)
- 5.2：规则 / 自由 / 文件夹三种分区，加上“其他”分区（桌面接管），以及系统右键菜单、拖放、重命名、存盘、Explorer 重启后重建、`fence.*` 命令、托盘子菜单和 `--test-fences` 自检。约定已写入[架构文档](../ARCHITECTURE.md#分区fences)，用法写入 README
- 5.1：三个实验已做完。实现了 `AboveIcons` 挂载到 DefView（`DesktopHost.AttachAboveIcons` / `Reveal`、`Features/Desktop/BlockWindow`）、图标显隐（`Platform/DesktopIcons`，命令 `desktop.icons`，托盘菜单项）和 `--test-desktop` 自检，约定已写入[架构文档](../ARCHITECTURE.md#aboveicons-层与图标显隐)
- 已定（2026-11-20，用户确认）：子窗口里的网页块不能透明，`BelowIcons` 装饰块不另开共享透明页面，改为画进壁纸本身，见路线图 5.4
- 第 4 步：素材库支持图片；HTML 用 `<meta name="awall-present">` 声明静态 / 定时；每块屏独立选呈现档（实时 / 系统壁纸 / 快照）。实现在 `Features/Wallpaper/WallpaperService.Static.cs`、`WebPresent.cs`，约定已写入[架构文档](../ARCHITECTURE.md#2-能静则静三档呈现)
- 第 3 步：`App/CommandPipe.cs`（管道服务端、客户端、命令行前端）+ 控制台程序 `AWallCmd.exe`；约定已写入[架构文档](../ARCHITECTURE.md#对外入口命令管道与命令行)，用法写入 README
- 已完成（第 1 步）：分层目录、命令表、桌面模型（层 / 块 / 场景 / 呈现策略）、系统壁纸接口，已写入 [架构文档](../ARCHITECTURE.md)
- 第 2 步：播放、轮播、布局逻辑已移到 `Features/Wallpaper/WallpaperService`（三个 partial 文件），主窗口只调用它的方法、订阅它的事件。自动回归和手动回归均已通过（多屏未测），见下方“验证”
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
| `BelowIcons` | 不单独开窗口，画进壁纸（见路线图 5.4） | 否，点击落到桌面 | 装饰类块，和壁纸一起被图标盖住 |
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

前两项已实现（第 3 步）：另加了控制台程序 `AWallCmd.exe`，因为 PowerShell / cmd 不等待窗口程序 `AWall.exe` 结束，直接在终端里跑 `--cmd` 时输出会落在提示符之后。

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
2. ✅ 把播放控制器抽成 `WallpaperService`（每一步都跑 `--test-transitions` 和 `--test-library` 回归）。
3. named pipe + `--cmd` 命令行。已实现，待用户验收。
4. 接通静态档和快照档：素材库支持图片类型；`present=interval` 的 HTML 定时离屏渲染并设成系统壁纸；按屏混合呈现。已实现，待用户验收（多屏混合只在单屏上验证了代码路径）。
5. 块宿主，拆成五个子步骤：
    1. 桌面层基础。已实现，待用户验收。
       - 三个小实验：挂到 DefView、带 WebView2 的窗口换父、HTML 块透明背景。
       - `AboveIcons` 挂到 DefView，并支持揭开 / 收回。
       - 图标显隐，含异常退出后的恢复，命令和托盘菜单都能调用。
       - `--test-desktop` 自检。
    2. 分区：规则 / 文件夹 / 自由三种；读图标、监听文件夹；双击、拖放、重命名；原生右键菜单（`ShellContextMenu`）；块布局存盘；Explorer 重启后重新挂载。已实现，待用户验收。“编辑模式”在 5.5 才做，目前先用“解锁布局”来移动和改大小。
    3. 鼠标钩子：双击桌面空白处隐藏图标和分区；按壁纸取色，分区配色跟着换壁纸过渡。已实现，用户已验收（2026-11-20）。配色在壁纸切换完成后才渐变，不是和壁纸淡出同时进行：要等新画面出来才能取色。
    4. `BelowIcons` 装饰块。实验 3 证明子窗口里的网页做不了透明，所以不另开共享页面，把块画进壁纸本身（2026-11-20 用户确认）：
       - 实时壁纸本来就是 WebView2 页面（视频、PKG、HTML），块作为叠加层注入；
       - 静态 / 快照档把块合成进系统壁纸图片；
       - mpv 后端没有页面，不支持装饰块。

    5. 编辑模式（网格、拖动吸附、块库）、资源管理器右键菜单项“编辑桌面（AWall）”。
    - 验收：Explorer 重启和异常退出后桌面完好；只有分区时 CPU 接近 0。
6. 数据源与场景：先接 `time` / `theme` / `sys` / `idle` / `folder`，再接 `media` / `audio`；场景切换（轮播改为场景内只换壁纸）、待机场景、揭开。
    - 验收：桌面被完全遮挡时，开销与壁纸暂停时持平。
7. 对外开放：`ext:` 外部数据源、`push:` 频道；MCP 桥接；AI 生成内容包的性能检查。

## 验证

每完成一步，在这里记录验收命令、结果和日志位置。

### 第 2 步：WallpaperService（已完成）

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
- 手动回归（第 4 条）：用户验收通过（设壁纸与 MP4/PKG/HTML 切换、暂停 / 继续、托盘“下一张”、轮播与全屏延后、重启恢复布局、停止壁纸、托盘唤出）。只有一块显示器，多屏“应用到”未测，接上第二块屏后补测。

### 第 3 步：命令管道与命令行（待用户验收）

结果（2026-11-19，单显示器）：

- 编译：`build-and-log.cmd` 通过，`bin\AWall.exe` 和 `bin\AWallCmd.exe` 都生成，只有两个既有警告。
- AWall 未运行：`AWallCmd wallpaper.status` 打印“没有在运行”，退出码 2；不带参数打印用法，退出码 64。
- AWall 运行时（PowerShell）：`help` 列出 16 个命令；`wallpaper.status` 人读输出与 `--json` 一致；`wallpaper.pause` / `resume` 生效，状态里 `paused` 随之变化；未知命令、`rotation.set minutes=7`、不存在的素材都返回失败、退出码 1；多余的非 `key=value` 参数退出码 64。
- `AWall.exe --cmd wallpaper.status --json` 在输出被重定向时结果与 `AWallCmd` 相同。
- Git Bash：默认输出按控制台代码页（GBK）编码，在 UTF-8 终端里中文乱码，所以加了 `--utf8`，加上后显示正常；`--json` 全是 ASCII，不受影响。8 个并发请求全部成功（`commands.log` 中同一秒 8 条 `wallpaper.status -> ok`）。
- 直接写管道：非法 JSON、缺少 `command`、超过 65536 字符的请求都返回 `ok:false` 和原因；数字参数按字符串传入。
- `app.exit` 通过命令行发出时仍能收到“正在退出。”，随后进程退出。
- `--test-transitions`：`success: true`，11 项全部通过（自检模式不开管道）。

待用户验收：在自己的终端里试几条命令（例如 `.\bin\AWallCmd.exe help`、`wallpaper.next`、`app.show`），确认 `app.show` 能把窗口带到前台。

### 第 4 步：静态档、快照档与按屏混合呈现（待用户验收）

结果（2026-11-19，单显示器 2560×1440，125% 缩放）：

- 编译通过，只有两个既有警告。`--test-transitions` 在改动后跑了两次，都是 11 项全部通过。
- 图片（`system-wallpaper.set`，scratchpad 里的 PNG）：0.6 秒完成切换，`route: system-wallpaper`，系统壁纸已更换。
- 素材库图片：在 `library/wallpapers/image/` 放一张 PNG 后，`library.list` 列出类型为 `image` 的项；`wallpaper.apply` 后状态为 `system-wallpaper`。
- 快照档（测试页声明 `interval=60`、显示当前时间）：约 2.1 秒完成切换，其中渲染 1.6 秒；`route: snapshot`；截图内容正确，同时生成了素材库预览图。约 60 秒后自动刷新，旧快照被删除，只留一张；这次刷新比预定晚了约一分钟，期间桌面可能被全屏窗口遮挡（遮挡时不刷新）。
- 测出并修复两个问题：
  1. 全静态时仍有 PKG 待命播放器（6 个 WebView2 进程）。改为全静态时关闭待命播放器，修复后 AWall 的 WebView2 进程数为 0。
  2. 离屏快照比屏幕大 23 像素（2583×1463）。改为按显示器像素设置窗口大小，修复后为 2560×1440。
- 重启 AWall：静态布局照样恢复（重新渲染一次快照）。
- 收尾：先设回原系统壁纸 `img0.jpg`，再切回动态壁纸“三维点阵海浪”。转码缓存与测试前的备份逐字节一致。测试文件均已删除。
- 未验证：多显示器混合（例如一块屏视频、一块屏图片），只有一块屏，代码路径只在单屏上走过；锁屏快照没测，以免改动你的锁屏图片。

待用户验收：导入一张图片设为壁纸；写一个带 `<meta name="awall-present" content="interval=60">` 的网页，看是否每分钟更新；有第二块屏时测一块视频、一块图片。

### 第 5.1 步：桌面层基础（待用户验收）

结果（2026-11-20，单显示器 2560×1440；VALORANT 全屏运行，`SHQueryUserNotificationState` = 2）：

- 编译通过，只有两个既有警告。`--test-transitions` 11 项全部通过。
- `--test-desktop` 的 12 项全部通过，结果在 `%LOCALAPPDATA%\AWall\desktop-test.json`：
  - **实验 1，原生块挂到 DefView**：挂载用时 80–150 ms。`ChildWindowFromPointEx` 显示块在图标列表之上。用 `PrintWindow` 抓桌面，能看到块的颜色。只有一个原生块时，AWall 的 CPU 为 0.9%（3 秒采样，进程刚启动）。
  - **实验 2，带 WebView2 的块揭开 / 收回**：揭开 9–11 ms，收回 3–4 ms。揭开后 400 ms 内每 15 ms 取一次色，没有一帧不对。页面帧计数一直在增长，来回两次都正常。一个网页块共 7 个进程（AWall + 6 个 WebView2），CPU 0.3%。
  - **实验 3，网页块透明背景：不可行**。
    - 透明背景：透明处是黑色 `#000000`。
    - 子窗口加 `WS_EX_LAYERED` 颜色键：`SetLayeredWindowAttributes` 返回 87，透明处是 WPF 背景色 `#010203`。

    结论见路线图 5.4（已按建议改，用户已确认）。
  - **图标**：隐藏后再恢复；模拟崩溃后启动时自动恢复；用户自己在资源管理器里关掉图标时，`Restore` 不会去打开。
- 命令行：`desktop.icons visible=off` / 不带参数切换 / `visible=maybe`（失败，退出码 1）都符合预期。先隐藏、再 `app.exit`，图标恢复，标记文件已删除。
- 未验证（桌面被全屏游戏挡住，自检自动跳过，也没有切换“显示桌面”）：
  - 屏幕上实际看到块；
  - 鼠标点在块上时命中块；
  - Win+D 后块仍留在桌面上；
  - Explorer 重启后图标与块的表现：自检不会杀 Explorer。

待用户验收：
1. 关掉全屏程序后运行 `.\bin\AWall.exe --test-desktop`，`desktop-test.json` 里应多出 `screen.*` 和 `win+d.*` 几项，且 `success` 为 `true`。
2. 在托盘菜单里试“隐藏桌面图标 / 显示桌面图标”。

### 第 5.2 步：分区（待用户验收）

结果（2026-11-20，单显示器 2560×1440，125% 缩放）：

- 编译通过，只有两个既有警告。回归：`--test-desktop` 的 `success` 为 `true`，屏幕取色项跳过，因为测试点被 Chrome 内核的窗口挡住；`--test-transitions` 11 项全部通过。
- `--test-fences` 的 20 项全部通过，结果在 `%LOCALAPPDATA%\AWall\fence-test.json`：
  - **规则归类**：自由分区优先认领，规则按顺序匹配，关键词、天数、文件夹、虚拟图标都按预期处理，剩下的进“其他”。规则顺序会影响结果。
  - **存盘**：写入后再读回，内容一致。
  - **文件夹分区**：
    - 挂在图标之上；
    - 列出 3 项，文件夹排在前面，3 个图标都加载出来了；
    - 用 `PrintWindow` 取色：内部是 `#1F2229`，左上角被圆角裁掉；
    - 新建、重命名（`SHFileOperation`）、删除文件后，分区都在 3 秒内更新。
  - **系统右键菜单**：一个 `.txt` 文件的菜单有 22 项，回收站有 9 项。回收站的图标取到了 48 px。
  - **布局**：用 `fence.set` 改位置和大小生效；收起后高 40 px，展开后恢复到 300 px，配置里存的仍是展开时的高度。
  - **恢复**：父窗口丢失后重新挂载；窗口被销毁（模拟 Explorer 重启）后，用新句柄重建并挂载，内容还在。
  - **桌面接管**：真实桌面 24 项，“图片”规则匹配 0 项，“其他”24 项，两者相加正好是全部；接管期间系统图标隐藏。删除规则分区后，图标恢复，“其他”分区移除。测试文件夹没有被改动。
  - **开销**：只开分区时只有 AWall 一个进程，CPU 0.2–0.5%，内存 155–176 MB（WPF 进程的总占用）。
- 在正式运行的 AWall 里用 `fence.add kind=folder` 建了一个演示分区并截图：圆角、深色卡片、图标和中文文件名显示正常。右上角的数量被圆角切掉，已加大右边距。演示分区已删除，你的 `fences.json` 为空，壁纸照常播放。
- 未验证：桌面一直被窗口挡着，鼠标操作没有在真机上测：
  - 单击、双击、拖出、拖入、调整顺序；
  - 弹出右键菜单和分区菜单；
  - F2 重命名时键盘焦点能否进入 Explorer 下的子窗口；
  - 解锁后拖动和改大小。

  自检只验证了这些操作背后的逻辑和 Shell 调用。另外，自检不会真的关掉 Explorer，Explorer 重启只做了模拟。

待用户验收：
1. 托盘“桌面分区”里先建一个文件夹分区，例如指向“下载”。
2. 在分区里试双击打开、右键菜单、F2 重命名、拖进拖出。
3. 想试接管，就新建一个规则分区（例如“图片”），确认系统图标隐藏、其余项目出现在“其他”分区；删除规则分区或退出 AWall 后图标恢复。
4. 解锁布局后拖动、改大小，再锁定。

### 第 5.3 步：双击隐藏与按壁纸取色（已验收）

结果（2026-11-20，单显示器 2560×1440，125% 缩放）：

- 编译通过，只有两个既有警告。构建新增引用 `UIAutomationClient.dll` / `UIAutomationTypes.dll`（.NET Framework 自带）。
- `--test-fences` 的 29 项全部通过，其中 5.2 的 20 项照旧，新增 9 项：
  - **双击计时**：系统双击时间内点两次算双击；太慢、距离太远都不算；第三次点击不会和第二次再凑成一次双击。
  - **鼠标钩子**：能装上；关掉开关后卸载，再打开又装上。
  - **点在分区上**：不算空白桌面。
  - **干净桌面**：分区和图标都隐藏；期间刷新也不会把分区显示出来；恢复后分区重新挂好，图标回到原来的状态。
  - **取色**：
    - 深蓝底配亮蓝色块，选中色是蓝色，底色亮度低于 0.16；
    - 亮米色底配橙色块，选中色是橙色，卡片仍然是深色；
    - 灰色图片得到几乎不带色调的底色；
    - 没有图片时用默认配色。
  - **应用配色**：用 `PrintWindow` 读分区像素，和配色一致。渐变中途取到 `#221F24`，结束时 `#171D2C`，与目标一致。
  - **按当前壁纸取色**：测试时没有播放器，读的是系统壁纸 `img0.jpg`，得到带蓝色调的配色。
- 回归：`--test-desktop` 的 `success` 为 `true`（屏幕取色项被 Chrome 窗口挡住，跳过）；`--test-transitions` 11 项全部通过。
- 正式实例：动态壁纸 horizon-sky-wanderer 播放时建了一个演示分区。取色用的是播放器截的当前帧（`%LOCALAPPDATA%\AWall\theme-DISPLAY1.jpg`），分区得到偏灰绿的深色底：这张壁纸的平均色饱和度很低，草地的绿色是强调色。`desktop.clean on=on` 和不带参数切换回来都正常。演示分区已删除，AWall 已退出，和测试前一样。
- 未验证：
  - 用鼠标真的双击桌面空白处。桌面一直被窗口挡着，而且自检不往别的程序发模拟点击。
  - UI Automation 在真实图标上判断为 `ListItem`：只按 DeskFrame 的做法实现了，没有实测。
  - 实际换壁纸时的渐变观感。

用户验收（2026-11-20）：通过。原验收项：
1. 显示桌面后双击空白处，图标和分区一起消失；再双击恢复。双击图标仍然是打开文件，不会触发隐藏。
2. 建一个分区后切换几张不同色调的壁纸，看分区颜色是否跟着变。
3. 托盘“桌面分区”里的“双击桌面空白处隐藏图标和分区”可以关掉。
