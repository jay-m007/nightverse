// Constellation: each word is a star, joined to the next word by a thin line
// as it is sung. The previous line stays behind as a faint constellation.
(function () {
  const T = (window.NVThemes = window.NVThemes || {});
  const NS = 'http://www.w3.org/2000/svg';
  const W = 1600, H = 900;

  function rng(seed) { // deterministic so a line always draws the same shape
    let s = (seed * 9301 + 49297) % 233280 || 1;
    return () => (s = (s * 9301 + 49297) % 233280) / 233280;
  }
  function el(tag, attrs, parent) {
    const e = document.createElementNS(NS, tag);
    for (const k in attrs) e.setAttribute(k, attrs[k]);
    if (parent) parent.appendChild(e);
    return e;
  }
  const star = (r) => `M0 ${-r} Q0 0 ${r} 0 Q0 0 0 ${r} Q0 0 ${-r} 0 Q0 0 0 ${-r}Z`;

  T.constellation = function (stage, api) {
    const svg = el('svg', { viewBox: `0 0 ${W} ${H}`, preserveAspectRatio: 'xMidYMid meet' }, stage);
    const status = document.createElement('div');
    status.className = 'status'; status.style.position = 'absolute';
    stage.appendChild(status);
    let shown = -2, groups = [], nodes = [];

    function layout(i, line) {
      const r = rng(i + 7 + line.text.length);
      const n = line.words.length;
      const upper = i % 2 === 0;
      const y0 = upper ? 250 : 470, y1 = upper ? 470 : 690;
      const left = 150 + r() * 80, right = W - 150 - r() * 80;
      const pts = [];
      for (let k = 0; k < n; k++) {
        const x = n === 1 ? W / 2 : left + ((right - left) * k) / (n - 1) + (r() - 0.5) * 50;
        const y = y0 + r() * (y1 - y0);
        pts.push({ x, y });
      }
      return pts;
    }

    function build(i) {
      // age the old groups
      groups.forEach((g, idx) => {
        if (idx === groups.length - 1) g.classList.add('old');
        else { g.classList.add('fading'); setTimeout(() => g.remove(), 1400); }
      });
      groups = groups.slice(-1);
      nodes = [];
      if (i < 0) return;
      const line = api.state.lines[i];
      if (!line.text) return;
      const g = el('g', {}, svg);
      groups.push(g);
      const pts = layout(i, line);
      const n = line.words.length;
      const base = n > 9 ? 22 : n > 6 ? 26 : 30;
      const edges = el('g', {}, g);
      pts.forEach((p, k) => {
        let edge = null;
        if (k > 0) {
          const q = pts[k - 1];
          const len = Math.hypot(p.x - q.x, p.y - q.y);
          edge = el('line', { x1: q.x, y1: q.y, x2: p.x, y2: p.y, class: 'c-edge', 'stroke-dasharray': len, 'stroke-dashoffset': len }, edges);
          edge._len = len;
        }
        const s = el('path', { d: star(9), class: 'c-star hidden', transform: `translate(${p.x} ${p.y})` }, g);
        const above = k % 2 === 0;
        const t = el('text', { x: p.x, y: p.y + (above ? -22 : 40), 'text-anchor': 'middle', class: 'c-word hidden', 'font-size': base }, g);
        t.textContent = line.words[k].text;
        nodes.push({ star: s, text: t, edge, base });
      });
    }

    return {
      onLyrics() {
        shown = -2;
        status.textContent = api.state.lines.length ? '' : (api.state.status === 'plain' ? 'Lyrics aren\'t time-synced — press T for Classic' : api.statusText());
        if (!api.state.lines.length) { svg.innerHTML = ''; groups = []; nodes = []; }
      },
      frame(ms) {
        const lines = api.state.lines;
        if (!lines.length) return;
        const i = api.currentIndex();
        if (i !== shown) { shown = i; build(i); }
        if (i < 0 || !nodes.length) return;
        const words = lines[i].words;
        let cur = -1;
        for (let k = 0; k < words.length; k++) if (words[k].t <= ms + 60) cur = k;
        nodes.forEach((nd, k) => {
          const on = k <= cur;
          if (nd._on !== on) {
            nd._on = on;
            nd.star.classList.toggle('hidden', !on);
            nd.text.classList.toggle('hidden', !on);
          }
          if (nd.edge) {
            // draw the edge towards word k while word k-1 is being sung
            const start = words[k - 1].t, end = words[k].t;
            const p = Math.max(0, Math.min(1, (ms - start) / Math.max(1, end - start)));
            nd.edge.setAttribute('stroke-dashoffset', nd.edge._len * (1 - p));
          }
          const now = k === cur;
          if (nd._now !== now) {
            nd._now = now;
            nd.text.classList.toggle('now', now);
            nd.text.setAttribute('font-size', now ? nd.base * 1.9 : nd.base);
            nd.star.setAttribute('d', star(now ? 15 : 9));
          }
        });
      },
      destroy() {},
    };
  };
  T.constellation.label = 'Constellation';
})();
