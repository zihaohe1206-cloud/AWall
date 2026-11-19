# AWall

AWall 是 Windows 动态桌面壁纸工具，支持 MP4、Wallpaper Engine 的 PKG 场景和单文件 HTML 壁纸。可为每块显示器选择壁纸、设置轮播，并把主显示器当前画面设为静态锁屏图片。运行 AWall 前请从托盘退出 Wallpaper Engine，避免两个程序同时渲染桌面。

- [开发与 AI 协作](#开发与-ai-协作)
- [启动与使用](#启动与使用)
- [构建与验证](#构建与验证)
- [项目目录](#项目目录)

## 开发与 AI 协作

**调研和计划默认在对话里交付。需要写文件时，先找现有文档，有同一主题就继续更新。** 开始工作前读本节，查看 `git status --short`，并搜索 `docs/` 和本地 `research/` 中已有的同主题成果。工作区可能包含其他人的未提交改动，修改时要保留这些工作。

| 内容 | 存放位置 | 写法 |
| --- | --- | --- |
| 临时调研、计划、排查过程 | 当前对话 | 直接给出结论、依据、实施步骤或待确认事项。 |
| 临时笔记、下载、实验和参考副本 | `research/` | 需要落盘时放在对应主题目录，例如 `research/notes/<主题>/`。该目录已被 Git 忽略。 |
| 需要进入版本库的调研、计划或跨会话交接 | `docs/tasks/<topic>.md` | 只有明确要求文件交付、需要后续接手或团队需要审阅时才建。每个任务只维护一份，以小写英文短横线命名，例如 `wallpaper-switch.md`。 |
| 长期使用说明、架构与技术约定 | README 或 `docs/` 中现有的相关文档 | 更新原位置。确实需要独立的长期文档时，在 `docs/` 新建并从本 README 链接。 |
| 手动保存的测试日志、截图和复现产物 | `test-artifacts/` | 该目录已被 Git 忽略。任务文档只记必要的复现步骤、验证结果和结论。 |

- 同一任务按需在原文件中记录**目标与状态、调研依据、实施计划、验证和未完成项**。调研、方案、执行记录和验收都可以是同一页里的小节。迭代时直接改这一页，版本历史由 Git 保存。
- 任务文档中把计划、已实现和已验证的内容标清楚。任务结束后，把仍适用的说明合并到 README 或现有专题文档，再更新原任务页的状态和验证结果。
- 根目录用于项目入口和仓库级文件，`src/` 用于实现与运行资源，`tools/` 用于工具脚本。调研和计划按上表归位。检查改动时，确认每份新增文档都有独立用途，且同一结论只有一个维护位置。
- 改代码时以 `build.ps1` 使用的分层源码为准。开发约定和命令表见 [架构文档](docs/ARCHITECTURE.md)；桌面美化尚未实现的设计与路线图见 [桌面美化任务](docs/tasks/desktop.md)。

### 何时移入 `垃圾/`

- 清理时，将已合并或被新版替代的旧稿、重复副本，以及用途已结束的一次性材料移入 `垃圾/`。移动前确认独有信息已有保留位置，并检查是否还有文档、脚本或构建过程依赖原路径。
- 仍在维护但放错位置的文档按上表归位，并更新引用。已完成的正式任务页可留在 `docs/tasks/`，供后续查阅状态和验证记录。第三方许可、运行资源及脚本的固定输出随各自的组件或流程存放。
- 归档只移动文件，保留原来的相对路径，例如 `垃圾/2026-11-18-文档整理/bin/commit-msg.txt`。遇到同名归档时使用新的目录。`垃圾/` 是本地恢复区，已被 Git 忽略。

## Git 提交与推送

- **Agent 核实任务完成后，做一次本地提交。** 先检查暂存区，只提交本次任务的改动，并向用户报告提交哈希和验证结果。
- **提交信息写清做了什么。** 标题概括实际变更，正文说明主要改动、验证方式和结果。
- **收到用户对本次任务的明确验收确认后，再推送一次。** 核对目标远程分支和待推送的提交，执行 `git push`，并报告推送结果。

## 启动与使用

双击 `AWall.cmd`，或运行 `bin/AWall.exe`。桌面快捷方式可直接指向 `bin/AWall.exe`，以显示 AWall 图标。

1. 点击“导入本地文件”导入 MP4、PKG 或 HTML，或点击“从 Wallpaper Engine 导入”选择工坊里的 MP4 / PKG。工坊导入会按工坊 ID 或文件 SHA-256 标记重复项，并可复制缺少的共享资源。
2. 选中素材，点击“设为动态桌面”。多屏时在“应用到”中选择全部显示器或指定屏幕。
3. 画面加载后可点击“同步锁屏快照”，将主显示器当前帧设为静态锁屏图片。Windows 策略可能限制此操作。

关闭窗口后程序驻留托盘。单击托盘图标或再次启动 AWall 可唤出窗口。托盘菜单支持暂停、继续、下一张、停止、隐藏 / 显示桌面图标和退出。图标只在 AWall 运行期间隐藏，退出、Explorer 重启或异常退出后再次启动时都会恢复。正常退出会保留已应用的布局，下次启动时恢复。“停止壁纸”会清除该布局。

在主界面可以开启轮播、设置顺序和间隔、开启登录时启动，或切换浅色、深色和跟随系统。轮播列表为空时使用整个素材库。手动切换会重新计时，暂停、锁屏或被全屏窗口遮挡时会延后轮播。开机启动默认关闭。

### 命令行控制

AWall 运行时，可以用 `bin/AWallCmd.exe` 从终端、脚本或 agent 执行和按钮相同的命令（命令表见[架构文档](docs/ARCHITECTURE.md#6-命令层)）：

```powershell
.\bin\AWallCmd.exe help
.\bin\AWallCmd.exe wallpaper.status
.\bin\AWallCmd.exe wallpaper.apply file=三维点阵海浪 display=1
```

`--json` 输出完整 JSON（中文转义为 `\uXXXX`，适合脚本）；Git Bash 等 UTF-8 终端加 `--utf8`。退出码：0 成功，1 命令失败，2 AWall 未运行，64 用法错误。`AWall.exe --cmd <命令> ...` 效果相同，但它是窗口程序，PowerShell 和 cmd 不会等它结束，只适合输出被重定向的场合。

### 素材与兼容性

本地素材库位于 `library/wallpapers/`，下设 `mp4/`、`pkg/`、`html/` 和 `image/`。可放置同名 `.jpg`、`.gif` 或 `.png` 作为预览图，PKG 可附带同名 `.json` 保存原 `project.json`。旧版 `library/wallpaper-engine/` 结构可用 `tools/Migrate-Library.ps1` 整理，默认预览，加 `-Apply` 才执行。

- **MP4** 默认使用 WebView2 内置播放器，也可在包含 mpv 的构建中切换播放器。
- **PKG** 使用 WebWallGL 和 WebView2 兼容渲染，部分特效、模型和 SceneScript 可能与官方引擎有差异。需要 Wallpaper Engine 共享资源时，将其安装目录下的 `assets` 复制到 `library/we-assets/`。
- **HTML** 使用 WebView2 渲染，默认限制至 30 FPS。支持通过 `awall-properties` 声明可调参数。仅允许从 jsDelivr、unpkg 和 cdnjs 加载指定类型的库文件，首次下载后缓存在 `library/web-cache/`。格式、运行约束和示例见 [AI 网页壁纸生成指南](docs/AI-WALLPAPER-PROMPT.md)。
- **图片**（jpg / jpeg / png / bmp）直接设为 Windows 系统壁纸，AWall 不参与渲染，也不占资源。导入时复制到 `library/wallpapers/image/`。
- **只需几分钟变一次的网页**（数据图、日历之类）在 `<head>` 里加 `<meta name="awall-present" content="interval=300">`：AWall 每 300 秒（最少 60 秒）在后台渲染一张图，设为系统壁纸后立即释放渲染器。`content="static"` 表示只渲染一次。
- 每块显示器各自选择呈现方式，可以一块屏播视频、另一块屏用图片。
- 当前仅支持静态锁屏快照。`application` 类壁纸、系统音频频谱、Now Playing 和鼠标点击尚未接入。全屏自动暂停当前按全局处理。

素材、缓存和共享资源留在本机，`library/` 已被 Git 忽略。请使用自己信任的素材。

## 构建与验证

在项目根目录运行以下命令。构建使用 Windows 自带的 .NET Framework 4.x x64 C# 编译器和 WPF，主要依赖在 `vendor/`。运行需要 WebView2 Runtime。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

- 默认输出为 `bin/AWall.exe`。如果它正在运行，可用 `-OutputDirectory bin-next` 生成独立构建。
- 需要 mpv 时，先将其运行文件放在 `vendor/mpv/`，再加 `-WithMpv` 构建。依赖来源与许可见 [第三方声明](THIRD-PARTY-NOTICES.md)。
- 修改图标后，可用 `assets/Draw-Icon.ps1` 或 `python assets/draw_icon.py assets` 重新生成图标。

退出其他 AWall 实例后，从项目根目录运行所需的自检。自检会实际播放壁纸，结果写入 `%LOCALAPPDATA%\AWall`。

| 命令 | 验证内容 | 结果文件 |
| --- | --- | --- |
| `.\bin\AWall.exe --self-test` | 基本播放、截图和短时资源采样 | `selftest.txt`、`test-frame.jpg` |
| `.\bin\AWall.exe --test-library` | 逐个检查支持的素材，记录画面、帧率和资源占用 | `compatibility/results.json`、`compatibility/perf-summary.txt`、`compatibility/perf-summary.json` |
| `.\bin\AWall.exe --test-transitions` | 使用素材库中的 MP4 和 PKG 验证切换、失败回滚和布局恢复，各需至少一项 | `transition-test.json` |
| `.\bin\AWall.exe --test-desktop` | 桌面块的挂载、揭开、透明背景实验，以及图标显隐和异常退出恢复。会短暂隐藏桌面图标；没有全屏程序时会切换两次“显示桌面” | `desktop-test.json` |

`--test-library` 支持 `--perf-seconds=N` 调整每项采样时间。测试 mpv 时可以加 `--video-backend=mpv`，并运行包含 mpv 的构建。首帧和截图可用于定位播放问题，特效还原程度需要对照原场景检查。

## 项目目录

| 路径 | 内容 |
| --- | --- |
| `src/App/`、`src/Core/` | 进程入口、路径、命令与命令管道、素材库、设置和布局模型 |
| `src/Cli/` | 命令行前端 `AWallCmd.exe` 的入口（单独编译） |
| `src/Platform/`、`src/Render/` | Windows 桌面挂载和系统设置、播放器、切换遮罩及 `src/Render/scene/` 页面资源 |
| `src/Features/`、`src/UI/` | 壁纸参数、桌面美化模型、主窗口、托盘等界面 |
| `src/Diagnostics/` | 自检和资源采样 |
| `docs/`、`tools/` | 长期文档、需保留的任务文档、开发工具脚本 |
| `assets/`、`vendor/` | 项目图标及生成脚本、构建依赖 |
| `library/`、`research/`、`test-artifacts/` | 本机素材、研究资料、测试产物，均由 Git 忽略 |

`build.ps1` 编译上述 `src/` 分层目录中的 C# 文件，并复制 `src/Render/scene/` 的页面资源。`src/` 顶层还留有重构前的副本，日常开发请修改对应的分层文件。

应用设置、布局和运行日志位于 `%LOCALAPPDATA%\AWall`。切换时序见 `transitions.log`，命令执行见 `commands.log`（命令管道自身的错误见 `command-pipe.log`），场景及网页诊断分别见 `scene-*.log` 和 `web-*.log`。
