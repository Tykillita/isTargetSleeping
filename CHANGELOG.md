# Changelog

All notable changes to isTargetSleeping. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow [SemVer](https://semver.org/).

## [Unreleased]

## [1.30.1] — 2026-11-09

### Added
- A «Legacy version (tray panel)» item in the tray menu turns the legacy version on or off.
- New installations open the notch's hover view with Music, To-do, Pomodoro, Shelf and System, across five pages with arrows, dots and the full-view dock. The closed notch shows the time by default; saved widget and pill choices remain intact.
- The Music widget keeps the last track and cover locally after its player closes. Play reopens a supported player and requests playback when it restores that same track; without playback history, it shows the bundled jazz sample with its starry-sky cover.

### Changed
- The closed notch detaches its full and hover views, and attaches them only while open. Music services also wait until the Music page is visible unless the pill or a live activity needs them.
- The hover view can keep up to eight selected widgets, one widget per page.
- Live activity controls move from Widgets to Settings › Alerts. All activities are enabled by default except the optional camera indicator shown while the camera is off.
- System widget graphs, Dock buttons and Favorites move from Widgets to Settings › Notch.
- Tall clock, battery and Shelf widgets center sparse content so it does not sit at the top with a large empty area below.
- Todo completion marks, disabled labels and green buttons use readable foreground colors.

### Fixed
- The legacy version (tray panel) now renders when turned on: the panel builds its own view each time it opens instead of reusing the notch's, which left it empty and gray.
- The agent beam redraws at a bounded 60 fps and reuses its drawing pens.
- The compact notch no longer preloads the hidden full panel: its layout was blocking the UI thread for about 1 s and stalling animations.
- The hover preview reserves the full panel width, so opening it from the mini dock does not shift the notch horizontally.
- The full notch attaches its panel after resizing the transparent window, cutting the pause before the spring animation; its opening spring now runs for 340 ms with the same easing curve.

## [1.30.0] — 2026-11-06

### Added
- The expanded notch keeps up to four cells on Home; each additional widget gets a dedicated page reachable from the header pager. Existing widget order and saved selections are preserved, with the twelve-cell total limit unchanged.
- The private mobile web app can choose Nimbo's provider, model and sign-in method. API keys stay encrypted on the desktop PC.

### Fixed
- The solo clock and calendar view no longer reserve an unused second row below the calendar and agenda.

## [1.29.0] — 2026-11-05

### Fixed
- Existing custom activity lists now include the notification indicator after updating, so notifications can use the closed notch's priority slot as well as the open header.
- A permission request no longer opens the notch or takes focus while something is full screen (a game, a video) or game mode is on: it waits in the queue (and in the Agents page) and the card opens by itself when you leave full screen. Before, a pinned notch ignored the full-screen rule and could pull you out of the game.
- «Deny» in the agent permission card is readable in high contrast: the red tint behind its red text is replaced by the theme's plain background.
- Team task merges now work when Git has no user identity configured; the merge uses a local fallback identity.

### Added
- Optional connected pet companion: download the separate desktop server from Settings › Notch. It serves the mobile web app privately over Tailscale, works without an ESP32 board, and can connect a board as an optional client.
- Bluetooth connection card in the closed notch (the «Bluetooth headphones» live activity): when a Bluetooth device connects or disconnects it shows its name, «Connected · 50%» (battery when Windows reports it) and its icon, with the battery as a ring around the Bluetooth badge. It now names the device and covers any Bluetooth device (headphones, mouse, keyboard…), not just audio outputs.
- Richer session rows on the Agents page: under each session's name, its git branch (read straight from `.git/HEAD`, also inside worktrees, with no git process) and how much context it is using, in thousands of tokens, taken from the last answer in its log (Claude Code sessions only; it is the context in use, not a percentage, because the window size is not in the log).
- Circuit breaker for the Team widget's scheduled tasks: a task that fails ten times in a row switches itself off (it keeps its history) instead of burning tokens forever, shows «paused after 10 failures in a row» in the schedule list and raises an alert on the Agents page. A success resets the count, cancelling or missing a run does not count, and turning the task back on by hand starts again from zero. «Every N hours» tasks already keep a minimum of one hour between runs.
- Decision time for permission requests (Settings › General): choose how long the notch waits for your answer (30 s, 1 min, 2 min or the maximum) before leaving the permission to Claude Code's terminal. Claude Code cuts the hook at 5 minutes, so that stays the maximum.
- The Agents page «Now» tab is now a single inbox: pending permissions, questions and plans, the while-you-were-away summary, agent alerts (stuck, looping, usage limits) and routines paused by the circuit breaker, followed by your sessions.
- Control over agent alerts (Settings › General): choose «Everything» (default), «Only what matters» (permissions, questions, plans, serious alerts and the while-you-were-away summary) or «Nothing», and snooze for 30 minutes, 1 hour or until tomorrow morning. It decides whether something chimes, the notch bell rings or the notch opens by itself; requests and alerts always stay on the Agents page (with «Nothing» a permission waits there without opening the notch or making a sound).
- Resume a session in a real terminal: on the Agents page, every Claude Code or Codex session that nobody is using (finished or idle, with a resumable id and an existing project folder) has a «Resume in the terminal» button. It opens Windows Terminal (or PowerShell) in the project folder and runs `claude --resume <id>` / `codex resume <id>`, so you carry on yourself. Unlike the read-only question box of the Agents widget, this hands you the real CLI. Sessions that are working are never offered, because two writers would trample the same session file. The Agents page also lists recently finished sessions now.
- «While you were away»: when you come back after 10 minutes or more without touching the keyboard or mouse, the pet sums up what your agents did meanwhile (how many finished, how many permissions were requested, how many alerts went off and how many requests are still waiting for you). It appears as a card on the Agents page, as a pet message in the Chat page and with the notch bell, and it stays quiet in game mode and when nothing happened.
- Notch light for your agents (Settings › General, on by default): a beam of light runs round the notch's edge, entering at the right ear, going down the side, along the base and out at the left ear (it never crosses the top, next to the screen), with a sharp tip and a tail that fades smoothly. Blue while an agent works, amber when something waits for your answer, red when there is an alert (stuck, looping or a usage limit used up) and green for a few seconds when one finishes. If another state arrives while a beam is running, the new colour waits for the current lap to end instead of overlapping it. It stays still (a plain ring) with reduced motion and takes the text colour in high contrast.
- Agent alerts on the Agents page (Now tab), with a bell in the notch the first time each one appears and a short mention in the closed pill: an agent that has shown no signs of life for 2 minutes (it may be stuck or waiting on a long command), one that repeats the same tool on the same file six or more times in a row (a possible loop), and a usage limit (Claude, Codex, Gemini) that is at 90 % or used up, with a countdown to when it comes back. They are computed locally from the session logs and the AI usage widget.
- Answer Claude Code's questions and review its plans from the notch. A question (`AskUserQuestion`) shows its options as buttons, one choice per question or several when the agent allows it, and «Send answer» replies with them; a plan (`ExitPlanMode`) shows its text with «Approve the plan» and «Ask for changes» (which tells the agent you want changes). «Decide in the terminal» and Esc leave either one to the terminal. Claude Code only accepts these answers from a `PreToolUse` hook, so turning on «Approve Claude Code permissions from the notch» now also installs a `PreToolUse` hook filtered to `AskUserQuestion|ExitPlanMode` (it never touches other tools). Questions and plans are never saved as «Always» rules, and the Y key does not answer a question.
- Agent center: an «Agents» page in the notch dock (shown while approving permissions from the notch) with three tabs. **Now** lists every permission request waiting for you, with Allow / Always / Deny right in the page, plus your latest agent sessions. **History** is a local log of what each agent asked and what was answered (allowed, denied, always, automatic or left to the terminal), saved as `decisions.jsonl`, trimmed on its own and clearable. **Rules** lists every «Always» rule so you can revoke any of them.
- Sensitive-command guard: recursive deletes, force pushes, `git reset --hard` / `clean`, downloaded scripts piped to a shell, `sudo`, disk formatting, dropping databases, shutdown, bulk permission or registry changes, and writes to credentials or sensitive settings now show a red warning on the card. They cannot be allowed with the Y key, cannot be saved as «Always» and no saved rule lets them through on its own; the warning is also kept in the history.
- Voice for your pet's chat, fully local: a microphone button on the Chat page listens for one phrase at a time (press to talk, never always-on) with the speech recognizer installed in Windows, and an optional switch makes the pet read its answers aloud with a Windows voice. No audio leaves the PC. Needs a Windows speech recognizer for your language; without one the microphone is hidden. Uses the `System.Speech` package.
- Weekly summary on the Chat page: messages, agents that finished, permissions granted and denied, Pomodoros and model naps this week, counted locally (`week.json`) and reset every Monday.
- Ambient chatter (Settings › General, off by default; Never / Rarely / Sometimes): now and then the pet adds a short, kind line to the Chat page and rings the notch bell. It does not use the model, picks the line from its mood and bond, keeps a minimum gap (50 min or 2 h) and stays quiet in game mode, with a permission pending or while listening.
- Bond and mood for your pet, shown on the Chat page. The bond grows with healthy use (chatting, an agent finishing, finishing a Pomodoro), with a daily cap per kind so it cannot be farmed, never drops when you are away, is stored locally (`bond.json`), can be reset, and does not reward freed RAM. The mood follows the context (worried when memory is critical, alert when an agent waits for your permission, happy for a while after something good, sleepy at night, calm otherwise) and, with the bond level, shapes how the pet talks in the chat.
- Chat with your pet (Settings › General, off by default): a «Chat» page in the notch dock where the local model you already have loaded in Ollama answers in short, friendly messages. It never loads a model on its own (if none is loaded it says so), nothing leaves the PC, the conversation only lives in memory, and answers stream in with a Stop button. Each pet talks in its own character (Mira dry and watchful, the llama proud and dramatic, the capybara calm, the orange cat playful), the same one its animations already have.
- Approve Claude Code permissions from the notch (Settings › General, off by default). Turning it on adds `PermissionRequest`, `Stop`, `Notification`, `UserPromptSubmit` and `SessionStart` hooks to `~/.claude/settings.json` (with a timestamped backup, never touching other hooks) and turning it off removes them. A permission request opens the notch with a card showing the agent, project and exact command, and **Allow / Always / Deny** buttons (Y, N, Esc on the keyboard; Esc leaves the decision to the terminal). Several requests wait their turn. «Always» is a rule of its own for Claude Code only (same project, same tool and, for Bash, the identical command) kept in `hook-rules.json`. The hook talks to the app through a named pipe restricted to your user (no network); if the app is not running or nobody answers within 280 s the hook exits quietly and Claude Code asks in its terminal as usual.
- Short, soft chimes when an agent asks for permission or finishes (synthesized by the app, switchable in Settings › General), and the taskbar pet sparkles when an agent needs you and jumps when it finishes.

### Changed
- Add a **Processes** tab to the app window; it reuses the existing process explorer and keeps Activity › Processes in the notch and tray panel.
- Animate the notch hover control dock as it opens and closes, including an exit transition before its space collapses.
- Smooth the notch's widget page transitions and avoid rebuilding the Home and widget folders when navigating.
- Adopt the sleeping slime as the app icon, with an updated animated tray icon that keeps status and the optional RAM percentage.
- Use native Windows notifications with the full-resolution logo, an Activity button for sleeping models and progress for detected download layers. Keep the tray notification as a fallback.

## [1.28.0] — 2026-10-27

### Added
- Notch screens (Settings › Display): by default the notch lives on the main screen, like Nusa's; pick the screens that carry one (one, several or all of them, each fixed to its own monitor) or let it follow the cursor, as it used to. Duplicated monitors share a single notch.
- «Hide in full screen» switch (Settings › General, on by default) with finer rules: only the monitor of the full-screen app is affected, Start and Search always hide it, an app list (name with or without `.exe`), and a locked session or the secure desktop (UAC) hide it everywhere. Hiding is immediate and showing again waits a quarter of a second, so Alt+Tab no longer flickers.
- «Open the notch» (hover or click) and «Animation speed» (normal or fast) in Settings › General.
- Short hover view in pages (one widget by default, or two or four), with arrows, page dots, mouse wheel and ← →; a hidden mini dock grows below the notch when you hover the dots. Settings are reorganized in a sidebar with colored icons and a title per page. The closed pill is smaller (150×30) with the clock and AM/PM in the middle, a battery ring, white live indicators (including a vibrating bell for notifications), and the cover and equalizer for music.
- Notch mascot (Settings › Notch, off by default): a white mascot with its own look takes over the closed notch. It appears after about 40 s without keyboard or mouse input, asleep (closed eyes); if something happens it wakes up for a while (eyes that look around, blink, squint and smile) and dozes off again. When the user comes back it opens its eyes, turns happy (smiling arched eyes and blushing cheeks) for about two seconds and fades out, and the normal notch (clock, data, indicators) returns. Live activities and the camera appear in their usual place beside it, on small black capsules over its white body; the battery ring is hidden while it is shown, and only music and opening the notch send it away. Drawn as WPF vectors, honors reduced motion and high contrast, and with «Minimal notch» the strip widens while it is shown.
- System popups in the closed notch, like Nusa's: volume, brightness, Caps Lock, Num Lock and keyboard language (with an EN/ES chip) appear for about two seconds as a wider black capsule with an icon and a bar or state, instead of a small icon in the pill.
- Synced lyrics (Settings › General, off until you press the lyrics button on the music page): a lyrics button in the hover music view opens a wide view with the current line highlighted and the cover and controls on the left, and the closed notch can show the line being sung (with the cover colors tinting its lower edge) instead of the clock. Lyrics come from lrclib.net, which receives the song's title and artist; found lyrics are cached on disk.

### Changed
- Notch open and close animations: a damped spring (about 350 ms to open with one soft overshoot, about 300 ms to close without dipping below the pill) with the content cross-fading with the pill and the window requesting the monitor refresh rate; the notch closes 0.15 s after the mouse leaves (it used to take 0.4 s). Opening no longer captures the screen in the middle of the animation.
- The notch is now the main interface and is on by default. The tray panel is now the «Legacy version», off by default and switchable in Settings › Notch.

### Fixed
- The lyrics pill and the opened lyrics view now share one cover-tinted gradient layer that stretches with the notch shape, so opening and closing no longer show a black box where two separate gradients crossed; the notch window also keeps a constant size on hover so the pill does not jump.
- Opening the notch on hover no longer makes the lyrics flash: the lyrics view restarts from the current line instead of replaying a multi-line jump, and the Ollama and LM Studio polls wait while the notch animates.
- The notch no longer stays over a video when it enters or leaves full screen without changing the foreground window (F11), and no longer stays hidden after leaving it; both used to wait for the mouse to touch the notch.
- A hidden-app rule no longer hides a notch you pinned, and a locked session, UAC or a disconnected remote session no longer leave it drawn over the lock screen.
- The hidden-apps field said the notch «passes clicks through»; it hides, and now says so.

## [1.27.1] — 2026-10-27

### Added
- Show shortcuts to installed, recognized music apps when nothing is playing; when none are found, use a bundled original slow New Orleans jazz preview that starts paused, plays once by default and loops only on request.
- Expand the opt-in AI usage widget beyond Claude, Codex and Gemini: read Muse Code quotas through its `muse usage` command, show active models from Ollama's local server, and show xAI Grok API team spend when `XAI_MANAGEMENT_API_KEY` and `XAI_TEAM_ID` are configured. Keep quota bars only for providers that report quota percentages; the gallery example remains Claude, Codex and Gemini.
- Add a 1×1 view for Ollama, Code and LM Studio. Code uses the project name, detects common stack markers and shows a short README excerpt locally; larger sizes keep the existing editor and engine controls.
- Make the Teleprompter useful in 1×1 with script editing, play/pause, restart and a reading progress line; show an empty-script prompt.
- Detect webcam use from Windows' local camera-privacy usage state or its on-demand Frame Server and show a status dot beside the notch automatically. Camera can also be enabled as an idle live activity or as a dock page; its local preview starts only with the switch and stops when leaving the page.
- List apps currently streaming from the camera below its preview. Comet's Manage button opens site camera permissions, where Discord Web's camera access can be blocked; Windows only offers a global camera setting for other desktop apps.
- Show the configured name in live activities when local Codex or Claude Code scheduled runs finish and expose their task identity in their session logs; the watcher does not show task prompts or results, and cannot observe cloud runs.

### Changed
- Give widget folders dark smoked glass with a soft top reflection; keep their fixed 3×3 footprint and fill icons left to right by row.
- Refine the camera activity as a true circular badge with a centered status dot; its active state uses a soft blue halo, while the idle state stays a plain neutral dot.
- Keep awake now reflects the last state Windows accepted, reports failures, and lays out its status and switch together.
- Replace the gallery's horizontal edge fog with soft 3D cloud columns centered just outside each viewport edge, covering the exposed edge of the side cards while the navigation stays above them. Layered smoke wisps flow slowly inside a fixed silhouette; clouds fade out on larger sizes and return on 1×1, and stay hidden in high-contrast and outline themes.
- Finish AI usage reads with a translated error state if provider detection fails instead of leaving «Consultando…» indefinitely.

### Fixed
- Improve icons in My apps by extracting high-resolution executable icons and trimming transparent padding, so app logos look sharper and fill their tiles more evenly.
- Keep the camera indicator circular on the left when a live activity appears on the right; only content sharing the left capsule changes its shape.
- Wrap AI provider error and extra-usage details beneath the provider name so compact widgets no longer cut off the explanation.
- Update camera, brightness and volume live activities promptly instead of waiting for slow polling or a full panel refresh.
- Close open widget folders and option sheets when the notch collapses, including automatic mouse-leave collapse, so they do not reappear over Home.
- Keep the gallery's existing WPF card views through the 3D transition instead of replacing them with bitmap clones. Defer content rebinding until each card has left the 3D viewport, preventing the brief zoomed replacement before the normal card returns.

## [1.27.0] — 2026-10-24

### Changed
- Redesign the Widgets page gallery as an inline iPhone-style 3D glass carousel: the selected widget stays interactive at its chosen size, neighboring widgets appear as separated non-interactive previews, and Notes uses its yellow accent for the add/remove pill. A short 3D transition moves the current widget back and brings the next one to the center; previews never sit behind the center card, and cards keep their intended size without a sudden zoom. High-contrast and outline themes keep flat surfaces and clear borders.

## [1.26.0] — 2026-10-20

### Added
- Redesign the Notes widget after iOS's Notes: several notes (the first line is the title) with a yellow header band and a dotted line under it, a list with title, date and preview, and an editor. One cell shows a note (title, preview, date) with arrows to move between notes, two cells in a row show three titles with their dates, one cell by two rows the long list, and two by two the list beside the open note. Tapping a note opens it; the band has new, delete and back buttons. Notes live in `notes.json` (the old single `notes.txt` becomes the first note) and the widget now fits in one cell too.
- Redesign the Ollama widget (a power button with its state, the loaded model with how long it has been idle, memory and GPU as rings, the actions, and in two rows the installed models as chips plus the automatic-release countdown), add an LM Studio widget (its local server, the loaded model, an idle bar, unload, and in two rows its downloaded models to load or unload with a tap) and a Local models launcher widget (Ollama and LM Studio side by side, one tap loads or unloads a model; one design per size).
- Add the AI usage widget: for each AI subscription already signed in on this computer (Claude Code, Codex, Gemini CLI) it shows the plan, «updated N min ago», the 5-hour and weekly usage bars with their reset time (bars turn amber and red near the limit), extra per-model quotas, a refresh button and a refresh every five minutes, in one design per size (1×1, 2×1, 1×2, 2×2) plus an optional notch-pill item with each weekly percentage. Off by default (Settings › Notch › Local features): it reads each CLI's local credential file read-only and asks that provider's own usage endpoint, keeping the token in memory only. Providers without a session do not appear; Grok and Ollama Cloud have no reliable way to read usage, so they are not shown.
- Redesign the AI Agents widget: a glance for one cell (how many agents are working, their live badges and who is waiting for you), three session cards for two cells in a row, a list of session cards with state pills for one cell by two rows, and the list with the selected session's detail beside it for two by two. The Pomodoro's start button now sits centred above its four-stage dots, and the current stage is a donut.
- Limit the Team board's «Done» column to the five most recent tasks (newest first, Team tasks and sessions from other apps together), with a «Show more» button that lists them all; the column count still shows the total. The effort button now shows only the gauge.
- Add the LED lights widget for Bluetooth LED strips (ELK-BLEDOM, MELK…), after the Lotus Lamp X app: find strips by name, power, colour wheel and ten quick colours, brightness, the strip's own effects with speed, scenes, lights that pulse with the volume of what plays, and on/off alarms, for several strips at once; each strip is remembered and reconnects by itself. Sizes: power, colours and brightness (one cell); wheel as well (two cells); five tabs in the tall sizes.
- Redesign the Pomodoro: a four-session cycle (a 15-minute long break after the fourth focus) shown as four dots and «Session n of 4», a phase chip, a solid start/pause button, a skip-phase button, and a different layout for each size (time and progress bar; a big progress ring with the time inside; the ring beside the session, hint and controls).
- Add a second look for the notch, «Light with outlines» (Settings › Notch › Notch look): a white background, black text and a thin outline around every piece, with flat surfaces and no glass; Windows high contrast still takes priority. The default stays the black glass.
- Make the Battery widget an iOS-style Batteries widget: besides this computer it lists every Bluetooth accessory that reports a level (mouse, keyboard, headphones, controller…), read from Windows without pairing or permissions and refreshed every minute, each with its own icon (laptop, mouse, keyboard, headphones, speaker, controller, phone, watch, pen, or Bluetooth when the type is unknown). One cell shows up to four rings with the percentage, two cells in one row up to six rings with names, and the tall sizes a list with icon, name, level and bar; a ring turns red below 20 %.
- Style the widget gallery's «Add to Home» button like iOS's: a wide pill in the Home's black acrylic with a plus in a circle.
- Give Music, Battery, Audio, Notes, Teleprompter and Agents designs that fit each grid size (the gallery preview now has exactly the size the widget will have on the Home, and the grid never lets a design spill over its cell), show sample data in the gallery while a widget has nothing real to show and its real data as soon as it has some, and let a widget offer only the sizes that make sense for it (Stay awake: 1×1 and 2×1).
- Redesign the Team widget for the Home grid: one row shows its three-step pipeline and status, one cell by two rows the task box plus the pipeline, and two by two the task box beside the plan; with a task running the plan goes below at full width. Widgets now stretch to fill their cell, and the gallery preview sits on the Home's dark glass at the exact cell size.
- Add a widget gallery to the Widgets page, like iOS's: one widget at a time with its name and what it does, a live preview at each size (1×1, 2×1, 2×2, 1×2), a pager and an «Add to Home» button that places it at that size. Widgets stay card-less on the Home; the preview sits on its own glass tile.
- Give the Pomodoro a tall design (a large progress ring with the time inside), show every forecast hour in the wide one-row Weather, and lay the one-row wide Clock out like the one-cell one so nothing is cut.
- Lay the Home out on an invisible two-column grid with fixed-height rows: each widget takes 1×1, 2×1, 1×2 or 2×2 cells (`id@CxR` in the stored layout; the resize handle cycles through the sizes that fit), a one-row slot uses the dense design and taller ones the full design, widgets are placed in the first free gap, and a widget whose content needs more rows grows instead of being cut. The Home budget counts cells (columns × rows, up to 12; the first 2×2 are visible on opening).
- Add the Team widget: a router that splits a task between Claude and Codex (real Codex models read from `~/.codex/models_cache.json`), with presets, a per-task git worktree and branch, cross review, a token cap, an editable plan to approve, follow-ups on the same branch, an in-notch diff viewer and merge/discard.
- Give the Team widget three selectable looks in Settings (Pipeline, Timeline, Crew), card-based Task / Plan / Result pages, a History page with filters and a Scheduled page where recurring tasks run while the app is open or from `isTargetSleeping --team-due` in Windows Task Scheduler.
- Add an optional Team pill item (progress and active agent), live activity (with progress ring) and a «Cancel the team's task» favorite, and name scheduled runs in the history after the schedule's title.

- Give the Team task box a glass prompt-bar design: attach up to thirty images (button, drag and drop or paste; sent to Codex with `-i` and to Claude through its Read tool, and deleted when the task ends), pick the split from the bar and set the agents' effort from Auto to Max with a tachometer dial (Claude `--effort`, Codex `model_reasoning_effort`). Enter launches and Shift+Enter adds a line.

- Answer simple questions with no project: a question such as «what does otorhinolaryngologist mean?» is detected automatically and answered by one fast agent (Haiku; Sonnet with Quality; Codex with Saving or Codex only) with web search, in an empty folder, with no git repository, branch or merge. The full answer stays in History and a Windows notification and a live-activity indicator say it is ready.
- Add a Board tab to the Team widget (and a List view of it) that groups every task into «Needs your attention», «Running» and «Done», each card with its agent, status and numbers. Click a card to unfold its steps and the actions that apply: approve or reject a plan, retry a failed task, open it or cancel it. The history now keeps the full task text so a failed task can be retried.
- Let the cheapest models do the cheap steps: clarifying a vague task now uses Haiku (or Codex's light model with Saving), and a simple question falls back to the other cheap model (Haiku or Codex's light model) when its agent is not installed or signed in.
- Show on the Team board the Claude Code and Codex sessions started from other apps (terminal, Codex app, Claude Code on desktop or in an editor), grouped into «Needs your attention», «Running» and «Done» like the team's own tasks, with a button to open the session's folder. It reads the same local logs as the AI Agents widget, read-only, and only if that reading is turned on (the board offers a button for it).
- Turn every list on the Widgets page (Home widgets, hover-view widgets, system graphs, live activities, closed-pill data, dock buttons and favorites) into iPhone-style glass folders: each folder shows its options as mini icons and a badge with how many are on. Tapping one opens it over the page, like an iPhone folder (big name and a glass card of icons), and tapping an option opens its own window: a preview of how it really looks in use (the real widget in one and two cells, alone in its list when it has a large view, the real closed pill, live activity or dock capsule), its size, position and on/off switch. Escape closes the window, then the folder.
- Redesign the AI Agents widget: each session is a card with its agent's badge (animated while working, with a warning mark when it needs you), a colored status chip, the project, the current action and the time since its last activity; the selected session opens a detail panel with its badge, statistics (last activity, changed lines), a summary with an accent rail, and an expandable section with the tool, file, the agent's last message (formatted) and a timeline of recent activity. The tabs are a segmented control and the empty state shows both agents' badges.
- Format the Team's answers: answers to simple questions (and the text of plans and reports) are shown as Markdown (headings, bold, lists, code and quotes) instead of raw asterisks, in a card with a copy button, and the answer prompt now asks for a one-sentence answer first, then short Markdown details.
- Keep the tray icon out of the hidden-icons menu (^) by default on Windows 11: the app promotes its icon to the taskbar as soon as Explorer registers it, and a new «Show the icon in the taskbar» item in the icon's right-click menu (next to the existing Settings switch) turns it on or off. If you turn it off, the app remembers and leaves it alone.
- Redesign the About window in the notch's liquid-glass style (glass tiles with ink icons like the widget folders, a black pill for the version, glass credit cards) and rewrite its text for what the app is today: a hero with the icon, name and version, a one-line description of the notch with widgets, the Claude + Codex team, AI agents, Ollama, RAM cleaning and system data, six feature tiles, and credit cards for the author and for the ModelNap inspiration.
- Follow up a closed answer: open an answered question in the Team history and ask another question or follow-up (a bare «and in English?» works): the same model answers with the previous conversation (up to three turns) as context, the new answer links back with «In reply to», and answered cards on the board get an «Ask» action.
- Continue real Claude Code and Codex sessions from the AI Agents widget: a finished or inactive session shows a «Continue this session…» box that resumes it with `claude --resume` or `codex exec resume`, read-only (it cannot edit files or run commands), and the new turn appears in the widget by itself. Sessions that are working, waiting for you or are automations cannot be resumed.
- Show «Copied» with a green check on the answer's copy button for a couple of seconds, and spin the «Working» status icon on board cards.
- Show a live log of what the Team's agent is doing right now: short phrases such as «Reading App.xaml», «Searching “WeatherWidget”» or «Thinking…» (never the reasoning or the messages), the last few with how long ago, and «Last move: 4 s ago» so a long step is not mistaken for a hang. Claude steps now stream their events (`stream-json`) and Codex steps are read line by line.
- Redesign and animate the Team pipeline: threads between steps light up with a glow as they are completed, sparks run along the one that is working, a light arc turns around the working agent and its step gets a tinted cell.
- Show the Team's running task in the AI Agents widget (Claude steps leave no session log, so before it looked like nothing was working) and hide the Codex steps' duplicate sessions.
- Show a plan waiting for approval with formatting (headings, lists, code) instead of raw text, with an «Edit» button that switches to the editable text, and say «Waiting for your approval» instead of a stale «Planning…».
- Add a Windows notification setting for the Team («Team: answer or task ready») and make the Team live activity (progress, then «ready») show by default.

### Fixed
- Stop limiting the size of attached images: an image over 10 MB asks for confirmation first («going over 10 MB can cost more tokens than usual») and is attached only if you press Continue.
- Fix the Team task box not refreshing its attached images: after the first one, pasting more or removing one had no visible effect.
- Redesign the Team effort dialog: the level in large coloured text next to the dial, a bar that fills with the level's colour (blue, amber at Max), no explanatory paragraph.
- Fix the Team step picker: clicking a step's icon or its name now rotates through Haiku, Sonnet, Opus and every Codex model (Astra, Luna, Sol, Terra…), then back to the router's choice, in every step. Before, the icon only rotated between agents and a Codex model could only be changed from a Codex step.
- Fix attaching images to a Team task: dropping files on the task box no longer sends them to the file tray, and Ctrl+V / Shift+Insert attach a screenshot or copied image files (a text box does not even run «paste» when the clipboard has no text).
- Make the send button and Enter explain why a task can't be launched (no project folder, not a git repository) instead of doing nothing.
- Close the Snipping Tool by itself when it freezes the screen: if its window is marked «not responding» for 8 seconds, or its full-screen overlay stays up for more than 30 seconds (Windows keeps reporting it as responding), the app closes `SnippingTool` and `ScreenClippingHost` and writes it to the log. It runs on its own thread, so a slow notch cannot delay it, and it never touches Game Bar.
- Fix the AI Agents recent-sessions filters clearing themselves right after choosing one: the option lists were rebuilt on every read, so the drop-down lost its selection and sent an empty value.
- Stop Team plans from using Claude Code's plan mode: it tried to call ExitPlanMode (which does not exist here), wrote the plan to a file in `~/.claude/plans` and returned a short summary that started with an apology. Read-only steps now run in normal mode with read-only tools, the plan is requested complete and in sections (goal, files to touch, steps, risks, tests, done criterion), and any leftover ExitPlanMode apology or «do you approve?» question is removed.
- Fix the Team board showing a task twice while its plan waited for approval (once «Needs your attention» and once «Running»), and stop the run clock while it waits for the approval.
- Fix Team questions that mention «code» (for example «give me 3 colour palettes with hex codes») being routed as a project task and failing with git's «not a git repository»; questions about the chosen project («what does this program do?») are now answered by reading its folder, read-only, instead of replying that there are no files.
- Fix files with accents or ñ in their name showing as `"Ã±…"` in the Team's changed-files list with an empty diff.
- Show a cancelled Team task as «Cancelled» (not «Failed»), let queued tasks be cancelled from their card, and say «Cancelling…» while the agent stops.
- Stop retrying (and escalating to another model) a step whose CLI is missing or that timed out, and show the real reason («Claude Code is not installed.», «Claude timed out after 8 min») instead of a bare «step failed»; reject empty tasks in the queue itself (retries and schedules too), and explain a folder without git in plain words without leaving empty folders behind.
- Stop the implementer from carrying out plan steps that are conditional or outside the request; say in the report which model approved the review; make «Approve» with an empty box approve the original plan.
- Show Team presets with a description of what each does (and drop the jargon in them), add a non-technical example question, show a Markdown-free summary on board cards, show each board step with its full model name, use one duration format and the app's own thousands separator, name the days of the week in the schedule editor for screen readers, rename the «Weather» widget in Spanish to «Clima», show the clock from the first frame, and correct the image-limit message.
- Hide the one-step plan card once a question has been answered so the answer sits right under the form instead of at the end of a long page; show Codex commands in the live log without disk paths or whole scripts; translate the model effort («medium» → «medio») in the Crew view; keep only the file name of files the agent links by disk path in the report; show «2 h» instead of «2 h 0 min», decimals with the app's own separator («58,3k»), and «Done» as a capitalised label.
- Cut what the model says before the first section of a plan («Confirmed: … I have what I need»), remove the empty project folder left in the Team's data folder after a discarded run, say «Choose the project folder» instead of a message with an empty name, and in high contrast show secondary text with the main colour and do not dim the preset descriptions (they dropped below readable contrast on the selected row).
- Describe the Team presets without jargon, label the closed preset and effort controls («Preset: Auto», «Effort: Auto») so the two «Auto» are told apart, give the Weather widget a cloud icon instead of the people icon, and make the live log say what a command or search does («Listing files», «Searching the code», «Running the tests») instead of showing raw commands, regex patterns or glob patterns.
- Replace the Weather city text box in Settings › Notch (it did not receive the keyboard when the notch was not pinned, and «City, Country» was not understood by the search) with two selectors: country, with its flag, and city. The 36 countries and 62 cities carry their coordinates, so only latitude and longitude are sent to Open-Meteo; a city typed in an earlier version still works. Flags are images from the MIT-licensed «flag-icons» project (`Resources/Flags`, with its licence file).
- Make a click on a text box in the notch activate the window and give the box the keyboard, and keep the notch from closing while typing.
- Redesign the Weather and System widgets: Weather shows a drawn condition icon, a large temperature, the condition, the city, the high and low and an hourly strip with icons; System shows each metric as a card with its last-minute curve and a usage bar, and its short view shows CPU and RAM as lines with bars instead of plain text. Usage bars turn amber from 70 % and red from 90 %, the curves are filled underneath, the weather icon has a soft glow in the colour of the weather, the first hour reads «Now» and a temperature curve runs under the hourly strip.
- Redraw the weather icons with depth: a sun with a gradient core and long and short rays, clouds as a single shape with a soft gradient, shade underneath and a highlight, rain drops of two sizes, snowflakes, a storm cloud that lights up with the lightning, and fog lines that fade at the ends. At night (19:00 to 6:00) clear or partly cloudy skies show a crescent moon with twinkling stars instead of the sun; in high contrast they are drawn flat in the text colour.
- Redesign the wide Clock and Calendar view: the weekday in large type with the full date, the day bar under the time, and three sober glass tiles (year progress as a ring that brightens along its path with the day number in the middle, the week number with its seven days, and today's real moon with its illumination and days to the next full moon); the calendar shows the year next to the month and dims weekends.
- Let the Home take more widgets: the first 4 cells are what shows when it opens and any others you add (up to 12 cells in all) appear below as you scroll; the hover (short) view stays at 4 cells. The Widgets page says so and counts the cells against 12 for the Home.
- Fix the Weather widget showing no hourly forecast late in the evening: it asked Open-Meteo for today only, so after the last hour of the day there was nothing left to show; it now asks for two days.
- Fill the free space of the short views: Weather shows the next three hours and the high and low next to the temperature, and System shows up to four metrics (CPU, RAM, then GPU, disk or network).
- Animate the main Weather icon very gently (sun rays turning slowly, clouds swaying, rain and snow falling, a flash on storms, fog sliding; off when Windows animations are off) and redesign the short Clock view: bigger time with a day bar (the sky from midnight to midnight, what has passed in full colour and what is left dimmed, marks at 6, 12 and 18 h with a small sun at noon, the real moon of the day in its current phase at both ends (computed from the mean lunar cycle, no network; drawn with a cream gradient, craters, a soft glow that grows with the light, earthshine on the dark part and a fine edge on the terminator), and a softly pulsing dot for now; its tooltip reads «Day elapsed · 43 % · Waxing gibbous»), the date as a small calendar leaf (weekday in red, day number, month) and the next event with an icon.
- Stop automatic RAM cleaning from freezing the screen until a reboot. It no longer empties the working set of Windows shell and graphics processes (`dwm`, `explorer`, text input, Snipping Tool, Game Bar, NVIDIA helpers); automatic cleanups (percentage, interval, critical pressure, game) never include the slow areas (full standby list, modified pages), pause while the Snipping Tool or Game Bar is in front, and wait at least 5 minutes between percentage cleanups (the 1 and 2 minute choices are gone). Manual cleaning keeps every area you selected. The memory agent moves to 1.6.2 and asks for administrator permission once to update.
- Run the global keyboard hook used for Caps/Num Lock and brightness keys on its own thread, so a slow interface can no longer delay the keyboard of the whole PC.
- Fix the Music widget logging a cross-thread error every second (2,500+ lines in three days), and summarize repeated identical log lines instead of writing each one.
- Protect Notes from corrupt or inaccessible storage: preserve the original file, back up before recovery, write atomically, keep the previous note if a backup fails, and offer undo after deletion. Show when the 50-note limit is reached.
- Make scheduled Team runs reserve work across app instances, record runner exceptions as failed, clean attached files after every outcome, and launch `.cmd`/`.bat` agents through `cmd.exe`.
- Keep Code widget navigation inside the selected project by checking reparse points when expanding, opening, reloading or saving files.
- Add regression coverage for Home's exact-fit, overflow and resize cell limits; correct Weather's local time and next-hour forecast, and LED alarm timer rounding and one-shot consumption.
- Improve accessibility: expose SeekBar, RulerPicker and ColorWheel values to UI Automation, contain keyboard focus in folders and sheets and restore it on close, and pause LED animation during simulated high contrast.

## [1.25.2] — 2026-10-19

### Fixed
- Restyle the AI Agents history filters with the app's glass theme, bounded dropdowns, readable selected rows and visible keyboard focus instead of the white native Windows menu.

## [1.25.1] — 2026-10-19

### Fixed
- Accept the official package's `THIRD-PARTY-NOTICES.md` entry during verified update extraction, while continuing to reject all unrecognized ZIP entries.

## [1.25.0] — 2026-10-19

### Added
- Give the AI Agents widget fixed Active/Recent tabs, a 12-hour local history of finished or inactive sessions, and search and filters for recent sessions by agent, project and state.
- Show an expandable session overview with elapsed inactivity, completed-command counts and unique edited-file counts based only on explicit log events and paths.
- **Phase 1 (1.21.0) — Audio and notch controls.** A local audio widget controls each active Windows audio session, remembers its level by executable and adds mute. Optional notch HUDs report brightness on supported internal displays, Caps Lock/Num Lock and connected audio endpoints. Settings add glass intensity and a list of apps that hide the notch.
- **Phase 2 (1.22.0) — Widget editor and system graphs.** Customize Home by adding, dragging, reordering and resizing compatible widgets, then save or discard the draft (four cells maximum). System charts show two chosen metrics or all six in the wide view, and sample only while visible.
- **Phase 3 (1.23.0) — Local coding-agent activity.** An optional widget lists recent active Claude Code and Codex CLI sessions and shows project, state, current and recent tools, file, changed-line counts and short agent-message excerpts. User prompts and thinking stay hidden; log data never leaves the PC.
- **Phase 4 (1.24.0) — Calendar, weather, OCR and teleprompter.** Calendar events show remaining progress and meeting links. Optional Open-Meteo weather uses the city you choose. Windows OCR copies recognized text from a tray image or selected screen region to the clipboard. A local teleprompter saves its script and scroll settings.

### Changed
- Keep “Needs attention” visible after a session goes inactive, and show how long it has been waiting; label stale working sessions as inactive instead of guessing they are paused.
- Keep recent messages, tools and file details collapsed until requested; never expose user prompts, raw command arguments or command output.
- Refine the AI Agents widget with a quieter glass surface, a labeled activity detail and a more balanced two-column session layout.
- Show every recent active Claude Code and Codex session in a selectable roster, with readable project names, live tool activity and short agent-message excerpts; user prompts and thinking remain hidden.
- New controls use the shared spring motion and liquid-glass styling; reduced-motion preference disables these animations. Audio sliders change the same per-app levels as the Windows mixer.
- All network features are optional and off by default: automatic update checks remain manual unless enabled, and weather sends the chosen city only when enabled.

### Fixed
- Hide the calendar's next-event bar when there is no upcoming or active event.
- Match the calendar's current-day marker to the reference with a 30-pixel circle and roomier date rows.
- Replace the Home "Customize widgets" label with an accessible edit icon.
- Read large Codex session logs from a bounded tail and recognize its current activity events in the Agents widget.
- Prioritize the brightness live indicator over lower-priority right-side activities and replay every Windows brightness event, including repeated reports of the same percentage.
- Align Home widget headers by row; each row now starts together and waits for its tallest widget.
- The brightness HUD now checks the internal display once per second, so short changes are not missed by the previous four-second interval.
- Restore the RAM-cleaning notch indicator for settings left with only the three optional HUDs by an older build.
- Listen for Windows brightness-change events as well as polling the current level, so brightness updates appear promptly.
- Detect Fn+F5/F6 brightness-key events as a fallback and confirm the internal display level changed before showing the notch HUD.
- Keep the Agents recent-session toggle readable in narrow layouts and use Claude Code titles or a Codex session ID suffix for session names.
- Distinguish active agent tools from completed work and summarize recent actions such as edited files and ran commands.
- Preserve agent work-summary categories beyond the six visible activity rows and collapse repeated identical tool events.
- Render scheduled-session heartbeat metadata as a readable automation result instead of raw XML, and use its automation ID as the session title.
- Keep the recent-session list selected across live refreshes while other sessions are active.
- Keep the Active/Recent selector visible in both widget layouts, including when either list is empty.
- Music from a web app installed from the browser (YouTube Music in Chrome or Edge) showed a long internal identifier where the app
  name goes. Known apps and browsers now show their name (Spotify, YouTube Music, Chrome, Edge…) and an unknown internal
  identifier shows nothing.
- Notch HUD, AI agent, weather and OCR switches now refresh immediately and preserve the default live activities when toggled.

## [1.11.0] — 2026-10-16

### Added
- Music in the closed notch pill: "Music" is now one of the items you can choose for the closed pill (Widgets > Closed
  pill). It shows an animated playing indicator and "title · artist", and when the text doesn't fit it scrolls inside a
  fixed width (with a pause at the start, looping) so the pill never grows. It hides itself while nothing plays, and when it
  is a pill item the "Music playing" live activity is not repeated. The live activity got the same indicator and scrolling.
- Tray: much better. Rows are bigger, with real thumbnails for images and the Material icons for folders; their actions
  light up on hover and double-click opens the item. Folders show how many items they hold and their size (measured in the
  background); "Open as a project" sends a folder to the Code widget (adding it to the Home if needed). New "Paste" (adds the
  files you copied in Explorer) and "Drag all" (takes every item at once), a "Drop to add to the tray" cue while you drag over
  the notch, a drop zone when the tray is empty, a full-tray notice, and correct singular/plural. Optionally the tray can be
  remembered between sessions (Settings > General, off by default; only the paths are stored).

## [1.10.0] — 2026-10-15

### Added
- File tray: drag files or folders onto the notch and it opens its new Tray page. It keeps only their paths (no
  copies, in memory); from each row you can drag the item back out to Explorer, mail or anywhere else, show it in
  Explorer, copy its path or remove it, and "Zip everything" writes a .zip to the Desktop without ever overwriting an
  existing one. The dock only shows the Tray button while something is waiting there.
- Code widget: a small IDE in the notch (two cells, off by default). Pick a project folder and it shows its file tree
  with the file and folder icons of Material Icon Theme (the VS Code extension, MIT), opens files in tabs (up to 8)
  and lets you edit and save text files with Ctrl+S, reload them, or view images. It keeps each file's encoding and
  line endings, saves atomically, never overwrites a file that changed on disk without asking, refuses binaries, non-UTF
  text and files over 1 MB instead of damaging them, hides `.git`, `node_modules`, `bin` and `obj` and does not follow
  links. Only the folder path is remembered; unsaved text lives in memory. Text is colored by language, letters only, in the editor of the widget and of the
  tray: Markdown (headings, code, bold, links, lists, quotes, tables), C-like languages, Python, JSON, YAML, XML/HTML/XAML,
  CSS, SQL and shell scripts, with the Material-style palette of VS Code (not for files over 200 KB or with the Windows
  high-contrast theme). Long lines wrap inside the card, so there is no horizontal scrolling. The hover view just
  summarizes it, and when it is the only widget its editor grows to fill the card.
- Quick text editing from the tray: the Edit button on a file opens it in the notch to read and change it, Ctrl+S
  or Save writes it, and Reload re-reads it from disk. It keeps the file's encoding (UTF-8, with or without BOM, or
  UTF-16) and line endings so diffs stay clean, saves atomically, refuses binaries, non-UTF text and files over 1 MB
  instead of damaging them, and never overwrites a file that changed on disk without asking. Unsaved text survives
  collapsing the notch, and closing with changes asks first.
- Clipboard history (Settings > General, off by default): keeps the last 25 texts you copy, only in memory. It never
  writes to disk, never keeps images or files, skips what password managers mark as private, can be paused, and is
  emptied when you turn it off or quit. Its page and dock button exist only while it is on.
- App favorites: the dock favorites (still up to four) can now launch any app, shortcut or file you pick with
  "Add an app…" on the Widgets page; they show up with the file's name and are removed from the same list.

### Changed
- Under the app name, the panel and the notch Home now show the version of isTargetSleeping (for example "v1.10.0")
  instead of the Ollama version and backend.

### Fixed
- The open notch no longer blinks. When the closed pill changed width (the clock ticking over, or a live activity such as the
  Pomodoro, the volume or the music showing up or going away), the notch redid its whole opening animation even while it was
  open, fading its content out and in. Now it only repaints when something really changed.

## [1.9.0] — 2026-10-15

### Added
- Live activities beside the closed notch pill: what is happening right now shows next to its data and the pill
  grows to make room. They are Freeing RAM, Turning Ollama on or off, Critical memory, a running Pomodoro, Keep
  awake and, if you turn it on, the music that is playing. Up to two at a time, the most urgent first; each one has
  its own switch on the Widgets page.
- Volume indicator in the notch: when the Windows volume changes (or is muted), the closed pill shows it for a couple
  of seconds as a live activity ("98 %" or "Muted"). It is shown in addition to the Windows indicator, which can't be
  removed in a supported way. It only reads the volume and never changes it; it has its own switch.
- Clickable progress bar: click or drag the song's progress bar in the Music widget to jump to that point.
- Swipe to skip tracks: dragging across the Music widget with the mouse or a finger, or scrolling horizontally with
  two fingers on a trackpad, goes to the next or previous track.
- Music widget (one or two cells): title, artist, cover art, progress and previous, play/pause and next controls for
  whatever Windows reports as playing (Spotify, the browser, the media player…). It reads the system's own media
  session locally and only starts when a list or the live activity needs it; the "Music playing" live activity is off
  by default.

## [1.8.0] — 2026-10-15

Includes the 1.7.0 notch dock, which was never published as a separate release.

### Added
- Widgets for the notch. Its new Home page holds up to four cells of widgets, in the order you choose: Ollama
  (status, memory, GPU, Free RAM, Free), System (CPU, RAM, VRAM, disk and network), Clock and calendar, Pomodoro
  (25 min focus, 5 min break, with a notice when a phase ends), Keep awake (stops Windows from sleeping the PC and
  the screen until you turn it off or quit), Notes (plain text saved on its own in `notes.txt` next to the
  settings) and Battery. Everything is read locally; nothing is sent anywhere.
- Customizable hover view: what the notch shows when you move the pointer over it is now its own list of widgets,
  chosen and ordered on the Widgets page (Ollama by default, as before). The Free RAM and Free model buttons stay in
  place while they work, dimmed and reading "Freeing…", instead of vanishing and shifting the others.
- Customizable closed pill: choose up to three items from status, model, RAM, VRAM, CPU, clock, battery and the
  Pomodoro timer, and put them in order, on the Widgets page (status, model and RAM by default, as before).
- Widgets adapt to where they are and who keeps them company: a one-cell widget that has no partner in its row
  widens to two cells and switches to its wide view (System in two columns, Clock with the month beside the time,
  Pomodoro and Battery in one row), and in the hover view widgets are denser (System without disk and network,
  Clock without the month, shorter Notes).
- Widgets page: turn each widget on or off and move it up or down, with a counter of the cells in use. A widget
  that doesn't fit asks you to remove another first.
- Configurable favorites: up to four one-tap actions next to the dock navigation, chosen from turn Ollama on/off,
  Free RAM, Free the model, Processes, Keep awake and Pomodoro. The default stays turn on/off, Free RAM and
  Processes.
- Notifications has a new "Pomodoro finished" switch.
- Optional notch dock (Settings > General > "Notch dock (experimental)"). A pill docked to the top edge of the
  screen on the monitor where the cursor is: hovering it opens a short view of Ollama (status, RAM and GPU bars,
  Free RAM, Free model and Processes) and a click opens the full two-column panel. Esc, a click elsewhere or
  leaving the pointer closes it. The tray icon and its menu keep working.
- Two strips of icon buttons below the notch, like a dock: navigation and quick actions that act without opening
  the panel.
- "Dynamic Glass" look: black at the screen edge fading to glass, curved shoulders where the notch meets the
  edge, and a fade-in of the content after the shape has grown. Honors reduced motion.
- The notch hides while a game, video or the Start menu is full screen, and follows the cursor to another
  monitor when it touches that monitor's top edge. Every notch button has an accessible name.

### Changed
- The memory agent behind Free RAM is no longer reinstalled with every release. It has its own version
  (`AGENT_VERSION`) and only needs updating when its code changes, so Free RAM keeps working after updating the
  app, without another administrator prompt. A test fails if the agent code changes without bumping it.
- Free RAM and Free from the notch, when the agent is missing or outdated, open the notch on Settings instead of
  doing nothing.
- In the notch, the dock navigation is Home, Ollama, Activity, Widgets and Settings, and the notch opens on Home.
  The tray panel is unchanged: it has no Home or Widgets pages.

## [1.6.1] — 2026-10-14

### Fixed
- Turning Ollama off also closes the `ollama serve` this app started, whichever backend is selected. Starting
  Ollama from the app and then opening the Ollama app used to leave that server running and report it as
  another program's, so the panel kept showing "on" with an "incomplete shutdown" notice.
- Freeing RAM while Ollama is on no longer confuses the watchdog or the pet. Ollama can answer slowly while
  Windows trims memory; that was read as a hang, so the watchdog killed and restarted it and the capybara went
  into its tub, came out to wash, and went back in. Now the panel stays on, the loaded models stay listed and
  the watchdog waits during the cleanup and for 20 s afterwards.

## [1.6.0] — 2026-10-14

Includes the 1.5.3 fixes, which were never published as a separate release.

### Added
- Activity shows the dedicated VRAM of every GPU separately: total, in use and the share used by models.
  Values Windows or the driver do not provide read "Unavailable" instead of an estimate. The summary on the
  main panel is unchanged and these readings never trigger a shutdown.
- Keyboard focus ring and a high-contrast theme that follows the Windows setting.
- Isolation check with a stand-in `ollama.exe` (`tests/BackendIntegration/isolation/verify-isolation.ps1`):
  stopping one scheduled task or the app-started binary leaves another task and a manual server running.

### Changed
- Turning Ollama off no longer closes every `ollama serve` process. It closes only what it can prove belongs to
  the selected backend: the running instance of the scheduled task, the children of the Ollama app, or the
  binary the app started (identified by PID and start time, so a reused PID is never hit). Any other server is
  left running and the app reports an incomplete shutdown. The watchdog's restart of a hung server uses the
  same path.
- Every button, including icon-only ones, segmented selectors and the log links, has an accessible name, and
  all of them (and the switches) can be reached with Tab. A UI test covers it in Spanish and English.

### Fixed
- Updater extraction maps accepted archive entries to fixed filenames and verifies each resolved destination
  stays inside its staging directory (previously listed as 1.5.3).
- Build workflow limits `GITHUB_TOKEN` to `contents: read` (previously listed as 1.5.3).

## [1.5.2] — 2026-10-03

### Added
- Reproducible Windows integration verification for Ollama service and scheduled-task backends.
  Both pass two lifecycle cycles using the published app and genuine Ollama 0.35.1 on a Windows Server 2025 x64 VM.
  The bilingual validation record distinguishes real OS integration from physical-PC, inference and UAC testing.

## [1.5.1] — 2026-10-03

### Added
- **Pin the icon from Windows:** when the direct "Always show in the taskbar" switch isn't available
  (Windows 10, or Windows 11 before Explorer registers the icon), Settings › General has a button that
  opens the Windows taskbar settings, where the icon is turned on: Personalization › Taskbar › Other
  system tray icons (Windows 11) or Select which icons appear on the taskbar (Windows 10).

### Fixed
- **The "Always show in the taskbar" switch tells the truth again:** if Windows created a new icon entry
  (for example after the icon changed), the switch could show on while the icon stayed behind the `^`
  menu. It now reads all of this `.exe`'s entries instead of a cached list, only shows on when every
  entry is promoted, and re-promotes all of them when toggled.

## [1.5.0] — 2026-10-02

### Added
- **Each pet cleans up in its own way** while RAM is freed, instead of all four sweeping: Mira scans the taskbar
  with a beam from her eye, pulls the loose data fragments towards her and compacts them into a little cube that
  bursts into sparkles; the llama shakes the dust out of her wool, the kitten washes itself and the capybara, awake,
  scoots her bottom along the floor without moving from her spot (she leans forward over her front leg with her rump
  planted, then drags the rump forward, flattening, and leaves a dust mark that stays behind; pose fields `Drag`,
  `Rump` and `Skid`) and stays seated, sparkling, when it's done; if she was asleep she
  climbs out of her hot tub, wrings out her fur with water drops flying and ends up dry. New pose fields `Shake`,
  `Wet` and `Beam` (Mira's scanner beam, aimed where her pupil looks), a `Cube` prop and `Drop` and `Fragment`
  particles (fragments fly towards Mira's eye).
- **An activity can look different depending on where it came from:** `PetBrain` records `ActivityFrom` and
  `ReactionFrom`, the director passes them on (`PetSituation.From`/`ReactionFrom`) and profiles look clips, motion,
  particles and reactions up by `PetContext` (activity plus origin, implicitly built from a `PetActivity`).
  `ProfileKit.AfterBed` registers the version of an activity (and of its reactions) that starts by leaving the bed:
  the capybara's wringing and its finale.
- **In-bed reactions:** asleep (or yawning into bed) the pet reacts to clicks, petting, the watchdog and sparkles
  without leaving its bed and without moving, with its own version per species and a fallback for anything not
  defined. Reactions record the activity they started in (`PetBrain.ReactionDuring`), which drives clip, duration,
  motion, particles and logo lights; asleep, a reaction doesn't light the Windows logo.
- `--export-pet` adds cleaning from bed and awake, going back to bed, every reaction in bed, both ends of a cleanup
  (awake and after the bed), the after-bed cleaning loop and getting on and off the crocodile.
- **The capybara's crocodile:** while Ollama starts, a crocodile walks in from the right; she gets up (out of her
  hot tub first if she was asleep), waits for it standing, jumps onto its back, lands on her feet and only then sits
  down, and they ride while it starts; once it's on she stands up on its back, jumps down and watches it leave
  standing. Standing on the crocodile, her body and feet are lifted onto its back (`CapybaraPet`).
  The ride always finishes: profiles can hold an activity with `HoldFor` (the cleanup hold now uses it too), and the
  capybara holds *waking up* for the arrival plus one ride loop. New pose fields `Croc`, `CrocStep` and `Ride`, and
  `UI/Pets/Crocodile.cs`: a pixel-art crocodile as wide as the pet (brow and yellow eye, toothy snout, back crests,
  light belly, tapering tail and stepping legs). With nothing to do the capybara now sits, with her own seated sprite
  (upright chest on a straight front leg, folded haunch), also for idle gestures, reactions, working, eating and
  riding; she stands to walk, carry the box, wring out her fur and get on or off the crocodile.
- **Memory now** in Activity: physical RAM, committed memory (with what it means), page files and the system cache
  (current and peak), each with a bar — the figures Mem Reduct shows, read with `GlobalMemoryStatusEx` and
  `NtQuerySystemInformation` (`MemoryBreakdown`).
- **Minimum pause for the percentage rule** (Settings › Free RAM, 1–30 min, 5 by default; pref `cleanCooldown`):
  while RAM stays over the chosen percentage, automatic cleanup repeats at most that often.

### Changed
- Cleaning stays visible: `PetBrain` keeps the cleaning activity at least for its intro plus one loop (at most 6 s)
  and the "RAM freed" sparkles are now its finale (`React(…, finale: true)`), played in the cleaning pose before
  going back to the requested activity, instead of arriving after the pet was already back in bed.
- Pet animation profiles take the activity a reaction started in (`For`, `ReactionDuration`, `Still`, `Motion`,
  `Drips`, `Burst`); `ProfileKit` adds per-activity reaction tables and `InBed`.
- **Mira's animations redone** on `ProfileKit` like the other three (`MiraProfile`; the original `PetAnimations`
  clips are gone, `PetMotion.cs` keeps the logo lights and the walker): anticipation before jumps, overshoot on
  landing, arms out of sync, a "boot" wake-up that calibrates her reticle, a radar sweep while on patrol, the box on
  her head steadied with both arms up, a celebration jump with her eye darting around and a pupil that spirals when
  dizzy. Her arms are outlined tick marks with round hands, her feet are rounded and the antenna has a beacon that
  lights up while she scans, pings or locks on.
- **The llama has no arms anymore:** the two stubs on her chest are gone and every animation was redone around her
  neck, ears, hooves and a woolly tail that now moves (`Tail`). She carries food in her muzzle, types by pecking the
  laptop to the beat of her hooves, prances when petted and has her own end of cleanup (fluffed-up wool, tail high).
- **Automatic cleanups are full again:** threshold, interval, critical and game cleanups clean the areas selected in
  Settings for every unprotected process, like the button (in 1.4.0 they trimmed at most ten idle processes and only
  working sets). The selective mode is removed from the protocol (`m`, `u` and `s` are rejected) and from the agent,
  together with `CleanSelection`. The agent re-checks live exclusions at most every 250 ms instead of once per process.
- Automatic rules (`CleanRuleTracker`) have independent clocks: the percentage acts as soon as RAM reaches it, even
  before the interval, and repeats after the minimum pause while RAM stays high; the interval counts from the last
  cleanup of any kind; critical pressure acts immediately when it starts and repeats after the minimum pause. At least
  one minute separates attempts; failures retry after 3, 6, 15 and then 30 minutes instead of stopping after three.

### Removed
- `PetProp.Broom` and the pose field `Sway` (only the broom used them).
- **Mem Reduct integration:** the Settings card (import settings, replace, go back), `MemReduct.cs` and the agent's
  command to close it (`x=memreduct` in the protocol and `--install … --close-memreduct`). isTargetSleeping cleans
  with its own agent and never needed Mem Reduct; the cleanup areas keep its `ReductMask2` bits.

### Fixed
- Long cleanups keep each pet's contextual finale before returning to bed or idle; game mode still cancels it.
- **The kitten, the llama and Mira fell back asleep on every loop while Ollama started:** their *waking up* loop
  started and ended with closed eyes, so a start longer than one loop showed them waking up again and again. The
  stretch and yawn now play once, as the transition out of bed, and the loop is an awake routine (washing the face,
  chewing, calibrating the eye, looking around) that never closes the eyes.
- The capybara no longer stands up just to yawn before getting into her tub: she yawns seated.
- **The process window can be closed:** its glass frame left no visible window buttons. A ✕ button in its header
  (and Esc) closes it and brings back the panel on Activity.
- **The taskbar pet vanished after window changes:** taskbar thumbnail previews, Alt+Tab and the Snipping Tool
  capture are invisible Explorer/system windows covering the whole screen, and Windows reports *busy*
  (`QUNS_BUSY`) while they are in front, so the pet took them for a full-screen app and hid — until another window
  took the foreground, sometimes not even on the desktop. Full screen now means a visible, uncloaked, activatable,
  not click-through window of an app other than the shell (`TaskbarPetLayout.FullscreenApp`); `QUNS_BUSY` alone no
  longer hides it. The pet also goes back above the taskbar whenever the taskbar ends up in front of it (checked
  after each foreground change and every second), not only when the taskbar is the foreground window.
- **Freeing RAM skipped most applications** (regression in 1.4.0): when the foreground window was the taskbar, the
  tray or the desktop, `explorer.exe` was protected as the foreground application together with every descendant —
  practically everything opened from Start (about half of the trimmable memory on a typical session). Protection no
  longer expands through the Windows shell, both in `ProcessMonitor.ExpandProtection` and in the agent's live check.
- Protected Windows processes that deny access even to administrators (antivirus, PPL) are skipped, as in 1.3 and
  Mem Reduct, instead of counting as failures: a normal manual cleanup no longer always ends as *Partial*.
- **The configured percentage was ignored** (regression in 1.4.0): every automatic rule, the interval included, ran
  only when Windows reported high physical pressure (85 % load) and the agent stopped trimming below 85 %, so a 70 %
  threshold never acted between 70 and 85 %. After a threshold cleanup it also waited for RAM to drop five points
  below the threshold, so with RAM staying high it never cleaned again. Game cleanup required high pressure too.

## [1.4.0] — 2026-10-01

### Added
- **Process explorer in Activity:** the five applications using the most RAM, plus a resizable window with
  applications grouped by executable path, owner and session. Expand PIDs, search name/path/PID, filter type and
  minimum RAM, and sort RAM, CPU, name, count or PID. Private resident RAM is used where Windows supports it;
  older versions explicitly show total resident RAM. Committed memory remains a separate detail.
- **End applications, processes or trees:** every forced termination confirms its scope, PIDs and observed RAM.
  PID plus creation time, held handles and individual exit checks protect against reused PIDs and incomplete
  trees. Critical Windows and app/agent processes are blocked. Access denied offers an explicit UAC retry through
  the installed agent, separate from its cleanup task. Managed servers ended here stay manually stopped.
- Cleanup reports include request ID, duration, outcome, process counts and Win32/NTSTATUS errors, written
  atomically in the protected agent directory. Metrics retain the observed change immediately and at 5/30 seconds,
  including negative changes; weekly cleanup totals use the five-second observation.
- Bilingual process-window tests cover filtering, identity-stable selection, missing metrics, critical processes,
  virtualized tables, confirmation and visibility/pause/minimize/close sampling. Termination tests use disposable
  child-process fixtures.

### Changed
- The process explorer and its confirmation follow the app's black glass design: panel-style header with the logo
  and summary, Fluent icon buttons, the segmented type filter from Settings, glass search and menus, a glass-card
  table with section-style headers, chevrons and thin RAM bars, and only the actions that apply to the selection.
  The confirmation lists every affected process in a glass card, warns in amber and keeps coral for **End**.
- **Automatic cleanup is selective:** physical RAM pressure must be high or critical. At most ten low-activity
  processes of the current user/session, with at least 128 MiB resident RAM and at most 1 % of one CPU core over
  two seconds, have their working sets trimmed. Models, engines, games, foreground applications and descendants
  remain protected; candidates and pressure are rechecked before each trim. Manual cleanup retains configured
  Mem Reduct-compatible areas, with global caches and volume writes labelled as advanced operations.
- Cleanup requests from panel, shortcut and CLI are serialized and track their specific agent instance. A running
  agent remains pending after 130 seconds and blocks another cleanup. The protocol rejects requests beyond 1024
  protected identities or 32,000 characters instead of silently dropping protections.
- Critical pressure takes priority over threshold and interval triggers. Automatic failures retry after three
  and six minutes, up to three attempts per episode. Game cleanup waits for confirmed engine shutdown and cancels
  when the game ends, the option changes or shutdown fails.
- Process sampling runs every two seconds while visible; pause, refresh, virtualized rows, cached icons/metadata
  and stable identities limit work and preserve selection. The window retains size, position and sorting.
- Existing settings and history are preserved; the memory agent is updated through the existing UAC installation flow.

### Fixed
- Ending a process that Windows was already closing (for example a console host exiting with its console) is no
  longer reported as access denied: an access-denied termination now waits briefly for the exit before offering
  an administrator retry. This also made the real termination test pass reliably.
- Cleanup failures no longer appear as successful RAM freeing. Success, partial completion, failure and no work
  have distinct results. Coincident rules consume one successful episode, failed attempts do not count as success,
  and critical pressure can be handled from startup.

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
- Three taskbar companions alongside Mira: a curious white llama, a relaxed capybara and a playful orange kitten.
  Each has its own activities, gestures, reactions and walking pace; choose one in the two-column thumbnail
  picker in Settings or the pet's context menu. The choice persists, with Mira as the default and fallback.
- Species animation profiles share reaction timing across the brain, director and previews; switching species
  clears old reactions and particles. Reduced motion uses species-specific static poses without animation timers.
- Pet review sheets cover every species, both walking directions, reduced motion and 1x–4x rendering on light/dark
  taskbars. `--snapshot output.png pet --pet llama` previews an individual pet; `settings demo --pet llama` shows
  its selection without changing saved preferences.
- Unique sleeping beds for every pet: Mira's blue capsule, the llama's Andean woven cushion, the capybara's
  leafy nest with a water lily, and the kitten's padded wicker basket. Beds enter and leave with the sleep pose.
- **Pets with personality:** each pet now has its own choreography, click reaction, idle gestures, particles and
  way of treating the mouse. **Mira** (watchful hacker) sweeps a radar ring from her eye, locks a red reticle on
  the cursor, her antenna sends signals and bits rise while she works; a click is a radar "ping". The **llama**
  (proud, curious, a bit dramatic) trots head-high, chews sideways, hums with notes, carries the box on her back
  under a woven blanket, stretches her neck toward the cursor and snorts a little cloud when clicked. The
  **capybara** (zen) barely flinches — a slow blink and a bubble — sleeps in a steaming wooden hot tub, has a
  bird land on her back, balances the box on her head with the mandarin on top and looks at the cursor slowly.
  The **kitten** (playful) crouches with dilated pupils when hovered and pounces if the cursor stays still for
  1.2 s, puffs up and swipes when clicked, grooms, kneads with purrs, swishes her tail, sits on the laptop and
  inside the box, and sleeps curled up with her tail around her.
- Hiding behind the Windows logo also depends on the pet: the llama peeks over the top like a periscope, the
  kitten shows her ears first and then her eyes while her tail sticks out the other side, and the capybara
  doesn't hide at all — she leans on the logo.
- New particles: steam, bubbles, falling leaves, music notes, purrs, snort clouds, 0/1 bits and antenna signals.
- `--export-pet` sheets now show each pet's own gestures (with their particles) and a mouse row, at 2× and 3×.

### Changed
- Each pet has its own animation profile instead of one shared animal choreography with speed switches.
- Sharper animal sprites: the kitten has slit pupils that dilate, ears that swivel and flatten, an expressive
  tail and a bristling arched back; the llama has long eyelashes, a bigger topknot, a chewing jaw and tucked legs
  when lying down; the capybara has heavy-lidded eyes and a wobbling mandarin. The capybara's leafy nest is
  replaced by the hot tub.
- Refined animal sprites: the capybara now has a raised pixel-art head, honey-colored fur, a blunt snout,
  four small connected feet with alternating steps and a mandarin, without arms; the llama has fluffy
  wool and hooves, and the orange kitten has tabby markings and a curved striped tail. Collection review sheets
  compare awake and sleeping pets on light and dark backgrounds.

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
