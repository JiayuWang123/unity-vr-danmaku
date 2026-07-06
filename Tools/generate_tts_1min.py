#!/usr/bin/env python3
"""从 audio_schedule_1min.json 生成 TTS mp3（需要: pip install edge-tts）"""
import asyncio
import json
import os
import sys

try:
    import edge_tts
except ImportError:
    print("请先安装: pip install edge-tts")
    sys.exit(1)

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SCHEDULE = os.path.join(ROOT, "Assets", "StreamingAssets", "AudioData", "audio_schedule_1min.json")
OUT_DIR = os.path.join(ROOT, "Assets", "Resources", "Audio", "TTS")

MALE_VOICE_MAP = {
    "analytical": "zh-CN-YunxiNeural",
    "anger_confusion": "zh-CN-YunjianNeural",
    "anger": "zh-CN-YunjianNeural",
    "joy": "zh-CN-YunjianNeural",
    "excitement": "zh-CN-YunjianNeural",
    "tension": "zh-CN-YunxiNeural",
    "default": "zh-CN-YunxiNeural",
}

FEMALE_VOICE_MAP = {
    "analytical": "zh-CN-XiaoyiNeural",
    "anger_confusion": "zh-CN-XiaohanNeural",
    "anger": "zh-CN-XiaohanNeural",
    "joy": "zh-CN-XiaoxiaoNeural",
    "excitement": "zh-CN-XiaoxiaoNeural",
    "tension": "zh-CN-XiaoyiNeural",
    "default": "zh-CN-XiaoyiNeural",
}


def pick_voice(emotion: str, gender: str) -> str:
    gender = (gender or "male").lower()
    voice_map = FEMALE_VOICE_MAP if gender == "female" else MALE_VOICE_MAP
    return voice_map.get(emotion, voice_map["default"])


async def synthesize_one(text: str, voice: str, out_path: str, rate: str = "+0%"):
    last_err = None
    for attempt in range(3):
        try:
            communicate = edge_tts.Communicate(text, voice, rate=rate)
            await communicate.save(out_path)
            return
        except Exception as err:
            last_err = err
            await asyncio.sleep(2 + attempt * 2)
    raise last_err


async def main():
    with open(SCHEDULE, encoding="utf-8") as f:
        data = json.load(f)

    os.makedirs(OUT_DIR, exist_ok=True)
    events = data.get("events", [])

    for e in events:
        eid = e["id"]
        text = e["text"]
        emotion = e.get("emotion", "default")
        gender = e.get("speaker_gender", "male")
        voice = pick_voice(emotion, gender)
        speed = e.get("speed", 1.0)
        rate_pct = int((float(speed) - 1.0) * 100)
        rate = f"{rate_pct:+d}%"
        out_file = os.path.join(OUT_DIR, f"{eid}.mp3")
        clip_path = f"Audio/TTS/{eid}"

        tag = "女声" if gender == "female" else "男声"
        print(f"生成 {eid} [{tag}/{voice}]: {text[:24]}... -> {out_file}")
        await synthesize_one(text, voice, out_file, rate)

        e["audio_clip"] = clip_path
        e["audio_clip_is_placeholder"] = False

    with open(SCHEDULE, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)

    print(f"\n完成: {len(events)} 条 TTS -> {OUT_DIR}")
    print(f"已更新 JSON: {SCHEDULE}")


if __name__ == "__main__":
    asyncio.run(main())
