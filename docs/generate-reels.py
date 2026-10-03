"""Render a separate 9:16 version of the shared 77-second presentation.

The original storyboard, music and current native pet frames are reused. Portrait
composition and the shared visual/audio mouse timeline live in docs/tour/reel-*.
"""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import wave

ROOT = Path(__file__).resolve().parent.parent
WORK = ROOT / "obj" / "tour-work"
sys.path.insert(0, str(ROOT / "obj" / "tour-tooling"))
import numpy as np
import imageio_ffmpeg


def run(command, **kwargs):
    return subprocess.run([str(arg) for arg in command], check=True, cwd=ROOT, **kwargs)


def prepare():
    originals = {lang: hashlib.sha256((ROOT / "docs" / "video" / f"isTargetSleeping-tour-{lang}.mp4").read_bytes()).hexdigest()
                 for lang in ("es", "en")}
    originals["soundtrack"] = hashlib.sha256((ROOT / "docs" / "audio" / "tour-soundtrack.m4a").read_bytes()).hexdigest()
    (WORK / "reel-originals.json").write_text(json.dumps(originals, indent=2), encoding="utf-8")
    source = WORK / "presentation"
    if not (source / "sprites" / "meta.json").is_file():
        spec = importlib.util.spec_from_file_location("tour", ROOT / "docs" / "generate-tours.py")
        tour = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(tour)
        source = tour.prepare(tour.art_module())
    stage = WORK / "reel"
    stage.mkdir(parents=True, exist_ok=True)
    shutil.copytree(source / "sprites", stage / "sprites", dirs_exist_ok=True)
    (stage / "img").mkdir(exist_ok=True)
    for lang in ("es", "en"):
        for name in ("panel", "activity", "settings"):
            shutil.copy2(ROOT / "docs" / "images" / f"{name}-{lang}.png", stage / "img" / f"{name}-{lang}.png")
    shutil.copy2(ROOT / "Assets" / "istargetsleeping-icon.svg", stage / "icon.svg")
    template = (ROOT / "docs" / "tour" / "index.html").read_text(encoding="utf-8")
    template = template.replace("</head>", '<link rel="stylesheet" href="reel.css">\n</head>')
    template = template.replace("</body>", '<script src="reel.js"></script>\n</body>')
    (stage / "index.html").write_text(template, encoding="utf-8")
    for name in ("reel.css", "reel.js", "reel-events.json"):
        shutil.copy2(ROOT / "docs" / "tour" / name, stage / name)
    return stage


def soundtrack():
    """Keep the original music/effects and add a deterministic, quiet mouse stem."""
    ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()
    source = ROOT / "docs" / "audio" / "tour-soundtrack.m4a"
    rate = 48000
    decoded = run([ffmpeg, "-v", "error", "-i", source, "-f", "f32le", "-ar", rate, "-ac", 2, "-"], stdout=subprocess.PIPE)
    audio = np.frombuffer(decoded.stdout, dtype="<f4").reshape(-1, 2).copy()
    frames = rate * 77
    audio = np.pad(audio[:frames], ((0, max(0, frames - len(audio))), (0, 0)))
    stem = np.zeros_like(audio)
    clicks = json.loads((ROOT / "docs" / "tour" / "reel-events.json").read_text())
    rng = np.random.default_rng(713)
    for event in clicks:
        for up, offset in ((False, 0), (True, .09)):
            n = int(rate * .055)
            t = np.arange(n) / rate
            noise = rng.normal(0, 1, n)
            # Short mechanical transients: a sharp switch, then a softer release.
            high = noise - np.concatenate(([0], noise[:-1]))
            body = np.sin(2 * np.pi * (1950 if up else 1450) * t) * np.exp(-t * 220)
            snap = high * np.exp(-t * 380)
            signal = (body * .20 + snap * .07) * (0.65 if up else 1)
            signal *= np.minimum(t * rate / 3, 1)
            at = round((event["time"] + offset) * rate)
            # Keep the click near the center; slight stereo width sounds natural.
            stem[at:at + n, 0] += signal * .96
            stem[at:at + n, 1] += signal
    mixed = audio + stem
    peak = float(np.max(np.abs(mixed)))
    if peak > .98:
        raise ValueError(f"Reel soundtrack would clip ({peak:.4f}); reduce the click stem")
    for name, signal in (("reel-clicks.wav", stem), ("reel-mix.wav", mixed)):
        with wave.open(str(WORK / name), "wb") as output:
            output.setnchannels(2); output.setsampwidth(3); output.setframerate(rate)
            pcm = np.round(signal * 8388607).astype("<i4").view(np.uint8).reshape(-1, 4)
            output.writeframes(pcm[:, :3].tobytes())
    target = WORK / "reel-soundtrack.m4a"
    run([ffmpeg, "-v", "error", "-y", "-i", WORK / "reel-mix.wav", "-c:a", "aac", "-b:a", "256k", target])
    (WORK / "reel-audio-report.json").write_text(json.dumps({
        "sampleRate": rate, "duration": 77, "peak": peak, "clicks": clicks,
        "musicGain": 1, "source": "docs/audio/tour-soundtrack.m4a",
    }, indent=2), encoding="utf-8")
    return target


def render(args, stage, lang, audio):
    temporary = WORK / f"reel-{lang}.mp4"
    poster = WORK / f"reel-{lang}.jpg"
    config = {
        "root": str(stage), "lang": lang, "chrome": str(args.chrome),
        "width": 1080, "height": 1920, "title": "Reels 9:16",
        "ffmpeg": imageio_ffmpeg.get_ffmpeg_exe(), "audio": str(audio),
        "output": str(temporary), "poster": str(poster), "review": str(WORK / "reel-review"),
        "reviewTimes": [4.5, 10, 18.5, 23, 25, 26.3, 28, 30, 33, 36, 39, 42.7, 44.5, 46.3,
                        52, 56, 58, 59.8, 62, 63.5, 65, 66.5, 67.6, 69.5, 70.5, 71.3, 75],
    }
    if args.stills:
        config["stills"] = [float(value) for value in args.stills.split(",")]
        if any(t < 0 or t >= 77 for t in config["stills"]):
            raise ValueError("Review times must be between 0 and 77 seconds")
    config_file = WORK / f"render-reel-{lang}.json"
    config_file.write_text(json.dumps(config), encoding="utf-8")
    env = os.environ.copy()
    env["NODE_PATH"] = str(ROOT / "obj" / "tour-tooling" / "node_modules")
    run([args.node, ROOT / "docs" / "tour" / "render.cjs", config_file], env=env)
    if not args.stills:
        video = ROOT / "docs" / "video" / f"isTargetSleeping-reel-{lang}.mp4"
        final_poster = ROOT / "docs" / "images" / f"reel-{lang}.jpg"
        os.replace(temporary, video)
        os.replace(poster, final_poster)
        print(f"Saved {video.relative_to(ROOT)} ({video.stat().st_size / 1024 / 1024:.1f} MiB)", flush=True)


def remix(lang, audio):
    video = ROOT / "docs" / "video" / f"isTargetSleeping-reel-{lang}.mp4"
    temporary = WORK / f"remixed-reel-{lang}.mp4"
    run([imageio_ffmpeg.get_ffmpeg_exe(), "-v", "error", "-y", "-i", video, "-i", audio,
         "-map", "0:v:0", "-map", "1:a:0", "-map_metadata", "0", "-c", "copy", "-t", "77",
         "-movflags", "+faststart", temporary])
    os.replace(temporary, video)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--prepare-only", action="store_true")
    parser.add_argument("--render-only", action="store_true")
    parser.add_argument("--audio-only", action="store_true", help="Update the mouse/music mix without re-encoding video frames")
    parser.add_argument("--lang", choices=("en", "es", "all"), default="all")
    parser.add_argument("--stills", help="Comma-separated scene times to review before encoding")
    parser.add_argument("--node", default=shutil.which("node") or "node")
    parser.add_argument("--chrome", type=Path, default=Path(os.environ.get("PROGRAMFILES", "C:/Program Files")) / "Google/Chrome/Application/chrome.exe")
    args = parser.parse_args()
    WORK.mkdir(parents=True, exist_ok=True)
    stage = WORK / "reel" if args.render_only or args.audio_only else prepare()
    audio = WORK / "reel-soundtrack.m4a" if args.render_only else soundtrack()
    if not args.prepare_only:
        for lang in (("es", "en") if args.lang == "all" else (args.lang,)):
            if args.audio_only:
                remix(lang, audio)
            else:
                render(args, stage, lang, audio)


if __name__ == "__main__":
    main()
