"""Layer-2 rule pre-filtering for normalized sports danmaku records."""

from __future__ import annotations

import json
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path


FORMAT_VERSION = "filtered_danmaku_v1"

DEFAULT_RULES = {
    "analysis": {
        "梅西", "姆巴佩", "C罗", "哈兰德", "亚马尔",
        "进球", "破门", "助攻", "越位", "点球",
        "任意球", "角球", "传中", "反击", "控球",
        "世界波", "绝杀", "帽子戏法", "扑救", "VAR",
        "裁判", "门将", "防守", "战术", "换人",
    },
    "atmosphere": {
        "哈哈", "哈哈哈", "666", "牛逼", "封神",
        "卧槽", "太帅了", "绝了", "离谱", "燃",
        "泪目", "笑死", "逆天", "无敌", "精彩",
        "啊啊", "爽", "漂亮",
    },
    "meta": {
        "打卡", "签到", "空降", "前排", "第一",
        "考古", "二刷", "三刷", "补课", "来了",
        "有人吗", "集合", "报到", "字幕", "缓存",
    },
    "noise": {
        "111111", "222222", "333333",
        "......", "？？？？", "????", "!!!!!!",
        "aaaa", "bbbb",
    },
}


def match_terms(text: str, terms: set[str]) -> list[str]:
    lowered = text.lower()
    return sorted(term for term in terms if term.lower() in lowered)


def classify_with_evidence(text: str) -> dict:
    text = "" if text is None else str(text).strip()
    if not text:
        return {
            "filter_label": "REMOVE_NOISE",
            "filter_reason": "empty_text",
            "filter_matched_terms": {"noise": []},
            "filter_confidence": 1.0,
            "removed_from_main_display": True,
        }

    checks = [
        ("noise", "REMOVE_NOISE", 0.95, "matched_noise_term", True),
        ("meta", "DOWNRANK_META", 0.85, "matched_meta_viewing_term", False),
        ("atmosphere", "KEEP_ATMOSPHERE", 0.8, "matched_atmosphere_term", False),
        ("analysis", "KEEP_ANALYSIS", 0.8, "matched_analysis_term", False),
    ]
    matched: dict[str, list[str]] = {}
    for category, label, confidence, reason, removed in checks:
        hits = match_terms(text, DEFAULT_RULES[category])
        matched[category] = hits
        if hits:
            return {
                "filter_label": label,
                "filter_reason": reason,
                "filter_matched_terms": {category: hits},
                "filter_confidence": confidence,
                "removed_from_main_display": removed,
            }

    return {
        "filter_label": "KEEP_ANALYSIS",
        "filter_reason": "default_low_confidence_analysis",
        "filter_matched_terms": {},
        "filter_confidence": 0.35,
        "removed_from_main_display": False,
    }


def classify(text: str) -> str:
    """Backward-compatible single-text classifier."""
    return classify_with_evidence(text)["filter_label"]


def filter_records(records: list[dict]) -> tuple[list[dict], dict]:
    output = []
    for record in records:
        text = record.get("text_norm") or record.get("text_raw") or record.get("text") or ""
        result = classify_with_evidence(text)
        merged = dict(record)
        merged.update(result)
        output.append(merged)

    label_counts = Counter(row["filter_label"] for row in output)
    stats = {
        "format_version": FORMAT_VERSION,
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "input_count": len(records),
        "output_count": len(output),
        "label_counts": dict(label_counts),
        "removed_from_main_display_count": sum(1 for row in output if row["removed_from_main_display"]),
    }
    return output, stats


def load_records(path: Path) -> list[dict]:
    data = json.loads(path.read_text(encoding="utf-8"))
    if isinstance(data, dict):
        data = data.get("entries", [])
    if not isinstance(data, list):
        raise ValueError(f"Input JSON must be a list or collection with entries: {path}")
    return [dict(item) for item in data]


def make_filtered_collection(records: list[dict], stats: dict) -> dict:
    return {
        "format_version": FORMAT_VERSION,
        "generated_at_utc": stats["generated_at_utc"],
        "entries": records,
        "stats": stats,
    }
