# 让 AI 生成 AWall 网页壁纸

把下面整段连同你的想法发给任意 AI（Claude、ChatGPT 等）。保存它给出的 `.html` 文件，然后在 AWall 里点“导入本地文件”，或者直接放进 `library\wallpapers\html\`。

---

请帮我写一个 **动态桌面壁纸**，主题是：【在这里描述你想要的画面】

技术要求（必须遵守）：

1. **只输出一个完整的 `.html` 文件**，CSS 和 JS 全部内联。
2. **外部资源只允许 three.js / p5.js 等库文件**，并且只能从 `cdn.jsdelivr.net`、`unpkg.com`、`cdnjs.cloudflare.com` 引用，**必须写明完整版本号**（例如 `three@0.170.0`、`p5.js/1.9.4`）。three.js 用 `<script type="importmap">` 加 ES module 的写法，addons 从同一个 CDN 的 `examples/jsm/` 引用。除此之外不用 Google Fonts、外链图片、纹理、模型或网络 API；图片用 Canvas/SVG/Shader 生成，字体用系统字体（"Segoe UI", "Microsoft YaHei UI", sans-serif）。
3. 画面铺满整个窗口（100vw × 100vh），不出现滚动条，能适配任意分辨率和缩放比例，并监听 `resize`。
4. 动画只用 `requestAnimationFrame`，不要用 `setInterval` 驱动动画。按时间差 (dt) 计算运动，掉帧时速度保持不变。
5. 注意性能（用 three.js 时：`renderer.setPixelRatio(Math.min(devicePixelRatio, 1.5))`、`powerPreference: 'low-power'`，动画用 `renderer.setAnimationLoop`；用 p5.js 时：`pixelDensity(Math.min(devicePixelRatio, 1.5))`，不要调用 `frameRate()`，并实现 `windowResized`）：这是长期在后台运行的壁纸。Canvas 分辨率最多取 `devicePixelRatio` 和 1.5 中较小的值，避免每帧大量创建对象。目标是 30 FPS 下 GPU/CPU 占用很低。
6. 没有用户交互、没有声音、不弹窗、不需要点击才开始。
7. 可选的鼠标互动：如果存在 `window.awall`，用 `awall.onPointer = (x, y) => {}` 接收光标位置（0~1 归一化）；否则监听 `mousemove`。
8. 可选：用 `awall.onPause` / `awall.onResume` 处理暂停和继续（壁纸被全屏窗口遮挡或锁屏时会触发）。
9. **提供 3~6 个可调参数**（速度、颜色、密度、开关之类）。用下面的格式写在 `<body>` 里：

```html
<script type="application/json" id="awall-properties">
{
  "speed": { "type": "slider", "label": "速度", "min": 0.2, "max": 3, "step": 0.1, "value": 1 },
  "tint":  { "type": "color",  "label": "主色", "value": "#5ea8ff" },
  "clock": { "type": "bool",   "label": "显示时钟", "value": true },
  "mode":  { "type": "combo",  "label": "风格", "options": [{ "value": "calm", "label": "平静" }, { "value": "wild", "label": "狂野" }], "value": "calm" }
}
</script>
```

   type 只能是 slider / color（#rrggbb）/ bool / combo；label 用中文。页面脚本开头用这段代码读取参数，并在动画中**每帧读取 P**，让参数修改能实时生效：

```js
const P = {};
const schema = JSON.parse(document.getElementById('awall-properties').textContent);
for (const k in schema) P[k] = schema[k].value;
if (window.awall) { Object.assign(P, awall.properties); awall.onProperty = (k, v) => { P[k] = v; }; }
```

   如果某个参数改变后需要重建场景（比如粒子数量），在 `onProperty` 里处理。

---

## 不需要动画的壁纸

如果画面只需要几分钟变一次（数据可视化、日历、名言），把上面第 4、5 条换成下面这条，AWall 就不会让页面一直运行：

> 在 `<head>` 里加 `<meta name="awall-present" content="interval=300">`。页面每次加载时只画一帧当前状态，不要用 `requestAnimationFrame` 循环。加载完成约 1 秒后，AWall 会截图，把截图设为系统壁纸，然后关闭页面；300 秒后重新加载并截图。间隔最少 60 秒。如果画面完全不变，写 `content="static"`。

---

## AWall 会对页面做什么

- jsdelivr / unpkg / cdnjs 上的库文件第一次使用时自动下载，缓存到 `library\web-cache\`，之后离线读取。只接受 JS / JSON / CSS / WASM，单个文件最大 50MB；其余外部请求一律拦截。
- rAF 限制到 30 FPS；暂停时停止 rAF、CSS 动画和 `<video>/<audio>`。暂停期间流逝的时间不会计入 rAF 时间戳。
- 光标位置约 20Hz 推送，同时会派发合成的 `mousemove` / `pointermove`（不会触发 `:hover`）。
- 页面脚本错误和被拦截的外部请求，会显示为状态栏警告，并写入 `%LOCALAPPDATA%\AWall\web-<名字>.log`。
- 首次成功播放约 1.5 秒后，自动截图保存为同名 `.jpg` 预览图（只针对素材库内的文件）。
- `setInterval` / `setTimeout` 驱动的动画暂停时不会停，所以第 4 条很重要。
- 参数：声明了 `awall-properties` 的壁纸，详情面板里会出现“调整参数”按钮。改动实时生效，并保存在 `%LOCALAPPDATA%\AWall\properties\<文件名>.json`，不会修改 HTML 文件本身。页面没有设置 `awall.onProperty`（也没有监听 `awall:property` 事件）时，AWall 会带着新值重新加载页面。
