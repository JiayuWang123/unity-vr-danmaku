#!/usr/bin/env python3
"""Build an emotion-only TTS schedule for Unity from the W2 emotion danmaku table."""

from __future__ import annotations

import argparse
import csv
import json
import math
import re
from collections import Counter, defaultdict
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SURF_ROOT = ROOT.parents[1]
DEFAULT_INPUT = SURF_ROOT / "Danmu" / "json" / "W2" / "supportive_interactions_emotion_from_excel.json"
STREAMING = ROOT / "Assets" / "StreamingAssets"
DEFAULT_TARGET_COUNT = 45

ANCHORS = ["seat_left", "seat_right", "seat_front", "seat_back"]

STRONG_POSITIVE = [
    "神",
    "球王",
    "风范",
    "兴奋",
    "vamos",
    "冲",
    "救赎",
    "庆祝",
    "圆满",
    "激动",
    "热泪盈眶",
    "精彩",
]
TENSION_TERMS = [
    "压力",
    "吓",
    "迷茫",
    "汗",
    "绷不住",
    "what",
    "wide",
    "？",
    "?",
    "！?",
    "！？",
]
LAUGHTER_TERMS = ["哈", "hhh", "笑"]
PUNCT_RE = re.compile(r"^[!?！？?。.，,、…~\-+]+$")


def load_items(path: Path) -> list[dict]:
    data = json.loads(path.read_text(encoding="utf-8"))
    if isinstance(data, dict) and isinstance(data.get("items"), list):
        return data["items"]
    if isinstance(data, list):
        return data
    raise ValueError(f"Unsupported input shape: {path}")


def text_value(item: dict) -> str:
    return str(item.get("弹幕内容", "")).strip()


def time_value(item: dict) -> float:
    return float(item.get("新视频中的时间", 0.0))


def sentiment_value(item: dict) -> str:
    return str(item.get("正反面情绪", "")).strip() or "unknown"


def mmss(seconds: float) -> str:
    whole = int(seconds)
    millis = int(round((seconds - whole) * 1000))
    if millis >= 1000:
        whole += 1
        millis -= 1000
    return f"{whole // 60:02d}:{whole % 60:02d}.{millis:03d}"


def normalize_spoken_text(raw: str) -> str:
    text = raw.strip()
    lower = text.lower()

    if PUNCT_RE.fullmatch(text):
        return ""

    if "666" in text:
        text = text.replace("666", "六六六")

    if "hhhh" in lower or "哈哈" in text:
        if "笑死" in text:
            return "哈哈，笑死了。"
        if len(text) >= 8:
            return "哈哈哈哈。"
        return text

    text = re.sub(r"[!！]{2,}", "！", text)
    text = re.sub(r"[?？]{2,}", "？", text)
    text = re.sub(r"[.。]{3,}", "。", text)
    return text


def classify_role(raw: str, sentiment: str) -> tuple[str, str, float]:
    lower = raw.lower()
    if any(term in lower for term in ["vamos"]) or raw in {"冲", "救赎", "庆祝"}:
        return "chant_fan", "chant_with_crowd", 0.92
    if any(term.lower() in lower for term in LAUGHTER_TERMS):
        return "amused_fan", "laugh_reaction", 0.88
    if any(term.lower() in lower for term in TENSION_TERMS) or sentiment == "negative":
        return "tense_fan", "tension_reaction", 0.82
    if any(term.lower() in lower for term in STRONG_POSITIVE) or sentiment == "positive":
        return "excited_fan", "excited_reaction", 0.86
    return "neutral_fan", "short_reaction", 0.70


def score_item(raw: str, spoken: str, sentiment: str, duplicate_count: int) -> float:
    lower = raw.lower()
    score = 0.50
    length = len(raw)
    if 1 <= length <= 8:
        score += 0.12
    elif length <= 12:
        score += 0.08
    elif length <= 20:
        score += 0.02
    else:
        score -= 0.20

    if sentiment == "positive":
        score += 0.05
    elif sentiment == "negative":
        score += 0.04

    if PUNCT_RE.fullmatch(raw):
        score += 0.10
    if any(term.lower() in lower for term in LAUGHTER_TERMS):
        score += 0.13
    if any(term.lower() in lower for term in STRONG_POSITIVE):
        score += 0.12
    if any(term.lower() in lower for term in TENSION_TERMS):
        score += 0.10
    if duplicate_count > 1:
        score += min(0.10, math.log2(duplicate_count + 1) * 0.03)
    if not spoken:
        score -= 1.0
    return round(max(0.0, min(1.0, score)), 3)


def estimate_duration(spoken: str) -> float:
    ascii_chars = sum(1 for ch in spoken if ord(ch) < 128 and ch.isalnum())
    cjk_chars = sum(1 for ch in spoken if ord(ch) >= 128 and not ch.isspace())
    punctuation = sum(1 for ch in spoken if ch in "，,。.!！?？")
    seconds = 0.45 + cjk_chars * 0.18 + ascii_chars * 0.08 + punctuation * 0.10
    return round(max(0.75, min(3.8, seconds)), 3)


def duplicate_counts(items: list[dict]) -> dict[int, int]:
    counts_by_index: dict[int, int] = {}
    by_text: dict[str, list[tuple[int, float]]] = defaultdict(list)
    for idx, item in enumerate(items):
        by_text[text_value(item)].append((idx, time_value(item)))

    for rows in by_text.values():
        for idx, t in rows:
            count = sum(1 for _, other_t in rows if abs(other_t - t) <= 10.0)
            counts_by_index[idx] = count
    return counts_by_index


def choose_candidates(items: list[dict], target_count: int) -> list[dict]:
    dup_counts = duplicate_counts(items)
    candidates = []
    for idx, item in enumerate(items):
        raw = text_value(item)
        if not raw or PUNCT_RE.fullmatch(raw):
            continue
        spoken = normalize_spoken_text(raw)
        sentiment = sentiment_value(item)
        role, mix, role_base = classify_role(raw, sentiment)
        priority = max(role_base, score_item(raw, spoken, sentiment, dup_counts.get(idx, 1)))
        candidates.append(
            {
                "source_index": idx,
                "source_comment_id": f"emotion_{idx + 1:03d}",
                "time_sec": round(time_value(item), 3),
                "time_mmss": mmss(time_value(item)),
                "text_raw": raw,
                "spoken_text": spoken,
                "sentiment": sentiment,
                "speaker_role": role,
                "tts_mix_type": mix,
                "priority": round(priority, 3),
                "duplicate_count": dup_counts.get(idx, 1),
                "is_punctuation": bool(PUNCT_RE.fullmatch(raw)),
            }
        )

    candidates.sort(key=lambda c: (c["time_sec"], c["source_index"]))

    selected = []
    selected_ids = set()
    per_three_sec: Counter[int] = Counter()
    per_burstish_ten_sec: Counter[int] = Counter()
    punctuation_kept = 0

    def can_take(c: dict) -> bool:
        three_key = int(c["time_sec"] // 3)
        ten_key = int(c["time_sec"] // 10)
        if per_three_sec[three_key] >= 3:
            return False
        if per_burstish_ten_sec[ten_key] >= 5:
            return False
        return True

    def take(c: dict) -> None:
        nonlocal punctuation_kept
        three_key = int(c["time_sec"] // 3)
        ten_key = int(c["time_sec"] // 10)
        selected.append(c)
        selected_ids.add(c["source_comment_id"])
        per_three_sec[three_key] += 1
        per_burstish_ten_sec[ten_key] += 1
        if c["is_punctuation"]:
            punctuation_kept += 1

    # First pass: keep the timeline legible by taking the best candidate in each 15s slice.
    buckets: dict[int, list[dict]] = defaultdict(list)
    for c in candidates:
        buckets[int(c["time_sec"] // 15)].append(c)
    for bucket in sorted(buckets):
        best = sorted(buckets[bucket], key=lambda c: (-c["priority"], c["time_sec"]))[0]
        if can_take(best):
            take(best)
        if len(selected) >= target_count:
            selected.sort(key=lambda c: (c["time_sec"], c["source_index"]))
            return selected

    # Second pass: fill the remaining slots with the strongest reactions.
    for c in sorted(candidates, key=lambda c: (-c["priority"], c["time_sec"], c["source_index"])):
        if c["source_comment_id"] in selected_ids:
            continue
        if not can_take(c):
            continue
        take(c)
        if len(selected) >= target_count:
            break

    selected.sort(key=lambda c: (c["time_sec"], c["source_index"]))
    return selected


def assign_spatial_and_offsets(candidates: list[dict]) -> None:
    anchor_cursor = 0
    recent_by_window: dict[int, set[str]] = defaultdict(set)
    exact_time_counts: Counter[float] = Counter()

    for c in candidates:
        key = int(c["time_sec"] // 3)
        used = recent_by_window[key]
        anchor = None
        for _ in ANCHORS:
            candidate = ANCHORS[anchor_cursor % len(ANCHORS)]
            anchor_cursor += 1
            if candidate not in used:
                anchor = candidate
                break
        if anchor is None:
            anchor = ANCHORS[anchor_cursor % len(ANCHORS)]
            anchor_cursor += 1
        used.add(anchor)
        c["spatial_anchor"] = anchor

        exact_key = round(c["time_sec"], 3)
        same_time_index = exact_time_counts[exact_key]
        exact_time_counts[exact_key] += 1
        # Keep the event at the recorded time, but soften identical sample starts.
        c["start_sec"] = round(c["time_sec"] + min(0.24, same_time_index * 0.12), 3)


def build_schedule(candidates: list[dict], audio_ext: str) -> dict:
    events = []
    for i, c in enumerate(candidates, start=1):
        event_id = f"emotion_tts_{i:03d}"
        duration = estimate_duration(c["spoken_text"])
        event = {
            "id": event_id,
            "start_sec": c["start_sec"],
            "original_time_sec": c["time_sec"],
            "time_mmss": c["time_mmss"],
            "duration_sec": duration,
            "end_sec": round(c["start_sec"] + duration, 3),
            "zone": "neutral",
            "camp": "neutral",
            "speaker_role": c["speaker_role"],
            "text": c["spoken_text"],
            "text_raw": c["text_raw"],
            "spoken_text": c["spoken_text"],
            "audio_clip": f"Audio/TTS/{event_id}.{audio_ext}",
            "volume": volume_for(c),
            "spatial_anchor": c["spatial_anchor"],
            "priority": c["priority"],
            "duck_video": False,
            "allow_overlap": True,
            "tts_mix_type": c["tts_mix_type"],
            "sentiment": c["sentiment"],
            "emotion_hint": c["tts_mix_type"],
            "source_comment_id": c["source_comment_id"],
            "duplicate_count": c["duplicate_count"],
        }
        c.update(event)
        events.append(event)

    return {
        "schema_version": "emotion_tts_v1",
        "video_id": "worldcup_penalty_w2",
        "time_base": "video_sec",
        "duck_video_volume": 0.0,
        "default_min_gap_sec": 0.0,
        "events": events,
        "schedule_stats": {
            "input_candidates": len(candidates),
            "scheduled": len(events),
            "dropped": 0,
            "punctuation_events": sum(1 for c in candidates if c.get("is_punctuation")),
            "overlap_allowed_events": len(events),
        },
    }


def volume_for(candidate: dict) -> float:
    mix = candidate["tts_mix_type"]
    if mix == "chant_with_crowd":
        return 0.92
    if mix == "laugh_reaction":
        return 0.84
    if mix == "tension_reaction":
        return 0.80
    if mix == "excited_reaction":
        return 0.88
    return 0.76


def write_json(path: Path, payload: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def write_mount_tables(streaming: Path, events: list[dict]) -> None:
    csv_path = streaming / "emotion_tts_mount_table.csv"
    md_path = streaming / "emotion_tts_mount_table.md"
    fields = [
        "id",
        "time_mmss",
        "original_time_sec",
        "start_sec",
        "duration_sec",
        "text_raw",
        "spoken_text",
        "speaker_role",
        "spatial_anchor",
        "audio_clip",
        "sentiment",
        "priority",
    ]

    with csv_path.open("w", encoding="utf-8-sig", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=fields)
        writer.writeheader()
        for ev in events:
            writer.writerow({field: ev.get(field, "") for field in fields})

    lines = [
        "# Emotion TTS Mount Table",
        "",
        "| # | video time | start sec | text raw | spoken text | role | anchor | audio |",
        "|---|---:|---:|---|---|---|---|---|",
    ]
    for idx, ev in enumerate(events, start=1):
        lines.append(
            "| {idx} | {time} | {start:.3f} | {raw} | {spoken} | {role} | {anchor} | {audio} |".format(
                idx=idx,
                time=ev["time_mmss"],
                start=ev["start_sec"],
                raw=escape_md(ev["text_raw"]),
                spoken=escape_md(ev["spoken_text"]),
                role=ev["speaker_role"],
                anchor=ev["spatial_anchor"],
                audio=ev["audio_clip"],
            )
        )
    md_path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def build_source_format_mount_json(events: list[dict]) -> dict:
    sentiment_counts = Counter(str(ev.get("sentiment", "unknown")) for ev in events)
    sentiment_order = ["positive", "negative"]
    sentiment_values = [
        value for value in sentiment_order if value in sentiment_counts
    ] + [
        value for value in sorted(sentiment_counts.keys()) if value not in sentiment_order and value != "unknown"
    ]
    ordered_sentiment_counts = {
        value: sentiment_counts[value]
        for value in sentiment_values
    }
    if "unknown" in sentiment_counts:
        ordered_sentiment_counts["unknown"] = sentiment_counts["unknown"]
    unknown_count = sentiment_counts.get("unknown", 0)

    items = []
    for ev in events:
        text_raw = str(ev.get("text_raw", ""))
        item = {
            "弹幕内容": text_raw,
            "长度": len(text_raw),
            "新视频中的时间": ev.get("original_time_sec", ev.get("start_sec", 0.0)),
            "正反面情绪": ev.get("sentiment", "unknown"),
            "TTS朗读文本": ev.get("spoken_text", ""),
            "TTS开始时间": ev.get("start_sec", 0.0),
            "TTS时间": ev.get("time_mmss", ""),
            "TTS角色": ev.get("speaker_role", ""),
            "TTS空间锚点": ev.get("spatial_anchor", ""),
            "TTS音频文件": ev.get("audio_clip", ""),
            "TTS音量": ev.get("volume", 0.0),
            "TTS优先级": ev.get("priority", 0.0),
            "TTS混音类型": ev.get("tts_mix_type", ""),
            "TTS允许重叠": bool(ev.get("allow_overlap", True)),
            "TTS不影响视频音量": not bool(ev.get("duck_video", False)),
            "来源弹幕ID": ev.get("source_comment_id", ""),
            "重复计数": ev.get("duplicate_count", 1),
        }
        items.append(item)

    return {
        "category": "Supportive Interactions",
        "category_zh": "情绪",
        "source": "Assets/StreamingAssets/emotion_tts_mount_table.md",
        "count": len(items),
        "sentiment_field": "正反面情绪",
        "sentiment_values": sentiment_values,
        "sentiment_counts": ordered_sentiment_counts,
        "unknown_sentiments": {} if unknown_count == 0 else {"unknown": unknown_count},
        "items": items,
    }


def escape_md(value: str) -> str:
    return str(value).replace("|", "\\|").replace("\n", " ")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", type=Path, default=DEFAULT_INPUT)
    parser.add_argument("--streaming-assets", type=Path, default=STREAMING)
    parser.add_argument("--target-count", type=int, default=DEFAULT_TARGET_COUNT)
    parser.add_argument("--audio-ext", choices=["wav", "mp3", "ogg"], default="wav")
    args = parser.parse_args()

    items = load_items(args.input)
    selected = choose_candidates(items, args.target_count)
    assign_spatial_and_offsets(selected)
    schedule = build_schedule(selected, args.audio_ext)

    streaming = args.streaming_assets
    write_json(streaming / "emotion_tts_candidates.json", selected)
    write_json(streaming / "audio_schedule.json", schedule)
    write_mount_tables(streaming, schedule["events"])
    write_json(streaming / "emotion_tts_mount_table_source_format.json", build_source_format_mount_json(schedule["events"]))
    write_json(
        streaming / "emotion_tts_manifest.json",
        {
            "schema_version": "emotion_tts_manifest_v1",
            "source_file": "Danmu/json/W2/supportive_interactions_emotion_from_excel.json",
            "target_count": args.target_count,
            "scheduled": len(schedule["events"]),
            "audio_extension": args.audio_ext,
            "selection_policy": "short emotion danmaku, original-time playback, overlap allowed, punctuation-only comments excluded",
            "outputs": [
                "Assets/StreamingAssets/emotion_tts_candidates.json",
                "Assets/StreamingAssets/audio_schedule.json",
                "Assets/StreamingAssets/emotion_tts_mount_table.csv",
                "Assets/StreamingAssets/emotion_tts_mount_table.md",
                "Assets/StreamingAssets/emotion_tts_mount_table_source_format.json",
            ],
        },
    )
    print(f"Wrote {len(schedule['events'])} emotion TTS events to {streaming / 'audio_schedule.json'}")


if __name__ == "__main__":
    main()
