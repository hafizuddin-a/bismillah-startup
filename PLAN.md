# Bismillah for Windows: plan

The Windows version of [bismillah-menubar](https://github.com/hafizuddin-a/bismillah-menubar). Same behaviour, same
logo and icon, rewritten for Windows because none of the Swift code can run there.

**Stack:** C# on .NET 10 with WPF. Windows 10 (1809) and later, x64 first, arm64 later.
**Repo:** `hafizuddin-a/bismillah-startup`, MIT, same README style as the Mac app.
**Testing:** builds and unit tests run on GitHub Actions (`windows-latest`); the real checks need a Windows PC, listed in
phase 7. The Mac can build the app (`dotnet publish -r win-x64`) but not run it.

## What stays the same

| Mac app | Windows version |
|---|---|
| Black popup centred on screen, bismillah written out like handwriting, 25% faster than the recording | Same. The outline and 112 pen strokes are exported once from `BismillahData.swift` into a JSON file the app loads |
| Wake popup (lid open / screen wake), waits for unlock | Same, from Windows power and session events |
| Dhikr popups at random times, 3 phrases, hours, per-day count, click to dismiss | Same scheduler, ported with its unit tests |
| Show on All Screens (off by default) | Same |
| Menu bar item "بسم الله" | Tray icon (an image, since the tray can't show text) with the same menu on right-click |
| Launch at Login | Registry `Run` entry |
| Weekly signed auto-update from GitHub releases | Same scheme, Ed25519, new key pair for this repo |
| Install with curl or Homebrew | `irm ... \| iex` one-liner in PowerShell, winget later |

## What is different

- **No notch.** The dhikr opens from the top centre of the screen, like the Mac app on a notchless Mac.
- **Tray icon.** "بسم الله" is drawn into a small icon image. Windows hides new tray icons behind the `^` arrow until the
  user drags them out, so the README will say so.
- **Arabic font for the dhikr text.** Geeza Pro isn't on Windows. Use Segoe UI / Sakkal Majalla (WPF shapes Arabic itself).
  The bismillah doesn't need a font, because the outline is baked in.
- **SmartScreen.** An unsigned exe shows a warning when downloaded in a browser. The PowerShell installer avoids it (no
  browser mark on the file). Code signing (about $10 a month via Azure Trusted Signing) is optional and later.

## Phases

Sizes are rough: S = one sitting, M = a few, L = more.

| # | Phase | What gets built | Size | Done when |
|---|---|---|---|---|
| 0 | **Repo** | Folder, git, logo, icon (`Bismillah.ico` made from the Mac icon), LICENSE, `.gitignore`, README skeleton. **Done locally** (not on GitHub yet) | S | Repo exists with the same logo and icon |
| 1 | **Data and animation** | A tool exports the outline and strokes to JSON. WPF renderer draws the stroke masks over the glyph outline, with the same timings (speed 1.25). A test renders frames to PNG so they can be compared with the Mac renderer's frames | M | Frames match the Mac ones by eye |
| 2 | **Popup and tray** | Borderless always-on-top window that never takes focus, click or Esc dismisses, fade in and out, per-monitor DPI, Reduce Motion (Windows "animation effects" off), single instance, tray icon with menu, Show Bismillah | M | Runs and shows on a CI Windows runner (screenshot saved) |
| 3 | **Wake and unlock** | Resume from sleep and screen-on trigger the greeting, wait for unlock if locked | S | Verified on a real PC (phase 7) |
| 4 | **Dhikr** | `DhikrPlan` and `DhikrScheduler` ported with the same tests; top-centre popup; count and hours menus; Next / Show Now; Show on All Screens; the Sincerely, @madebyhafiz item | M | Unit tests pass on CI |
| 5 | **Settings and startup** | Settings saved in `%APPDATA%\Bismillah\settings.json`; Launch at Login | S | Settings survive restart |
| 6 | **Install and updates** | `get.ps1` installer into `%LOCALAPPDATA%\Bismillah` (no admin, so in-place updates work); updater with Ed25519 check (NSec library) and swap-and-relaunch; GitHub Actions release workflow (tag in, signed zip out); `publish` notes in the same style | L | An update from one release to the next works |
| 7 | **Real-PC test** | See checklist below | M | Everything ticked |
| 8 | **README and 1.0.0** | README with both GIFs, install section (PowerShell first), uninstall | S | Release live |

Phases 1 to 6 can all be built and unit-tested without a Windows PC. Phase 7 is the only one that needs one.

## Checklist for the first real Windows test

1. The one-line install works from PowerShell, with no SmartScreen warning.
2. The tray icon appears (look behind the `^`), shows the Arabic text legibly at 100%, 125% and 150% scaling.
3. Show Bismillah writes out smoothly and matches the Mac look; click and Esc dismiss.
4. Close the laptop lid and open it: the bismillah appears. Lock (Win+L), sleep, then wake: it waits for the unlock.
5. Dhikr: Show Now, a scheduled one, click to dismiss, the hours and count menus.
6. Two monitors: Show on All Screens off and on; monitors with different scaling.
7. Launch at Login survives a restart.
8. Check for Updates finds a newer test release, installs it, and reopens.
9. Games and full-screen videos: note whether the popup appears over them (it probably shouldn't over exclusive full screen).

## Open choices (defaults in bold)

- Self-contained single exe (**about 70 MB zip, nothing to install**) or small exe that needs the .NET runtime.
- x64 only first (**yes**), arm64 after.
- Keep the dhikr wording and phrases identical to the Mac app (**yes**).
- Windows 10 support (**yes**, 1809+).

## Risks

- Nothing runs on real hardware until phase 7, so wake, lock and tray behaviour are written from documentation. Expect a
  round or two of fixes after the first real test.
- WPF has no true transparent click-through for the area around the popup, so the window is sized to the popup itself.
- Windows power events differ by laptop (Modern Standby vs S3 sleep); the test on the user's PC covers only theirs.

## Status (7 Nov 2026)

| Phase | State |
|---|---|
| 0 Repo | Done: https://github.com/hafizuddin-a/bismillah-startup |
| 1 Data and animation | Done. Frames drawn by Skia (on the Mac) and by WPF (on a Windows runner) match the Mac app's |
| 2 Popup and tray | Done. Checked on a real PC and photographed on a Windows runner |
| 3 Wake and unlock | Done and checked on a real PC: sleep, lid, fingerprint unlock. The lock screen is recognised by four signals (lock screen picture, secure desktop, session flag, and the app's own events), and the greeting waits for the desktop (see `GreetGate`) |
| 4 Dhikr | Done, unit-tested, text drawn from the Mac app's Geeza Pro outlines |
| 5 Settings and startup | Done. Launch at Login checked across a real restart; the menu stays open for settings |
| 6 Install and updates | Done. Real in-place updates from 0.9.0 up to 0.9.5 on a real PC; signed and forged updates tested on a runner |
| 7 Real-PC test | Done for the first PC: tray, dhikr, wake/lock, restart, update. **Not yet tested:** multiple monitors with different scaling, full-screen games and video |
| 8 README and release | 1.0.0 prepared |

Known limits: Windows holds sign-in apps back about 10 s on its own; the Start menu is always drawn above the popup;
no code signing (the PowerShell installer avoids the SmartScreen warning). Possible later work: arm64, winget, a
scheduled-task start to skip Windows' sign-in delay (needs a way to do it without admin rights).
