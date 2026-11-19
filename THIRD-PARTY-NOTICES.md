# Third-party components

## WebWallGL 3.0.2

- Source: https://github.com/oneincase/webwallgl
- Package: https://registry.npmjs.org/webwallgl/-/webwallgl-3.0.2.tgz
- MIT, copyright (c) 2026 oneincase. Full license: `vendor/webwallgl/LICENSE` and `bin/scene/WebWallGL-LICENSE.txt`.
- Original integrity: `sha512-mYb/zwq9vDVF8KLOBswZEf+DZ3bvVLnrcTjqzEf2oKV4hfrobCBnDTXJ9goEiK/eNGdL4xyEJX+bxJmvh1jfHA==`.
- Local compatibility fix: `vendor/webwallgl/AWall-patch.mjs`, explicitly passes the texture map to captureEmptyComposeAtZOrder instead of referencing an unbound textures variable. Applied to UMD and ESM bundles.
- The UMD includes Mediabunny 1.56.0 (MPL-2.0), see below.

## Mediabunny 1.56.0 (included in the WebWallGL UMD)

- Source package: https://registry.npmjs.org/mediabunny/-/mediabunny-1.56.0.tgz
- MPL-2.0, full license and original source files included in `vendor/mediabunny/LICENSE` and `vendor/mediabunny/src/`.
- The bundled media dependency is unmodified. Corresponding source is shipped alongside the AWall project and source archive. Its file-level license remains MPL-2.0.

## Microsoft Edge WebView2 SDK 1.0.4314.35

- Package: https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/1.0.4314.35/microsoft.web.webview2.1.0.4314.35.nupkg
- Full license: `vendor/webview2/LICENSE.txt`.
- Uses Core and WPF net462 assemblies plus x64 WebView2Loader.dll. Runtime uses the installed Microsoft Edge WebView2, not a bundled browser.

## Materials

No Steam Workshop artwork, PKG, MP4, or Wallpaper Engine assets are redistributed with AWall. It only reads local materials. Their copyright remains with their respective authors.

## mpv 视频后端（2026-11-16，2026-11-17 起为可选）

自 2026-11-17 起默认构建不再包含 mpv：MP4 默认由 WebView2 内置播放器播放。只有使用 `build.ps1 -WithMpv` 构建时才会把下述 mpv 文件复制到输出目录。

MP4 使用独立 mpv 子进程，通过 --wid 嵌入 AWall 的桌面窗口，JSON named-pipe IPC 控制。PKG 继续使用 WebView2/WebWallGL。mpv 二进制及两个运行库取自本机 Lively Wallpaper 2.2.1.0 安装包的 Plugins/Mpv；不是运行时依赖 Lively。mpv.exe SHA256：EA97CCD5AB7122EE7084FB04A4D0E74606B344F708FEDBA75B56B22A2D0E4C98。

mpv 上游：[mpv-player/mpv](https://github.com/mpv-player/mpv)；许可证见 mpv/LICENSE.GPL。构建依赖及精确源码版本尚未从这份本机二进制确认；当前用于本机开发，公开分发前需要补全对应构建的源码与依赖许可材料。awall-ready.lua 是 AWall 自有桥接脚本。

## Lively Wallpaper 机制研究（2026-11-16）

- 上游：[rocksdanister/lively](https://github.com/rocksdanister/lively)。本地研究检出提交为 `25a0a5f4ed1988ff42117d3a17feb321062237fe`，其 LICENSE 为 GNU GPL version 3。
- REA 静态分析目标为本机安装的 Lively.dll 2.2.1.0，SHA-256 `ca73583755c8fc381cdda674bfbc3c60fea0bf924dcb06f846083c0b30f354ad`，MVID `132f72e5-047c-4397-9fed-876a4eb08716`。研究源码和安装二进制的 raised desktop 分支存在差异，不能视为同一精确构建。
- AWall 按观察到的桌面发现、播放器启动后挂载、退出与恢复机制独立重写宿主流程，并按本项目需求保留真实首帧、快照遮罩和回滚。Lively 程序集未被复制为 AWall 的运行依赖；这项实现方式说明不构成许可豁免声明。上文所列 mpv 运行文件仍按其实际来源和许可材料处理。

