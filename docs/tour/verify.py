"""Verify the bilingual notch tour, current app captures and unchanged soundtrack."""
import hashlib
import json
from pathlib import Path
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
    expected_audio = audio_hash(ROOT / "docs" / "audio" / "tour-soundtrack.m4a")
    source = (ROOT / "docs" / "tour" / "index.html").read_text(encoding="utf-8").lower()
    assert 'iframe id="simulator"' in source
    assert 'button[data-s="ollama"]{display:none!important}' in source
    assert 'tour-ui-widgets-${lang}.png' in source and 'tour-ui-display-${lang}.png' in source
    renderer = (ROOT / "docs" / "tour" / "render.cjs").read_text(encoding="utf-8")
    assert "nextFrameAt" in renderer and "performance.now()" in renderer
    assert "site/index.html?lang=${lang}#probar" not in source
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
        for view in ("widgets", "display"):
            with Image.open(ROOT / "docs" / "images" / f"tour-ui-{view}-{lang}.png") as image:
                assert image.size == (1960, 1320), (view, image.size)
                result["screenshots"][f"tour-ui-{view}-{lang}"] = list(image.size)
        with Image.open(ROOT / "docs" / "images" / f"tour-{lang}.jpg") as image:
            assert image.size == (1920, 1080), (lang, image.size)
    output = ROOT / "obj" / "tour-work" / "verification.json"
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
