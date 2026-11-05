// Kinetic: one line at a time, words land as they are sung,
// the word being sung gets a highlighter box in the album colour.
(function () {
  const T = (window.NVThemes = window.NVThemes || {});

  function sizeFor(text) {
    const n = text.length;
    const vw = n <= 10 ? 13 : n <= 20 ? 9.5 : n <= 34 ? 7 : n <= 50 ? 5.4 : 4.3;
    return `min(${vw}vw, ${vw * 1.7}vh)`;
  }

  T.kinetic = function (stage, api) {
    stage.innerHTML = '<div class="k-wrap"><div class="k-line"></div><div class="k-prev"></div></div>';
    const wrap = stage.querySelector('.k-wrap');
    let lineEl = stage.querySelector('.k-line');
    const prevEl = stage.querySelector('.k-prev');
    let shown = -2, spans = [];

    function showStatus() {
      lineEl.innerHTML = '';
      prevEl.textContent = '';
      const s = api.state;
      const msg = s.status === 'plain' ? 'These lyrics aren\'t time-synced yet' : api.statusText();
      const hint = s.status === 'plain' ? 'Press T for Classic to read them' : s.status === 'notfound' ? 'You can add them at lrclib.net' : '';
      lineEl.innerHTML = `<div class="status">${msg}${hint ? `<small>${hint}</small>` : ''}</div>`;
    }

    function build(i) {
      const lines = api.state.lines;
      if (lineEl.childElementCount) {
        const old = lineEl;
        old.classList.add('leaving');
        setTimeout(() => old.remove(), 450);
        lineEl = document.createElement('div');
        lineEl.className = 'k-line';
        wrap.insertBefore(lineEl, prevEl);
        lineEl.style.position = 'relative';
        old.style.position = 'absolute'; old.style.left = '7vw'; old.style.right = '7vw';
      }
      spans = [];
      prevEl.textContent = i > 0 ? lines[i - 1].text : '';
      if (i < 0) { lineEl.innerHTML = '<div class="status">♪</div>'; return; }
      const line = lines[i];
      if (!line.text) { lineEl.innerHTML = '<div class="status" style="font-size:min(7vw,12vh)">♪</div>'; return; }
      lineEl.style.fontSize = sizeFor(line.text);
      for (const w of line.words) {
        const s = document.createElement('span');
        s.className = 'k-word';
        s.textContent = w.text;
        lineEl.appendChild(s);
        spans.push(s);
      }
    }

    return {
      onLyrics() { shown = -2; if (!api.state.lines.length) showStatus(); },
      frame(ms) {
        const lines = api.state.lines;
        if (!lines.length) return;
        const i = api.currentIndex();
        if (i !== shown) { shown = i; build(i); }
        if (i < 0) return;
        const words = lines[i].words;
        let cur = -1;
        for (let k = 0; k < words.length; k++) if (words[k].t <= ms + 60) cur = k;
        for (let k = 0; k < spans.length; k++) {
          const s = spans[k];
          const on = k <= cur;
          if (s._on !== on) { s.classList.toggle('on', on); s._on = on; }
          const now = k === cur && ms < lines[i].end - 120;
          if (s._now !== now) { s.classList.toggle('now', now); s.classList.toggle('past', on && !now); s._now = now; }
        }
      },
    };
  };
  T.kinetic.label = 'Kinetic';
})();
