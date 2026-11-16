// Ambient lighting panel (Windows app only). Sends the user's choices to the host,
// which draws the light around the screen edges and drives it from the PC's audio.
(function () {
  const $ = (s) => document.querySelector(s);
  const PRESETS = {
    'RGB': ['#ff2a55', '#35ff8a', '#2d7bff'],
    'Neon': ['#ff2bd6', '#26e6ff', '#9b5cff'],
    'Sunset': ['#ff7a2f', '#ff3d6e', '#ffc65a'],
    'Aurora': ['#3dffb0', '#38b6ff', '#9a6bff'],
    'Ice': ['#9be8ff', '#5aa8ff', '#e9f7ff'],
    'Lava': ['#ff3b1f', '#ff8a00', '#ffd23f'],
    'Ocean': ['#0077ff', '#00d4ff', '#00ffc8'],
    'Forest': ['#0fbf5a', '#9bff4d', '#1b8f6a'],
    'Candy': ['#ff5fa2', '#ffd1e8', '#7be0ff'],
    'Cyber': ['#fcee0a', '#00f0ff', '#ff003c'],
    'Vapor': ['#ff71ce', '#01cdfe', '#b967ff', '#05ffa1'],
    'Gold': ['#ffb800', '#ff6a00', '#fff1b8'],
    'Rose': ['#ff4d6d', '#ff8fa3', '#c9184a'],
    'Midnight': ['#4361ee', '#7209b7', '#3a0ca3'],
    'Red & Blue': ['#e8112d', '#1d4ed8', '#ffffff'],
    'Pure white': ['#ffffff', '#ffe9c4'],
  };
  const STYLES = {
    ambient: 'A bright glow sweeps round a soft rounded frame, trailing light behind it.',
    static: 'Your colours held in place around the screen. No movement, no beat.',
    dynamic: 'Hits hard on every beat and jumps to a new shade each time.',
    aurora: 'Northern-lights curtain: soft bars on every edge, dancing to the music.',
    rays: 'Thin neon streaks shoot inwards from the edges, faster on the beat.',
    tunnel: 'Neon rings run inwards like a lit tunnel, a new one on every beat.',
  };

  const L = { mode: 'custom', style: 'ambient', colors: PRESETS.RGB.slice(), react: 0.7, speed: 0.35, reach: 0.45, gradient: 0.85, brightness: 0.8, glow: true };
  let quiet = false;

  function push() {
    if (quiet || !window.NV) return;
    window.NV.send({ type: 'lighting', lighting: { ...L, colors: L.colors.slice() } });
  }

  function drawSwatches() {
    const box = $('#lt-sw');
    box.innerHTML = '';
    L.colors.forEach((c, i) => {
      const d = document.createElement('div'); d.className = 'lt-sw'; d.style.background = c;
      const inp = document.createElement('input'); inp.type = 'color'; inp.value = c;
      inp.addEventListener('input', () => { L.colors[i] = inp.value; d.style.background = inp.value; L.mode = 'custom'; $('#lt-album').checked = false; push(); });
      d.appendChild(inp);
      if (L.colors.length > 1) {
        const x = document.createElement('button'); x.type = 'button'; x.textContent = '×'; x.title = 'Remove colour';
        x.addEventListener('click', (e) => { e.preventDefault(); L.colors.splice(i, 1); drawSwatches(); push(); });
        d.appendChild(x);
      }
      box.appendChild(d);
    });
    if (L.colors.length < 12) {
      const add = document.createElement('button'); add.type = 'button'; add.className = 'lt-add'; add.textContent = '+'; add.title = 'Add a colour';
      add.addEventListener('click', () => {
        L.colors.push('#' + Math.floor(Math.random() * 0xffffff).toString(16).padStart(6, '0'));
        L.mode = 'custom'; $('#lt-album').checked = false; drawSwatches(); push();
      });
      box.appendChild(add);
    }
  }

  function bindRange(id, key) {
    const el = $(id), out = el.parentElement.querySelector('output');
    el.addEventListener('input', () => { L[key] = el.value / 100; out.textContent = Math.round(L[key] * 100); push(); });
  }
  function syncStyle() {
    document.querySelectorAll('#lt-style [data-style]').forEach((b) => b.classList.toggle('active', b.dataset.style === L.style));
    $('#lt-desc').textContent = STYLES[L.style] || '';
    document.body.dataset.ltStyle = L.style;
  }
  document.querySelectorAll('#lt-style [data-style]').forEach((b) => b.addEventListener('click', () => { L.style = b.dataset.style; syncStyle(); push(); }));
  function syncUi() {
    syncStyle();
    $('#lt-glow').checked = !!L.glow;
    $('#lt-album').checked = L.mode === 'album';
    [['#lt-react', 'react'], ['#lt-speed', 'speed'], ['#lt-reach', 'reach'], ['#lt-grad', 'gradient'], ['#lt-bright', 'brightness']].forEach(([id, k]) => {
      const el = $(id); el.value = Math.round(L[k] * 100); el.parentElement.querySelector('output').textContent = Math.round(L[k] * 100);
    });
    drawSwatches();
  }

  const pre = $('#lt-presets');
  Object.keys(PRESETS).forEach((name) => {
    const b = document.createElement('button'); b.type = 'button'; b.textContent = name;
    b.title = name; b.style.setProperty('--sw', 'linear-gradient(90deg,' + PRESETS[name].join(',') + ')');
    b.addEventListener('click', () => { L.colors = PRESETS[name].slice(); L.mode = 'custom'; $('#lt-album').checked = false; drawSwatches(); push(); });
    pre.appendChild(b);
  });
  bindRange('#lt-react', 'react'); bindRange('#lt-speed', 'speed'); bindRange('#lt-reach', 'reach'); bindRange('#lt-grad', 'gradient'); bindRange('#lt-bright', 'brightness');
  $('#lt-glow').addEventListener('change', (e) => { L.glow = e.target.checked; push(); });
  $('#lt-album').addEventListener('change', (e) => { L.mode = e.target.checked ? 'album' : 'custom'; push(); });
  $('#lt-close').addEventListener('click', () => document.body.classList.remove('show-lights'));
  syncUi();

  window.NVLights = {
    set(v) {
      quiet = true;
      if (Array.isArray(v.colors) && v.colors.length) L.colors = v.colors.slice(0, 12);
      ['mode', 'style', 'react', 'speed', 'reach', 'gradient', 'brightness', 'glow'].forEach((k) => { if (v[k] !== undefined) L[k] = v[k]; });
      syncUi();
      quiet = false;
    },
  };
})();
