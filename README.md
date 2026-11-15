# AWall 0.2 — MP4 / PKG

Windows 动态桌面壁纸与锁屏静态快照原型。不依赖 Wallpaper Engine 运行，不修改原素材。PKG 使用 WebWallGL（MIT）兼容渲染，并按需读取本机引擎共享纹理。

## 启动

双击 `AWall.cmd`，或运行 `bin/AWall.exe`。

1. 默认扫描 `E:\Steam\steamapps\workshop\content\431960` 中的 project.json，可通过“选择素材目录”更换目录。
2. 选择带 MP4 或 PKG 的素材，点击“设为动态桌面”。所有显示器播放同一素材。也可点“导入 MP4 / PKG”选择本地文件。
3. 画面加载后点击“同步锁屏快照”，从主显示器当前视频或场景生成 JPG，设置为当前用户锁屏图片。
4. 关闭窗口仅隐藏到托盘，托盘菜单可暂停、打开和退出。“停止壁纸”移除 AWall 窗口。

使用 AWall 时请先从托盘退出 Wallpaper Engine，避免两个程序同时在桌面层渲染。

## 功能边界

- MP4：使用 WebView2 视频控件进行硬件加速、静音循环播放，首帧显示后报告就绪；解码格式依赖 WebView2 支持的编码。加载失败或超时会显示错误。
- 切换壁纸：并行捕获各屏旧画面为内存快照，用独立桌面遮罩保持画面；暂停旧播放器，在下层加载新壁纸，全部显示器首帧就绪后以 350ms 淡出遮罩。加载失败或 60 秒超时恢复旧壁纸；首次播放用系统静态背景遮罩等待新内容。健康的同素材重复应用跳过重建。
- 首次遮罩支持系统背景色及常见图片布局；暂不完整还原按显示器分别设置的不同图片或 Spotlight。优化执行记录和未验证项见 `OPTIMIZATION-PLAN.md`，切换阶段耗时写入 `%LOCALAPPDATA%\AWall\transitions.log`。
- PKG：WebWallGL 3.0.2 + 本机 WebView2，按需启动，默认 30 FPS。解析真实场景、TEX 纹理、图层、粒子及兼容效果，不以预览图代替。
- `project.json` 指向不存在的 `scene.json` 而目录中包含 `scene.pkg` 时自动使用包；可扫描松散场景目录。
- PKG 为兼容渲染，**不保证任意特效、模型和 SceneScript 与官方完全一致**。状态栏显示警告数量，详情记录到逐场景日志。
- 网页和 application 壁纸仍未支持，目前优先 MP4 / PKG。
- 尚未接入系统音频频谱、Now Playing、鼠标点击。频谱输入为零，不以模拟数据冒充系统声音；鼠标位置约 20 Hz 推送给场景。
- 锁屏是静态截图，不是 Win+L 后播放视频。系统策略可能限制锁屏设置；失败会显示错误，不会修改系统策略。
- 自动暂停：会话锁定、任意显示器被前台全屏窗口覆盖。全屏暂停目前是全局策略。
- 多屏同源播放，显示器拓扑变化后重建窗口。桌面宿主消失后尝试重新挂载。
- 不自动启用开机启动、不停止 Wallpaper Engine、不捆绑或上传壁纸素材。
- MP4 和 PKG 均使用 WebView2，有额外进程和内存开销；切换素材或停止后销毁播放控件，尚未做多硬件性能基准。
- 场景脚本启用以提高兼容性；宿主不暴露本机对象，拦截外网、下载和权限请求。仅播放自己信任的本地素材。

## 构建

Windows PowerShell 中运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

使用系统 .NET Framework 4.x 的 x64 C# 编译器和 WPF。固定版本依赖已置于 `vendor/`，构建无需联网。机器需安装 WebView2 Runtime（本机已有）。许可见 `THIRD-PARTY-NOTICES.md`。

若 `bin/AWall.exe` 正在运行，可加 `-OutputDirectory bin-next` 构建到独立目录。本次优化版已生成到 `bin-next/AWall.exe`；原 `bin` 因程序占用未覆盖。本次按要求未运行测试或启动新版。

关闭正在运行的 AWall 后，可运行 `bin/AWall.exe --test-transitions` 做切换集成验证（会临时播放本机素材并在结束时退出）。结果写入 `%LOCALAPPDATA%\AWall\transition-test.json`，覆盖首次播放、视频/场景互切、旧播放器暂停、遮罩清理、损坏视频回退、连续切换和捕获中停止。

## 目录

- `src/AWall.cs`：UI、素材扫描、桌面窗口宿主、视频循环、暂停和快照。
- `src/ScenePlayer.cs`：WebView2 场景宿主、本地映射、共享纹理、诊断、暂停和截图。
- `src/scene/index.html`：PKG 渲染入口与消息桥接。
- `src/scene/video.html`：MP4 首帧检测、循环播放、暂停及错误报告。
- `src/Set-LockImage.ps1`：WinRT 锁屏图片接口；先尝试 UserProfilePersonalizationSettings，拒绝时使用 LockScreen.SetImageFileAsync。
- `build.ps1`：生成 bin/AWall.exe 并复制锁屏脚本。
- 数据、日志和锁屏图片：`%LOCALAPPDATA%\AWall`。
- 自检：`bin\AWall.exe --self-test`。自检会暂时启动视频壁纸，8 秒后保存帧和结果并退出。
- 全素材自检：退出其他 AWall 实例后运行 `bin\AWall.exe --test-library`，自动遍历视频与 PKG，并在结束后退出。结果在 `%LOCALAPPDATA%\AWall\compatibility\results.json`，包含像素范围、两个时刻的画面是否变化、PKG 暂停状态、运行帧率、警告与截图。首帧成功不代表效果完全还原。
- 场景诊断：`%LOCALAPPDATA%\AWall\scene-<工坊ID>.log`；WebView2 缓存在 `%LOCALAPPDATA%\AWall\WebView2`。

素材版权属于原作者，仅从本机现有下载读取，不包含在源代码中。

当前唯一正式构建入口：`AWall.cmd` 或 `bin/AWall.exe`。MP4 使用 mpv；PKG 使用独立 WebView2 环境，预热复用已撤回。保留并行截图加载、淡入和失败回退。最新版已重新编译，尚未实机验证 PKG 回归修复。GitHub 源码不包含本机素材、研究副本及来源尚未完整核实的 mpv 二进制；本机编译需在 vendor/mpv 放置所需运行文件，来源见 THIRD-PARTY-NOTICES.md。
