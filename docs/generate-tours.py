"""Render the original bilingual presentation with current native app captures.

The storyboard, choreography and soundtrack are checked-in sources, rather than
temporary assets. Only app screenshots and native pet frames are refreshed.
"""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parent.parent
WORK = ROOT / "obj" / "tour-work"
sys.path.insert(0, str(ROOT / "obj" / "tour-tooling"))
from PIL import Image
import imageio_ffmpeg


def run(command, **kwargs):
    subprocess.run([str(arg) for arg in command], check=True, cwd=ROOT, **kwargs)


def dotnet_path():
    local = Path(os.environ.get("LOCALAPPDATA", "")) / "Microsoft" / "dotnet" / "dotnet.exe"
    return local if local.is_file() else shutil.which("dotnet") or "dotnet"


def art_module():
    spec = importlib.util.spec_from_file_location("readme_art", ROOT / "docs" / "readme-art.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def capture(dotnet):
    # This project references current sources; it cannot silently use an older
    # installed/published executable. No engine or preference changes in demo mode.
    run([dotnet, "run", "--project", ROOT / "docs" / "MediaCapture", "-c", "Release", "--",
         ROOT, "--views-only"])
    app = ROOT / "src" / "IsTargetSleeping" / "bin" / "Release" / "net10.0-windows10.0.19041.0" / "isTargetSleeping.dll"
    run([dotnet, app, "--export-pet", WORK / "pets"])


def prepare(art):
    stage = WORK / "presentation"
    (stage / "img").mkdir(parents=True, exist_ok=True)
    (stage / "sprites").mkdir(exist_ok=True)
    shutil.copy2(ROOT / "docs" / "tour" / "index.html", stage / "index.html")
    shutil.copy2(ROOT / "Assets" / "istargetsleeping-icon.svg", stage / "icon.svg")
    for lang in ("es", "en"):
        for view in ("panel", "activity", "settings"):
            source = ROOT / "docs" / "images" / f"{view}-{lang}.png"
            with Image.open(source) as image:
                if image.width < 1368:
                    raise ValueError(f"{source.name}: refresh the native 4× capture first (--capture)")
            shutil.copy2(source, stage / "img" / source.name)

    meta = json.loads((ROOT / "docs" / "tour" / "reference-sprites.json").read_text())
    transitions = dict(zip(["T_Sleep_Wake", "T_Alert_Work", "T_Work_Alert", "T_Alert_Down",
                            "T_Eat_Alert", "T_Alert_Sleep", "T_Drowsy_Eat"],
                           [f"T{i}" for i in range(1, 8)]))
    sheets = {pet: art.load_sheet(pet, str(WORK / "pets")) for pet in art.PETS}
    for pet, layout in meta.items():
        for row, count in layout["rows"].items():
            source = sheets[pet][transitions.get(row, row)]
            if not source:
                raise ValueError(f"Missing native sprite row: {pet}/{row}")
            strip = Image.new("RGBA", (layout["w"] * count, layout["h"]))
            for i in range(count):
                # Keep the original cell geometry, frame counts and choreography.
                # The procedural renderer provides the current pet appearance.
                index = round(i * (len(source) - 1) / max(1, count - 1))
                frame = source[index].resize((source[index].width * 2, source[index].height * 2), Image.Resampling.NEAREST)
                if frame.size != (layout["w"], layout["h"]):
                    raise ValueError(f"Changed pet canvas for {pet}: review placement before rendering")
                strip.paste(frame, (i * layout["w"], 0))
            strip.save(stage / "sprites" / f"{pet}-{row}.png", optimize=True)
    (stage / "sprites" / "meta.json").write_text(json.dumps(meta), encoding="utf-8")
    for lang in ("es", "en"):
        peeks = {pet: art.load_peek(pet, str(WORK / "pets")) for pet in art.PETS
                 if (WORK / "pets" / f"pet-{pet}-peek.png").is_file()}
        art.pets_home(sheets, peeks, lang)
        art.pets_states(sheets, lang)
    art.settings_previews()
    return stage


def render(args, stage, lang):
    video = ROOT / "docs" / "video" / f"isTargetSleeping-tour-{lang}.mp4"
    temporary = WORK / f"original-layout-{lang}.mp4"
    poster = ROOT / "docs" / "images" / f"tour-{lang}.jpg"
    temporary_poster = WORK / f"poster-{lang}.jpg"
    config = {
        "root": str(stage), "lang": lang, "chrome": str(args.chrome),
        "ffmpeg": imageio_ffmpeg.get_ffmpeg_exe(), "audio": str(ROOT / "docs" / "audio" / "tour-soundtrack.m4a"),
        "output": str(temporary), "poster": str(temporary_poster), "review": str(WORK / "review"),
    }
    if args.stills:
        config["stills"] = [float(value) for value in args.stills.split(",")]
        if any(t < 0 or t >= 77 for t in config["stills"]):
            raise ValueError("Review times must be between 0 and 77 seconds")
    config_path = WORK / f"render-{lang}.json"
    config_path.write_text(json.dumps(config), encoding="utf-8")
    env = os.environ.copy()
    env["NODE_PATH"] = str(ROOT / "obj" / "tour-tooling" / "node_modules")
    run([args.node, ROOT / "docs" / "tour" / "render.cjs", config_path], env=env)
    if not args.stills:
        video.parent.mkdir(exist_ok=True)
        os.replace(temporary, video)
        os.replace(temporary_poster, poster)
        print(f"Saved {video.relative_to(ROOT)} ({video.stat().st_size / 1024 / 1024:.1f} MiB)", flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--capture", action="store_true", help="Refresh native 4× app screenshots and current pet exports")
    parser.add_argument("--prepare-only", action="store_true", help="Prepare current media without rendering videos")
    parser.add_argument("--render-only", action="store_true", help="Render already prepared assets (allows independent language renders)")
    parser.add_argument("--lang", choices=("en", "es", "all"), default="all")
    parser.add_argument("--stills", help="Comma-separated review times instead of a full encode")
    parser.add_argument("--dotnet", default=dotnet_path())
    parser.add_argument("--node", default=shutil.which("node") or "node")
    parser.add_argument("--chrome", type=Path, default=Path(os.environ.get("PROGRAMFILES", "C:/Program Files")) / "Google/Chrome/Application/chrome.exe")
    args = parser.parse_args()
    WORK.mkdir(parents=True, exist_ok=True)
    if args.capture:
        capture(args.dotnet)
    stage = WORK / "presentation" if args.render_only else prepare(art_module())
    if not args.prepare_only:
        for lang in (("es", "en") if args.lang == "all" else (args.lang,)):
            render(args, stage, lang)


if __name__ == "__main__":
    main()
