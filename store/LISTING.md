# Store submission kit

Everything to copy-paste into Microsoft Edge Add-ons and the Microsoft Store.
Privacy policy URL (after the repo is public and Pages is on): `https://jay-m007.github.io/nightverse/privacy`
Support / website URL: `https://github.com/jay-m007/nightverse`

---

## 1. Microsoft Edge Add-ons (the YT Music extension)

**Dashboard:** https://partner.microsoft.com/dashboard/microsoftedge/overview
**Upload:** `store/edge/nightverse-extension.zip`

| Field | Value |
|---|---|
| Account type | Individual (can't be changed later) |
| Publisher display name | Jay Maurya |
| Visibility | Public, all markets |
| Category | Entertainment |
| Privacy policy required? | Yes, the extension sends song title/artist to lrclib.net. Use the URL above. |
| Website | https://github.com/jay-m007/nightverse |
| Support contact | https://github.com/jay-m007/nightverse/issues |
| Logo | `store/edge/logo-300x300.png` |
| Small promo tile | `store/edge/small-promo-440x280.png` |
| Large promo tile (optional) | `store/edge/large-promo-1400x560.png` |
| Screenshots | the five `store/edge/screenshot-*-1280x800.png` |
| Search terms | lyrics, synced lyrics, YT Music, karaoke, music visualizer, now playing, lyrics overlay |

**Display name:** Nightverse Lyrics

**Short description:**
Animated, time-synced lyrics for YT Music in 14 styles: kinetic, neon, synthwave, karaoke, comic, terminal and more.

**Description:**
```
Nightverse turns YT Music into a late-night lyrics screen.

Open music.youtube.com, play any song and press the Lyrics button (or Alt+L). The lyrics appear full screen, perfectly in time with the music, in the style you pick:

• Kinetic – one line at a time, with the sung word highlighted in the album's colour
• Constellation – words appear as stars joined by lines
• Classic – cover art next to a scrolling lyric sheet
• Vinyl – a spinning record with the cover as its label
• Neon Sign, Cyberpunk, Comic Pop, Terminal, Synthwave, Karaoke, Aurora, Rainy Window, Warp Speed and Editorial

Features
• Lyrics stay in sync when you pause, skip or seek
• Colours adapt to each song's album art
• Full-screen mode and an optional clock, perfect for a desk or second screen
• Keyboard shortcuts: S style picker, T next style, Space play/pause, F full screen
• No account, no tracking, no ads

Lyrics come from LRCLIB (lrclib.net), a free community lyrics database. If a song is missing, you can add it there.

Open source: https://github.com/jay-m007/nightverse
Not affiliated with YouTube or Google.
```

**Notes for certification:**
```
Open https://music.youtube.com, sign-in is not required. Play any popular song (e.g. "Sweater Weather" by The Neighbourhood), then click the "Lyrics" button at the bottom-right (or press Alt+L). Synced lyrics appear over the page. Press S to switch styles, Esc to close.
Permissions: storage (settings + lyrics cache); lrclib.net (fetch lyrics); lh3.googleusercontent.com and i.ytimg.com (read album art to pick colours).
```

---

## 2. Microsoft Store (the Windows app)

Do this **after** the app has been run and tested on Windows (`dotnet run --project apps/desktop`).

1. Sign up free at https://storedeveloper.microsoft.com → **Individual developer (free)** → verify with your ID + selfie on your phone.
2. Partner Center → Apps and games → New product → **MSIX or PWA app** → reserve **Nightverse**.
3. Product management → Product identity → copy:
   - `Package/Identity/Name` → repo variable `STORE_IDENTITY_NAME`
   - `Package/Identity/Publisher` → repo variable `STORE_PUBLISHER`
   - `Package/Properties/PublisherDisplayName` → repo variable `STORE_PUBLISHER_DISPLAY_NAME`
   (GitHub → repo → Settings → Secrets and variables → Actions → Variables)
4. Push a tag `v1.0.0` → GitHub Actions builds `Nightverse_1.0.0.0_x64.msix` into the release.
5. Fill the submission:

| Section | Value |
|---|---|
| Pricing | Free, all markets, public |
| Category | Music |
| Privacy policy URL | https://jay-m007.github.io/nightverse/privacy |
| Website | https://github.com/jay-m007/nightverse |
| Age rating (IARC) | No violence, no user interaction, no purchases, no location. Mention lyrics may contain explicit language. |
| Packages | the `.msix` from the release |
| Screenshots | any 10 of `docs/screenshots/*.png` (1920×1080) |
| 1:1 App tile icon | `docs/store-tile-300.png` |
| 16:9 Super hero art | `store/edge/large-promo-1400x560.png` is too short; use `docs/screenshots/synthwave.png` (no text overlay needed) |
| Restricted capability `runFullTrust` justification | Nightverse is a .NET desktop (WPF) app. It needs full trust to read the Windows system media session (now playing) and to draw a click-through screen-glow overlay. |
| Notes for certification | Play any song in Spotify desktop or in YT Music in Edge, then open Nightverse. Right-click the tray icon for styles, glow and mini player. |

**Description (Microsoft Store):**
```
Nightverse gives whatever you're playing a late-night glow: animated, time-synced lyrics in 14 styles and a soft light around your screen that takes its colour from the album art.

It works with Spotify, YT Music in your browser, Apple Music and any app that shows up in the Windows media controls. No sign-in and no API keys: Nightverse reads what's playing straight from Windows.

LYRIC STYLES
Kinetic, Constellation, Classic, Vinyl, Neon Sign, Cyberpunk, Comic Pop, Terminal, Synthwave, Karaoke, Aurora, Rainy Window, Warp Speed and Editorial.

FEATURES
• Lyrics in perfect sync, even when you pause, skip or seek
• Screen glow: a click-through light around your screen edges in the album's colour
• Mini vinyl player that stays on top in the corner
• Full screen with an optional clock, made for a second monitor or a desk setup
• Play / pause / skip from the app, styles from the tray
• Starts with Windows if you want it to
• No account, no tracking, no ads

Lyrics come from LRCLIB (lrclib.net), a free community lyrics database.
Open source on GitHub. Not affiliated with Spotify or YouTube.
```

**Product features (one per line):**
```
14 animated lyric styles
Time-synced lyrics for Spotify, YT Music and more
Screen-edge glow in the album's colour
Mini vinyl player, always on top
Full screen with clock
No sign-in, no tracking
```

**Search terms:** lyrics, synced lyrics, music lyrics, karaoke, music visualizer, now playing, desk setup
