// Ten extra lyric styles built on the shared line factory (_line.js).
// Their looks live in themes.css under .theme-<name>.
(function () {
  const T = window.NVThemes;
  const line = T.__line;

  // ---------------- Neon: glass tubes that light up word by word ----------------
  T.neon = line({
    label: 'Neon Sign',
    swatch: 'radial-gradient(circle at 50% 55%, #ff3cac 0 18%, transparent 19%), #1a0d14',
    scale: 0.95,
  });

  // ---------------- Cyberpunk: HUD frame, glitching words ----------------
  T.cyberpunk = line({
    label: 'Cyberpunk',
    swatch: 'linear-gradient(135deg, #fcee0a 0 45%, #00f0ff 45% 55%, #0a0a12 55%)',
    scale: 0.82,
    html: `<div class="cp-hud"><span class="cp-tl">SYS//NIGHTVERSE</span><span class="cp-tr"></span><span class="cp-bl"></span><span class="cp-br">LYRIC.FEED ▮▮▮▯</span></div>
      <div class="ln-wrap"><div class="ln-prev"></div><div class="ln-line"></div><div class="ln-next"></div></div>`,
    onTrack(ctx, t) {
      ctx.el('.cp-tr').textContent = `TRK: ${t.title}`.toUpperCase().slice(0, 42);
      ctx.el('.cp-bl').textContent = `SRC: ${t.artist}`.toUpperCase().slice(0, 42);
    },
  });

  // ---------------- Comic Pop: halftone page, caption box, burst ----------------
  T.comic = line({
    label: 'Comic Pop',
    swatch: 'radial-gradient(#e2367a 30%, transparent 32%) 0 0/8px 8px, #ffe14d',
    scale: 0.82,
    html: `<div class="cm-panel"><div class="cm-tag">MEANWHILE…</div><div class="ln-line"></div><div class="ln-next"></div></div>`,
  });

  // ---------------- Terminal: green phosphor log with a typing cursor ----------------
  T.terminal = line({
    label: 'Terminal',
    swatch: 'repeating-linear-gradient(#0b1a0f 0 3px, #07120a 3px 5px)',
    scale: 0.6,
    html: `<div class="tm-screen"><div class="tm-head"></div><div class="tm-log"></div><div class="tm-cur"><span class="tm-ps">$&nbsp;</span><span class="ln-line"></span></div></div>`,
    onTrack(ctx, t) { ctx.el('.tm-head').textContent = `nightverse --play "${t.title}" --artist "${t.artist}"`; },
    beforeLine(ctx, prev) {
      if (!prev || !prev.text) return;
      const log = ctx.el('.tm-log');
      const row = document.createElement('div');
      row.textContent = '> ' + prev.text;
      log.appendChild(row);
      while (log.children.length > 7) log.firstChild.remove();
    },
  });

  // ---------------- Synthwave: sunset, moving grid, chrome text ----------------
  T.synthwave = line({
    label: 'Synthwave',
    swatch: 'linear-gradient(#2b1055 0 45%, #ff6fb5 45% 55%, #120321 55%)',
    scale: 0.9,
    html: `<div class="sw-sky"><div class="sw-sun"></div></div><div class="sw-floor"><div class="sw-grid"></div></div>
      <div class="ln-wrap"><div class="ln-line"></div><div class="ln-next"></div></div>`,
  });

  // ---------------- Karaoke: colour sweeps through each word ----------------
  T.karaoke = line({
    label: 'Karaoke',
    swatch: 'linear-gradient(90deg, #ffd23f 0 55%, #fff 55%)',
    scale: 0.85,
    wordProgress: true,
    html: '<div class="ln-wrap"><div class="ln-line"></div><div class="ln-next"></div><div class="kk-dots"><i></i><i></i><i></i></div></div>',
  });

  // ---------------- Aurora: drifting northern lights, soft serif ----------------
  T.aurora = line({
    label: 'Aurora',
    swatch: 'linear-gradient(160deg, #0b1d2a, #1de9b6 50%, #7c4dff)',
    scale: 0.9,
    html: '<div class="au-sky"><i></i><i></i><i></i></div><div class="ln-wrap"><div class="ln-prev"></div><div class="ln-line"></div></div>',
  });

  // ---------------- Rainy Window: lo-fi rain running down glass ----------------
  T.rain = line({
    label: 'Rainy Window',
    swatch: 'linear-gradient(180deg, #2c3e50, #4b6584)',
    scale: 0.8,
    setup(ctx) {
      ctx.cv = T.__canvas(ctx.stage);
      ctx.drops = Array.from({ length: 220 }, () => newDrop(true));
      ctx.beads = Array.from({ length: 70 }, () => ({ x: Math.random(), y: Math.random(), r: 1 + Math.random() * 3.5 }));
    },
    frame(ctx) {
      const { x, w, h } = ctx.cv, W = w(), H = h();
      x.clearRect(0, 0, W, H);
      x.strokeStyle = 'rgba(200,220,255,0.28)';
      x.lineWidth = 1;
      x.beginPath();
      for (const d of ctx.drops) {
        x.moveTo(d.x * W, d.y * H);
        x.lineTo(d.x * W - d.len * 0.12, d.y * H + d.len);
        d.y += d.v; d.x -= d.v * 0.012;
        if (d.y > 1.05) Object.assign(d, newDrop(false));
      }
      x.stroke();
      for (const b of ctx.beads) {
        const g = x.createRadialGradient(b.x * W - b.r / 3, b.y * H - b.r / 3, 0, b.x * W, b.y * H, b.r);
        g.addColorStop(0, 'rgba(255,255,255,0.55)'); g.addColorStop(1, 'rgba(255,255,255,0.04)');
        x.fillStyle = g; x.beginPath(); x.arc(b.x * W, b.y * H, b.r, 0, 7); x.fill();
        b.y += b.r * 0.00006; if (b.y > 1.02) { b.y = -0.02; b.x = Math.random(); }
      }
    },
    destroy(ctx) { ctx.cv.off(); },
  });
  function newDrop(any) { return { x: Math.random() * 1.1, y: any ? Math.random() : -0.1 - Math.random() * 0.2, v: 0.006 + Math.random() * 0.01, len: 10 + Math.random() * 22 }; }

  // ---------------- Warp Speed: stars stream past, surge on each new line ----------------
  T.warp = line({
    label: 'Warp Speed',
    swatch: 'radial-gradient(circle, #fff 0 4%, #6a5cff 5% 14%, #05030f 15%)',
    scale: 0.85,
    setup(ctx) {
      ctx.cv = T.__canvas(ctx.stage);
      ctx.stars = Array.from({ length: 420 }, () => star(true));
      ctx.boost = 0;
    },
    onLine(ctx) { ctx.boost = 1; },
    frame(ctx) {
      const { x, w, h } = ctx.cv, W = w(), H = h(), cx = W / 2, cy = H / 2;
      const playing = ctx.api.state.clock.playing;
      ctx.boost *= 0.94;
      const speed = (playing ? 0.012 : 0.002) + ctx.boost * 0.05;
      x.fillStyle = 'rgba(5,3,15,0.35)'; x.fillRect(0, 0, W, H);
      const accent = getComputedStyle(document.documentElement).getPropertyValue('--accent');
      for (const s of ctx.stars) {
        const pz = s.z; s.z -= speed;
        if (s.z <= 0.02) { Object.assign(s, star(false)); continue; }
        const k = Math.max(W, H) * 0.5;
        const x1 = cx + (s.x / pz) * k, y1 = cy + (s.y / pz) * k, x2 = cx + (s.x / s.z) * k, y2 = cy + (s.y / s.z) * k;
        x.strokeStyle = s.tint ? accent : `rgba(255,255,255,${Math.min(1, 1.2 - s.z)})`;
        x.lineWidth = Math.max(0.6, 2.2 * (1 - s.z));
        x.beginPath(); x.moveTo(x1, y1); x.lineTo(x2, y2); x.stroke();
      }
    },
    destroy(ctx) { ctx.cv.off(); },
  });
  function star(any) { return { x: (Math.random() - 0.5) * 2, y: (Math.random() - 0.5) * 2, z: any ? Math.random() : 1, tint: Math.random() < 0.12 }; }

  // ---------------- Editorial: magazine page, black serif on paper ----------------
  T.editorial = line({
    label: 'Editorial',
    swatch: 'linear-gradient(#f2efe8 0 60%, #141414 60%)',
    scale: 0.92,
    html: '<div class="ed-page"><div class="ed-mast"><span class="ed-issue"></span><span>NIGHTVERSE</span><span class="ed-no">Nº 01</span></div><div class="ln-line"></div><div class="ln-next"></div></div>',
    onTrack(ctx, t) { ctx.el('.ed-issue').textContent = `${t.title} — ${t.artist}`; },
  });
})();
