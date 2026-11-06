// isTargetSleeping · web
// El HTML está en español; aquí va el inglés, el selector de idioma y las pocas cosas interactivas.

const EN = {
  'nav.secciones': 'Sections',
  'castillo.nav.menu': 'Navigation menu',
  'castillo.nav.demo': 'Try the notch',
  'castillo.nav.privacy': 'Privacy',
  'castillo.nav.install': 'Install',
  'castillo.nav.faq': 'FAQ',
  'castillo.nav.download': 'Download',
  'castillo.hero.eyebrow': 'A quieter way to work with Ollama',
  'castillo.hero.title': 'Your PC, your models and your tools. At a glance.',
  'castillo.hero.text': 'Control Ollama and bring useful widgets to the edge of your Windows desktop.',
  'castillo.hero.secondary': 'Explore the scene',
  'castillo.story.aria': 'A white castle emerges through silver mist as the mascot and product appear.',
  'castillo.product.alt': 'isTargetSleeping panel showing widgets and Ollama status',
  'castillo.story.step1.title': 'Let your model rest when it is idle.',
  'castillo.story.step1.text': 'When Ollama stops generating, the model can leave memory. Ollama stays on and reloads it with your next message.',
  'castillo.story.step2.title': 'Keep the useful things close.',
  'castillo.story.step2.text': 'Ollama, system stats, music, Pomodoro and more live in a notch you can open from anywhere.',
  'castillo.story.download': 'Get it for Windows',
  'castillo.scroll': 'Scroll to continue',
  'mundo.aria': 'A walk through the White Castle: five scenes with the mascot.',
  'mundo.mascota': 'The isTargetSleeping mascot',
  'mundo.nav': 'Scenes',
  'mundo.d1': 'Threshold', 'mundo.d2': 'The hall', 'mundo.d3': 'The notch', 'mundo.d4': 'The garden', 'mundo.d5': 'The throne',
  'mundo.probar': 'Try it here',
  'mundo.n.notas': 'Notes', 'mundo.n.reloj': 'Clock and calendar', 'mundo.n.despierto': 'Keep awake', 'mundo.n.bateria': 'Battery', 'mundo.n.codigo': 'Code', 'mundo.n.equipo': 'Team',
  'mundo.w.eyebrow': 'Widgets',
  'mundo.w.title': 'Choose what lives in your notch.',
  'mundo.w.text': 'Local tools you arrange your way. Scroll to see more.',
  'mundo.w.lista': 'Available widgets',
  'mundo.w.ollama': 'Turn it on, watch RAM and let models sleep.',
  'mundo.w.sistema': 'CPU, RAM, VRAM, disk and network.',
  'mundo.w.reloj': 'A month, an agenda and .ics events.',
  'mundo.w.pomodoro': 'With an alert when it ends.',
  'mundo.w.despierto': 'Ask Windows not to sleep.',
  'mundo.w.notas': 'Saved automatically, locally.',
  'mundo.w.musica': 'What is playing, with controls.',
  'mundo.w.bateria': 'Charge at a glance.',
  'mundo.w.codigo': 'A tree, tabs and a small editor.',
  'mundo.w.equipo': 'Split a task between Claude and Codex.',
  'mundo.w.audio': 'The volume of each app.',
  'mundo.w.lmstudio': 'Another engine, experimental.',
  'mundo.n.estante': 'Shelf', 'mundo.w.estante': 'A temporary tray for your files.',
  'mundo.n.tareas': 'Tasks', 'mundo.w.tareas': 'A list with a calendar and daily repeat.',
  'mundo.sw.label': 'Model in memory',
  'mundo.sw.nota': 'A demo with sample data.',
  'mundo.m.eyebrow': 'And there is more',
  'mundo.m.title': 'The details you will appreciate.',
  'mundo.m.text': 'Small things that work in the background so you do not have to think about them.',
  'mundo.m.juego': 'Game mode',
  'mundo.m.juego.d': 'Open a game and Ollama turns off to leave it the RAM and VRAM; when you quit, it turns back on.',
  'mundo.m.vigilante': 'Watchdog',
  'mundo.m.vigilante.d': 'If Ollama crashes or hangs, it restarts it the same way, up to 3 times in 10 minutes.',
  'mundo.m.atajos': 'Keyboard shortcuts',
  'mundo.m.atajos.d': 'Ctrl+Alt+O turns it on or off, Ctrl+Alt+S sleeps the model and Ctrl+Alt+L frees RAM.',
  'mundo.m.enlaces': 'Links for your scripts',
  'mundo.m.enlaces.d': 'istargetsleeping://on, off, toggle, sleep or load/model, for Stream Deck, PowerToys or your scripts.',
  'mundo.m.mascotas': 'Mascots with personality',
  'mundo.m.mascotas.d': 'Mira, a llama, a capybara or an orange kitten live on your taskbar and react to what Ollama does.',
  'mundo.m.despierta': 'Who woke it up',
  'mundo.m.despierta.d': 'See which app loaded the model (Obsidian, Cursor, a script…) by its connection to the Ollama port.',
  'mundo.p.eyebrow': 'Mascots with personality',
  'mundo.p.title': 'Choose who keeps you company.',
  'mundo.p.text': 'They live on your taskbar and tell you what Ollama is doing. They start off; you turn them on.',
  'mundo.p.lista': 'Mascot',
  'mundo.p.estados': 'What Ollama is doing',
  'mundo.p.n.mira': 'Mira',
  'mundo.p.n.llama': 'Llama',
  'mundo.p.n.capibara': 'Capybara',
  'mundo.p.n.gatito': 'Kitten',
  'mundo.p.s.duerme': 'Sleeps',
  'mundo.p.c.duerme': 'Sleeps in its bed while Ollama rests.',
  'mundo.p.s.come': 'Eats',
  'mundo.p.c.come': 'Eats while a model loads.',
  'mundo.p.s.teclea': 'Types',
  'mundo.p.c.teclea': 'Types while the model generates.',
  'mundo.p.s.asea': 'Grooms',
  'mundo.p.c.asea': 'Grooms while RAM is freed.',
  'mundo.p.s.despierta': 'Awake',
  'mundo.p.c.despierta': 'Awake, reacting to your clicks and cursor in its own way.',
  'mundo.w.agentes': 'Active and recent Claude Code and Codex sessions.',
  'mundo.n.agentes': 'AI agents',
  'mundo.w.uso': 'Claude, Codex, Gemini and Muse quotas. Optional.',
  'mundo.n.uso': 'AI usage',
  'mundo.w.clima': 'The weather now and the next hours.',
  'mundo.n.clima': 'Weather',
  'mundo.w.teleprompter': 'Keeps your text locally; edit and play it.',
  'mundo.t.eyebrow': 'See what it does',
  'mundo.t.title': 'Your notch, on any Windows screen.',
  'mundo.t.alt': 'A Windows monitor and a laptop with the wallpaper on screen',
  'mundo.t.lista': 'Notch views',
  'mundo.t.home': 'Home', 'mundo.t.notch': 'Notch', 'mundo.t.actividad': 'Activity', 'mundo.t.panel': 'Panel',
  'mundo.t.home.d': 'The Home has four cells you choose and arrange your way.',
  'mundo.t.notch.d': 'Closed, it is a pill with the three data points you choose.',
  'mundo.t.actividad.d': 'This week: GB recovered, naps and hours with the model loaded.',
  'mundo.t.panel.d': 'Ollama status, PC and GPU memory, and your models.',
  'castillo.demo.eyebrow': 'A small window into your day',
  'castillo.demo.title': 'See it in the notch.',
  'castillo.demo.text': 'Choose a state to explore the same notch, widgets and controls available in the app.',
  'castillo.video.eyebrow': 'Tour · 77 seconds',
  'castillo.video.title': 'Take a closer look.',
  'castillo.video.caption': 'A 1080p tour with sound and current app captures. Sample data.',
  'castillo.video.other': 'Watch in Spanish',
  'castillo.features.eyebrow': 'Made to stay within reach',
  'castillo.features.title': 'One quiet place for the things you use.',
  'castillo.features.text': 'A Windows notch that keeps Ollama and everyday tools close, without opening another window.',
  'castillo.features.ollama.title': 'Ollama, without the detour',
  'castillo.features.ollama.text': 'Turn it on or off, see model memory use and let idle models sleep to get RAM back.',
  'castillo.features.widgets.title': 'Your tools, arranged your way',
  'castillo.features.widgets.text': 'Choose widgets for system stats, clock, Pomodoro, notes, music, clipboard and more.',
  'castillo.features.private.title': 'Local by design',
  'castillo.features.private.text': 'No account or telemetry. Models, notes and settings stay on your PC.',
  'castillo.privacy.eyebrow': 'Your PC stays yours',
  'castillo.privacy.title': 'It all happens on your PC.',
  'castillo.privacy.text': 'The app reads local services and device data. The optional mobile web app is served from this PC inside your Tailscale tailnet, not as a public website, and works without an ESP32 board. Optional update checks contact GitHub; weather sends only the city you choose to Open-Meteo.',
  'castillo.privacy.link': 'Read the privacy details',
  'castillo.install.eyebrow': 'Windows 10 and 11',
  'castillo.install.title': 'A minute to get started.',
  'castillo.install.text': 'Download the self-contained installer, open it, then find the mascot by the Windows clock.',
  'castillo.install.version': 'Latest release: 1.29.0 · x64 and ARM64 · No .NET install required',
  'castillo.install.portable': 'Portable x64 (.zip)',
  'castillo.install.arm': 'Installer for ARM64',
  'castillo.install.armzip': 'Portable ARM64 (.zip)',
  'castillo.source.eyebrow': 'Open source · MIT',
  'castillo.source.title': 'Nothing hidden in the tray.',
  'castillo.source.text': 'Read the code, see how it works, or help make it better on GitHub.',
  'castillo.source.link': 'Explore the repository',
  'castillo.close.eyebrow': 'Free and open source',
  'castillo.close.title': 'Let your model rest.',
  'castillo.close.text': 'Bring Ollama and the tools you use to one small place on your Windows desktop.',
  'castillo.close.download': 'Download for Windows',
  'castillo.footer': 'Designed and built by CodeSentry - Tykillita.',
  'castillo.footer.home': 'Main website',
  'pet.e6': "Waiting for a model",
  'pet.c6': "Peeks out from behind the Windows logo while Ollama waits for a model.",
  'pet.grupoMascota': "Choose a pet",
  'pet.grupoEstado': "Choose what Ollama is doing",
  'pet.grupoDemo': "Choose an Ollama state or pet interaction",
  'pet.estadosTitulo': "Ollama status",
  'pet.estadosAyuda': "Choose an Ollama state; petting is shown separately.",
  'pet.reaccion': "Pet reaction",
  'pet.interaccion': "Pet interaction",
  'pet.capibara': "Capybara",
  'pet.gatito': "Kitten",
  'pet.e0': "Ollama off",
  'pet.e1': "Loading",
  'pet.e2': "Generating",
  'pet.e3': "Downloading",
  'pet.e4': "Freeing RAM",
  'pet.e5': "Petting",
  'pet.c0': "Sleeps in its bed while Ollama is off.",
  'pet.c1': "Eats while Ollama loads a model.",
  'pet.c2': "Types on a laptop while Ollama generates.",
  'pet.c3': "Carries a box while a model downloads.",
  'pet.c4': "Grooms in its own way while it frees RAM.",
  'pet.c5': "Drops hearts when you pet it; tap the pet.",
  'sim.b.pet': "Resting",
  'sim.c.pet': "Resting: the mascot sleeps in the closed notch and opens its eyes if you touch it.",
  'sim.titulo': "Try it here",
  'sim.aria': "isTargetSleeping notch simulation",
  'sim.pomo': "Pomodoro",
  'sim.pomoFoco': "Focus time",
  'sim.descanso': "Break",
  'sim.foco': "Focus",
  'sim.enRam': "GB in RAM",
  'sim.ollamaDuerme': "llama3.1:8b · sleeps in 12 min",
  'sim.liberar': "Free RAM",
  'tb.buscar': "Search",
  'tb.alt': "The Windows taskbar with the isTargetSleeping mascot",
  'tb.eyebrow': "Your mascot, on the taskbar",
  'tb.title': "See what Ollama is doing.",
  'tb.text': "Switch the mascot and Ollama's state, and watch it react right above your taskbar. Hover over it or tap it to pet it.",
  'sim.liberarModelo': "Free model",
  'sim.sinModelo': "No model",
  'sim.ollamaApagado': "Apps that use Ollama don't respond while it is off.",
  'sim.disco': "Disk",
  'sim.procesos': "Processes",
  'sim.sistema': "System",
  'sim.carga': "Charging",
  'sim.cargador': "Charger connected",
  'sim.grupo': "Choose what to see in the notch",
  'sim.b.idle': "Closed",
  'sim.b.music': "Music",
  'sim.b.lyrics': "Lyrics",
  'sim.b.pomo': "Pomodoro",
  'sim.b.system': "System",
  'sim.b.charge': "Battery",
  'sim.c.idle': "Closed, it stays a small pill with the battery, the time and the music.",
  'sim.c.music': "Music: cover, progress and controls for what is playing.",
  'sim.c.lyrics': "Synced lyrics beside it, with the current line highlighted.",
  'sim.c.pomo': "Pomodoro: focus and breaks without leaving what you are doing.",
  'sim.c.ollama': "Ollama: turn it on, see the RAM it uses and free it.",
  'sim.c.system': "System: CPU, memory and GPU at a glance.",
  'sim.c.charge': "Battery notice: it shows when you plug in the charger.",
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

  'hero.eyebrow': "Version 1.27.1 available · Native Windows",
  'hero.titulo': 'A notch for Windows: Ollama, your PC and your tools, at a glance.',
  'hero.sub': 'isTargetSleeping lives next to the Windows clock or, if you like, at the edge of the screen as a notch. It turns Ollama on and off, puts its models to sleep when they stop working and gives you your RAM back, and adds widgets: system, clock, Pomodoro, notes…',
  'hero.meta': 'Free and open source · Latest version: 1.27.1 · Windows 10 and 11 · x64 and ARM64',
  'hero.escritorio': 'The isTargetSleeping panel open on the Windows desktop',
  'hero.panelAlt': 'Panel: Ollama on, PC and GPU memory, llama.cpp and the installed models',
  'cta.windows': 'Download for Windows',
  'cta.como': 'See how it works',

  'pill.nativa': 'Native: .NET 10 and Win32',
  'pill.cuentas': 'No desktop account',
  'pill.idiomas': 'English and Spanish',
  'pill.mit': 'Open source, MIT',

  'video.eyebrow': 'Version 1.27.1, in 77 seconds',
  'video.titulo': 'Take a closer look.',
  'video.pie': 'In 1080p, with sound and screenshots from version 1.27.1. Sample data.',
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
  'estado.texto': 'The mascot next to the clock tells you how Ollama is doing. In Settings › General you can always show it; if the toggle is unavailable, Pin the icon from Windows lets you open the taskbar settings.',
  'estado.on': 'On',
  'estado.onDet': 'blue dot',
  'estado.cambio': 'Starting or stopping',
  'estado.cambioDet': 'amber dot and motion',
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
  'priv.sub': 'It talks to Ollama’s local API at <code>127.0.0.1:11434</code> (or <code>OLLAMA_HOST</code>, if you set it) and to the other engines’ local ports. Widgets read your PC locally; the optional mobile web app is served from this PC inside your Tailscale tailnet, not as a public website, and works without an ESP32 board. The Camera page only shows video while open and never records, saves or sends it. In Comet, you can block individual sites in its camera permissions; Windows only offers a global control for other desktop apps. Agent logs are read only if you enable that feature and never leave your PC. The app can show short excerpts from agent messages, never user prompts. Weather is off by default and sends only the city you choose to Open-Meteo. The notch glass uses a screen copy held in memory; web pages only open when you click.',
  'priv.c1': 'No app account',
  'priv.c1t': 'The desktop app needs no account. Private mobile access uses your Tailscale account.',
  'priv.c2': 'No telemetry',
  'priv.c2t': 'No analytics either. Your 90-day history and your notes stay in <code>%LOCALAPPDATA%</code>.',
  'priv.c3': 'Optional connections',
  'priv.c3t': 'If enabled, update checks contact GitHub. Weather is off by default and sends only your chosen city to Open-Meteo. The optional mobile web app connects to this PC inside your Tailscale tailnet, with no public endpoint and no ESP32 requirement.',
  'priv.admin': 'UAC is requested when enabling or updating the RAM agent, when controlling Ollama as a service if needed, or when you choose to retry process termination as administrator.',

  'inst.eyebrow': 'Install',
  'inst.titulo': 'One minute. No admin.',
  'inst.sub': 'A single self-contained .exe: you don’t need to install .NET.',
  'inst.p1': 'Download it',
  'inst.p2': 'Open it',
  'inst.p2t': 'It installs for your user in <code>%LOCALAPPDATA%\\Programs</code>. No UAC.',
  'inst.p3': 'Look by the clock',
  'inst.p3t': 'Find the mascot in the hidden icons (^). In Settings › General, turn on Always show in the taskbar or click Open taskbar settings to pin it from Windows.',
  'inst.zip': 'Portable x64 (.zip)',
  'inst.arm': 'ARM64 installer',
  'inst.armZip': 'Portable ARM64 (.zip)',
  'inst.x64': 'x64 installer',
  'dl.titulo': 'Your download has started',
  'dl.otra': 'Didn’t start? Try again',
  'dl.pasos': 'See the steps',
  'dl.cerrar': 'Close notice',
  'inst.verificar': 'Verify the download (optional)',
  'inst.copiar': 'Copy',
  'inst.smart': 'Every file in the release has its <code>.sha256</code> next to it. If the build isn’t signed, Windows SmartScreen may ask you to confirm: <b>More info › Run anyway</b>.',
  'inst.meta': '~75 MB · Windows 10 (2004+) or 11 · requires Ollama · latest published version: 1.27.1 · GitHub Releases',

  'auto.eyebrow': 'For automators',
  'auto.titulo': 'Links and flags for your scripts.',
  'auto.texto': '<code>istargetsleeping://</code> links work from Stream Deck, PowerToys or a shortcut. The same .exe answers to flags, through the same path as the panel.',
  'auto.enlaces': 'Links',
  'auto.c1': '# detected mechanism and whether the API responds',
  'auto.c2': '# the same path as the panel button',
  'auto.c3': '# puts the models to sleep without turning anything off',
  'auto.c4': '# RAM, runner CPU time and GPU memory',

    'cx.idle.num': '2.5',
  'cx.ag.f1': 'Next version · Team',
  'cx.ag.f2': 'Next version · Own branch',
  'cx.ag.f3': 'Next version · History and schedules',
  'cx.ag.f4': 'Next version · Three looks',
  'cx.ag.f8': 'Next version · Agent tasks',
  'cx.ctl.orden': 'It looks in this order',
  'cx.idle.k1': 'Measures the runners’ CPU time',
  'cx.idle.k1d': 'every',
  'cx.idle.k2': 'of extra CPU counts as activity',
  'cx.idle.k3': 'With no activity, the model leaves memory',
  'cx.idle.k3b': 'Ollama stays on',
  'cx.mem.eyebrow': 'Memory and models',
  'cx.mem.titulo': 'See what takes up what.',
  'cx.mem.texto': 'PC and per-GPU memory, the models you have and the apps using the most RAM, all in one place.',
  'cx.mem.gpu': 'GPU',
  'cx.proc.cabecera': 'Processes tab',
  'cx.ag.eyebrow': 'AI agents',
  'cx.ag.titulo': 'Your coding agents, at a glance.',
  'cx.ag.texto': 'Hand out work to your agents from the notch and review the result before you accept it.',
  'cx.ag.p1': 'Plan',
  'cx.ag.p2': 'Implement',
  'cx.ag.p3': 'Review',
  'cx.ag.p3d': 'You approve',
  'cx.nov.todas': 'See everything that’s new',
  'cx.inst.req': '~75 MB · Windows 10 (2004+) or 11 · needs Ollama',
  'cx.inst.titulo': 'Three steps',
  'cx.priv.red': 'It talks to Ollama’s local API at',
  'cx.src.validacion': 'Validation report',
  'cx.src.changelog': 'Changelog',
  'cx.src.licencia': 'MIT license',
  'cx.src.seguridad': 'Security',
  'cx.src.contribuir': 'Contribute',
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
  'faq.q2': 'How do I turn Ollama on or off?',
  'faq.a2': 'Use the power button in the Ollama widget in the notch, the classic panel if you enabled it, or Ctrl+Alt+O. The app detects how Ollama started and stops it through that same mechanism.',
  'faq.q3': 'How do I unload a model without turning Ollama off?',
  'faq.a3': 'Set the idle timeout in the Ollama widget. To unload a model now, use its Unload model button or Ctrl+Alt+S. Ollama stays on and loads the model again when it receives another request.',
  'faq.q4': 'Is it an official Ollama app?',
  'faq.a4': 'No. It is an independent project by CodeSentry - Tykillita and is not affiliated with Ollama.',
  'faq.q5': 'Does it need admin rights?',
  'faq.a5': 'Normal use does not need it. Windows may ask for UAC to install or update the RAM agent, manage a service, or retry closing a protected process as administrator.',
  'faq.q6': 'What happens to my apps while the model sleeps?',
  'faq.a6': 'Nothing odd. Ollama keeps listening, so Obsidian, Cursor or your script send their message as usual. The first reply takes a little longer while the model loads again.',
  'faq.q7': 'Does it make models faster?',
  'faq.a7': 'No. It frees the RAM and VRAM used by idle models so the rest of your PC can use them. When you request a sleeping model again, Ollama takes a little while to load it.',
  'faq.q8': 'Can I turn off the notch mascot?',
  'faq.a8': 'Yes. It is optional and off by default. When enabled in Settings > Notch, it appears after about 40 seconds without input and respects the reduced-motion setting.',
  'faq.q9': 'How does it update?',
  'faq.a9': 'Automatic update checks are optional and off by default. When enabled, the app checks GitHub for stable releases; you choose when to install. It verifies SHA-256 and restores the previous version if an update fails.',
  'faq.q10': 'Does it work on Mac or Linux?',
  'faq.a10': 'No. The app runs on Windows 10 (version 2004 or later) and Windows 11, on x64 and ARM64 PCs.',
  'faq.q11': 'Where do I report a bug?',
  'faq.a11': 'In the <a href="https://github.com/Tykillita/isTargetSleeping/issues">GitHub issues</a>. Attach the log from <code>%LOCALAPPDATA%\\isTargetSleeping\\Logs\\app.log</code> if you can. For a vulnerability, use the <a href="https://github.com/Tykillita/isTargetSleeping/security/advisories/new">private report</a> in the security policy.',

  'cierre.titulo': 'Let your model rest.',
  'cierre.texto': 'Free for Windows. No accounts. Awake when you talk to it, asleep when you don’t.',
  'cierre.meta': "Version 1.27.1 · Windows 10 and 11 · x64 and ARM64",

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
  'pie.version': "Version 1.29.0 available",
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
  'widg.l5': "On both sides of the closed pill, live activities show what is happening now: Ollama’s on the left, plus a camera status dot when Windows reports webcam use; enable Camera to keep it visible while off, after Free RAM. Click the dot to open a local preview. The volume, Pomodoro and, if enabled, music appear on the right.",
  'nov.v1_9_0.fecha': 'Oct 15, 2026',
  'nov.v1_9_0.resumen': 'Live activities beside the closed notch pill, a Music widget with cover art, controls and a clickable progress bar, and the volume in the notch.',
  'nov.v1_9_0.actividadesT': 'Live activities',
  'nov.v1_9_0.actividadesD': 'Next to the closed pill’s data you see what is happening now: freeing RAM, Ollama turning on or off, critical memory, a running Pomodoro, Keep awake and the music that is playing. The pill grows to make room, two fit at a time (the most urgent first) and each has its own switch on the Widgets page.',
  'nov.v1_9_0.volumenT': 'Volume in the notch',
  'nov.v1_9_0.volumenD': 'When the Windows volume changes (or you mute it), the closed pill shows it for a couple of seconds, for example “98 %” or “Muted”. It shows up in addition to the Windows indicator, which can’t be removed in a supported way. It only reads the volume, never changes it, and has its own switch.',
  'nov.v1_9_0.musicaT': 'Music widget',
  'nov.v1_9_0.musicaD': 'Title, artist, cover art, progress and the previous, play or pause and next controls for whatever Windows says is playing (Spotify, the browser, the media player…). The progress bar can be clicked or dragged to jump to another point in the song. Swipe the widget left or right (with the mouse, or two fingers on the trackpad) to skip tracks. It is read on your PC and only when something needs it; the “Music playing” activity comes turned off.',
  'widg.l6': 'Drag files onto the notch and they wait in the Tray until you drop them wherever you like; text files can be edited right there. The clipboard history is optional, text only and in memory only, and favorites open your apps too. The Code widget is a small IDE: file tree with Material Icon Theme icons, tabs, an image viewer and an editor. The closed pill can also show the music that is playing, with its title scrolling.',
  'dev127.eyebrow': 'Version 1.27.1 · Available',
  'dev127.titulo': 'Your widgets, in an iPhone-style 3D carousel.',
  'dev127.sub': 'Version 1.27.1 fixes the gallery’s 3D turn so it keeps the original cards, adds the camera indicator, and includes Team, accessibility and reliability improvements still in development.',
  'dev127.f1': '1.27.1 · Team',
  'dev127.f1t': 'Each step, to the right agent',
  'dev127.f1d': 'A router decides who plans, implements and reviews, and with which of your Codex models. Attach images, pick the split and the effort (Auto to Max, with a tachometer) and approve or edit the plan first.',
  'dev127.f2': '1.27.1 · Own branch',
  'dev127.f2t': 'Never touches your code without permission',
  'dev127.f2d': 'Each task works in its own git worktree and branch; the other model reviews the diff and, whenever you like, you read it in the notch, ask for changes or merge.',
  'dev127.f3': '1.27.1 · History and schedules',
  'dev127.f3t': 'Agents that work while you\'re away',
  'dev127.f3d': 'Repeat a task daily, weekly or every N hours, with a token cap, also from Windows Task Scheduler. Each run stays in the history with its branch.',
  'dev127.f4': '1.27.1 · Three looks',
  'dev127.f4t': 'Pipeline, Timeline or Crew',
  'dev127.f4d': 'Choose how to see the work in Settings. The closed notch can show progress as a pill item, a live activity or a cancel favorite.',
  'dev127.f5': '1.27.1 · Compact widgets',
  'dev127.f5t': 'More detail in one cell',
  'dev127.f5d': 'Ollama, Code and LM Studio now have a 1×1 view. Code summarizes the README and detects the local stack; the teleprompter can edit, restart and show progress, and Keep awake reflects the state Windows accepted.',
  'dev127.f6': '1.27.1 · Gallery',
  'dev127.f6t': 'The same cards turn in 3D',
  'dev127.f6d': 'The transition keeps the original WPF cards and moves the chosen side preview to the center along a 3D arc, without replacing the card or jumping in zoom. Viewport-fixed 3D cloud columns appear only at 1×1, gently billow around a fixed center without drifting, and fade out at larger sizes. Notes keeps its yellow accent.',
  'dev127.f7': '1.27.1 · Camera',
  'dev127.f7t': 'A camera status dot beside the notch',
  'dev127.f7d': 'A centered dot stays neutral while idle and gets a soft blue halo when Windows reports camera use. You can keep it visible while the camera is off and turn on a local preview with its switch; it stops when you leave. The page lists apps streaming now. In Comet, Manage opens site permissions so you can block Discord Web; Windows only offers one global setting for other desktop apps.',
  'dev127.f8': '1.27.1 · Agent tasks',
  'dev127.f8t': 'The notch tells you when a task finishes',
  'dev127.f8d': 'With live activity enabled, local scheduled Codex or Claude Code runs that include their task name in the log show it on completion. Cloud tasks are not detected.',
  'dev127.f9': '1.27.1 · Music',
  'dev127.f9t': 'A shortcut when nothing is playing',
  'dev127.f9d': 'Open Spotify and other recognized installed players. If none are present, the widget shows the gallery preview with an original slow New Orleans jazz track: paused, one play-through by default, with optional looping.',
  'dev127.f10': '1.27.1 · Folders',
  'dev127.f10t': 'Neutral smoked glass',
  'dev127.f10d': 'Widget folders use graphite without a blue cast, with soft reflections that blend into the dark page. The 3×3 grid stays fixed and icons fill row by row.',
  'dev127.estado': 'Version 1.27.1 is available on GitHub Releases for Windows x64 and ARM64.',
  'dev129.eyebrow': 'Version 1.29.0',
  'dev129.titulo': 'A private pet on your phone, agents under control.',
  'dev129.sub': 'The mobile web app uses Tailscale and works without an ESP32; the board stays optional.',
  'dev129.f1': '1.29.0 · Agents', 'dev129.f1t': 'Everything waiting for you, together', 'dev129.f1d': 'Permissions, questions, plans, alerts and recent sessions share one inbox. Review your history and saved Always rules there too.',
  'dev129.f2': '1.29.0 · Safety', 'dev129.f2t': 'Sensitive commands need your attention', 'dev129.f2d': 'Recursive deletes, Git force pushes and other risky commands are marked in red: Y cannot approve them and they cannot be saved as Always.',
  'dev129.f3': '1.29.0 · Mobile pet', 'dev129.f3t': 'Private web app; optional ESP32', 'dev129.f3d': 'In Settings › Notch, download the companion and open the mobile web app over Tailscale. Chat with your pet even without an ESP32 board.',
  'dev129.f4': '1.29.0 · Agents', 'dev129.f4t': 'Resume a session in its terminal', 'dev129.f4d': 'Continue idle Claude Code or Codex sessions in Windows Terminal, from their original folder, without opening two writers at once.',
  'dev129.f5': '1.29.0 · Team', 'dev129.f5t': 'Scheduled tasks pause after repeated failures', 'dev129.f5d': 'After ten failures in a row, a routine stops and alerts you. Turn it back on whenever you are ready.',
  'dev129.f6': '1.29.0 · Pet', 'dev129.f6t': 'Voice, a weekly recap and a bond', 'dev129.f6d': 'Speak one phrase each time you press the microphone, review your week and let your pet’s mood respond to what happens in the app.',
  'dev129.f7': '1.29.0 · Notch', 'dev129.f7t': 'See what your agents are doing', 'dev129.f7d': 'A light moves around the notch to show an agent’s state; optional soft chimes and alert controls let you choose how much it interrupts you.',
  'dev129.f8': '1.29.0 · System', 'dev129.f8t': 'More context in the notch', 'dev129.f8d': 'See Git branches and session context, notice Bluetooth connections and open Processes in its own tab.',
  'dev129.f9': '1.29.0 · Widgets', 'dev129.f9t': 'A file shelf and tasks with a calendar', 'dev129.f9d': 'Keep file paths in Shelf and organize tasks with times, daily repeats and a month calendar.',
  'dev129.estado': 'Download version 1.29.0 for Windows x64 and ARM64.',
  'dev128.eyebrow': 'Version 1.28.0 · In development',
  'dev128.titulo': 'The notch becomes the main version.',
  'dev128.sub': 'In 1.28.0 the notch starts turned on and is no longer experimental. The tray panel stays available as the “Legacy version”.',
  'dev128.f1': '1.28.0 · Notch', 'dev128.f1t': 'On by default',
  'dev128.f1d': 'After installing or updating, the app opens the notch at the top edge of the screen instead of the tray panel.',
  'dev128.f2': '1.28.0 · Legacy version', 'dev128.f2t': 'The tray panel, optional',
  'dev128.f2d': 'If you prefer the classic panel, turn on “Legacy version” in Settings › Notch; it is off by default.',
  'dev128.f3': '1.28.0 · Mascot', 'dev128.f3t': 'A mascot in the closed notch',
  'dev128.f3d': 'If you turn it on in Settings › Notch, after a while without keyboard or mouse input, a mascot with its own eyes takes over the notch, asleep; when you come back it greets you and makes way for the normal notch, and alerts appear beside it. Music or opening the notch send it away.',
  'dev128.f4': 'Next version · Shelf', 'dev128.f4t': 'A tray for your files',
  'dev128.f4d': 'Drop files or folders on the Shelf widget, open them with a click and drag them out when you need them. It only keeps their paths and never moves or copies anything.',
  'dev128.f5': 'Next version · Tasks', 'dev128.f5t': 'A list with a calendar',
  'dev128.f5d': 'The Tasks widget counts what you have done today, repeats tasks every day and has a month calendar to see a day or prepare future ones.',
  'dev128.estado':'Version 1.28.0 downloads will appear here when its GitHub release is published.',
  'nov.v1_29_0.indice': '1.29.0',
  'nov.v1_29_0.fecha': '5 November 2026',
  'nov.v1_29_0.resumen': 'The Agents page brings together permissions, questions, alerts and sessions. The private mobile web app opens over Tailscale and works without an ESP32 board.',
  'nov.v1_29_0.petT': 'Private mobile web app; optional ESP32',
  'nov.v1_29_0.petD': 'Download the companion from Settings › Notch and use its QR code to open the mobile web app over Tailscale. The web app works without a board; ESP32 is an optional client.',
  'nov.v1_29_0.centroT': 'One inbox for your agents',
  'nov.v1_29_0.centroD': 'The Agents page brings together pending permissions, questions, plans, alerts and sessions. History keeps what each agent asked and how you answered; Rules lets you review or revoke Always rules.',
  'nov.v1_29_0.guardiaT': 'Protection for sensitive commands',
  'nov.v1_29_0.guardiaD': 'Recursive deletes, git push --force, sudo and other risky commands are marked in red and require a deliberate click. Y cannot approve them, they cannot be saved as Always and no rule lets them through automatically.',
  'nov.v1_29_0.preguntasT': 'Answer questions and plans from the notch',
  'nov.v1_29_0.preguntasD': 'When Claude Code asks a question, choose one or several options on the card. You can also approve its plan or request changes, or leave the answer to the terminal.',
  'nov.v1_29_0.alertasT': 'Choose when agents alert you',
  'nov.v1_29_0.alertasD': 'Choose every alert, only important ones or none, snooze them and set how long the notch waits for a permission answer. Requests remain visible on the Agents page.',
  'nov.v1_29_0.sesionesT': 'More context in session rows',
  'nov.v1_29_0.sesionesD': 'Rows show each session’s Git branch and, for Claude Code, context used. The Now page combines permissions, questions, plans, alerts, away summaries and paused routines.',
  'nov.v1_29_0.retomarT': 'Resume sessions in a real terminal',
  'nov.v1_29_0.retomarD': 'Open finished or idle Claude Code and Codex sessions in Windows Terminal from their original folder. The action is never offered while a session is still working.',
  'nov.v1_29_0.luzT': 'Soft light and sounds for your agents',
  'nov.v1_29_0.luzD': 'A light moves around the notch edge to show agent status; a short chime can play when an agent asks for permission or finishes. You can also see what happened while you were away.',
  'nov.v1_29_0.mascotaT': 'Chat, voice and a bond with your pet',
  'nov.v1_29_0.mascotaD': 'Chat can listen to one phrase when you press the microphone, recap your week and reflect your pet’s mood and bond. Ambient chatter is optional and does not use the model.',
  'nov.v1_29_0.equipoT': 'Scheduled tasks pause after repeated failures',
  'nov.v1_29_0.equipoD': 'A Team routine that fails ten times in a row switches itself off and keeps its history. A successful run resets the counter.',
  'nov.v1_29_0.bluetoothT': 'Bluetooth connections in the notch',
  'nov.v1_29_0.bluetoothD': 'When a Bluetooth accessory connects or disconnects, its card shows its name, icon and battery when Windows reports it.',
  'nov.v1_29_0.procesosT': 'Processes in their own tab',
  'nov.v1_29_0.procesosD': 'Open the process explorer from a dedicated tab in the app window; Activity › Processes remains available in the notch and tray panel.',
  'nov.v1_29_0.notchT': 'Smoother transitions and clearer notifications',
  'nov.v1_29_0.notchD': 'The notch dock and pages move more smoothly. Windows notifications use the high-resolution logo and show Activity actions and download progress.',
  'nov.v1_29_0.arreglosT': 'Permission requests wait during full screen',
  'nov.v1_29_0.arreglosD': 'A request no longer opens the notch or steals focus while you play a game or watch a video; it waits and opens when you leave. Notification priority and Deny text also respect custom activities and high contrast.',
  'nov.v1_28_0.indice': '1.28.0 · Included in 1.29.0',
  'nov.v1_28_0.fecha': 'Included in 1.29.0',
  'nov.v1_28_0.resumen': 'The notch is no longer experimental and becomes the main version: it starts turned on. The tray panel becomes the “Legacy version”.',
  'nov.v1_28_0.pantallasT': 'Notch screens',
  'nov.v1_28_0.pantallasD': 'In Settings › Display you choose where the notch lives: on the main screen by default, or on one screen, several or all of them (each with its own), or let it follow the cursor as before. Duplicated monitors share a single one.',
  'nov.v1_28_0.ocultarT': 'Hide in full screen, with finer rules',
  'nov.v1_28_0.ocultarD': 'A switch in Settings › General (on) moves the notch away only from the monitor of the full-screen app. Start and Search, an app list, a locked session and UAC also hide it. Showing it again waits a quarter of a second so Alt+Tab does not flicker.',
  'nov.v1_28_0.abrirT': 'Open on hover or click, and speed',
  'nov.v1_28_0.abrirD': 'In Settings › General: the short view opens when the mouse passes over the pill or when you press it, and animations can be normal or fast.',
  'nov.v1_28_0.hoverT': 'Short view in pages',
  'nov.v1_28_0.hoverD': 'Hovering shows one widget per page (or two, or four), with arrows, dots, mouse wheel and ← →. Hovering the dots makes the notch grow below with a mini dock: one icon per page and Settings. Music shows the cover, times, controls and volume.',
  'nov.v1_28_0.pastillaT': 'New closed pill',
  'nov.v1_28_0.pastillaD': 'Smaller (150×30), with the time and AM/PM in the middle, a battery ring on the left, white indicators on the right (a vibrating bell for notifications, music with its cover and equalizer) and the camera with its usual dot.',
  'nov.v1_28_0.ajustesT': 'Settings with a sidebar',
  'nov.v1_28_0.ajustesD': 'Eight categories with colored icons and a title per page, instead of two very long pages.',
  'nov.v1_28_0.f11T': 'The notch no longer stays over a video',
  'nov.v1_28_0.f11D': 'When a window enters or leaves full screen without changing windows (F11), the notch steps aside and comes back at once, without waiting for the mouse to touch it. A hidden app no longer hides a pinned notch, and the notch is not drawn over the lock screen.',
  'nov.v1_28_0.animT': 'Open and close animations',
  'nov.v1_28_0.animD': 'The shape opens with a damped spring (about 350 ms, with one soft bounce) and closes in about 300 ms without dipping while the content cross-fades with the pill, and it closes 0.15 s after the mouse leaves (it used to take 0.4 s).',
  'nov.v1_28_0.avisosT': 'System popups in the notch',
  'nov.v1_28_0.avisosD': 'Volume, brightness, Caps Lock, Num Lock and the keyboard language (with its EN/ES chip) show for about two seconds in the closed notch, in a wider black capsule with an icon and a bar or state, like Nusa.',
  'nov.v1_28_0.letrasT': 'Synced lyrics',
  'nov.v1_28_0.letrasD': 'A lyrics button on the music page opens a wide view with the current line highlighted, and the closed notch can show that line instead of the clock. They are fetched from lrclib.net (it receives title and artist) and cached on disk; turn them off in Settings › General.',
  'nov.v1_28_0.mascotaT': 'Notch mascot',
  'nov.v1_28_0.mascotaD': 'Optional, in Settings › Notch. After about 40 seconds without keyboard or mouse input, a mascot with its own eyes and animations takes over the closed notch, asleep; if something happens it wakes up, looks around, blinks and smiles, then dozes off again. When you come back, it opens its eyes, greets you and fades out to the normal notch. Activities and the camera appear in their usual place beside it; music or opening the notch send it away.',
  'nov.v1_28_0.notchT':'The notch, by default',
  'nov.v1_28_0.notchD': 'The notch appears at the top edge of the screen when the app opens. You no longer need to turn it on in Settings, and it no longer carries the “experimental” label.',
  'nov.v1_28_0.legacyT': 'Legacy version',
  'nov.v1_28_0.legacyD': 'The panel that pops out of the tray is now called the “Legacy version”, is off by default, and is turned on in Settings › Notch.',
  'nov.v1_28_0.estado': 'The 1.28.0 features are included in the 1.29.0 release.',
  'nov.v1_27_1.indice': '1.27.1',
  'nov.v1_27_1.fecha': 'October 27, 2026',
  'nov.v1_27_1.resumen': 'The Widgets gallery keeps the same WPF card views through the 3D turn. Clouds slowly billow around a fixed center only at 1×1, stay fixed to the viewport edges and fade out at larger sizes. Ollama, Code and LM Studio also gain 1×1 views, the teleprompter gets new controls, Keep awake reports its state more clearly, and the notch gains a camera indicator with a local preview and new idle states for Music.',
  'nov.v1_27_1.cameraT': 'Camera beside the notch',
  'nov.v1_27_1.cameraD': 'A centered dot shows the state and gets a soft blue halo when Windows reports camera use. You can also keep it visible while the camera is off and add a dock page for a local preview that only starts with its switch. The card lists apps streaming now. In Comet, Manage opens site permissions so you can block Discord Web; Windows only offers a global control for other desktop apps.',
  'nov.v1_27_1.agendadasT': 'Local scheduled-task alerts',
  'nov.v1_27_1.agendadasD': 'With agent live activity enabled, the notch briefly shows the name when a local Codex or Claude Code task identified in its logs finishes. It does not read or show its prompt or result; cloud tasks are not detected.',
  'nov.v1_27_1.musicaInactivaT': 'Music is ready when idle',
  'nov.v1_27_1.musicaInactivaD': 'When nothing is playing, the widget offers shortcuts to recognized installed players. If none are found, it shows the gallery preview with an original slow New Orleans jazz track; it starts paused, plays once, and repeats only if you enable looping. The app list and audio stay on the PC.',
  'nov.v1_27_1.compactoT': 'Useful widgets in 1×1',
  'nov.v1_27_1.compactoD': 'Ollama and LM Studio keep their power switch, model and memory in one cell. Code shows the project name, technologies detected from its files and a README excerpt; the summary is processed on the PC.',
  'nov.v1_27_1.teleprompterT': 'A pocket teleprompter',
  'nov.v1_27_1.teleprompterD': 'In 1×1, edit the script, start or pause scrolling, jump back to the beginning and follow its progress. An empty script invites you to type or paste one.',
  'nov.v1_27_1.awakeT': 'Clearer Keep awake status',
  'nov.v1_27_1.awakeD': 'The widget lays out the state and switch on one row, and marks the PC awake only when Windows confirms the request.',
  'nov.v1_27_1.galeriaT': 'Natural 3D card transition',
  'nov.v1_27_1.galeriaD': 'Each card keeps its own live view while it moves along a 3D arc. Its bindings update only after it leaves the 3D viewport, avoiding a mid-turn replacement and size jump.',
  'nov.v1_27_1.actividadesT': 'Faster notch activities',
  'nov.v1_27_1.actividadesD': 'Camera, brightness and volume indicators update as soon as their state changes, without waiting for a full panel rebuild.',
  'nov.v1_27_1.cameraShapeT': 'Camera keeps its shape',
  'nov.v1_27_1.cameraShapeD': 'An activity on the opposite side of the notch no longer stretches the camera capsule; its shape depends only on content on its own side.',
  'nov.v1_27_1.carpetasT': 'Folders close with the notch',
  'nov.v1_27_1.carpetasD': 'When the notch collapses, open folders and option sheets close too. Opening it again shows a clean Home.',
  'nov.v1_27_1.nubesT': 'Cloud columns at the carousel edges',
  'nov.v1_27_1.nubesD': '3D clouds slowly billow and thin in place only at 1×1. Their center stays fixed; they fade out at larger sizes and disappear in high-contrast and outline themes.',
  'nov.v1_27_1.carpetas3x3T': 'Dark glass folders',
  'nov.v1_27_1.carpetas3x3D': 'Neutral graphite smoked glass blends into the dark page without a blue cast, with a soft top reflection. Icons fill by row, and folders keep the space of a 3×3 grid even when cells are empty.',
  'nov.v1_27_0.indice': '1.27.0',
  'nov.v1_27_0.fecha': 'October 24, 2026',
  'nov.v1_27_0.resumen': 'The Widgets gallery becomes a glass 3D carousel: the selected widget stays in the center and its neighbors appear as separate previews, never behind the center card.',
  'nov.v1_27_0.galeriaT': '3D widget carousel',
  'nov.v1_27_0.galeriaD': 'Side previews stay separate from the center card and remain non-interactive. When you navigate, the current widget recedes and the next one moves to the center in a short 3D transition, without a sudden zoom or size change. Notes keeps its yellow add/remove button, and high-contrast themes keep flat surfaces with clear borders.',
  'nov.v1_26_0.indice': '1.26.0',
  'nov.v1_26_0.fecha': 'October 20, 2026',
  'nov.v1_26_0.resumen': 'The Team widget arrives: Claude and Codex work together from the notch, including scheduled tasks; Notes saves, task execution and accessibility are also hardened.',
  'nov.v1_26_0.repartoT': 'Automatic split',
  'nov.v1_26_0.repartoD': 'A router picks who plans, implements and reviews each task and which real model from your Codex each step uses. Seven splits (Auto, Saving, Quality, Design, Speed, Claude only, Codex only), an editable plan before implementing and a token cap.',
  'nov.v1_26_0.ramaT': 'Own branch and cross review',
  'nov.v1_26_0.ramaD': 'Each task works in its own git worktree and branch and never merges on its own. The other model reviews the diff (up to three rounds); you read the changes in the notch, ask for changes on the same branch, merge or discard.',
  'nov.v1_26_0.cajaT': 'Task box with images and effort',
  'nov.v1_26_0.cajaD': 'A glass bar to write the task: attach up to thirty images (+ button, drag or paste a screenshot), pick the split and set the agents\' effort, from Auto to Max, with a tachometer. Enter launches and Shift+Enter adds a line.',
  'nov.v1_26_0.preguntaT': 'Simple questions, no project',
  'nov.v1_26_0.preguntaD': 'A question such as «what does otorhinolaryngologist mean?» is detected automatically and answered by one fast agent, with web search, with no project, branch or merge. The full answer stays in the history, and a Windows notification and the notch\'s live activity tell you when it is ready.',
  'nov.v1_26_0.registroT': 'See what the team is doing',
  'nov.v1_26_0.registroD': "A live log tells in short phrases what the agent is doing right now («Reading App.xaml», «Searching…», «Thinking…») and how long since it last moved, and the pipeline comes alive: threads that light up, sparks on the working one and an arc of light around the agent. It also shows in AI Agents.",
  'nov.v1_26_0.planT': 'Formatted plans',
  'nov.v1_26_0.planD': "The plan waiting for your approval shows with headings and lists, with an «Edit» button to tweak the text; it is no longer requested in Claude Code's plan mode, and it comes complete and in sections. The board stops repeating the task while it waits.",
  'nov.v1_26_0.preguntaProyT': 'Ask about your project',
  'nov.v1_26_0.preguntaProyD': "Ask the team «what does this program do?» and it reads the project folder (read-only) to answer. Questions that use the word «code» (like «palettes with hex codes») are no longer treated as tasks.",
  'nov.v1_26_0.cancelarT': 'Cancel with clarity',
  'nov.v1_26_0.cancelarD': "A cancelled task shows as «Cancelled», tasks waiting in the queue can be cancelled from their card, and it says «Cancelling…» while the agent stops. If Claude or Codex is missing, or time runs out, you see the reason instead of retries.",
  'nov.v1_26_0.repartosT': 'Presets explained',
  'nov.v1_26_0.repartosD': "The preset list says what each one does, there is a simple example question to start with, and the board cards show each step with its full model name.",
  'nov.v1_26_0.seguirT': 'Follow up a closed answer',
  'nov.v1_26_0.seguirD': "Open an already answered question from the history and ask another one, even a short one like «and in English?»: the same model answers remembering what came before and the new answer links to the old one.",
  'nov.v1_26_0.reanudarT': 'Resume your Claude Code and Codex sessions',
  'nov.v1_26_0.reanudarD': "In the AI Agents widget, a finished or inactive session has a box to continue it: it resumes with its full context, read-only, and the new message shows up in the widget by itself.",
  'nov.v1_26_0.bandejaT': 'Icon always in view',
  'nov.v1_26_0.bandejaD': "On Windows 11 the app's icon now goes to the taskbar by default instead of hiding in the ^ menu, and you can turn that on or off from the icon's own right-click menu.",
  'nov.v1_26_0.acercaT': 'A new About window',
  'nov.v1_26_0.acercaD': "A new window with text that says what the app is today (notch with widgets, Claude + Codex team, AI agents, Ollama and RAM), six feature tiles and the credits.",
  'nov.v1_26_0.agentesT': 'AI Agents, redesigned',
  'nov.v1_26_0.agentesD': "Each session is a card with its agent's badge, its status in color, the project and what it is doing; choosing one opens a panel with its summary, changed lines, last message with formatting and a timeline of recent activity.",
  'nov.v1_26_0.respuestasT': 'Formatted answers',
  'nov.v1_26_0.respuestasD': "Team answers are laid out (headings, bold, lists and code) instead of showing asterisks, with a button to copy them; and the model is asked to start with a one-sentence answer.",
  'nov.v1_26_0.carpetasT': 'Widgets in folders',
  'nov.v1_26_0.carpetasD': "Every list on the Widgets page is an iPhone-style glass folder. Each one shows its options as mini icons and how many you have on; tapping it opens it over the page, and choosing an option opens another window showing how it really looks in use (the widget in one or two cells, the pill, the live activity or the dock) and lets you change its size, its position and turn it on.",
  'nov.v1_26_0.tableroT': 'Team board',
  'nov.v1_26_0.tableroD': 'A new tab with every task grouped into «Needs your attention», «Running» and «Done», as a board or a list, each with its agent, status and numbers. Click a card to open it.',
  'nov.v1_26_0.progT': 'History and scheduled tasks',
  'nov.v1_26_0.progD': 'History keeps each task with its numbers and pipeline. Scheduled tasks repeat once, daily, on weekdays, weekly or every N hours, with an optional catch-up if the app was closed, and run with the app open or with isTargetSleeping --team-due from Windows Task Scheduler.',
  'nov.v1_26_0.aspectoT': 'Three looks and notch shortcuts',
  'nov.v1_26_0.aspectoD': 'Pipeline, Timeline or Crew, chosen in Settings, with one-cell and two-cell versions. The closed notch can show progress as a pill item, as a live activity or with a favorite to cancel the task.',
  'nov.v1_26_0.capturaT': 'The Snipping Tool no longer freezes the screen',
  'nov.v1_26_0.capturaD': "If the Snipping Tool hangs and leaves the screen frozen (its window stops responding, or its full-screen layer stays up for more than 30 seconds), the app closes it by itself and notes it in the log. It leaves Game Bar alone.",
  'nov.v1_26_0.cuelgueT': 'No more screen freezes from RAM cleaning',
  'nov.v1_26_0.cuelgueD': 'Automatic cleaning no longer empties the memory of Windows desktop and screenshot processes, the notch keyboard hook runs on its own thread and the Music widget no longer floods the log with errors.',
  'nov.v1_26_0.datosT': 'Safer Notes and scheduled tasks',
  'nov.v1_26_0.datosD': 'Notes preserves the original if its file is corrupt or cannot be backed up, writes atomically and lets you undo deletion. Team tasks record exceptions, reserve runs across app instances and clean up attachments when they finish.',
  'nov.v1_26_0.widgetsT': 'Fixes in Code, Weather and LED',
  'nov.v1_26_0.widgetsD': 'The Code tree checks that paths stay inside the project; Weather uses local time and keeps the next forecast hour; LED alarms round their timers and consume one-shot alerts only once. Regression tests cover the Home cell limit.',
  'nov.v1_26_0.accesibilidadT': 'More reliable keyboard and accessibility',
  'nov.v1_26_0.accesibilidadD': 'Value controls expose their range to UI Automation; folders and sheets contain keyboard focus and return it when closed; LED strips pause animation during simulated high contrast.',
  'hero.logoDev': 'The new icon is in development; the current download still uses the previous one.',
  'nov.estante.titulo': 'Shelf: a tray for your files',
  'nov.estante.detalle': 'Drop files or folders on the Shelf widget to keep them at hand: open one with a click, drag them out when you need them or remove them («Select» marks several). It only keeps their paths, never moves or copies anything, and files that disappear drop out by themselves. When you drop, a wave runs across the widget, and a card you remove flies to the Clear all button.',
  'nov.tareas.titulo': 'Tasks: a list with a calendar',
  'nov.tareas.detalle': 'The Tasks widget counts what you have done today, adds and edits tasks with a title and time, repeats them every day if you want and has a month calendar: pick a day to see its tasks or prepare those of future dates. Click the card to widen it.',
  'nov.botonCompleto.titulo': 'The full notch opens with a button',
  'nov.botonCompleto.detalle': 'Pressing the buttons of a widget in the hover view no longer opens the big notch. To open it, the mini dock has a new button, where Settings used to be.',
  'nov.vistaCorta.titulo': 'A more flexible hover view',
  'nov.vistaCorta.detalle': 'The arrows wrap around (from the last widget to the first), Ollama can go in the hover view and changing widget slides more smoothly.',
  'nov.letra.titulo': 'Lyrics respect your setting',
  'nov.letra.detalle': 'With the lyrics view off in Music, the closed notch no longer shows lyrics, and the app no longer reopens inside the music widget with lyrics on after a restart.',
  'nov.logo.indice': 'Next version',
  'nov.logo.fecha': 'In development',
  'nov.agentes.titulo': 'Approve Claude Code permissions from the notch',
  'nov.agentes.detalle': 'Optional, in Settings › General. When Claude Code asks for permission, the notch opens with a card showing the project and the exact command, with Allow, Always and Deny (or Y, N and Esc to leave it to the terminal). If the app is not running, Claude Code asks in its terminal as usual. The hooks are added to your settings.json with a backup and removed when you turn it off.',
  'nov.chat.titulo': 'Talk to your pet',
  'nov.chat.detalle': 'Optional, in Settings › General: a Chat page in the notch dock where the local model you already have loaded in Ollama answers. It loads none on its own, nothing leaves your PC and the chat only lives in memory. Each pet talks in its own way: Mira dry and watchful, the llama proud, the capybara calm and the orange cat playful.',
  'nov.sesiones.titulo': 'Sessions with more detail',
  'nov.sesiones.detalle': 'Each session on the Agents page shows its git branch and how much context it has used, and the page gathers permissions, questions, alerts and the summary of what happened while you were away into a single inbox.',
  'nov.disyuntor.titulo': 'Routines that keep failing switch themselves off',
  'nov.disyuntor.detalle': 'A scheduled Team task that fails ten times in a row is paused so it does not burn tokens for nothing, and the Agents page tells you. You also choose how long the notch waits for your answer to a permission.',
  'nov.nivel.titulo': 'You decide how much they alert you',
  'nov.nivel.detalle': 'In Settings you choose whether your agents alert you about everything, only what matters or nothing, and you can snooze alerts for 30 minutes, an hour or until tomorrow. Requests always wait on the Agents page.',
  'nov.retomar.titulo': 'Resume a session in the terminal',
  'nov.retomar.detalle': 'Every finished Claude Code or Codex session has a button on the Agents page that opens Windows Terminal in its folder and resumes it with claude --resume or codex resume. It is only offered when nobody is using it.',
  'nov.away.titulo': 'While you were away',
  'nov.away.detalle': 'When you come back after a good while away, your pet tells you what your agents did: how many finished, how many permissions were requested and what is still waiting for you. If nothing happened, it says nothing.',
  'nov.luz.titulo': 'Notch light for your agents',
  'nov.luz.detalle': 'A beam of light runs round the edge of the notch: it enters at the right ear, goes down the side and along the base and leaves at the left, with a sharp tip and a tail that fades out; it never crosses the top. Blue while an agent works, amber if something waits for your answer, red if there is an alert and green when one finishes. If another state arrives, it waits for the current lap to end. You can turn it off in Settings and it stays still with reduced motion.',
  'nov.avisos.titulo': 'Alerts from your agents',
  'nov.avisos.detalle': 'The Agents page warns you if an agent has gone quiet for a while, keeps repeating the same thing or a usage limit is running out, with a countdown to when it comes back. If you play full screen, a new permission waits in the queue without pulling you out of the game.',
  'nov.preguntas.titulo': 'Answer Claude Code from the notch',
  'nov.preguntas.detalle': 'When Claude Code asks you a question, the notch shows its options as buttons and sends your answer back; when it proposes a plan, you can approve it or ask for changes without opening the terminal. You can always leave it to the terminal.',
  'nov.centro.titulo': 'Agent center',
  'nov.centro.detalle': 'An Agents page in the notch dock gathers what is waiting for your answer (with Allow, Always and Deny right there), a local history of what each agent asked and what you answered, and all your Always rules so you can revoke them whenever you like.',
  'nov.guardia.titulo': 'Sensitive-command guard',
  'nov.guardia.detalle': 'Recursive deletes, git push --force, sudo, disk formatting or touching credentials are marked in red and ask for a deliberate click: they cannot be allowed with the Y key, cannot be saved as Always and no rule lets them through on its own.',
  'nov.voz.titulo': 'Talk with your voice',
  'nov.voz.detalle': 'A microphone button on the Chat page listens for one phrase each time you press it, with the Windows speech recognizer, and you can have the pet read its answers aloud. Everything is local: no audio leaves your PC and there is never always-on listening.',
  'nov.semana.titulo': 'Your week and ambient chatter',
  'nov.semana.detalle': 'The Chat page sums up your week with your pet. And if you turn on ambient chatter in Settings › General, now and then it adds a short, kind line, with a bell in the notch; it stays quiet in game mode and does not use the model.',
  'nov.vinculo.titulo': 'Bond and mood',
  'nov.vinculo.detalle': 'Your pet has a five-level bond that grows with healthy use (chatting, an agent finishing, finishing a Pomodoro), does not drop when you are away, is stored on your PC and can be reset. Its mood changes with the context: it worries when memory is critical, gets alert when an agent waits for your permission and gets sleepy at night.',
  'nov.agentes.sonidoT': 'Agent chimes',
  'nov.agentes.sonidoD': 'A short, soft sound when an agent asks for permission or finishes, which you can turn off, and the taskbar pet gets alert or jumps for joy.',
  'nov.logo.titulo': 'A new face for isTargetSleeping',
  'nov.logo.detalle': 'The sleeping slime is the new app icon and appears in the installer, notifications and tray. The tray keeps its status and optional RAM percentage.',
  'nov.notificaciones.titulo': 'Refreshed notifications',
  'nov.notificaciones.detalle': 'Windows notifications use the high-resolution logo. Open Activity when a model falls asleep; while a model downloads, the notification shows progress for detected layers.',
  'nov.transicion.titulo': 'Smoother notch controls',
  'nov.transicion.detalle': 'The widget mini dock slides and fades in and out as its controls appear or hide.',
  'nov.proximaLink': '1.25.2 · Archive',
  'nov.v1_25_1.indice': '1.25.1 · Archive',
  'nov.publicadaLink': '1.25.0 · Archive',
  'nov.s.proxima': 'In development',
  'nov.proxima.audioT': '1.21.0 · Audio and HUDs',
  'nov.proxima.audioD': 'Per-app Windows mixer control with remembered levels, optional brightness, lock-key and audio-connection HUDs, glass intensity and per-app hiding.',
  'nov.proxima.widgetsT': '1.22.0 · Editor and system',
  'nov.proxima.widgetsD': 'Add, reorder and resize Home widgets; charts show two chosen metrics or all six in the wide view.',
  'nov.proxima.agentesT': '1.23.0 · Local agents',
  'nov.proxima.agentesD': 'Lists active Claude Code and Codex sessions with names, current tools, completed-work summaries and short message excerpts; scheduled sessions show a readable result. Logs are processed locally.',
  'nov.proxima.herramientasT': '1.24.0 · Calendar and tools',
  'nov.proxima.herramientasD': 'Event progress and a join button; optional weather using your chosen city; local OCR to the clipboard and a teleprompter with local text.',
  'nov.proxima.agentesPlusT': '1.25.0 · Interactive AI Agents',
  'nov.proxima.agentesPlusD': 'Fixed tabs separate active and recent sessions. Search and filter 12 hours of local history, see counts based on confirmed calls and paths, keep attention states visible, and expand tools, files and short messages on request.',
  'nov.proxima.usoT': 'Multi-provider AI usage',
  'nov.proxima.usoD': 'Keeps Claude, Codex and Gemini as the primary example, while adding Muse quotas, local Ollama activity and Grok API team spend when configured. Metrics without a quota are never shown as invented percentages.',
  'nov.proxima.procesosT': 'Processes in their own tab',
  'nov.proxima.procesosD': 'Open the process explorer from a dedicated tab in the app window. The Activity tab remains available in the notch and tray panel.',
  'nov.v1_25_0.fecha': 'Oct 19, 2026',
  'nov.v1_25_0.resumen': 'Version 1.25.0 brings per-app audio controls, customizable widgets, local tools and a more complete agents widget for Windows.',
  'nov.v1_25_0.cambiosT': 'Clearer agent sessions',
  'nov.v1_25_0.cambiosD': 'The Active and Recent tabs stay visible. Search and filter history, review summaries based on confirmed activity, and open details when needed. Logs are read locally; prompts and full command output stay hidden.',
  'nov.v1_25_0.resumenesT': 'Reliable states and summaries',
  'nov.v1_25_0.resumenesD': 'Inactive sessions and attention states show elapsed time. Counts include only tool calls and explicit paths found in the logs.',
  'nov.v1_25_0.calendarioFixT': 'A cleaner calendar',
  'nov.v1_25_0.calendarioFixD': 'The current-day marker is circular, and the next-event bar disappears when there are no events to show.',
  'nov.v1_25_0.notchFixT': 'More consistent indicators and widgets',
  'nov.v1_25_0.notchFixD': 'Fixed repeated brightness HUD updates, the RAM-freeing activity, widget-title alignment, and several session selection, accessibility and presentation details.',
  'nov.v1_25_1.fecha': 'Oct 19, 2026',
  'nov.v1_25_1.resumen': 'Updater patch to recognize the official ZIP package while keeping strict package validation.',
  'nov.v1_25_1.actualizadorT': 'Updater now accepts the official package',
  'nov.v1_25_1.actualizadorD': 'The ZIP contains the executable, license and third-party notices. The updater accepts those exact three files, checks SHA-256 and continues to reject unexpected entries.',
  'nov.v1_25_2.resumen': 'Visual refinement for the AI Agents widget filters: controls and dropdown lists now match the glass theme.',
  'nov.v1_25_2.filtrosT': 'Filters match the theme',
  'nov.v1_25_2.filtrosD': 'Agent, project and state selectors have readable dark surfaces, visible keyboard focus and menus bounded to their controls, without the white Windows panel or overflow over the card.',
  'nov.v1_25_2.fecha': 'Oct 19, 2026',

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
  'notch.alt': 'The closed notch pill showing the current time',
  'widg.eyebrow': 'Widgets',
  'widg.titulo': 'Your Home, with what you choose.',
  'widg.texto': 'Four cells that you order your way on the Widgets page. Each widget is on only if you want it.',
  'widg.l1': 'Ollama: turn it on or off, memory and GPU, always in view.',
  'widg.l2': "System (CPU, RAM, VRAM, disk and network), Clock and calendar with an agenda of reminders and an optional .ics file, Battery and Music with cover art, controls, shuffle and repeat.",
  'widg.l3': 'Pomodoro with a notice, Keep awake and Notes that save themselves, on your PC.',
  'widg.l4': 'Widgets sit straight on the glass, with no cards, in two columns that fill with no gaps. Each widget changes its view depending on where it is and who keeps it company. The closed pill, the hover view and the favorites are yours to choose too.',
  'widg.alt': 'The notch Home with Weather, Clock and calendar, Music and current CPU, RAM, GPU and disk metrics',
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
  castillo: 'isTargetSleeping · A quieter place for Ollama',
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
    const t = localStorage.getItem('its-theme2');
    return t === 'light' || t === 'dark' || t === 'system' ? t : null;
  } catch (e) { return null; }
}

let temaTemporal = null;

function temaElegido() {
  return temaTemporal || temaGuardado() || 'dark';   // como Nusa, oscuro por defecto; «Sistema» sigue a Windows
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
  try { localStorage.setItem('its-theme2', nuevo); } catch (e) { /* sin almacenamiento: dura hasta recargar */ }
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

aplicarIdioma(leerPreferencia());

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
