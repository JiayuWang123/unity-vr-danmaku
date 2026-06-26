"""Schemas and validation helpers for the layer-4/5 LLM Agent outputs."""

from __future__ import annotations

from datetime import datetime, timezone
from typing import Any, Sequence


LAYER45_SCHEMA_VERSION = "layer45_qwen_agent_v1"

EMOTION_LABELS = {
    "excitement",
    "joy",
    "tension",
    "anger",
    "sarcasm",
    "confusion",
    "disappointment",
    "neutral",
    "mixed",
}

CONTENT_LABELS = {
    "gameplay_reaction",
    "tactical_analysis",
    "rule_or_referee_discussion",
    "player_or_team_evaluation",
    "celebration",
    "viewer_meta",
    "meme_or_slang",
    "noise",
    "unclear",
}

DISPLAY_MODES = {
    "hide",
    "text_overlay",
    "crowd_atmosphere",
    "spatial_burst",
    "tts_highlight",
}

ANCHOR_HINTS = {
    "audience_area",
    "field_center",
    "scoreboard",
    "player_focus",
    "referee_focus",
    "ambient_space",
}

COMMENT_SCORE_FIELDS = [
    "schema_version",
    "danmaku_id",
    "time_sec",
    "text_norm",
    "source_burst_id",
    "analysis_value_score",
    "atmosphere_value_score",
    "tts_value_score",
    "vr_display_value_score",
    "emotion_label",
    "content_label",
    "confidence",
    "reason",
    "model",
    "usage_total_tokens",
]


def clamp_score(value: Any, default: float = 0.0) -> float:
    try:
        number = float(value)
    except (TypeError, ValueError):
        number = default
    return round(max(0.0, min(1.0, number)), 3)


def enum_value(value: Any, allowed: set[str], default: str) -> str:
    text = str(value or "").strip()
    return text if text in allowed else default


def normalize_comment_score(raw: dict, candidate: dict | None = None, model: str = "", usage_total_tokens: int = 0) -> dict:
    candidate = candidate or {}
    return {
        "schema_version": LAYER45_SCHEMA_VERSION,
        "danmaku_id": str(raw.get("danmaku_id") or candidate.get("danmaku_id") or ""),
        "time_sec": clamp_time(raw.get("time_sec", candidate.get("time_sec", 0.0))),
        "text_norm": str(raw.get("text_norm") or candidate.get("text_norm") or ""),
        "source_burst_id": str(raw.get("source_burst_id") or candidate.get("source_burst_id") or ""),
        "analysis_value_score": clamp_score(raw.get("analysis_value_score")),
        "atmosphere_value_score": clamp_score(raw.get("atmosphere_value_score")),
        "tts_value_score": clamp_score(raw.get("tts_value_score")),
        "vr_display_value_score": clamp_score(raw.get("vr_display_value_score")),
        "emotion_label": enum_value(raw.get("emotion_label"), EMOTION_LABELS, "unclear" if "unclear" in EMOTION_LABELS else "mixed"),
        "content_label": enum_value(raw.get("content_label"), CONTENT_LABELS, "unclear"),
        "confidence": clamp_score(raw.get("confidence"), default=0.5),
        "reason": str(raw.get("reason") or "No model reason provided.")[:600],
        "model": str(raw.get("model") or model),
        "usage_total_tokens": int(raw.get("usage_total_tokens") or usage_total_tokens or 0),
    }


def normalize_burst_summary(raw: dict, burst: dict | None = None, model: str = "", usage_total_tokens: int = 0) -> dict:
    burst = burst or {}
    burst_id = str(raw.get("burst_id") or burst.get("burst_id") or "")
    return {
        "schema_version": LAYER45_SCHEMA_VERSION,
        "burst_id": burst_id,
        "burst_title": str(raw.get("burst_title") or burst.get("topic_label") or burst_id or "Burst event")[:120],
        "start_sec": clamp_time(raw.get("start_sec", burst.get("start_seconds", 0.0))),
        "end_sec": clamp_time(raw.get("end_sec", burst.get("end_seconds", 0.0))),
        "peak_sec": clamp_time(raw.get("peak_sec", burst.get("peak_seconds", 0.0))),
        "dominant_emotion": enum_value(raw.get("dominant_emotion"), EMOTION_LABELS, "mixed"),
        "content_topic": enum_value(raw.get("content_topic"), CONTENT_LABELS, "unclear"),
        "representative_comments": normalize_string_list(raw.get("representative_comments"), limit=8),
        "vr_scene_suggestion": str(raw.get("vr_scene_suggestion") or "Use local burst intensity and representative comments for VR mapping.")[:800],
        "confidence": clamp_score(raw.get("confidence"), default=0.5),
        "reason": str(raw.get("reason") or "Summary generated from burst evidence and selected danmaku.")[:800],
        "model": str(raw.get("model") or model),
        "usage_total_tokens": int(raw.get("usage_total_tokens") or usage_total_tokens or 0),
    }


def build_vr_mapping_event(summary: dict, burst: dict | None = None, scored_comments: Sequence[dict] | None = None) -> dict:
    burst = burst or {}
    scored_comments = list(scored_comments or [])
    representative_ids = [
        str(row.get("danmaku_id"))
        for row in sorted(scored_comments, key=lambda item: float(item.get("vr_display_value_score", 0.0)), reverse=True)[:8]
        if row.get("danmaku_id")
    ]
    tts_lines = [
        {
            "danmaku_id": row.get("danmaku_id", ""),
            "time_sec": row.get("time_sec", 0.0),
            "text": row.get("text_norm", ""),
            "tts_value_score": row.get("tts_value_score", 0.0),
        }
        for row in sorted(scored_comments, key=lambda item: float(item.get("tts_value_score", 0.0)), reverse=True)[:5]
        if float(row.get("tts_value_score", 0.0)) >= 0.65 and row.get("text_norm")
    ]
    intensity = max([float(row.get("vr_display_value_score", 0.0)) for row in scored_comments] + [summary.get("confidence", 0.0)])
    content_topic = summary.get("content_topic", "unclear")
    display_mode = "tts_highlight" if tts_lines else "crowd_atmosphere"
    if content_topic in {"gameplay_reaction", "tactical_analysis", "rule_or_referee_discussion", "celebration"}:
        display_mode = "spatial_burst"
    return {
        "event_id": str(summary.get("burst_id") or burst.get("burst_id") or ""),
        "start_sec": summary.get("start_sec", clamp_time(burst.get("start_seconds", 0.0))),
        "end_sec": summary.get("end_sec", clamp_time(burst.get("end_seconds", 0.0))),
        "peak_sec": summary.get("peak_sec", clamp_time(burst.get("peak_seconds", 0.0))),
        "event_type": content_topic,
        "dominant_emotion": summary.get("dominant_emotion", "mixed"),
        "intensity": clamp_score(intensity),
        "display_mode": enum_value(display_mode, DISPLAY_MODES, "crowd_atmosphere"),
        "anchor_hint": infer_anchor_hint(content_topic, summary.get("dominant_emotion", "")),
        "tts_lines": tts_lines,
        "representative_danmaku_ids": representative_ids,
        "confidence": summary.get("confidence", 0.0),
        "evidence": {
            "burst_kind": burst.get("burst_kind", ""),
            "peak_density_5s": burst.get("peak_density_5s", ""),
            "baseline_multiplier": burst.get("baseline_multiplier", ""),
            "topic_label_layer3": burst.get("topic_label", ""),
            "representative_comments_layer3": burst.get("representative_comments", ""),
        },
    }


def infer_anchor_hint(content_topic: str, emotion: str) -> str:
    if content_topic == "rule_or_referee_discussion":
        return "referee_focus"
    if content_topic in {"tactical_analysis", "gameplay_reaction", "celebration"}:
        return "field_center"
    if content_topic == "player_or_team_evaluation":
        return "player_focus"
    if emotion in {"excitement", "joy", "tension"}:
        return "audience_area"
    return "ambient_space"


def normalize_string_list(value: Any, limit: int) -> list[str]:
    if isinstance(value, str):
        items = [part.strip() for part in value.replace("|", "\n").splitlines()]
    elif isinstance(value, list):
        items = [str(item).strip() for item in value]
    else:
        items = []
    output = []
    seen = set()
    for item in items:
        if item and item not in seen:
            seen.add(item)
            output.append(item[:160])
        if len(output) >= limit:
            break
    return output


def clamp_time(value: Any) -> float:
    try:
        return round(max(0.0, float(value)), 3)
    except (TypeError, ValueError):
        return 0.0


def make_manifest(
    input_dir: str,
    output_dir: str,
    model: str,
    mode: str,
    candidate_count: int,
    scored_count: int,
    burst_count: int,
    usage: dict,
    errors: Sequence[dict],
    config: dict,
) -> dict:
    return {
        "format_version": LAYER45_SCHEMA_VERSION,
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "github_branch": "Danmaku-analysis-agent",
        "input_dir": input_dir,
        "output_dir": output_dir,
        "mode": mode,
        "model": model,
        "row_counts": {
            "llm_candidates": candidate_count,
            "llm_scored_danmaku": scored_count,
            "burst_agent_summaries": burst_count,
        },
        "usage": usage,
        "error_count": len(errors),
        "config": config,
    }
