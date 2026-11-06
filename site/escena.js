// Movimiento de la portada: progreso de scroll por escena (--p), inclinación con el ratón (--mx/--my),
// aparición al entrar en pantalla y vídeos cortos que solo se reproducen mientras se ven.
// Sin librerías. Con prefers-reduced-motion todo queda en su estado final y estático.
(() => {
  const quieto = matchMedia('(prefers-reduced-motion: reduce)').matches;
  const clamp = (v) => Math.min(1, Math.max(0, v));

  // Aparición: lo que lleva .revela entra una vez (y no vuelve a esconderse).
  const reveladas = document.querySelectorAll('.revela');
  if (quieto || !('IntersectionObserver' in window)) reveladas.forEach((e) => e.classList.add('vista'));
  else {
    const io = new IntersectionObserver((es) => es.forEach((e) => { if (e.isIntersecting) { e.target.classList.add('vista'); io.unobserve(e.target); } }), { rootMargin: '0px 0px -8% 0px' });
    reveladas.forEach((e) => io.observe(e));
  }

  // Escenas: --p va de 0 (entra por abajo) a 1 (a media pantalla), suavizado exponencial independiente de los fps.
  const escenas = [...document.querySelectorAll('[data-escena]')].map((el) => ({ el, p: quieto ? 1 : 0, meta: quieto ? 1 : 0, ox: 0, oy: 0, mx: 0, my: 0, visible: false }));
  if (!escenas.length) return;
  const vio = new IntersectionObserver((es) => es.forEach((e) => { const s = escenas.find((x) => x.el === e.target); if (s) s.visible = e.isIntersecting; }));
  escenas.forEach((s) => { vio.observe(s.el); s.el.style.setProperty('--p', s.p); });
  if (quieto) return;

  let ultimo = 0, vivo = false;
  const medir = () => {
    const alto = innerHeight;
    escenas.forEach((s) => {
      const r = s.el.getBoundingClientRect();
      s.meta = clamp((alto - r.top) / (alto * 0.62)); // 1 cuando el borde superior pasa del 38 % de la pantalla
    });
  };
  const paso = (t) => {
    const dt = Math.min(64, t - ultimo) || 16; ultimo = t;
    const k = 1 - Math.exp(-dt / 90); // constante de tiempo 90 ms
    let activo = false;
    escenas.forEach((s) => {
      if (!s.visible) return;
      s.p += (s.meta - s.p) * k; s.mx += (s.ox - s.mx) * k; s.my += (s.oy - s.my) * k;
      if (Math.abs(s.meta - s.p) < 0.001) s.p = s.meta;
      s.el.style.setProperty('--p', s.p.toFixed(3));
      s.el.style.setProperty('--mx', s.mx.toFixed(3));
      s.el.style.setProperty('--my', s.my.toFixed(3));
      if (s.p !== s.meta || Math.abs(s.ox - s.mx) > 0.002 || Math.abs(s.oy - s.my) > 0.002) activo = true;
    });
    vivo = activo; if (activo) requestAnimationFrame(paso);
  };
  const despertar = () => { if (!vivo) { vivo = true; ultimo = performance.now(); requestAnimationFrame(paso); } };
  const alScroll = () => { medir(); despertar(); };
  addEventListener('scroll', alScroll, { passive: true }); addEventListener('resize', alScroll);
  escenas.forEach((s) => {
    s.el.addEventListener('pointermove', (e) => {
      if (e.pointerType === 'touch') return;
      const r = s.el.getBoundingClientRect();
      s.ox = ((e.clientX - r.left) / r.width - 0.5) * 2; s.oy = ((e.clientY - r.top) / r.height - 0.5) * 2; despertar();
    });
    s.el.addEventListener('pointerleave', () => { s.ox = 0; s.oy = 0; despertar(); });
  });
  medir(); despertar();
})();

// Vídeos cortos en bucle dentro de las maquetas: <video data-clip muted loop playsinline data-src="…">.
// Cargan 600 px antes de verse, empiezan al 50 % visible, paran por debajo del 20 % y no arrancan con Save-Data.
(() => {
  const clips = document.querySelectorAll('video[data-clip]');
  if (!clips.length) return;
  const ahorro = matchMedia('(prefers-reduced-motion: reduce)').matches || (navigator.connection && navigator.connection.saveData);
  clips.forEach((v) => {
    v.muted = true;
    const cargar = () => { if (v.dataset.src && !v.src) { v.src = v.dataset.src; v.addEventListener('loadeddata', () => v.classList.add('con-cuadro'), { once: true }); } };
    new IntersectionObserver((es, o) => { if (es.some((e) => e.isIntersecting)) { cargar(); o.disconnect(); } }, { rootMargin: '600px 0px' }).observe(v);
    if (ahorro) return;
    let suena = false;
    new IntersectionObserver((es) => es.forEach((e) => {
      if (!suena && e.intersectionRatio >= 0.5) { suena = true; cargar(); v.play().catch(() => {}); }
      else if (suena && e.intersectionRatio < 0.2) { suena = false; v.pause(); }
    }), { threshold: [0, 0.2, 0.5] }).observe(v);
  });
  document.addEventListener('visibilitychange', () => { if (document.hidden) clips.forEach((v) => v.pause()); });
})();
