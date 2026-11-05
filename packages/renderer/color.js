// Picks a vivid accent colour from album art (runs on a 40x40 downsample).
(function (root) {
  function rgbToHsl(r, g, b) {
    r /= 255; g /= 255; b /= 255;
    const max = Math.max(r, g, b), min = Math.min(r, g, b);
    let h = 0, s = 0; const l = (max + min) / 2;
    if (max !== min) {
      const d = max - min;
      s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
      h = max === r ? (g - b) / d + (g < b ? 6 : 0) : max === g ? (b - r) / d + 2 : (r - g) / d + 4;
      h /= 6;
    }
    return [h * 360, s, l];
  }

  function hsl(h, s, l) { return `hsl(${Math.round(h)} ${Math.round(s * 100)}% ${Math.round(l * 100)}%)`; }

  function hslToHex(h, s, l) {
    const a = s * Math.min(l, 1 - l);
    const f = (n) => { const k = (n + h / 30) % 12; const c = l - a * Math.max(-1, Math.min(k - 3, 9 - k, 1)); return Math.round(c * 255).toString(16).padStart(2, '0'); };
    return `#${f(0)}${f(8)}${f(4)}`;
  }

  function extractAccent(src) {
    return new Promise((resolve) => {
      const fallback = { h: 38, s: 0.95, l: 0.6 };
      if (!src) return resolve(pack(fallback));
      const img = new Image();
      img.crossOrigin = 'anonymous';
      img.onload = () => {
        try {
          const c = document.createElement('canvas'); c.width = c.height = 40;
          const x = c.getContext('2d', { willReadFrequently: true });
          x.drawImage(img, 0, 0, 40, 40);
          const d = x.getImageData(0, 0, 40, 40).data;
          const buckets = new Map();
          for (let i = 0; i < d.length; i += 4) {
            const [h, s, l] = rgbToHsl(d[i], d[i + 1], d[i + 2]);
            if (l < 0.12 || l > 0.92) continue;
            const key = Math.round(h / 15) % 24;
            const score = s * s * (1 - Math.abs(l - 0.55));
            const b = buckets.get(key) || { n: 0, score: 0, h: 0, s: 0, l: 0 };
            b.n++; b.score += score; b.h += h; b.s += s; b.l += l;
            buckets.set(key, b);
          }
          let best = null;
          for (const b of buckets.values()) if (!best || b.score > best.score) best = b;
          if (!best || best.score / best.n < 0.04) return resolve(pack({ h: 40, s: 0.15, l: 0.85 })); // greyscale art → warm white
          const h = best.h / best.n, s = Math.max(0.65, best.s / best.n), l = Math.min(0.68, Math.max(0.55, best.l / best.n));
          resolve(pack({ h, s, l }));
        } catch (e) { resolve(pack(fallback)); }
      };
      img.onerror = () => resolve(pack(fallback));
      img.src = src;
    });
  }

  function pack({ h, s, l }) {
    return { css: hsl(h, s, l), hex: hslToHex(h, s, l), soft: `hsl(${Math.round(h)} ${Math.round(s * 100)}% ${Math.round(l * 100)}% / 0.25)`, h, s, l };
  }

  root.NVColor = { extractAccent };
})(window);
