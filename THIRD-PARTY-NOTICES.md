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

## mpv 视频后端（2026-11-16）

MP4 使用独立 mpv 子进程，通过 --wid 嵌入 AWall 的桌面窗口，JSON named-pipe IPC 控制。PKG 继续使用 WebView2/WebWallGL。mpv 二进制及两个运行库取自本机 Lively Wallpaper 2.2.1.0 安装包的 Plugins/Mpv；不是运行时依赖 Lively。mpv.exe SHA256：EA97CCD5AB7122EE7084FB04A4D0E74606B344F708FEDBA75B56B22A2D0E4C98。

mpv 上游：https://github.com/mpv-player/mpv；许可证见 mpv/LICENSE.GPL。构建依赖及精确源码版本尚未从这份本机二进制确认；当前用于本机开发，公开分发前需要补全对应构建的源码与依赖许可材料。awall-ready.lua 是 AWall 自有桥接脚本。
