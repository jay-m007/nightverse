// Nightverse renderer engine: receives now-playing data from a host
// (Windows app via WebView2, or the browser extension via postMessage),
// keeps a smooth clock, and drives the active theme.
(function () {
  const $ = (s) => document.querySelector(s);
  const params = new URLSearchParams(location.search);

  const state = {
    track: null,          // { title, artist, album, durationMs, cover }
    lines: [],            // parsed synced lyrics
    plain: null,          // plain lyrics fallback
    status: 'idle',       // idle | loading | found | plain | notfound | instrumental
    accent: null,
    settings: { theme: 'kinetic', showClock: true, ...loadSettings() },
    clock: { base: 0, at: performance.now(), playing: false, rate: 1 },
    host: detectHost(),
  };

  function loadSettings() {
    try { return JSON.parse(localStorage.getItem('nv-settings') || '{}'); } catch { return {}; }
  }
  function saveSettings() {
    try { localStorage.setItem('nv-settings', JSON.stringify(state.settings)); } catch {}
    send({ type: 'settings', settings: state.settings });
  }

  function detectHost() {
    if (window.chrome && window.chrome.webview) return 'webview2';
    if (window.parent !== window) return 'frame';
    return 'standalone';
  }

  function send(msg) {
    if (state.host === 'webview2') window.chrome.webview.postMessage(msg);
    else if (state.host === 'frame') window.parent.postMessage({ nightverse: true, ...msg }, '*');
  }

  // ---------- clock ----------
  function now() {
    const c = state.clock;
    return c.base + (c.playing ? (performance.now() - c.at) * c.rate : 0);
  }
  function setPosition(ms, playing) {
    const current = now();
    const drift = ms - current;
    // Ignore tiny jitter from hosts that report coarse positions; re-anchor on real jumps.
    if (playing !== state.clock.playing || Math.abs(drift) > 350 || !state.clock.playing) {
      state.clock = { base: ms, at: performance.now(), playing, rate: 1 };
    } else {
      // gently steer towards the reported time over ~2s
      state.clock = { base: current, at: performance.now(), playing, rate: 1 + Math.max(-0.15, Math.min(0.15, drift / 2000)) };
    }
    document.body.classList.toggle('paused', !playing);
  }

  // ---------- themes ----------
  const themes = window.NVThemes;
  const order = ['kinetic', 'constellation', 'classic', 'vinyl'];
  let active = null;
  const stage = $('#stage');

  function useTheme(name) {
    if (!themes[name]) name = 'kinetic';
    if (active && active.destroy) active.destroy();
    stage.innerHTML = '';
    stage.className = 'stage theme-' + name;
    document.body.dataset.theme = name;
    active = themes[name](stage, api);
    state.settings.theme = name;
    if (state.track && active.onTrack) active.onTrack(state.track);
    if (active.onLyrics) active.onLyrics();
    toast(themes[name].label || name);
  }

  // ---------- incoming messages ----------
  async function handle(msg) {
    if (!msg || typeof msg !== 'object') return;
    switch (msg.type) {
      case 'track': {
        const t = { title: msg.title || '', artist: msg.artist || '', album: msg.album || '', durationMs: msg.durationMs || 0, cover: msg.cover || null };
        const same = state.track && state.track.title === t.title && state.track.artist === t.artist;
        state.track = t;
        if (!same) { state.lines = []; state.plain = null; state.status = 'loading'; }
        $('#bg').style.backgroundImage = t.cover ? `url("${t.cover}")` : 'none';
        $('#meta-title').textContent = t.title;
        $('#meta-artist').textContent = t.artist;
        document.body.classList.toggle('no-track', !t.title);
        const accent = await window.NVColor.extractAccent(t.cover);
        state.accent = accent;
        document.documentElement.style.setProperty('--accent', accent.css);
        document.documentElement.style.setProperty('--accent-soft', accent.soft);
        send({ type: 'accent', color: accent.hex });
        if (active && active.onTrack) active.onTrack(t);
        if (active && active.onLyrics && !same) active.onLyrics();
        break;
      }
      case 'lyrics': {
        state.lines = window.NVLrc.parseLrc(msg.synced || '');
        state.plain = msg.plain || null;
        state.status = msg.instrumental ? 'instrumental' : state.lines.length ? 'found' : state.plain ? 'plain' : (msg.status || 'notfound');
        if (active && active.onLyrics) active.onLyrics();
        break;
      }
      case 'position':
        setPosition(msg.positionMs || 0, !!msg.playing);
        break;
      case 'settings':
        Object.assign(state.settings, msg.settings || {});
        applySettings();
        break;
      case 'idle':
        state.track = null; state.lines = []; state.status = 'idle';
        document.body.classList.add('no-track');
        if (active && active.onLyrics) active.onLyrics();
        break;
    }
  }

  if (state.host === 'webview2') window.chrome.webview.addEventListener('message', (e) => handle(e.data));
  window.addEventListener('message', (e) => { if (e.data && e.data.nightverse) handle(e.data); });

  // ---------- helpers for themes ----------
  const api = {
    state,
    now,
    currentIndex() { return window.NVLrc.lineIndexAt(state.lines, now()); },
    statusText() {
      switch (state.status) {
        case 'loading': return 'Finding lyrics…';
        case 'instrumental': return '♪ Instrumental ♪';
        case 'notfound': return 'No synced lyrics for this one';
        case 'idle': return 'Play something in Spotify or YT Music';
        default: return '';
      }
    },
  };

  // ---------- chrome: clock, keys, toast ----------
  function applySettings() {
    document.body.classList.toggle('show-clock', !!state.settings.showClock);
    document.body.classList.toggle('mini', !!state.settings.mini);
    if (!active || document.body.dataset.theme !== state.settings.theme) useTheme(state.settings.theme);
  }

  function tickClock() {
    const d = new Date();
    $('#clock-time').textContent = d.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' }).replace(/\s?[AP]M/i, '');
    $('#clock-date').textContent = d.toLocaleDateString([], { weekday: 'short', month: 'short', day: 'numeric' });
  }
  setInterval(tickClock, 1000); tickClock();

  let toastTimer;
  function toast(text) {
    const el = $('#toast');
    el.textContent = text; el.classList.add('show');
    clearTimeout(toastTimer); toastTimer = setTimeout(() => el.classList.remove('show'), 1400);
  }

  function nextTheme(dir = 1) {
    const i = order.indexOf(state.settings.theme);
    useTheme(order[(i + dir + order.length) % order.length]);
    saveSettings();
  }

  window.addEventListener('keydown', (e) => {
    if (e.target && /input|select|textarea/i.test(e.target.tagName)) return;
    const k = e.key.toLowerCase();
    if (k === 't') nextTheme(e.shiftKey ? -1 : 1);
    else if (k === 'c') { state.settings.showClock = !state.settings.showClock; applySettings(); saveSettings(); }
    else if (k === ' ') { e.preventDefault(); send({ type: 'command', name: 'playPause' }); }
    else if (k === 'arrowright') send({ type: 'command', name: 'next' });
    else if (k === 'arrowleft') send({ type: 'command', name: 'previous' });
    else if (k === 'g') send({ type: 'command', name: 'toggleGlow' });
    else if (k === 'm') send({ type: 'command', name: 'toggleMini' });
    else if (k === 'f' || k === 'f11') { e.preventDefault(); send({ type: 'command', name: 'fullscreen' }); }
    else if (k === 'escape') send({ type: 'command', name: 'exitFullscreen' });
    else if (k === 'h' || k === '?') document.body.classList.toggle('show-help');
  });

  // Theme dock (hover at the bottom)
  document.querySelectorAll('[data-theme-btn]').forEach((b) => b.addEventListener('click', () => { useTheme(b.dataset.themeBtn); saveSettings(); }));
  document.querySelectorAll('[data-cmd]').forEach((b) => b.addEventListener('click', () => {
    const c = b.dataset.cmd;
    if (c === 'clock') { state.settings.showClock = !state.settings.showClock; applySettings(); saveSettings(); }
    else if (c === 'help') document.body.classList.toggle('show-help');
    else send({ type: 'command', name: c });
  }));
  let idleTimer;
  window.addEventListener('mousemove', () => {
    document.body.classList.add('pointer-active');
    clearTimeout(idleTimer); idleTimer = setTimeout(() => document.body.classList.remove('pointer-active'), 2200);
  });
  document.body.classList.add('host-' + state.host);

  // ---------- render loop ----------
  function frame() {
    if (active && active.frame) active.frame(now());
    requestAnimationFrame(frame);
  }

  applySettings();
  requestAnimationFrame(frame);
  send({ type: 'ready' });

  // ---------- demo ----------
  if (params.has('demo') || state.host === 'standalone') window.NVDemo && window.NVDemo.start(handle, params);
  if (params.has('theme')) useTheme(params.get('theme'));
  if (params.has('seek')) setTimeout(() => setPosition(+params.get('seek'), !params.has('paused')), 50);
  window.NV = { handle, useTheme, state, setPosition };
})();
