// Shared factory for "one line at a time" themes. A theme supplies markup,
// CSS (in themes.css) and optional hooks; this handles timing and word state:
//   .w        every word        .on  already sung        .now  being sung
//   --wp      0..1 progress through the current word (for karaoke fills)
//   --lp      0..1 progress through the whole line
(function () {
  const T = (window.NVThemes = window.NVThemes || {});

  function sizeFor(text, scale = 1) {
    const n = text.length;
    const vw = (n <= 10 ? 11 : n <= 20 ? 8.4 : n <= 34 ? 6.3 : n <= 50 ? 5 : 4) * scale;
    return `min(${vw.toFixed(2)}vw, ${(vw * 1.7).toFixed(2)}vh)`;
  }

  T.__line = function (opts) {
    const theme = function (stage, api) {
      stage.innerHTML = opts.html || '<div class="ln-wrap"><div class="ln-prev"></div><div class="ln-line"></div><div class="ln-next"></div></div>';
      const ctx = { stage, api, el: (s) => stage.querySelector(s) };
      const lineEl = ctx.el('.ln-line'), prevEl = ctx.el('.ln-prev'), nextEl = ctx.el('.ln-next');
      let shown = -2, spans = [];
      if (opts.setup) opts.setup(ctx);

      function status() {
        const s = api.state;
        const msg = s.status === 'plain' ? 'These lyrics aren\'t time-synced yet — press T for Classic' : api.statusText();
        lineEl.innerHTML = `<span class="ln-status">${msg}</span>`;
        lineEl.style.fontSize = '';
        if (prevEl) prevEl.textContent = '';
        if (nextEl) nextEl.textContent = '';
        spans = [];
      }

      function build(i) {
        const lines = api.state.lines;
        const prev = i > 0 ? lines[i - 1] : null;
        if (opts.beforeLine) opts.beforeLine(ctx, prev, i);
        if (prevEl) prevEl.textContent = prev ? prev.text : '';
        if (nextEl) nextEl.textContent = i + 1 < lines.length ? lines[i + 1].text : '';
        lineEl.innerHTML = '';
        spans = [];
        lineEl.classList.remove('enter');
        void lineEl.offsetWidth; // restart entry animation
        lineEl.classList.add('enter');
        if (i < 0 || !lines[i].text) {
          lineEl.innerHTML = '<span class="ln-note">♪</span>';
          lineEl.style.fontSize = sizeFor('♪♪♪♪♪♪', opts.scale);
          return;
        }
        const line = lines[i];
        lineEl.style.fontSize = sizeFor(line.text, opts.scale);
        line.words.forEach((w, k) => {
          const s = document.createElement('span');
          s.className = 'w';
          s.textContent = w.text;
          s.dataset.t = w.text;
          s.style.setProperty('--i', k);
          lineEl.appendChild(s);
          spans.push(s);
        });
        if (opts.onLine) opts.onLine(ctx, line, i, lineEl);
      }

      return {
        onTrack(t) { if (opts.onTrack) opts.onTrack(ctx, t); },
        onLyrics() { shown = -2; if (!api.state.lines.length) status(); },
        frame(ms) {
          if (opts.frame) opts.frame(ctx, ms);
          const lines = api.state.lines;
          if (!lines.length) return;
          const i = api.currentIndex();
          if (i !== shown) { shown = i; build(i); }
          if (i < 0 || !spans.length) return;
          const line = lines[i], words = line.words;
          let cur = -1;
          for (let k = 0; k < words.length; k++) if (words[k].t <= ms + 60) cur = k;
          lineEl.style.setProperty('--lp', Math.max(0, Math.min(1, (ms - line.t) / Math.max(1, line.end - line.t))).toFixed(3));
          for (let k = 0; k < spans.length; k++) {
            const s = spans[k], on = k <= cur, now = k === cur && ms < line.end - 120;
            if (s._on !== on) { s._on = on; s.classList.toggle('on', on); }
            if (s._now !== now) { s._now = now; s.classList.toggle('now', now); }
            if (opts.wordProgress) {
              const end = k + 1 < words.length ? words[k + 1].t : line.end;
              const p = k < cur ? 1 : k > cur ? 0 : Math.min(1, (ms - words[k].t) / Math.max(1, end - words[k].t));
              s.style.setProperty('--wp', p.toFixed(3));
            }
          }
        },
        destroy() { if (opts.destroy) opts.destroy(ctx); },
      };
    };
    theme.label = opts.label;
    theme.swatch = opts.swatch;
    return theme;
  };

  // Full-screen canvas behind the lyrics, sized for the device pixel ratio.
  T.__canvas = function (stage) {
    const c = document.createElement('canvas');
    c.className = 'ln-canvas';
    stage.prepend(c);
    const x = c.getContext('2d');
    const fit = () => { const d = Math.min(2, devicePixelRatio || 1); c.width = innerWidth * d; c.height = innerHeight * d; x.setTransform(d, 0, 0, d, 0, 0); };
    fit();
    addEventListener('resize', fit);
    return { c, x, w: () => innerWidth, h: () => innerHeight, off: () => removeEventListener('resize', fit) };
  };
})();
