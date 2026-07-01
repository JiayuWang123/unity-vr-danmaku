#!/usr/bin/env python3
"""Compatibility CLI: split normalized danmaku JSON into filtered and noise arrays."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from danmaku_framework.core import split_stage1_noise, write_json_array


def load_records(path: Path) -> list[dict]:
    data = json.loads(path.read_text(encoding="utf-8"))
    if isinstance(data, dict):
        data = data.get("entries", [])
    if not isinstance(data, list):
        raise ValueError("Input JSON must be an array or a collection with entries.")
    return [item for item in data if isinstance(item, dict)]


def default_output_paths(input_path: Path) -> tuple[Path, Path]:
    stem = input_path.stem
    if stem.endswith("_normalized_danmaku"):
        stem = stem[: -len("_normalized_danmaku")]
    return (
        input_path.with_name(f"{stem}_filtered_danmaku.json"),
        input_path.with_name(f"{stem}_noise_danmaku.json"),
    )


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Split normalized danmaku JSON into obvious noise and filtered arrays.")
    parser.add_argument("input", type=Path, help="Input normalized danmaku JSON.")
    parser.add_argument("--filtered-output", type=Path, help="Output JSON for records kept after filtering.")
    parser.add_argument("--noise-output", type=Path, help="Output JSON for records classified as obvious noise.")
    parser.add_argument(
        "--profile",
        choices=("minimal", "full"),
        default="minimal",
        help="Fields kept in outputs. Defaults to minimal for backward compatibility.",
    )
    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    if not args.input.exists():
        print(f"Input JSON does not exist: {args.input}", file=sys.stderr)
        return 1

    default_filtered, default_noise = default_output_paths(args.input)
    filtered_output = args.filtered_output or default_filtered
    noise_output = args.noise_output or default_noise

    try:
        records = load_records(args.input)
    except Exception as exc:  # noqa: BLE001 - CLI should print readable errors.
        print(f"Failed to load input JSON: {exc}", file=sys.stderr)
        return 1

    kept, noise, reason_counts = split_stage1_noise(records, args.profile)
    write_json_array(filtered_output, kept)
    write_json_array(noise_output, noise)

    print(f"Input records: {len(records)}")
    print(f"Kept records: {len(kept)}")
    print(f"Noise records: {len(noise)}")
    for reason, count in sorted(reason_counts.items()):
        print(f"{reason}: {count}")
    print(f"Wrote filtered JSON to {filtered_output}")
    print(f"Wrote noise JSON to {noise_output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
