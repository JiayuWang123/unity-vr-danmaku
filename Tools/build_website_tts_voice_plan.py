#!/usr/bin/env python3
"""Build a front-end TTS website voice plan for the emotion danmaku clips.

This script does not call text-to-speech.cn or any hidden API. It only turns the
existing Unity audio schedule into a reproducible task list that can be used in
the website UI, then imported back into Unity with the same clip names.
"""

from __future__ import annotations

import argparse
import csv
import html
import json
import re
from collections import Counter, defaultdict
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
STREAMING = ROOT / "Assets" / "StreamingAssets"
DEFAULT_SCHEDULE = STREAMING / "audio_schedule.json"
DEFAULT_PLAN_JSON = STREAMING / "emotion_tts_website_voice_plan.json"
DEFAULT_PLAN_CSV = STREAMING / "emotion_tts_website_voice_plan.csv"
DEFAULT_PLAN_MD = STREAMING / "emotion_tts_website_voice_plan.md"

WEBSITE_URL = "https://www.text-to-speech.cn/"
WEBSITE_LANGUAGE = "中文（普通话，简体）"
WEBSITE_LANGUAGE_VALUE = "zh-CN"
WEBSITE_OUTPUT_FORMAT = "riff-16khz-16bit-mono-pcm"
WEBSITE_OUTPUT_FORMAT_NOTE = "WAV 16 kHz 16-bit mono PCM; keep .wav so Unity schedule paths do not change"

VOICE_PALETTES: dict[str, list[dict[str, object]]] = {
    "tense_fan": [
        {
            "voice": "zh-CN-YunxiNeural",
            "voice_label": "云希(年轻男)",
            "style": "fearful",
            "style_label": "紧张/恐惧",
            "pitch": "-4%",
        },
        {
            "voice": "zh-CN-XiaomoNeural",
            "voice_label": "晓墨(成年女)",
            "style": "serious",
            "style_label": "严肃",
            "pitch": "-6%",
        },
        {
            "voice": "zh-CN-YunjianNeural",
            "voice_label": "云健(成年男)",
            "style": "serious",
            "style_label": "严肃",
            "pitch": "-8%",
        },
    ],
    "amused_fan": [
        {
            "voice": "zh-CN-XiaoshuangNeural",
            "voice_label": "晓双(儿童女)",
            "style": "cheerful",
            "style_label": "开心",
            "pitch": "+14%",
        },
        {
            "voice": "zh-CN-XiaoyouNeural",
            "voice_label": "晓悠(儿童女)",
            "style": "excited",
            "style_label": "兴奋",
            "pitch": "+12%",
        },
        {
            "voice": "zh-CN-XiaoxiaoNeural",
            "voice_label": "晓晓(年轻女)",
            "style": "cheerful",
            "style_label": "开心",
            "pitch": "+10%",
        },
        {
            "voice": "zh-CN-XiaoyiNeural",
            "voice_label": "晓伊(年轻女)",
            "style": "chat",
            "style_label": "轻松聊天",
            "pitch": "+8%",
        },
    ],
    "chant_fan": [
        {
            "voice": "zh-CN-YunyangNeural",
            "voice_label": "云扬(成年男)",
            "style": "excited",
            "style_label": "兴奋",
            "pitch": "+4%",
        },
        {
            "voice": "zh-CN-YunhaoNeural",
            "voice_label": "云皓(成年男)",
            "style": "excited",
            "style_label": "兴奋",
            "pitch": "+2%",
        },
        {
            "voice": "zh-CN-YunfengNeural",
            "voice_label": "云枫(年轻男)",
            "style": "cheerful",
            "style_label": "开心",
            "pitch": "+6%",
        },
        {
            "voice": "zh-CN-YunjianNeural",
            "voice_label": "云健(成年男)",
            "style": "excited",
            "style_label": "兴奋",
            "pitch": "+1%",
        },
    ],
    "excited_fan": [
        {
            "voice": "zh-CN-XiaoxiaoNeural",
            "voice_label": "晓晓(年轻女)",
            "style": "cheerful",
            "style_label": "开心",
            "pitch": "+8%",
        },
        {
            "voice": "zh-CN-XiaohanNeural",
            "voice_label": "晓涵(成年女)",
            "style": "excited",
            "style_label": "兴奋",
            "pitch": "+6%",
        },
        {
            "voice": "zh-CN-XiaochenNeural",
            "voice_label": "晓辰(年轻女)",
            "style": "friendly",
            "style_label": "友好",
            "pitch": "+6%",
        },
        {
            "voice": "zh-CN-XiaomengNeural",
            "voice_label": "晓梦(年轻女)",
            "style": "cheerful",
            "style_label": "开心",
            "pitch": "+9%",
        },
        {
            "voice": "zh-CN-YunyangNeural",
            "voice_label": "云扬(成年男)",
            "style": "excited",
            "style_label": "兴奋",
            "pitch": "+3%",
        },
    ],
    "neutral_fan": [
        {
            "voice": "zh-CN-XiaorouNeural",
            "voice_label": "晓柔(成年女)",
            "style": "chat",
            "style_label": "轻松聊天",
            "pitch": "+2%",
        },
        {
            "voice": "zh-CN-YunyeNeural",
            "voice_label": "云野(老年男)",
            "style": "calm",
            "style_label": "平静",
            "pitch": "-4%",
        },
    ],
}

ROLE_SETTINGS = {
    "tense_fan": {
        "rate": "+6%",
        "volume": "loud",
        "styledegree": "1.25",
        "note": "nervous but still quick enough to sit under match audio",
    },
    "amused_fan": {
        "rate": "+24%",
        "volume": "x-loud",
        "styledegree": "1.45",
        "note": "short laugh reactions should be fast, bright, and varied",
    },
    "chant_fan": {
        "rate": "+12%",
        "volume": "x-loud",
        "styledegree": "1.65",
        "note": "chant/cheer lines should cut through the video more clearly",
    },
    "excited_fan": {
        "rate": "+14%",
        "volume": "x-loud",
        "styledegree": "1.35",
        "note": "positive reactions should sound present but not like formal narration",
    },
    "neutral_fan": {
        "rate": "+8%",
        "volume": "loud",
        "styledegree": "1.10",
        "note": "fallback voice for uncategorized short reactions",
    },
}

EVENT_OVERRIDES = {
    "emotion_tts_001": {
        "rate": "+22%",
        "styledegree": "1.55",
        "note": "opening tense line intentionally faster, per current mix feedback",
    },
    "emotion_tts_002": {
        "rate": "+20%",
        "styledegree": "1.50",
        "note": "opening tense line intentionally faster, per current mix feedback",
    },
}


def load_schedule(path: Path) -> dict:
    payload = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(payload, dict) or not isinstance(payload.get("events"), list):
        raise ValueError(f"Unsupported schedule shape: {path}")
    return payload


def voice_for_event(event: dict, role_counts: Counter[str], recent_window: dict[tuple[int, str], set[str]]) -> dict:
    role = str(event.get("speaker_role") or "neutral_fan")
    palette = VOICE_PALETTES.get(role, VOICE_PALETTES["neutral_fan"])
    window_key = (int(float(event.get("start_sec", 0.0)) // 3), role)
    used = recent_window[window_key]
    ordinal = role_counts[role]
    selected = None

    for offset in range(len(palette)):
        candidate = palette[(ordinal + offset) % len(palette)]
        voice = str(candidate["voice"])
        if voice not in used:
            selected = candidate
            break

    if selected is None:
        selected = palette[ordinal % len(palette)]

    used.add(str(selected["voice"]))
    role_counts[role] += 1
    return dict(selected)


def normalize_download_filename(event: dict) -> str:
    audio_clip = str(event.get("audio_clip", ""))
    name = Path(audio_clip.replace("\\", "/")).name
    if name:
        return name
    return f"{event.get('id', 'emotion_tts_unknown')}.wav"


def mmss(seconds: float) -> str:
    whole = int(seconds)
    millis = int(round((seconds - whole) * 1000))
    if millis >= 1000:
        whole += 1
        millis -= 1000
    return f"{whole // 60:02d}:{whole % 60:02d}.{millis:03d}"


def clean_website_text(text: str) -> str:
    normalized = str(text or "").strip()
    normalized = re.sub(r"\s+", " ", normalized)
    return normalized


def build_ssml_reference(task: dict) -> str:
    text = html.escape(clean_website_text(task["text"]), quote=False)
    return (
        '<speak version="1.0" '
        'xmlns="http://www.w3.org/2001/10/synthesis" '
        'xmlns:mstts="https://www.w3.org/2001/mstts" '
        f'xml:lang="{WEBSITE_LANGUAGE_VALUE}">\n'
        f'  <voice name="{task["voice"]}">\n'
        f'    <mstts:express-as style="{task["style"]}" styledegree="{task["styledegree"]}">\n'
        f'      <prosody rate="{task["rate"]}" pitch="{task["pitch"]}" volume="{task["volume"]}">{text}</prosody>\n'
        "    </mstts:express-as>\n"
        "  </voice>\n"
        "</speak>"
    )


def build_tasks(schedule: dict) -> list[dict]:
    role_counts: Counter[str] = Counter()
    recent_window: dict[tuple[int, str], set[str]] = defaultdict(set)
    tasks = []

    for index, event in enumerate(schedule["events"], start=1):
        role = str(event.get("speaker_role") or "neutral_fan")
        text = clean_website_text(event.get("spoken_text") or event.get("text") or event.get("text_raw") or "")
        voice = voice_for_event(event, role_counts, recent_window)
        settings = dict(ROLE_SETTINGS.get(role, ROLE_SETTINGS["neutral_fan"]))
        override = EVENT_OVERRIDES.get(str(event.get("id")), {})
        settings.update({key: value for key, value in override.items() if key != "note"})
        task_note = str(override.get("note") or settings.get("note") or "")

        task = {
            "index": index,
            "id": event.get("id"),
            "time_mmss": event.get("time_mmss") or mmss(float(event.get("start_sec", 0.0))),
            "start_sec": event.get("start_sec"),
            "original_time_sec": event.get("original_time_sec", event.get("start_sec")),
            "text": text,
            "website_input_text": text,
            "text_raw": event.get("text_raw", text),
            "speaker_role": role,
            "spatial_anchor": event.get("spatial_anchor"),
            "audio_clip": event.get("audio_clip"),
            "download_filename": normalize_download_filename(event),
            "website_url": WEBSITE_URL,
            "language": WEBSITE_LANGUAGE,
            "language_value": WEBSITE_LANGUAGE_VALUE,
            "voice": voice["voice"],
            "voice_label": voice["voice_label"],
            "style": voice["style"],
            "style_label": voice["style_label"],
            "styledegree": settings["styledegree"],
            "rate": settings["rate"],
            "pitch": voice["pitch"],
            "volume": settings["volume"],
            "output_format": WEBSITE_OUTPUT_FORMAT,
            "output_format_note": WEBSITE_OUTPUT_FORMAT_NOTE,
            "generation_mode": "plain_text",
            "use_ssml_if_ui_rate_pitch_unavailable": False,
            "note": task_note,
        }
        task["ssml_reference_only"] = build_ssml_reference(task)
        tasks.append(task)

    return tasks


def write_json(path: Path, payload: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def write_csv(path: Path, tasks: list[dict]) -> None:
    fields = [
        "index",
        "id",
        "time_mmss",
        "text",
        "speaker_role",
        "spatial_anchor",
        "voice_label",
        "voice",
        "style_label",
        "style",
        "styledegree",
        "rate",
        "pitch",
        "volume",
        "output_format",
        "download_filename",
        "audio_clip",
        "note",
    ]
    with path.open("w", encoding="utf-8-sig", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=fields)
        writer.writeheader()
        for task in tasks:
            writer.writerow({field: task.get(field, "") for field in fields})


def escape_md(value: object) -> str:
    return str(value).replace("|", "\\|").replace("\n", " ")


def write_md(path: Path, tasks: list[dict]) -> None:
    lines = [
        "# Website TTS Voice Plan",
        "",
        "Use the website UI at <https://www.text-to-speech.cn/>. Do not call hidden backend endpoints.",
        "",
        "Recommended format: `riff-16khz-16bit-mono-pcm` / WAV, then save each download with the `download filename` below.",
        "",
        "| # | time | text | role | anchor | voice | style | rate | pitch | file |",
        "|---|---:|---|---|---|---|---|---:|---:|---|",
    ]
    for task in tasks:
        lines.append(
            "| {index} | {time} | {text} | {role} | {anchor} | {voice_label} `{voice}` | {style_label} `{style}` | {rate} | {pitch} | `{file}` |".format(
                index=task["index"],
                time=escape_md(task["time_mmss"]),
                text=escape_md(task["text"]),
                role=escape_md(task["speaker_role"]),
                anchor=escape_md(task["spatial_anchor"]),
                voice_label=escape_md(task["voice_label"]),
                voice=escape_md(task["voice"]),
                style_label=escape_md(task["style_label"]),
                style=escape_md(task["style"]),
                rate=escape_md(task["rate"]),
                pitch=escape_md(task["pitch"]),
                file=escape_md(task["download_filename"]),
            )
        )
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--schedule", type=Path, default=DEFAULT_SCHEDULE)
    parser.add_argument("--plan-json", type=Path, default=DEFAULT_PLAN_JSON)
    parser.add_argument("--plan-csv", type=Path, default=DEFAULT_PLAN_CSV)
    parser.add_argument("--plan-md", type=Path, default=DEFAULT_PLAN_MD)
    args = parser.parse_args()

    schedule = load_schedule(args.schedule)
    tasks = build_tasks(schedule)
    voice_counts = Counter(task["voice"] for task in tasks)
    role_counts = Counter(task["speaker_role"] for task in tasks)

    payload = {
        "schema_version": "emotion_tts_website_voice_plan_v1",
        "source_schedule": "Assets/StreamingAssets/audio_schedule.json",
        "website_url": WEBSITE_URL,
        "website_use_policy": "Use the visible website UI with plain danmaku text only. Do not scrape captured backend API calls.",
        "output_format": WEBSITE_OUTPUT_FORMAT,
        "output_format_note": WEBSITE_OUTPUT_FORMAT_NOTE,
        "download_naming_rule": "Submit only website_input_text, download or rename each generated file to the task download_filename, then import it over the existing Unity clip path.",
        "voice_counts": dict(sorted(voice_counts.items())),
        "role_counts": dict(sorted(role_counts.items())),
        "tasks": tasks,
    }
    write_json(args.plan_json, payload)
    write_csv(args.plan_csv, tasks)
    write_md(args.plan_md, tasks)

    print(f"Wrote {len(tasks)} website TTS tasks")
    print(f"JSON: {args.plan_json}")
    print(f"CSV:  {args.plan_csv}")
    print(f"MD:   {args.plan_md}")


if __name__ == "__main__":
    main()
