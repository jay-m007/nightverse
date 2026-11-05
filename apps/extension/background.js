// Service worker: fetches lyrics from LRCLIB and album art (as a data URL so the
// renderer can read its colours). Content scripts can't make these cross-origin calls.
const UA_NOTE = 'Nightverse extension (+https://github.com/jay-m007/nightverse)';
const cache = new Map();

function cleanTitle(t) {
  return t
    .replace(/\s*[([](?:feat|ft|with|prod|official|lyric|audio|video|visuali[sz]er|remaster|explicit|from)[^)\]]*[)\]]/gi, '')
    .replace(/\s+-\s+(?:.*remaster.*|live.*|radio edit|single version|mono|stereo)$/i, '')
    .trim();
}

async function getJson(url) {
  try {
    const r = await fetch('https://lrclib.net' + url, { headers: { 'Lrclib-Client': UA_NOTE } });
    if (!r.ok) return null;
    return await r.json();
  } catch { return null; }
}

const good = (r) => r && (r.instrumental || r.syncedLyrics || r.plainLyrics);

async function findLyrics({ title, artist, album, durationMs }) {
  const key = `${artist}|${title}`.toLowerCase();
  if (cache.has(key)) return cache.get(key);
  const stored = await chrome.storage.local.get(key);
  if (stored[key] && (!stored[key].notFound || Date.now() - stored[key].at < 864e5)) return stored[key];

  const t = cleanTitle(title);
  const main = artist.split(/\s*(?:,|&| x | feat\.?| ft\.?| and )\s*/i)[0];
  const secs = Math.round((durationMs || 0) / 1000);
  const e = encodeURIComponent;
  let hit = null;
  if (secs > 0) {
    const r = await getJson(`/api/get?track_name=${e(t)}&artist_name=${e(artist)}&album_name=${e(album || '')}&duration=${secs}`);
    if (good(r)) hit = r;
  }
  for (const q of [`/api/search?track_name=${e(t)}&artist_name=${e(main)}`, `/api/search?q=${e(t + ' ' + main)}`]) {
    if (hit) break;
    const list = await getJson(q);
    if (!Array.isArray(list)) continue;
    hit = list
      .filter(good)
      .filter((r) => !secs || Math.abs(r.duration - secs) <= 8)
      .sort((a, b) => (!!b.syncedLyrics - !!a.syncedLyrics) || (secs ? Math.abs(a.duration - secs) - Math.abs(b.duration - secs) : 0))[0] || null;
  }
  const result = hit
    ? { synced: hit.syncedLyrics || null, plain: hit.plainLyrics || null, instrumental: !!hit.instrumental, at: Date.now() }
    : { notFound: true, at: Date.now() };
  cache.set(key, result);
  chrome.storage.local.set({ [key]: result });
  return result;
}

async function coverToDataUrl(url) {
  try {
    const r = await fetch(url);
    const blob = await r.blob();
    const buf = new Uint8Array(await blob.arrayBuffer());
    let bin = '';
    for (let i = 0; i < buf.length; i += 0x8000) bin += String.fromCharCode.apply(null, buf.subarray(i, i + 0x8000));
    return `data:${blob.type || 'image/jpeg'};base64,${btoa(bin)}`;
  } catch { return null; }
}

chrome.runtime.onMessage.addListener((msg, _sender, reply) => {
  if (msg.type === 'lyrics') { findLyrics(msg.track).then(reply); return true; }
  if (msg.type === 'cover') { coverToDataUrl(msg.url).then(reply); return true; }
});

async function toggleInTab(tab) {
  if (tab && tab.id && tab.url && tab.url.startsWith('https://music.youtube.com/')) {
    chrome.tabs.sendMessage(tab.id, { type: 'toggle' }).catch(() => {});
  } else {
    chrome.tabs.create({ url: 'https://music.youtube.com/' });
  }
}
chrome.action.onClicked.addListener(toggleInTab);
chrome.commands.onCommand.addListener(async (cmd) => {
  if (cmd !== 'toggle-lyrics') return;
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  toggleInTab(tab);
});
