#!/usr/bin/env python3
"""根据 audio_schedule.json 批量生成 TTS mp3（edge-tts，限并发+重试）。"""

import asyncio
import json
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
STREAMING = ROOT / "Assets" / "StreamingAssets"
SCHEDULE = STREAMING / "audio_schedule.json"
OUT_DIR = STREAMING / "Audio" / "TTS"
MAX_CONCURRENT = 4
MAX_RETRIES = 3

ROLE_VOICE = {
    "excited_fan": "zh-CN-YunxiNeural",
    "tactical_fan": "zh-CN-YunjianNeural",
    "neutral_fan": "zh-CN-XiaoxiaoNeural",
    "angry_fan": "zh-CN-YunyangNeural",
}


def ensure_edge_tts():
    try:
        import edge_tts  # noqa: F401
        return True
    except ImportError:
        print("Installing edge-tts ...")
        subprocess.check_call([sys.executable, "-m", "pip", "install", "edge-tts"])
        return True


async def synth_one(text, voice, out_path, sem):
    import edge_tts

    async with sem:
        for attempt in range(1, MAX_RETRIES + 1):
            try:
                communicate = edge_tts.Communicate(text, voice)
                await communicate.save(str(out_path))
                print(f"OK {out_path.name}")
                return
            except Exception as exc:
                if attempt >= MAX_RETRIES:
                    raise
                wait = attempt * 1.5
                print(f"Retry {out_path.name} ({attempt}/{MAX_RETRIES}): {exc}")
                await asyncio.sleep(wait)


async def main_async():
    ensure_edge_tts()
    if not SCHEDULE.exists():
        raise SystemExit("Run generate_audio_schedules.py first.")

    with open(SCHEDULE, encoding="utf-8") as f:
        schedule = json.load(f)

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    sem = asyncio.Semaphore(MAX_CONCURRENT)
    tasks = []

    for ev in schedule.get("events", []):
        rel = ev.get("audio_clip", "")
        if not rel:
            continue
        out_path = STREAMING / rel.replace("\\", "/")
        out_path.parent.mkdir(parents=True, exist_ok=True)
        if out_path.exists() and out_path.stat().st_size > 512:
            print(f"Skip existing {out_path.name}")
            continue
        voice = ROLE_VOICE.get(ev.get("speaker_role"), "zh-CN-YunxiNeural")
        text = ev.get("text", "")
        print(f"Queue {out_path.name}: {text[:24]}...")
        tasks.append(synth_one(text, voice, out_path, sem))

    if tasks:
        await asyncio.gather(*tasks)
    print(f"Done. Clips in {OUT_DIR}")


def main():
    asyncio.run(main_async())


if __name__ == "__main__":
    main()
