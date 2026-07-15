#!/usr/bin/env python3

"""根据 TTS 候选 JSON 批量生成 mp3（edge-tts，限并发+重试）。"""

import argparse
import asyncio
import json
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
STREAMING = ROOT / "Assets" / "StreamingAssets"
DEFAULT_CANDIDATES = STREAMING / "Audio" / "tts_candidates_no_overlap.json"
DEFAULT_SCHEDULE = STREAMING / "audio_schedule.json"
DEFAULT_OUT_DIR = STREAMING / "Audio" / "TTS"

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


def resolve_path(relative: str) -> Path:
    rel = relative.replace("\\", "/")
    if rel.startswith("Assets/StreamingAssets/"):
        return ROOT / rel
    return STREAMING / rel


def load_events(candidates_path: Path, schedule_path: Path, out_dir_rel: str = "Audio/TTS"):
    if candidates_path.exists():
        with open(candidates_path, encoding="utf-8") as f:
            data = json.load(f)
        if isinstance(data, dict) and "tts_segments" in data:
            items = data["tts_segments"]
        elif isinstance(data, dict) and "events" in data:
            items = data["events"]
        else:
            items = data

        events = []
        folder = out_dir_rel.replace("\\", "/").rstrip("/")
        for i, item in enumerate(items, start=1):
            seg_id = f"tts_{i:03d}"
            events.append(
                {
                    "id": seg_id,
                    "text": item.get("text", ""),
                    "speaker_role": item.get("speaker_role", "neutral_fan"),
                    "audio_clip": f"{folder}/{seg_id}.mp3",
                }
            )
        return events

    if not schedule_path.exists():
        raise SystemExit(
            f"Neither candidates nor schedule found:\n  {candidates_path}\n  {schedule_path}"
        )

    with open(schedule_path, encoding="utf-8") as f:
        schedule = json.load(f)
    return schedule.get("events", [])


async def synth_one(text, voice, out_path, sem):
    import edge_tts

    async with sem:
        for attempt in range(1, MAX_RETRIES + 1):
            try:
                communicate = edge_tts.Communicate(text, voice)
                await communicate.save(str(out_path))
                print(f"OK {out_path.name} ({out_path.stat().st_size} bytes)")
                return
            except Exception as exc:
                if attempt >= MAX_RETRIES:
                    raise
                wait = attempt * 1.5
                print(f"Retry {out_path.name} ({attempt}/{MAX_RETRIES}): {exc}")
                await asyncio.sleep(wait)


async def main_async(args):
    ensure_edge_tts()

    candidates_path = resolve_path(args.candidates)
    schedule_path = resolve_path(args.schedule)
    out_dir = resolve_path(args.out_dir)

    events = load_events(candidates_path, schedule_path, args.out_dir)
    if not events:
        raise SystemExit("No TTS events found.")

    out_dir.mkdir(parents=True, exist_ok=True)
    sem = asyncio.Semaphore(MAX_CONCURRENT)
    tasks = []

    for ev in events:
        rel = ev.get("audio_clip", "")
        if not rel:
            continue

        out_path = resolve_path(rel)
        if args.out_dir:
            out_path = out_dir / Path(rel).name

        out_path.parent.mkdir(parents=True, exist_ok=True)

        if out_path.exists() and out_path.stat().st_size > 512 and not args.force:
            print(f"Skip existing {out_path.name}")
            continue

        voice = ROLE_VOICE.get(ev.get("speaker_role"), "zh-CN-YunxiNeural")
        text = ev.get("text", "")
        if not text.strip():
            print(f"Skip empty text for {out_path.name}")
            continue

        preview = text[:32] + ("..." if len(text) > 32 else "")
        print(f"Queue {out_path.name}: {preview}")
        tasks.append(synth_one(text, voice, out_path, sem))

    if tasks:
        await asyncio.gather(*tasks)

    generated = sorted(out_dir.glob("tts_*.mp3"))
    print(f"Done. {len(generated)} clips in {out_dir}")


def main():
    parser = argparse.ArgumentParser(description="Generate TTS mp3 clips for Unity.")
    parser.add_argument(
        "--candidates",
        default="Audio/tts_candidates_no_overlap.json",
        help="Relative path under StreamingAssets (default: Audio/tts_candidates_no_overlap.json)",
    )
    parser.add_argument(
        "--schedule",
        default="audio_schedule.json",
        help="Fallback schedule JSON under StreamingAssets",
    )
    parser.add_argument(
        "--out-dir",
        default="Audio/TTS",
        help="Output directory relative to StreamingAssets (default: Audio/TTS)",
    )
    parser.add_argument(
        "--force",
        action="store_true",
        help="Regenerate even if mp3 already exists",
    )
    args = parser.parse_args()
    asyncio.run(main_async(args))


if __name__ == "__main__":
    main()
