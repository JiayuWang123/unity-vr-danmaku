"""Core XML normalization, field projection, filtering, and sampling logic."""

from __future__ import annotations

import html
import json
import random
import re
import unicodedata
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from pathlib import Path
from typing import Any


WHITESPACE_RE = re.compile(r"\s+")

MODE_NAMES = {
    1: "scroll",
    2: "scroll",
    3: "scroll",
    4: "bottom",
    5: "top",
    6: "reverse",
    7: "special",
    8: "code",
    9: "bas",
}

PUNCT_REPLACEMENTS = {
    "。": ".",
    "，": ",",
    "、": ",",
    "；": ";",
    "：": ":",
    "！": "!",
    "？": "?",
    "（": "(",
    "）": ")",
    "【": "[",
    "】": "]",
    "《": "<",
    "》": ">",
    "“": '"',
    "”": '"',
    "‘": "'",
    "’": "'",
    "～": "~",
    "　": " ",
}

ROOT_META_TAGS = {
    "chatid",
}

FULL_NORMALIZED_FIELDS = (
    "id",
    "video_id",
    "source_file",
    "index",
    "time_sec",
    "time_mmss",
    "text_raw",
    "text_norm",
    "mode",
    "mode_name",
    "font_size",
    "color_decimal",
    "color_hex",
    "created_at_unix",
    "pool",
    "user_hash",
    "danmaku_id",
    "weight",
)

RAW_REVIEW_FIELDS = (
    "time_sec",
    "time_mmss",
    "text_raw",
)

MINIMAL_FIELDS = RAW_REVIEW_FIELDS

SUBTITLE_NOISE_RE = re.compile(
    r"^(?:"
    r"自动|自動|auto|"
    r"生成|生产|轉換|转换|转化|"
    r"字幕|字母|弹幕|翻译|"
    r"中文|英文|组|幕|刷|\.|。"
    r")+$",
    re.IGNORECASE,
)

EXACT_NOISE_TEXTS = {
    "空格",
    "空白",
    "字幕",
    "字母",
    "翻译",
    "字幕翻译",
    "生成字幕",
    "中文字幕",
    "自动生成字幕",
    "自动生成字幕.",
    "自動生成字幕",
    "自动生成弹幕",
    "自动生成字母",
    "自动生成字母幕",
    "自动生成字字幕",
    "自动生产字幕",
    "自动生产字母",
    "自动转换字幕",
    "自动翻译字幕",
    "自动生成字幕组",
    "自动字母",
    "自动刷",
}

SUBTITLE_META_TERMS = ("字幕", "字母", "翻译")
AUTO_GENERATED_META_TERMS = ("自动", "自動", "生成", "生产", "轉換", "转换", "转化", "zimu")


@dataclass(frozen=True)
class ParseResult:
    video_id: str
    records: list[dict[str, Any]]
    warnings: list[str]


def normalize_text(text: str | None) -> str:
    value = html.unescape(text or "")
    value = unicodedata.normalize("NFKC", value)
    value = "".join(PUNCT_REPLACEMENTS.get(char, char) for char in value)
    return WHITESPACE_RE.sub(" ", value).strip()


def safe_float(value: str, default: float = 0.0) -> tuple[float, bool]:
    try:
        return float(value), True
    except (TypeError, ValueError):
        return default, False


def safe_int(value: str, default: int = 0) -> tuple[int, bool]:
    try:
        return int(float(value)), True
    except (TypeError, ValueError):
        return default, False


def color_decimal_to_hex(color_decimal: int) -> str:
    color_decimal = max(0, min(color_decimal, 0xFFFFFF))
    return f"#{color_decimal:06X}"


def seconds_to_mmss(time_sec: float) -> str:
    total_seconds = max(0, int(time_sec))
    minutes = total_seconds // 60
    seconds = total_seconds % 60
    return f"{minutes:02d}:{seconds:02d}"


def parse_p_fields(p_raw: str) -> list[str]:
    return [field.strip() for field in (p_raw or "").split(",")]


def p_field(fields: list[str], index: int, default: str = "") -> str:
    if index >= len(fields):
        return default
    return fields[index] if fields[index] != "" else default


def make_generated_id(video_id: str, time_sec: float, index: int) -> str:
    stable_video_id = video_id.strip() or "unknown_video"
    return f"{stable_video_id}_{time_sec:.3f}_{index:06d}"


def extract_root_metadata(root: ET.Element) -> dict[str, str]:
    metadata: dict[str, str] = {}
    for child in root:
        tag = child.tag.split("}", 1)[-1]
        if tag in ROOT_META_TAGS and child.text is not None:
            metadata[tag] = child.text.strip()
    return metadata


def build_record(element: ET.Element, source_file: str, video_id: str, index: int) -> tuple[dict[str, Any] | None, str | None]:
    p_raw = element.attrib.get("p", "")
    p_fields = parse_p_fields(p_raw)
    if len(p_fields) < 2:
        return None, f"row_{index}: missing_or_short_p_attribute"

    time_sec, valid_time = safe_float(p_field(p_fields, 0, "0"))
    if not valid_time or time_sec < 0:
        return None, f"row_{index}: invalid_time"

    mode, _ = safe_int(p_field(p_fields, 1, "0"))
    font_size, _ = safe_int(p_field(p_fields, 2, "25"), 25)
    color_decimal, _ = safe_int(p_field(p_fields, 3, "16777215"), 16777215)
    created_at_unix, _ = safe_int(p_field(p_fields, 4, "0"))
    pool, _ = safe_int(p_field(p_fields, 5, "0"))
    user_hash = p_field(p_fields, 6)
    danmaku_id = p_field(p_fields, 7)
    weight, _ = safe_int(p_field(p_fields, 8, "0"))
    text_raw = element.text or ""

    return {
        "id": danmaku_id or make_generated_id(video_id, time_sec, index),
        "video_id": video_id,
        "source_file": source_file,
        "index": index,
        "time_sec": time_sec,
        "time_mmss": seconds_to_mmss(time_sec),
        "text_raw": text_raw,
        "text_norm": normalize_text(text_raw),
        "mode": mode,
        "mode_name": MODE_NAMES.get(mode, "unknown"),
        "font_size": font_size,
        "color_decimal": color_decimal,
        "color_hex": color_decimal_to_hex(color_decimal),
        "created_at_unix": created_at_unix,
        "pool": pool,
        "user_hash": user_hash,
        "danmaku_id": danmaku_id,
        "weight": weight,
    }, None


def parse_bilibili_xml(xml_path: Path, video_id: str = "") -> ParseResult:
    root = ET.parse(xml_path).getroot()
    return parse_bilibili_xml_root(root, xml_path.name, video_id)


def parse_bilibili_xml_bytes(xml_bytes: bytes, source_file: str, video_id: str = "") -> ParseResult:
    root = ET.fromstring(xml_bytes)
    return parse_bilibili_xml_root(root, Path(source_file).name, video_id)


def parse_bilibili_xml_root(root: ET.Element, source_file: str, fallback_video_id: str) -> ParseResult:
    metadata = extract_root_metadata(root)
    effective_video_id = fallback_video_id or metadata.get("chatid", "") or Path(source_file).stem
    records: list[dict[str, Any]] = []
    warnings: list[str] = []

    for index, element in enumerate(root.iter("d"), 1):
        record, warning = build_record(element, Path(source_file).name, effective_video_id, index)
        if warning:
            warnings.append(warning)
        if record is not None:
            records.append(project_record(record, "full"))

    records.sort(key=lambda item: (float(item["time_sec"]), int(item["index"])))
    return ParseResult(video_id=effective_video_id, records=records, warnings=warnings)


def project_record(record: dict[str, Any], profile: str = "minimal") -> dict[str, Any]:
    if profile == "full":
        fields = FULL_NORMALIZED_FIELDS
    elif profile == "raw_minimal":
        fields = RAW_REVIEW_FIELDS
    elif profile == "minimal":
        fields = MINIMAL_FIELDS
    else:
        raise ValueError(f"Unknown record profile: {profile}")
    return {field: record.get(field, "") for field in fields}


def project_records(records: list[dict[str, Any]], profile: str) -> list[dict[str, Any]]:
    return [project_record(record, profile) for record in records]


def trim_record(record: dict[str, Any]) -> dict[str, Any]:
    """Project records to the manual-review minimal field set."""
    return project_record(record, "minimal")


def text_for_filter(record: dict[str, Any]) -> str:
    return str(record.get("text_norm") or record.get("text_raw") or "").strip()


def compact_text(text: str) -> str:
    return re.sub(r"[\s\[\]【】()（）<>《》\"'“”‘’:：,，.!！?？;；~～_-]+", "", text).strip()


def classify_obvious_noise(record: dict[str, Any]) -> tuple[bool, str]:
    text = text_for_filter(record)
    compact = compact_text(text)

    if not text:
        return True, "empty_text"
    if text in EXACT_NOISE_TEXTS or compact in EXACT_NOISE_TEXTS:
        return True, "subtitle_or_translation_meta"
    if SUBTITLE_NOISE_RE.fullmatch(compact):
        return True, "subtitle_or_translation_meta"
    if any(term in compact for term in SUBTITLE_META_TERMS):
        return True, "subtitle_or_translation_meta"
    if "弹幕" in compact and any(term in compact for term in AUTO_GENERATED_META_TERMS):
        return True, "subtitle_or_translation_meta"
    if "zimu" in compact.lower() and any(term in compact for term in AUTO_GENERATED_META_TERMS):
        return True, "subtitle_or_translation_meta"

    return False, ""


def split_stage1_noise(
    records: list[dict[str, Any]],
    output_profile: str = "minimal",
) -> tuple[list[dict[str, Any]], list[dict[str, Any]], dict[str, int]]:
    kept: list[dict[str, Any]] = []
    noise: list[dict[str, Any]] = []
    reason_counts: dict[str, int] = {}

    for record in records:
        is_noise, reason = classify_obvious_noise(record)
        if is_noise:
            noise_record = project_record(record, output_profile)
            noise_record["noise_stage"] = "stage1_obvious_noise"
            noise_record["noise_reason"] = reason
            noise.append(noise_record)
            reason_counts[reason] = reason_counts.get(reason, 0) + 1
        else:
            kept.append(project_record(record, output_profile))

    return kept, noise, reason_counts


def assign_time_strata(records: list[dict[str, Any]], strata_count: int) -> dict[int, list[dict[str, Any]]]:
    if strata_count <= 0:
        raise ValueError("strata_count must be greater than 0")
    if not records:
        return {}

    max_time = max(float(record.get("time_sec", 0) or 0) for record in records)
    width = max_time / strata_count if max_time > 0 else 1
    strata: dict[int, list[dict[str, Any]]] = {index: [] for index in range(1, strata_count + 1)}

    for record in records:
        time_sec = float(record.get("time_sec", 0) or 0)
        stratum = min(int(time_sec / width) + 1, strata_count)
        strata[stratum].append(record)

    return strata


def quotas_for_strata(strata: dict[int, list[dict[str, Any]]], sample_size: int) -> dict[int, int]:
    non_empty = [index for index, rows in strata.items() if rows]
    if not non_empty:
        return {}

    base = sample_size // len(non_empty)
    remainder = sample_size % len(non_empty)
    quotas: dict[int, int] = {}

    for offset, index in enumerate(non_empty):
        quota = base + (1 if offset < remainder else 0)
        quotas[index] = min(quota, len(strata[index]))

    shortfall = sample_size - sum(quotas.values())
    if shortfall <= 0:
        return quotas

    for index in non_empty:
        if shortfall <= 0:
            break
        available = len(strata[index]) - quotas[index]
        add_count = min(available, shortfall)
        quotas[index] += add_count
        shortfall -= add_count

    return quotas


def sample_by_time_strata(
    records: list[dict[str, Any]],
    sample_size: int,
    strata_count: int,
    seed: int,
) -> list[dict[str, Any]]:
    if sample_size <= 0:
        raise ValueError("sample_size must be greater than 0")
    if sample_size >= len(records):
        return sorted(records, key=sample_sort_key)

    strata = assign_time_strata(records, strata_count)
    quotas = quotas_for_strata(strata, sample_size)
    rng = random.Random(seed)
    sampled: list[dict[str, Any]] = []

    for stratum in sorted(quotas):
        sampled.extend(rng.sample(strata[stratum], quotas[stratum]))

    return sorted(sampled, key=sample_sort_key)


def sample_sort_key(record: dict[str, Any]) -> tuple[float, int, str]:
    time_sec = float(record.get("time_sec", 0) or 0)
    index = int(record.get("index", 0) or 0)
    return time_sec, index, str(record.get("id", ""))


def write_json_array(path: Path, records: list[dict[str, Any]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(records, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
