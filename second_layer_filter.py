#!/usr/bin/env python3
"""Compatibility CLI and imports for layer-2 rule pre-filtering."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
import sys


ROOT = Path(__file__).resolve().parent
PYTHON_DIR = ROOT / "danmaku-burst-map" / "python"
if str(PYTHON_DIR) not in sys.path:
    sys.path.insert(0, str(PYTHON_DIR))

from burst_map.prefilter import classify, classify_with_evidence, filter_records, load_records, make_filtered_collection


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Apply layer-2 rule pre-filtering to normalized danmaku JSON.")
    parser.add_argument("--input", type=Path, help="Input normalized JSON from layer 1.")
    parser.add_argument("--output", type=Path, help="Output filtered JSON.")
    parser.add_argument("--text", help="Classify a single text string.")
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    if args.text is not None:
        print(json.dumps(classify_with_evidence(args.text), ensure_ascii=False, indent=2))
        return
    if not args.input or not args.output:
        raise SystemExit("Use --text for a single string or provide --input and --output.")
    records = load_records(args.input)
    filtered, stats = filter_records(records)
    collection = make_filtered_collection(filtered, stats)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(collection, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
