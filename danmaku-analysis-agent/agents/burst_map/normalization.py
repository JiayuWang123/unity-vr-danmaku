"""Layer-1 normalization for Bilibili danmaku XML and normalized JSON."""

from __future__ import annotations

import html
import json
import re
import unicodedata
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path
from typing import Iterable
from xml.etree import ElementTree


FORMAT_VERSION = "normalized_danmaku_v1"

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
    "！": "!",
    "？": "?",
    "。": ".",
    "，": ",",
    "、": ",",
    "；": ";",
    "：": ":",
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
}


def normalize_text(text: str) -> str:
    """Normalize text while preserving semantic content and emoji-like chars."""
    text = html.unescape(text or "")
    text = unicodedata.normalize("NFKC", text)
    text = "".join(PUNCT_REPLACEMENTS.get(char, char) for char in text)
    text = re.sub(r"\s+", " ", text).strip()
    text = re.sub(r"([!?.。,，;；:：~～])\1+", r"\1\1\1", text)
    return text


def parse_p_attribute(p_value: str) -> list[str]:
    return [part.strip() for part in (p_value or "").split(",")]


def color_to_hex(raw_color: str) -> str:
    try:
        color_number = int(float(raw_color))
    except (TypeError, ValueError):
        color_number = 0xFFFFFF
    color_number = max(0, min(color_number, 0xFFFFFF))
    return f"#{color_number:06X}"


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


def build_record_from_values(
    p_value: str,
    text: str,
    source_file: Path,
    video_id: str,
    sport_type: str,
    row_index: int,
) -> tuple[dict | None, list[str]]:
    warnings = []
    parts = parse_p_attribute(p_value)

    def part(index: int, default: str = "") -> str:
        return parts[index] if index < len(parts) and parts[index] != "" else default

    if len(parts) < 2:
        warnings.append(f"row_{row_index}: missing_or_short_p_attribute")
        return None, warnings

    time_sec, valid_time = safe_float(part(0, "0"), 0.0)
    if not valid_time or time_sec < 0:
        warnings.append(f"row_{row_index}: invalid_time")
        return None, warnings

    mode, valid_mode = safe_int(part(1, "1"), 1)
    if not valid_mode:
        warnings.append(f"row_{row_index}: invalid_mode_defaulted")

    font_size, valid_font = safe_int(part(2, "25"), 25)
    if not valid_font:
        warnings.append(f"row_{row_index}: invalid_font_size_defaulted")

    raw_id = part(7)
    danmaku_id = raw_id or f"{source_file.stem}_{row_index:06d}"
    if not raw_id:
        warnings.append(f"row_{row_index}: generated_danmaku_id")

    text_raw = html.unescape(text or "").strip()
    record = {
        "danmaku_id": danmaku_id,
        "video_id": video_id,
        "sport_type": sport_type,
        "time_sec": time_sec,
        "text_raw": text_raw,
        "text_norm": normalize_text(text_raw),
        "mode": mode,
        "mode_name": MODE_NAMES.get(mode, "unknown"),
        "font_size": font_size,
        "color_hex": color_to_hex(part(3, "16777215")),
        "user_hash": part(6),
        "source": "bilibili_xml",
        "source_file": source_file.name,
    }
    return record, warnings


def parse_bilibili_xml_file(xml_path: Path, video_id: str = "", sport_type: str = "") -> tuple[list[dict], list[str]]:
    warnings: list[str] = []
    try:
        tree = ElementTree.parse(xml_path)
    except ElementTree.ParseError:
        records, excel_warnings = parse_excel_xml_file(xml_path, video_id, sport_type)
        return records, [f"xml_parse_failed_try_excel_xml: {xml_path.name}", *excel_warnings]

    records = []
    for index, element in enumerate(tree.iter("d"), 1):
        record, row_warnings = build_record_from_values(
            element.attrib.get("p", ""),
            element.text or "",
            xml_path,
            video_id,
            sport_type,
            index,
        )
        warnings.extend(row_warnings)
        if record is not None:
            records.append(record)

    if records:
        records.sort(key=lambda item: (float(item["time_sec"]), str(item["danmaku_id"])))
        return records, warnings

    records, excel_warnings = parse_excel_xml_file(xml_path, video_id, sport_type)
    return records, [*warnings, *excel_warnings]


def get_excel_cell_text(cell: ElementTree.Element, namespace: dict[str, str]) -> str:
    data = cell.find("ss:Data", namespace)
    return "" if data is None or data.text is None else data.text


def get_excel_row_values(row: ElementTree.Element, namespace: dict[str, str]) -> list[str]:
    values: list[str] = []
    for cell in row.findall("ss:Cell", namespace):
        cell_index = cell.attrib.get("{urn:schemas-microsoft-com:office:spreadsheet}Index")
        if cell_index:
            while len(values) < int(cell_index) - 1:
                values.append("")
        values.append(get_excel_cell_text(cell, namespace))
    return values


def parse_excel_xml_file(xml_path: Path, video_id: str = "", sport_type: str = "") -> tuple[list[dict], list[str]]:
    warnings: list[str] = []
    namespace = {"ss": "urn:schemas-microsoft-com:office:spreadsheet"}
    try:
        root = ElementTree.parse(xml_path).getroot()
    except ElementTree.ParseError as exc:
        return [], [f"excel_xml_parse_failed: {exc}"]

    rows = root.findall(".//ss:Worksheet/ss:Table/ss:Row", namespace)
    if not rows:
        return [], ["excel_xml_no_rows"]

    headers = get_excel_row_values(rows[0], namespace)
    header_indexes = {header: index for index, header in enumerate(headers)}
    if "d" not in header_indexes or "p" not in header_indexes:
        return [], ["excel_xml_missing_d_or_p_columns"]

    records = []
    for index, row in enumerate(rows[1:], 1):
        values = get_excel_row_values(row, namespace)

        def value(header: str) -> str:
            column = header_indexes.get(header, -1)
            return values[column] if 0 <= column < len(values) else ""

        p_value = value("p")
        text = value("d")
        if not p_value and not text:
            continue
        record_video_id = video_id or value("chatid")
        record, row_warnings = build_record_from_values(
            p_value,
            text,
            xml_path,
            record_video_id,
            sport_type,
            index,
        )
        warnings.extend(row_warnings)
        if record is not None:
            records.append(record)

    records.sort(key=lambda item: (float(item["time_sec"]), str(item["danmaku_id"])))
    return records, warnings


def load_normalized_json(path: Path) -> list[dict]:
    data = json.loads(path.read_text(encoding="utf-8"))
    if isinstance(data, dict):
        entries = data.get("entries", [])
    else:
        entries = data
    if not isinstance(entries, list):
        raise ValueError(f"Normalized JSON does not contain a list of entries: {path}")
    return [dict(item) for item in entries]


def normalization_stats(records: list[dict], warnings: list[str]) -> dict:
    mode_counts = Counter(str(record.get("mode_name", "unknown")) for record in records)
    source_counts = Counter(str(record.get("source_file", "")) for record in records)
    return {
        "parsed_count": len(records),
        "warning_count": len(warnings),
        "first_time_sec": records[0]["time_sec"] if records else 0,
        "last_time_sec": records[-1]["time_sec"] if records else 0,
        "mode_counts": dict(mode_counts),
        "source_file_counts": dict(source_counts),
        "warnings": warnings[:200],
    }


def make_normalized_collection(records: list[dict], warnings: list[str]) -> dict:
    return {
        "format_version": FORMAT_VERSION,
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "entries": records,
        "stats": normalization_stats(records, warnings),
    }


def write_records(records: Iterable[dict], output_path: Path | None, as_jsonl: bool) -> None:
    records = list(records)
    if as_jsonl:
        content = "\n".join(json.dumps(record, ensure_ascii=False) for record in records)
        if content:
            content += "\n"
    else:
        content = json.dumps(records, ensure_ascii=False, indent=2) + "\n"

    if output_path:
        output_path.parent.mkdir(parents=True, exist_ok=True)
        output_path.write_text(content, encoding="utf-8")
    else:
        print(content, end="")
