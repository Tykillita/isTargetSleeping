"""Generate the bilingual 77-second tour from current, real demo captures.

Windows / uso en Windows:
  python -m pip install --target obj/tour-tooling Pillow numpy imageio-ffmpeg==0.6.0
  python docs/generate-tours.py --capture --exe build/isTargetSleeping.exe

The executable must match VERSION. Demo snapshots and pet export do not operate
Ollama or save preferences. A deterministic instrumental soundtrack is generated
from notes, without narration. --audio can supply another owned soundtrack.
El ejecutable debe coincidir con VERSION. Las capturas demo y la exportación de
mascotas no cambian Ollama ni los ajustes. La banda instrumental se genera a partir
de notas, sin narración; --audio permite indicar otra banda sonora propia.

Output / salida: docs/images/tour-{en,es}.jpg and docs/video/*-tour-{en,es}.mp4.
Temporary exports, audio and review frames stay in ignored obj/tour-work/.
Requires Segoe UI on Windows. All screenshots and sprites come from the app.
"""

from __future__ import annotations

import argparse
import ctypes
import importlib.util
import math
import os
from pathlib import Path
import subprocess
import sys
import wave

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "obj" / "tour-tooling"))

import imageio_ffmpeg
import numpy as np
from PIL import Image, ImageDraw, ImageFont

WIDTH, HEIGHT, FPS, DURATION = 1920, 1080, 30, 77
BLUE, WHITE, MUTED = "#4da3ff", "#f5f5f7", "#a1a7b0"
SCENES = [("intro", 7), ("panel", 9), ("idle", 8), ("memory", 9),
          ("processes", 8), ("clean", 9), ("game", 7), ("pets", 9),
          ("pin", 7), ("end", 4)]

TEXT = {
    "en": {
        "intro": ("NATIVE WINDOWS · FREE & OPEN SOURCE", "Your models work.\nThen they sleep.",
                  "isTargetSleeping keeps an eye on your local models and gives memory back when they are idle."),
        "panel": ("ONE PANEL · BESIDE THE CLOCK", "RAM, VRAM and\nyour local models.",
                  "Turn Ollama on or off, unload models and manage llama.cpp from the same tray panel."),
        "idle": ("AUTOMATIC MODEL SLEEP", "Keep Ollama ready.\nLet the model sleep.",
                 "Choose 5, 15, 30 or 60 minutes without activity. The next request loads the model again."),
        "memory": ("ACTIVITY · MEMORY NOW", "See where your\nmemory is going.",
                   "Physical RAM, committed memory, page files and system cache are shown separately."),
        "processes": ("ACTIVITY · PROCESS EXPLORER", "Recognize the apps\nusing your RAM.",
                      "Open all processes to search, sort and inspect groups and PIDs. Termination asks for confirmation."),
        "clean": ("FREE RAM · YOUR RULES", "Choose when\nWindows cleans up.",
                  "Use a RAM percentage, a minimum pause, an interval or critical pressure. Models, games and the active app are protected."),
        "game": ("AUTOMATIC · GAME MODE", "More room\nfor your game.",
                 "Engines turn off after the game starts. After you quit, only the ones that were on come back."),
        "pets": ("BESIDE START · FOUR PERSONALITIES", "Every pet cleans\nin its own way.",
                 "Mira scans and compacts. The llama shakes her wool, the kitten washes, and the capybara gets out of her tub."),
        "pin": ("WINDOWS · THE TRAY ICON", "Keep the icon\nwithin reach.",
                "Use the tray switch when Windows exposes it. Otherwise, open taskbar settings to show the icon beside the clock."),
        "end": ("PRIVATE · NATIVE · MIT", "isTargetSleeping", "Your PC. Your models. Your memory."),
        "demo": "Real interface · example data", "sleep": "Asleep in their own beds",
        "cleanup": "Cleaning after leaving the bed", "awake": "Reactions in bed · a crocodile ride",
        "pets_names": ["Mira", "Llama", "Capybara", "Kitten"],
        "site": "istargetsleeping.web.app",
    },
    "es": {
        "intro": ("WINDOWS NATIVO · LIBRE Y GRATUITO", "Tus modelos trabajan.\nDespués, duermen.",
                  "isTargetSleeping vigila tus modelos locales y devuelve memoria cuando dejan de trabajar."),
        "panel": ("UN PANEL · JUNTO AL RELOJ", "RAM, VRAM y\ntus modelos locales.",
                  "Enciende o apaga Ollama, duerme modelos y controla llama.cpp desde el mismo panel de bandeja."),
        "idle": ("MODELOS QUE DUERMEN SOLOS", "Ollama sigue listo.\nEl modelo descansa.",
                 "Elige 5, 15, 30 o 60 minutos sin actividad. La siguiente petición vuelve a cargar el modelo."),
        "memory": ("ACTIVIDAD · MEMORIA AHORA", "Mira dónde está\nyendo tu memoria.",
                   "La RAM física, la memoria comprometida, la paginación y la caché del sistema se muestran por separado."),
        "processes": ("ACTIVIDAD · RECONOCIMIENTO DE PROCESOS", "Reconoce las apps\nque usan tu RAM.",
                      "Abre todos los procesos para buscar, ordenar y revisar grupos y PIDs. La finalización pide confirmación."),
        "clean": ("LIBERAR RAM · TUS REGLAS", "Elige cuándo\nWindows hace sitio.",
                  "Por porcentaje de RAM, pausa mínima, intervalo o presión crítica. Se protegen los modelos, el juego y la app activa."),
        "game": ("AUTOMÁTICO · MODO JUEGO", "Más espacio\npara tu juego.",
                 "Los motores se apagan después de abrir el juego. Al salir, solo vuelve lo que estaba encendido."),
        "pets": ("JUNTO A INICIO · CUATRO PERSONALIDADES", "Cada mascota limpia\na su manera.",
                 "Mira escanea y compacta. La llama sacude la lana, el gatito se lava y la capibara sale de su tina."),
        "pin": ("WINDOWS · EL ÍCONO DE BANDEJA", "Deja el ícono\na mano.",
                "Usa el interruptor si Windows permite detectarlo. Si no, abre los ajustes de la barra para mostrarlo junto al reloj."),
        "end": ("PRIVADO · NATIVO · MIT", "isTargetSleeping", "Tu PC. Tus modelos. Tu memoria."),
        "demo": "Interfaz real · datos de ejemplo", "sleep": "Dormidas en sus propias camas",
        "cleanup": "Limpieza después de salir de la cama", "awake": "Caricias en la cama · paseo en cocodrilo",
        "pets_names": ["Mira", "Llama", "Capibara", "Gatito"],
        "site": "istargetsleeping.web.app",
    },
}


def run(command):
    subprocess.run([str(x) for x in command], check=True)


def check_executable_version(exe):
    """Read the embedded Windows version resource before generating any captures."""
    if os.name != "nt":
        raise RuntimeError("App capture requires Windows")
    if not exe.is_file():
        raise FileNotFoundError(f"Build the current app first: {exe}")
    version_api = ctypes.WinDLL("version", use_last_error=True)
    version_api.GetFileVersionInfoSizeW.argtypes = [ctypes.c_wchar_p, ctypes.POINTER(ctypes.c_uint32)]
    version_api.GetFileVersionInfoSizeW.restype = ctypes.c_uint32
    version_api.GetFileVersionInfoW.argtypes = [ctypes.c_wchar_p, ctypes.c_uint32, ctypes.c_uint32, ctypes.c_void_p]
    version_api.GetFileVersionInfoW.restype = ctypes.c_int
    version_api.VerQueryValueW.argtypes = [ctypes.c_void_p, ctypes.c_wchar_p, ctypes.POINTER(ctypes.c_void_p), ctypes.POINTER(ctypes.c_uint32)]
    version_api.VerQueryValueW.restype = ctypes.c_int
    ignored = ctypes.c_uint32()
    length = version_api.GetFileVersionInfoSizeW(str(exe), ctypes.byref(ignored))
    if not length:
        raise ctypes.WinError(ctypes.get_last_error())
    resource = ctypes.create_string_buffer(length)
    if not version_api.GetFileVersionInfoW(str(exe), 0, length, resource):
        raise ctypes.WinError(ctypes.get_last_error())
    address, size = ctypes.c_void_p(), ctypes.c_uint32()
    if not version_api.VerQueryValueW(resource, "\\", ctypes.byref(address), ctypes.byref(size)) or size.value < 52:
        raise RuntimeError("The executable does not have a valid fixed version resource")
    fixed = ctypes.cast(address, ctypes.POINTER(ctypes.c_uint32 * 13)).contents
    if fixed[0] != 0xFEEF04BD:
        raise RuntimeError("Invalid Windows version resource signature")
    actual = (fixed[2] >> 16, fixed[2] & 0xFFFF, fixed[3] >> 16)
    expected = tuple(int(part) for part in (ROOT/"VERSION").read_text().strip().split("."))
    if actual != expected:
        raise RuntimeError(f"Executable version {'.'.join(map(str, actual))} does not match VERSION {'.'.join(map(str, expected))}")


def font(size, bold=False):
    path = Path(os.environ.get("WINDIR", "C:/Windows")) / "Fonts" / ("segoeuib.ttf" if bold else "segoeui.ttf")
    return ImageFont.truetype(str(path), size)


def wrapped(draw, text, selected_font, width):
    lines = []
    for paragraph in text.split("\n"):
        line = ""
        for word in paragraph.split():
            candidate = f"{line} {word}".strip()
            if line and draw.textlength(candidate, font=selected_font) > width:
                lines.append(line)
                line = word
            else:
                line = candidate
        lines.append(line)
    return lines


def text_block(draw, xy, text, selected_font, width, fill, line_height):
    x, y = xy
    for line in wrapped(draw, text, selected_font, width):
        draw.text((x, y), line, font=selected_font, fill=fill)
        y += line_height
    return y


def background():
    y, x = np.mgrid[0:HEIGHT, 0:WIDTH]
    glow = np.exp(-((x-530)**2/(690**2) + (y-430)**2/(480**2)))
    return Image.fromarray(np.stack([9+5*glow, 11+12*glow, 15+21*glow], axis=2).astype("uint8"), "RGB")


def place(image, asset, box, border=True):
    x, y, width, height = box
    asset = asset.copy().convert("RGBA")
    asset.thumbnail((width, height), Image.Resampling.LANCZOS)
    left, top = x+(width-asset.width)//2, y+(height-asset.height)//2
    if border:
        ImageDraw.Draw(image).rounded_rectangle((left-2, top-2, left+asset.width+2, top+asset.height+2), 18, outline="#303942", width=2)
    image.paste(asset, (left, top), asset)


def capture(exe, work, languages):
    for language in languages:
        for view in ("panel", "activity", "settings"):
            command = [exe, "--snapshot", ROOT/"docs"/"images"/f"{view}-{language}.png", "demo", "--lang", language]
            if view != "panel":
                command.append(view)
            run(command)
        run([exe, "--snapshot", work/f"game-{language}.png", "game", "demo", "--lang", language])
    run([exe, "--export-pet", work/"pets"])
    spec = importlib.util.spec_from_file_location("readme_art", ROOT/"docs"/"readme-art.py")
    art = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(art)
    art.settings_previews()
    return art


def make_base(language, name, version, assets, canvas):
    image = canvas.copy()
    draw = ImageDraw.Draw(image)
    t = TEXT[language]
    kicker, title, description = t[name]
    draw.text((104, 68), "isTargetSleeping", font=font(26, True), fill=WHITE)
    draw.text((1585, 70), f"v{version} · Windows", font=font(23), fill=MUTED)
    draw.line((104, 117, 1816, 117), fill="#26313c", width=1)
    draw.text((104, 215), kicker, font=font(20, True), fill=BLUE)
    title_y = text_block(draw, (104, 270), title, font(65, True), 745, WHITE, 83)
    text_block(draw, (104, max(490, title_y+45)), description, font(31), 705, MUTED, 46)
    draw.text((104, 969), t["site"], font=font(23), fill=BLUE)
    if name not in ("pets", "end"):
        draw.text((1110, 955), t["demo"], font=font(21), fill=MUTED)
    if name == "intro":
        place(image, assets["logo"], (104, 752, 110, 110), border=False)
        place(image, assets["panel"], (1140, 146, 620, 760))
    elif name == "panel":
        place(image, assets["panel"], (1050, 146, 710, 760))
    elif name == "idle":
        place(image, assets["settings"].crop((10, 0, 674, 540)), (930, 230, 870, 630))
    elif name == "memory":
        place(image, assets["activity"].crop((14, 580, 670, 1070)), (910, 245, 900, 640))
    elif name == "processes":
        place(image, assets["activity"].crop((14, 1070, 670, 1360)), (900, 360, 910, 460))
    elif name == "clean":
        place(image, assets["settings"].crop((14, 1210, 670, 2170)), (990, 147, 785, 770))
    elif name == "game":
        place(image, assets["game"], (1020, 146, 730, 760))
    elif name == "pin":
        place(image, assets["settings"].crop((14, 5380, 670, 5650)), (910, 295, 900, 550))
    elif name == "pets":
        for i, pet_name in enumerate(t["pets_names"]):
            x, y = 945+(i%2)*425, 210+(i//2)*330
            draw.rounded_rectangle((x, y, x+390, y+295), 24, fill="#1b2026", outline="#303942", width=2)
            draw.text((x+24, y+20), pet_name, font=font(28, True), fill=WHITE)
    elif name == "end":
        place(image, assets["logo"], (1130, 275, 450, 450), border=False)
    return image


def animate_pets(image, local_time, language, sheets):
    draw = ImageDraw.Draw(image)
    stage = min(int(local_time/3), 2)
    label = ["sleep", "cleanup", "awake"][stage]
    draw.text((945, 151), TEXT[language][label], font=font(23), fill=BLUE)
    for i, pet in enumerate(("mira", "llama", "capybara", "orange-cat")):
        row = "DeepSleep" if stage == 0 else "SweepingBed" if stage == 1 else "WakingUp" if pet == "capybara" else "HeartsBed"
        frames = sheets[pet][row]
        period = 2.6 if stage == 0 else 3.0
        index = min(int(((local_time % 3)/period)*len(frames)), len(frames)-1)
        asset = frames[index].resize((frames[index].width*3, frames[index].height*3), Image.Resampling.NEAREST)
        x, y = 945+(i%2)*425, 210+(i//2)*330
        image.paste(asset, (x+(390-asset.width)//2, y+280-asset.height), asset)


def soundtrack(work):
    """An original, quiet arpeggio with no recordings or spoken content."""
    output = work/"soundtrack.wav"
    rate, beat = 48000, 60/84
    music = np.zeros((rate*DURATION, 2), dtype=np.float32)
    chords = [(57, 60, 64), (53, 57, 60), (60, 64, 67), (55, 59, 62)]
    for pulse in range(math.ceil(DURATION/beat)):
        start = int(pulse*beat*rate)
        count = min(int(beat*2.8*rate), len(music)-start)
        if count <= 0:
            break
        t = np.arange(count, dtype=np.float32)/rate
        chord = chords[(pulse//8) % len(chords)]
        note = chord[pulse % 3] + (12 if pulse % 4 == 3 else 0)
        frequency = 440*2**((note-69)/12)
        envelope = (1-np.exp(-t*120))*np.exp(-t*2.3)
        tone = (.073*np.sin(2*np.pi*frequency*t)
                + .014*np.sin(2*np.pi*frequency*2*t)*np.exp(-t*3)
                + .006*np.sin(2*np.pi*frequency*3*t)*np.exp(-t*5))*envelope
        pan = .25*math.sin(pulse*.7)
        music[start:start+count, 0] += tone*(1-pan)
        music[start:start+count, 1] += tone*(1+pan)
        if pulse % 4 == 0:
            bass = 440*2**((chord[0]-24-69)/12)
            low = .026*np.sin(2*np.pi*bass*t)*(1-np.exp(-t*40))*np.exp(-t*.85)
            music[start:start+count] += low[:, None]
    fade = np.minimum(1, np.arange(len(music))/(rate*1.8))
    fade *= np.minimum(1, (len(music)-1-np.arange(len(music)))/(rate*3))
    music *= fade[:, None]
    with wave.open(str(output), "wb") as target:
        target.setnchannels(2)
        target.setsampwidth(2)
        target.setframerate(rate)
        target.writeframes((np.clip(music, -1, 1)*32767).astype("<i2").tobytes())
    return output


def generate(language, args, work, art, sheets):
    ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()
    output = ROOT/"docs"/"video"/f"isTargetSleeping-tour-{language}.mp4"
    audio = work/f"soundtrack-{language}.m4a"
    audio_source = Path(args.audio) if args.audio else soundtrack(work)
    if not audio_source.exists():
        raise FileNotFoundError("The supplied soundtrack does not exist")
    run([ffmpeg, "-hide_banner", "-loglevel", "error", "-y", "-i", audio_source, "-vn", "-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-ac", "2", audio])
    assets = {view: Image.open(ROOT/"docs"/"images"/f"{view}-{language}.png").convert("RGBA") for view in ("panel", "activity", "settings")}
    assets["game"] = Image.open(work/f"game-{language}.png").convert("RGBA")
    assets["logo"] = Image.open(ROOT/"Assets"/"istargetsleeping-icon-512.png").convert("RGBA")
    version = (ROOT/"VERSION").read_text().strip()
    canvas = background()
    bases = {name: make_base(language, name, version, assets, canvas) for name, _ in SCENES}
    bases["intro"].save(ROOT/"docs"/"images"/f"tour-{language}.jpg", quality=94, subsampling=0, optimize=True)
    temporary = work/f"tour-{language}.mp4"
    command = [ffmpeg, "-hide_banner", "-loglevel", "error", "-y", "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{WIDTH}x{HEIGHT}", "-r", str(FPS), "-i", "pipe:0", "-i", str(audio), "-map", "0:v:0", "-map", "1:a:0", "-c:v", "libx264", "-preset", "fast", "-crf", "19", "-threads", "4", "-pix_fmt", "yuv420p", "-c:a", "copy", "-t", str(DURATION), "-movflags", "+faststart", "-metadata", f"title=isTargetSleeping {version} — {language.upper()} tour", "-metadata", "comment=Current app demo captures and native pet animations; example data.", str(temporary)]
    process = subprocess.Popen(command, stdin=subprocess.PIPE)
    elapsed = 0
    review = []
    try:
        for number, (name, seconds) in enumerate(SCENES):
            print(f"{language}: {name}, {elapsed}–{elapsed+seconds}s", flush=True)
            for frame in range(seconds*FPS):
                local_time = frame/FPS
                image = bases[name].copy()
                if name == "pets":
                    animate_pets(image, local_time, language, sheets)
                draw = ImageDraw.Draw(image)
                progress = (elapsed+local_time)/DURATION
                draw.line((104, 1025, 1816, 1025), fill="#25313e", width=3)
                draw.line((104, 1025, 104+int(1712*progress), 1025), fill=BLUE, width=3)
                # A short fade through the neutral canvas keeps each genuine capture legible.
                opacity = min(1.0, local_time/.35, (seconds-local_time)/.35)
                if opacity < 1.0:
                    image = Image.blend(canvas, image, max(0, opacity))
                process.stdin.write(image.tobytes())
                if frame == int(seconds*FPS/2):
                    review.append(image.copy())
                    image.save(work/f"review-{language}-{number:02d}-{name}.jpg", quality=90)
                if name == "pets" and frame in (45, 135, 225):
                    image.save(work/f"review-{language}-pets-{frame}.jpg", quality=92)
            elapsed += seconds
    finally:
        process.stdin.close()
    if process.wait() != 0:
        raise RuntimeError("FFmpeg failed; the existing tour was not replaced")
    os.replace(temporary, output)
    contact = Image.new("RGB", (1280, math.ceil(len(review)/2)*380), "#0d1117")
    for i, image in enumerate(review):
        image.thumbnail((640, 360), Image.Resampling.LANCZOS)
        x,y = (i%2)*640, (i//2)*380
        contact.paste(image, (x,y+20))
        ImageDraw.Draw(contact).text((x+10,y+1), SCENES[i][0], font=font(15), fill=WHITE)
    contact.save(work/f"contact-{language}.jpg", quality=93)
    print(f"Saved {output.relative_to(ROOT)} ({output.stat().st_size/1024/1024:.1f} MiB)", flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--capture", action="store_true", help="Refresh app snapshots and pet exports first")
    parser.add_argument("--exe", type=Path, default=ROOT/"build"/"isTargetSleeping.exe")
    parser.add_argument("--lang", choices=("en", "es", "all"), default="all")
    parser.add_argument("--audio", help="An owned soundtrack instead of the generated instrumental")
    args = parser.parse_args()
    languages = ("en", "es") if args.lang == "all" else (args.lang,)
    work = ROOT/"obj"/"tour-work"
    work.mkdir(parents=True, exist_ok=True)
    spec = importlib.util.spec_from_file_location("readme_art", ROOT/"docs"/"readme-art.py")
    art = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(art)
    if args.capture:
        check_executable_version(args.exe.resolve())
        art = capture(args.exe.resolve(), work, languages)
    sheets = {pet: art.load_sheet(pet, str(work/"pets")) for pet in art.PETS}
    for language in languages:
        generate(language, args, work, art, sheets)


if __name__ == "__main__":
    main()
