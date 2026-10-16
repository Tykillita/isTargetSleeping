"""Convierte Material Icon Theme (MIT, https://github.com/material-extensions/vscode-material-icon-theme) en
src/IsTargetSleeping/Resources/FileIcons.json.gz, el archivo que lee el árbol del widget Código.

Uso:  python tools/build-file-icons.py <carpeta del paquete npm «material-icon-theme» descomprimido>
      (la que contiene icons/ y dist/material-icons.json)

Solo hace falta para actualizar los iconos; la app no ejecuta nada de esto. Cada SVG se reduce a una lista de formas
(trazado + relleno/contorno + transformación): los degradados se sustituyen por la media de sus colores y los recortes
(clipPath) se ignoran, y WPF no necesita ninguna librería SVG.
"""
import gzip
import json
import math
import os
import re
import sys
import xml.etree.ElementTree as ET

SRC = sys.argv[1]
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'src', 'IsTargetSleeping', 'Resources', 'FileIcons.json.gz')
theme = json.load(open(os.path.join(SRC, 'dist', 'material-icons.json'), encoding='utf-8'))
stats = {'gradients': 0, 'skipped': {}, 'colors': set()}


def tag(e):
    return e.tag.split('}')[-1]


def num(v, default=0.0):
    if v is None:
        return default
    m = re.match(r'\s*(-?[0-9.]+(?:e-?[0-9]+)?)', v)
    return float(m.group(1)) if m else default


def mul(m1, m2):
    a1, b1, c1, d1, e1, f1 = m1
    a2, b2, c2, d2, e2, f2 = m2
    return (a1 * a2 + c1 * b2, b1 * a2 + d1 * b2, a1 * c2 + c1 * d2, b1 * c2 + d1 * d2, a1 * e2 + c1 * f2 + e1, b1 * e2 + d1 * f2 + f1)


IDENT = (1, 0, 0, 1, 0, 0)


def transform(text):
    m = IDENT
    for name, args in re.findall(r'(\w+)\s*\(([^)]*)\)', text or ''):
        a = [float(x) for x in re.split(r'[\s,]+', args.strip()) if x]
        if name == 'translate':
            t = (1, 0, 0, 1, a[0], a[1] if len(a) > 1 else 0)
        elif name == 'scale':
            t = (a[0], 0, 0, a[1] if len(a) > 1 else a[0], 0, 0)
        elif name == 'rotate':
            r = math.radians(a[0]); c, s = math.cos(r), math.sin(r)
            t = (c, s, -s, c, 0, 0)
            if len(a) == 3:
                t = mul(mul((1, 0, 0, 1, a[1], a[2]), t), (1, 0, 0, 1, -a[1], -a[2]))
        elif name == 'matrix' and len(a) == 6:
            t = tuple(a)
        elif name == 'skewX':
            t = (1, 0, math.tan(math.radians(a[0])), 1, 0, 0)
        elif name == 'skewY':
            t = (1, math.tan(math.radians(a[0])), 0, 1, 0, 0)
        else:
            continue
        m = mul(m, t)
    return m


def style_of(e):
    out = {}
    for part in (e.get('style') or '').split(';'):
        if ':' in part:
            k, v = part.split(':', 1)
            out[k.strip()] = v.strip()
    return out


def hexcolor(v):
    """'#rgb' | '#rrggbb' | 'rgb()' | nombre → (‘#RRGGBB’, alfa) o None si no hay color."""
    v = (v or '').strip().lower()
    if v in ('', 'none', 'transparent'):
        return None
    names = {'white': '#ffffff', 'black': '#000000', 'red': '#ff0000'}
    v = names.get(v, v)
    if re.fullmatch(r'#[0-9a-f]{3}', v):
        return '#' + ''.join(c * 2 for c in v[1:]), 1.0
    if re.fullmatch(r'#[0-9a-f]{6}', v):
        return v, 1.0
    if re.fullmatch(r'#[0-9a-f]{8}', v):
        return v[:7], int(v[7:], 16) / 255
    m = re.fullmatch(r'rgb\(\s*(\d+)[ ,]+(\d+)[ ,]+(\d+)\s*\)', v)
    if m:
        return '#%02x%02x%02x' % tuple(int(x) for x in m.groups()), 1.0
    stats['colors'].add(v)
    return '#808080', 1.0


def gradient_color(root, ident):
    """Los degradados se aplanan a la media de sus paradas."""
    for g in root.iter():
        if g.get('id') == ident and tag(g) in ('linearGradient', 'radialGradient'):
            href = g.get('{http://www.w3.org/1999/xlink}href') or g.get('href')
            stops = [s for s in g.iter() if tag(s) == 'stop']
            if not stops and href:
                return gradient_color(root, href.lstrip('#'))
            cols = []
            for s in stops:
                c = hexcolor(s.get('stop-color') or style_of(s).get('stop-color'))
                if c:
                    cols.append(c[0])
            if not cols:
                return None
            stats['gradients'] += 1
            rgb = [sum(int(c[i:i + 2], 16) for c in cols) // len(cols) for i in (1, 3, 5)]
            return '#%02x%02x%02x' % tuple(rgb), 1.0
    return None


def paint(root, value):
    value = (value or '').strip()
    m = re.fullmatch(r'url\(#([^)]+)\)', value)
    return gradient_color(root, m.group(1)) if m else hexcolor(value)


def f(x):
    return ('%.4f' % x).rstrip('0').rstrip('.')


def shape_path(e):
    t = tag(e)
    if t == 'path':
        return e.get('d')
    if t in ('circle', 'ellipse'):
        cx, cy = num(e.get('cx')), num(e.get('cy'))
        rx = num(e.get('r')) if t == 'circle' else num(e.get('rx'))
        ry = rx if t == 'circle' else num(e.get('ry'))
        return f'M{f(cx - rx)} {f(cy)}A{f(rx)} {f(ry)} 0 1 0 {f(cx + rx)} {f(cy)}A{f(rx)} {f(ry)} 0 1 0 {f(cx - rx)} {f(cy)}Z'
    if t == 'rect':
        x, y, w, h = num(e.get('x')), num(e.get('y')), num(e.get('width')), num(e.get('height'))
        rx, ry = num(e.get('rx'), -1), num(e.get('ry'), -1)
        rx, ry = (ry if rx < 0 else rx), (rx if ry < 0 else ry)
        rx, ry = max(rx, 0), max(ry, 0)
        rx, ry = min(rx, w / 2), min(ry, h / 2)
        if rx == 0 or ry == 0:
            return f'M{f(x)} {f(y)}H{f(x + w)}V{f(y + h)}H{f(x)}Z'
        return (f'M{f(x + rx)} {f(y)}H{f(x + w - rx)}A{f(rx)} {f(ry)} 0 0 1 {f(x + w)} {f(y + ry)}V{f(y + h - ry)}A{f(rx)} {f(ry)} 0 0 1 {f(x + w - rx)} {f(y + h)}'
                f'H{f(x + rx)}A{f(rx)} {f(ry)} 0 0 1 {f(x)} {f(y + h - ry)}V{f(y + ry)}A{f(rx)} {f(ry)} 0 0 1 {f(x + rx)} {f(y)}Z')
    if t in ('polygon', 'polyline'):
        pts = [float(x) for x in re.split(r'[\s,]+', (e.get('points') or '').strip()) if x]
        pairs = list(zip(pts[0::2], pts[1::2]))
        return 'M' + 'L'.join(f'{f(a)} {f(b)}' for a, b in pairs) + ('Z' if t == 'polygon' else '')
    if t == 'line':
        return f'M{f(num(e.get("x1")))} {f(num(e.get("y1")))}L{f(num(e.get("x2")))} {f(num(e.get("y2")))}'
    return None


def convert(path):
    root = ET.parse(path).getroot()
    by_id = {e.get('id'): e for e in root.iter() if e.get('id')}
    vb = [float(x) for x in re.split(r'[\s,]+', root.get('viewBox', '0 0 16 16').strip())]
    shapes = []

    def walk(e, inh, depth=0):
        t = tag(e)
        if t in ('defs', 'clipPath', 'linearGradient', 'radialGradient', 'mask', 'title', 'desc', 'metadata', 'style'):
            return
        st = style_of(e)
        get = lambda k, d=None: st.get(k, e.get(k, d))
        cur = dict(inh)
        for k in ('fill', 'fill-opacity', 'stroke', 'stroke-width', 'stroke-opacity', 'fill-rule', 'stroke-linejoin'):
            if get(k) is not None:
                cur[k] = get(k)
        if get('opacity') is not None:
            cur['opacity'] = cur.get('opacity', 1.0) * num(get('opacity'), 1.0)
        if e.get('transform'):
            cur['m'] = mul(cur['m'], transform(e.get('transform')))
        if t in ('svg', 'g'):
            for child in e:
                walk(child, cur, depth)
            return
        if t == 'use':
            ref = by_id.get((e.get('{http://www.w3.org/1999/xlink}href') or e.get('href') or '').lstrip('#'))
            if ref is not None and depth < 4:
                inner = dict(cur)
                inner['m'] = mul(cur['m'], (1, 0, 0, 1, num(e.get('x')), num(e.get('y'))))
                walk(ref, inner, depth + 1)
            return
        d = shape_path(e)
        if d is None:
            stats['skipped'][t] = stats['skipped'].get(t, 0) + 1
            return
        fill = paint(root, cur.get('fill', '#000000'))
        stroke = paint(root, cur.get('stroke'))
        if fill is None and stroke is None:
            return
        op = cur.get('opacity', 1.0)
        s = {'d': d}
        if fill:
            s['f'] = fill[0]
            a = fill[1] * num(cur.get('fill-opacity'), 1.0) * op
            if a < 0.999:
                s['fa'] = round(a, 3)
        if stroke:
            s['s'] = stroke[0]
            s['sw'] = num(cur.get('stroke-width'), 1.0)
            a = stroke[1] * num(cur.get('stroke-opacity'), 1.0) * op
            if a < 0.999:
                s['sa'] = round(a, 3)
        if (cur.get('fill-rule') or '').strip() == 'evenodd':
            s['r'] = 1
        if cur['m'] != IDENT:
            s['m'] = [round(x, 5) for x in cur['m']]
        shapes.append(s)

    walk(root, {'m': IDENT})
    return {'vb': vb, 's': shapes}


# qué iconos hacen falta (sin las de carpeta raíz ni las variantes claras)
needed = {theme['file'], theme['folder'], theme['folderExpanded']}
for key in ('fileExtensions', 'fileNames', 'folderNames', 'folderNamesExpanded'):
    needed.update(theme[key].values())
icons = {}
for name in sorted(needed):
    definition = theme['iconDefinitions'].get(name)
    if not definition:
        continue
    svg = os.path.normpath(os.path.join(SRC, 'dist', definition['iconPath']))
    try:
        icons[name] = convert(svg)
    except (ET.ParseError, OSError, ValueError) as ex:
        print('no se pudo convertir', name, ex)

lower = lambda d: {k.lower(): v for k, v in d.items() if v in icons}
data = {
    'icons': icons,
    'file': theme['file'], 'folder': theme['folder'], 'open': theme['folderExpanded'],
    'ext': lower(theme['fileExtensions']), 'files': lower(theme['fileNames']),
    'dirs': lower(theme['folderNames']), 'dirsOpen': lower(theme['folderNamesExpanded']),
}
raw = json.dumps(data, ensure_ascii=False, separators=(',', ':')).encode('utf-8')
with open(OUT, 'wb') as fh:
    with gzip.GzipFile(fileobj=fh, mode='wb', mtime=0) as gz:
        gz.write(raw)
print(f'{len(icons)} iconos, {len(raw) // 1024} KB sin comprimir, {os.path.getsize(OUT) // 1024} KB comprimido')
print('degradados aplanados:', stats['gradients'], '| elementos ignorados:', stats['skipped'], '| colores raros:', sorted(stats['colors']))
