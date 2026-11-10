// Mundo del Castillo Blanco: 5 salas pintadas (placas de imagegen) con cámara que sigue a la mascota, lianas que reaccionan
// al viento que levanta al caminar, luces que se avivan cuando se acerca, niebla, partículas atraídas por ella y un
// disolvente final de azul a blanco hacia la siguiente sección. Sin librerías.
// Con prefers-reduced-motion el mundo sigue siendo el mismo (el scroll lo controla la persona), pero sin movimiento propio: sin paseos, partículas ni lianas que se mecen.
(() => {
  const root = document.querySelector('.mundo');
  if (!root) return;
  const $ = (s) => root.querySelector(s);
  const stage = $('.mundo-escena'), fondos = $('.mundo-fondos'), frentes = $('.mundo-frentes'), mascota = $('.mundo-mascota');
  const mascotaUse = mascota.querySelector('use'), mascotaSvg = mascota.querySelector('svg');
  const sombra = $('.mundo-sombra'), lucesBox = $('.mundo-luces'), canvas = $('.mundo-motas'), textos = $('.mundo-textos'), alba = $('.mundo-alba');
  const copys = [...root.querySelectorAll('.mundo-copy')], dots = [...root.querySelectorAll('.mundo-dots button')];
  const reduce = matchMedia('(prefers-reduced-motion: reduce)');
  const N = 5, INK = '#05080e';
  const clamp = (v, a = 0, b = 1) => Math.min(b, Math.max(a, v));
  const lerp = (a, b, k) => a + (b - a) * k;
  const rng = (seed) => { let s = seed * 9301 + 49297; return () => (s = (s * 16807) % 2147483647) / 2147483647; };
  const f = (n) => n.toFixed(1);

  // ---------- lianas delanteras (SVG) ----------
  const vine = (x, len, seed, sc = 1, i = 0) => {
    const r = rng(seed), y0 = -260, n = Math.floor((len - y0) / (34 * sc)); let d = `M${f(x)} ${y0}`, leaves = '';
    for (let k = 0; k < n; k++) {
      const y = y0 + (k + 1) * 34 * sc, px = x + Math.sin(k * .7 + seed) * 9 * sc, s = k % 2 ? 1 : -1, a = s * (26 + r() * 20);
      d += `L${f(px)} ${f(y)}`;
      const cx = px + s * 13 * sc;
      leaves += `<ellipse cx="${f(cx)}" cy="${f(y)}" rx="${f(12 * sc)}" ry="${f(25 * sc)}" transform="rotate(${f(a)} ${f(cx)} ${f(y)})"/>`;
      if (k < n - 2 && r() > .35) leaves += `<ellipse cx="${f(cx - s * 2)}" cy="${f(y - 9 * sc)}" rx="${f(4 * sc)}" ry="${f(9 * sc)}" fill="#fff" fill-opacity=".5" transform="rotate(${f(a)} ${f(cx)} ${f(y)})"/>`;
    }
    return `<svg class="liana" viewBox="${f(x - 70 * sc)} ${y0} ${f(140 * sc)} ${f(len - y0 + 60)}" data-x="${f(x)}" data-sc="${f(sc)}" data-len="${f(len)}" data-k="${i}" aria-hidden="true" focusable="false"><path d="${d}" fill="none" stroke="${INK}" stroke-width="${f(6 * sc)}" stroke-linecap="round"/><g fill="${INK}">${leaves}</g></svg>`;
  };
  const lianas = (i, paso) => {
    const r = rng(40 + i * 7); let o = '', k = 0;
    for (let x = 40; x < 1580; x += paso + r() * 70) { const borde = Math.abs(x - 800) / 800; o += vine(x, 140 + borde * 420 + r() * 120, 3 + i * 11 + k, .8 + borde * .35, k); k++; }
    return o;
  };
  const frenteHtml = (i) => `<div class="frente" aria-hidden="true">${lianas(i, i === 3 ? 110 : 160)}</div>`;
  const plano = (n, carga = 'data-src') => `<img class="plano" ${carga}="castillo/img/plano-${n}-fondo.webp" alt="" decoding="async"${carga === 'src' ? ' loading="lazy"' : ''}>`;

  // ---------- datos de cada sala (coordenadas del dibujo 1600x900) ----------
  // Un único nivel de suelo para el personaje (SUELO): cada fondo se desplaza en vertical (dy) para que el borde de su
  // suelo (medido sobre la imagen) quede `aj` unidades por debajo de los pies; aj es cuánto se ve «por encima» del borde.
  const SUELO = 780;
  const dyDe = (borde, aj) => SUELO + aj - borde;
  const ESC = [
    { dy: dyDe(790.6, 3), suelo: () => SUELO, rango: [470, 1250], casa: 960, base: 'idle', luces: [{ x: 790, y: 150, r: 260, c: '255,255,255', a: .32, re: 400 }] },
    { dy: dyDe(807.4, 3), suelo: () => SUELO, rango: [470, 1250], casa: 940, base: 'sleep', luces: [{ x: 1115, y: 700, r: 210, c: '190,215,255', a: .38, re: 300 }] },
    { dy: dyDe(800.5, 3), suelo: () => SUELO, rango: [470, 1300], casa: 1000, base: 'idle', luces: [222, 602, 791, 973, 1160, 1423].map((x) => ({ x, y: 722, r: 62, c: '255,226,170', a: .85, re: 230 })) },
    { dy: dyDe(777, 3), suelo: () => SUELO, rango: [470, 1300], casa: 1060, base: 'curious', luces: [{ x: 1050, y: 450, r: 300, c: '170,200,255', a: .3, re: 360 }, { x: 640, y: 560, r: 120, c: '200,225,255', a: .35, re: 300 }] },
    { dy: dyDe(762.4, 3), suelo: () => SUELO, rango: [470, 1300], casa: 1120, base: 'idle', luces: [{ x: 1215, y: 560, r: 340, c: '255,255,255', a: .5, re: 420 }] },
  ];

  // ---------- construcción ----------
  fondos.innerHTML = ESC.map((_, i) => plano(i + 1)).join('');
  frentes.innerHTML = ESC.map((_, i) => frenteHtml(i)).join('');
  const planos = [...fondos.children], fsvgs = [...frentes.children];
  const vinesDe = fsvgs.map((sv) => [...sv.querySelectorAll('.liana')].map((el) => ({ el, x: +el.dataset.x, sc: +el.dataset.sc, len: +el.dataset.len, k: +el.dataset.k, p: 0 })));
  const lucesDe = ESC.map((E) => E.luces.map((l) => {
    const el = document.createElement('div'); el.className = 'luz'; el.innerHTML = `<i style="background:radial-gradient(circle, rgba(${l.c},1), rgba(${l.c},0) 68%);animation-delay:${(-Math.random() * 4).toFixed(2)}s;animation-duration:${(2.2 + Math.random() * 2.6).toFixed(2)}s"></i>`;
    lucesBox.append(el); return { el, ...l };
  }));

  // ---------- estado ----------
  let flow = false, vert = false, W = 1280, H = 720, s = .8, ty = 0, camMin = 800, camMax = 800;
  let t = 0, activa = 0, cam = 800, ax = ESC[0].casa, ay = ESC[0].suelo(ESC[0].casa), tx = ax, air = 0, lean = 0, dir = 1;
  const HOP_MS = 1150; // un salto de slime: preparación (16 %), vuelo (68 %) y aterrizaje con rebote amortiguado (16 %)
  let paseoIni = 0, paseoHasta = 0, hopHasta = 0, hopIni = 0, glanceHasta = 0, ratonHasta = 0, sigAccion = 3000, choque = 0, visible = true, vivo = false, anterior = 0, estadoActual = '';
  // física del cuerpo: altura y velocidad vertical (ph, pv, pg), deformación elástica (dv, dvv) y cizalla de gelatina (sh, shv)
  let ph = 0, pv = 0, pg = 2200, pSuelo = true, dv = 0, dvv = 0, sh = 0, shv = 0, pCiclo = -1, pHp = -1, proxSacudida = 3500;
  let sqx = 1, sqy = 1, mx = 0, my = 0, smx = 0, smy = 0, cargado = false, ramVista = 0, ramMeta = 0, albaV = 0;
  const setEstado = (e) => { if (e !== estadoActual) { estadoActual = e; mascotaUse.setAttribute('href', `castillo/mascota.svg?v=6#${e}`); mascota.dataset.estado = e; } };
  const mapX = (x) => (x - cam) * s + W / 2;
  const mapY = (y) => (y - 450) * s + H / 2 + ty;

  const medir = () => {
    W = stage.clientWidth; H = stage.clientHeight; vert = innerWidth <= 720;
    if (vert) { s = Math.max(.74 * H / 1012.5, W / 1800 * 1.05); ty = 506.25 * s - H / 2; } else { s = Math.max(W / 1600, H / 900); ty = 0; }
    const hw = W / (2 * s); camMin = -100 + hw; camMax = 1700 - hw; if (camMin > camMax) camMin = camMax = 800;
    planos.forEach((im) => { im.style.width = 1800 * s + 'px'; im.style.height = 1012.5 * s + 'px'; });
    vinesDe.forEach((ls) => ls.forEach((v) => { const st = v.el.style; st.left = (v.x - 70 * v.sc) * s + 'px'; st.top = -260 * s + 'px'; st.width = 140 * v.sc * s + 'px'; st.height = (v.len + 320) * s + 'px'; }));
    lucesDe.forEach((ls) => ls.forEach((l) => { l.el.style.width = l.el.style.height = 2 * l.r * s + 'px'; }));
  };

  const limitaX = (x, w) => {
    let v = clamp(x, ...ESC[activa].rango);
    if (!vert) { const tr = textos.getBoundingClientRect(), sr = stage.getBoundingClientRect(); v = Math.max(v, cam + (tr.right - sr.left + w * .5 + 14 - W / 2) / s); }
    return v;
  };

  // ---------- bucle ----------
  const pintar = (dt, ahora) => {
    const r = root.getBoundingClientRect(), cola = innerHeight * .9, recorrido = Math.max(1, root.offsetHeight - innerHeight - cola);
    const objetivo = clamp(-r.top / recorrido) * (N - 1) || 0;
    albaV += (clamp((-r.top - recorrido) / (cola * .7)) - albaV) * (1 - Math.exp(-dt * 8));
    const antes = t;
    t += (objetivo - t) * (1 - Math.exp(-dt * 6.5)); if (Math.abs(objetivo - t) < .0004) t = objetivo;
    const velScroll = Math.abs(t - antes) / Math.max(dt, .001);
    smx += (mx - smx) * (1 - Math.exp(-dt * 5)); smy += (my - smy) * (1 - Math.exp(-dt * 5));
    if (!Number.isFinite(t)) t = 0;
    const nueva = clamp(Math.round(t), 0, N - 1);
    if (nueva !== activa) { activa = nueva; hopHasta = ahora + HOP_MS; hopIni = ahora; choque = 1; }
    const E = ESC[activa], trans = Math.abs(t - nueva) > .035;
    const w = Math.min(190 * s * (W < 1100 ? 1.15 : 1), H > W ? W * (vert ? .34 : .2) : 1e9);

    // posición de la mascota: guiada por el scroll entre salas; libre (camina al clic o por su cuenta) cuando se queda quieta
    let vxs = 0;
    const libre = !trans && Math.abs(tx - ax) > 1;                       // paseo libre (clic o por su cuenta)
    // cada saltito de paseo es un evento con su propia duración: el scroll solo decide si empieza otro, nunca lo corta a medias (así no se queda en el aire)
    if ((velScroll > .06 || libre) && ahora >= paseoHasta && ahora >= hopHasta) { paseoIni = ahora; paseoHasta = ahora + 660; }
    const ciclo = ahora < paseoHasta ? (ahora - paseoIni) / 660 : -1;   // se agacha, vuela y aterriza
    const enAire = libre && ciclo > .2 && ciclo < .74;
    if (trans) {
      const a = Math.floor(clamp(t, 0, N - 1.0001)), u = t - a, e = u * u * (3 - 2 * u), A = ESC[a], B = ESC[Math.min(N - 1, a + 1)];
      const nx = lerp(A.casa, B.casa, e), ny = lerp(A.suelo(A.casa), B.suelo(B.casa), e);
      vxs = (nx - ax) / Math.max(dt, .001); ax = nx; ay = ny; tx = nx;
    } else {
      const v = enAire ? 640 * dt : 0; const paso = clamp(tx - ax, -v, v); ax += paso; vxs = paso / Math.max(dt, .001); // solo avanza mientras está en el aire
      ay += (E.suelo(ax) - ay) * (1 - Math.exp(-dt * 16));
    }
    const moviendo = velScroll > .06 || libre;
    const salta = ciclo >= 0;
    if (moviendo) sigAccion = Math.max(sigAccion, ahora + 2500);
    const hp = ahora < hopHasta ? clamp((ahora - hopIni) / HOP_MS) : -1;
    // Física del cuerpo. Cada salto (el de paseo y el saltito) arranca un tiro balístico con la altura y la duración de siempre (v0 = 4·h/T, g = 8·h/T²); al caer rebota con
    // pérdida de energía (restitución ~0,3) hasta quedarse quieto, y cada impacto chafa el cuerpo. El cuerpo es un resorte amortiguado (gelatina): se agacha antes de saltar,
    // se estira con la velocidad, se chafa al aterrizar y vibra hasta asentarse; respira en reposo y de vez en cuando se sacude un poco.
    const lanza = (apex, T) => { pv = (4 * apex) / T; pg = (8 * apex) / (T * T); pSuelo = false; dvv += 3.2; };   // al despegar suelta la compresión y se estira con rebote
    if (hp >= 0.16 && pHp < 0.16) lanza(74, (HOP_MS * 0.68) / 1000);
    if (ciclo > 0.2 && pCiclo <= 0.2) lanza(42, 0.66 * 0.54);
    pHp = hp; pCiclo = ciclo;
    if (!pSuelo) {
      pv -= pg * dt; ph += pv * dt;
      if (ph <= 0) {
        const imp = -pv; ph = 0;
        if (imp > 70) { pv = imp * 0.3; pg = 2600; dvv -= Math.min(6, imp * 0.0055); } else { pv = 0; pSuelo = true; }
        if (imp > 70 && pv < 70) { pv = 0; pSuelo = true; }
      }
    }
    air = ph;
    const quieta = !reduce.matches;
    let dvT = 0;
    if ((hp >= 0 && hp < 0.16) || (ciclo >= 0 && ciclo < 0.2)) dvT = -0.2;                  // se agacha antes de saltar
    else if (!pSuelo) dvT = clamp(Math.abs(pv) / 1500, 0, 0.24);                             // se estira con la velocidad
    if (quieta) dvT += 0.014 * Math.sin(ahora / 520);                                         // respira
    if (quieta && pSuelo && !moviendo && ahora > proxSacudida) { dvv += (Math.random() < 0.5 ? -1 : 1) * (1.4 + Math.random() * 1.4); proxSacudida = ahora + 4200 + Math.random() * 5000; }   // se acomoda
    dvv += (260 * (dvT - dv) - 7.5 * dvv) * dt; dv = clamp(dv + dvv * dt, -0.34, 0.32);
    shv += (180 * (clamp(-vxs * 0.012, -9, 9) - sh) - 9 * shv) * dt; sh += shv * dt;          // la gelatina se queda atrás al arrancar y frenar
    if (document.documentElement.classList.contains('intro')) cam = clamp(ax, camMin, camMax);   // intro de la landing: sin el paneo inicial de la cámara, la mascota ya está en su sitio definitivo
    else cam += (clamp(ax, camMin, camMax) - cam) * (1 - Math.exp(-dt * 2.8));

    // vida propia: pasea, mira alrededor o salta de vez en cuando (salvo si duerme)
    const durmiendo = E.base === 'sleep' && !(activa === 1 && cargado);
    if (document.documentElement.classList.contains('intro')) sigAccion = Math.max(sigAccion, ahora + 2500);   // intro de la landing: la mascota se queda quieta, la gota blanca cae en su sitio
    if (!trans && !moviendo && ahora > sigAccion && !reduce.matches) {
      sigAccion = ahora + 3800 + Math.random() * 4500;
      if (!durmiendo) { const q = Math.random(); if (q < .5) tx = limitaX(ax + (Math.random() - .5) * 360, w); else if (q < .8) glanceHasta = ahora + 1700; else { hopHasta = ahora + HOP_MS; hopIni = ahora; choque = 1; } }
    }

    // escenas: opacidades, textos, cámara de fondo y primer plano
    const frExtra = -(cam - 800) * s * .35 + smx * 14;
    for (let i = 0; i < N; i++) {
      const d = t - i, op = clamp((0.85 - Math.abs(d)) / 0.5);
      stage.style.setProperty('--w' + i, op.toFixed(3));
      const im = planos[i];
      if (im.dataset.src && Math.abs(t - i) < 1.6) { im.src = im.dataset.src; im.removeAttribute('data-src'); }
      im.style.opacity = fsvgs[i].style.opacity = op.toFixed(3); fsvgs[i].style.display = op > 0 ? '' : 'none';
      lucesDe[i].forEach((l) => { l.el.style.opacity = op > 0 ? (op * l.a * (1 + 1.3 * Math.exp(-(((ax - l.x) / l.re) ** 2)))).toFixed(3) : 0; });
      if (op > 0) {
        im.style.transform = `translate3d(${f(mapX(-100) + smx * 8)}px,${f(mapY(-56 + ESC[i].dy) + smy * 5)}px,0)`;
        fsvgs[i].style.transform = `translate3d(${f(W / 2 - cam * s + frExtra)}px,${f(H / 2 - 450 * s + ty + smy * 9)}px,0)`;
        lucesDe[i].forEach((l) => { l.el.style.transform = `translate3d(${f(mapX(l.x) - l.r * s + smx * 8)}px,${f(mapY(l.y + ESC[i].dy) - l.r * s)}px,0)`; });
        // viento: las lianas cercanas se apartan cuando la mascota pasa o salta
        const mxs = mapX(ax), shock = choque > 0 ? Math.max(0, 1 - (ahora - hopIni) / 700) : 0, viento = clamp(Math.abs(vxs) * s / 260) + shock * .8;
        for (const v of vinesDe[i]) {
          const dx = W / 2 - cam * s + frExtra + v.x * s - mxs, tgt = Math.sign(dx || 1) * viento * Math.exp(-((dx / 190) ** 2)) * 11;
          v.p += (tgt - v.p) * .16;
          const sway = reduce.matches ? 0 : Math.sin(ahora / (2300 + v.k * 370) + v.k * 1.7) * 1.8; // vaivén propio de cada liana
          v.el.style.transform = `rotate(${(sway + v.p).toFixed(2)}deg)`;
        }
      }
      const cp = copys[i], vis = Math.abs(d) < .34, fadeAlba = i === N - 1 ? 1 - clamp(albaV * 2.4) : 1;
      cp.classList.toggle('activa', Math.abs(d) < .34);
      cp.style.opacity = vis ? (clamp(1 - Math.abs(d) / .34) * fadeAlba).toFixed(3) : 0;
      cp.style.transform = `translate(0,var(--tcy,-50%)) translate3d(0,${f(d * -46)}px,0)`;
      cp.toggleAttribute('inert', !vis || Math.abs(d) > .2);
      cp.setAttribute('aria-hidden', String(!vis));
      cp.style.visibility = vis && fadeAlba > .02 ? 'visible' : 'hidden';
    }
    dots.forEach((b, i) => b.setAttribute('aria-current', String(i === activa)));
    stage.dataset.escena = activa;
    stage.style.setProperty('--alba', albaV.toFixed(3));
    alba.style.transform = `translate3d(0,calc(${f((1 - albaV) * 100)}% + ${f(albaV * H * .28)}px),0)`; // al terminar, el banco de nubes queda a media pantalla (no arriba del todo) para dejar poco blanco vacío antes de la sección siguiente

    // mascota
    const px = clamp(mapX(ax), w * .5 + 8, W - w * .5 - 8), suelo = E.suelo(ax), lift = 0; // durante el disolvente final el slime se queda en el suelo (solo se desvanece)
    lean += ((libre && enAire && air > 10 ? Math.sign(tx - ax) * 3 : 0) - lean) * (1 - Math.exp(-dt * 30)); // solo se inclina en el aire; al tocar el suelo vuelve recto de inmediato // se inclina hacia delante al saltar; nunca se gira
    mascota.style.width = f(w) + 'px';
    // intro de la landing (castillo-intro.js): mientras dura la formación, la propia mascota hace de gota que se convierte en ella, sin clones: el intro le dice dónde está la masa (x, y en pantalla)
    // y cuánto la aplasta o estira (sx, sy), la aclara (bri) y qué pose lleva (estado); aquí solo se suma al movimiento normal.
    const hi = window.mundoIntro || null;
    const mx0 = px + smx * 8, my0 = mapY(ay) - air * s - lift + smy * 5;
    let hiMov = '';
    if (hi) { const sr = stage.getBoundingClientRect(); hiMov = `translate3d(${f(hi.f * (hi.x - sr.left - mx0))}px,${f(hi.f * (hi.y - sr.top - my0))}px,0) `; }
    mascota.style.transform = `${hiMov}translate3d(${f(mx0)}px,${f(my0)}px,0) translate(-50%,-92.45%) rotate(${f(lean)}deg)`;
    sqy = 1 + dv; sqx = 1 / Math.pow(sqy, 0.9);   // volumen casi constante: más alto = más estrecho
    mascotaSvg.style.transform = `scale(${(sqx * (hi ? hi.sx : 1)).toFixed(3)},${(sqy * (hi ? hi.sy : 1)).toFixed(3)}) skewX(${sh.toFixed(2)}deg)`;
    mascotaSvg.style.filter = hi && hi.bri > 1.01 ? `brightness(${hi.bri.toFixed(2)})` : '';
    mascotaSvg.style.opacity = hi && hi.real != null ? hi.real.toFixed(3) : '';   // intro: el dibujo real aparece al final; hasta entonces manda la forma que se transforma
    if (hi && hi.overlay) hi.overlay.style.transform = mascotaSvg.style.transform;   // y esa forma se deforma y se mueve igual que la mascota
    sombra.style.width = f(w * .78 * (1 - air / 150)) + 'px';
    sombra.style.opacity = (.55 * (1 - air / 160)).toFixed(3);
    sombra.style.transform = `translate3d(${f(px + smx * 8)}px,${f(mapY(suelo) - 3 + smy * 5)}px,0) translate(-50%,-50%)`;
    let st = E.base;
    if (ahora < hopHasta) st = 'hop';
    else if (moviendo) st = 'walk';
    else if (activa === 1 && cargado) st = 'idle';
    else if (!durmiendo && ahora < glanceHasta) st = 'curious';
    setEstado((hi && hi.estado) || st);
    motas(dt, ahora, px, mapY(ay) - air * s - lift - w * .5, s);
    choque = Math.max(0, choque - dt * 2.5);
  };

  // ---------- partículas ----------
  const ctx = canvas.getContext('2d');
  let motasLista = [], hojas = [], burst = [], dpr = 1;
  const brillo = document.createElement('canvas'); brillo.width = brillo.height = 64; // sprite del brillo: se dibuja una vez y se estampa
  { const b = brillo.getContext('2d'), g = b.createRadialGradient(32, 32, 0, 32, 32, 32); g.addColorStop(0, 'rgba(255,255,255,1)'); g.addColorStop(1, 'rgba(255,255,255,0)'); b.fillStyle = g; b.fillRect(0, 0, 64, 64); }
  const iniciarMotas = () => {
    dpr = Math.min(1.5, devicePixelRatio || 1); canvas.width = W * dpr; canvas.height = H * dpr;
    const r = rng(7); motasLista = Array.from({ length: W < 760 ? 34 : 64 }, () => ({ x: r() * W, y: r() * H, r: .8 + r() * 2.2, vx: (r() - .5) * 10, vy: -(4 + r() * 12), ph: r() * 6.28, z: r() }));
  };
  const motas = (dt, ahora, hx, hy, sz) => {
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0); ctx.clearRect(0, 0, W, H); ctx.globalAlpha = 1;
    if (reduce.matches) return;
    const jar = +stage.style.getPropertyValue('--w3') || 0, dis = 1 - clamp(albaV * 1.4);
    for (const m of motasLista) {
      const dx = hx - m.x, dy = hy - m.y, d = Math.hypot(dx, dy) || 1;
      if (d < 280) { const k = (1 - d / 280) * 55 * dt; m.vx += (dx / d) * k + (-dy / d) * k * .8; m.vy += (dy / d) * k + (dx / d) * k * .8; } // atraídas y en órbita
      if (choque > .9 && d < 240) { m.vx -= (dx / d) * 120; m.vy -= (dy / d) * 120; } // se dispersan cuando salta
      m.vx *= 1 - dt * .6; m.vy += (-(4 + m.z * 10) - m.vy) * dt * .35;
      m.x += (m.vx + Math.sin(ahora / 1800 + m.ph) * 8) * dt * (.4 + m.z); m.y += m.vy * dt * (.4 + m.z);
      if (m.y < -10) { m.y = H + 10; m.x = Math.random() * W; } if (m.x < -10) m.x = W + 10; if (m.x > W + 10) m.x = -10;
      const tw = .5 + .5 * Math.sin(ahora / 700 + m.ph), rr = m.r * (1 + jar * 1.2 + (d < 200 ? .6 : 0)) * (.6 + m.z * .8);
      ctx.globalAlpha = (.5 + tw * .4) * (.55 + m.z * .4) * dis; ctx.drawImage(brillo, m.x - rr * 4, m.y - rr * 4, rr * 8, rr * 8);
    }
    if (jar > .25 && hojas.length < 18 && Math.random() < dt * 2.4) hojas.push({ x: Math.random() * W, y: -16, vy: 26 + Math.random() * 30, vx: (Math.random() - .5) * 18, ph: Math.random() * 6.28, rot: Math.random() * 6.28, vr: (Math.random() - .5) * 2.2, rr: 5 + Math.random() * 5 });
    hojas = hojas.filter((h) => h.y < H + 20);
    for (const h of hojas) {
      const dx = h.x - hx, dy = h.y - hy, d = Math.hypot(dx, dy) || 1; if (d < 110) { h.vx += (dx / d) * 160 * dt; h.vy -= 60 * dt; } // la mascota las aparta
      h.vx *= 1 - dt * .8; h.x += (h.vx + Math.sin(ahora / 900 + h.ph) * 26) * dt; h.y += h.vy * dt; h.rot += h.vr * dt;
      ctx.save(); ctx.globalAlpha = jar * dis; ctx.translate(h.x, h.y); ctx.rotate(h.rot); ctx.fillStyle = 'rgba(8,12,20,.92)'; ctx.strokeStyle = 'rgba(255,255,255,.4)'; ctx.lineWidth = 1;
      ctx.beginPath(); ctx.ellipse(0, 0, h.rr * .55, h.rr, 0, 0, 6.283); ctx.fill(); ctx.stroke(); ctx.restore();
    }
    ctx.globalAlpha = 1; burst = burst.filter((b) => (b.l -= dt) > 0);
    for (const b of burst) { b.x += b.vx * dt; b.y += b.vy * dt; b.vy += 60 * dt; ctx.fillStyle = `rgba(255,255,255,${clamp(b.l / b.t) * .9})`; ctx.beginPath(); ctx.arc(b.x, b.y, b.r * sz * 1.6, 0, 6.283); ctx.fill(); }
  };
  const chispas = (px, py) => {
    const rc = mascota.getBoundingClientRect(), sr = stage.getBoundingClientRect(), cx = px ?? rc.left + rc.width / 2 - sr.left, cy = py ?? rc.top + rc.height * .45 - sr.top;
    for (let k = 0; k < 22; k++) { const a = Math.random() * 6.283, v = 60 + Math.random() * 180; burst.push({ x: cx, y: cy, vx: Math.cos(a) * v, vy: Math.sin(a) * v - 90, r: 1.2 + Math.random() * 2.2, l: .9 + Math.random() * .8, t: 1.6 }); }
  };

  // ---------- interruptor de la sala 2: cargar el modelo despierta a la mascota y sube la RAM de ejemplo ----------
  const sw = $('#mundo-sw'), ramOut = $('#mundo-sw-ram');
  const ramTexto = (v) => v.toFixed(1).replace('.', document.documentElement.lang === 'en' ? '.' : ',') + ' GB';
  const animarRam = () => { ramVista += (ramMeta - ramVista) * .18; if (Math.abs(ramMeta - ramVista) < .05) ramVista = ramMeta; ramOut.textContent = ramTexto(ramVista); if (ramVista !== ramMeta) requestAnimationFrame(animarRam); };
  if (sw) sw.addEventListener('click', () => { cargado = !cargado; sw.setAttribute('aria-checked', String(cargado)); ramMeta = cargado ? 4.2 : 0; hopHasta = performance.now() + HOP_MS; hopIni = performance.now(); choque = 1; animarRam(); });

  // ---------- bucle y modos ----------
  const bucle = (ms) => {
    vivo = false;
    if (flow || !visible || document.hidden) return;
    const dt = Math.min(.05, (ms - anterior) / 1000 || .016); anterior = ms;
    pintar(dt, ms);
    vivo = true; requestAnimationFrame(bucle);
  };
  const arrancar = () => { if (!vivo && !flow && visible && !document.hidden) { anterior = performance.now(); vivo = true; requestAnimationFrame(bucle); } };

  const modo = () => {
    root.classList.add('mundo--fijo');
    root.classList.toggle('mundo--vert', innerWidth <= 720);
    medir(); iniciarMotas(); estadoActual = ''; arrancar();
  };

  new IntersectionObserver((es) => { visible = es[0].isIntersecting; if (visible) arrancar(); }, { rootMargin: '10% 0px' }).observe(root);
  document.addEventListener('visibilitychange', arrancar);
  addEventListener('resize', modo);
  reduce.addEventListener('change', modo);

  const foco = document.createElement('div'); foco.className = 'mundo-foco'; foco.setAttribute('aria-hidden', 'true'); stage.append(foco);
  stage.addEventListener('pointermove', (e) => {
    const r = stage.getBoundingClientRect(); mx = ((e.clientX - r.left) / r.width - .5) * 2; my = ((e.clientY - r.top) / r.height - .5) * 2; ratonHasta = performance.now() + 1800;
    stage.style.setProperty('--cx', (e.clientX - r.left) + 'px'); stage.style.setProperty('--cy', (e.clientY - r.top) + 'px');
  });
  stage.addEventListener('pointerleave', () => { mx = 0; my = 0; });
  // el slime no reacciona al ratón: solo el fondo (foco de luz, profundidad) y unas chispas al hacer clic en el escenario
  stage.addEventListener('click', (e) => {
    if (flow || e.target.closest('a,button,.mundo-copy')) return;
    const r = stage.getBoundingClientRect(); chispas(e.clientX - r.left, e.clientY - r.top);
  });

  const ir = (i) => {
    const total = root.offsetHeight - innerHeight - innerHeight * .9, y = root.getBoundingClientRect().top + scrollY + (i / (N - 1)) * total;
    scrollTo({ top: y, behavior: reduce.matches ? 'auto' : 'smooth' });
  };
  // «Explora la escena»: un recorrido lento y suave por las cinco salas, de la primera a la última (unos 4,4 s por sala de media), con arranque y llegada suaves. Cualquier gesto de la persona
  // (rueda, toque, tecla o clic) lo detiene al instante y le devuelve el control. Con movimiento reducido salta directo a la última sala.
  let recorrido = 0;
  const parar = () => {
    cancelAnimationFrame(recorrido); recorrido = 0;
    for (const t of ['wheel', 'touchstart', 'keydown', 'pointerdown']) removeEventListener(t, parar);
  };
  const explora = () => {
    const total = root.offsetHeight - innerHeight - innerHeight * .9, y0 = scrollY, y1 = root.getBoundingClientRect().top + scrollY + total;
    if (y1 <= y0 + 4) return;
    if (reduce.matches) { scrollTo({ top: y1, behavior: 'instant' }); return; }
    parar();
    const dur = Math.max(5000, ((y1 - y0) / total) * (N - 1) * 4400), t0 = performance.now();
    const paso = (ahora) => {
      const u = Math.min(1, (ahora - t0) / dur), e = .5 - Math.cos(Math.PI * u) / 2;   // ease-in-out senoidal: arranca y llega con calma y en medio no corre
      scrollTo({ top: y0 + (y1 - y0) * e, behavior: 'instant' });
      if (u < 1) recorrido = requestAnimationFrame(paso); else parar();
    };
    recorrido = requestAnimationFrame(paso);
    for (const t of ['wheel', 'touchstart', 'keydown', 'pointerdown']) addEventListener(t, parar, { passive: true, once: true });
  };
  document.querySelector('.castillo-text-link[href="#widgets"]')?.addEventListener('click', (e) => { e.preventDefault(); explora(); });
  dots.forEach((b, i) => {
    b.addEventListener('click', () => ir(i));
    b.addEventListener('keydown', (e) => {
      const k = { ArrowDown: 1, ArrowRight: 1, ArrowUp: -1, ArrowLeft: -1 }[e.key];
      if (k) { e.preventDefault(); const n = clamp(i + k, 0, N - 1); dots[n].focus(); dots[n].click(); }
    });
  });
  modo();
})();

// Carrusel de widgets movido por el scroll, en dos filas que van en sentidos opuestos: la sección se fija, la fila de arriba avanza
// hacia la izquierda y la de abajo hacia la derecha hasta el final; después la página sigue.
// Con movimiento reducido se queda como un carrusel que se arrastra a mano (sin fijar nada).
(() => {
  const sec = document.querySelector('#widgets'), filas = [...document.querySelectorAll('.widgets-pista')];
  if (!sec || !filas.length) return;
  const fijo = sec.querySelector('.widgets-fijo'), ventana = sec.querySelector('.widgets-ventana');
  const reduce = matchMedia('(prefers-reduced-motion: reduce)');
  let dist = filas.map(() => 0), pendiente = false;
  const clamp01 = (v) => Math.min(1, Math.max(0, v));
  const pintar = () => {
    pendiente = false;
    if (reduce.matches) return;
    const r = sec.getBoundingClientRect(), p = clamp01(-r.top / Math.max(1, r.height - fijo.offsetHeight));
    filas.forEach((f, i) => { const x = i % 2 === 0 ? -p * dist[i] : -(1 - p) * dist[i]; f.style.transform = `translate3d(${x.toFixed(1)}px,0,0)`; });
  };
  const pedir = () => { if (!pendiente) { pendiente = true; requestAnimationFrame(pintar); } };
  const medir = () => {
    const manual = reduce.matches;
    sec.classList.toggle('widgets--manual', manual);
    if (manual) { sec.style.height = ''; filas.forEach((f) => { f.style.transform = ''; }); return; }
    dist = filas.map((f) => Math.max(0, f.scrollWidth - innerWidth));
    sec.style.height = Math.round(Math.max(...dist) + fijo.offsetHeight) + 'px';
    pintar();
  };
  addEventListener('scroll', pedir, { passive: true });
  addEventListener('resize', medir);
  addEventListener('load', medir);
  reduce.addEventListener('change', medir);
  // arrastre con ratón solo en el modo manual
  let abajo = false, x0 = 0, s0 = 0;
  ventana.addEventListener('pointerdown', (e) => { if (!reduce.matches || e.pointerType !== 'mouse') return; abajo = true; x0 = e.clientX; s0 = ventana.scrollLeft; });
  addEventListener('pointermove', (e) => { if (abajo) ventana.scrollLeft = s0 - (e.clientX - x0); });
  addEventListener('pointerup', () => { abajo = false; });
  sec.querySelectorAll('.w-tarjeta').forEach((c) => {
    c.addEventListener('pointermove', (e) => { if (e.pointerType !== 'mouse') return; const r = c.getBoundingClientRect(); c.style.setProperty('--rx', (((e.clientX - r.left) / r.width - .5) * 10).toFixed(1) + 'deg'); c.style.setProperty('--ry', ((.5 - (e.clientY - r.top) / r.height) * 10).toFixed(1) + 'deg'); });
    c.addEventListener('pointerleave', () => { c.style.removeProperty('--rx'); c.style.removeProperty('--ry'); });
  });
  medir();
})();

// Selector de mascotas: los sprites son las tiras de la propia app (p<mascota>s<estado>.png, 12 fotogramas; 13 en «despierta»).
(() => {
  const spr = document.querySelector('#pet-sprite');
  if (!spr) return;
  const FW = [96, 118, 116, 108], FH = [96, 112, 118, 108];
  const radios = [...document.querySelectorAll('.pet-opcion')], chips = [...document.querySelectorAll('.pet-estado')], caps = [...document.querySelectorAll('#pet-cap-lista [data-cap]')];
  const quieto = matchMedia('(prefers-reduced-motion: reduce)').matches;
  let pet = 0, est = 0, timer = 0, dentro = false;
  document.querySelectorAll('.pet-mini').forEach((m) => { const i = +m.dataset.pet, k = 64 / FH[i]; m.style.backgroundImage = `url(media/p${i}s5.png)`; m.style.backgroundSize = `${FW[i] * 13 * k}px ${FH[i] * k}px`; m.style.width = FW[i] * k + 'px'; m.style.height = 64 + 'px'; });
  const pinta = () => {
    const sp = +chips[est].dataset.s, n = sp === 5 ? 13 : 12;
    spr.style.cssText = `width:${FW[pet]}px;height:${FH[pet]}px;background-image:url(media/p${pet}s${sp}.png);background-size:${FW[pet] * n}px ${FH[pet]}px;--n:${n};--w:${FW[pet]}px;--dur:${(n * .095).toFixed(2)}s;--z:${Math.min(3.4, 290 / FH[pet]).toFixed(2)}${quieto ? ';animation:none' : ''}`;
    chips.forEach((c, k) => c.setAttribute('aria-pressed', String(k === est)));
    caps.forEach((c) => { c.hidden = c.dataset.cap !== String(sp); });
    radios.forEach((r, k) => { r.setAttribute('aria-checked', String(k === pet)); r.tabIndex = k === pet ? 0 : -1; });
  };
  const rota = () => { clearTimeout(timer); if (!quieto && !dentro && !document.hidden) timer = setTimeout(() => { est = (est + 1) % chips.length; pinta(); rota(); }, 4200); };
  radios.forEach((r, k) => {
    r.addEventListener('click', () => { pet = k; pinta(); });
    r.addEventListener('keydown', (e) => { const d = { ArrowRight: 1, ArrowDown: 1, ArrowLeft: -1, ArrowUp: -1 }[e.key]; if (d) { e.preventDefault(); pet = (pet + d + radios.length) % radios.length; pinta(); radios[pet].focus(); } });
  });
  chips.forEach((c, k) => c.addEventListener('click', () => { est = k; pinta(); rota(); }));
  const caja = document.querySelector('.pet-caja');
  caja.addEventListener('pointerenter', () => { dentro = true; clearTimeout(timer); });
  caja.addEventListener('pointerleave', () => { dentro = false; rota(); });
  document.addEventListener('visibilitychange', rota);
  pinta(); rota();
})();

// los fondos empiezan a reproducirse poco antes de entrar en pantalla y se pausan al alejarse
(() => {
  const vs = [...document.querySelectorAll('video[data-fondo]')];
  if (!vs.length || matchMedia('(prefers-reduced-motion: reduce)').matches) return;
  const io = new IntersectionObserver((es) => es.forEach((e) => (e.isIntersecting ? e.target.play().catch(() => {}) : e.target.pause())), { rootMargin: '600px 0px' });
  vs.forEach((v) => io.observe(v));
})();

// el notch de las pantallas repite el ciclo real de la mascota, distinto cada vuelta: dormida → despierta → mira, parpadea, sonríe → feliz y sonrojada → notch normal
(() => {
  const R = (a, b) => a + Math.random() * (b - a), esp = (ms) => new Promise((r) => setTimeout(r, ms));
  const pick = (a) => a[Math.floor(Math.random() * a.length)];
  const ciclo = async (el) => {
    const pose = (p) => (el.dataset.pose = p), parpadea = () => { el.classList.add('parp'); setTimeout(() => el.classList.remove('parp'), 110); };
    await esp(R(0, 2500));
    for (;;) {
      el.dataset.mostrar = 'mascota'; pose('dormida'); await esp(R(2600, 5200));
      pose('abierta'); parpadea(); await esp(R(900, 1500));
      for (let i = Math.round(R(2, 4)); i > 0; i--) {
        const p = pick(['izq', 'der', 'parpadeo', 'sonrisa', 'entorna']);
        p === 'parpadeo' ? parpadea() : pose(p);
        await esp(R(800, 1600)); pose('abierta'); await esp(R(400, 900));
      }
      if (Math.random() < .2) { pose('dormida'); await esp(R(2000, 3500)); continue; }   // a veces se vuelve a dormir sin saludar
      pose('feliz'); el.classList.add('rubor'); await esp(1800);
      el.dataset.mostrar = 'notch'; await esp(600); el.classList.remove('rubor');
      await esp(R(3500, 6000));
    }
  };
  document.querySelectorAll('.notch-real').forEach((el) => {
    el.dataset.pose = 'dormida'; el.dataset.mostrar = 'mascota';
    if (matchMedia('(prefers-reduced-motion: reduce)').matches) el.dataset.mostrar = 'notch'; else ciclo(el);
  });
})();

// la hora del notch de las pantallas va en tiempo real y con el formato del país del visitante (el idioma del navegador: 12 h con AM/PM o 24 h), sin geolocalizar por IP
(() => {
  const fmt = new Intl.DateTimeFormat(undefined, { hour: 'numeric', minute: '2-digit' });
  const pon = () => {
    const partes = fmt.formatToParts(new Date()), de = (t) => partes.find((p) => p.type === t)?.value ?? '';
    const ampm = de('dayPeriod');
    document.querySelectorAll('[data-hora]').forEach((e) => (e.textContent = de('hour') + (partes.some((p) => p.type === 'literal' && p.value.trim()) ? partes.find((p) => p.type === 'literal' && p.value.trim()).value : ':') + de('minute')));
    document.querySelectorAll('[data-ampm]').forEach((e) => { e.textContent = ampm; e.hidden = !ampm; });
  };
  pon();
  setTimeout(function t() { pon(); setTimeout(t, 60000); }, 60000 - (Date.now() % 60000));
})();

// la fecha del notch del simulador, con el idioma de la página
(() => {
  const pon = () => {
    const f = new Intl.DateTimeFormat(document.documentElement.lang || undefined, { weekday: 'short', day: 'numeric', month: 'short', year: 'numeric' }).format(new Date()).replace(/,/g, '').replace(/\./g, '');
    document.querySelectorAll('[data-fecha]').forEach((e) => (e.textContent = f));
  };
  pon(); setInterval(pon, 30000); document.getElementById('idioma')?.addEventListener('click', () => setTimeout(pon, 80));
})();

// el reproductor del simulador funciona «de verdad» aunque no suene nada: un reloj de 3:08 que sube, la barra, el tiempo restante y la letra sincronizada
// cada línea de la letra lleva su segundo de entrada (data-t, como un .lrc) y se resalta en cuanto el reloj lo alcanza
(() => {
  const sim = document.getElementById('probar'), lyr = document.getElementById('lyr');
  if (!sim || !lyr) return;
  const DUR = 188, INICIO = 17;   // duración de la canción y segundo en el que arranca la demo
  const lineas = [...lyr.children], marcas = lineas.map((li) => +li.dataset.t), mmss = (x) => `${String(Math.floor(x / 60)).padStart(2, '0')}:${String(Math.floor(x % 60)).padStart(2, '0')}`;
  const vistas = ['.l-music', '.l-lyrics'].map((sel) => {
    const capa = sim.querySelector(sel), t = capa.querySelectorAll('.nx-t');
    return { capa, ahora: t[0], resta: t[1], barra: capa.querySelector('.prog i') };
  });
  const arranque = performance.now();
  let previa = -2, visible = true, ult = '';
  new IntersectionObserver((es) => { visible = es[0].isIntersecting; }).observe(sim);
  const pinta = () => {
    requestAnimationFrame(pinta);
    if (!visible || document.hidden) return;
    const e = (INICIO + (performance.now() - arranque) / 1000) % DUR, seg = mmss(e);
    if (seg !== ult) {   // los textos cambian una vez por segundo; la barra, en cada fotograma
      ult = seg;
      vistas.forEach((v) => { v.ahora.textContent = seg; v.resta.textContent = '-' + mmss(DUR - e); });
    }
    const p = (e / DUR * 100).toFixed(2) + '%';
    vistas.forEach((v) => { v.barra.style.animation = 'none'; v.barra.style.width = p; });
    let l = -1;
    for (let i = 0; i < marcas.length; i++) if (e >= marcas[i]) l = i;
    if (l !== previa) {
      previa = l;
      lineas.forEach((li, i) => li.classList.toggle('on', i === l));
      lyr.style.transform = `translateY(${-((lineas[Math.max(l, 0)].offsetTop) - 40)}px)`;
    }
  };
  pinta();
})();

// el simulador en el móvil: la laptop se aleja lo justo para que quepa entero el widget más ancho (música con letra: 560 px + sus orejas), sin tocar el tamaño de los widgets
(() => {
  const sim = document.getElementById('probar');
  if (!sim) return;
  const ajusta = () => { if (innerWidth <= 720) sim.style.setProperty('--u', Math.max(.38, Math.min(1, (sim.clientWidth - 46) / 592)).toFixed(3)); };
  ajusta(); addEventListener('resize', ajusta);
})();

// el escritorio de la barra de tareas en móvil: se reduce entero (fondo, barra, iconos y mascota) para que quepa en el ancho de la laptop
(() => {
  const esc = document.querySelector('.marco-b .escritorio');
  if (!esc) return;
  const ajusta = () => {
    const marco = esc.parentElement, cs = getComputedStyle(marco);
    const ancho = marco.clientWidth - parseFloat(cs.paddingLeft) - parseFloat(cs.paddingRight);
    esc.style.setProperty('--k', Math.min(1, ancho / 1280).toFixed(4));
  };
  ajusta(); addEventListener('resize', ajusta);
})();
