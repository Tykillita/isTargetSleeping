// Deterministic frame sampling of the presentation at its original 30 fps.
const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const { spawn } = require('node:child_process');
const { once } = require('node:events');
const puppeteer = require('puppeteer-core');

const config = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
const FPS = 30;
const types = { '.html': 'text/html; charset=utf-8', '.css': 'text/css; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.png': 'image/png', '.json': 'application/json', '.svg': 'image/svg+xml' };
const errors = [];
const server = http.createServer((request, response) => {
  const relative = decodeURIComponent(new URL(request.url, 'http://localhost').pathname).slice(1);
  const file = path.resolve(config.root, relative || 'index.html');
  if (!file.startsWith(path.resolve(config.root) + path.sep)) {
    response.writeHead(403); response.end(); return;
  }
  fs.readFile(file, (error, data) => {
    if (error) { response.writeHead(404); response.end(); errors.push(relative); return; }
    response.writeHead(200, { 'Content-Type': types[path.extname(file)] || 'application/octet-stream' });
    response.end(data);
  });
});

(async () => {
  let browser, encoder;
  try {
    server.listen(0, '127.0.0.1');
    await once(server, 'listening');
    browser = await puppeteer.launch({ executablePath: config.chrome, headless: true,
      args: ['--hide-scrollbars', '--force-color-profile=srgb', '--font-render-hinting=none'] });
    const page = await browser.newPage();
    await page.setViewport({ width: config.width || 1920, height: config.height || 1080, deviceScaleFactor: 1 });
    page.on('pageerror', error => errors.push(error.message));
    await page.goto(`http://127.0.0.1:${server.address().port}/index.html?lang=${config.lang}`, { waitUntil: 'networkidle0' });
    const duration = await page.evaluate(() => window.ready);
    if (duration !== 77) throw new Error('The original presentation must last exactly 77 seconds.');
    if (errors.length) throw new Error(`Presentation assets failed: ${errors.join(', ')}`);
    fs.mkdirSync(config.review, { recursive: true });
    if (config.stills) {
      for (const time of config.stills) {
        await page.evaluate(t => window.render(t), time);
        await page.screenshot({ path: path.join(config.review, `${config.lang}-${time}.png`) });
      }
      return;
    }
    // PNG frames avoid an intermediate JPEG generation. Preserve the original
    // compressed soundtrack verbatim, including its fades and effect timing.
    encoder = spawn(config.ffmpeg, ['-hide_banner', '-loglevel', 'error', '-y',
      '-f', 'image2pipe', '-framerate', String(FPS), '-c:v', 'png', '-i', '-',
      '-i', config.audio, '-map', '0:v:0', '-map', '1:a:0',
      '-c:v', 'libx264', '-preset', 'slow', '-crf', '16', '-threads', '4',
      '-pix_fmt', 'yuv420p', '-tune', 'animation', '-c:a', 'copy',
      '-t', String(duration), '-movflags', '+faststart',
      '-metadata', `title=isTargetSleeping — ${config.title ? config.title + ' — ' : ''}${config.lang.toUpperCase()}`,
      config.output], { stdio: ['pipe', 'ignore', 'inherit'] });
    const complete = once(encoder, 'close');
    encoder.stdin.on('error', error => errors.push(error.message));
    const frames = duration * FPS;
    const reviewFrames = new Set((config.reviewTimes || [4.5, 10, 18.5, 23, 25, 27, 29, 35, 38, 44.5, 47.5, 52, 56, 58, 59.5,
      62, 63.5, 65, 66.5, 67.5, 70.5, 75]).map(t => Math.round(t * FPS)));
    for (let i = 0; i < frames; i++) {
      await page.evaluate(t => window.render(t), i / FPS);
      const frame = await page.screenshot({ type: 'png', optimizeForSpeed: true });
      if (encoder.exitCode !== null || errors.length) throw new Error(`Encoder failed: ${errors.join(', ')}`);
      if (!encoder.stdin.write(frame)) await once(encoder.stdin, 'drain');
      if (reviewFrames.has(i)) fs.writeFileSync(path.join(config.review, `${config.lang}-${i / FPS}.png`), frame);
      if (i === 135) {
        await page.screenshot({ path: config.poster, type: 'jpeg', quality: 98 });
      }
      if (i % 150 === 0) console.log(`${config.lang}: ${i}/${frames} frames`);
    }
    encoder.stdin.end();
    const [code] = await complete;
    if (code !== 0) throw new Error(`FFmpeg exited with ${code}`);
    console.log(`${config.lang}: 2310 frames, supplied audio preserved`);
  } finally {
    if (encoder && encoder.exitCode === null) encoder.kill();
    if (browser) await browser.close();
    server.close();
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
