// Recompose the shared storyboard; its script, scene clock and easing stay shared.
(() => {
  const originalRender = window.render;
  const originalReady = window.ready;
  const W = 840, H = 730;
  let events, camera, initialized = false;
  const focus = [
    { scale: 1.3, x: 342, y: 380, box: [138, 196, 30, 624] },
    { scale: 1.28, x: 342, y: 520, box: [355, 330, 40, 604] },
    { scale: 2.45, x: 535, y: 526, box: [495, 65, 448, 186] },
    { scale: 1.36, x: 342, y: 760, box: [700, 100, 40, 604] },
    { scale: 1.2, x: 342, y: 1090, box: [840, 510, 30, 625] },
  ];
  function element(id, parent, html, className = '') {
    const el = document.createElement('div');
    el.id = id; el.className = className; el.innerHTML = html;
    $(parent).appendChild(el);
    return el;
  }
  function configure() {
    $('s4panel').appendChild($('s4ring'));
    element('reel-pages', 's5', '<i></i><i></i><i></i>');
    element('reel-launch', 's6', `<svg viewBox="0 0 24 24">${ICONS.pad}</svg><span>${T.game}</span>`, 'glass');
    element('reel-close', 's6game', '<svg viewBox="0 0 24 24"><path d="m6 6 12 12M18 6 6 18" fill="none"/></svg>');
    element('reel-tabs', 's10', `<div id="reel-activity-tab"><svg viewBox="0 0 24 24">${ICONS.chart}</svg>${LANG === 'es' ? 'Actividad' : 'Activity'}</div><div id="reel-settings-tab"><svg viewBox="0 0 24 24"><path d="m9 3-1 3-3 1v4l-2 1 2 1v4l3 1 1 3h6l1-3 3-1v-4l2-1-2-1V7l-3-1-1-3z"/><circle cx="12" cy="12" r="3"/></svg>${LANG === 'es' ? 'Ajustes' : 'Settings'}</div>`);
    element('reel-tray', 'stage', '<img src="icon.svg">');
    element('reel-ripple', 'stage', '');
    element('reel-cursor', 'stage', '<svg viewBox="0 0 54 68" width="54" height="68"><path d="M3 3v49l12-12 10 23 10-5-10-22h19z" fill="#fff" stroke="#111821" stroke-width="3" stroke-linejoin="round"/></svg>');
    $('fc9').querySelector('.d').innerHTML = T.feats[9][2].replace('istargetsleeping://sleep', 'istargetsleeping:<wbr>//sleep');
    T.pets.forEach(([id], i) => {
      const wrap = $('pw' + i), sp = $('pet' + i), m = META[id];
      wrap.style.left = `${80 + (i % 2) * 440}px`;
      wrap.style.top = `${640 + Math.floor(i / 2) * 465}px`;
      sp._scale = 2.5; sp._row = null;
      sp.style.width = `${m.w * 2.5}px`; sp.style.height = `${m.h * 2.5}px`;
      sp.style.left = `${(400 - m.w * 2.5) / 2}px`;
      sp.style.top = `${325 - m.h * 2.5}px`;
      wrap.firstElementChild.style.top = '300px';
    });
    // A closer taskbar view keeps the original Start logo and app icons.
    const bar = $('s9bar');
    [...bar.children].forEach(el => {
      if (!el.id && !el.classList.contains('tbicon')) el.style.display = 'none';
    });
    bar.querySelectorAll('.tbicon').forEach((el, i) => {
      el.style.left = `${[55, 175, 565, 685, 685][i]}px`;
      if (i === 4) el.style.display = 'none';
    });
  }
  function panelCamera(t) {
    const l = t - 21, idx = clamp(Math.floor((l - 1.2) / 1.85), 0, 4);
    const k = idx ? eInOut(seg(l, 1.2 + idx * 1.85, 1.65 + idx * 1.85)) : 1;
    const a = focus[Math.max(0, idx - 1)], b = focus[idx];
    const sc = lerp(a.scale, b.scale, k);
    const x = clamp(lerp(a.x, b.x, k) - W / (2 * sc), 0, Math.max(0, 684 - W / sc));
    const y = clamp(lerp(a.y, b.y, k) - H / (2 * sc), 0, Math.max(0, $('s4img').naturalHeight / 2 - H / sc));
    camera = { sc, x, y };
    const img = $('s4img');
    img.style.width = `${684 * sc}px`;
    img.style.left = `${-x * sc}px`; img.style.top = `${-y * sc}px`;
    const box = b.box.map((v, i) => lerp(a.box[i], v, k));
    const ring = $('s4ring');
    ring.style.left = `${(box[2] - x) * sc - 6}px`;
    ring.style.top = `${(box[0] - y) * sc - 6}px`;
    ring.style.width = `${box[3] * sc + 12}px`;
    ring.style.height = `${box[1] * sc + 12}px`;
  }
  function featurePages(t) {
    const l = t - 31, starts = [0.8, 3.75, 6.7], ends = [3.85, 6.8, 10.2];
    T.feats.forEach((_, i) => {
      const page = Math.floor(i / 4), local = i % 4;
      const a = starts[page] + local * 0.13;
      const k = eBack(seg(l, a, a + 0.55)), out = seg(l, ends[page] - .2, ends[page] + .12);
      const card = $('fc' + i);
      card.style.left = `${80 + (local % 2) * 440}px`;
      card.style.top = `${650 + Math.floor(local / 2) * 420}px`;
      card.style.opacity = seg(l, a, a + .3) * (1 - out);
      card.style.transform = `translateY(${(1 - k) * 60 - out * 28}px) scale(${lerp(.9, 1, k)})`;
      const wave = Math.exp(-Math.pow((l - starts[page] - 1.4) * 3 - local * .45, 2));
      card.style.borderColor = `rgba(77,163,255,${.09 + wave * .7})`;
      card.style.boxShadow = `0 30px 80px #0008, 0 0 ${wave * 40}px rgba(77,163,255,${wave * .35})`;
    });
    [...$('reel-pages').children].forEach((el, i) => {
      el.style.background = l >= starts[i] && l < ends[i] ? 'var(--blue)' : '#333b46';
    });
    $('reel-pages').style.opacity = seg(l, .8, 1.4);
  }
  function portrait(t) {
    if (t > 20.5 && t < 31.5) panelCamera(t);
    if (t > 30.5 && t < 41.5) featurePages(t);
    const tray = $('reel-tray');
    tray.style.opacity = seg(t, 20.65, 20.95) * (1 - seg(t, 21.35, 21.8));
    $('reel-launch').style.opacity = seg(t, 41.9, 42.3) * (1 - seg(t, 42.6, 42.9));
    if (t > 48 && t < 55) {
      const l = t - 48.5;
      T.toasts.forEach((_, i) => {
        let shift = 0;
        for (let j = i + 1; j < 4; j++) shift += eOut(seg(l, .9 + j, 1.4 + j)) * 196;
        $('to' + i).style.top = `${1320 - shift}px`;
        $('to' + i).style.opacity = eOut(seg(l, .9 + i, 1.4 + i)) * (1 - seg(shift, 392, 588));
      });
    }
    if (t > 60 && t < 69) {
      const l = t - 60.5, i = clamp(Math.floor((l - 1) / 1.2), 0, 5), li = l - 1 - i * 1.2;
      const pet = $('tbpet'), m = META[pet._pet], row = pet._row;
      pet._scale = 3; pet._row = null;
      pet.style.width = `${m.w * 3}px`; pet.style.height = `${m.h * 3}px`;
      setSprite(pet, row, Math.floor(Math.max(0, li) * FPS_SPRITE), row !== 'Hearts');
      pet.style.left = `${500 - m.w * 3 / 2}px`;
      pet.style.top = `${1370 - m.h * 3 + 36 + (1 - eOut(seg(l, .2, .9))) * 160}px`;
    }
    if (t > 68 && t < 72.5) {
      const l = t - 68.5;
      const swap = eInOut(seg(t, 70.25, 70.55));
      $('s10a').style.opacity = eOut(seg(l, .2, .6)) * (1 - swap);
      $('s10s').style.opacity = swap;
      $('s10a').style.transform = `translateY(${(1 - eOut(seg(l, .2, 1))) * 120}px)`;
      $('s10s').style.transform = `translateY(${(1 - swap) * 30}px)`;
      $('s10aimg').style.top = '-130px';
      $('s10simg').style.top = `${-165 - eInOut(seg(t, 70.8, 71.8)) * 170}px`;
      $('reel-tabs').style.opacity = eOut(seg(l, .2, .6));
      $('reel-activity-tab').classList.toggle('selected', t >= 69.1 && t < 70.25);
      $('reel-settings-tab').classList.toggle('selected', t >= 70.25);
    }
  }
  function center(id, fx = .5, fy = .5) {
    const r = $(id).getBoundingClientRect();
    return [r.left + r.width * fx, r.top + r.height * fy];
  }
  function point(target) {
    switch (target) {
      case 'tray': return center('reel-tray');
      case 'free-ram': {
        const r = $('s4panel').getBoundingClientRect();
        return [r.left + (535 - camera.x) * camera.sc, r.top + (526 - camera.y) * camera.sc];
      }
      case 'launch': return center('reel-launch');
      case 'close': return center('reel-close');
      case 'capybara': return center('pet2', .5, .68);
      case 'pet': return center('tbpet', .5, .68);
      case 'activity': return center('reel-activity-tab', .3);
      case 'settings': return center('reel-settings-tab', .3);
      default: throw new Error(`Unknown reel click target: ${target}`);
    }
  }
  function cursor(t) {
    const pointer = $('reel-cursor'), ripple = $('reel-ripple');
    const event = events.find(e => t >= e.time - .7 && t < e.time + (e.hold || .6) + .35);
    pointer.style.opacity = 0; ripple.style.opacity = 0;
    if (!event) return;
    const to = point(event.target), k = eInOut(seg(t, event.time - .7, event.time - .04));
    const x = lerp(event.from[0], to[0], k), y = lerp(event.from[1], to[1], k) - Math.sin(k * Math.PI) * 38;
    const tail = event.time + (event.hold || .6);
    pointer.style.left = `${x - 3}px`; pointer.style.top = `${y - 3}px`;
    pointer.style.opacity = seg(t, event.time - .7, event.time - .48) * (1 - seg(t, tail, tail + .35));
    const down = seg(t, event.time, event.time + .04) * (1 - seg(t, event.time + .08, event.time + .14));
    pointer.style.transform = `scale(${1 - .12 * down})`;
    const r = seg(t, event.time, event.time + .65);
    ripple.style.left = `${to[0]}px`; ripple.style.top = `${to[1]}px`;
    ripple.style.transform = `scale(${lerp(.25, 1.35, eOut(r))})`;
    ripple.style.opacity = t >= event.time ? (1 - r) * .85 : 0;
  }
  window.ready = Promise.all([originalReady, fetch('reel-events.json').then(r => r.json())]).then(([duration, clicks]) => {
    events = clicks;
    configure();
    initialized = true;
    originalRender(0); portrait(0); cursor(0);
    // Read-only helpers used by the frame verifier.
    window.reel = { events, point, safe: { left: 64, top: 170, right: 956, bottom: 1600 } };
    return duration;
  });
  window.render = t => { originalRender(t); if (initialized) { portrait(t); cursor(t); } };
})();
