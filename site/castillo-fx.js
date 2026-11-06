// Títulos de las secciones nuevas: cada palabra va en su propio span (--i) para que el CSS la anime con el scroll. Si el idioma cambia, site.js repone el texto plano y el título se ve entero.
(() => {
  if (matchMedia('(prefers-reduced-motion: reduce)').matches || !CSS.supports('animation-timeline: view()')) return;
  document.querySelectorAll('.cx-control .cabecera h2, .cx-memoria .cabecera h2, .cx-agentes .cabecera h2, .cx-novedades .cabecera h2, .mas .cabecera h2').forEach((h2) => {
    if (h2.children.length) return;
    const palabras = h2.textContent.trim().split(/\s+/);
    h2.replaceChildren(...palabras.flatMap((p, i) => {
      const s = document.createElement('span');
      s.className = 'cx-pal'; s.style.setProperty('--i', i); s.textContent = p;
      return i ? [' ', s] : [s];
    }));
  });
})();

// Detalles de movimiento de castillo.html: el foco de luz y la inclinación 3D de las tarjetas siguen al cursor. Solo con ratón y sin movimiento reducido.
(() => {
  if (!matchMedia('(hover: hover) and (pointer: fine)').matches || matchMedia('(prefers-reduced-motion: reduce)').matches) return;
  let activa = null, raf = 0, ev = null;
  const pinta = () => {
    raf = 0;
    if (!activa || !ev) return;
    const r = activa.getBoundingClientRect(), x = ev.clientX - r.left, y = ev.clientY - r.top;
    activa.style.setProperty('--mx', `${x}px`);
    activa.style.setProperty('--my', `${y}px`);
    if (activa.classList.contains('cx-card')) {
      activa.style.setProperty('--ry', `${((x / r.width) - 0.5) * 5}deg`);
      activa.style.setProperty('--rx', `${(0.5 - (y / r.height)) * 4}deg`);
    }
  };
  document.addEventListener('pointermove', (e) => {
    const c = e.target.closest?.('.mas-tarjeta, .cx-pasos li');
    if (activa && activa !== c) { activa.style.removeProperty('--rx'); activa.style.removeProperty('--ry'); }
    activa = c; ev = e;
    if (c && !raf) raf = requestAnimationFrame(pinta);
  }, { passive: true });
  document.addEventListener('pointerleave', () => { if (activa) { activa.style.removeProperty('--rx'); activa.style.removeProperty('--ry'); activa = null; } }, true);
})();

// Haz de luz del notch: varias capas de la misma línea con largo creciente y poca opacidad; el frente (donde se suman todas) queda nítido y la cola se va desvaneciendo,
// como las 48 capas del haz de la app. Cada capa mueve su stroke-dashoffset con la API de animaciones (sin tocar el scroll); el color cambia con la sección bajo el notch.
(() => {
  const caja = document.querySelector('.nav .contenedor');
  if (!caja || matchMedia('(prefers-reduced-motion: reduce)').matches) return;
  const NS = 'http://www.w3.org/2000/svg', CAPAS = 22, COLA = 46, VUELTA = 5600;
  const svg = document.createElementNS(NS, 'svg');
  svg.setAttribute('class', 'cx-haz'); svg.setAttribute('aria-hidden', 'true');
  const grupo = (ancho, op) => {
    const g = document.createElementNS(NS, 'g'); g.setAttribute('stroke-width', ancho);
    for (let i = 0; i < CAPAS; i++) {
      const p = document.createElementNS(NS, 'path'), d = COLA * (i + 1) / CAPAS;
      p.setAttribute('pathLength', '100'); p.setAttribute('stroke-opacity', op); p.setAttribute('stroke-dasharray', `${d.toFixed(2)} 400`);
      p.dataset.d = d; g.append(p);
    }
    svg.append(g); return g;
  };
  grupo(5, .035); grupo(2, .11);   // un velo ancho y suave detrás de la línea fina
  const paths = [...svg.querySelectorAll('path')];
  caja.append(svg);
  let anims = [];
  const dibuja = () => {
    const r = caja.getBoundingClientRect(), cs = getComputedStyle(caja), W = r.width, H = r.height;
    const e = parseFloat(getComputedStyle(caja.closest('.nav')).getPropertyValue('--e')) || 24;
    const rad = Math.min(parseFloat(cs.borderBottomLeftRadius) || 28, H / 2, W / 2);
    if (!W || !H) return;
    // oreja derecha → pared derecha → esquina → borde de abajo → esquina → pared izquierda → oreja izquierda (el haz no pasa por arriba)
    const d = `M${W + e} 0A${e} ${e} 0 0 0 ${W} ${e}L${W} ${H - rad}A${rad} ${rad} 0 0 1 ${W - rad} ${H}L${rad} ${H}A${rad} ${rad} 0 0 1 0 ${H - rad}L0 ${e}A${e} ${e} 0 0 0 ${-e} 0`;
    svg.setAttribute('viewBox', `${-e - 4} -4 ${W + 2 * e + 8} ${H + 8}`);
    svg.setAttribute('width', W + 2 * e + 8); svg.setAttribute('height', H + 8); svg.setAttribute('preserveAspectRatio', 'none');
    Object.assign(svg.style, { width: `${W + 2 * e + 8}px`, height: `${H + 8}px`, maxWidth: 'none' });   // site.css limita los svg a max-width: 100%: encogería el trazo y el haz quedaría dentro del notch
    svg.style.left = `${-e - 4}px`; svg.style.top = '-4px';
    const ritmos = anims.map((a) => a.currentTime);
    anims.forEach((a) => a.cancel());
    anims = paths.map((p, i) => {
      p.setAttribute('d', d);
      const dd = +p.dataset.d;
      const a = p.animate([{ strokeDashoffset: dd }, { strokeDashoffset: dd - (100 + COLA + 4), offset: .74 }, { strokeDashoffset: dd - (100 + COLA + 4) }], { duration: VUELTA, iterations: Infinity, easing: 'linear' });
      if (ritmos[i] != null) a.currentTime = ritmos[i];   // al cambiar el tamaño sigue en la misma vuelta
      return a;
    });
  };
  new ResizeObserver(dibuja).observe(caja);
  document.fonts?.ready.then(dibuja);
  // Color: el acento de la sección que está bajo el notch (cambia suave con transition: color)
  const COLORES = { inicio: '#a9b7ff', widgets: '#8fb4e0', vistas: '#8fb4e0', probar: '#9fb6ff', video: '#9fb6ff', funciones: '#7d9bbf', control: '#6fa8a0', memoria: '#8a93d6', agentes: '#c9a877', mas: '#b393b8', barra: '#9fb6ff', mascotas: '#9fb6ff', novedades: '#c98d86', privacidad: '#8fb08f', instalar: '#7d9bbf', automatizar: '#7d9bbf', 'codigo-abierto': '#9fb6ff', preguntas: '#9fb6ff' };
  const secs = Object.keys(COLORES).map((id) => document.getElementById(id)).filter(Boolean);
  let actual = '', raf = 0;
  const color = () => {
    raf = 0;
    const y = innerHeight * .35, s = secs.find((el) => { const r = el.getBoundingClientRect(); return r.top <= y && r.bottom > y; });
    if (s && s.id !== actual) { actual = s.id; svg.style.color = COLORES[s.id]; }
  };
  addEventListener('scroll', () => { if (!raf) raf = requestAnimationFrame(color); }, { passive: true });
  color();
})();
