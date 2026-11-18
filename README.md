# AWall 0.2 — MP4 / PKG

Windows 动态桌面壁纸与锁屏静态快照原型。不依赖 Wallpaper Engine 运行，不修改原素材。PKG 使用 WebWallGL（MIT）兼容渲染，并按需读取本机引擎共享纹理。

## Git 提交与推送

- **Agent 核实任务完成后，做一次本地提交。** 先检查暂存区，只提交本次任务的改动，并向用户报告提交哈希和验证结果。
- **提交信息写清做了什么。** 标题概括实际变更，正文说明主要改动、验证方式和结果。
- **收到用户对本次任务的明确验收确认后，再推送一次。** 核对目标远程分支和待推送的提交，执行 `git push`，并报告推送结果。

## 启动

双击 `AWall.cmd`，或运行 `bin/AWall.exe`。

1. 默认扫描 `E:\Steam\steamapps\workshop\content\431960` 中的 project.json，可通过“选择素材目录”更换目录。
2. 选择带 MP4 或 PKG 的素材，点击“设为动态桌面”。所有显示器播放同一素材。也可点“导入 MP4 / PKG”选择本地文件。
3. 画面加载后点击“同步锁屏快照”，从主显示器当前视频或场景生成 JPG，设置为当前用户锁屏图片。
4. 关闭窗口仅隐藏到托盘，托盘菜单可暂停、打开和退出。成功切换后保存布局，正常退出后再次启动会恢复；“停止壁纸”移除窗口并清除恢复布局。

使用 AWall 时请先从托盘退出 Wallpaper Engine，避免两个程序同时在桌面层渲染。

## 功能边界

- MP4：使用独立 mpv 子进程，静音循环、cover 填充，硬解首选 `auto-safe`；实际是否硬解依赖驱动及编码。通过 playback-restart 检测首帧，启动与首帧总上限为 30 秒。退出优先发送 IPC quit，有限等待后回收自有进程；Job Object在AWall异常终止时兜底回收mpv。
- 切换壁纸：并行捕获各屏旧画面，先显示独立桌面遮罩并暂停旧播放器，再在离屏窗口初始化新内容；真实首帧就绪后挂载桌面，全部显示器就绪后以 350ms 淡出遮罩。加载失败或 60 秒切换超时恢复旧壁纸；首次播放用系统静态背景遮罩。健康的同素材重复应用跳过重建。
- 首次遮罩支持系统背景色及常见图片布局；暂不完整还原按显示器分别设置的不同图片或 Spotlight。切换阶段耗时写入 `%LOCALAPPDATA%\AWall\transitions.log`。
- PKG：WebWallGL 3.0.2 + 本机 WebView2，按需启动，默认 30 FPS。解析真实场景、TEX 纹理、图层、粒子及兼容效果，不以预览图代替。
- `project.json` 指向不存在的 `scene.json` 而目录中包含 `scene.pkg` 时自动使用包；可扫描松散场景目录。
- PKG 为兼容渲染，**不保证任意特效、模型和 SceneScript 与官方完全一致**。状态栏显示警告数量，详情记录到逐场景日志。
- 网页和 application 壁纸仍未支持，目前优先 MP4 / PKG。
- 尚未接入系统音频频谱、Now Playing、鼠标点击。频谱输入为零，不以模拟数据冒充系统声音；鼠标位置约 20 Hz 推送给场景。
- 锁屏是静态截图，不是 Win+L 后播放视频。系统策略可能限制锁屏设置；失败会显示错误，不会修改系统策略。
- 自动暂停：会话锁定、任意显示器被前台全屏窗口覆盖。全屏暂停目前是全局策略。
- 多屏同源播放，显示器拓扑变化或 TaskbarCreated 通知后重建窗口；周期检查宿主健康。播放后端故障会清理失效批次并报告，允许重新应用。
- 成功布局保存在 `%LOCALAPPDATA%\AWall\wallpaper-layout.json`，包括素材与暂停设置；正常启动恢复，Stop 清除、Exit 保留。测试使用独立的 `wallpaper-layout-test.json`，不自动恢复正式布局。
- 不自动启用开机启动、不停止 Wallpaper Engine、不捆绑或上传壁纸素材。
- MP4 每屏使用独立 mpv，PKG 每屏使用 WebView2；切换素材或停止后释放播放器。尚未做多硬件性能基准。
- 场景脚本启用以提高兼容性；宿主不暴露本机对象，拦截外网、下载和权限请求。仅播放自己信任的本地素材。

## 构建

Windows PowerShell 中运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

使用系统 .NET Framework 4.x 的 x64 C# 编译器和 WPF。固定版本依赖已置于 `vendor/`，构建无需联网。机器需安装 WebView2 Runtime（本机已有）。许可见 `THIRD-PARTY-NOTICES.md`。

若 `bin/AWall.exe` 正在运行，可加 `-OutputDirectory bin-next` 构建到独立目录。


## 目录

- `src/AWall.cs`：UI、素材扫描、离屏播放器窗口、遮罩切换、布局恢复、暂停和快照。
- `src/DesktopHost.cs`：Lively 机制适配后的桌面宿主发现、健康检查、坐标转换和挂载排序。
- `src/MpvPlayer.cs`：MP4 子进程、named-pipe IPC、首帧、暂停、截图与进程回收。
- `src/ScenePlayer.cs`：WebView2 场景宿主、本地映射、共享纹理、诊断、暂停和截图。
- `src/scene/index.html`：PKG 渲染入口与消息桥接。
- `src/scene/video.html`：保留的旧视频页面资源，当前 MP4 路径不初始化它。
- `src/Set-LockImage.ps1`：WinRT 锁屏图片接口；先尝试 UserProfilePersonalizationSettings，拒绝时使用 LockScreen.SetImageFileAsync。
- `build.ps1`：生成 bin/AWall.exe 并复制锁屏脚本。
- 数据、日志和锁屏图片：`%LOCALAPPDATA%\AWall`。
- 自检：`bin\AWall.exe --self-test`。自检会暂时启动视频壁纸，8 秒后保存帧和结果并退出。
- 全素材自检：退出其他 AWall 实例后运行 `bin\AWall.exe --test-library`，自动遍历视频与 PKG，并在结束后退出。结果在 `%LOCALAPPDATA%\AWall\compatibility\results.json`，包含像素范围、两个时刻的画面是否变化、PKG 暂停状态、运行帧率、警告与截图。首帧成功不代表效果完全还原。
- 场景诊断：`%LOCALAPPDATA%\AWall\scene-<工坊ID>.log`；WebView2 缓存在 `%LOCALAPPDATA%\AWall\WebView2`。

素材版权属于原作者，仅从本机现有下载读取，不包含在源代码中。

正式启动入口：`AWall.cmd` 或 `bin/AWall.exe`。PKG 使用独立 WebView2 环境，当前不做环境预热复用。GitHub 源码不包含本机素材、研究副本及来源尚未完整核实的 mpv 二进制；本机编译需在 `vendor/mpv` 放置所需运行文件，来源见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。

