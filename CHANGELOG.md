# Changelog

All notable changes to isTargetSleeping. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow [SemVer](https://semver.org/).

## [Unreleased]

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
- Optional **tray icon with the RAM %** (amber/coral with pressure); the tray tooltip always shows RAM %.
- "RAM freed" notification (off by default, like Mem Reduct).
- Tests for the agent's command format (strict validation), the cleaning rules, the `memreduct.ini` parser/editor
  and the weekly totals with cleanups.

- **Move the panel:** drag it by its header and it stays there (also the next time it opens), always inside the
  screen; double-click the header to put it back next to the tray.

### Fixed
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
