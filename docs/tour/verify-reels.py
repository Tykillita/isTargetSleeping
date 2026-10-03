"""Verify Reels geometry, complete streams, synchronized audio and source preservation."""
import hashlib
import json
from pathlib import Path
import struct
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
WORK = ROOT / "obj" / "tour-work"
sys.path.insert(0, str(ROOT / "obj" / "tour-tooling"))
import imageio_ffmpeg
import numpy as np
from PIL import Image


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def audio(path, compressed=False):
    cmd = [imageio_ffmpeg.get_ffmpeg_exe(), "-v", "error", "-i", str(path), "-map", "0:a:0"]
    cmd += ["-c:a", "copy", "-f", "adts", "-"] if compressed else ["-ar", "48000", "-ac", "2", "-f", "f32le", "-"]
    data = subprocess.check_output(cmd)
    return hashlib.sha256(data).hexdigest() if compressed else np.frombuffer(data, dtype="<f4").reshape(-1, 2)


def fast_start(path):
    boxes = {}
    with path.open("rb") as source:
        while source.tell() < path.stat().st_size:
            offset = source.tell()
            size, name = struct.unpack(">I4s", source.read(8))
            if size == 1:
                size = struct.unpack(">Q", source.read(8))[0]
            elif size == 0:
                size = path.stat().st_size - offset
            boxes[name] = offset
            source.seek(offset + size)
    return boxes[b"moov"] < boxes[b"mdat"]


def main():
    report = {"videos": {}, "mouseClicks": [], "originalsPreserved": {}}
    baseline = json.loads((WORK / "reel-originals.json").read_text())
    for lang in ("es", "en"):
        expected = baseline[lang]
        assert sha(ROOT / "docs" / "video" / f"isTargetSleeping-tour-{lang}.mp4") == expected, f"Original video changed: {lang}"
        report["originalsPreserved"][lang] = True
    assert sha(ROOT / "docs" / "audio" / "tour-soundtrack.m4a") == baseline["soundtrack"]

    original = audio(ROOT / "docs" / "audio" / "tour-soundtrack.m4a")[:48000 * 77]
    mixed = audio(WORK / "reel-mix.wav")
    clicks = audio(WORK / "reel-clicks.wav")
    assert len(mixed) == len(clicks) == 48000 * 77
    assert np.max(np.abs(mixed - original - clicks)) < 4 / 8388607, "Music level or timing changed in the PCM mix"
    assert np.max(np.abs(mixed)) < .98, "Mix clips"
    timeline = json.loads((ROOT / "docs" / "tour" / "reel-events.json").read_text())
    mask = np.zeros(len(clicks), dtype=bool)
    for event in timeline:
        for offset in (0, .09):
            start = round((event["time"] + offset) * 48000)
            mask[start:start + round(.055 * 48000)] = True
            assert np.max(np.abs(clicks[start:start + 2640])) > .01, f"Missing click: {event}"
        report["mouseClicks"].append({"time": event["time"], "target": event["target"], "release": event["time"] + .09})
    assert np.max(np.abs(clicks[~mask])) == 0, "Unexpected click outside the visual timeline"
    expected_audio = audio(WORK / "reel-soundtrack.m4a", compressed=True)
    for lang in ("es", "en"):
        video = ROOT / "docs" / "video" / f"isTargetSleeping-reel-{lang}.mp4"
        stream = imageio_ffmpeg.read_frames(str(video))
        metadata = next(stream); stream.close()
        frames, seconds = imageio_ffmpeg.count_frames_and_secs(str(video))
        assert metadata["size"] == (1080, 1920) and metadata["fps"] == 30, metadata
        assert frames == 2310 and abs(seconds - 77) < .001, (frames, seconds)
        assert "h264" in metadata["codec"] and "yuv420p" in metadata["pix_fmt"], metadata
        assert audio(video, compressed=True) == expected_audio, f"Mix changed: {lang}"
        assert np.max(np.abs(audio(video))) < 1, "Encoded audio clips"
        assert fast_start(video), "Metadata must precede video data for mobile playback"
        with Image.open(ROOT / "docs" / "images" / f"reel-{lang}.jpg") as poster:
            assert poster.size == (1080, 1920)
        report["videos"][lang] = {"width": 1080, "height": 1920, "fps": 30, "frames": frames,
                                  "seconds": seconds, "bytes": video.stat().st_size, "audioIdentical": True, "fastStart": True}
    (WORK / "reel-verification.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
