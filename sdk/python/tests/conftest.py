"""Shared fixtures for the keyframe_sdk harness.

Environment: KEYFRAME_URL (default http://127.0.0.1:8710), KEYFRAME_ADMIN_KEY (default keyframeadmin),
KEYFRAME_ACCESS_KEY (default default). Media fixtures are generated with ffmpeg (or $FFMPEG) at run time.
"""

from __future__ import annotations

import os
import shutil
import subprocess
from dataclasses import dataclass
from pathlib import Path
from typing import Iterator, Optional

import pytest

from keyframe_sdk import KeyframeClient

URL = os.environ.get("KEYFRAME_URL") or "http://127.0.0.1:8710"
ADMIN_KEY = os.environ.get("KEYFRAME_ADMIN_KEY") or "keyframeadmin"
ACCESS_KEY = os.environ.get("KEYFRAME_ACCESS_KEY") or "default"
FFMPEG = os.environ.get("FFMPEG") or "ffmpeg"


@dataclass
class Media:
    image: Path
    video: Path
    long_video: Path
    audio: Path
    available: bool
    reason: Optional[str]

    def require(self) -> None:
        if not self.available:
            pytest.skip(self.reason or "media fixtures unavailable")


def _video_args(seconds: int, path: Path) -> list:
    return [FFMPEG, "-y", "-loglevel", "error", "-f", "lavfi", "-i", f"testsrc=duration={seconds}:size=320x240:rate=25",
            "-f", "lavfi", "-i", f"sine=frequency=440:duration={seconds}", "-c:v", "libx264", "-pix_fmt", "yuv420p",
            "-c:a", "aac", "-shortest", str(path)]


def _audio_args(seconds: int, path: Path) -> list:
    return [FFMPEG, "-y", "-loglevel", "error", "-f", "lavfi", "-i", f"sine=frequency=330:duration={seconds}",
            "-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le", str(path)]


@pytest.fixture(scope="session")
def admin() -> Iterator[KeyframeClient]:
    with KeyframeClient(URL, ADMIN_KEY) as client:
        if not client.health.is_alive():
            pytest.exit(f"Server at {URL} is not reachable. Start it or set KEYFRAME_URL.", returncode=2)
        yield client


@pytest.fixture(scope="session")
def user() -> Iterator[KeyframeClient]:
    with KeyframeClient(URL, ACCESS_KEY) as client:
        yield client


@pytest.fixture(scope="session")
def anonymous() -> Iterator[KeyframeClient]:
    with KeyframeClient(URL) as client:
        yield client


@pytest.fixture(scope="session")
def media(tmp_path_factory: pytest.TempPathFactory) -> Media:
    directory = tmp_path_factory.mktemp("keyframe-media")
    image, video, long_video = directory / "image.jpg", directory / "video.mp4", directory / "long.mp4"
    audio = directory / "audio.wav"
    if shutil.which(FFMPEG) is None:
        return Media(image, video, long_video, audio, False, f"{FFMPEG} not found on PATH")
    try:
        subprocess.run([FFMPEG, "-y", "-loglevel", "error", "-f", "lavfi", "-i", "testsrc2=size=640x360", "-frames:v", "1", str(image)],
                       check=True, capture_output=True)
        subprocess.run(_video_args(6, video), check=True, capture_output=True)
        subprocess.run(_video_args(30, long_video), check=True, capture_output=True)
        subprocess.run(_audio_args(5, audio), check=True, capture_output=True)
    except subprocess.CalledProcessError as e:
        return Media(image, video, long_video, audio, False, "ffmpeg failed: " + e.stderr.decode("utf-8", "replace"))
    ok = image.exists() and video.exists() and long_video.exists() and audio.exists()
    return Media(image, video, long_video, audio, ok, None if ok else "ffmpeg did not produce every fixture")
