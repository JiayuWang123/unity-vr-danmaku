"""Candidate routing for the layer-4/5 LLM Agent."""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any, Sequence


ROUTING_SCHEMA_VERSION = "layer45_routing_v1"


def load_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8"))


def load_feature_rows(path: Path) -> list[dict]:
    data = load_json(path)
    if isinstance(data, dict):
        data = data.get("entries", [])
    if not isinstance(data, list):
        raise ValueError(f"Feature JSON must be a list or a collection with entries: {path}")
    return [dict(row) for row in data]


def load_bursts(path: Path) -> list[dict]:
    data = load_json(path)
    if isinstance(data, dict):
        data = data.get("bursts", [])
    if not isinstance(data, list):
        raise ValueError(f"Burst JSON must be a list or a collection with bursts: {path}")
    return [dict(row) for row in data]


def select_llm_candidates(
    feature_rows: Sequence[dict],
    bursts: Sequence[dict],
    max_comments_per_burst: int = 60,
    max_global_comments: int = 250,
    max_total_candidates: int = 800,
    include_noise_near_burst: bool = False,
) -> list[dict]:
    """Select a bounded set of high-value or ambiguous comments for LLM analysis."""
    rows = [dict(row) for row in feature_rows]
    by_id: dict[str, dict] = {}

    for burst in bursts:
        burst_id = str(burst.get("burst_id", ""))
        start = float(burst.get("start_seconds", 0.0) or 0.0) - 10.0
        end = float(burst.get("end_seconds", 0.0) or 0.0) + 15.0
        in_window = [row for row in rows if start <= float(row.get("time_sec", 0.0) or 0.0) <= end]
        ranked = sorted(
            (candidate_with_score(row, burst_id, "burst_context", include_noise_near_burst) for row in in_window),
            key=lambda item: item["routing_priority"],
            reverse=True,
        )
        for candidate in ranked[:max_comments_per_burst]:
            if should_include(candidate, include_noise_near_burst):
                by_id.setdefault(candidate["danmaku_id"], candidate)

    ambiguous = [
        candidate_with_score(row, "", "global_ambiguous", include_noise_near_burst)
        for row in rows
        if is_ambiguous_or_high_value(row, include_noise_near_burst)
    ]
    ambiguous.sort(key=lambda item: item["routing_priority"], reverse=True)
    for candidate in ambiguous[:max_global_comments]:
        by_id.setdefault(candidate["danmaku_id"], candidate)

    selected = sorted(by_id.values(), key=lambda item: (item["source_burst_id"] == "", -item["routing_priority"], item["time_sec"]))
    selected = selected[:max_total_candidates]
    for index, candidate in enumerate(selected, 1):
        candidate["candidate_rank"] = index
        candidate["estimated_prompt_group"] = prompt_group(candidate)
    return selected


def candidate_with_score(row: dict, burst_id: str, reason: str, include_noise_near_burst: bool) -> dict:
    routing_priority, reasons = routing_score(row, burst_id, include_noise_near_burst)
    if reason == "burst_context":
        reasons.insert(0, "inside_burst_context")
    else:
        reasons.insert(0, "global_ambiguous_or_high_value")
    candidate = {
        "schema_version": ROUTING_SCHEMA_VERSION,
        "danmaku_id": str(row.get("danmaku_id", "")),
        "time_sec": float(row.get("time_sec", 0.0) or 0.0),
        "text_raw": str(row.get("text_raw", "")),
        "text_norm": str(row.get("text_norm", "")),
        "filter_label": str(row.get("filter_label", "")),
        "filter_reason": str(row.get("filter_reason", "")),
        "filter_confidence": to_float(row.get("filter_confidence", 0.0)),
        "source_burst_id": burst_id,
        "routing_priority": round(routing_priority, 3),
        "candidate_reason": "; ".join(dict.fromkeys(reasons)),
        "feature_evidence": compact_feature_evidence(row),
    }
    return candidate


def routing_score(row: dict, burst_id: str, include_noise_near_burst: bool) -> tuple[float, list[str]]:
    score = 0.0
    reasons = []
    label = str(row.get("filter_label", ""))
    density_z = to_float(row.get("density_z_score", 0.0))
    duplicate_count = int(to_float(row.get("duplicate_count_in_5s", 0.0)))
    same_text_count = int(to_float(row.get("same_text_global_count", 0.0)))
    length = int(to_float(row.get("length", 0.0)))

    label_weights = {
        "KEEP_ANALYSIS": 1.0,
        "KEEP_ATMOSPHERE": 0.75,
        "DOWNRANK_META": 0.15,
        "REMOVE_NOISE": -0.9,
    }
    score += label_weights.get(label, 0.2)
    reasons.append(f"filter_label={label or 'unknown'}")

    if burst_id:
        score += 0.8
    if row.get("near_burst_peak"):
        score += 0.7
        reasons.append("near_burst_peak")
    if density_z > 0:
        score += min(0.7, density_z * 0.12)
        reasons.append(f"density_z={round(density_z, 2)}")
    if row.get("has_sports_terms"):
        score += 0.9
        reasons.append("sports_terms")
    if row.get("has_emotion_terms"):
        score += 0.55
        reasons.append("emotion_terms")
    if row.get("has_meme_terms"):
        score += 0.35
        reasons.append("meme_terms")
    if row.get("has_toxic_terms"):
        score += 0.25
        reasons.append("toxicity_risk")
    if 4 <= length <= 80:
        score += 0.25
    if to_float(row.get("filter_confidence", 1.0)) <= 0.45:
        score += 0.35
        reasons.append("low_filter_confidence")
    if duplicate_count >= 4:
        score -= min(0.9, duplicate_count * 0.08)
        reasons.append("local_duplicate_penalty")
    if same_text_count >= 30:
        score -= 0.35
        reasons.append("global_repetition_penalty")
    if label == "REMOVE_NOISE" and include_noise_near_burst and (row.get("near_burst_peak") or burst_id) and (row.get("has_sports_terms") or row.get("has_emotion_terms")):
        score += 0.9
        reasons.append("noise_kept_for_burst_audit")
    return score, reasons


def should_include(candidate: dict, include_noise_near_burst: bool) -> bool:
    if not candidate.get("danmaku_id"):
        return False
    if candidate.get("filter_label") == "REMOVE_NOISE" and not include_noise_near_burst:
        return False
    return float(candidate.get("routing_priority", 0.0)) >= 0.55


def is_ambiguous_or_high_value(row: dict, include_noise_near_burst: bool) -> bool:
    if str(row.get("filter_label")) == "REMOVE_NOISE" and not include_noise_near_burst:
        return False
    signal_count = sum(
        1
        for key in [
            "has_sports_terms",
            "has_emotion_terms",
            "has_meta_noise_terms",
            "has_toxic_terms",
            "has_meme_terms",
            "near_burst_peak",
        ]
        if bool(row.get(key))
    )
    return (
        signal_count >= 2
        or to_float(row.get("filter_confidence", 1.0)) <= 0.45
        or to_float(row.get("density_z_score", 0.0)) >= 2.0
        or str(row.get("filter_label")) in {"KEEP_ANALYSIS", "KEEP_ATMOSPHERE"}
    )


def compact_feature_evidence(row: dict) -> dict:
    return {
        "length": row.get("length", 0),
        "has_sports_terms": bool(row.get("has_sports_terms")),
        "has_emotion_terms": bool(row.get("has_emotion_terms")),
        "has_meta_noise_terms": bool(row.get("has_meta_noise_terms")),
        "has_toxic_terms": bool(row.get("has_toxic_terms")),
        "has_meme_terms": bool(row.get("has_meme_terms")),
        "matched_terms": row.get("matched_terms", {}),
        "duplicate_count_in_5s": row.get("duplicate_count_in_5s", 0),
        "same_text_global_count": row.get("same_text_global_count", 0),
        "time_window_density": row.get("time_window_density", 0),
        "density_z_score": row.get("density_z_score", 0.0),
        "near_burst_peak": bool(row.get("near_burst_peak")),
        "feature_quality_flags": row.get("feature_quality_flags", []),
    }


def prompt_group(candidate: dict) -> str:
    label = candidate.get("filter_label", "")
    evidence = candidate.get("feature_evidence", {})
    if candidate.get("source_burst_id"):
        return "burst_context"
    if evidence.get("has_toxic_terms"):
        return "risk_review"
    if label == "KEEP_ATMOSPHERE" or evidence.get("has_emotion_terms"):
        return "emotion_atmosphere"
    if label == "KEEP_ANALYSIS" or evidence.get("has_sports_terms"):
        return "sports_analysis"
    return "general_review"


def to_float(value: Any) -> float:
    try:
        return float(value)
    except (TypeError, ValueError):
        return 0.0
