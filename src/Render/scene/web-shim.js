// AWall HTML wallpaper shim. Injected before any page script runs
// (AddScriptToExecuteOnDocumentCreatedAsync), top frame only.
// - caps requestAnimationFrame at AWALL_FPS and lets the host pause it
// - pauses CSS/Web animations and media elements on awallPause(true)
// - reports the first painted frame ("ready"), script errors ("diagnostic")
// - exposes an optional window.awall API for pages written for AWall
(function () {
  'use strict';
  if (window.top !== window) return;
  var FPS = 30, interval = 1000 / FPS;
  var nativeRaf = window.requestAnimationFrame.bind(window);
  var nativeCaf = window.cancelAnimationFrame.bind(window);
  var post = function (type, value) { try { window.chrome.webview.postMessage({ type: type, value: value }); } catch (e) {} };

  var queue = [], nextId = 1, pending = 0, last = 0, paused = false;
  var pausedAt = 0, pausedTotal = 0, frames = 0, fpsStart = 0, fps = 0;
  function pump(t) {
    pending = 0;
    if (paused) return;
    if (last && t - last < interval - 1) { pending = nativeRaf(pump); return; }
    last = t; frames++;
    if (!fpsStart) fpsStart = t;
    else if (t - fpsStart >= 1000) { fps = Math.round(frames * 10000 / (t - fpsStart)) / 10; frames = 0; fpsStart = t; }
    var run = queue; queue = [];
    var stamp = t - pausedTotal; // time spent paused is hidden from page animations
    for (var i = 0; i < run.length; i++) {
      if (!run[i].cb) continue;
      try { run[i].cb(stamp); } catch (e) { report(e && e.message ? e.message : String(e)); }
    }
    if (queue.length && !pending) pending = nativeRaf(pump);
  }
  window.requestAnimationFrame = function (cb) {
    var id = nextId++; queue.push({ id: id, cb: cb });
    if (!pending && !paused) pending = nativeRaf(pump);
    return id;
  };
  window.cancelAnimationFrame = function (id) {
    for (var i = 0; i < queue.length; i++) if (queue[i].id === id) { queue[i].cb = null; break; }
  };

  var reported = {}, reportCount = 0;
  function report(message) {
    message = String(message || '未知脚本错误').slice(0, 300);
    if (reported[message] || reportCount >= 20) return;
    reported[message] = true; reportCount++;
    post('diagnostic', { level: 'warn', message: '页面脚本错误：' + message });
  }
  window.addEventListener('error', function (e) { report(e.message || (e.target && e.target.src ? '资源加载失败 ' + e.target.src : '')); }, true);
  window.addEventListener('unhandledrejection', function (e) { report(e.reason && e.reason.message ? e.reason.message : String(e.reason)); });

  // Parameters: host-injected values (defaults + saved), then any live changes made
  // before a reload of this same page (kept in sessionStorage, which dies with the player).
  var PROPS_KEY = 'awall.properties';
  var props = {};
  try { Object.assign(props, window.__awallProps || {}); } catch (e) {}
  try { delete window.__awallProps; } catch (e) { window.__awallProps = undefined; }
  try { Object.assign(props, JSON.parse(sessionStorage.getItem(PROPS_KEY) || '{}')); } catch (e) {}

  var api = {
    host: 'AWall', fps: FPS, paused: false, pointer: { x: 0.5, y: 0.5 }, properties: props,
    onPointer: null, onPause: null, onResume: null, onProperty: null
  };

  // Track whether the page listens for live parameter changes; if not, reload it.
  var propertyListeners = 0;
  var nativeAdd = window.addEventListener;
  window.addEventListener = function (type) {
    if (type === 'awall:property') propertyListeners++;
    return nativeAdd.apply(this, arguments);
  };
  function emit(name, detail) {
    try { window.dispatchEvent(new CustomEvent('awall:' + name, { detail: detail })); } catch (e) {}
  }

  var heldAnimations = [], heldMedia = [];
  window.awallPause = function (value) {
    value = !!value;
    if (value === paused) return;
    paused = value; api.paused = value;
    if (value) {
      pausedAt = performance.now();
      if (pending) { nativeCaf(pending); pending = 0; }
      try { heldAnimations = document.getAnimations().filter(function (a) { return a.playState === 'running'; }); } catch (e) { heldAnimations = []; }
      heldAnimations.forEach(function (a) { try { a.pause(); } catch (e) {} });
      heldMedia = [].filter.call(document.querySelectorAll('video,audio'), function (m) { return !m.paused; });
      heldMedia.forEach(function (m) { try { m.pause(); } catch (e) {} });
      if (typeof api.onPause === 'function') try { api.onPause(); } catch (e) { report(e.message); }
      emit('pause');
    } else {
      pausedTotal += performance.now() - pausedAt; last = 0; fpsStart = 0; frames = 0;
      heldAnimations.forEach(function (a) { try { a.play(); } catch (e) {} });
      heldMedia.forEach(function (m) { try { var p = m.play(); if (p && p.catch) p.catch(function () {}); } catch (e) {} });
      heldAnimations = []; heldMedia = [];
      if (queue.length && !pending) pending = nativeRaf(pump);
      if (typeof api.onResume === 'function') try { api.onResume(); } catch (e) { report(e.message); }
      emit('resume');
    }
  };

  // u, v: cursor position on this monitor, 0..1. Delivered both through the
  // awall API and as synthetic mouse/pointer moves for ordinary pages.
  window.awallPointer = function (u, v) {
    api.pointer.x = u; api.pointer.y = v;
    var x = u * window.innerWidth, y = v * window.innerHeight;
    if (typeof api.onPointer === 'function') try { api.onPointer(u, v); } catch (e) { report(e.message); }
    emit('pointer', { x: u, y: v });
    var target = document.elementFromPoint(x, y) || document.documentElement || document;
    var init = { bubbles: true, cancelable: true, clientX: x, clientY: y, screenX: x, screenY: y, view: window };
    try { target.dispatchEvent(new PointerEvent('pointermove', Object.assign({ pointerType: 'mouse', isPrimary: true }, init))); } catch (e) {}
    try { target.dispatchEvent(new MouseEvent('mousemove', init)); } catch (e) {}
  };

  window.awallProperties = function (changes) {
    if (!changes) return;
    Object.assign(props, changes);
    try {
      var live = JSON.parse(sessionStorage.getItem(PROPS_KEY) || '{}');
      sessionStorage.setItem(PROPS_KEY, JSON.stringify(Object.assign(live, changes)));
    } catch (e) {}
    if (typeof api.onProperty !== 'function' && !propertyListeners) { location.reload(); return; }
    if (typeof api.onProperty === 'function') {
      for (var k in changes) try { api.onProperty(k, changes[k]); } catch (e) { report(e.message); }
    }
    emit('property', changes);
  };

  window.awallStats = function () {
    return { type: 'web', paused: paused, fps: fps, fpsCap: FPS, width: window.innerWidth, height: window.innerHeight };
  };

  try { Object.defineProperty(window, 'awall', { value: api, writable: false, configurable: false }); } catch (e) { window.awall = api; }

  // Wallpapers never scroll or show scrollbars.
  document.addEventListener('DOMContentLoaded', function () {
    var style = document.createElement('style');
    style.textContent = 'html,body{overflow:hidden!important}::-webkit-scrollbar{display:none}';
    (document.head || document.documentElement).appendChild(style);
  });

  // First frame: after load, wait for two real paints so the page has drawn.
  window.addEventListener('load', function () {
    nativeRaf(function () { nativeRaf(function () {
      post('ready', { type: 'web', width: window.innerWidth, height: window.innerHeight, title: document.title || '' });
    }); });
  });
})();
