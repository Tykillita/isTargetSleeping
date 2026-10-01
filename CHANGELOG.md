# Changelog

All notable changes to isTargetSleeping. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow [SemVer](https://semver.org/).

## [Unreleased]

## [1.3.0] — 2026-09-30

### Fixed
- About now explicitly describes isTargetSleeping as the redesigned native Windows app. ModelNap is credited
  separately under **Inspiration**, in both English and Spanish.

### Added
- **GitHub updates:** the official public repository is configured by default. Automatic checks start enabled,
  run at startup and every 24 hours, and preserve an existing `updateCheck: false`. Manual checks are available
  in Settings and the tray menu even when automatic checks are off. Separate results for up to date, no published
  releases, incompatible packages, connection errors and GitHub rate limits; Spanish and English messages.
- **User-controlled installation:** current/new version, release notes, download progress and cancellation.
  Stable releases only, exact x64/ARM64 asset selection, HTTPS download validation, SHA-256, bounded ZIP extraction
  and executable architecture/version checks. A copy of the app runs as a background update helper after normal
  shutdown, keeps a backup until the new tray starts and restores/reopens the previous version on failure.
  Settings, history and the current portable/install location are preserved. Memory-agent updates remain separate.
- **Reviewed releases:** stable version tags validate `VERSION` and changelog notes, run tests, build portable ZIPs
  and per-user installers for x64/ARM64, verify all eight packages/checksums and create a GitHub draft. Existing
  releases are never overwritten; publishing remains a manual review step. Windows signing is optional.
- Update tests exercise public-release parsing, network errors/rate limits, corrupt or unsafe packages,
  cancellation, locked files, permission errors, backup recovery and real child-process startup confirmation.
- Optional **Pet beside Start** (tray icon's right-click menu and Settings › General, off by default): **Mira**, the
  app's own pet — its body is the crosshair ring, the tick marks are its antenna, arms and feet, and its eye is the
  logo's (closed with lashes while it sleeps, the bullseye when awake). It acts out what Ollama is doing: sleeps
  tucked into its little bed with Z's when Ollama is off, stretches while it starts, dozes with Ollama on and no model, munches a
  snack while a model loads, stands alert (the logo pose) with a model in memory, types on a tiny laptop while the
  model generates, carries a box while downloading, sweeps while freeing RAM and yawns as Ollama stops. One-shot
  reactions: sparkles when RAM is freed, dizzy stars when Ollama crashes (sad if the watchdog gives up), a yawn
  when the model naps, a jump when clicked and hearts when petted. It sweats with high memory pressure and turns
  coral when it's critical.
- **Three places to live:** peeking over the taskbar above Start, inside the taskbar just left of Start, its hand touching the Windows logo as if they were one drawing (falls back
  to above Start, with a note in Settings, when there's no room), or **walking** along the taskbar's edge up to the
  tray while a model is awake, heading back home to Start to sleep.
- **The Windows logo joins in** as part of the pet's home: a second click-through window paints "lights" over the
  real logo, pane by pane. With Ollama off the logo stays exactly as Windows draws it; with Ollama on it keeps a
  soft glow (a bit gentler than the RAM-freed one), and on top of it: the glow arrives pane by pane as Ollama
  starts, a pane flashes with each keystroke, the panes fill up like a progress bar while downloading, it glows
  amber while a model loads, a shine sweeps across now and then while alert, the glow leaves pane by pane as Ollama
  stops, and it glows with sparkles, hearts and jumps. The logo is found by
  capturing the Start button (only when Start moves, with the pet hidden for a moment) and keeping its blue pixels,
  so the lights never touch the taskbar and the pet snugs up against the logo's real edge. Clicks still go to Start.
- **Shy while it waits:** next to Start (*left of Start*), while Ollama starts or is on with no model loaded, it
  hides behind the Windows logo and peeks out from its left side with half an eye, curious; now and then it
  ducks back, blushing, until a model arrives. Whatever falls on the logo isn't drawn (and clicks there still go
  to Start).
- **Interaction** (on by default, *Interact with the pet*): click it to open the panel, right-click for its menu
  (turn Ollama on/off, put the model to sleep, load the main model, position, hide), move the mouse back and forth
  over it to pet it; its eye follows the cursor. Only its silhouette takes clicks and it never takes focus; turn
  interaction off and every click passes through.
- Drawn pixel by pixel on its own layered window at a whole-pixel scale (2× at 100 %, 3× at 125–150 %, 4× at
  200 %), so it stays crisp at any DPI. **Smooth motion:** the drawing runs at 12 fps with quick gestures and long
  rests (a blink takes a quarter of a second), while jumps (a real arc), hops, sways, walking, peeking out from
  behind the logo and moving between spots run continuously at ~30 fps, one physical pixel at a time, with easing —
  it slides instead of teleporting, and stays covered by the logo as it comes out from behind it. Ready for a **collection** of pets: each one only draws the shared poses, and
  a pet selector appears once there's more than one. *Reduced motion* shows one still pose per state.
- Lighter on Explorer: the Start button is found with UI Automation once and then only its position is read; it
  hides instantly (foreground events, not polling) when Start, Search, a game or a full-screen app opens, and puts
  itself back on top when you click the taskbar. It hides with an auto-hiding taskbar. Turning it off closes its
  window and stops its timers. `--export-pet dir` writes every frame for review.

## [1.2.0] — 2026-09-30

### Added
- **Free RAM** — what Mem Reduct does, built in: working sets, system file cache, low-priority and full standby
  lists, modified page list, combined memory lists, registry cache and modified file cache (the same areas and bits
  as Mem Reduct's `ReductMask2`). Unlike Mem Reduct, working sets are trimmed **per process** and the models (Ollama
  runners, llama-server, LM Studio) and the game being played are never touched, so the next response doesn't have to
  page the model back in.
- **Memory agent:** a small separate program (`isTargetSleeping.MemoryAgent.exe`, bundled in the app) installed once
  with a single UAC prompt into `Program Files\isTargetSleeping\cleaner` and registered as the scheduled task
  `\isTargetSleeping\Liberar RAM` (highest privileges, on demand only). Every later cleanup runs it with no prompt; the
  app reads the result from the task's exit code. Remove it from Settings (or the uninstaller).
- **Rules:** clean when RAM use goes over X % (re-arms after dropping 5 points), every N minutes, on critical
  pressure, and when a game starts (after game mode turns Ollama off); at least 3 minutes between automatic cleanups.
- **Free RAM** button in the memory card (right-click: also put the models to sleep), in the tray menu,
  **Ctrl+Alt+L**, `istargetsleeping://clean` and `--clean`. The last cleanup is shown in the card and in Activity.
- **Mem Reduct:** detects it, imports its settings (auto-clean %, interval, areas, notification) and **replaces** it —
  closes it, removes it from startup and turns its auto-clean off — with a one-click **Go back to Mem Reduct**.
- Optional **tray icon with the RAM %**, styled like Windows' own indicators: a crisp, pixel-snapped number in the
  taskbar's color over a thin gauge bar that fills with the % (blue with Ollama on, gray off, amber while switching or
  with high pressure, coral when critical). Toggle it from the tray icon's right-click menu or in Settings; the tray
  tooltip always shows RAM %.
- "RAM freed" notification (off by default, like Mem Reduct).
- Tests for the agent's command format (strict validation), the cleaning rules, the `memreduct.ini` parser/editor
  and the weekly totals with cleanups.

- **Redrawn tray icon**, hand-tuned for 16–32 px instead of the big logo scaled down: pixel-snapped crosshair ticks, a
  solid ring, the status dot as a badge with a cut-out (like Windows' own), and softer (not washed-out) ink when
  Ollama is off. The eye tells the model's state: closed with lashes while it sleeps, open — the bullseye — while a
  model is in memory.
- **Animated tray icon** instead of the on/off blink while Ollama switches: *searching* — a faint ring with an amber
  radar arc sweeping clockwise — while it starts; *lock-on* when it's on — the arc closes into the ring, the ticks
  bounce and the blue badge pops; *letting go* while it stops — the eye closes, the badge turns amber and the radar
  sweeps backwards; and the ring un-drawing to a dot and coming back dimmed when it's off. The eye opens and closes
  when a model loads or goes to sleep, and the RAM % icon gets a light sweeping along its bar while Ollama switches.
  20 fps only while something changes (the radar frames are drawn once and reused; the timer stops when idle), and a
  new state picks up from wherever the icon is. **Reduced motion** (Settings › General) turns it off — the icon then
  shows a steady amber badge while switching. Until you touch it, it follows Windows' *Animation effects*; turn it off
  to animate the icon even with Windows' animations off.
- **Main model:** star a model in the installed list and it loads by itself every time Ollama is turned on (from the
  app, a hotkey, a link, game mode or the watchdog; not when Ollama was already on when the app opened). Also from
  the tray icon's right-click menu: **Load when Ollama starts** › *None* or one of your models (listed even with
  Ollama off, from its manifests on disk, honoring `OLLAMA_MODELS`). Click the star again
  (or pick *None*) to clear it; deleting the model clears it too. The tray menu now supports submenus.
- **Always show in the taskbar** (Settings › General): the same switch as Windows 11's *Other system tray icons*
  (`HKCU\Control Panel\NotifyIconSettings`, `IsPromoted`), so the icon doesn't hide behind ^. Shown only when Windows
  has the entry (Windows 11).
- **Move the panel:** drag it by its header and it stays there (also the next time it opens), always inside the
  screen; double-click the header to put it back next to the tray.

### Fixed
- **About** now links to the app's own repository (github.com/Tykillita/isTargetSleeping), under the signature.
- The number in the RAM % tray icon changed size with the value (a "63" came out bigger than a "66"): it was fitted
  to each number's own ink. It's now fitted to a fixed "88", so every value from 0 to 99 has the same size.
- A flat dark strip across the top of the panel: DWM was painting the (hidden) title bar there, over the acrylic.
  The panel now asks DWM for no caption color.
- The Free RAM shortcut is **Ctrl+Alt+L** ("liberar"): Ctrl+Alt+M is taken by the NVIDIA App overlay.
- The close button (X) is always in the header's corner, in both Activity and Settings; the button next to it
  switches to the other view.

### Changed
- "Reclaimed" in Activity adds naps and RAM cleanups.
- The uninstaller also removes the memory agent (asks for UAC if it was installed).
- Signature: **By CodeSentry - Tykillita** (CodeSentry is Ruben Pino's software development and cybersecurity
  company) in the panel, *About*, the README, the license and the installer. The credits say it plainly: the app is
  based on the idea of ModelNap by Erik Taveras, completely redesigned and with new, original features.

## [1.1.0] — 2026-09-29

### Added
- **Notifications:** native Windows notifications (the tray balloon, shown as a notification with the logo) when a
  model falls asleep on its own, Ollama crashes or is restarted, memory becomes critical, game mode starts or ends, a
  download finishes, or an update is available. One switch per kind in Settings › Notifications; the same notice at
  most once a minute. Clicking one opens the panel.
- **Watchdog:** restarts Ollama with its last mechanism if it crashes (API down and no `ollama serve` for ~5 s) or
  hangs (server alive but API silent for ~7.5 s, killed first); at most 3 times in 10 minutes, then it gives up and
  says so. It never acts if you turned Ollama off, quit the Ollama app from its menu, or during game mode.
- **Game mode:** detects games from Steam (every library), Epic, Riot, EA, Rockstar, GOG, Xbox, your own `.exe`
  list, or exclusive Direct3D full screen; after 5 s it turns off Ollama and the other engines that were on, and
  30 s after you quit it turns back on only those, with the same mechanism. Manual changes while playing are
  respected. *Turn on anyway* and *Not a game* in the panel.
- **`istargetsleeping://` links** (`on`, `off`, `toggle`, `sleep`, `load/<model>`, `show`, `activity`), registered
  per user; a second instance forwards them to the running one via `WM_COPYDATA`. `--sleep` and `--url` on the
  command line.
- **Ctrl+Alt+S** puts the model to sleep without turning Ollama off (with its own switch); also in the tray menu.
- **GPU memory:** a second bar with the dedicated GPU's VRAM in use and the model's share (DXGI + PDH, the counters
  Task Manager uses). Hidden without a dedicated GPU.
- **Who wakes the model:** apps connected to Ollama's port (from the TCP table with owning process) are credited when
  a model loads — «Woken by Obsidian · 3 min ago» in the panel, with icons and counts in Activity.
- **Activity view:** this week's GB reclaimed, naps, hours with a model loaded and watchdog restarts; a 30-minute RAM
  chart; who wakes the model; the latest events. 90 days of history in `stats.json`.
- **Download and delete models** from the panel: `+` with suggestions, live progress from `/api/pull` and cancel;
  a trash button on hover with inline confirmation (a loaded model is unloaded first).
- **Other engines:** a standalone **llama.cpp** `llama-server` gets its own card, idle timer and auto-sleep (stop
  and relaunch with the exact same command line; Ollama blob paths are shown with their Ollama name).
- **Experimental: LM Studio** (not tested on real hardware): card and idle timer through `lms` and its local API,
  shown only if installed.
- **Updates (prepared):** with a GitHub repository set as `updateRepo`, it checks `releases/latest` at start and
  every 24 h, then downloads the zip, verifies its SHA-256, swaps the `.exe` and relaunches. Without a repository the
  option doesn't appear and nothing leaves the PC.
- App log in `Logs\app.log` (notifications, watchdog, game mode, links). `--status` lists llama-server processes and
  `--memory` shows GPU memory. The switches expose the UI Automation *Toggle* pattern for screen readers.
- Tests for the watchdog, game mode, library parsers, client attribution, statistics, pull progress, links and the
  updater (against a local HTTP server).

### Changed
- The panel has three views (main, Activity, Settings); Settings is organized in sections and every view scrolls
  within ~75 % of the screen.
- A `llama-server.exe` counts as an Ollama runner when Ollama launched it (parent process, or Ollama's runner flags),
  not merely because it lives in Ollama's folder.
- The installer also removes the `istargetsleeping://` registration.

## [1.0.0] — 2026-09-29

First release of isTargetSleeping, by CodeSentry - Tykillita.

### Added
- Native Windows tray app for Ollama: C# / .NET 10, WPF interface and direct Win32 calls, no NuGet packages; a single
  self-contained `.exe` for x64 and ARM64.
- New identity: the name isTargetSleeping and a target-with-a-sleeping-eye logo used for the app icon, the tray icon,
  the panel header and the exported SVG/PNG assets.
- Black glassmorphism panel over Windows 11's real acrylic backdrop, translucent cards, monochrome UI with blue
  (`#4DA3FF`) only for the on state; black shadows, no colored glows. Opaque black glass on Windows 10.
- Turn Ollama on and off from the panel, the tray menu or **Ctrl+Alt+O**, detecting how it's installed: the Ollama
  app (started with `hidden`, so its chat window stays closed), Windows services (SCM, UAC only when needed),
  scheduled tasks (Task Scheduler) and a manual `ollama serve`.
- Idle auto-free after 5, 15, 30 or 60 minutes, based on the runners' CPU time; recognizes `llama-server.exe`
  (Ollama 0.34+), `ollama runner` and `ollama_llama_server.exe`.
- *PC memory* card: RAM in use like Task Manager, installed total, the runner's real private memory as the model's
  share, and memory pressure.
- Installed models with size, quantization and vision/tools tags; load or free in one click.
- Open at login (`HKCU\…\Run`, respecting Task Manager's *Startup apps* switch), English and Spanish.
- Command line: `--status`, `--on`, `--off`, `--memory`, `--idle-test`, `--snapshot`, `--export-logo`.
- `build.ps1`, `package.ps1` (zip + Inno Setup installer, optional signing) and IdleTracker tests.

### Not tested on real hardware
- Starting and stopping Ollama as a Windows service or a scheduled task (the code paths exist).
