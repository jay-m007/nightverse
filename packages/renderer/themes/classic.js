// Classic: cover art on the left, a scrolling lyric sheet on the right
// with the current line in focus. Also the only theme that shows plain lyrics.
(function () {
  const T = (window.NVThemes = window.NVThemes || {});
  const esc = (s) => s.replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));

  T.classic = function (stage, api) {
    stage.innerHTML = `
      <div class="cl-card"><div class="cl-cover"></div><div class="cl-title"></div><div class="cl-artist"></div></div>
      <div class="cl-scroller"><div class="cl-list"></div></div>`;
    const cover = stage.querySelector('.cl-cover');
    const title = stage.querySelector('.cl-title');
    const artist = stage.querySelector('.cl-artist');
    const scroller = stage.querySelector('.cl-scroller');
    const list = stage.querySelector('.cl-list');
    let els = [], shown = -2, wordEls = [];

    function center(i) {
      const target = els[Math.max(0, i)];
      if (!target) return;
      const y = scroller.clientHeight * 0.4 - target.offsetTop - target.offsetHeight / 2;
      list.style.transform = `translateY(${y}px)`;
    }

    return {
      onTrack(t) {
        cover.style.backgroundImage = t.cover ? `url("${t.cover}")` : 'none';
        title.textContent = t.title; artist.textContent = t.artist;
      },
      onLyrics() {
        const s = api.state;
        shown = -2; wordEls = [];
        list.classList.toggle('cl-plain', s.status === 'plain');
        if (s.lines.length) {
          list.innerHTML = s.lines.map((l) => `<div class="cl-line">${l.text ? l.words.map((w) => `<span>${esc(w.text)}</span>`).join(' ') : '♪'}</div>`).join('');
        } else if (s.status === 'plain' && s.plain) {
          list.innerHTML = s.plain.split(/\r?\n/).map((l) => `<div class="cl-line">${esc(l) || '&nbsp;'}</div>`).join('');
          list.style.transform = 'translateY(12vh)';
          list.style.transition = 'none';
          // slow auto-scroll through plain lyrics based on song progress
        } else {
          list.innerHTML = `<div class="cl-line now" style="font-size:clamp(18px,2vw,28px)">${esc(api.statusText())}</div>`;
        }
        els = [...list.children];
        if (s.lines.length) { list.style.transition = ''; center(0); }
      },
      frame(ms) {
        const s = api.state;
        if (s.status === 'plain' && s.track && s.track.durationMs && els.length) {
          const p = Math.min(1, ms / s.track.durationMs);
          const max = list.scrollHeight - scroller.clientHeight * 0.5;
          list.style.transform = `translateY(${scroller.clientHeight * 0.15 - p * max}px)`;
          return;
        }
        if (!s.lines.length) return;
        const i = api.currentIndex();
        if (i !== shown) {
          if (els[shown]) els[shown].classList.remove('now');
          els.forEach((e, k) => e.classList.toggle('near', Math.abs(k - i) === 1));
          if (els[i]) els[i].classList.add('now');
          wordEls = els[i] ? [...els[i].children] : [];
          shown = i; center(i);
        }
        if (i >= 0) {
          const words = s.lines[i].words;
          wordEls.forEach((w, k) => { const on = words[k] && words[k].t <= ms + 60; if (w._on !== on) { w._on = on; w.classList.toggle('w-on', on); } });
        }
      },
    };
  };
  T.classic.label = 'Classic';
})();
