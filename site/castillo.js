// Avance de las tres escenas fijadas; en móvil y con movimiento reducido, el relato queda en flujo normal.
(() => {
  const menuButton = document.querySelector('#castillo-menu');
  const menu = document.querySelector('#castillo-links');
  if (menuButton && menu) {
    // Rebote como el del notch de la app (BackEase, amplitud .5): el menú crece pasándose un poco y se asienta; al cerrar, se recoge y los enlaces se esconden en cascada.
    const REBOTE = 'cubic-bezier(.34, 1.5, .5, 1)', RECOGE = 'cubic-bezier(.5, 0, .75, 0)';
    const quieto = matchMedia('(prefers-reduced-motion: reduce)');
    const cerradoH = (menu.closest('.contenedor') || menu).offsetHeight;   // la barra con el menú cerrado (medida antes de abrirlo nunca)
    let anim = [];
    const para = () => { anim.forEach((x) => x.cancel()); anim = []; };
    const items = () => [...menu.children];
    const abre = () => {
      para();
      menu.classList.add('abierto'); menuButton.setAttribute('aria-expanded', 'true');
      if (quieto.matches) return;
      const h = menu.scrollHeight;
      anim.push(menu.animate([{ height: '0px', opacity: 0, paddingTop: '0px', marginTop: '-12px' }, { height: h + 'px', opacity: 1, marginTop: '0px' }], { duration: 520, easing: REBOTE }));
      items().forEach((el, i) => anim.push(el.animate([{ opacity: 0, transform: 'translateY(-10px) scale(.85)' }, { opacity: 1, transform: 'none' }], { duration: 480, delay: 70 + i * 45, easing: REBOTE, fill: 'backwards' })));
      menuButton.animate([{ transform: 'rotate(-90deg) scale(.8)' }, { transform: 'none' }], { duration: 420, easing: REBOTE });
    };
    const close = () => {
      if (menuButton.getAttribute('aria-expanded') !== 'true') return;
      menuButton.setAttribute('aria-expanded', 'false');
      if (quieto.matches) { menu.classList.remove('abierto'); return; }
      para();
      const h = menu.offsetHeight, hs = items();
      // los enlaces se esconden de abajo arriba y la barra se recoge de una sola vez, con una curva suave que frena al final (sin rebote final, que se veía brusco)
      hs.forEach((el, i) => anim.push(el.animate([{ opacity: 1, transform: 'none' }, { opacity: 0, transform: 'translateY(-8px) scale(.92)' }], { duration: 240, delay: (hs.length - 1 - i) * 28, easing: 'cubic-bezier(.4, 0, .6, 1)', fill: 'forwards' })));
      const cuerpo = menu.animate([{ height: h + 'px', opacity: 1, marginTop: '0px' }, { height: '0px', opacity: 0, paddingTop: '0px', borderTopWidth: '0px', marginTop: '-12px' }], { duration: 460, delay: 70, easing: 'cubic-bezier(.32, .72, 0, 1)', fill: 'forwards' });
      anim.push(cuerpo);
      // la barra anima su propia altura hasta la altura exacta de la barra cerrada: así, al ocultar el menú, no queda ningún salto residual
      const barra = menu.closest('.contenedor'), hBarra = barra.offsetHeight;
      anim.push(barra.animate([{ height: hBarra + 'px' }, { height: cerradoH + 'px' }], { duration: 460, delay: 70, easing: 'cubic-bezier(.32, .72, 0, 1)', fill: 'forwards' }));
      cuerpo.onfinish = () => {
        if (menuButton.getAttribute('aria-expanded') !== 'false') return;
        menu.style.display = 'none';   // se oculta ya, antes de soltar las animaciones, para que no asome ni un fotograma del menú abierto
        menu.classList.remove('abierto'); para();
        requestAnimationFrame(() => { menu.style.display = ''; });
      };
    };
    menuButton.addEventListener('click', () => (menuButton.getAttribute('aria-expanded') === 'true' ? close() : abre()));
    menu.addEventListener('click', (event) => { if (event.target.closest('a')) close(); });
    addEventListener('keydown', (event) => {
      if (event.key === 'Escape' && menuButton.getAttribute('aria-expanded') === 'true') { close(); menuButton.focus(); }
    });
  }

  const nav = document.querySelector('.nav');
  if (nav) {
    const menuButton = nav.querySelector('#castillo-menu');
    const compactable = [...nav.querySelector('.contenedor').children].filter((el) => !el.classList.contains('nav-mascota'));
    const reduceMotion = matchMedia('(prefers-reduced-motion: reduce)');
    const idleMs = 25000;
    const routineStates = new Set(['mirando-izquierda', 'mirando-derecha', 'durmiendo', 'despertando', 'despierta']);
    const randomBetween = (min, max) => Math.round(min + Math.random() * (max - min));
    let lastActivity = Date.now(), idleTimer = null, phaseTimer = null, greetingTimer = null, greetingUntil = 0, phaseUntil = 0, routine = [], routineIndex = 0, state = '';
    const allowed = () => nav.classList.contains('nav--claro') && !reduceMotion.matches && !(innerWidth <= 900 && menuButton?.getAttribute('aria-expanded') === 'true');
    const setInert = (inert) => compactable.forEach((el) => { el.inert = inert; });
    const cancelRoutine = () => {
      clearTimeout(phaseTimer);
      phaseTimer = null;
      phaseUntil = 0;
      routine = [];
      routineIndex = 0;
      nav.style.removeProperty('--nav-blink-duration');
      nav.style.removeProperty('--nav-blink-delay');
    };
    const restore = () => {
      cancelRoutine();
      clearTimeout(greetingTimer);
      greetingTimer = null;
      greetingUntil = 0;
      state = '';
      nav.removeAttribute('data-mascota');
      setInert(false);
    };
    const finishGreeting = () => { restore(); scheduleIdle(); };
    const showNextPhase = (startedAt = Date.now()) => {
      if (routineIndex >= routine.length) {
        const first = Math.random() < .5 ? 'izquierda' : 'derecha';
        routine = [
          [`mirando-${first}`, randomBetween(1100, 1550)],
          [`mirando-${first === 'izquierda' ? 'derecha' : 'izquierda'}`, randomBetween(850, 1250)],
          ['despierta', randomBetween(650, 1100)],
          ['durmiendo', randomBetween(4800, 7600)],
          ['despertando', randomBetween(380, 580)],
          ['despierta', randomBetween(4200, 7000)]
        ];
        routineIndex = 0;
        nav.style.setProperty('--nav-blink-duration', `${randomBetween(4200, 6800)}ms`);
        nav.style.setProperty('--nav-blink-delay', `${randomBetween(350, 1600)}ms`);
      }
      const [next, duration] = routine[routineIndex++];
      state = next;
      nav.dataset.mascota = state;
      phaseUntil = startedAt + duration;
    };
    const runRoutine = () => {
      clearTimeout(phaseTimer);
      phaseTimer = null;
      if (!routineStates.has(state)) return;
      if (!allowed()) { restore(); return; }
      const now = Date.now();
      let skipped = 0;
      // shortcut: cap background catch-up at 48 phases, restart at now after longer tab suspensions.
      while (phaseUntil <= now && skipped < 48) { showNextPhase(phaseUntil); skipped++; }
      if (phaseUntil <= now) { routine = []; routineIndex = 0; showNextPhase(now); }
      phaseTimer = setTimeout(runRoutine, Math.max(0, phaseUntil - Date.now()));
    };
    const startRoutine = () => {
      if (!allowed()) return;
      cancelRoutine();
      setInert(true);
      if (nav.contains(document.activeElement)) document.activeElement.blur();
      showNextPhase();
      runRoutine();
    };
    const checkIdle = () => {
      idleTimer = null;
      if (!allowed()) return;
      const remaining = idleMs - (Date.now() - lastActivity);
      if (remaining > 0) {
        idleTimer = setTimeout(checkIdle, remaining);
        return;
      }
      startRoutine();
    };
    const scheduleIdle = () => {
      if (!allowed() || state || idleTimer !== null) return;
      idleTimer = setTimeout(checkIdle, Math.max(0, idleMs - (Date.now() - lastActivity)));
    };
    const greet = () => {
      cancelRoutine();
      state = 'saludo';
      greetingUntil = Date.now() + 1450;
      nav.dataset.mascota = state;
      greetingTimer = setTimeout(finishGreeting, greetingUntil - Date.now());
    };
    const activity = () => {
      lastActivity = Date.now();
      if (!allowed()) {
        clearTimeout(idleTimer);
        idleTimer = null;
        if (state) restore();
      } else if (state && state !== 'saludo') greet();
      else if (!state) scheduleIdle();
    };
    const sync = (onReturn = false) => {
      if (!allowed()) {
        clearTimeout(idleTimer);
        idleTimer = null;
        if (state) restore();
      } else if (state === 'saludo') {
        const remaining = greetingUntil - Date.now();
        if (remaining <= 0) finishGreeting();
        else if (onReturn || greetingTimer === null) {
          clearTimeout(greetingTimer);
          greetingTimer = setTimeout(finishGreeting, remaining);
        }
      } else if (routineStates.has(state)) {
        runRoutine();
      } else if (onReturn && !state) {
        clearTimeout(idleTimer);
        idleTimer = null;
        checkIdle();
      } else scheduleIdle();
    };
    new MutationObserver(() => sync()).observe(nav, { attributes: true, attributeFilter: ['class'] });
    reduceMotion.addEventListener('change', () => sync());
    addEventListener('resize', () => sync());
    document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'visible') sync(true); });
    for (const type of ['pointermove', 'pointerdown', 'keydown', 'click', 'touchstart', 'touchmove', 'wheel', 'focusin']) document.addEventListener(type, activity, { passive: true });
    sync();
  }

  const story = document.querySelector('[data-story]');
  if (!story) return;

  const pin = story.querySelector('.castillo-fijado');
  const stages = [...story.querySelectorAll('[data-stage]')];
  const reduceMotion = matchMedia('(prefers-reduced-motion: reduce)');
  let frame = 0;

  const clamp = (value) => Math.min(1, Math.max(0, value));
  const setStep = (step) => {
    if (story.dataset.step === String(step)) return;
    story.dataset.step = String(step);
    stages.forEach((stage) => stage.setAttribute('aria-hidden', String(Number(stage.dataset.stage) !== step)));
  };
  const paint = () => {
    frame = 0;
    if (!story.classList.contains('castillo-recorrido--pinned')) return;
    const distance = Math.max(1, story.offsetHeight - pin.offsetHeight);
    const progress = clamp(-story.getBoundingClientRect().top / distance);
    story.style.setProperty('--story-progress', progress.toFixed(3));
    setStep(Math.min(2, Math.floor(progress * 3)));
  };
  const schedule = () => { if (!frame) frame = requestAnimationFrame(paint); };
  const setMode = () => {
    const pinned = innerWidth > 900 && !reduceMotion.matches;
    story.classList.toggle('castillo-recorrido--pinned', pinned);
    if (pinned) schedule();
    else {
      story.style.removeProperty('--story-progress');
      setStep(0);
      stages.forEach((stage) => stage.setAttribute('aria-hidden', 'false'));
    }
  };

  addEventListener('scroll', schedule, { passive: true });
  addEventListener('resize', setMode);
  reduceMotion.addEventListener('change', setMode);
  setMode();
})();
