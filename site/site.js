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
  'tema.aOscuro': 'Switch to dark mode',
  'tema.aClaro': 'Switch to light mode',

  'hero.eyebrow': 'New · Version 1.5.1, the icon always at hand',
  'hero.titulo': 'Your models work when you talk to them. The rest of the time, they sleep.',
  'hero.sub': 'isTargetSleeping lives next to the Windows clock. It turns Ollama on and off in one click, puts the model to sleep when it stops generating and gives you your RAM and VRAM back.',
  'hero.meta': 'Free and open source · Version 1.5.1 · Windows 10 and 11 · x64 and ARM64',
  'hero.escritorio': 'The isTargetSleeping panel open on the Windows desktop',
  'hero.panelAlt': 'Panel: Ollama on, PC and GPU memory, llama.cpp and the installed models',
  'cta.windows': 'Download for Windows',
  'cta.como': 'See how it works',

  'pill.nativa': 'Native: .NET 10 and Win32',
  'pill.cuentas': 'No accounts',
  'pill.idiomas': 'English and Spanish',
  'pill.mit': 'Open source, MIT',

  'video.eyebrow': 'Version 1.5.1, in 77 seconds',
  'video.titulo': 'Take a closer look.',
  'video.pie': 'With sound and sample data from the current app.',
  'video.otro': 'Watch in Spanish',

  'fun.eyebrow': 'Features',
  'fun.titulo': 'Everything Ollama doesn’t ship with.',
  'fun.sub': 'Turn it on, turn it off, keep an eye on it and make room for everything else. No terminal needed.',

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
  'mem.texto': 'How much is the model and how much is everything else. The same figure as Task Manager.',
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
  'proc.texto': 'Activity shows the five biggest users. Open the full window, sort by RAM or CPU, filter and expand each PID with its details.',
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
  'vidrio.texto': 'Real acrylic, translucent cards and a monochrome UI where color only means status. The tray icon follows your taskbar mode.',

  'idioma.eyebrow': 'Language',
  'idioma.titulo': 'It speaks your language.',
  'idioma.texto': 'Follows the Windows language, or pick one in Settings.',

  'como.eyebrow': 'How it works',
  'como.titulo': 'It stops Ollama the way it started.',
  'como.sub': 'Killing the wrong process doesn’t work: the Ollama app, a service or a scheduled task just launches it again. isTargetSleeping detects which one is yours and uses that same path.',
  'como.probado': 'Tested',
  'como.sinProbar': 'Not tested on real hardware',
  'como.apagar': 'Stop',
  'como.encender': 'Start',
  'como.app': 'Ollama app',
  'como.appOff': 'Closes its windows and its process tree',
  'como.servicio': 'Windows service',
  'como.scmStop': 'Stop through the SCM',
  'como.scmStart': 'Start through the SCM (with UAC)',
  'como.tarea': 'Scheduled task',
  'como.tareaStop': 'Stop in Task Scheduler',
  'como.tareaRun': 'Run in Task Scheduler',
  'como.manual': 'Manual <code>ollama serve</code>',
  'como.manualOff': 'Ends the server’s process tree',
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

  'priv.eyebrow': 'Privacy',
  'priv.titulo': 'It talks to Ollama.<br>On your PC. That’s it.',
  'priv.sub': 'It connects to Ollama’s local API at <code>127.0.0.1:11434</code> (or <code>OLLAMA_HOST</code>, if you set it) and to the other engines’ local ports. Web pages only open when you click.',
  'priv.c1': 'No accounts',
  'priv.c1t': 'No sign-up, no email, no password. Download it and use it.',
  'priv.c2': 'No telemetry',
  'priv.c2t': 'No analytics either. Your 90-day history stays in <code>%LOCALAPPDATA%</code>.',
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
  'inst.meta': '~65 MB · Windows 10 (2004+) or 11 · requires Ollama · downloads from GitHub Releases',
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
  'cierre.meta': 'Version 1.5.1 · Windows 10 and 11 · x64 and ARM64',

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
  'pie.version': 'Version 1.5.1',
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
  'nov.s.proxima': "Next version",
  'nov.web.indice': "Website",
  'nov.web.titulo': "Website updated",
  'nov.web.fecha': "Oct 3, 2026",
  'nov.web.tipo': "Website",
  'nov.web.resumen': "The app stays at 1.5.1. We refreshed its presentation so the images and video show the current version.",
  'nov.web.mediosT': "Current screenshots and tour",
  'nov.web.mediosD': "Panel, Activity and Settings from 1.5.1, plus a new tour in both languages with memory now, cleanup rules and the current pets.",
  'nov.web.cacheT': "Each image has its own version",
  'nov.web.cacheD': "When the website is updated, the browser receives the new images and videos even if it saved the previous ones.",
  'nov.web.guiasT': "Contributing and security",
  'nov.web.guiasD': "The footers link to the repository’s bilingual guides, and vulnerability reports are private.",
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
  'nov.v1_5_0.cambios2t': "One-click download",
  'nov.v1_5_0.cambios2d': "Every “Download” button on the site gets the installer right away, without opening GitHub — the ARM64 one if your PC is ARM.",
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
  'nov.v1_4_0.resumen': "Process explorer, confirmed termination and selective automatic cleanup. The website and app tour accompany this version.",
  'nov.v1_4_0.nuevo1t': "Official website",
  'nov.v1_4_0.nuevo1d': "This site, live at istargetsleeping.web.app: features, how it works, install, FAQ, this what’s-new page and light or dark mode.",
  'nov.v1_4_0.nuevo2t': "Video tour",
  'nov.v1_4_0.nuevo2d': "77 seconds through the app, in English and Spanish, with an original soundtrack.",
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
  inicio: 'isTargetSleeping · Put Ollama to sleep and get your PC’s RAM back',
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

// ---------- Tema claro / oscuro ----------
// Sin preferencia guardada sigue a Windows; el <head> ya puso data-theme antes del CSS para que no parpadee.

const oscuroDelSistema = matchMedia('(prefers-color-scheme: dark)');

function temaGuardado() {
  try {
    const t = localStorage.getItem('its-theme');
    return t === 'light' || t === 'dark' ? t : null;
  } catch (e) { return null; }
}

function temaActual() {
  return temaGuardado() || (oscuroDelSistema.matches ? 'dark' : 'light');
}

function pintarBotonTema() {
  const oscuro = temaActual() === 'dark';
  const b = $('tema');
  const es = idioma === 'es';
  b.setAttribute('aria-label', oscuro
    ? (es ? 'Cambiar a modo claro' : EN['tema.aClaro'])
    : (es ? 'Cambiar a modo oscuro' : EN['tema.aOscuro']));
  b.setAttribute('aria-pressed', oscuro ? 'true' : 'false');
  document.querySelector('meta[name="theme-color"]')?.setAttribute('content', oscuro ? '#0A0A0C' : '#FFFFFF');
}

$('tema').addEventListener('click', () => {
  const nuevo = temaActual() === 'dark' ? 'light' : 'dark';
  document.documentElement.dataset.theme = nuevo;
  try { localStorage.setItem('its-theme', nuevo); } catch (e) { /* sin almacenamiento: dura hasta recargar */ }
  pintarBotonTema();
});

oscuroDelSistema.addEventListener('change', () => {
  if (!temaGuardado()) { delete document.documentElement.dataset.theme; pintarBotonTema(); }
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
