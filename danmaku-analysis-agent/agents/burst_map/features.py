"""Layer-3 per-comment feature extraction for sports danmaku analysis."""

from __future__ import annotations

import bisect
import math
import re
import statistics
import unicodedata
from collections import Counter, defaultdict
from types import SimpleNamespace
from typing import Sequence

from .lexicons import load_lexicons, match_lexicons
from .schema import FEATURE_SCHEMA_VERSION


SHORT_REACTION_TERMS = {
    "牛逼",
    "进了",
    "好球",
    "漂亮",
    "绝了",
    "稳了",
    "燃",
    "帅",
    "强",
    "nb",
    "gg",
    "goal",
}


def build_feature_rows(
    entries: Sequence[object],
    density: Sequence[dict],
    bursts: Sequence[dict],
    duplicate_window_seconds: float = 5.0,
    near_peak_seconds: float = 10.0,
) -> list[dict]:
    """Return one stable feature row for every normalized danmaku entry."""
    lexicons = load_lexicons()
    density_lookup, density_z_lookup = build_density_lookups(density)
    global_counts = Counter(getattr(entry, "normalized_text", "") for entry in entries)
    times_by_text: dict[str, list[float]] = defaultdict(list)
    for entry in entries:
        times_by_text[getattr(entry, "normalized_text", "")].append(float(getattr(entry, "time_seconds", 0.0)))

    rows = []
    for entry in entries:
        text_raw = getattr(entry, "raw_text", "")
        text_norm = getattr(entry, "clean_text", "") or getattr(entry, "normalized_text", "")
        normalized_key = getattr(entry, "normalized_text", "")
        time_sec = float(getattr(entry, "time_seconds", 0.0))
        matched_terms = match_lexicons(text_norm, lexicons)
        structure = text_structure_features(text_norm)
        duplicate_count = count_nearby_duplicates(
            times_by_text.get(normalized_key, []),
            time_sec,
            duplicate_window_seconds,
        )
        density_key = density_window_start(time_sec, density)
        flags = feature_quality_flags(entry, text_norm, structure, matched_terms, duplicate_count)

        rows.append(
            {
                "schema_version": FEATURE_SCHEMA_VERSION,
                "danmaku_id": str(getattr(entry, "danmaku_id", "")),
                "time_sec": round(time_sec, 3),
                "text_raw": text_raw,
                "text_norm": text_norm,
                "filter_label": getattr(entry, "filter_label", ""),
                "filter_reason": getattr(entry, "filter_reason", ""),
                "filter_confidence": getattr(entry, "filter_confidence", ""),
                "length": structure["length"],
                "char_repeat_ratio": structure["char_repeat_ratio"],
                "punctuation_ratio": structure["punctuation_ratio"],
                "symbol_ratio": structure["symbol_ratio"],
                "digit_ratio": structure["digit_ratio"],
                "emoji_or_emoticon_count": structure["emoji_or_emoticon_count"],
                "has_sports_terms": bool(matched_terms["sports"]),
                "has_emotion_terms": bool(matched_terms["emotion"]),
                "has_meta_noise_terms": bool(matched_terms["meta_noise"]),
                "has_toxic_terms": bool(matched_terms["toxic"]),
                "has_meme_terms": bool(matched_terms["meme"]),
                "matched_terms": matched_terms,
                "same_text_global_count": int(global_counts.get(normalized_key, 0)),
                "duplicate_count_in_5s": duplicate_count,
                "time_window_density": int(density_lookup.get(density_key, 0)),
                "density_z_score": float(density_z_lookup.get(density_key, 0.0)),
                "near_burst_peak": is_near_burst_peak(time_sec, bursts, near_peak_seconds),
                "is_short_reaction": is_short_reaction(text_norm, matched_terms),
                "is_symbol_only": bool(getattr(entry, "is_symbol_only", False)),
                "is_repetition_pattern": is_repetition_pattern(text_norm),
                "feature_quality_flags": flags,
            }
        )
    return rows


def records_to_feature_entries(records: Sequence[dict]) -> list[object]:
    """Adapt layer-1/layer-2 JSON records to the attribute shape used here."""
    entries = []
    normalized_counts = Counter(normalize_for_duplicate(record.get("text_norm") or record.get("text_raw") or "") for record in records)
    for index, record in enumerate(records, 1):
        text_norm = record.get("text_norm") or record.get("text_raw") or ""
        normalized_text = normalize_for_duplicate(text_norm)
        entries.append(
            SimpleNamespace(
                time_seconds=float(record.get("time_sec", 0.0) or 0.0),
                mode=int(record.get("mode", 1) or 1),
                font_size=int(record.get("font_size", 25) or 25),
                color=record.get("color_hex", "#FFFFFF"),
                timestamp=str(record.get("timestamp", "")),
                pool=str(record.get("pool", "")),
                user_hash=str(record.get("user_hash", "")),
                danmaku_id=str(record.get("danmaku_id", f"record_{index:06d}")),
                raw_text=str(record.get("text_raw", "")),
                clean_text=str(text_norm),
                normalized_text=normalized_text,
                is_duplicate_like=normalized_counts[normalized_text] > 1,
                is_spam_like=bool(record.get("removed_from_main_display", False)),
                is_symbol_only=is_symbol_only_text(text_norm),
                text_length=len(text_norm),
                priority_score=1.0,
                evidence_weight=1.0,
                filter_label=record.get("filter_label", ""),
                filter_reason=record.get("filter_reason", ""),
                filter_confidence=record.get("filter_confidence", ""),
            )
        )
    entries.sort(key=lambda item: (item.time_seconds, item.danmaku_id))
    return entries


def normalize_for_duplicate(text: str) -> str:
    return re.sub(r"\s+", "", str(text).lower())


def is_symbol_only_text(text: str) -> bool:
    return bool(re.fullmatch(r"[\W_]+", text, flags=re.UNICODE)) if text else True


def text_structure_features(text: str) -> dict:
    length = len(text)
    if length == 0:
        return {
            "length": 0,
            "char_repeat_ratio": 0.0,
            "punctuation_ratio": 0.0,
            "symbol_ratio": 0.0,
            "digit_ratio": 0.0,
            "emoji_or_emoticon_count": 0,
        }

    char_counts = Counter(text)
    punctuation = sum(1 for char in text if unicodedata.category(char).startswith("P"))
    symbols = sum(1 for char in text if unicodedata.category(char).startswith("S"))
    digits = sum(1 for char in text if char.isdigit())
    emoji_count = sum(1 for char in text if is_emoji_like(char)) + len(re.findall(r"(?::|;|=)[\-o\*']?[\)\]\(\[dDpP/:\}\{@\|\\]", text))

    return {
        "length": length,
        "char_repeat_ratio": round(max(char_counts.values()) / length, 3),
        "punctuation_ratio": round(punctuation / length, 3),
        "symbol_ratio": round(symbols / length, 3),
        "digit_ratio": round(digits / length, 3),
        "emoji_or_emoticon_count": emoji_count,
    }


def is_emoji_like(char: str) -> bool:
    codepoint = ord(char)
    return (
        0x1F300 <= codepoint <= 0x1FAFF
        or 0x2600 <= codepoint <= 0x27BF
        or unicodedata.category(char) == "So" and codepoint > 0x2FFF
    )


def build_density_lookups(density: Sequence[dict]) -> tuple[dict[float, int], dict[float, float]]:
    if not density:
        return {}, {}
    counts = [int(row.get("raw_count", 0)) for row in density]
    median = statistics.median(counts)
    deviations = [abs(count - median) for count in counts]
    robust_sigma = 1.4826 * (statistics.median(deviations) or 1.0)
    density_lookup = {}
    density_z_lookup = {}
    for row, count in zip(density, counts):
        start = float(row.get("window_start_seconds", 0.0))
        density_lookup[start] = count
        density_z_lookup[start] = round((count - median) / robust_sigma, 3)
    return density_lookup, density_z_lookup


def density_window_start(time_sec: float, density: Sequence[dict]) -> float:
    if not density:
        return 0.0
    first = density[0]
    window = float(first.get("window_end_seconds", 5.0)) - float(first.get("window_start_seconds", 0.0))
    if window <= 0:
        window = 5.0
    return math.floor(time_sec / window) * window


def count_nearby_duplicates(times: Sequence[float], time_sec: float, window_seconds: float) -> int:
    half_window = window_seconds / 2.0
    left = bisect.bisect_left(times, time_sec - half_window)
    right = bisect.bisect_right(times, time_sec + half_window)
    return max(0, right - left)


def is_near_burst_peak(time_sec: float, bursts: Sequence[dict], near_peak_seconds: float) -> bool:
    for burst in bursts:
        peak = float(burst.get("peak_seconds", -999999.0))
        start = float(burst.get("start_seconds", peak))
        end = float(burst.get("end_seconds", peak))
        if abs(time_sec - peak) <= near_peak_seconds or start <= time_sec <= end:
            return True
    return False


def is_short_reaction(text: str, matched_terms: dict[str, list[str]]) -> bool:
    lowered = text.lower()
    if lowered in SHORT_REACTION_TERMS:
        return True
    if len(text) <= 8 and (matched_terms["emotion"] or matched_terms["sports"]):
        return True
    return bool(re.fullmatch(r"(哈|啊|哇|牛|6|nb|NB|！|!|？|\?){2,}", text))


def is_repetition_pattern(text: str) -> bool:
    if not text:
        return False
    if re.fullmatch(r"(.{1,4})\1{3,}", text):
        return True
    return any(count >= max(4, len(text) * 0.7) for count in Counter(text).values())


def feature_quality_flags(
    entry: object,
    text: str,
    structure: dict,
    matched_terms: dict[str, list[str]],
    duplicate_count: int,
) -> list[str]:
    flags = []
    if not text:
        flags.append("empty_text")
    if structure["length"] <= 2:
        flags.append("very_short")
    if bool(getattr(entry, "is_symbol_only", False)):
        flags.append("symbol_only")
    if bool(getattr(entry, "is_spam_like", False)):
        flags.append("spam_like")
    if duplicate_count >= 4:
        flags.append("local_duplicate_cluster")
    if structure["char_repeat_ratio"] >= 0.7:
        flags.append("high_character_repetition")
    if structure["punctuation_ratio"] >= 0.6:
        flags.append("punctuation_heavy")
    if matched_terms["meta_noise"] and not matched_terms["sports"]:
        flags.append("meta_without_sports_context")
    if matched_terms["toxic"]:
        flags.append("toxicity_risk")
    return flags
