#!/usr/bin/env python3
"""Compatibility CLI: normalize Bilibili XML into a minimal JSON array."""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from danmaku_framework.core import parse_bilibili_xml, write_json_array


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Parse Bilibili XML danmaku into normalized minimal JSON array.")
    parser.add_argument("xml", type=Path, help="Input Bilibili XML danmaku file.")
    parser.add_argument(
        "-o",
        "--output",
        type=Path,
        default=Path("normalized_danmaku.json"),
        help="Output JSON path. Defaults to normalized_danmaku.json.",
    )
    parser.add_argument(
        "--video-id",
        default="",
        help="Stable video id used for fallback ids. Defaults to XML chatid, then the XML filename.",
    )
    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    if not args.xml.exists():
        print(f"Input XML does not exist: {args.xml}", file=sys.stderr)
        return 1

    try:
        result = parse_bilibili_xml(args.xml, args.video_id)
    except Exception as exc:  # noqa: BLE001 - CLI should print readable errors.
        print(f"Failed to parse XML: {exc}", file=sys.stderr)
        return 1

    write_json_array(args.output, result.records)
    for warning in result.warnings[:20]:
        print(f"warning: {warning}", file=sys.stderr)
    if len(result.warnings) > 20:
        print(f"warning: {len(result.warnings) - 20} additional warnings omitted", file=sys.stderr)
    print(f"Parsed {len(result.records)} danmaku records")
    print(f"Wrote JSON to {args.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
