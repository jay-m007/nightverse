// Demo mode: an original song (lyrics written for this project) so the
// renderer can be previewed in any browser without a music player.
(function () {
  const LRC = `[00:00.80]
[00:02.40]City lights are humming low
[00:06.10]Every window's got a glow
[00:09.80]I keep your voice on repeat
[00:13.30]Like a heartbeat in the street
[00:17.00]Stay up, stay up with me
[00:20.40]We can be the static on the radio
[00:24.60]Midnight, midnight, let it be
[00:28.10]Neon over everything we know
[00:32.00]
[00:33.20]Paper stars on the ceiling
[00:36.80]Half a song and a feeling
[00:40.40]Hold on, hold on, don't let go
[00:44.20]Let the night run slow`;

  function cover() {
    const c = document.createElement('canvas'); c.width = c.height = 512;
    const x = c.getContext('2d');
    const g = x.createLinearGradient(0, 0, 512, 512);
    g.addColorStop(0, '#1b0b2e'); g.addColorStop(0.55, '#5a1446'); g.addColorStop(1, '#ff7a2f');
    x.fillStyle = g; x.fillRect(0, 0, 512, 512);
    const s = x.createRadialGradient(256, 300, 10, 256, 300, 150);
    s.addColorStop(0, '#ffd27a'); s.addColorStop(1, '#ff5e3a');
    x.fillStyle = s; x.beginPath(); x.arc(256, 300, 130, Math.PI, 0); x.fill();
    x.fillStyle = '#1b0b2e';
    for (let i = 0; i < 7; i++) x.fillRect(110, 300 + i * 14, 292, 4 + i);
    x.fillStyle = 'rgba(255,255,255,0.9)';
    for (let i = 0; i < 40; i++) x.fillRect((i * 97) % 512, (i * 53) % 220, 2, 2);
    x.font = '800 44px system-ui, sans-serif'; x.fillStyle = '#fff'; x.textAlign = 'center';
    x.fillText('NEON STATIC', 256, 470);
    return c.toDataURL('image/png');
  }

  window.NVDemo = {
    lrc: LRC,
    start(handle, params) {
      const duration = 48000;
      handle({ type: 'track', title: 'Neon Static', artist: 'Nightverse Demo', album: 'Demo', durationMs: duration, cover: cover() });
      handle({ type: 'lyrics', synced: LRC });
      const t0 = performance.now() - (+params.get('seek') || 0);
      const paused = params.has('paused');
      handle({ type: 'position', positionMs: +params.get('seek') || 0, playing: !paused });
      if (paused) return;
      setInterval(() => {
        const pos = (performance.now() - t0) % duration;
        handle({ type: 'position', positionMs: pos, playing: true });
      }, 1000);
    },
  };
})();
