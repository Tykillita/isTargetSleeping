// isTargetSleeping · web
// El HTML está en español; aquí va el inglés, el selector de idioma y las pocas cosas interactivas.

const EN = {
  'nav.funciones': 'Features',
  'nav.como': 'How it works',
  'nav.privacidad': 'Privacy',
  'nav.instalar': 'Install',
  'nav.preguntas': 'FAQ',
  'nav.descargar': 'Download',
  'nav.novedades': 'What’s new',
  'tema.grupo': 'Color theme',
  'tema.claro': 'Light theme',
  'tema.oscuro': 'Dark theme',
  'tema.sistema': 'System theme',

  'hero.eyebrow': "Version 1.20.0 · Native Windows",
  'hero.titulo': 'A notch for Windows: Ollama, your PC and your tools, at a glance.',
  'hero.sub': 'isTargetSleeping lives next to the Windows clock or, if you like, at the edge of the screen as a notch. It turns Ollama on and off, puts its models to sleep when they stop working and gives you your RAM back, and adds widgets: system, clock, Pomodoro, notes…',
  'hero.meta': 'Free and open source · Latest published version: 1.20.0 · Windows 10 and 11 · x64 and ARM64',
  'hero.escritorio': 'The isTargetSleeping panel open on the Windows desktop',
  'hero.panelAlt': 'Panel: Ollama on, PC and GPU memory, llama.cpp and the installed models',
  'cta.windows': 'Download for Windows',
  'cta.como': 'See how it works',

  'pill.nativa': 'Native: .NET 10 and Win32',
  'pill.cuentas': 'No accounts',
  'pill.idiomas': 'English and Spanish',
  'pill.mit': 'Open source, MIT',

  'video.eyebrow': 'Version 1.5.2, in 77 seconds',
  'video.titulo': 'Take a closer look.',
  'video.pie': 'In 1080p, with sound, the current pets and app screenshots. Sample data.',
  'video.otro': 'Watch in Spanish',
  'video.reels': 'Also for Reels: 9:16, 77 seconds, larger text and visible, audible mouse clicks.',
  'video.reelEs': 'Download in Spanish',
  'video.reelEn': 'Download in English',

  'fun.eyebrow': 'Features',
  'fun.titulo': 'For Ollama and for everything else.',
  'fun.sub': 'Turn Ollama on, off and keep an eye on it, and set the notch up with your widgets. No terminal needed.',

  'siesta.eyebrow': 'Automatic',
  'siesta.titulo': 'Stops generating? It goes to sleep.',
  'siesta.texto': 'After the idle time you pick, the model leaves memory. Ollama stays on and reloads it by itself with the next message. Want it now? Ctrl+Alt+S.',
  'siesta.grupo': 'Idle time',
  'siesta.nunca': 'Never',

  'atajo.eyebrow': 'Global shortcut',
  'atajo.titulo': 'From any window.',
  'atajo.texto': 'Turn Ollama on or off without opening the panel, even in full screen.',
  'atajo.s': 'puts the model to sleep',
  'atajo.l': 'frees RAM',

  'pets.eyebrow': 'Pets with personality',
  'pets.titulo': 'Someone tells you what Ollama is up to.',
  'pets.texto': 'Mira, a llama, a capybara or an orange kitten moves into your taskbar, right by the Start button. Drawn pixel by pixel, crisp at any scale.',
  'pets.novedades': 'Everything new in 1.5',
  'pets.desliza': 'Swipe to see them all →',
  'pets.l1': 'They sleep in their bed while Ollama is off and eat while a model loads.',
  'pets.l2': 'They type on a tiny laptop while it generates and, while RAM is freed, each one cleans up in its own way: Mira scans the taskbar with her eye and compacts the loose data, the capybara scoots her bottom along the floor (or, if she was asleep, climbs out of her tub and wrings out her fur), the kitten washes and the llama shakes her wool.',
  'pets.l3': 'Each one reacts to your clicks and cursor in its own way; asleep, without leaving its bed. When Ollama starts, the capybara arrives riding her crocodile. Off by default.',
  'pets.alt': 'The four pets in six states: sleeping, eating, typing, carrying a box, cleaning up in their own way and hearts',

  'estado.eyebrow': 'In the tray',
  'estado.titulo': 'Status at a glance.',
  'estado.texto': 'The target next to the clock tells you how Ollama is doing. In Settings › General you can always show it; if the toggle is unavailable, Pin the icon from Windows lets you open the taskbar settings.',
  'estado.on': 'On',
  'estado.onDet': 'blue dot',
  'estado.cambio': 'Starting or stopping',
  'estado.cambioDet': 'amber dot and radar',
  'estado.off': 'Off',
  'estado.offDet': 'no dot',

  'mem.eyebrow': 'Memory',
  'mem.titulo': 'RAM and VRAM, no guessing.',
  'mem.texto': 'How much is the model and how much is everything else, on each dedicated GPU. The same figure as Task Manager.',
  'mem.pc': 'PC memory',
  'mem.presion': 'normal pressure',
  'mem.modelo': 'Model 5.5',
  'mem.resto': 'Rest 4.7',
  'mem.modeloGpu': 'Model 4.3',
  'mem.restoGpu': 'Rest 0.8',

  'mod.eyebrow': 'Models',
  'mod.titulo': 'Your library, in the panel.',
  'mod.texto': 'Size, quantization and tags. Load, download or delete models without typing commands.',
  'mod.tools': 'tools',
  'mod.vision': 'vision',
  'mod.enUso': 'in use',

  'juego.eyebrow': 'Game mode',
  'juego.titulo': 'You play. Ollama steps aside.',
  'juego.texto': 'Open a game and Ollama turns off to leave it the RAM and VRAM. When you quit, it comes back the same way.',
  'juego.p1': 'You open the game',
  'juego.p2': 'Ollama turns off',
  'juego.salir': 'quit +30 s',
  'juego.p3': 'What was on comes back',
  'juego.propios': '· and any you add',

  'ram.eyebrow': 'Free RAM',
  'ram.titulo': 'Cleans when you decide.',
  'ram.texto': 'Over the percentage you choose, every few minutes or under critical pressure, with the areas you set in Settings. If RAM stays high, a minimum pause of 1 to 30 minutes keeps it from repeating in a loop. It protects models, the game and the app you’re using.',
  'ram.antes': 'Before',
  'ram.despues': 'After',
  'ram.nota': 'illustrative example · observe changes immediately and at 5 and 30 seconds',

  'proc.eyebrow': 'Process explorer',
  'proc.titulo': 'RAM, application by application.',
  'proc.texto': "The Processes tab of Activity, inside the notch: RAM and CPU per application; sort, filter and expand each PID with its details. A forced close asks for confirmation right there.",
  'proc.app': 'End application',
  'proc.grupo': 'group and descendants',
  'proc.pid': 'End process',
  'proc.uno': 'selected PID only',
  'proc.arbol': 'End process tree',
  'proc.hijos': 'PID and descendants',
  'proc.nota': 'forced closure with confirmation · UAC if Windows denies access',

  'vig.eyebrow': 'Watchdog',
  'vig.titulo': 'If it falls, it gets back up.',
  'vig.texto': 'If Ollama hangs or quits on its own, it restarts it the same way. If you turned it off, it leaves it alone.',
  'vig.caida': 'Crash',
  'vig.cuelgue': 'Hang',
  'vig.limite': 'Limit',
  'vig.limiteDet': '3 in 10 min',

  'quien.eyebrow': 'Activity',
  'quien.titulo': 'You know who woke it up.',
  'quien.texto': 'Which app loaded the model, from its connection to Ollama’s port. Plus your week: GB reclaimed, naps and hours with a model, next to memory right now: physical RAM, committed, page files and system cache.',
  'quien.o': '3 times · 12 min ago',
  'quien.oc': 'once · 3 h ago',
  'quien.c': 'once · Sep 29',

  'motores.eyebrow': 'Other engines',
  'motores.titulo': 'Not just Ollama.',
  'motores.texto': 'Each engine gets its own card and its own idle timer.',
  'motores.probado': 'tested',
  'motores.probado2': 'tested',
  'motores.exp': 'experimental',

  'vidrio.eyebrow': 'Appearance',
  'vidrio.titulo': 'Black glass on Windows 11.',
  'vidrio.texto': "Real acrylic in the tray and, in the notch, true black that fades into liquid glass. A monochrome UI where color only means status (and the cover’s color for music). The tray icon follows your taskbar mode.",

  'idioma.eyebrow': 'Language',
  'idioma.titulo': 'It speaks your language.',
  'idioma.texto': 'Follows the Windows language, or pick one in Settings.',

  'como.eyebrow': 'How it works',
  'como.titulo': 'It stops Ollama the way it started.',
  'como.sub': 'Killing the wrong process doesn’t work: the Ollama app, a service or a scheduled task just launches it again. isTargetSleeping detects which one is yours and uses that same path.',
  'como.probado': 'Tested',
  'como.probadoVM': 'Tested on a Windows VM',
  'como.validacion': 'Service and task: two cycles with genuine Ollama 0.35.1 on Windows Server 2025 x64, verifying the API and process exit. No models were loaded and interactive UAC was not tested. <a href="https://github.com/Tykillita/isTargetSleeping/blob/main/docs/backend-validation-1.5.2.md">Read the report and its limits</a>.',
  'como.apagar': 'Stop',
  'como.encender': 'Start',
  'como.app': 'Ollama app',
  'como.appOff': 'Closes its windows and the processes that hang from the app',
  'como.servicio': 'Windows service',
  'como.scmStop': 'Stop through the SCM',
  'como.scmStart': 'Start through the SCM (UAC if needed)',
  'como.tarea': 'Scheduled task',
  'como.tareaStop': 'Stop in Task Scheduler; only closes that task’s processes',
  'como.tareaRun': 'Run in Task Scheduler',
  'como.manual': 'Manual <code>ollama serve</code>',
  'como.manualOff': 'Ends only the server the app started',
  'como.manualOn': 'Launches it detached, with a log',
  'como.noInst': 'Not installed',
  'como.nada': 'Nothing to stop',
  'como.descarga': 'Opens ollama.com/download',
  'como.orden': 'It looks in this order: a running Ollama app, a service, a scheduled task, a standalone <code>ollama serve</code> and whatever is installed. While Ollama is off, it uses the one you picked in Settings or the last one it ran with.',
  'como.idleTitulo': 'How does it know the model is idle?',
  'como.idleTexto': 'Ollama doesn’t say when the last request happened. So every 2.5 s it adds up its runners’ CPU time: growth above 0.08 s means activity. It works even when the model runs entirely on the GPU.',
  'como.idleAlt': 'Every 2.5 s it adds up the runners’ CPU time; growth above 0.08 s counts as activity; after the idle time the model leaves memory while Ollama stays on',
  'como.juegoTitulo': 'And when you open a game',
  'como.juegoTexto': 'It detects games from your libraries that have a visible window. Launchers, anti-cheat and Wallpaper Engine don’t count. If you turn Ollama on by hand while playing, it respects that.',
  'como.juegoAlt': 'Timeline: you open a game, 5 s later Ollama turns off; you quit and 30 s later only what was on comes back',
  'como.juegoDiagramaKicker': 'Game mode',
  'como.juegoDiagramaTitulo': 'Your game gets the RAM and VRAM; Ollama comes back on its own',
  'como.juegoDiagramaBanda': 'RAM and VRAM free for the game',
  'como.juegoPaso1': 'Game opens',
  'como.juegoPaso1Texto': 'A visible window from Steam, Epic, Riot, EA, GOG, Xbox…',
  'como.juegoPaso2Texto': 'Ollama and the other engines turn off.',
  'como.juegoPaso3': 'You quit',
  'como.juegoPaso3Texto': 'Turn on anyway and Not a game stay in the panel.',
  'como.juegoPaso4Texto': 'Only what was on comes back, the same way.',

  'priv.eyebrow': 'Privacy',
  'priv.titulo': 'It all happens on your PC.<br>That’s it.',
  'priv.sub': 'It talks to Ollama’s local API at <code>127.0.0.1:11434</code> (or <code>OLLAMA_HOST</code>, if you set it) and to the other engines’ local ports. The widgets read data from your own PC and send nothing (the notch’s glass uses a copy of the screen that only lives in memory; calendar reminders are saved in your settings); the tray (which only remembers its paths if you ask) and the optional clipboard live in memory. Web pages only open when you click.',
  'priv.c1': 'No accounts',
  'priv.c1t': 'No sign-up, no email, no password. Download it and use it.',
  'priv.c2': 'No telemetry',
  'priv.c2t': 'No analytics either. Your 90-day history and your notes stay in <code>%LOCALAPPDATA%</code>.',
  'priv.c3': 'One optional way out',
  'priv.c3t': 'Checking GitHub for updates, without credentials. Turn it off in Settings; it never uploads models, prompts or settings.',
  'priv.admin': 'UAC is requested when enabling or updating the RAM agent, when controlling Ollama as a service if needed, or when you choose to retry process termination as administrator.',

  'inst.eyebrow': 'Install',
  'inst.titulo': 'One minute. No admin.',
  'inst.sub': 'A single self-contained .exe: you don’t need to install .NET.',
  'inst.p1': 'Download it',
  'inst.p2': 'Open it',
  'inst.p2t': 'It installs for your user in <code>%LOCALAPPDATA%\\Programs</code>. No UAC.',
  'inst.p3': 'Look by the clock',
  'inst.p3t': 'Find the target in the hidden icons (^). In Settings › General, turn on Always show in the taskbar or click Open taskbar settings to pin it from Windows.',
  'inst.zip': 'Portable x64 (.zip)',
  'inst.arm': 'ARM64 installer',
  'inst.armZip': 'Portable ARM64 (.zip)',
  'inst.x64': 'x64 installer',
  'dl.titulo': 'Your download has started',
  'dl.otra': 'Didn’t start? Try again',
  'dl.pasos': 'See the steps',
  'dl.cerrar': 'Close notice',
  'inst.meta': '~75 MB · Windows 10 (2004+) or 11 · requires Ollama · latest published version: 1.20.0 · GitHub Releases',
  'inst.verificar': 'Verify the download (optional)',
  'inst.copiar': 'Copy',
  'inst.smart': 'Every file in the release has its <code>.sha256</code> next to it. If the build isn’t signed, Windows SmartScreen may ask you to confirm: <b>More info › Run anyway</b>.',

  'auto.eyebrow': 'For automators',
  'auto.titulo': 'Links and flags for your scripts.',
  'auto.texto': '<code>istargetsleeping://</code> links work from Stream Deck, PowerToys or a shortcut. The same .exe answers to flags, through the same path as the panel.',
  'auto.enlaces': 'Links',
  'auto.c1': '# detected mechanism and whether the API responds',
  'auto.c2': '# the same path as the panel button',
  'auto.c3': '# puts the models to sleep without turning anything off',
  'auto.c4': '# RAM, runner CPU time and GPU memory',

  'oss.eyebrow': 'Open source',
  'oss.titulo': 'Nothing hidden in your tray.',
  'oss.texto': 'isTargetSleeping is free software under the MIT license. Read what it does on your PC, build it yourself and suggest improvements.',
  'oss.github': 'View on GitHub',
  'oss.cambios': 'What’s new',
  'oss.k1': 'C# on .NET 10: WPF for the interface and Win32 directly for everything else.',
  'oss.k2': 'No NuGet packages, no WinForms, no web view.',
  'oss.k3': 'Every change passes the tests and builds for x64 and ARM64 on GitHub Actions.',
  'oss.legal': 'By CodeSentry - Tykillita. Not affiliated with Ollama.',
  'oss.c1': '# logic, integration and interface tests',
  'oss.c2': '# build\\isTargetSleeping.exe, a single file',
  'oss.c3': '# zip + installer for x64 and ARM64, with .sha256',

  'faq.titulo': 'Frequently asked questions.',
  'faq.q1': 'Does it cost anything?',
  'faq.a1': 'No. It’s free, there’s no paid version and you don’t need to create an account.',
  'faq.q2': 'How do I turn Ollama off on Windows?',
  'faq.a2': 'With the big button in the panel or Ctrl+Alt+O. isTargetSleeping detects whether Ollama came from its app, a service, a scheduled task or a manual <code>ollama serve</code>, and stops it the same way so it doesn’t start again by itself.',
  'faq.q3': 'How do I unload a model without turning Ollama off?',
  'faq.a3': 'Pick an idle time (5, 15, 30 or 60 min) and it will go to sleep by itself. To do it now: Ctrl+Alt+S, the panel’s Unload button or <code>istargetsleeping://sleep</code>. Ollama stays on and reloads it with the next message.',
  'faq.q4': 'Is it an official Ollama app?',
  'faq.a4': 'No. It’s an independent project by CodeSentry - Tykillita, not affiliated with Ollama. It only uses its local API.',
  'faq.q5': 'Does it need admin rights?',
  'faq.a5': 'It installs and runs as your user. UAC is requested when enabling or updating the RAM agent, if Ollama runs as a service, or when you choose to retry process termination as administrator. Process queries work without the agent.',
  'faq.q6': 'What happens to my apps while the model sleeps?',
  'faq.a6': 'Nothing odd. Ollama keeps listening, so Obsidian, Cursor or your script send their message as usual. The first reply takes a little longer while the model loads again.',
  'faq.q7': 'Does it make models faster?',
  'faq.a7': 'Not directly. It gives the RAM and VRAM back to the rest of your PC when the model isn’t working, and moves Ollama out of the way while you play.',
  'faq.q8': 'Do the pets use resources?',
  'faq.a8': 'Very few, and they’re off by default. They hide when Start, a game or a full-screen app opens; with reduced motion they use one still pose with no timers. Turning them off closes their window.',
  'faq.q9': 'How does it update?',
  'faq.a9': 'It checks GitHub for stable releases at startup and every 24 h, shows you the notes and only installs when you click Update. It verifies the SHA-256 and rolls back to the previous version if anything fails.',
  'faq.q10': 'Does it work on Mac or Linux?',
  'faq.a10': 'No: it’s built on Win32 and WPF and only runs on Windows 10 (2004 or later) and 11, on x64 and ARM64.',
  'faq.q11': 'Where do I report a bug?',
  'faq.a11': 'In the <a href="https://github.com/Tykillita/isTargetSleeping/issues">GitHub issues</a>. Attach the log from <code>%LOCALAPPDATA%\\isTargetSleeping\\Logs\\app.log</code> if you can. For a vulnerability, use the <a href="https://github.com/Tykillita/isTargetSleeping/security/advisories/new">private report</a> in the security policy.',

  'cierre.titulo': 'Let your model rest.',
  'cierre.texto': 'Free for Windows. No accounts. Awake when you talk to it, asleep when you don’t.',
  'cierre.meta': "Version 1.20.0 · Windows 10 and 11 · x64 and ARM64",

  'pie.mit': 'MIT License',
  'pie.novedades': 'What’s new',
  'pie.cambios': 'Changelog',
  'pie.lema': 'Watches your local models and puts them to sleep when they stop working. Your RAM, back.',
  'pie.descargar': 'Download for Windows',
  'pie.aria': 'Footer',
  'pie.producto': 'Product',
  'pie.oss': 'Open source',
  'pie.repo': 'GitHub repository',
  'pie.notas': 'Release notes',
  'pie.fallo': 'Report a bug',
  'pie.contribuir': 'Contributing',
  'pie.seguridad': 'Security policy',
  'pie.quien': 'Who makes it',
  'pie.autor': 'Designed and built by <b>CodeSentry - Tykillita</b>, software development and cybersecurity.',
  'pie.aviso': 'isTargetSleeping is an independent project, not affiliated with Ollama. The idea of putting idle models to sleep comes from <a href="https://github.com/eriktaveras/modelnap">ModelNap</a> by Erik Taveras (MIT); the Windows app’s design and code are its own.',
  'pie.version': "Version 1.20.0",
  'pie.idiomaAria': 'Language',
  'pie.licencia': 'License',

  // ---------- Novedades (site/novedades.html) ----------

  'nov.eyebrow': "What’s new",
  'nov.titulo': "Every version, feature by feature.",
  'nov.sub': "What changed in isTargetSleeping, newest first. The technical detail lives in the CHANGELOG.",
  'nov.changelog': "Read the full CHANGELOG",
  'nov.volver': "Back to home",
  'nov.indice': "Versions",
  'nov.g.nuevo': "New",
  'nov.g.cambios': "Changed",
  'nov.g.arreglos': "Fixed",
  'nov.s.grande': "Major",
  'nov.s.feature': "Feature",
  'nov.s.arreglo': "Fix",
  'widg.l5': "On both sides of the closed pill, live activities show up: Ollama’s on the left (freeing RAM, turning on, critical memory); the volume, the Pomodoro and, if you turn it on, the music that is playing on the right.",
  'nov.v1_9_0.fecha': 'Oct 15, 2026',
  'nov.v1_9_0.resumen': 'Live activities beside the closed notch pill, a Music widget with cover art, controls and a clickable progress bar, and the volume in the notch.',
  'nov.v1_9_0.actividadesT': 'Live activities',
  'nov.v1_9_0.actividadesD': 'Next to the closed pill’s data you see what is happening now: freeing RAM, Ollama turning on or off, critical memory, a running Pomodoro, Keep awake and the music that is playing. The pill grows to make room, two fit at a time (the most urgent first) and each has its own switch on the Widgets page.',
  'nov.v1_9_0.volumenT': 'Volume in the notch',
  'nov.v1_9_0.volumenD': 'When the Windows volume changes (or you mute it), the closed pill shows it for a couple of seconds, for example “98 %” or “Muted”. It shows up in addition to the Windows indicator, which can’t be removed in a supported way. It only reads the volume, never changes it, and has its own switch.',
  'nov.v1_9_0.musicaT': 'Music widget',
  'nov.v1_9_0.musicaD': 'Title, artist, cover art, progress and the previous, play or pause and next controls for whatever Windows says is playing (Spotify, the browser, the media player…). The progress bar can be clicked or dragged to jump to another point in the song. Swipe the widget left or right (with the mouse, or two fingers on the trackpad) to skip tracks. It is read on your PC and only when something needs it; the “Music playing” activity comes turned off.',
  'widg.l6': 'Drag files onto the notch and they wait in the Tray until you drop them wherever you like; text files can be edited right there. The clipboard history is optional, text only and in memory only, and favorites open your apps too. The Code widget is a small IDE: file tree with Material Icon Theme icons, tabs, an image viewer and an editor. The closed pill can also show the music that is playing, with its title scrolling.',
  'nov.v1_20_0.fecha': 'Oct 17, 2026',
  'nov.v1_20_0.resumen': "The notch becomes liquid glass, with a glass dock and a lens you drag, widgets with no cards, a calendar with an agenda and processes inside the notch.",
  'nov.v1_20_0.cristalT': "Liquid-glass notch",
  'nov.v1_20_0.cristalD': "The open panel is true black at the top, next to the camera, and fades into real frosted glass: what is behind it is blurred and tinted so the text stays readable. That copy of the screen is taken when it opens, lives in memory and is never saved or sent.",
  'nov.v1_20_0.lenteT': "A glass lens in the dock",
  'nov.v1_20_0.lenteD': "Press a dock button and it swells into a glass capsule that magnifies the icons and follows you with a springy motion; let go and it opens the page it lands on. The color fringes come from the icons themselves.",
  'nov.v1_20_0.calendarioT': "Calendar with an agenda",
  'nov.v1_20_0.calendarioD': "The month with arrows and the day’s reminders beside it, grouped by day. Add reminders with a ruler you drag to pick the day, and bring your events from an optional .ics file (Google Calendar, Outlook or Apple). All local, no accounts, no network.",
  'nov.v1_20_0.actividadesT': "Activities on both sides",
  'nov.v1_20_0.actividadesD': "Live activities come out in black capsules on both sides of the closed pill: Ollama’s on the left, the Pomodoro, music and volume on the right. They are measured before they show, so they never flash over the notch.",
  'nov.v1_20_0.fijarT': "Pin button",
  'nov.v1_20_0.fijarD': "It keeps the notch open while you work in another window.",
  'nov.v1_20_0.aleatorioT': "Shuffle and repeat",
  'nov.v1_20_0.aleatorioD': "The Music widget shows shuffle and repeat (with “1” for repeat-one) when the player supports them; Spotify does, browsers rarely.",
  'nov.v1_20_0.dockT': "A glass dock",
  'nov.v1_20_0.dockD': "Two glass capsules with loose icons, a glass circle behind the chosen page and a small bounce when you press. By default it only has Home, Widgets and Settings plus your favorites; Ollama and Activity are optional. It only shows when you click the notch.",
  'nov.v1_20_0.musicaT': "Droppy-style music",
  'nov.v1_20_0.musicaD': "Bold double arrows, a bar-style pause without discs, drawn shuffle and repeat, remaining time and the cover’s color tinting the bar, the equalizer and the buttons. The one-cell card is vertical, with a big cover.",
  'nov.v1_20_0.columnasT': "No cards, in columns",
  'nov.v1_20_0.columnasD': "Widgets sit straight on the glass, with no card or border, and the Home lays them out in two columns that fill by the shorter one, with no gaps. The “By CodeSentry - Tykillita” footer sits at the end of each page.",
  'nov.v1_20_0.pestanasT': "Settings and Activity in tabs",
  'nov.v1_20_0.pestanasD': "Instead of one long scroll of mixed cards: Ollama, Memory, Notch, Pet and General in Settings; Memory, Processes and History in Activity.",
  'nov.v1_20_0.procesosT': "Processes inside the notch",
  'nov.v1_20_0.procesosD': "The process explorer is no longer a separate window: it is the Processes tab of Activity, with the close confirmation right there. Every “View all processes” button opens that tab.",
  'nov.v1_20_0.nombreT': "Music name from the browser",
  'nov.v1_20_0.nombreD': "Music from a web app installed from the browser (YouTube Music in Chrome or Edge) showed a long internal identifier where the app name goes. Now the name shows (Spotify, YouTube Music, Chrome, Edge…) and an unknown identifier shows nothing.",
  'nov.v1_11_0.fecha': 'Oct 16, 2026',
  'nov.v1_11_0.resumen': 'The music that is playing in the closed notch pill and a much better file tray.',
  'nov.v1_11_0.pastillaT': 'Music in the closed pill',
  'nov.v1_11_0.pastillaD': 'The music that is playing can now be picked as an item of the closed pill (Widgets > Closed pill): three animated bars and “title · artist”. If the text doesn’t fit, it scrolls in a loop inside a fixed width so the pill doesn’t grow, and it hides while nothing plays. With that item picked, the “Music playing” activity is not repeated. It respects reduced motion: clipped text and still bars.',
  'nov.v1_11_0.bandejaT': 'A renewed tray',
  'nov.v1_11_0.bandejaD': 'Bigger rows with real thumbnails for images and Material icons for folders; the actions light up on hover and double-click opens the item. Folders say how many items they hold and how big they are, and “Open as a project” sends them to the Code widget. New “Paste” (the files you copied in Explorer) and “Drag all”, a “Drop to add to the tray” cue while you drag, a drop zone when it is empty, a full-tray notice and correct plurals. If you want, the tray is remembered between sessions (Settings > General, off by default; only the paths are stored).',
  'nov.v1_10_0.fecha': 'Oct 15, 2026',
  'nov.v1_10_0.resumen': 'A file tray in the notch, a Code widget with a file tree and editor, an optional clipboard history and favorites that open your apps.',
  'nov.v1_10_0.bandejaT': 'File tray',
  'nov.v1_10_0.bandejaD': 'Drag files or folders onto the notch and its Tray page opens. It keeps only their paths (never copies), in memory. From each row you can drag the file back out to Explorer or an email, show it in Explorer, copy its path or remove it, and “Zip everything” creates a .zip on the Desktop without overwriting any. The dock button only shows up while something is waiting.',
  'nov.v1_10_0.versionT': 'App version under the name',
  'nov.v1_10_0.versionD': 'Under the isTargetSleeping name, the panel and the notch Home now show the app’s version (for example “v1.10.0”), not Ollama’s.',
  'nov.v1_10_0.parpadeoT': 'The open notch no longer blinks',
  'nov.v1_10_0.parpadeoD': 'When the closed pill changed width (the clock ticking over, or a live activity such as the Pomodoro, the volume or the music showing up or going away), the notch redid its opening animation even though it was already open, and its content faded out and back in. Now it only repaints when something really changed.',
  'nov.v1_10_0.codigoT': 'Code widget',
  'nov.v1_10_0.codigoD': 'A small IDE inside the notch: pick a project folder and see its file tree, with the Material Icon Theme icons (the VS Code extension) for every language and folder. It opens up to 8 files in tabs, edits and saves text with Ctrl+S, and shows images. It keeps each file’s encoding and line endings, saves atomically, won’t overwrite a file that changed on disk without asking you, and won’t open binaries or files over 1 MB. Text is colored by language (letters only): Markdown, code, JSON, YAML, XML, CSS, SQL and scripts, and long lines wrap to the width, with no horizontal scrolling. It only remembers the folder’s path; unsaved text lives in memory. It comes turned off: turn it on in the Widgets page.',
  'nov.v1_10_0.editorT': 'Quick text editing',
  'nov.v1_10_0.editorD': 'A text file’s Edit button opens it in the notch to read and change; Ctrl+S or Save writes it and Reload reads it again from disk. It keeps its encoding and line endings so diffs stay clean, saves atomically, won’t open binaries, non-UTF text or files over 1 MB, and never overwrites a file that changed on disk without asking you. Unsaved text survives collapsing the notch, and closing with changes warns you first.',
  'nov.v1_10_0.portapapelesT': 'Clipboard history',
  'nov.v1_10_0.portapapelesD': 'Optional and off by default (Settings > General). It keeps the last 25 texts you copy, in memory only: nothing is written to disk, no images or files are kept, whatever password managers mark as private is skipped, it can be paused and it empties when you turn it off or quit.',
  'nov.v1_10_0.appsT': 'Favorites that open apps',
  'nov.v1_10_0.appsD': 'With “Add an app…” on the Widgets page, the dock favorites (up to four) can also open any app, shortcut or file you pick. They show up with their name and are removed from the same list.',
  'nov.v1_8_0.fecha': 'Oct 15, 2026',
  'nov.v1_8_0.resumen': 'An optional notch dock with a Home of widgets that you choose and order, and favorites you configure. It includes the 1.7.0 notch, which was never published separately.',
  'nov.v1_8_0.notchT': 'Notch dock',
  'nov.v1_8_0.notchD': 'Turn it on in Settings > General. A pill docked to the top edge, on the monitor where your cursor is: hover it for a short view of Ollama with status, RAM, GPU and the Free RAM, Free model and Processes buttons; click it to open the Home. Esc, a click elsewhere or moving the pointer away closes it.',
  'nov.v1_8_0.dockT': 'Two icon strips below',
  'nov.v1_8_0.dockD': 'One to navigate (Home, Ollama, Activity, Widgets and Settings) and one with your favorites. Favorites work without opening the panel.',
  'nov.v1_8_0.vidrioT': 'Glass with curved shoulders',
  'nov.v1_8_0.vidrioD': 'Black at the screen edge and more translucent further down, with the top corners curving outward. The content appears once the shape has grown, and reduced motion is respected.',
  'nov.v1_8_0.pantallaT': 'Shows up where you need it',
  'nov.v1_8_0.pantallaD': 'It follows the cursor when it touches another monitor’s top edge and hides while a game, a video or the Start menu is full screen. Every button has an accessible name.',
  'notch.eyebrow': 'Notch',
  'notch.titulo': 'Your panel, at the edge of the screen.',
  'notch.texto': "An optional dock, glued to the top: true black that fades into liquid glass, with glass capsules and a lens you drag. It opens when you hover it. Turn it on in Settings > General.",
  'notch.l1': 'Hover: a short view with status, RAM and GPU, plus the Free RAM, Free model and Processes buttons.',
  'notch.l2': 'Click: the Home with your widgets, in two columns. Esc or a click elsewhere closes it.',
  'notch.l3': "Below it, two glass capsules: the pages (Home, Widgets and Settings; Ollama and Activity if you turn them on) and up to four one-tap favorites that you choose. Press and drag across the buttons: a glass lens follows you and opens the page it lands on. A pin button keeps the notch open. It appears on the monitor where your cursor is and hides while a game or video is full screen.",
  'notch.alt': 'The open notch: Ollama status, PC and GPU memory and action buttons, with two icon strips below it',
  'widg.eyebrow': 'Widgets',
  'widg.titulo': 'Your Home, with what you choose.',
  'widg.texto': 'Four cells that you order your way on the Widgets page. Each widget is on only if you want it.',
  'widg.l1': 'Ollama: turn it on or off, memory and GPU, always in view.',
  'widg.l2': "System (CPU, RAM, VRAM, disk and network), Clock and calendar with an agenda of reminders and an optional .ics file, Battery and Music with cover art, controls, shuffle and repeat.",
  'widg.l3': 'Pomodoro with a notice, Keep awake and Notes that save themselves, on your PC.',
  'widg.l4': 'Widgets sit straight on the glass, with no cards, in two columns that fill with no gaps. Each widget changes its view depending on where it is and who keeps it company. The closed pill, the hover view and the favorites are yours to choose too.',
  'widg.alt': 'The notch Home with the Ollama, System and Clock and calendar widgets, and the two icon strips below',
  'nov.v1_8_0.widgetsT': 'Home with widgets',
  'nov.v1_8_0.widgetsD': 'The notch opens on a four-cell Home with the widgets you pick: Ollama, System (CPU, RAM, VRAM, disk and network), Clock and calendar, Pomodoro, Keep awake, Notes and Battery. Everything is read on your PC and nothing is sent anywhere.',
  'nov.v1_8_0.peekT': 'Hover view, your way',
  'nov.v1_8_0.peekD': 'What you see when you move the pointer over the notch is now its own list of widgets, chosen and ordered on the Widgets page (Ollama by default, as always). The Free RAM and Free model buttons no longer vanish while they work: they dim and read “Freeing…”, without moving the others.',
  'nov.v1_8_0.pildoraT': 'Pill your way',
  'nov.v1_8_0.pildoraD': 'Choose up to three items for the closed pill: status, model, RAM, VRAM, CPU, time, battery or the Pomodoro time, and put them in order. Status, model and RAM by default, as always.',
  'nov.v1_8_0.adaptaT': 'Widgets that adapt',
  'nov.v1_8_0.adaptaD': 'Each widget changes its view depending on where it is and who keeps it company: if it ends up alone in its row it widens and uses its wide view (System in two columns, Clock with the month beside the time, Pomodoro and Battery in one row), and in the hover view widgets are denser, without the secondary details.',
  'nov.v1_8_0.paginaT': 'Widgets page',
  'nov.v1_8_0.paginaD': 'Turn each widget on or off and move it up or down; a counter shows how many cells you use, and if one doesn’t fit it asks you to remove another first.',
  'nov.v1_8_0.favT': 'Favorites your way',
  'nov.v1_8_0.favD': 'Up to four one-tap actions next to the navigation: turn Ollama on or off, Free RAM, Free the model, Processes, Keep awake and Pomodoro.',
  'nov.v1_8_0.pomoT': 'Pomodoro with a notice',
  'nov.v1_8_0.pomoD': '25 minutes of focus and 5 of break. When each phase ends it sends a notification, which has its own switch in Settings.',
  'nov.v1_8_0.agenteT': 'Free RAM without reinstalling the agent',
  'nov.v1_8_0.agenteD': 'The memory agent is no longer requested again with every release: it has its own version and only needs updating when its code changes. Free RAM keeps working after updating the app, with no new administrator prompt. And if it is missing or outdated, the notch buttons open Settings instead of doing nothing.',
  'nov.v1_8_0.navT': 'Different navigation in the notch',
  'nov.v1_8_0.navD': 'The notch opens on Home and its navigation is Home, Ollama, Activity, Widgets and Settings. The tray panel is unchanged.',
  'nov.v1_6_1.fecha': 'Oct 14, 2026',
  'nov.v1_6_1.resumen': 'Turning Ollama off also closes the server the app started, and freeing RAM no longer causes restarts or pet glitches.',
  'nov.v1_6_1.limpiezaT': 'Freeing RAM without a scare',
  'nov.v1_6_1.limpiezaD': 'While Windows trims memory, Ollama can answer slowly. The app took that for a hang: it restarted Ollama and the capybara went into its tub, came out to wash and went back in. Now the panel stays on, the models stay in view and the watchdog waits during the cleanup and for 20 s afterwards.',
  'nov.v1_6_1.apagadoT': 'Turning off closes the server the app started',
  'nov.v1_6_1.apagadoD': 'If you turned Ollama on from isTargetSleeping and then opened the Ollama app, turning it off left a server running and the panel kept showing “On” with an incomplete-shutdown notice. Now the server the app started is closed too, whichever mechanism is selected; servers launched by other programs are left untouched.',
  'nov.v1_6_0.fecha': 'Oct 14, 2026',
  'nov.v1_6_0.resumen': 'Turning Ollama off respects other servers, Activity shows the VRAM of each GPU and the panel works with the keyboard and a screen reader. It also includes the updater fix.',
  'nov.v1_6_0.vramT': 'VRAM of each GPU',
  'nov.v1_6_0.vramD': 'Activity lists every dedicated GPU with its total, what is in use and the share used by models. If Windows or the driver does not provide a value, it shows as unavailable instead of an estimate. It only informs; it never turns anything off on its own.',
  'nov.v1_6_0.apagadoT': 'Turning off without taking others with it',
  'nov.v1_6_0.apagadoD': 'When Ollama is turned off, only the servers the app can prove belong to the chosen mechanism are closed: its task’s instance, what hangs from the Ollama app, or the binary the app started. Another task or a server started by hand keeps running and the app reports that the shutdown was incomplete.',
  'nov.v1_6_0.tecladoT': 'Panel usable with keyboard and screen reader',
  'nov.v1_6_0.tecladoD': 'Every button and switch can be reached with Tab and shows where focus is. Icon-only buttons have their own name for Narrator, in English too.',
  'nov.v1_6_0.contrasteT': 'High contrast',
  'nov.v1_6_0.contrasteD': 'If you turn on a Windows high-contrast theme, the panel uses your system colors instead of the black glass.',
  'nov.v1_6_0.seguridadT': 'Safer update extraction',
  'nov.v1_6_0.seguridadD': 'The updater accepts only the expected ZIP files, writes them under fixed names and checks that no path leaves its staging folder.',
  'nov.v1_5_2.fecha': 'Oct 3, 2026',
  'nov.v1_5_2.resumen': 'Real validation of Ollama services and scheduled tasks.',
  'nov.v1_5_2.validacionT': 'Reproducible Windows verification',
  'nov.v1_5_2.validacionD': 'Service and task passed two start/stop cycles with genuine Ollama 0.35.1 on a Windows Server 2025 x64 VM. The public report describes process exit, API verification and the limits of these checks.',
  'nov.v1_5_1.fecha': "Oct 3, 2026",
  'nov.v1_5_1.resumen': "The icon, always at hand: a button pins it from the Windows settings and the toggle tracks every one of its entries.",
  'nov.v1_5_1.nuevo1t': "Pin the icon from Windows",
  'nov.v1_5_1.nuevo1d': "If the direct toggle doesn’t appear (Windows 10, or Windows 11 before registering the icon), Settings › General has a button that opens the taskbar settings to turn it on there.",
  'nov.v1_5_1.arreglos1t': "The toggle detects new entries",
  'nov.v1_5_1.arreglos1d': "If Windows created another entry for the icon, the toggle could stay on with the icon behind the ^. It now takes every entry into account and re-pins them together when toggled.",
  'nov.v1_5_0.fecha': "Oct 2, 2026",
  'nov.v1_5_0.resumen': "Memory now in Activity, cleanup rules that follow your percentage and renewed pets: each one cleans up in its own way and reacts in its bed.",
  'nov.v1_5_0.nuevo1t': "Each one cleans up its own way",
  'nov.v1_5_0.nuevo1d': "Freeing RAM no longer has all four sweeping: Mira scans the taskbar with a beam from her eye and compacts the loose data into a cube that bursts into sparkles, the llama shakes the dust out of her wool, the kitten washes itself and the capybara scoots her bottom along the floor until she’s gleaming.",
  'nov.v1_5_0.nuevo2t': "Out of bed to clean",
  'nov.v1_5_0.nuevo2d': "If it was asleep with Ollama off, it first gets out of its bed and goes back when it’s done. The capybara climbs out of her tub and wrings the water out of her fur.",
  'nov.v1_5_0.nuevo3t': "Reactions in bed",
  'nov.v1_5_0.nuevo3d': "Asleep, a click, a pat or a watchdog alert no longer gets it up: it half opens an eye, twitches an ear or lets out a bubble, and the Windows logo stays dark.",
  'nov.v1_5_0.cambios5t': "Mira and the llama, renewed",
  'nov.v1_5_0.cambios5d': "Mira moves with more craft: she crouches before jumping, bounces on landing, boots up calibrating her eye and patrols with her radar. The llama no longer has arms: she eats with her muzzle, types by pecking to the beat of her hooves and speaks with her neck, ears and a tail that finally moves.",
  'nov.v1_5_0.cambios3t': "Full automatic cleanup",
  'nov.v1_5_0.cambios3d': "Cleanups by percentage, by time, under critical pressure and when gaming clean the same as the button, with the areas you choose. New minimum pause: if RAM stays above your percentage, it repeats at most every 1 to 30 minutes.",
  'nov.v1_5_0.cambios4t': "No Mem Reduct",
  'nov.v1_5_0.cambios4d': "The Mem Reduct card is gone from Settings: isTargetSleeping cleans with its own agent and doesn’t need it.",
  'nov.v1_5_0.arreglos3t': "Your percentage rules",
  'nov.v1_5_0.arreglos3d': "Since 1.4.0, automatic rules only acted above 85 % load even if you chose 70 %, and didn’t repeat while RAM stayed high. Now they act when your percentage is reached, even before the interval.",
  'nov.v1_5_0.nuevo4t': "The capybara, on a crocodile",
  'nov.v1_5_0.nuevo4d': "While Ollama starts, her crocodile arrives: she waits for it standing, jumps onto its back and sits down for the ride; once it's on, she jumps off. The ride always finishes even if Ollama starts sooner. And with nothing to do, she now sits.",
  'nov.v1_5_0.nuevo5t': "Memory now",
  'nov.v1_5_0.nuevo5d': "Activity shows physical RAM, committed memory, page files and the system cache, like Mem Reduct, each with its own bar.",
  'nov.v1_5_0.arreglos4t': "Closing the process window",
  'nov.v1_5_0.arreglos4d': "It has a button to close it (Esc works too) that takes you back to the isTargetSleeping panel.",
  'nov.v1_5_0.arreglos6t': "They wake up just once",
  'nov.v1_5_0.arreglos6d': "While Ollama started, the kitten, the llama and Mira closed their eyes and stretched again and again. Now they stretch when leaving their bed and then stay awake.",
  'nov.v1_5_0.arreglos5t': "The pet no longer vanishes",
  'nov.v1_5_0.arreglos5d': "Taskbar previews, Alt+Tab or a Snipping Tool capture hid it as if they were a full-screen game, and sometimes it didn't come back even on the desktop. Now it only hides for real full-screen apps and moves back in front of the taskbar.",
  'nov.v1_5_0.arreglos1t': "Free RAM frees RAM again",
  'nov.v1_5_0.arreglos1d': "Since 1.4.0, pressing it from the tray skipped almost everything you had opened from Start. Now only models, the game and the app you’re using are protected.",
  'nov.v1_5_0.arreglos2t': "No needless “partial”",
  'nov.v1_5_0.arreglos2d': "Protected Windows processes, like the antivirus, are skipped quietly instead of marking every cleanup as partial.",
  'nov.v1_5_0.cambios1t': "Sparkles at the end",
  'nov.v1_5_0.cambios1d': "The “RAM freed” sparkles arrive once you’ve seen it cleaning, including long cleanups; only then does it go back to what it was doing.",
  'nov.v1_4_0.fecha': "Oct 1, 2026",
  'nov.v1_4_0.resumen': 'Process explorer, confirmed shutdowns and selective automatic cleanup.',
  'nov.v1_4_0.nuevo3t': "Process explorer",
  'nov.v1_4_0.nuevo3d': "The five applications using the most RAM in Activity, plus a window with every process, filters, sorting and PID details.",
  'nov.v1_4_0.nuevo4t': "You choose what to close",
  'nov.v1_4_0.nuevo4d': "End an application, a PID or its tree, always with confirmation. If Windows denies access, you can retry with UAC. Critical and application processes are blocked.",
  'nov.v1_4_0.cambios1t': "Selective automatic cleanup",
  'nov.v1_4_0.cambios1d': "With high physical pressure, trims low-activity processes while protecting models, engines, games and foreground applications. Advanced areas remain available in manual cleanup.",
  'nov.v1_4_0.cambios2t': "Observed RAM changes",
  'nov.v1_4_0.cambios2d': "Observe memory immediately and at five and thirty seconds. Values may increase or decrease; process sampling pauses when minimized or closed.",
  'nov.v1_4_0.cambios3t': "The explorer, in the app’s black glass",
  'nov.v1_4_0.cambios3d': "The process window and its confirmation use the panel header, icon buttons, the segmented picker from Settings and a glass-card table. You only see the actions that apply to the selection.",
  'nov.v1_4_0.arreglos1t': "Clear results",
  'nov.v1_4_0.arreglos1d': "Separate success, partial completion, failure and no work. A pending cleanup blocks another request and automatic failures have bounded retries.",
  'nov.v1_4_0.arreglos2t': "Coordinated rules",
  'nov.v1_4_0.arreglos2d': "Critical pressure takes priority; game cleanup waits for engine shutdown. Servers ended manually are not restored by the watchdog or game mode.",
  'nov.v1_4_0.arreglos3t': "No false “access denied”",
  'nov.v1_4_0.arreglos3d': "If Windows was already closing a process, isTargetSleeping waits a moment and counts it as ended instead of asking for administrator rights.",
  'nov.v1_3_0.fecha': "Sep 30, 2026",
  'nov.v1_3_0.resumen': "Updates from GitHub and four taskbar pets: Mira, the llama, the capybara and the kitten.",
  'nov.v1_3_0.nuevo1t': "Updates",
  'nov.v1_3_0.nuevo1d': "Checks GitHub for stable releases at startup and every 24 h. You can turn it off, or check by hand from Settings or the tray.",
  'nov.v1_3_0.nuevo2t': "You decide when to install",
  'nov.v1_3_0.nuevo2d': "You see the new version and its notes, and nothing installs until you click Update. It verifies the SHA-256 and rolls back by itself if anything fails.",
  'nov.v1_3_0.nuevo3t': "Mira by the Start button",
  'nov.v1_3_0.nuevo3d': "The app’s pet lives in the taskbar and acts out what Ollama is doing: sleeps, eats while loading, types while generating and sweeps while freeing RAM.",
  'nov.v1_3_0.nuevo4t': "Three places to live",
  'nov.v1_3_0.nuevo4d': "Above Start, inside the taskbar by the logo, or walking to the tray while a model is awake.",
  'nov.v1_3_0.nuevo5t': "The Windows logo lights up",
  'nov.v1_3_0.nuevo5d': "It glows softly with Ollama on, flashes with each keystroke while generating and fills like a bar while downloading. Clicks still go to Start.",
  'nov.v1_3_0.nuevo6t': "Three more pets",
  'nov.v1_3_0.nuevo6d': "A curious llama, a zen capybara and a playful orange kitten join Mira. Pick one in the two-column picker in Settings or in the pet’s menu.",
  'nov.v1_3_0.nuevo7t': "A personality of their own",
  'nov.v1_3_0.nuevo7d': "Each pet has its own choreography, click reaction and way of treating the cursor: Mira sends a radar ping, the llama snorts, the capybara barely flinches and the kitten pounces on the pointer.",
  'nov.v1_3_0.nuevo8t': "A bed for each one",
  'nov.v1_3_0.nuevo8d': "A blue capsule for Mira, an Andean cushion for the llama, a steaming wooden tub for the capybara and a wicker basket for the kitten.",
  'nov.v1_3_0.nuevo9t': "Different hiding styles",
  'nov.v1_3_0.nuevo9d': "Behind the Windows logo, the llama peeks like a periscope, the kitten shows its ears first and the capybara doesn’t hide at all: it leans on it.",
  'nov.v1_3_0.nuevo10t': "New particles",
  'nov.v1_3_0.nuevo10d': "Steam, bubbles, leaves, music notes, purrs, bits and antenna signals.",
  'nov.v1_3_0.nuevo11t': "Reviewed releases",
  'nov.v1_3_0.nuevo11d': "Every version runs the tests, builds x64 and ARM64 packages with their SHA-256 and stays as a draft to review before publishing.",
  'nov.v1_3_0.cambios1t': "Sharper sprites",
  'nov.v1_3_0.cambios1d': "Pupils that dilate, ears that swivel, expressive tails and a separate animation profile per species instead of a shared one.",
  'nov.v1_3_0.arreglos1t': "About",
  'nov.v1_3_0.arreglos1d': "Describes the app for what it is and credits ModelNap separately, as inspiration.",
  'nov.v1_2_0.fecha': "Sep 30, 2026",
  'nov.v1_2_0.resumen': "Built-in RAM freeing, a new tray icon and the main model.",
  'nov.v1_2_0.nuevo1t': "Free RAM",
  'nov.v1_2_0.nuevo1d': "What Mem Reduct does, built in, but process by process and without touching the models or your game. Button, Ctrl+Alt+L, link or command line.",
  'nov.v1_2_0.nuevo2t': "Memory agent",
  'nov.v1_2_0.nuevo2d': "A small separate program installed once with UAC; after that every cleanup runs without prompting.",
  'nov.v1_2_0.nuevo3t': "Automatic rules",
  'nov.v1_2_0.nuevo3d': "Cleans over X % RAM, every N minutes, on critical pressure or when a game starts.",
  'nov.v1_2_0.nuevo4t': "Replaces Mem Reduct",
  'nov.v1_2_0.nuevo4d': "Detects it, imports its settings and takes over. One click brings it back.",
  'nov.v1_2_0.nuevo5t': "New tray icon",
  'nov.v1_2_0.nuevo5d': "Redrawn for 16–32 px, with a radar animation while switching and an eye that opens with a model loaded. Optional RAM % icon.",
  'nov.v1_2_0.nuevo6t': "Main model",
  'nov.v1_2_0.nuevo6d': "Star a model and it loads by itself every time Ollama turns on.",
  'nov.v1_2_0.nuevo7t': "Panel where you want it",
  'nov.v1_2_0.nuevo7d': "Drag it by its header and it stays there. Plus an option so the icon never hides behind ^.",
  'nov.v1_2_0.arreglos1t': "RAM shortcut",
  'nov.v1_2_0.arreglos1d': "Moved to Ctrl+Alt+L: Ctrl+Alt+M was taken by the NVIDIA overlay.",
  'nov.v1_2_0.arreglos2t': "Visual details",
  'nov.v1_2_0.arreglos2d': "The RAM icon’s number no longer changes size, and a dark strip at the top of the panel is gone.",
  'nov.v1_1_0.fecha': "Sep 29, 2026",
  'nov.v1_1_0.resumen': "Notifications, watchdog, game mode and the Activity view.",
  'nov.v1_1_0.nuevo1t': "Notifications",
  'nov.v1_1_0.nuevo1d': "Native Windows notifications when a model falls asleep, Ollama crashes, memory gets critical, a game starts or a download finishes.",
  'nov.v1_1_0.nuevo2t': "Watchdog",
  'nov.v1_1_0.nuevo2d': "Restarts Ollama if it crashes or hangs, up to 3 times in 10 minutes.",
  'nov.v1_1_0.nuevo3t': "Game mode",
  'nov.v1_1_0.nuevo3d': "Opening a game turns off Ollama and the other engines; quitting brings back what was on.",
  'nov.v1_1_0.nuevo4t': "Links and sleep shortcut",
  'nov.v1_1_0.nuevo4d': "istargetsleeping://on, off, sleep, load… for Stream Deck or scripts, and Ctrl+Alt+S to put the model to sleep.",
  'nov.v1_1_0.nuevo5t': "GPU memory",
  'nov.v1_1_0.nuevo5d': "A second bar with dedicated VRAM and the model’s share.",
  'nov.v1_1_0.nuevo6t': "Activity and who woke it",
  'nov.v1_1_0.nuevo6d': "This week’s GB reclaimed, naps and hours with a model, a 30-minute chart and which app loaded the model.",
  'nov.v1_1_0.nuevo7t': "Download and delete models",
  'nov.v1_1_0.nuevo7d': "From the panel, with progress and cancel.",
  'nov.v1_1_0.nuevo8t': "Other engines",
  'nov.v1_1_0.nuevo8d': "Its own card and idle timer for llama.cpp; LM Studio as an experiment.",
  'nov.v1_1_0.cambios1t': "Three views",
  'nov.v1_1_0.cambios1d': "The panel splits into main, Activity and Settings, and each one scrolls if it doesn’t fit.",
  'nov.v1_0_0.fecha': "Sep 29, 2026",
  'nov.v1_0_0.resumen': "The first release of isTargetSleeping.",
  'nov.v1_0_0.nuevo1t': "Native Windows app",
  'nov.v1_0_0.nuevo1d': "C# and .NET 10 with WPF and Win32, no external packages. A single .exe for x64 and ARM64.",
  'nov.v1_0_0.nuevo2t': "Identity and black glass",
  'nov.v1_0_0.nuevo2d': "The name, the target with a sleeping eye and a black glass panel over Windows 11’s acrylic.",
  'nov.v1_0_0.nuevo3t': "Turning it on and off properly",
  'nov.v1_0_0.nuevo3d': "Detects whether Ollama comes from its app, a service, a task or an ollama serve, and stops it that way. Ctrl+Alt+O.",
  'nov.v1_0_0.nuevo4t': "Automatic nap",
  'nov.v1_0_0.nuevo4d': "After 5, 15, 30 or 60 minutes without generating, the model leaves memory.",
  'nov.v1_0_0.nuevo5t': "Memory and models",
  'nov.v1_0_0.nuevo5d': "RAM like Task Manager, the model’s real share and the model list with its tags.",
  'nov.v1_0_0.nuevo6t': "Command line",
  'nov.v1_0_0.nuevo6d': "--status, --on, --off, --memory and --idle-test.",
};

const TITULOS_EN = {
  inicio: 'isTargetSleeping · A notch for Windows with Ollama and your widgets',
  novedades: 'What’s new · isTargetSleeping',
};
const PAGINA = document.body.dataset.pagina || 'inicio';
const TITULO = { es: document.title, en: TITULOS_EN[PAGINA] };
const $ = (id) => document.getElementById(id);

// ---------- Idioma ----------

const ES = {};       // el español original, guardado la primera vez que se cambia
let idioma = 'es';

function leerPreferencia() {
  try {
    const p = new URLSearchParams(location.search).get('lang');
    if (p === 'es' || p === 'en') return p;
    const g = localStorage.getItem('its-lang');
    if (g === 'es' || g === 'en') return g;
  } catch (e) { /* almacenamiento bloqueado: seguimos con el navegador */ }
  return (navigator.language || 'es').toLowerCase().startsWith('es') ? 'es' : 'en';
}

function aplicarIdioma(lang) {
  idioma = lang;
  document.documentElement.lang = lang;
  document.title = TITULO[lang];

  document.querySelectorAll('[data-i18n]').forEach((el) => {
    const k = el.dataset.i18n;
    if (!(k in ES)) ES[k] = el.innerHTML;
    el.innerHTML = lang === 'en' && EN[k] ? EN[k] : ES[k];
  });
  document.querySelectorAll('[data-i18n-alt]').forEach((el) => {
    const k = el.dataset.i18nAlt;
    if (!(k in ES)) ES[k] = el.alt;
    el.alt = lang === 'en' && EN[k] ? EN[k] : ES[k];
  });
  document.querySelectorAll('[data-i18n-aria]').forEach((el) => {
    const k = el.dataset.i18nAria;
    if (!(k in ES)) ES[k] = el.getAttribute('aria-label');
    el.setAttribute('aria-label', lang === 'en' && EN[k] ? EN[k] : ES[k]);
  });
  document.querySelectorAll('[data-src-lang]').forEach((el) => {
    const src = el.dataset.srcLang.replace('{lang}', lang);
    if (el.getAttribute('src') !== src) el.setAttribute('src', src);
  });
  document.querySelectorAll('[data-poster-lang]').forEach((el) => {
    el.setAttribute('poster', el.dataset.posterLang.replace('{lang}', lang));
  });

  const btn = $('idioma');
  btn.textContent = lang === 'es' ? 'EN' : 'ES';
  btn.setAttribute('aria-label', lang === 'es' ? 'Switch to English' : 'Cambiar a español');

  document.querySelectorAll('.pie-idioma [data-lang]').forEach((b) => {
    b.setAttribute('aria-current', b.dataset.lang === lang ? 'true' : 'false');
  });

  pintarBotonTema();
  pintarAviso();
  if ($('siesta')) actualizarSiesta();
  if ($('reloj')) actualizarReloj();
}

function elegirIdioma(nuevo) {
  try { localStorage.setItem('its-lang', nuevo); } catch (e) { /* sin almacenamiento */ }
  aplicarIdioma(nuevo);
}
$('idioma').addEventListener('click', () => elegirIdioma(idioma === 'es' ? 'en' : 'es'));
document.querySelectorAll('.pie-idioma [data-lang]').forEach((b) => {
  b.addEventListener('click', () => elegirIdioma(b.dataset.lang));
});

// ---------- Tema claro / oscuro / sistema ----------
// Sin preferencia guardada sigue a Windows; el <head> aplica las preferencias guardadas antes del CSS.

const oscuroDelSistema = matchMedia('(prefers-color-scheme: dark)');

function temaGuardado() {
  try {
    const t = localStorage.getItem('its-theme');
    return t === 'light' || t === 'dark' || t === 'system' ? t : null;
  } catch (e) { return null; }
}

let temaTemporal = null;

function temaElegido() {
  return temaTemporal || temaGuardado() || 'system';
}

function temaActual() {
  const elegido = temaElegido();
  return elegido === 'system' ? (oscuroDelSistema.matches ? 'dark' : 'light') : elegido;
}

function pintarBotonTema() {
  const elegido = temaElegido();
  document.querySelectorAll('[data-theme-choice]').forEach((b) => {
    b.setAttribute('aria-pressed', b.dataset.themeChoice === elegido ? 'true' : 'false');
  });
  document.querySelector('meta[name="theme-color"]')?.setAttribute('content', temaActual() === 'dark' ? '#0A0A0C' : '#FFFFFF');
}

[...document.querySelectorAll('[data-theme-choice]')].forEach((b) => b.addEventListener('click', () => {
  const nuevo = b.dataset.themeChoice;
  temaTemporal = nuevo;
  if (nuevo === 'system') delete document.documentElement.dataset.theme;
  else document.documentElement.dataset.theme = nuevo;
  try { localStorage.setItem('its-theme', nuevo); } catch (e) { /* sin almacenamiento: dura hasta recargar */ }
  pintarBotonTema();
}));

oscuroDelSistema.addEventListener('change', () => {
  if (temaElegido() === 'system') pintarBotonTema();
});

// El video en el otro idioma, sin cambiar el resto de la página
$('video-otro')?.addEventListener('click', (e) => {
  e.preventDefault();
  const v = document.getElementById('tour');
  const actual = v.getAttribute('src').includes('-es.') ? 'es' : 'en';
  const otro = actual === 'es' ? 'en' : 'es';
  v.setAttribute('src', v.dataset.srcLang.replace('{lang}', otro));
  v.setAttribute('poster', v.dataset.posterLang.replace('{lang}', otro));
  v.play().catch(() => {});
  e.currentTarget.textContent = idioma === 'es'
    ? (otro === 'es' ? 'Ver en inglés' : 'Ver en español')
    : (otro === 'en' ? 'Watch in Spanish' : 'Watch in English');
});

// ---------- Siesta: el selector de tiempo sin uso ----------

const SIN_USO = 7; // minutos que lleva el modelo sin generar en la demo
let limite = 30;

function actualizarSiesta() {
  const linea = document.getElementById('siesta-linea');
  const texto = document.getElementById('siesta-texto');
  const barra = linea.querySelector('.barra i');
  const en = idioma === 'en';
  linea.classList.toggle('siesta-linea--pausa', limite === 0);

  if (limite === 0) {
    barra.style.width = '100%';
    texto.textContent = en ? `idle ${SIN_USO} min · stays loaded` : `sin uso ${SIN_USO} min · se queda en memoria`;
  } else if (SIN_USO >= limite) {
    barra.style.width = '100%';
    const hace = SIN_USO - limite;
    texto.textContent = en
      ? `asleep${hace ? ` ${hace} min ago` : ' just now'} · 5.2 GB freed`
      : `dormido${hace ? ` hace ${hace} min` : ' ahora mismo'} · 5.2 GB liberados`;
  } else {
    barra.style.width = `${Math.round((SIN_USO / limite) * 100)}%`;
    const falta = limite - SIN_USO;
    texto.textContent = en
      ? `idle ${SIN_USO} min · unloads in ${falta} min`
      : `sin uso ${SIN_USO} min · se libera en ${falta} min`;
  }
}

document.querySelectorAll('#siesta button').forEach((b) => {
  b.setAttribute('aria-pressed', b.dataset.min === String(limite) ? 'true' : 'false');
  b.addEventListener('click', () => {
    limite = Number(b.dataset.min);
    document.querySelectorAll('#siesta button').forEach((o) => o.setAttribute('aria-pressed', o === b ? 'true' : 'false'));
    actualizarSiesta();
  });
});

// ---------- Atajo: las teclas se iluminan si las pulsas ----------

const teclas = [...document.querySelectorAll('#teclas kbd')];
function marcarTeclas(e) {
  if (!teclas.length) return;
  teclas[0].classList.toggle('activa', e.ctrlKey);
  teclas[1].classList.toggle('activa', e.altKey);
  teclas[2].classList.toggle('activa', e.type === 'keydown' && e.key.toLowerCase() === 'o');
}
addEventListener('keydown', marcarTeclas);
addEventListener('keyup', marcarTeclas);
addEventListener('blur', () => teclas.forEach((t) => t.classList.remove('activa')));

// ---------- Reloj de la barra de tareas ----------

function actualizarReloj() {
  const ahora = new Date();
  const loc = idioma === 'en' ? 'en-US' : 'es-ES';
  const hora = ahora.toLocaleTimeString(loc, { hour: 'numeric', minute: '2-digit' });
  const fecha = ahora.toLocaleDateString(loc, { day: '2-digit', month: '2-digit', year: 'numeric' });
  $('reloj').innerHTML = `${hora}<br>${fecha}`;
}
if ($('reloj')) setInterval(actualizarReloj, 30000);

// ---------- Copiar hashes ----------

document.querySelectorAll('.copiar').forEach((b) => {
  b.addEventListener('click', async () => {
    const hash = b.parentElement.querySelector('code').textContent;
    try {
      await navigator.clipboard.writeText(hash);
      b.textContent = idioma === 'en' ? 'Copied' : 'Copiado';
      b.classList.add('ok');
      setTimeout(() => {
        b.textContent = idioma === 'en' ? EN['inst.copiar'] : ES['inst.copiar'] || 'Copiar';
        b.classList.remove('ok');
      }, 1600);
    } catch (e) { /* el portapapeles no está disponible: el hash sigue a la vista */ }
  });
});

// ---------- Aparición al hacer scroll ----------

const revelables = document.querySelectorAll('.revela');
if ('IntersectionObserver' in window) {
  const io = new IntersectionObserver((entradas) => {
    entradas.forEach((en) => {
      if (en.isIntersecting) { en.target.classList.add('visible'); io.unobserve(en.target); }
    });
  }, { rootMargin: '0px 0px -8% 0px', threshold: 0.08 });
  revelables.forEach((el) => io.observe(el));
} else {
  revelables.forEach((el) => el.classList.add('visible'));
}

// ---------- Descargas ----------
// Los botones «Descargar» son enlaces directos a los archivos de GitHub Releases: GitHub responde con el archivo
// y el navegador lo descarga sin salir de la web (también sin JS). Aquí solo se elige ARM64 si el equipo lo es
// y se avisa de que la descarga ha empezado. Versión y arquitectura salen del propio enlace.

function describirDescarga(href) {
  const m = /isTargetSleeping-(\d+\.\d+\.\d+)-(setup|win)-(x64|arm64)\.(exe|zip)$/.exec(href);
  if (!m) return '';
  const tipo = m[2] === 'setup'
    ? (idioma === 'es' ? 'instalador' : 'installer')
    : (idioma === 'es' ? 'portable (.zip)' : 'portable (.zip)');
  return `isTargetSleeping ${m[1]} · ${tipo} ${m[3] === 'arm64' ? 'ARM64' : 'x64'}`;
}

function pintarAviso() {
  const aviso = $('aviso-descarga');
  if (aviso?.dataset.href) $('aviso-archivo').textContent = describirDescarga(aviso.dataset.href);
}

let cierreAviso = 0;
function cerrarAviso() {
  clearTimeout(cierreAviso);
  $('aviso-descarga').classList.remove('visible');
  cierreAviso = setTimeout(() => { $('aviso-descarga').hidden = true; }, 250);
}

function avisarDescarga(href) {
  const aviso = $('aviso-descarga');
  if (!aviso) return;
  clearTimeout(cierreAviso);
  aviso.dataset.href = href;
  $('aviso-otra').href = href;
  pintarAviso();
  aviso.hidden = false;
  void aviso.offsetWidth;   // aplica el estado inicial para que entre con su transición
  aviso.classList.add('visible');
  cierreAviso = setTimeout(cerrarAviso, 9000);
}

document.querySelectorAll('[data-descarga]').forEach((a) => {
  a.addEventListener('click', () => avisarDescarga(a.href));   // sin preventDefault: la descarga es la del enlace
});
$('aviso-otra')?.addEventListener('click', (e) => avisarDescarga(e.currentTarget.href));
$('aviso-pasos')?.addEventListener('click', cerrarAviso);
$('aviso-cerrar')?.addEventListener('click', cerrarAviso);

// En Windows ARM, los botones principales bajan el instalador ARM64 y el x64 pasa a las alternativas.
navigator.userAgentData?.getHighEntropyValues?.(['architecture']).then(({ architecture }) => {
  if (architecture !== 'arm') return;
  document.querySelectorAll('[data-descarga="setup"]').forEach((a) => {
    a.href = a.href.replace('-setup-x64.exe', '-setup-arm64.exe');
  });
  document.querySelectorAll('[data-descarga="setup-arm64"]').forEach((a) => { a.hidden = true; });
  document.querySelectorAll('[data-descarga="setup-x64"]').forEach((a) => { a.hidden = false; });
}).catch(() => { /* sin datos de arquitectura: x64 */ });

aplicarIdioma(leerPreferencia());
