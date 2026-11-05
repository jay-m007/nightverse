// LRC parser. Supports standard line timestamps [mm:ss.xx] (several per line)
// and "enhanced" word timestamps <mm:ss.xx> inside a line.
(function (root) {
  const TIME = /\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]/g;
  const WORD = /<(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?>/g;

  function toMs(m, s, frac) {
    let ms = 0;
    if (frac) ms = frac.length === 1 ? +frac * 100 : frac.length === 2 ? +frac * 10 : +frac;
    return (+m * 60 + +s) * 1000 + ms;
  }

  function splitWords(text, start, end) {
    // Evenly spread words over the line, weighted by length (long words take longer).
    const parts = text.split(/\s+/).filter(Boolean);
    if (!parts.length) return [];
    const total = parts.reduce((a, w) => a + w.length + 2, 0);
    const span = Math.max(400, Math.min(end - start, 12000)) * 0.9;
    let t = start;
    return parts.map((w) => {
      const word = { text: w, t: Math.round(t) };
      t += (span * (w.length + 2)) / total;
      return word;
    });
  }

  function parseLrc(input) {
    if (!input) return [];
    const lines = [];
    let offset = 0;
    for (const raw of input.split(/\r?\n/)) {
      const off = raw.match(/^\[offset:\s*([+-]?\d+)\]/i);
      if (off) { offset = +off[1]; continue; }
      const stamps = [];
      TIME.lastIndex = 0;
      let m, lastIndex = 0;
      while ((m = TIME.exec(raw)) && m.index === lastIndex) {
        stamps.push(toMs(m[1], m[2], m[3]));
        lastIndex = TIME.lastIndex;
      }
      if (!stamps.length) continue;
      const body = raw.slice(lastIndex);
      // enhanced words
      let words = null;
      if (WORD.test(body)) {
        WORD.lastIndex = 0;
        words = [];
        const re = /<(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?>([^<]*)/g;
        let w;
        while ((w = re.exec(body))) {
          const txt = w[4].trim();
          if (txt) txt.split(/\s+/).forEach((piece, i) => words.push({ text: piece, t: toMs(w[1], w[2], w[3]) + i * 120 }));
        }
      }
      const text = body.replace(/<[^>]*>/g, '').replace(/\s+/g, ' ').trim();
      for (const t of stamps) lines.push({ t: t - offset, text, words: words && words.map((x) => ({ ...x, t: x.t - offset })) });
    }
    lines.sort((a, b) => a.t - b.t);
    for (let i = 0; i < lines.length; i++) {
      const end = i + 1 < lines.length ? lines[i + 1].t : lines[i].t + 5000;
      lines[i].end = end;
      if (!lines[i].words) lines[i].words = splitWords(lines[i].text, lines[i].t, end);
    }
    return lines;
  }

  function lineIndexAt(lines, ms) {
    let lo = 0, hi = lines.length - 1, ans = -1;
    while (lo <= hi) {
      const mid = (lo + hi) >> 1;
      if (lines[mid].t <= ms) { ans = mid; lo = mid + 1; } else hi = mid - 1;
    }
    return ans;
  }

  const api = { parseLrc, lineIndexAt };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.NVLrc = api;
})(typeof window !== 'undefined' ? window : globalThis);
