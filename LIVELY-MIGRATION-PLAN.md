# Lively 启动与切换机制逆向、AWall 迁移计划

状态：已实施并覆盖正式入口，当前机器单屏验收通过。日期：2026-11-16。本文保留逆向证据、完整计划、适配差异和实际验证边界。

## 1. 目标与移植边界

替换 AWall 现有的桌面宿主发现、播放器启动挂载、切换提交及恢复机制，采用 Lively 的“创建播放器 → 等待窗口可用 → 配置窗口 → 挂载桌面 → 注册活动实例”的分层生命周期。覆盖现有入口，不保留另一个可供用户误选的旧流程。

用户明确要求保留 AWall 的切换遮罩，因此保留每屏旧画面快照、首次静态背景、旧实例暂停、所有显示器真实首帧屏障、350ms 淡出、60 秒失败回滚及连续请求取消。Lively 原版先关闭旧实例，不能直接提供 AWall 的实例级回滚；该步骤必须适配。

本任务的内容类型为 AWall 已支持的 MP4 和 PKG。MP4 保留 mpv 后端；PKG 保留 WebView2/WebWallGL。Lively 的 WebView2 播放器支持网页，不能替代 Wallpaper Engine PKG 兼容渲染。这里的移植指上述启动、挂载、切换与恢复方案，不声称包含 Lively 的所有播放器、UI、RPC、音频输入、HDR、应用壁纸、屏保或在线壁纸功能。

## 2. 目标身份与证据

实际分析目标为本机 `C:\Users\18316\Desktop\Lively Wallpaper\Lively.dll`，759808 字节：

- 程序集版本：2.2.1.0，x86_64，.NETCoreApp v9.0，CIL。
- SHA-256：`ca73583755c8fc381cdda674bfbc3c60fea0bf924dcb06f846083c0b30f354ad`。
- MVID：`132f72e5-047c-4397-9fed-876a4eb08716`。
- REA 程序集身份：`ev_6754ca6b9206907b4e03a8262909a4f3003285a27447143111ae1d38b476c37b`（主调查会话）。
- REA 全程序集成员/CIL/静态调用边：`ev_9010c0290c5580b604342ad4d33fd6ae35d9c9c3de1482215b31336e8c504998`。
- REA 四个重点类型成员：`ev_78658d38389972666899bce7ca4e996baffbfef533bc65747e7dfb304de64d27`。
- 上述重点成员的精选方法页：`ev_2e12efa8949fe9ac04a0c8f8fcd3a0bfb3f903d4ecfe8053023c8807d8a96c91`。
- 主调查证据包：`test-artifacts/lively-rea-primary-evidence.json`，包含上述主会话程序集身份与全程序集CIL证据。
- 重点调查证据包：`test-artifacts/lively-rea-evidence.json`。证据包是忽略目录中的本机研究产物，不作为应用依赖或公开源码内容。

交叉参考源码为 `research/lively`，提交 `25a0a5f4ed1988ff42117d3a17feb321062237fe`。重点文件：

- `src/Lively/Lively/Core/WinDesktopCore.cs`。
- `src/Lively/Lively/Core/Wallpapers/VideoMpvPlayer.cs`。
- `src/Lively/Lively/Core/Wallpapers/WebWebView2.cs`。
- `src/Lively/Lively/Factories/WallpaperPluginFactory.cs`。

证据分类：REA 读取实际文件的元数据、CIL 指令和调用边属于静态观察；下文将其还原为生命周期属于推导；研究源码仅用于解释与交叉核对。没有通过静态分析观测 Lively 实际执行，也没有把源码检出版本假定为安装二进制的精确版本。方法 token 和 IL 偏移仅对上述 SHA/MVID 有效。

## 3. Lively 的实际方案

### 3.1 桌面层初始化

`WinDesktopCore.SetupDesktopLayer`（token `0x06000335`）的 CIL 显示：

1. 获取 Progman，检查扩展样式 `0x00200000`（WS_EX_NOREDIRECTIONBITMAP），识别 raised desktop。
2. 向 Progman 发送 `0x052C`，wParam=`0xD`，lParam=`1`，超时 1000ms；这些常量在 IL77–103 可见。
3. 枚举顶层窗口，找有 `SHELLDLL_DefView` 子窗口的窗口，再找其后的 WorkerW。
4. raised desktop 分支改取 Progman 的 WorkerW 子窗口，并保留 DefView 句柄用于层级排序。
5. 桌面层初始化完成后发出重置事件。Windows 7 分支使用 Progman，但本次 AWall 验收以当前 Windows 为准。

`TrySetWallpaperPerScreen`（`0x0600033b`）先按显示器屏幕矩形定位，再 `MapWindowPoints`（IL197）转换坐标，调用 `TryAttachToDesktop`（IL205），最后按新的父窗口坐标定位。`SetParent` 与定位是可单独失败的步骤，不能用“内容已加载”替代“挂载成功”。

`TryAttachToDesktop`（`0x06000352`）在 legacy 桌面将壁纸挂到 WorkerW。安装二进制的 raised 分支设置 WS_CHILD 和透明度 255，挂到 Progman，排在 DefView 后，再确保 WorkerW 位于底部。

**版本差异：**研究源码额外提供 `!isWorkerWParent` 分支条件，安装二进制 CIL 没有该条件。AWall 若选择 raised WorkerW 子宿主，以让 WPF、WebView2 子窗口与独立遮罩共享可控的 Z 顺序，这是明确的宿主适配，不能宣称与安装二进制的 raised 路径逐指令一致。

### 3.2 启动和替换的顺序

`SetWallpaperAsync` 的真正逻辑在异步状态机 `<SetWallpaperAsync>d__38.MoveNext`（`0x06000611`），其关键调用为：

| 阶段 | CIL 调用点 | 观察与含义 |
| --- | --- | --- |
| 加载互斥 | IL29 `SemaphoreSlim.WaitAsync` | 串行进入生命周期管理，finally IL2014 释放 |
| 校验 | IL228 `File.Exists` | 本地入口缺失直接报告错误 |
| 关闭旧内容 | per IL461 `CloseWallpaper`；duplicate IL1132 `CloseAllWallpapers` | 原版先释放旧播放器 |
| 工厂 | per IL502；duplicate IL1217 `CreateWallpaper` | 按内容类型及配置选后端 |
| 生命周期事件 | per IL530/553 | 订阅 Exited、Loaded |
| 播放器窗口启动 | per IL564；duplicate IL1279 `ShowAsync` | await 窗口可用 |
| 挂载桌面 | per IL677；duplicate IL1392 `TrySetWallpaperPerScreen` | 窗口可用后才执行桌面嵌入 |
| 进程登记 | per IL773 `watchdog.Add`；IL790 活动列表 Add | 挂载之后跟踪进程和活动实例 |
| 多屏协调 | duplicate IL1637/1650 | 非主屏静音，并设置播放位置以尝试同步 |

span 分支同样先 CloseAll（IL802），ShowAsync（IL905），再 TrySetWallpaperSpanScreen（IL1007）。本次 AWall 沿用“所有显示器同源”的现有语义，不增加 span/per 的新产品选项。

异常分支报告错误并 Dispose 当前创建的实例。Lively 在 attach 返回 false 后主要记日志；AWall 为保证遮罩提交可靠，应把挂载失败升级为事务失败，禁止淡出到未正确挂载的窗口。

### 3.3 mpv 启动、Loaded 和退出

`VideoMpvPlayer` 构造器配置独立进程和随机 named pipe：静音启动、循环、keep-open、离屏 geometry、force-window、初始最小化、不抢焦点、不阻止屏保、关闭默认输入和 OSC、硬解首选 auto-safe、隔离用户配置。

真正启动在 `<ShowAsync>d__56.MoveNext`（`0x06000638`）：Process.Start（IL97）→ BeginOutputReadLine（IL131）→ WaitForProcesWindow（IL161）→ 保存 Handle（IL265）→ 去边框（IL308）→ 隐藏任务栏（IL319）→ 应用属性（IL331）→ 短暂 Delay（IL338）→ IsLoaded（IL433）→ Loaded 事件（IL456）。

这证明 **Lively 的 mpv Loaded 表示窗口和初始化属性已可用，不能据此证明某帧已真正呈现**。AWall 应保留现有 `awall-ready.lua` 的 playback-restart 首帧门槛，并区分 HWND 可用与内容 ready。不要简单复制其 69ms 延迟当作首帧判断。

`<Close>d__48.MoveNext`（`0x06000634`）先取消窗口等待，等待其结束，再通过 IPC 发送 quit。正常 Pause/Play 通过 JSON named pipe 改 pause 属性。AWall 应给正常退出设置有限等待，失败后结束本程序拥有的进程，并在异步初始化取消后拒绝迟到回调。

### 3.4 WebView2 的两阶段就绪

`WebWebView2` 是独立播放器进程；`<ShowAsync>d__58.MoveNext`（`0x06000654`）启动进程后等待 `tcsProcessWait`。输出消息处理区分 hwnd 消息与 wploaded 消息，前者使窗口等待完成，后者触发 Loaded/contentReady。

AWall 的 PKG 使用进程内 WebView2 控件和 WebWallGL，无法原样复制 Lively 的网页进程协议。应移植其“窗口可挂载”和“内容就绪”分离原则：先离屏创建渲染窗口，真实场景 ready 后再挂桌面，保持每个活跃实例独立的 WebView2 profile，避免旧实例暂停影响新实例。

### 3.5 桌面销毁和 Explorer 重建

`<WorkerWHook_EventReceived>d__37.MoveNext`（`0x06000619`）处理宿主销毁：raised 路径重新 SetupDesktopLayer、重新排序并 EnsureWorkerWZOrder；其他路径 ResetWallpaperAsync。

`<WndProc_TaskbarCreated>d__68.MoveNext`（`0x06000617`）获取 Explorer PID（IL43），确认进程身份变化后按崩溃间隔执行 ResetWallpaperAsync（IL190 等），防止把所有 TaskbarCreated 都当成 Explorer 崩溃。

AWall 应监听 TaskbarCreated，主动使宿主缓存失效，同时保留周期健康检查。重建时检查父窗口身份、DefView/WorkerW 拓扑、真实 GetParent 和窗口可用性；对于被 Explorer 连带销毁的播放器 HWND，重新创建播放器，不能只反复 SetParent 一个失效窗口。

### 3.6 启动恢复和布局保存

REA 全程序集调用边还观察到 `Lively.App..ctor` 在 IL774 调用 `IDesktopCore.RestoreWallpaper`；WinDesktopCore 构造器订阅 `SetupDesktop_WallpaperChanged`，该处理器在 IL1 调用 `SaveWallpaperLayout`。研究源码对应逻辑将活动壁纸的显示器身份及库路径写入 WallpaperLayout，duplicate/span 恢复时读取首个布局项并向主屏发起 SetWallpaperAsync；per 则按显示器匹配恢复，缺失显示器可留待重连。

AWall 应移植成功状态持久化和启动恢复的闭环，按现有全屏同素材模型简化为 `%LOCALAPPDATA%\AWall\wallpaper-layout.json`：保存 Item、手动暂停和全屏自动暂停设置。仅在成功淡出提交后写入新布局；正常模式启动读取并走同一切换入口；测试模式不自动恢复或污染用户布局。用户显式 Stop 清除布局，退出保留最后成功布局。恢复失败记录并允许手动重试，不在后台无限重建，也不把尚未成功的新素材覆盖到旧成功布局。

## 4. AWall 的替换设计

### 4.1 状态与职责

- `DesktopHost`：独立负责宿主发现、缓存有效性、raised/legacy 分支、坐标转换、checked 挂载/排序与诊断。
- `Player`：负责离屏 HWND 与后端的创建、窗口就绪、真实内容就绪、桌面挂载、呈现及释放；窗口消息不得把 staging 窗口意外抬到前景。
- `MainWindow`：负责请求版本、素材选择、遮罩事务、多屏提交、回滚与系统事件；不再自行猜测 Progman/WorkerW。
- `ScenePlayer`/`MpvPlayer`：保留各自内容引擎，向上提供 ready/failed/pause/capture/dispose；后台进程退出必须反馈失败。

每次切换保存稳定旧批次、旧素材与旧手动暂停状态。新批次仅能在自己的版本仍有效时推进。所有关闭操作应使事件回调失效；异步 capture/initialize 完成后先检查版本再触碰共享集合。

### 4.2 切换事务

1. 验证入口；健康的同素材且显示器身份/尺寸一致时跳过重建。
2. 取消未完成的旧请求，恢复其稳定批次；分配新的 version。
3. 并行捕获所有旧显示器画面；首次启动读取系统静态背景。捕获期间保持旧播放器可用。
4. 每屏显示独立快照遮罩，等待遮罩完成一次呈现后暂停旧播放器。
5. 新播放器在独立、无激活、无任务栏的离屏 staging HWND 初始化，尚未成为桌面子窗口；为 WebView2 保留可渲染尺寸与独立 profile。
6. 新内容报告真实首帧后，核验其 HWND、checked 挂载桌面并定位；挂载失败立即回滚。
7. 全部显示器新内容就绪且挂载成功后，一次提交批次；先提升新窗口，再提升所有遮罩，应用暂停策略。
8. 以 350ms 淡出整个遮罩 HWND，确保 WebView2/mpv 子窗口也被遮挡。
9. 淡出成功后销毁遮罩和旧实例，登记新稳定批次，清理计时器。
10. 任一错误、进程早退或 60 秒超时：关闭新批次，提升并恢复旧批次及原暂停状态，移除遮罩；首次启动失败则恢复系统桌面。
11. Stop/Exit 递增 version，关闭所有新旧批次与遮罩，不允许迟到截图或 ready 重建窗口。

这保留 AWall 的遮罩逻辑，同时用 Lively 的启动后挂载方案覆盖原来的“新窗口在 SourceInitialized 立即入桌面再开始加载”路径。旧实例延迟关闭和真实首帧屏障属于用户要求下的必要增强。

### 4.3 恢复与资源边界

- TaskbarCreated 与健康检查汇合到受版本保护的恢复路径，避免系统事件在切换期间重复创建批次。
- 拓扑身份包含设备名、Bounds 和主屏标识；拓扑改变后取消当前事务并按新屏幕重建。
- 正常播放后的后端故障应清理失效批次、清楚报告并提供可重试状态，使同素材重新应用能够重建；不得把 Failed 窗口当成健康实例。
- 新旧 WebView2 不复用活跃 profile；Dispose 后释放 profile 占用。
- mpv 只控制本程序拥有的进程，不停止用户的 Lively/Wallpaper Engine。
- 单一退出等待任务回收Process；正常释放后台IPC quit后限时Kill；归属Job Object配置KILL_ON_JOB_CLOSE覆盖AWall异常终止。
- 宿主未就绪的恢复失败保留独立目标，按2秒递增、最多10秒间隔做5次重试；TaskbarCreated重置次数。Stop清除恢复目标；预览素材不改变恢复目标。
- 退出不改系统静态壁纸和锁屏；锁屏仍由用户显式调用原有功能。
- 移植按机制重写，不把整个 Lively 程序集作为运行依赖；引用来源与已有 mpv 运行文件许可材料在 THIRD-PARTY-NOTICES 中说明。

## 5. 文件实施顺序

| 顺序 | 文件 | 具体替换 |
| --- | --- | --- |
| 1 | `src/DesktopHost.cs` | 新的宿主发现、健康验证、坐标转换与checked挂载实现 |
| 2 | `src/AWall.cs` 的 Native/Player/TransitionCover | 删除旧宿主发现实现，统一调用 DesktopHost；Player 改为离屏加载后挂载；遮罩仍使用独立桌面 HWND |
| 3 | `src/AWall.cs` 的 ApplyAsync/系统事件 | 调整捕获与启动顺序、批次提交、取消、恢复、拓扑身份 |
| 4 | `src/MpvPlayer.cs`、必要时 `src/ScenePlayer.cs` | 生命周期取消、错误传播与进程回收；保持首帧和暂停协议 |
| 5 | `build.ps1` | 编译新源文件并生成正式 `bin/AWall.exe` |
| 6 | README/AI_TECHNICAL_CONTEXT/THIRD-PARTY-NOTICES | 删除已过时的播放说明，记载最终实现及实测边界 |
| 7 | 现有自检入口 | 运行构建、切换和有代表性的播放回归；仅在有具体缺口时补充生命周期测试 |

## 6. 验收与失败判据

| 场景 | 必须成立 | 验证方式 |
| --- | --- | --- |
| 编译 | 正式构建无错误，使用最新页面和新宿主源文件 | `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1` |
| 首次MP4 | 启动背景遮罩先出现；内容ready、父窗口正确；淡出后无遗留遮罩 | `--test-transitions` 加诊断 |
| MP4→PKG→MP4 | 每屏旧快照在新内容加载期间保持；旧内容暂停；成功后旧实例关闭 | `--test-transitions` |
| 同素材重复应用 | 健康状态跳过重建；故障或拓扑变化时允许重建 | 现有/补充集成断言 |
| 启动恢复 | 正常退出后再次启动恢复最后成功素材及暂停设置；Stop 后不自动恢复 | 布局持久化和正常模式启动检查 |
| 损坏素材 | 不淡出到空内容，恢复相同旧实例和暂停状态 | 损坏 MP4 回滚断言 |
| 连续切换 | 最新请求胜出，无过时回调替换壁纸 | rapid-switch 断言 |
| 截图中Stop | 不因异步完成再次创建窗口 | stop-during-capture 断言 |
| 进程回收 | Stop/退出后无本次测试所属 mpv/播放器残留 | PID/父子关系检查 |
| 桌面挂载 | 内容ready不能掩盖SetParent/SetWindowPos失败 | GetParent 与诊断断言 |
| 视频回归 | 截图非纯色、暂停后时间停止、恢复和结尾循环 | 代表性 `--test-library --only=...` |
| PKG回归 | 首帧、截图非纯色、暂停/恢复与渲染统计可用 | 代表性scene自检 |
| Explorer恢复 | 宿主失效后能重建，遮罩无孤儿，恢复后坐标正确 | 恢复路径集成测试；真实Explorer重启单列验证 |
| 多屏/混合DPI | 所有屏幕一起提交；负坐标和不同缩放不偏移 | 仅在实际硬件具备条件时确认 |

日志应能区分 capture、covers-ready、backend-ready、attached、all-content-ready、fade、complete、rollback，以及宿主重建和后端退出。任何挂载失败、未就绪提交、遮罩遗留、旧进程泄漏或回滚丢失均为不通过。

## 7. 当前未知与结论限制

- REA 本轮只做文件静态分析；没有记录 Lively 的动态窗口轨迹、耗时或实际GPU路径。
- Lively 的窗口ready与AWall真实首帧ready含义不同；性能和视觉效果不能从调用边推定。
- raised WorkerW 子宿主与本机安装版直接 Progman 挂载存在适配差异，应以AWall实测结果判断，不掩盖版本差异。
- 实机只有当前显示器拓扑时，不能把测试通过扩展到所有多屏、混合DPI、HDR和其他Windows版本。
- PKG首帧或非纯色截图不证明所有场景特效完全兼容。
- 实施完成后，应在本文末尾补充构建路径、实际执行检查、结果文件和仍待验证项；未执行项目保持明确待验证。

## 8. 实施结果与实际验收（2026-11-16）

### 8.1 已覆盖的正式实现

- 正式入口仍是 `AWall.cmd` → `bin/AWall.exe`，没有新旧流程选择开关。
- 删除原Native桌面发现和Player提前挂载路径，统一使用新DesktopHost验证、定位、挂载和调整层级。
- 遮罩先建立并冻结旧实例；新Player离屏初始化，全部内容就绪后核验挂载；350ms淡出完成才退役旧实例。
- 保留快照、首次静态背景、首帧屏障、60秒事务回滚及请求版本保护。MP4继续mpv，PKG继续WebWallGL。
- 保存最后成功布局及暂停设置；正式与测试布局文件隔离；正常Exit保留，Stop清除。
- TaskbarCreated、显示器身份变化、窗口失效使用明确恢复目标；回滚始终清理pending和covers，保留健康旧批次的回调身份。
- mpv启动立即监测退出，启动与首帧共享30秒墙钟截止；停止取消等待，正常IPC quit后必要时Kill；Job Object兜底回收异常终止后的自有进程。

### 8.2 执行结果

当前环境为DISPLAY1，2560×1440，raised desktop；以下结论仅覆盖本机当前环境。

| 检查 | 结果 | 本地结果 |
| --- | --- | --- |
| 正式构建 | 通过，无编译错误或警告 | `bin/AWall.exe` |
| 首次播放及GetParent核验 | 通过 | `test-artifacts/lively-migration-transitions.json` |
| 同素材健康重复应用不重建 | 通过 | 同上 |
| 视频→场景、旧实例冻结、淡出、遮罩销毁 | 通过 | 同上 |
| 场景→视频 | 通过 | 同上 |
| 宿主重建不误用预览素材 | 通过 | 同上 |
| 布局与手动暂停恢复 | 通过 | 同上 |
| 销毁播放器HWND后的重建 | 通过 | 同上 |
| 无活动实例时使用保留的壳层恢复目标 | 通过 | 同上 |
| 损坏MP4达到启动截止后恢复原实例 | 通过 | 同上 |
| 快速连续切换 | 通过 | 同上 |
| 截图过程中Stop | 通过 | 同上 |
| MP4：2070877392 | 非纯色、帧变化、暂停时间停止及结尾循环通过 | `test-artifacts/lively-migration-playback.json` |
| PKG：1400240127 | 首帧、非纯色、帧变化、暂停/恢复通过；诊断警告0 | 同上 |
| 正常播放后强制结束自有mpv | 已检测并清理活动壁纸，显示错误信息 | `test-artifacts/lively-migration-backend-exit.json` |
| 强制结束测试AWall | 自有mpv由Job Object回收，无遗留 | `test-artifacts/lively-migration-owner-crash.json` |

命令：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
.\bin\AWall.exe --test-transitions
.\bin\AWall.exe --test-library --only=2070877392,1400240127
```

进程故障检查只操作本轮启动的测试AWall及其明确父子关系的mpv。PKG截图已目视确认有实际场景画面，这不等于全部官方特效还原。测试结束无本轮AWall或mpv遗留。最终构建SHA256：`EFD93D4DB3503EE4DB799EF3929F0FC6358E0A4A03562CF932435A18247FC740`。

### 8.3 仍待实际环境验证

- 没有强制重启用户Explorer。已测窗口销毁、宿主重新发现和保留恢复目标，完整壳层重启及宿主暂不可用时序仍待实测。
- 布局恢复调用与暂停设置已在测试模式同一进程验证；正式模式跨进程自动恢复尚未单独运行。
- 未验证真实多屏、混合DPI、负坐标拓扑、HDR、Windows旧版或不同GPU驱动。
- 未动态跟踪Lively；逆向来自实际程序集的静态CIL和明确版本研究源码交叉核对。
- 全素材兼容性、专有效果不在本轮代表性回归结论内。
