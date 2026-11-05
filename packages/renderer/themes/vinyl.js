// Vinyl: a spinning record with the cover as its label, a tonearm that lifts
// on pause, and the current lyric line underneath.
(function () {
  const T = (window.NVThemes = window.NVThemes || {});

  T.vinyl = function (stage, api) {
    stage.innerHTML = `
      <div class="v-wrap">
        <div class="v-deck">
          <div class="v-record"><div class="v-label"></div></div>
          <div class="v-arm"><div class="rod"></div><div class="head"></div><div class="pivot"></div></div>
        </div>
        <div class="v-title"></div>
        <div class="v-artist"></div>
        <div class="v-lyric"></div>
        <div class="v-controls">
          <button data-c="previous" title="Previous">⏮</button>
          <button data-c="playPause" title="Play / pause">⏯</button>
          <button data-c="next" title="Next">⏭</button>
        </div>
      </div>`;
    const label = stage.querySelector('.v-label');
    const title = stage.querySelector('.v-title');
    const artist = stage.querySelector('.v-artist');
    const lyric = stage.querySelector('.v-lyric');
    stage.querySelectorAll('[data-c]').forEach((b) => b.addEventListener('click', () => {
      const msg = { type: 'command', name: b.dataset.c };
      if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage(msg);
      else if (window.parent !== window) window.parent.postMessage({ nightverse: true, ...msg }, '*');
    }));
    let shown = -2;

    return {
      onTrack(t) {
        label.style.backgroundImage = t.cover ? `url("${t.cover}")` : 'none';
        title.textContent = t.title || 'Nothing playing';
        artist.textContent = t.artist || '';
      },
      onLyrics() {
        shown = -2;
        if (!api.state.track) { title.textContent = 'Nothing playing'; artist.textContent = ''; label.style.backgroundImage = 'none'; }
        lyric.textContent = api.state.lines.length ? '' : (api.state.status === 'found' ? '' : api.statusText());
      },
      frame() {
        if (!api.state.lines.length) return;
        const i = api.currentIndex();
        if (i === shown) return;
        shown = i;
        lyric.style.opacity = 0;
        setTimeout(() => { lyric.textContent = i >= 0 ? (api.state.lines[i].text || '♪') : '♪'; lyric.style.opacity = 1; }, 160);
      },
    };
  };
  T.vinyl.label = 'Vinyl';
})();
