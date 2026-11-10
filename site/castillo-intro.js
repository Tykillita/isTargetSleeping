// Intro del inicio: el notch aparece pintado de blanco, la pintura se derrite y gotea; las gotas se juntan en un punto del suelo, se funden y se convierten en la mascota.
// Solo corre si el <head> puso html.intro (sin movimiento reducido, en #inicio o sin hash, con el scroll arriba). Si algo falla, la mascota aparece igual.
(() => {
  const root = document.documentElement;
  if (!root.classList.contains('intro')) return;
  const mascota = document.querySelector('.mundo-mascota');
  const barra = document.querySelector('.nav .contenedor');
  const pintura = document.querySelector('.nav-pintura');
  const termina = (capa) => { capa?.remove(); root.classList.remove('intro'); };
  if (!mascota || !barra || !pintura || !Element.prototype.animate || document.hidden) { termina(); return; }

  const rnd = (a, b) => a + Math.random() * (b - a);
  const clamp = (v, a, b) => Math.min(b, Math.max(a, v));
  // Ritmo general: 1,2 veces los tiempos de abajo (un punto medio: ni brusco ni lento). ?introLento=5 los estira, solo para revisar el movimiento fotograma a fotograma.
  const k = clamp(+new URLSearchParams(location.search).get('introLento') || 1.2, 1, 5);
  // La mascota ya está medida y puesta por mundo.js cuando tiene transform y tamaño; si tarda demasiado, se sigue igual.
  const lista = () => new Promise((ok) => {
    const t0 = performance.now();
    let ant = null, quietas = 0;
    const mira = () => {
      const r = mascota.getBoundingClientRect(), puesta = mascota.style.transform && r.width > 10;
      quietas = puesta && ant && Math.hypot(r.left - ant.left, r.top - ant.top, r.width - ant.width) < 0.6 ? quietas + 1 : 0;   // quieta: la escena acabó de medirse y colocarla
      ant = { left: r.left, top: r.top, width: r.width };
      if ((quietas >= 6 && document.readyState === 'complete') || performance.now() - t0 > 1800) ok();
      else setTimeout(mira, 40);   // con temporizador y no rAF: en una pestaña en segundo plano rAF no corre
    };
    mira();
  });

  (async () => {
    await Promise.all([lista(), document.fonts?.ready]);
    if (!root.classList.contains('intro')) return;
    // Al recargar, el navegador restaura el scroll después de que el <head> comprobó que estaba arriba: si ya no estamos en la primera escena, no hay intro.
    if (scrollY >= 40 || (location.hash && location.hash !== '#inicio')) { termina(); return; }
    removeEventListener('scroll', window.__introEscucha);   // desde aquí la intro ya corre entera
    const nb = barra.getBoundingClientRect(), mr = mascota.getBoundingClientRect();
    pintura.style.setProperty('--intro-nav-height', `${nb.height}px`);
    const e = parseFloat(getComputedStyle(barra.closest('.nav')).getPropertyValue('--e')) || 24;
    const sueloY = mr.top + mr.height * 0.9245, mx = mr.left + mr.width / 2;   // los pies están al 92,45 % del alto (ver mundo.css)
    const y0 = nb.bottom - 4;
    const capa = document.createElement('div');
    capa.className = 'intro-capa';
    capa.setAttribute('aria-hidden', 'true');
    document.body.append(capa);
    const animaciones = [];
    const mueve = (el, kf, op) => { const a = el.animate(kf, { fill: 'both', ...op, duration: op.duration * k, delay: (op.delay || 0) * k }); animaciones.push(a); return a; };
    // Las gotas, la tira de pintura del borde y el núcleo viven en una capa con filtro «goo» (desenfoque + umbral de opacidad): al tocarse se funden con cuellos suaves, y al soltarse
    // se estiran y se pellizcan como pintura de verdad; el mismo grano que la pintura del notch (feTurbulence + luz) les da su textura. Los charcos y la silueta de la mascota quedan fuera, para no endurecerlos.
    capa.insertAdjacentHTML('beforeend', '<svg width="0" height="0" style="position:absolute" aria-hidden="true" focusable="false"><defs><filter id="intro-goo" x="-5%" y="-5%" width="110%" height="110%" color-interpolation-filters="sRGB"><feGaussianBlur in="SourceGraphic" stdDeviation="2.6" result="b"/><feColorMatrix in="b" values="1 0 0 0 0  0 1 0 0 0  0 0 1 0 0  0 0 0 18 -7" result="goo"/><feTurbulence type="fractalNoise" baseFrequency=".85 .3" numOctaves="3" seed="7" result="t"/><feDiffuseLighting in="t" lighting-color="#fff" surfaceScale="1.4" result="l"><feDistantLight azimuth="235" elevation="74"/></feDiffuseLighting><feBlend in="goo" in2="l" mode="multiply" result="m0"/><feComposite in="m0" in2="goo" operator="in" result="m"/><feComposite id="intro-mezcla" in="m" in2="goo" operator="arithmetic" k1="0" k2="1" k3="0" k4="0" result="pintura"/><feMorphology in="goo" operator="dilate" radius="2" result="dil"/><feComposite in="dil" in2="goo" operator="out" result="anillo"/><feFlood id="intro-tinta" flood-color="#05080e" flood-opacity="0" result="tinta"/><feComposite in="tinta" in2="anillo" operator="in" result="contorno"/><feMerge><feMergeNode in="contorno"/><feMergeNode in="pintura"/></feMerge></filter></defs></svg>');
    const goo = document.createElement('div');
    goo.className = 'intro-goo';
    capa.append(goo);
    const pon = (clase, css, dest = capa) => { const el = document.createElement('span'); el.className = clase; Object.assign(el.style, css); dest.append(el); return el; };
    let revelada = false, fin = false;
    // Una sola mascota: la de verdad. Mientras está oculta (html.intro) no se ve; al empezar la formación se le quita el ocultado y mundo.js la dibuja como masa blanca que se
    // transforma en ella (window.mundoIntro); la sombra del suelo vuelve a media formación.
    const revela = () => {
      if (revelada) return;
      revelada = true;
      root.classList.remove('intro');
      root.classList.add('intro-sale');
      setTimeout(() => root.classList.remove('intro-sale'), 700);
    };
    const acaba = () => {
      if (fin) return;
      fin = true;
      window.mundoIntro = null;
      animaciones.forEach((a) => a.cancel());
      revela();
      root.classList.remove('intro-sombra');
      capa.remove();
      removeEventListener('resize', acaba);
      removeEventListener('scroll', alScroll);
      document.removeEventListener('visibilitychange', alOcultar);
    };
    const alOcultar = () => { if (document.hidden) acaba(); };
    // Si la página se desplaza (p. ej. el navegador restaura tarde el scroll de una recarga), la intro se corta: solo corre en la primera escena.
    const alScroll = () => { if (scrollY >= 40) acaba(); };
    addEventListener('scroll', alScroll, { passive: true });
    addEventListener('resize', acaba);
    document.addEventListener('visibilitychange', alOcultar);

    // 1) La pintura se derrite: el borde de arriba desciende por columnas, cada una a su ritmo, y descubre el negro del notch.
    const N = 16, W = nb.width, H = nb.height;
    const xs = Array.from({ length: N + 1 }, (_, i) => -e - 2 + ((W + 2 * e + 4) * i) / N);
    const retraso = xs.map(() => rnd(0, 0.34));
    const forma = (t) => {
      const ys = xs.map((_, i) => { const u = clamp((t - retraso[i]) / 0.66, 0, 1); return H * 1.06 * u * u * (3 - 2 * u); });
      return `polygon(${xs.map((x, i) => `${x.toFixed(1)}px ${ys[i].toFixed(1)}px`).join(',')},${xs[N]}px ${H + 80}px,${xs[0]}px ${H + 80}px)`;
    };
    mueve(pintura, Array.from({ length: 10 }, (_, k) => ({ clipPath: forma(k / 9) })), { duration: 1100, delay: 250, easing: 'linear' });

    // 2) Las gotas cuelgan del borde de abajo (cola que se estira y bulbo que se hincha), se sueltan y caen con gravedad. La grande cae directa al punto donde está
    //    la mascota; las pequeñas caen por su sitio, se aplastan y ruedan por el suelo hasta ese mismo punto, donde se funden con ella.
    const gota = ({ x, w, L, d, cuelga, suelta, principal }) => {
      const D = sueloY - d / 2 - y0;   // el centro del bulbo baja hasta que su borde de abajo toca el suelo
      const dur = principal ? clamp(520 + D * 0.55, 760, 1150) : clamp(360 + D * 0.4, 480, 820);
      const desliz = principal ? 0 : clamp(Math.abs(mx - x) * 0.75, 290, 540);   // lo que tardan en rodar hasta el punto
      const resto = principal ? 160 : desliz + 160;
      const hang = suelta - cuelga, total = hang + dur + resto, fh = hang / total, fl = (hang + dur) / total, fs = (hang + dur + desliz) / total;
      const dx = principal ? mx - x : 0;
      const cola = pon('intro-cola', { left: `${x - w / 2}px`, top: `${y0}px`, width: `${w}px`, height: `${L}px` }, goo);
      const bulbo = pon('intro-bulbo', { left: `${x - d / 2}px`, top: `${y0 - d / 2}px`, width: `${d}px`, height: `${d}px` }, goo);
      // la pestaña: un ensanche donde la gota nace del borde, que se recoge cuando se suelta
      const pest = pon('intro-cola', { left: `${x - w * 1.5}px`, top: `${y0 - 8}px`, width: `${w * 3}px`, height: '16px', borderRadius: '50%' }, goo);
      mueve(pest, [{ transform: 'scaleX(0)', opacity: 1 }, { offset: Math.min(0.5, fh * 0.35), transform: 'scaleX(1)', opacity: 1 }, { offset: fh, transform: 'scaleX(.9)', opacity: 1 }, { offset: Math.min(0.999, fh + 220 / total), transform: 'scaleX(0)', opacity: 1 }, { offset: 1, transform: 'scaleX(0)', opacity: 0 }], { duration: total, delay: cuelga });
      mueve(cola, [
        { transform: 'scaleY(0)', opacity: 1, easing: 'cubic-bezier(.4,0,.6,1)' },
        { offset: fh, transform: 'scaleY(1)', opacity: 1, easing: 'cubic-bezier(.5,0,1,.6)' },
        { offset: Math.min(0.999, fh + 180 / total), transform: 'scaleY(0)', opacity: 1 },
        { offset: 1, transform: 'scaleY(0)', opacity: 0 }
      ], { duration: total, delay: cuelga });
      const aplasta = (principal ? 130 : 60) / total;
      const aplas = principal ? 0.62 : 0.55;   // cuánto se aplasta contra el suelo: la grande se queda como un montículo, las pequeñas más planas
      const sube = (d / 2) * (1 - aplas);   // al aplastarse, el borde de abajo se queda en el suelo
      const plano = `translate(${dx}px,${D + sube}px) scale(${principal ? 1.5 : 1.4},${aplas})`;
      const kf = [
        { transform: 'translate(0,0) scale(.45)', opacity: 0, easing: 'ease-out' },
        { offset: Math.min(0.2, fh * 0.15), transform: 'translate(0,0) scale(.55)', opacity: 1, easing: 'cubic-bezier(.4,0,.6,1)' },
        { offset: fh, transform: `translate(0,${L}px) scale(1)`, opacity: 1, easing: 'cubic-bezier(.45,0,.95,.55)' },
        { offset: fl - aplasta, transform: `translate(${dx}px,${D}px) scale(.82,1.5)`, opacity: 1 },
        { offset: fl, transform: plano, opacity: 1 }
      ];
      if (principal) kf.push({ offset: 1, transform: plano, opacity: 0 });   // el núcleo toma su lugar
      else {
        const rueda = `translate(${mx - x}px,${D + sube}px) scale(1.3,.6)`;
        const absorbida = `translate(${mx - x}px,${D + sube}px) scale(.5,.45)`;   // al llegar se hunde en el montículo
        kf.push({ offset: fl + 50 / total, transform: plano, opacity: 1, easing: 'cubic-bezier(.5,0,.5,1)' },
          { offset: fs, transform: rueda, opacity: 1 }, { offset: Math.min(0.998, fs + 110 / total), transform: absorbida, opacity: 0.9 }, { offset: Math.min(0.999, fs + 140 / total), transform: absorbida, opacity: 0 }, { offset: 1, transform: absorbida, opacity: 0 });
      }
      mueve(bulbo, kf, { duration: total, delay: cuelga });
      const aterriza = cuelga + hang + dur;
      const cw = principal ? d * 3.1 : d * 2.6, ch = Math.max(5, cw * 0.2);
      const charco = pon('intro-charco', { left: `${x + dx - cw / 2}px`, top: `${sueloY - ch * 0.62}px`, width: `${cw}px`, height: `${ch}px` });
      mueve(charco, [
        { transform: 'scale(.15,.4)', opacity: 0 },
        { offset: 0.16, transform: 'scale(.9,1)', opacity: 0.95, easing: 'ease-out' },
        { offset: 0.5, transform: 'scale(1.12,.92)', opacity: 0.55 },
        { offset: 1, transform: 'scale(1.3,.8)', opacity: 0 }
      ], { duration: principal ? 1300 : 700, delay: aterriza - 20 });
      return { aterriza, fusiona: aterriza + desliz };
    };

    let izq = nb.left + e + 18, der = nb.right - e - 18;
    if (Math.min(der, mx + 230) - Math.max(izq, mx - 230) > 140) { izq = Math.max(izq, mx - 230); der = Math.min(der, mx + 230); }   // las gotas caen cerca del punto: no cruzan toda la pantalla
    const n = 6, fusiones = [];
    for (let i = 0; i < n; i++) {
      const x = izq + ((der - izq) * (i + rnd(0.15, 0.85))) / n;
      const cuelga = 280 + i * 45 + rnd(0, 60), suelta = 640 + i * 70 + rnd(0, 90);   // todas se sueltan mientras aún queda pintura en el notch: nacen de él, no de una línea
      fusiones.push(gota({ x, w: rnd(8, 13), L: rnd(34, 92), d: rnd(15, 22), cuelga, suelta }).fusiona);
    }
    const xp = clamp(mx, izq + 30, der - 30);
    const { aterriza: tL } = gota({ x: xp, w: 24, L: 70, d: 62, cuelga: 320, suelta: 760, principal: true });

    // 3) El núcleo: la gota grande, aplastada en el suelo, se hincha un poco con cada gota que llega rodando (más ancho y más alto, con los pies fijos).
    const d0 = 62, esc = (j) => `scale(${1.4 + 0.07 * j},${0.6 + 0.11 * j})`;   // el montículo: crece (más ancho y más alto) con cada gota que se funde
    const nucleo = pon('intro-bulbo', { left: `${mx - d0 / 2}px`, top: `${sueloY - d0}px`, width: `${d0}px`, height: `${d0}px`, transformOrigin: '50% 100%', borderRadius: '46% 46% 30% 30% / 62% 62% 26% 26%', background: 'radial-gradient(ellipse at 38% 22%, #fff 0 22%, #eef3fb 55%, #c9d5ea 100%)' }, goo);   // cúpula con la base más ancha: un montículo de pintura, no un plato
    const hitos = [];
    let fin0 = tL;
    fusiones.map((t) => Math.max(t, tL + 90)).sort((a, b) => a - b).forEach((t, j) => {
      const ini = Math.max(t, fin0);
      hitos.push({ ini, fin: ini + 130, j });
      fin0 = ini + 130;
    });
    const M = hitos.length, tF = fin0 + 40;   // todo fusionado
    const tot = tF + 260 - tL, off = (t) => Math.min(1, (t - tL) / tot);
    const kfN = [{ offset: 0, transform: esc(0), opacity: 1 }];
    hitos.forEach(({ ini, fin, j }) => {
      kfN.push({ offset: off(ini), transform: esc(j), opacity: 1, easing: 'cubic-bezier(.3,1.7,.5,1)' }, { offset: off(fin), transform: esc(j + 1), opacity: 1 });
    });
    kfN.push({ offset: off(tF), transform: esc(M), opacity: 1 }, { offset: 1, transform: esc(M), opacity: 0 });
    mueve(nucleo, kfN, { duration: tot, delay: tL, fill: 'forwards' });

    // Mientras las gotas se funden, la pintura se alisa (el grano se va) y se perfila con un contorno oscuro fino, como el de la mascota: así la masa y la mascota comparten material.
    const mezcla = capa.querySelector('#intro-mezcla'), tinta = capa.querySelector('#intro-tinta');
    setTimeout(() => {
      if (fin || !mezcla || !tinta) return;
      const ini = performance.now();
      const id = setInterval(() => {
        if (fin) { clearInterval(id); return; }
        const u = clamp((performance.now() - ini) / ((tF - tL) * k), 0, 1), q = u * u * (3 - 2 * u);
        mezcla.setAttribute('k2', (1 - q).toFixed(3)); mezcla.setAttribute('k3', q.toFixed(3));
        tinta.setAttribute('flood-opacity', (0.92 * q).toFixed(3));
        if (u >= 1) clearInterval(id);
      }, 16);
    }, tL * k);

    // 4) La masa se convierte en la mascota, de verdad: una forma (dentro de la propia mascota) que pasa, punto a punto, del montículo al contorno exacto del cuerpo de la mascota.
    //    Es el mismo trazo que el cuerpo del dibujo, con la misma estructura de curvas, así que se interpola número a número; al terminar es idéntico a él. Mientras sube y se aplasta
    //    ya es la mascota; los ojos (que lleva el dibujo real) aparecen al final, cuando ya tiene su forma, y la forma cede el sitio a la mascota sin que se note.
    const MORFA = 800;
    // montículo (mismo número de curvas que el cuerpo) y cuerpo de la mascota, en las coordenadas de mascota.svg
    const CUPULA = [0, 820, 10, 640, 200, 520, 380, 505, 450, 498, 574, 498, 644, 505, 824, 520, 1014, 640, 1024, 820, 1024, 880, 1024, 1024, 880, 1024, 144, 1024, 0, 1024, 0, 880];
    const CUERPO = [0, 610, 35, 350, 262, 300, 394, 318, 590, 335, 688, 423, 842, 478, 955, 518, 1024, 578, 1024, 685, 1024, 800, 1024, 1024, 800, 1024, 224, 1024, 0, 1024, 0, 800];
    const trazo = (a) => `M${a[0]} ${a[1]}C${a[2]} ${a[3]} ${a[4]} ${a[5]} ${a[6]} ${a[7]}C${a[8]} ${a[9]} ${a[10]} ${a[11]} ${a[12]} ${a[13]}C${a[14]} ${a[15]} ${a[16]} ${a[17]} ${a[18]} ${a[19]}L${a[20]} ${a[21]}Q${a[22]} ${a[23]} ${a[24]} ${a[25]}L${a[26]} ${a[27]}Q${a[28]} ${a[29]} ${a[30]} ${a[31]}Z`;
    const entre = (t) => trazo(CUPULA.map((v, i) => v + (CUERPO[i] - v) * t));
    const claves = [
      { t: 0, sx: 0.5, sy: 0.4, f: 1 },
      { t: 0.3, sx: 0.62, sy: 1.18, f: 0.6 },
      { t: 0.52, sx: 1.12, sy: 0.86, f: 0.1 },
      { t: 0.72, sx: 0.97, sy: 1.04, f: 0 },
      { t: 0.88, sx: 1.012, sy: 0.992, f: 0 },
      { t: 1, sx: 1, sy: 1, f: 0 }
    ];
    const valor = (t) => {
      let i = 0;
      while (i < claves.length - 2 && t > claves[i + 1].t) i++;
      const a = claves[i], b = claves[i + 1], u = clamp((t - a.t) / (b.t - a.t), 0, 1), q = u * u * (3 - 2 * u);
      const mezcla = (c) => a[c] + (b[c] - a[c]) * q;
      const real = clamp((t - 0.46) / 0.2, 0, 1);   // el dibujo real (con ojos) toma el relevo cuando la forma ya es la suya
      return { sx: mezcla('sx'), sy: mezcla('sy'), f: mezcla('f'), bri: 1 + 0.4 * (1 - real), estado: t < 0.62 ? 'sleep' : 'idle', real };
    };
    setTimeout(() => {
      if (fin) return;
      const r = mascota.getBoundingClientRect();
      if (r.width < 10) { revela(); return; }
      const u = r.width / 1224;   // píxeles por unidad del dibujo de la mascota
      // de partida, la cúpula mide lo que el montículo del suelo
      claves[0].sx = clamp((d0 * (1.4 + 0.07 * M)) / (1024 * u), 0.1, 1.2);
      claves[0].sy = clamp((d0 * (0.6 + 0.11 * M)) / (519 * u), 0.1, 1.2);
      const ns = 'http://www.w3.org/2000/svg';
      const ov = document.createElementNS(ns, 'svg');
      ov.setAttribute('viewBox', '-100 -200 1224 1324');
      ov.setAttribute('aria-hidden', 'true');
      ov.style.cssText = 'position:absolute;left:0;top:0;width:100%;height:auto;overflow:visible;pointer-events:none';
      ov.innerHTML = '<defs><radialGradient id="intro-cuerpo" cx=".32" cy=".26" r=".95"><stop offset="0" stop-color="#fff"/><stop offset=".5" stop-color="#eef2f9"/><stop offset="1" stop-color="#aab6cc"/></radialGradient></defs>'
        + '<path fill="url(#intro-cuerpo)" stroke="#0b1018" stroke-width="16" stroke-linejoin="round"/>';
      const camino = ov.querySelector('path');
      camino.setAttribute('d', entre(0));
      mascota.append(ov);
      window.mundoIntro = { x: mx, y: sueloY, overlay: ov, ...valor(0) };
      root.classList.add('intro-sombra');   // la sombra del suelo espera: la masa aún no es la mascota
      setTimeout(() => { if (!fin) revela(); }, 50 * k);   // un instante para que mundo.js la dibuje ya con la forma de la masa
      const t0 = performance.now();
      let sombra = false;
      const paso = setInterval(() => {
        if (fin) { clearInterval(paso); ov.remove(); return; }
        const t = clamp((performance.now() - t0) / (MORFA * k), 0, 1);
        const v = valor(t), m = clamp(t / 0.46, 0, 1), me = m * m * (3 - 2 * m);
        camino.setAttribute('d', entre(me));
        ov.style.opacity = (1 - v.real).toFixed(3);
        window.mundoIntro = { x: mx, y: sueloY, overlay: ov, ...v };
        if (!sombra && t > 0.55) { sombra = true; root.classList.remove('intro-sombra'); root.classList.add('intro-sombra-sale'); setTimeout(() => root.classList.remove('intro-sombra-sale'), 500); }
        if (t >= 1) { clearInterval(paso); window.mundoIntro = null; ov.remove(); }
      }, 16);
    }, tF * k);
    if (new URLSearchParams(location.search).has('introPausa')) return;   // solo para revisar: no se limpia al final
    setTimeout(acaba, (tF + MORFA + 500) * k);
  })().catch(() => termina());
})();
