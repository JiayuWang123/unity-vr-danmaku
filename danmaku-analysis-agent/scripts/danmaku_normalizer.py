#!/usr/bin/env python3
"""Compatibility CLI for layer-1 danmaku normalization."""

from __future__ import annotations

import argparse
from pathlib import Path
import sys


AGENTS_DIR = Path(__file__).resolve().parents[1] / "agents"
if str(AGENTS_DIR) not in sys.path:
    sys.path.insert(0, str(AGENTS_DIR))

from burst_map.normalization import parse_bilibili_xml_file, write_records


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Read Bilibili XML danmaku and export normalized JSON fields.")
    parser.add_argument("xml_file", type=Path, help="Input XML danmaku file.")
    parser.add_argument("-o", "--output", type=Path, help="Output JSON/JSONL file.")
    parser.add_argument("--video-id", default="", help="Video ID or BV number.")
    parser.add_argument("--sport-type", default="", help="Sport type, such as football or esports.")
    parser.add_argument("--jsonl", action="store_true", help="Write one JSON object per line instead of a JSON array.")
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    records, warnings = parse_bilibili_xml_file(args.xml_file, args.video_id, args.sport_type)
    for warning in warnings[:20]:
        print(f"warning: {warning}", file=sys.stderr)
    if len(warnings) > 20:
        print(f"warning: {len(warnings) - 20} additional normalization warnings omitted", file=sys.stderr)
    write_records(records, args.output, args.jsonl)


if __name__ == "__main__":
    main()
