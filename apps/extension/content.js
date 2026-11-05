// Runs on music.youtube.com. Reads what's playing from the page, sends it to
// the Nightverse renderer (an iframe overlay) and relays its button presses.
(() => {
  if (window.__nightverse) return;
  window.__nightverse = true;

  let frame = null, ready = false, open = false;
  let track = null, lyricsMsg = null, coverData = null;

  const $ = (s) => document.querySelector(s);
  const video = () => $('video');

  // ---------- reading the player ----------
  function readTrack() {
    const md = navigator.mediaSession && navigator.mediaSession.metadata;
    let title = md && md.title, artist = md && md.artist, album = md && md.album;
    let art = md && md.artwork && md.artwork.length ? md.artwork[md.artwork.length - 1].src : null;
    if (!title) {
      title = $('ytmusic-player-bar .title')?.textContent?.trim();
      const byline = $('ytmusic-player-bar .byline')?.textContent || '';
      const parts = byline.split('•').map((s) => s.trim());
      artist = parts[0]; album = parts[1];
      art = $('ytmusic-player-bar img.image')?.src || null;
    }
    if (!title) return null;
    const v = video();
    return { title, artist: artist || '', album: album || '', art, durationMs: v && isFinite(v.duration) ? Math.round(v.duration * 1000) : 0 };
  }

  // ---------- overlay ----------
  function ensureFrame() {
    if (frame) return frame;
    frame = document.createElement('iframe');
    frame.id = 'nightverse-frame';
    frame.src = chrome.runtime.getURL('renderer/index.html');
    frame.allow = 'fullscreen';
    frame.setAttribute('allowfullscreen', '');
    document.documentElement.appendChild(frame);
    return frame;
  }

  function post(msg) {
    if (frame && ready) frame.contentWindow.postMessage({ nightverse: true, ...msg }, '*');
  }

  function setOpen(v) {
    open = v;
    ensureFrame().classList.toggle('nv-open', v);
    button.classList.toggle('nv-active', v);
    chrome.storage.local.set({ nvOpen: v });
    if (v) setTimeout(() => frame.focus(), 50);
    else if (document.fullscreenElement === frame) document.exitFullscreen().catch(() => {});
  }

  const button = document.createElement('button');
  button.id = 'nightverse-toggle';
  button.title = 'Nightverse lyrics (Alt+L)';
  button.textContent = 'Lyrics';
  button.addEventListener('click', () => setOpen(!open));
  document.documentElement.appendChild(button);

  window.addEventListener('message', (e) => {
    if (!frame || e.source !== frame.contentWindow || !e.data || !e.data.nightverse) return;
    const m = e.data;
    if (m.type === 'ready') {
      ready = true;
      if (track) post(trackMsg());
      if (lyricsMsg) post(lyricsMsg);
      sendPosition();
    } else if (m.type === 'command') command(m.name);
  });

  function command(name) {
    const v = video();
    if (name === 'playPause' && v) v.paused ? v.play() : v.pause();
    else if (name === 'next') $('ytmusic-player-bar .next-button')?.click();
    else if (name === 'previous') $('ytmusic-player-bar .previous-button')?.click();
    else if (name === 'fullscreen') {
      if (document.fullscreenElement) document.exitFullscreen().catch(() => {});
      else frame.requestFullscreen().catch(() => {});
    } else if (name === 'exitFullscreen' || name === 'close') {
      if (document.fullscreenElement) document.exitFullscreen().catch(() => {});
      else setOpen(false);
    }
  }

  chrome.runtime.onMessage.addListener((msg) => { if (msg.type === 'toggle') setOpen(!open); });

  // ---------- sync loop ----------
  const trackMsg = () => ({ type: 'track', title: track.title, artist: track.artist, album: track.album, durationMs: track.durationMs, cover: coverData || track.art });

  function sendPosition() {
    const v = video();
    if (v) post({ type: 'position', positionMs: Math.round(v.currentTime * 1000), playing: !v.paused && !v.ended });
  }

  let lastKey = '';
  function tick() {
    const t = readTrack();
    const key = t ? `${t.artist}|${t.title}` : '';
    if (key !== lastKey) {
      lastKey = key;
      track = t; coverData = null; lyricsMsg = null;
      if (!t) { post({ type: 'idle' }); return; }
      post(trackMsg());
      const want = key;
      chrome.runtime.sendMessage({ type: 'lyrics', track: t }, (r) => {
        if (want !== lastKey || !r) return;
        lyricsMsg = { type: 'lyrics', synced: r.synced, plain: r.plain, instrumental: r.instrumental, status: r.notFound ? 'notfound' : 'found' };
        post(lyricsMsg);
      });
      if (t.art) chrome.runtime.sendMessage({ type: 'cover', url: t.art }, (data) => {
        if (want !== lastKey || !data) return;
        coverData = data; post(trackMsg());
      });
    } else if (t && track && !track.durationMs && t.durationMs) {
      track.durationMs = t.durationMs;
    }
  }

  // video events give exact timing on seek/pause; the interval catches song changes
  document.addEventListener('play', sendPosition, true);
  document.addEventListener('pause', sendPosition, true);
  document.addEventListener('seeked', sendPosition, true);
  setInterval(() => { tick(); sendPosition(); }, 700);

  chrome.storage.local.get('nvOpen').then((s) => { if (s.nvOpen) setOpen(true); });
})();
