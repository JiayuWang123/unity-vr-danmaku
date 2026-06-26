import argparse
import html
import json
import re
import unicodedata
from pathlib import Path
from typing import Iterable
from xml.etree import ElementTree


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
    """Normalize danmaku text while preserving semantic content and emojis."""
    text = html.unescape(text or "")
    text = unicodedata.normalize("NFKC", text)
    text = "".join(PUNCT_REPLACEMENTS.get(char, char) for char in text)
    text = re.sub(r"\s+", " ", text).strip()
    text = re.sub(r"([!?.。,，;；:：~～])\1+", r"\1\1\1", text)
    return text


def color_to_hex(raw_color: str) -> str:
    try:
        color_number = int(raw_color)
    except (TypeError, ValueError):
        color_number = 0xFFFFFF
    color_number = max(0, min(color_number, 0xFFFFFF))
    return f"#{color_number:06X}"


def parse_p_attribute(p_value: str) -> list[str]:
    return [part.strip() for part in (p_value or "").split(",")]


def build_record_from_values(
    p_value: str,
    text: str,
    source_file: Path,
    video_id: str,
    sport_type: str,
) -> dict:
    parts = parse_p_attribute(p_value)

    def part(index: int, default: str = "") -> str:
        return parts[index] if index < len(parts) and parts[index] != "" else default

    raw_mode = part(1, "1")
    try:
        mode = int(raw_mode)
    except ValueError:
        mode = 1

    text_raw = html.unescape(text or "").strip()

    return {
        "danmaku_id": part(7),
        "video_id": video_id,
        "sport_type": sport_type,
        "time_sec": float(part(0, "0")),
        "text_raw": text_raw,
        "text_norm": normalize_text(text_raw),
        "mode": mode,
        "mode_name": MODE_NAMES.get(mode, "unknown"),
        "font_size": int(float(part(2, "0"))),
        "color_hex": color_to_hex(part(3, "16777215")),
        "user_hash": part(6),
        "source": "bilibili_xml",
        "source_file": source_file.name,
    }


def build_record(
    element: ElementTree.Element,
    source_file: Path,
    video_id: str,
    sport_type: str,
) -> dict:
    return build_record_from_values(
        element.attrib.get("p", ""),
        element.text or "",
        source_file,
        video_id,
        sport_type,
    )


def get_excel_cell_text(cell: ElementTree.Element, namespace: dict[str, str]) -> str:
    data = cell.find("ss:Data", namespace)
    return "" if data is None or data.text is None else data.text


def get_excel_row_values(
    row: ElementTree.Element,
    namespace: dict[str, str],
) -> list[str]:
    values: list[str] = []
    for cell in row.findall("ss:Cell", namespace):
        cell_index = cell.attrib.get(
            "{urn:schemas-microsoft-com:office:spreadsheet}Index"
        )
        if cell_index:
            while len(values) < int(cell_index) - 1:
                values.append("")
        values.append(get_excel_cell_text(cell, namespace))
    return values


def parse_excel_xml_file(xml_path: Path, video_id: str, sport_type: str) -> list[dict]:
    namespace = {"ss": "urn:schemas-microsoft-com:office:spreadsheet"}
    root = ElementTree.parse(xml_path).getroot()
    rows = root.findall(".//ss:Worksheet/ss:Table/ss:Row", namespace)
    if not rows:
        return []

    headers = get_excel_row_values(rows[0], namespace)
    header_indexes = {header: index for index, header in enumerate(headers)}
    if "d" not in header_indexes or "p" not in header_indexes:
        return []

    records = []
    for row in rows[1:]:
        values = get_excel_row_values(row, namespace)

        def value(header: str) -> str:
            index = header_indexes.get(header, -1)
            return values[index] if 0 <= index < len(values) else ""

        p_value = value("p")
        text = value("d")
        if not p_value and not text:
            continue

        record_video_id = video_id or value("chatid")
        records.append(
            build_record_from_values(
                p_value,
                text,
                xml_path,
                record_video_id,
                sport_type,
            )
        )
    return records


def parse_xml_file(xml_path: Path, video_id: str, sport_type: str) -> list[dict]:
    tree = ElementTree.parse(xml_path)
    records = [
        build_record(element, xml_path, video_id, sport_type)
        for element in tree.iter("d")
    ]
    if records:
        return records
    return parse_excel_xml_file(xml_path, video_id, sport_type)



def write_records(records: Iterable[dict], output_path: Path | None, as_jsonl: bool) -> None:
    records = list(records)
    if as_jsonl:
        content = "\n".join(
            json.dumps(record, ensure_ascii=False) for record in records
        )
        if content:
            content += "\n"
    else:
        content = json.dumps(records, ensure_ascii=False, indent=2)
        content += "\n"

    if output_path:
        output_path.write_text(content, encoding="utf-8")
    else:
        print(content, end="")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Read Bilibili XML danmaku and export normalized JSON fields."
    )
    parser.add_argument("xml_file", type=Path, help="Input XML danmaku file.")
    parser.add_argument("-o", "--output", type=Path, help="Output JSON/JSONL file.")
    parser.add_argument("--video-id", default="", help="Video ID or BV number.")
    parser.add_argument(
        "--sport-type",
        default="",
        help="Sport type, such as football or esports.",
    )
    parser.add_argument(
        "--jsonl",
        action="store_true",
        help="Write one JSON object per line instead of a JSON array.",
    )
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    records = parse_xml_file(args.xml_file, args.video_id, args.sport_type)
    write_records(records, args.output, args.jsonl)


if __name__ == "__main__":
    main()
