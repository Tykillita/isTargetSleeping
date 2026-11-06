// Simulador del notch: cambia el estado de #mn con los botones. Datos de ejemplo; no habla con la app.
(() => {
  const sim = document.getElementById('probar'), mn = document.getElementById('mn');
  if (!sim || !mn) return;
  const botones = sim.querySelectorAll('.try button'), caps = sim.querySelectorAll('#sim-cap [data-c]');
  const lyr = document.getElementById('lyr'), lineas = lyr ? [...lyr.children] : [];
  let linea = 0, timer = 0;
  const escala = () => sim.style.setProperty('--u', Math.min(1, Math.max(.5, (sim.clientWidth - 24) / 540)).toFixed(3));
  const letra = () => {
    lineas.forEach((l, i) => l.classList.toggle('on', i === linea));
    lyr.style.transform = `translateY(${-(lineas[linea].offsetTop - 40)}px)`;
  };
  const poner = (s) => {
    mn.dataset.s = s;
    botones.forEach((b) => b.setAttribute('aria-pressed', String(b.dataset.s === s)));
    caps.forEach((c) => { c.hidden = c.dataset.c !== s; });
    clearInterval(timer);
    if (s === 'lyrics' && !document.body.hasAttribute('data-letra-real')) { linea = 0; letra(); timer = setInterval(() => { linea = (linea + 1) % lineas.length; letra(); }, 2400); }
  };
  botones.forEach((b) => b.addEventListener('click', () => poner(b.dataset.s)));
  const hora = () => { const t = new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }); sim.querySelectorAll('.js-hora').forEach((e) => { e.textContent = t; }); };
  hora(); setInterval(hora, 30000);
  escala(); addEventListener('resize', escala);
  const pet = document.getElementById('sim-pet');
  mn.addEventListener('click', () => { if (mn.dataset.s === 'pet') { pet.classList.add('awake'); setTimeout(() => pet.classList.remove('awake'), 4000); } });
  poner('pet');
})();

// Barra de tareas simulada: la mascota (hojas de sprites reales de la app) cuenta lo que hace Ollama.
(() => {
  const pet = document.getElementById('pet'), esc = document.getElementById('pet-escena');
  if (!pet || !esc) return;
  // Fotograma de cada mascota (Mira, Llama, Capibara, Gatito) y sus hojas de "esperando un modelo", que se asoma tras el logo.
  const medidas = [[96, 96], [118, 112], [116, 118], [108, 108]];
  // Asomada: [ancho, alto, fotogramas, segundos, ancla x (el centro del logo dentro del fotograma), borde inferior]; la capibara se apoya al lado del logo.
  const asoma = [[180, 108, 24, 3.6, 123, -97], [180, 108, 24, 3.6, 123, -97], [88, 88, 12, 2.6, 101, -71], [180, 108, 24, 3.6, 123, -97]];
  const estados = [{ d: 2.6, n: 12, c: '#6b6b72' }, { d: 1.5, n: 12, c: '#f2a33a' }, { d: 1.3, n: 12, c: '#33e07a' }, { d: 1.6, n: 12, c: '#f2f2f4' }, { d: 1.4, n: 12, c: '#f2f2f4' }, { d: 1.7, n: 13, c: '#ff6b5e' }, { d: 3.6, n: 24, c: '#f2a33a' }];
  const caps = document.querySelectorAll('#pet-cap [data-c]'), punto = document.querySelector('.bandeja .punto');
  let p = 0, e = 0, volver = 0;
  const pintar = () => {
    const st = pet.style, s = estados[e];
    if (e === 6) {
      const [w, h, n, d, ox, by] = asoma[p];
      st.setProperty('--fw', w + 'px'); st.setProperty('--fh', h + 'px'); st.setProperty('--n', n); st.setProperty('--dur', d + 's');
      st.setProperty('--ox', ox + 'px'); st.setProperty('--by', by + 'px');
      st.backgroundImage = `url("media/peek${p}.png")`; st.backgroundSize = `${w * n}px ${h}px`;
    } else {
      const [w, h] = medidas[p];
      st.setProperty('--fw', w + 'px'); st.setProperty('--fh', h + 'px'); st.setProperty('--n', s.n); st.setProperty('--dur', s.d + 's');
      st.removeProperty('--ox'); st.removeProperty('--by');
      st.backgroundImage = `url("media/p${p}s${e}.png")`; st.backgroundSize = `${w * s.n}px ${h}px`;
    }
    if (punto) punto.style.background = s.c;
    caps.forEach((c) => { c.hidden = Number(c.dataset.c) !== e; });
    document.querySelectorAll('#pet-estados button').forEach((b) => b.setAttribute('aria-pressed', String(Number(b.dataset.e) === e)));
    document.querySelectorAll('#pet-mascotas button').forEach((b) => b.setAttribute('aria-pressed', String(Number(b.dataset.p) === p)));
  };
  document.querySelectorAll('#pet-mascotas button').forEach((b) => b.addEventListener('click', () => { p = Number(b.dataset.p); pintar(); }));
  document.querySelectorAll('#pet-estados button').forEach((b) => b.addEventListener('click', () => { clearTimeout(volver); e = Number(b.dataset.e); pintar(); }));
  // En Castillo, hover y clic usan la misma animación de caricias.
  const acariciar = () => {
    if (e === 5) return;
    const antes = e; e = 5; pintar();
    volver = setTimeout(() => { e = antes; pintar(); }, 3400);
  };
  if (document.body.dataset.pagina === 'castillo') pet.addEventListener('pointerenter', acariciar);
  pet.addEventListener('click', acariciar);
  pintar();
})();

// Recorrido: con la escena fijada, el avance del scroll por la sección elige el estado del notch.
(() => {
  const sim = document.getElementById('probar');
  if (!sim || matchMedia('(prefers-reduced-motion: reduce)').matches) return;
  const orden = ['pet', 'idle', 'music', 'lyrics', 'pomo', 'ollama', 'system', 'charge'];
  const botones = sim.querySelectorAll('.try button');
  let actual = -1;
  addEventListener('scroll', () => {
    if (getComputedStyle(sim.querySelector('.sim-pin')).position !== 'sticky') return;
    const r = sim.getBoundingClientRect(), recorrido = r.height - innerHeight;
    const i = Math.min(orden.length - 1, Math.max(0, Math.floor(-r.top / recorrido * orden.length)));
    if (i === actual) return;
    actual = i;
    const b = [...botones].find((x) => x.dataset.s === orden[i]);
    if (b) b.click();
  }, { passive: true });
})();
