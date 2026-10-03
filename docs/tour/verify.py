"""Verify presentation length, native captures and an unchanged AAC soundtrack."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "obj" / "tour-tooling"))
import imageio_ffmpeg
from PIL import Image


def audio_hash(path):
    data = subprocess.check_output([imageio_ffmpeg.get_ffmpeg_exe(), "-v", "error", "-i", str(path),
                                    "-map", "0:a:0", "-c:a", "copy", "-f", "adts", "-"])
    return hashlib.sha256(data).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--reference", type=Path, help="Optional original HTML to verify choreography")
    args = parser.parse_args()
    expected_audio = audio_hash(ROOT / "docs" / "audio" / "tour-soundtrack.m4a")
    result = {"audioAdtsSha256": expected_audio, "videos": {}, "screenshots": {}}
    for lang in ("es", "en"):
        video = ROOT / "docs" / "video" / f"isTargetSleeping-tour-{lang}.mp4"
        stream = imageio_ffmpeg.read_frames(str(video))
        metadata = next(stream)
        stream.close()
        frames, seconds = imageio_ffmpeg.count_frames_and_secs(str(video))
        assert metadata["size"] == (1920, 1080), metadata
        assert metadata["fps"] == 30, metadata
        assert frames == 2310 and abs(seconds - 77) < .001, (frames, seconds)
        assert audio_hash(video) == expected_audio, f"Soundtrack changed: {lang}"
        result["videos"][lang] = {"width": 1920, "height": 1080, "fps": 30,
                                   "frames": frames, "seconds": seconds, "audioIdentical": True}
        for view in ("panel", "activity", "settings", "settings-preview"):
            with Image.open(ROOT / "docs" / "images" / f"{view}-{lang}.png") as image:
                assert image.width == 1368, (view, image.size)
                if view != "settings-preview":
                    assert abs(image.info["dpi"][0] - 384) < .1, (view, image.info)
                result["screenshots"][f"{view}-{lang}"] = list(image.size)
    if args.reference:
        source = (ROOT / "docs" / "tour" / "index.html").read_text(encoding="utf-8")
        reference = args.reference.read_text(encoding="utf-8")
        for name, pattern in {
            "choreography": r"function render\(t\) \{.*?(?=window\.ready)",
            "timings": r"const SCENES = .*?const DURATION = 77;",
            "script": r"const T = \{.*?\}\[LANG\];",
            "easing": r"const clamp = .*?(?=// Scenes:)",
        }.items():
            a = re.search(pattern, source, re.S).group(0).strip()
            b = re.search(pattern, reference, re.S).group(0).strip()
            assert a == b, f"Original {name} changed"
            result[name + "Identical"] = True
    output = ROOT / "obj" / "tour-work" / "verification.json"
    output.write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
