"""Render the bilingual notch tour from the website simulator and current app captures."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parent.parent
WORK = ROOT / "obj" / "tour-work"
sys.path.insert(0, str(ROOT / "obj" / "tour-tooling"))
import imageio_ffmpeg

APP_DLL = ROOT / "src" / "IsTargetSleeping" / "bin" / "Release" / "net10.0-windows10.0.19041.0" / "isTargetSleeping.dll"


def run(command, **kwargs):
    subprocess.run([str(arg) for arg in command], check=True, cwd=ROOT, **kwargs)


def capture(dotnet):
    run([dotnet, "build", ROOT / "src" / "IsTargetSleeping" / "IsTargetSleeping.csproj", "-c", "Release"])
    for lang in ("es", "en"):
        native = WORK / "native"
        native.mkdir(parents=True, exist_ok=True)
        settings = native / f"settings-{lang}.png"
        run([dotnet, APP_DLL, "--snapshot", settings, "settings", "appwindow", "demo", "--tab", "general", "--lang", lang])
        shutil.copy2(settings, ROOT / "docs" / "images" / f"settings-{lang}.png")
        for name, view, options in (
            ("widgets", "widgets", ("--gallery", "media", "--home-widgets", "media,clock,pomodoro,todo,shelf")),
            ("display", "settings", ("--tab", "display")),
        ):
            output = ROOT / "docs" / "images" / f"tour-ui-{name}-{lang}.png"
            run([dotnet, APP_DLL, "--snapshot", output, view, "appwindow", "demo", *options, "--lang", lang])


def render(args, lang):
    video = ROOT / "docs" / "video" / f"isTargetSleeping-tour-{lang}.mp4"
    poster = ROOT / "docs" / "images" / f"tour-{lang}.jpg"
    temporary = WORK / f"notch-tour-{lang}.mp4"
    temporary_poster = WORK / f"notch-tour-{lang}.jpg"
    config = {
        "root": str(ROOT), "page": "docs/tour/index.html", "lang": lang,
        "chrome": str(args.chrome), "ffmpeg": imageio_ffmpeg.get_ffmpeg_exe(),
        "audio": str(ROOT / "docs" / "audio" / "tour-soundtrack.m4a"),
        "output": str(temporary), "poster": str(temporary_poster),
        "review": str(WORK / "review"), "title": "One edge, more of your day",
        "reviewTimes": [2, 10, 19, 27, 38, 47, 57, 68, 75],
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
    parser.add_argument("--capture", action="store_true", help="Build current sources and refresh current app window captures")
    parser.add_argument("--lang", choices=("en", "es", "all"), default="all")
    parser.add_argument("--stills", help="Comma-separated review times instead of a full encode")
    parser.add_argument("--dotnet", default=shutil.which("dotnet") or "dotnet")
    parser.add_argument("--node", default=shutil.which("node") or "node")
    parser.add_argument("--chrome", type=Path, default=Path(os.environ.get("PROGRAMFILES", "C:/Program Files")) / "Google/Chrome/Application/chrome.exe")
    args = parser.parse_args()
    WORK.mkdir(parents=True, exist_ok=True)
    if args.capture:
        capture(args.dotnet)
    for lang in (("es", "en") if args.lang == "all" else (args.lang,)):
        render(args, lang)


if __name__ == "__main__":
    main()
