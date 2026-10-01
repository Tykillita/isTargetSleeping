# Genera las ilustraciones del README: SVG animados con los sprites reales de las mascotas
# (sacados de `--export-pet`), infografías en vidrio negro y los pósters del video.
#
#   python docs/readme-art.py <carpeta de --export-pet>
#
# Requiere Python 3 con Pillow y numpy; ffmpeg solo para los pósters del video.
# docs/generate-images.ps1 lo llama si encuentra Python.
import base64
import io
import os
import shutil
import subprocess
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
IMAGES = os.path.join(ROOT, 'docs', 'images')
VIDEO = os.path.join(ROOT, 'docs', 'video')
PETS = ['mira', 'llama', 'capybara', 'orange-cat']
SIZES = {'mira': (27, 24), 'llama': (32, 30), 'capybara': (32, 30), 'orange-cat': (32, 30)}
FIXED = ['DeepSleep', 'WakingUp', 'Drowsy', 'Eating', 'Alert', 'Working', 'Downloading', 'Sweeping', 'Yawning', 'Peek',
         'T1', 'T2', 'T3', 'T4', 'T5', 'T6', 'T7']
TAIL = ['Mouse', 'Jump', 'Hearts', 'Sparkle', 'Nap', 'Dizzy', 'Sad', 'WalkL', 'WalkR', 'Reduced']
FONT = "-apple-system, 'Segoe UI', Helvetica, Arial, sans-serif"
INK, BLUE, LIGHT, DEEP = '#f5f5f7', '#4DA3FF', '#4CC2FF', '#1A8FE8'

T = {
    'en': {
        'states_kicker': 'PETS · WHAT THEY DO', 'states_title': 'They act out what Ollama is doing',
        'cols': [('Ollama off', 'sleeps', '#8a8f98'), ('Loading', 'eats', BLUE), ('Generating', 'types', BLUE),
                 ('Downloading', 'carries a box', BLUE), ('Freeing RAM', 'sweeps', '#5CD69A'), ('Petted', 'hearts', '#FF7A9C')],
        'pets': [('Mira', 'watchful hacker'), ('Llama', 'proud & curious'), ('Capybara', 'totally zen'), ('Kitten', 'playful')],
        'home_kicker': 'PETS · BESIDE START', 'home_title': 'Each one sleeps in its own bed, right above Start',
        'beds': ['target capsule', 'woven cushion', 'steaming hot tub', 'wicker basket'],
        'peek_title': 'While it waits for a model, it peeks out from behind the Windows logo',
        'peek': ['peeks with half an eye', 'periscope over the top', 'leans on it, unbothered', 'ears first, tail out'],
        'idle_kicker': 'IDLE DETECTION', 'idle_title': 'No request log needed: the runner’s CPU time tells',
        'idle': [('clock', 'Every 2.5 s', ['Adds up the CPU time', 'of Ollama’s runners', '(GetProcessTimes).']),
                 ('pulse', 'Activity?', ['Grew > 0.08 s, or the', 'loaded model changed.', 'Works on GPU too.']),
                 ('hourglass', 'Idle timer', ['5, 15, 30 or 60 min', 'without generating', '(Settings › Ollama).']),
                 ('moon', 'Asleep', ['Unloads the model;', 'Ollama stays on and', 'reloads it on demand.'])],
        'game_kicker': 'GAME MODE', 'game_title': 'Your game gets the RAM and VRAM; Ollama comes back on its own',
        'game': [('Game opens', 'a visible window from Steam,', 'Epic, Riot, EA, GOG, Xbox…'),
                 ('+5 s', 'Ollama and the other', 'engines turn off'),
                 ('You quit', 'Turn on anyway and Not a', 'game stay in the panel'),
                 ('+30 s', 'only what was on comes', 'back, the same way')],
        'game_band': 'RAM and VRAM free for the game',
        'poster_cta': 'Watch the tour', 'poster_sub': '1:17 · with sound',
    },
    'es': {
        'states_kicker': 'MASCOTAS · QUÉ HACEN', 'states_title': 'Cuentan lo que hace Ollama',
        'cols': [('Ollama apagado', 'duerme', '#8a8f98'), ('Cargando', 'come', BLUE), ('Generando', 'teclea', BLUE),
                 ('Descargando', 'carga una caja', BLUE), ('Liberando RAM', 'barre', '#5CD69A'), ('Caricias', 'corazones', '#FF7A9C')],
        'pets': [('Mira', 'hacker vigilante'), ('Llama', 'orgullosa y curiosa'), ('Capibara', 'totalmente zen'), ('Gatito', 'juguetón')],
        'home_kicker': 'MASCOTAS · JUNTO A INICIO', 'home_title': 'Cada una duerme en su cama, justo encima de Inicio',
        'beds': ['cápsula con diana', 'cojín andino', 'tina humeante', 'cesta de mimbre'],
        'peek_title': 'Mientras espera un modelo, se asoma detrás del logo de Windows',
        'peek': ['se asoma con medio ojo', 'periscopio por arriba', 'se apoya, tan tranquila', 'orejas primero, cola fuera'],
        'idle_kicker': 'DETECCIÓN DE INACTIVIDAD', 'idle_title': 'Sin registro de peticiones: lo dice el CPU del runner',
        'idle': [('clock', 'Cada 2,5 s', ['Suma el tiempo de CPU', 'de los runners de Ollama', '(GetProcessTimes).']),
                 ('pulse', '¿Actividad?', ['Creció > 0,08 s, o', 'cambió el modelo cargado.', 'Vale también en GPU.']),
                 ('hourglass', 'Temporizador', ['5, 15, 30 o 60 min', 'sin generar', '(Ajustes › Ollama).']),
                 ('moon', 'Dormido', ['Descarga el modelo;', 'Ollama sigue encendido', 'y lo recarga al pedirlo.'])],
        'game_kicker': 'MODO JUEGO', 'game_title': 'Tu juego se queda la RAM y la VRAM; Ollama vuelve solo',
        'game': [('Abres un juego', 'ventana visible de Steam,', 'Epic, Riot, EA, GOG, Xbox…'),
                 ('+5 s', 'se apagan Ollama y', 'los otros motores'),
                 ('Sales', 'Encender igual y No es un', 'juego siguen en el panel'),
                 ('+30 s', 'vuelve solo lo que estaba', 'encendido, por el mismo camino')],
        'game_band': 'RAM y VRAM libres para el juego',
        'poster_cta': 'Mira el video', 'poster_sub': '1:17 · con sonido',
    },
}

ICONS = {
    'clock': '<circle cx="12" cy="12" r="8.5"/><path d="M12 7.5V12l3 2"/>',
    'pulse': '<path d="M3 12h4l2.5-5 4 10 2.5-5H21"/>',
    'hourglass': '<path d="M7 3.5h10M7 20.5h10M8 3.5c0 5 8 5 8 8.5s-8 3.5-8 8.5M16 3.5c0 5-8 5-8 8.5s8 3.5 8 8.5"/>',
    'moon': '<path d="M19.5 14.5A8 8 0 1 1 9.5 4.5a6.5 6.5 0 0 0 10 10z"/>',
}


# ------------------------------------------------------------------ sprites

def load_sheet(pet, src):
    """Every row of the 2x dark sheet, as frames at the pet canvas resolution (2 px per pet pixel)."""
    w, h = SIZES[pet]
    im = np.asarray(Image.open(os.path.join(src, f'pet-{pet}-2x-dark.png')).convert('RGBA')).copy()
    bg = (im[:, :, 0] == 0x1C) & (im[:, :, 1] == 0x1C) & (im[:, :, 2] == 0x1C)
    im[bg] = 0
    cw, ch = (w + 6) * 4, (h + 8) * 4
    rows = (im.shape[0] - 4) // (ch + 4)
    names = FIXED + [f'G{i}' for i in range(rows - len(FIXED) - len(TAIL))] + TAIL
    out = {}
    for r, name in enumerate(names):
        y = 4 + r * (ch + 4)
        frames = []
        k = 0
        while 170 + k * (cw + 4) + cw <= im.shape[1]:
            cell = im[y:y + ch, 170 + k * (cw + 4):170 + k * (cw + 4) + cw]
            if not cell[:, :, 3].any():
                break
            frames.append(Image.fromarray(cell[::2, ::2].copy()))   # undo the sheet's 2x zoom
            k += 1
        out[name] = frames
    return out


def load_peek(pet, src):
    im = np.asarray(Image.open(os.path.join(src, f'pet-{pet}-peek.png')).convert('RGBA')).copy()
    teal = (im[:, :, 0] == 0x0C) & (im[:, :, 1] == 0x2A) & (im[:, :, 2] == 0x2E)
    im[teal] = 0
    im = im[::4, ::4]                                              # undo the sheet's 4x zoom
    return [Image.fromarray(im[:, k * 90:(k + 1) * 90].copy()) for k in range(im.shape[1] // 90)]


def loop(frames):
    return frames[:-1] if len(frames) == 13 else frames


def bbox(frames):
    boxes = [f.getbbox() for f in frames if f.getbbox()]
    return (min(b[0] for b in boxes), min(b[1] for b in boxes), max(b[2] for b in boxes), max(b[3] for b in boxes))


class Svg:
    def __init__(self, w, h, title, desc):
        self.w, self.h, self.title, self.desc = w, h, title, desc
        self.body, self.css, self.n = [], [], 0

    def add(self, s):
        self.body.append(s)

    def anim(self, x, y, frames, scale, secs, crop=None):
        """Frames side by side in one PNG; a clipped viewport steps through them with CSS."""
        if crop:
            frames = [f.crop(crop) for f in frames]
        fw, fh = frames[0].size
        strip = Image.new('RGBA', (fw * len(frames), fh))
        for i, f in enumerate(frames):
            strip.paste(f, (i * fw, 0))
        strip = strip.resize((strip.width * scale, fh * scale), Image.NEAREST)
        buf = io.BytesIO()
        strip.save(buf, 'PNG', optimize=True)
        data = base64.b64encode(buf.getvalue()).decode()
        self.n += 1
        W, H, n = fw * scale, fh * scale, len(frames)
        self.css.append(f'.a{self.n}{{animation:k{self.n} {secs}s steps({n}) infinite}}'
                        f'@keyframes k{self.n}{{to{{transform:translateX(-{W * n}px)}}}}')
        self.add(f'<svg x="{x}" y="{y}" width="{W}" height="{H}" viewBox="0 0 {W} {H}" overflow="hidden">'
                 f'<image class="a{self.n}" width="{W * n}" height="{H}" style="image-rendering:pixelated" '
                 f'href="data:image/png;base64,{data}"/></svg>')
        return W, H

    def text(self, x, y, s, size=13, weight=400, fill=INK, opacity=1, anchor='start', spacing=0):
        s = s.replace('&', '&amp;').replace('<', '&lt;')
        self.add(f'<text x="{x}" y="{y}" font-size="{size}" font-weight="{weight}" fill="{fill}" fill-opacity="{opacity}" '
                 f'text-anchor="{anchor}"' + (f' letter-spacing="{spacing}"' if spacing else '') + f'>{s}</text>')

    def frame(self, kicker, title):
        self.add(f'<rect width="{self.w}" height="{self.h}" rx="28" fill="url(#bg)"/>')
        self.add(f'<rect x="1" y="1" width="{self.w - 2}" height="{self.h - 2}" rx="27" fill="url(#glass)" stroke="#fff" stroke-opacity=".14"/>')
        self.text(40, 58, kicker, 12, 600, opacity=.6, spacing=1.6)
        self.text(40, 88, title, 22, 650)

    def card(self, x, y, w, h, rx=18):
        self.add(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="{rx}" fill="#fff" fill-opacity=".045" stroke="#fff" stroke-opacity=".08"/>')

    def save(self, path):
        css = '\n'.join(self.css)
        out = (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {self.w} {self.h}" width="{self.w}" height="{self.h}" '
               f'role="img" aria-labelledby="t d" font-family="{FONT}">\n'
               f'  <title id="t">{self.title}</title>\n  <desc id="d">{self.desc}</desc>\n'
               f'  <style>\n{css}\n@media (prefers-reduced-motion: reduce){{image{{animation:none!important}}}}\n</style>\n'
               '  <defs>\n'
               '    <linearGradient id="bg" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#08090b"/><stop offset=".55" stop-color="#14171c"/><stop offset="1" stop-color="#090b0d"/></linearGradient>\n'
               '    <linearGradient id="glass" x1="0" y1="0" x2=".9" y2="1"><stop offset="0" stop-color="#fff" stop-opacity=".09"/><stop offset=".5" stop-color="#fff" stop-opacity=".025"/><stop offset="1" stop-color="#b9c7ca" stop-opacity=".06"/></linearGradient>\n'
               '    <linearGradient id="bar" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#26272c"/><stop offset="1" stop-color="#1b1c20"/></linearGradient>\n'
               '  </defs>\n  ' + '\n  '.join(self.body) + '\n</svg>\n')
        with open(path, 'w', encoding='utf-8', newline='\n') as f:
            f.write(out)
        print(f'{os.path.relpath(path, ROOT)}: {len(out) // 1024} KB')


def windows_logo(svg, x, y, s, lit=False):
    """The Start logo as the peek sheets draw it: two light panes on top, two deeper below."""
    p, g = 11 * s, s
    for i, (dx, dy) in enumerate(((0, 0), (p + g, 0), (0, p + g), (p + g, p + g))):
        svg.add(f'<rect x="{x + dx}" y="{y + dy}" width="{p}" height="{p}" fill="{LIGHT if i < 2 else DEEP}"/>')


# ------------------------------------------------------------------ pets: states grid

def pets_states(sheets, lang):
    t = T[lang]
    col_w, left, scale = 132, 150, 2
    w, row_h, top = 960, 150, 168
    h = top + row_h * 4 + 24
    svg = Svg(w, h, t['states_title'], ' · '.join(f'{a}: {b}' for a, b, _ in t['cols']))
    svg.frame(t['states_kicker'], t['states_title'])
    rows = [('DeepSleep', 2.6), ('Eating', 1.5), ('Working', 1.3), ('Downloading', 1.6), ('Sweeping', 1.4), ('Hearts', 1.7)]
    for c, (state, action, color) in enumerate(t['cols']):
        cx = left + c * col_w + col_w // 2
        svg.add(f'<circle cx="{cx - 6 - len(state) * 3.1:.1f}" cy="{125}" r="3.5" fill="{color}"/>')
        svg.text(cx + 2, 129, state, 12, 600, opacity=.62, anchor='middle')
        svg.text(cx, 150, action, 15, 650, anchor='middle')
    for r, pet in enumerate(PETS):
        y0 = top + r * row_h
        svg.card(24, y0 + 6, w - 48, row_h - 12, 20)
        name, trait = t['pets'][r]
        svg.text(44, y0 + row_h // 2 - 2, name, 18, 650)
        svg.text(44, y0 + row_h // 2 + 20, trait, 13, opacity=.62)
        frames = {k: (loop(sheets[pet][k]) if k != 'Hearts' else sheets[pet][k]) for k, _ in rows}
        box = bbox([f for v in frames.values() for f in v])
        bw, bh = (box[2] - box[0]) * scale, (box[3] - box[1]) * scale
        for c, (key, secs) in enumerate(rows):
            x = left + c * col_w + (col_w - bw) // 2
            y = y0 + row_h - 14 - bh
            svg.anim(x, y, frames[key], scale, secs, crop=box)
    svg.save(os.path.join(IMAGES, f'pets-states-{lang}.svg'))


# ------------------------------------------------------------------ pets: home beside Start

def pets_home(sheets, peeks, lang):
    t = T[lang]
    w, h = 960, 612
    svg = Svg(w, h, t['home_title'], t['peek_title'])
    svg.frame(t['home_kicker'], t['home_title'])
    scale, cw = 2, 222
    # Row 1: asleep in its bed, sitting on the taskbar right above Start
    for i, pet in enumerate(PETS):
        x0 = 30 + i * (cw + 3)
        svg.card(x0, 110, cw - 6, 238)
        bar_y = 110 + 128
        svg.add(f'<rect x="{x0 + 1}" y="{bar_y}" width="{cw - 8}" height="56" fill="url(#bar)"/>')
        svg.add(f'<rect x="{x0 + 1}" y="{bar_y}" width="{cw - 8}" height="1" fill="#fff" fill-opacity=".1"/>')
        lx = x0 + (cw - 6) // 2 - 23
        windows_logo(svg, lx, bar_y + 5, 2)
        frames = loop(sheets[pet]['DeepSleep'])
        box = bbox(frames)
        bw, bh = (box[2] - box[0]) * scale, (box[3] - box[1]) * scale
        svg.anim(x0 + (cw - 6 - bw) // 2, bar_y + 2 - bh, frames, scale, 2.6, crop=box)
        svg.text(x0 + 18, 110 + 238 - 34, t['pets'][i][0], 14, 650)
        svg.text(x0 + 18, 110 + 238 - 15, t['beds'][i], 12, opacity=.62)
    # Row 2: hiding behind the logo inside the taskbar, from the app's own peek sheets
    svg.text(40, 392, t['peek_title'], 17, 650)
    for i, pet in enumerate(PETS):
        x0 = 30 + i * (cw + 3)
        svg.card(x0, 410, cw - 6, 178)
        fx, fy = x0 + (cw - 6 - 180) // 2, 414
        bar_y = fy + 6 * scale
        svg.add(f'<rect x="{x0 + 1}" y="{bar_y}" width="{cw - 8}" height="{48 * scale}" fill="url(#bar)"/>')
        svg.add(f'<rect x="{x0 + 1}" y="{bar_y}" width="{cw - 8}" height="1" fill="#fff" fill-opacity=".1"/>')
        if pet in peeks:
            # Nothing is drawn below the taskbar: the screen ends there.
            svg.anim(fx, fy, peeks[pet], scale, 3.6, crop=(0, 0, 90, 54))
        else:
            # The capybara doesn't hide: she leans on the logo, dozing.
            frames = loop(sheets[pet]['Drowsy'])
            box = bbox(frames)
            bw, bh = (box[2] - box[0]) * scale, (box[3] - box[1]) * scale
            lx, ly = fx + 50 * scale, fy + 18 * scale
            svg.anim(lx - bw + 4 * scale, ly + 23 * scale - bh + 2 * scale, frames, scale, 2.6, crop=box)
            windows_logo(svg, lx, ly, scale)
        svg.text(x0 + 18, 410 + 178 - 34, t['pets'][i][0], 14, 650)
        svg.text(x0 + 18, 410 + 178 - 15, t['peek'][i], 12, opacity=.62)
    svg.save(os.path.join(IMAGES, f'pets-home-{lang}.svg'))


# ------------------------------------------------------------------ infographics

def icon(svg, x, y, name):
    svg.add(f'<rect x="{x}" y="{y}" width="38" height="38" rx="12" fill="#fff" fill-opacity=".07"/>')
    svg.add(f'<g transform="translate({x + 7} {y + 7})" fill="none" stroke="{INK}" stroke-opacity=".85" stroke-width="1.7" '
            f'stroke-linecap="round" stroke-linejoin="round">{ICONS[name]}</g>')


def idle_flow(lang):
    t = T[lang]
    svg = Svg(960, 330, t['idle_title'], ' → '.join(s[1] for s in t['idle']))
    svg.frame(t['idle_kicker'], t['idle_title'])
    for i, (ic, head, lines) in enumerate(t['idle']):
        x = 40 + i * 232
        svg.card(x, 116, 196, 176)
        icon(svg, x + 18, 134, ic)
        svg.text(x + 176, 158, str(i + 1), 13, 600, opacity=.4, anchor='end')
        svg.text(x + 18, 200, head, 16, 600, fill=BLUE if i == 3 else INK)
        for k, line in enumerate(lines):
            svg.text(x + 18, 226 + k * 19, line, 13, fill='#ebebf5', opacity=.66)
        if i < 3:
            svg.add(f'<path d="M{x + 202} 204h24m-7-6 7 6-7 6" fill="none" stroke="#fff" stroke-opacity=".4" stroke-width="1.7" '
                    'stroke-linecap="round" stroke-linejoin="round"/>')
    svg.save(os.path.join(IMAGES, f'idle-flow-{lang}.svg'))


def game_mode(lang):
    t = T[lang]
    svg = Svg(960, 330, t['game_title'], ' → '.join(e[0] for e in t['game']))
    svg.frame(t['game_kicker'], t['game_title'])
    xs = [80, 330, 600, 860]
    line_y = 152
    svg.add(f'<rect x="{xs[1]}" y="{line_y - 16}" width="{xs[3] - xs[1]}" height="32" rx="16" fill="{BLUE}" fill-opacity=".12" stroke="{BLUE}" stroke-opacity=".35"/>')
    svg.text((xs[1] + xs[2]) // 2, line_y + 5, t['game_band'], 13, 600, fill=BLUE, anchor='middle')
    svg.add(f'<path d="M{xs[0]} {line_y}H{xs[1]}" stroke="#fff" stroke-opacity=".25" stroke-width="2" stroke-dasharray="2 6" stroke-linecap="round"/>')
    for i, (head, a, b) in enumerate(t['game']):
        x = xs[i]
        on = i in (0, 3)
        color = BLUE if i == 3 else ('#8a8f98' if i == 1 else INK)
        svg.add(f'<circle cx="{x}" cy="{line_y}" r="9" fill="#0b0c0f" stroke="{color}" stroke-width="2.5"/>')
        if i in (1, 3):
            svg.add(f'<circle cx="{x}" cy="{line_y}" r="3.5" fill="{color}"/>')
        anchor = 'start' if i == 0 else 'end' if i == 3 else 'middle'
        tx = x - 10 if i == 0 else x + 10 if i == 3 else x
        svg.text(tx, 210, head, 16, 650, fill=color if i else INK, anchor=anchor)
        svg.text(tx, 236, a, 13, fill='#ebebf5', opacity=.66, anchor=anchor)
        svg.text(tx, 255, b, 13, fill='#ebebf5', opacity=.66, anchor=anchor)
    # Ollama on/off chips under the line
    for x, label, color in ((xs[0], 'Ollama ON', BLUE), (xs[1] + 60, 'Ollama OFF', '#8a8f98'), (xs[3] - 70, 'Ollama ON', BLUE)):
        svg.add(f'<rect x="{x - 2}" y="282" width="94" height="24" rx="12" fill="#fff" fill-opacity=".06" stroke="#fff" stroke-opacity=".1"/>')
        svg.add(f'<circle cx="{x + 12}" cy="294" r="3.5" fill="{color}"/>')
        svg.text(x + 22, 298, label, 11, 600, opacity=.8, spacing=.6)
    svg.save(os.path.join(IMAGES, f'game-mode-{lang}.svg'))


# ------------------------------------------------------------------ video posters

def posters():
    if not shutil.which('ffmpeg'):
        print('ffmpeg not found: skipping video posters')
        return
    for lang in ('en', 'es'):
        mp4 = os.path.join(VIDEO, f'isTargetSleeping-tour-{lang}.mp4')
        if not os.path.exists(mp4):
            continue
        frame = subprocess.run(['ffmpeg', '-v', 'error', '-ss', '58.6', '-i', mp4, '-frames:v', '1', '-f', 'image2pipe',
                                '-c:v', 'png', '-'], capture_output=True, check=True).stdout
        im = Image.open(io.BytesIO(frame)).convert('RGB').resize((1280, 720), Image.LANCZOS)
        # A pill at the bottom: play icon, call to action and length, sized to its text.
        try:
            from PIL import ImageFont
            bold = ImageFont.truetype('segoeuib.ttf', 30)
            reg = ImageFont.truetype('segoeui.ttf', 24)
        except OSError:
            bold = reg = None
        layer = Image.new('RGBA', im.size, (0, 0, 0, 0))
        d = ImageDraw.Draw(layer)
        cta, sub = T[lang]['poster_cta'], T[lang]['poster_sub']
        w = 64 + 16 + d.textlength(cta, font=bold) + 18 + d.textlength(sub, font=reg) + 34
        x0, y0, y1 = (1280 - w) / 2, 600, 676
        glow = Image.new('L', im.size, 0)
        ImageDraw.Draw(glow).rounded_rectangle((x0, y0, x0 + w, y1), 38, fill=150)
        im.paste((77, 163, 255), (0, 0), glow.filter(ImageFilter.GaussianBlur(26)))
        d.rounded_rectangle((x0, y0, x0 + w, y1), 38, fill=(10, 11, 14, 235), outline=(77, 163, 255, 160), width=2)
        d.ellipse((x0 + 12, y0 + 12, x0 + 64, y0 + 64), fill=(77, 163, 255, 255))
        d.polygon([(x0 + 32, y0 + 26), (x0 + 32, y0 + 50), (x0 + 52, y0 + 38)], fill=(255, 255, 255, 255))
        d.text((x0 + 80, y0 + 17), cta, font=bold, fill=(245, 245, 247, 255))
        d.text((x0 + 80 + d.textlength(cta, font=bold) + 18, y0 + 22), sub, font=reg, fill=(160, 166, 176, 255))
        im = Image.alpha_composite(im.convert('RGBA'), layer).convert('RGB')
        out = os.path.join(IMAGES, f'video-poster-{lang}.jpg')
        im.save(out, quality=88, optimize=True, progressive=True)
        print(f'{os.path.relpath(out, ROOT)}: {os.path.getsize(out) // 1024} KB')


def main():
    src = sys.argv[1]
    sheets = {p: load_sheet(p, src) for p in PETS}
    peeks = {p: load_peek(p, src) for p in PETS if os.path.exists(os.path.join(src, f'pet-{p}-peek.png'))}
    for lang in ('en', 'es'):
        pets_states(sheets, lang)
        pets_home(sheets, peeks, lang)
        idle_flow(lang)
        game_mode(lang)
    posters()


if __name__ == '__main__':
    main()
