(function() {
  const state = {
    script: localStorage.getItem('tp_script') || '',
    speed: parseInt(localStorage.getItem('tp_speed')) || 25,
    running: false,
    lastFrame: 0
  };

  const el = {
    input: document.getElementById('script-input'),
    display: document.getElementById('display-text'),
    viewer: document.getElementById('viewer'),
    btnPlay: document.getElementById('btn-play'),
    btnReset: document.getElementById('btn-reset'),
    btnToggle: document.getElementById('toggle-edit'),
    editor: document.getElementById('editor'),
    speedInput: document.getElementById('speed'),
    speedVal: document.getElementById('speed-val'),
    progress: document.getElementById('progress')
  };

  function updateDisplay() {
    el.display.textContent = state.script || 'El texto aparecerá aquí...';
    localStorage.setItem('tp_script', state.script);
  }

  function updateProgress() {
    const end = el.viewer.scrollHeight - el.viewer.clientHeight;
    const progress = end > 0 ? (el.viewer.scrollTop / end) * 100 : 0;
    el.progress.style.width = `${Math.max(0, Math.min(100, progress))}%`;
  }

  function resetPosition() {
    el.viewer.scrollTop = 0;
    updateProgress();
  }

  function animate(now) {
    if (!state.running) return;

    if (!state.lastFrame) {
      state.lastFrame = now;
      requestAnimationFrame(animate);
      return;
    }

    const dt = Math.min((now - state.lastFrame) / 1000, 1 / 30);
    state.lastFrame = now;

    const end = el.viewer.scrollHeight - el.viewer.clientHeight;
    if (end <= 0) {
      state.running = false;
      el.btnPlay.textContent = 'Reproducir';
      return;
    }

    if (el.viewer.scrollTop + state.speed * dt >= end) {
      resetPosition();
    } else {
      el.viewer.scrollTop += state.speed * dt;
    }

    updateProgress();
    requestAnimationFrame(animate);
  }

  // --- Events ---

  el.btnPlay.onclick = () => {
    state.running = !state.running;
    el.btnPlay.textContent = state.running ? 'Pausar' : 'Reproducir';
    if (state.running) {
      state.lastFrame = 0;
      requestAnimationFrame(animate);
    }
  };

  el.btnReset.onclick = resetPosition;

  el.btnToggle.onclick = () => {
    el.editor.classList.toggle('hidden');
    el.btnToggle.textContent = el.editor.classList.contains('hidden') ? '⚙️' : '✖️';
  };

  el.input.oninput = (e) => {
    state.script = e.target.value;
    updateDisplay();
  };

  el.speedInput.oninput = (e) => {
    state.speed = parseInt(e.target.value);
    el.speedVal.textContent = state.speed;
    localStorage.setItem('tp_speed', state.speed);
  };

  // Initialize
  el.input.value = state.script;
  el.speedInput.value = state.speed;
  el.speedVal.textContent = state.speed;
  updateDisplay();

  // Accessibility: Reduced Motion
  const motionQuery = window.matchMedia('(prefers-reduced-motion: reduce)');
  if (motionQuery.matches) {
    state.running = false;
    el.btnPlay.disabled = true;
    el.btnPlay.textContent = 'Desactivado (Motion)';
  }

  motionQuery.addEventListener('change', (e) => {
    if (e.matches) {
      state.running = false;
      el.btnPlay.disabled = true;
      el.btnPlay.textContent = 'Desactivado (Motion)';
    } else {
      el.btnPlay.disabled = false;
      el.btnPlay.textContent = 'Reproducir';
    }
  });

  el.viewer.onscroll = updateProgress;
})();
