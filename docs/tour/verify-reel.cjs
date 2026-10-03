// Verify the portrait storyboard at phone-safe bounds in both languages.
const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const { once } = require('node:events');
const puppeteer = require('puppeteer-core');
const root = path.resolve(__dirname, '../../obj/tour-work/reel');
const types = { '.html': 'text/html', '.css': 'text/css', '.js': 'text/javascript', '.png': 'image/png', '.json': 'application/json', '.svg': 'image/svg+xml' };
const failures = [];
const server = http.createServer((req, res) => {
  const file = path.resolve(root, decodeURIComponent(new URL(req.url, 'http://localhost').pathname).slice(1) || 'index.html');
  if (!file.startsWith(root + path.sep)) { res.writeHead(403); res.end(); return; }
  fs.readFile(file, (error, bytes) => {
    if (error) { res.writeHead(404); res.end(); failures.push(file); return; }
    res.writeHead(200, { 'Content-Type': types[path.extname(file)] || 'application/octet-stream' }); res.end(bytes);
  });
});
(async () => {
  let browser;
  try {
    server.listen(0, '127.0.0.1'); await once(server, 'listening');
    browser = await puppeteer.launch({ executablePath: process.argv[2] || 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true });
    const page = await browser.newPage();
    await page.setViewport({ width: 1080, height: 1920, deviceScaleFactor: 1 });
    page.on('pageerror', error => failures.push(error.message));
    for (const lang of ['es', 'en']) {
      await page.goto(`http://127.0.0.1:${server.address().port}/?lang=${lang}`, { waitUntil: 'networkidle0' });
      if (await page.evaluate(() => window.ready) !== 77) failures.push('Duration changed');
      const result = await page.evaluate(() => {
        const errors = new Set();
        const visible = el => {
          let opacity = 1;
          for (let p = el; p; p = p.parentElement) {
            const s = getComputedStyle(p);
            if (s.display === 'none') return false;
            opacity *= Number(s.opacity);
          }
          return opacity > .8;
        };
        const selectors = '.scene > .h1, .scene > .h2, .scene > .kicker, .scene > .p, #s3big, #tag1, .callout .n, .callout .t, .callout .d, .fcard .t, .fcard .d, .toast .tt, .toast .td, #s8pets > div > div:last-child, #s9state > div, #s11pills, #s11url, #s11by, #s2mem, #s2press, #s2model2, #s2gb, #s2idle, #s3timer, #s3model2, #s3gb, #s6state, #s6sub, #s6gname, #s6vram > div:first-child, #reel-tabs';
        const safe = window.reel.safe;
        for (let frame = 0; frame < 770; frame++) {
          const t = frame / 10;
          window.render(t);
          for (const el of document.querySelectorAll(selectors)) {
            if (!visible(el) || !el.textContent.trim()) continue;
            const range = document.createRange(); range.selectNodeContents(el);
            const r = range.getBoundingClientRect();
            const label = `${el.id || el.className}: ${el.textContent.slice(0, 42)}`;
            if (r.left < safe.left || r.right > safe.right || r.top < safe.top || r.bottom > safe.bottom) {
              errors.add(`Outside safe area at ${t}s: ${label} [${[r.left,r.top,r.right,r.bottom].map(Math.round)}]`);
            }
            const card = el.closest('.fcard');
            if (card && r.bottom > card.getBoundingClientRect().bottom - 18) errors.add(`Feature card overflow: ${label}`);
          }
          for (const [heading, body] of [['s2t','s2card'], ['s3t','s3timer'], ['s6t','s6p'], ['s7t','s7p'], ['s10t','s10p'], ['s10p','reel-tabs']]) {
            const a = document.getElementById(heading), b = document.getElementById(body);
            if (visible(a) && visible(b) && a.getBoundingClientRect().bottom > b.getBoundingClientRect().top - 8) errors.add(`Overlapping ${heading}/${body} at ${t}s`);
          }
        }
        for (const event of window.reel.events) {
          window.render(event.time);
          const [x,y] = window.reel.point(event.target);
          if (x < safe.left || x > safe.right || y < safe.top || y > safe.bottom) errors.add(`Click outside safe area: ${event.target} [${x},${y}]`);
        }
        return [...errors];
      });
      failures.push(...result.map(error => `${lang}: ${error}`));
      console.log(`${lang}: checked 770 storyboard times and 8 mouse actions`);
    }
    if (failures.length) throw new Error(failures.slice(0, 25).join('\n') + `\n${failures.length} failures`);
    console.log('PASS: bilingual layout, text bounds, feature cards, click targets, scene duration and assets');
  } finally { if (browser) await browser.close(); server.close(); }
})().catch(error => { console.error(error.message); process.exitCode = 1; });
