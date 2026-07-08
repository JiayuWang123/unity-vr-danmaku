#!/usr/bin/env python3
"""从 tts_candidates_no_overlap.json 生成 Unity 音频排期与环境音安排。"""

import json
import os
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
STREAMING = ROOT / "Assets" / "StreamingAssets"
INPUT = STREAMING / "tts_candidates_no_overlap.json"
VIDEO_DURATION = 600.0  # 10 分钟

# 混音比例
TTS_VOLUME = 1.0
ENABLE_CROWD_AUDIO = False
DUCK_VIDEO_VOLUME = 0.18
# 仅 ENABLE_CROWD_AUDIO=True 时使用
BASELINE_AMBIENT_VOLUME = 0.12
CROWD_VOLUME_SCALE = 0.48

MIX_TO_CROWD = {
    "excited_commentary_over_cheer": {
        "clip": "cheer_long",
        "spatial": "surround",
        "atmosphere": "Excited",
    },
    "analysis_over_ambient": {
        "clip": "normal_ambient",
        "spatial": "surround",
        "atmosphere": "Neutral",
    },
    "standalone_tts": {
        "clip": "normal_ambient",
        "spatial": "directional",
        "atmosphere": "Neutral",
    },
    "dispute_over_tension": {
        "clip": "tension_heart",
        "spatial": "surround",
        "atmosphere": "Tension",
    },
    "chant_with_crowd": {
        "clip": "cheer_long",
        "spatial": "surround",
        "atmosphere": "Excited",
    },
}

ANCHOR_TO_TARGET = {
    "seat_left": ["left"],
    "seat_right": ["right"],
    "seat_front": ["front"],
    "seat_back": ["back"],
}


def anchor_targets(entry):
    spatial = MIX_TO_CROWD.get(entry["tts_mix_type"], MIX_TO_CROWD["analysis_over_ambient"])[
        "spatial"
    ]
    if spatial == "surround":
        return ["front", "back", "left", "right"]
    anchor = entry.get("spatial_anchor", "seat_front")
    return ANCHOR_TO_TARGET.get(anchor, ["front"])


def load_candidates():
    with open(INPUT, encoding="utf-8") as f:
        data = json.load(f)
    if isinstance(data, dict) and "tts_segments" in data:
        return data["tts_segments"]
    return data


def build_audio_schedule(candidates):
    events = []
    for i, c in enumerate(candidates, start=1):
        seg_id = f"tts_{i:03d}"
        start = float(c["time_sec"])
        dur = float(c.get("duration_sec", max(1.5, len(c["text"]) * 0.22)))
        tts_vol = TTS_VOLUME
        events.append(
            {
                "id": seg_id,
                "start_sec": round(start, 3),
                "duration_sec": round(dur, 3),
                "end_sec": round(start + dur, 3),
                "zone": "neutral",
                "camp": "neutral",
                "speaker_role": c.get("speaker_role", "neutral_fan"),
                "text": c["text"],
                "audio_clip": f"Audio/TTS/{seg_id}.mp3",
                "volume": round(tts_vol, 3),
                "spatial_anchor": c.get("spatial_anchor", "seat_front"),
                "priority": float(c.get("priority", 0.5)),
                "duck_video": True,
                "tts_mix_type": c.get("tts_mix_type", "analysis_over_ambient"),
                "preferred_crowd_duck_volume": float(
                    c.get("preferred_crowd_duck_volume", 0.5)
                ),
            }
        )
    return {
        "schema_version": "1.0",
        "video_id": "football_10min",
        "time_base": "video_sec",
        "duck_video_volume": DUCK_VIDEO_VOLUME,
        "default_min_gap_sec": 0.8,
        "events": events,
        "schedule_stats": {
            "input_candidates": len(candidates),
            "scheduled": len(events),
            "dropped": 0,
        },
    }


def merge_crowd_events(raw_events):
    """按 clip+targets 合并重叠区间，保留较高音量。"""
    if not raw_events:
        return []
    raw_events.sort(key=lambda e: (e["clip"], ",".join(e["targets"]), e["start_sec"]))
    merged = []
    for ev in raw_events:
        if not merged:
            merged.append(ev)
            continue
        last = merged[-1]
        same = last["clip"] == ev["clip"] and last["targets"] == ev["targets"]
        if same and ev["start_sec"] <= last["stop_sec"] + 0.05:
            last["stop_sec"] = max(last["stop_sec"], ev["stop_sec"])
            last["volume"] = max(last["volume"], ev["volume"])
            if ev.get("note"):
                last["note"] = (last.get("note", "") + " | " + ev["note"]).strip(" |")
        else:
            merged.append(ev)
    return merged


def build_crowd_schedule(candidates):
    if not ENABLE_CROWD_AUDIO:
        return {
            "schema_version": "1.0",
            "video_id": "football_10min",
            "events": [],
        }

    events = []

    # 10 分钟底噪：正常赛场环境
    events.append(
        {
            "id": "crowd_baseline",
            "start_sec": 0.0,
            "stop_sec": VIDEO_DURATION,
            "clip": "normal_ambient",
            "volume": BASELINE_AMBIENT_VOLUME,
            "zone": "all",
            "spatial": "surround",
            "targets": ["front", "back", "left", "right"],
            "loop": True,
            "note": "10分钟底噪",
        }
    )

    for i, c in enumerate(candidates, start=1):
        mix = MIX_TO_CROWD.get(c.get("tts_mix_type"), MIX_TO_CROWD["analysis_over_ambient"])
        start = float(c["time_sec"])
        dur = float(c.get("duration_sec", max(1.5, len(c["text"]) * 0.22)))
        stop = min(VIDEO_DURATION, start + dur)
        pre = 0.8 if mix["clip"].startswith("cheer") else 0.3
        events.append(
            {
                "id": f"crowd_tts_{i:03d}",
                "start_sec": round(max(0.0, start - pre), 3),
                "stop_sec": round(stop, 3),
                "clip": mix["clip"],
                "volume": round(
                    min(1.0, float(c.get("preferred_crowd_duck_volume", 0.5)) * CROWD_VOLUME_SCALE),
                    3,
                ),
                "zone": "all",
                "spatial": mix["spatial"],
                "targets": anchor_targets(c),
                "loop": True,
                "note": c.get("tts_mix_type", ""),
            }
        )

    events = merge_crowd_events(events)
    return {
        "schema_version": "1.0",
        "video_id": "football_10min",
        "events": events,
    }


def build_atmosphere_schedule(candidates):
    """根据 tts_mix_type 推荐氛围灯光窗口。"""
    events = [{"start_sec": 0.0, "stop_sec": VIDEO_DURATION, "mode": "Neutral", "note": "默认"}]
    for i, c in enumerate(candidates, start=1):
        mix = MIX_TO_CROWD.get(c.get("tts_mix_type"), MIX_TO_CROWD["analysis_over_ambient"])
        start = float(c["time_sec"])
        dur = float(c.get("duration_sec", max(1.5, len(c["text"]) * 0.22)))
        events.append(
            {
                "start_sec": round(max(0.0, start - 0.5), 3),
                "stop_sec": round(min(VIDEO_DURATION, start + dur + 0.8), 3),
                "mode": mix["atmosphere"],
                "note": f"tts_{i:03d}:{c.get('tts_mix_type')}",
            }
        )
    return {
        "schema_version": "1.0",
        "video_id": "football_10min",
        "events": events,
    }


def main():
    if not INPUT.exists():
        raise SystemExit(f"Missing input: {INPUT}")

    candidates = load_candidates()
    audio = build_audio_schedule(candidates)
    crowd = build_crowd_schedule(candidates)
    atmosphere = build_atmosphere_schedule(candidates)

    out_audio = STREAMING / "audio_schedule.json"
    out_crowd = STREAMING / "crowd_audio_schedule.json"
    out_atmo = STREAMING / "atmosphere_schedule.json"

    for path, payload in [
        (out_audio, audio),
        (out_crowd, crowd),
        (out_atmo, atmosphere),
    ]:
        with open(path, "w", encoding="utf-8") as f:
            json.dump(payload, f, ensure_ascii=False, indent=2)
        print(f"Wrote {path} ({len(payload.get('events', []))} events)")

    print(f"Scheduled {len(audio['events'])} TTS clips for {VIDEO_DURATION}s video.")


if __name__ == "__main__":
    main()
