#!/usr/bin/env python3
"""Run layers 1-3 of the Sports Danmaku Intelligence Pipeline."""

from __future__ import annotations

import argparse
import json
import subprocess
from datetime import datetime, timezone
from pathlib import Path
import sys

AGENTS_DIR = Path(__file__).resolve().parents[1] / "agents"
if str(AGENTS_DIR) not in sys.path:
    sys.path.insert(0, str(AGENTS_DIR))

from burst_map.features import build_feature_rows, records_to_feature_entries
from burst_map.normalization import (
    FORMAT_VERSION as NORMALIZATION_SCHEMA_VERSION,
    load_normalized_json,
    make_normalized_collection,
    normalization_stats,
    parse_bilibili_xml_file,
)
from burst_map.prefilter import (
    FORMAT_VERSION as FILTER_SCHEMA_VERSION,
    filter_records,
    make_filtered_collection,
)
from burst_map.schema import FEATURE_SCHEMA_VERSION
from run_burst_map import build_density, characterize_bursts, detect_bursts, write_csv, write_json


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Run layer 1 normalization, layer 2 filtering, and layer 3 features.")
    parser.add_argument("--input", required=True, type=Path, help="Input Bilibili XML or normalized JSON.")
    parser.add_argument("--output", required=True, type=Path, help="Output directory.")
    parser.add_argument("--input-format", choices=["auto", "xml", "normalized-json"], default="auto")
    parser.add_argument("--video-id", default="", help="Video ID or BV number for XML inputs.")
    parser.add_argument("--sport-type", default="", help="Sport type such as football, basketball, or esports.")
    parser.add_argument("--max-bursts", type=int, default=10)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    output_dir = args.output
    output_dir.mkdir(parents=True, exist_ok=True)

    input_format = resolve_input_format(args.input, args.input_format)
    if input_format == "normalized-json":
        normalized_records = load_normalized_json(args.input)
        normalization_warnings = []
    else:
        normalized_records, normalization_warnings = parse_bilibili_xml_file(
            args.input,
            video_id=args.video_id,
            sport_type=args.sport_type,
        )

    normalized_records.sort(key=lambda item: (float(item.get("time_sec", 0.0)), str(item.get("danmaku_id", ""))))
    normalized_collection = make_normalized_collection(normalized_records, normalization_warnings)
    write_json(output_dir / "normalized_danmaku.json", normalized_collection)
    write_csv(output_dir / "normalized_danmaku.csv", normalized_records)
    write_json(output_dir / "normalization_stats.json", normalized_collection["stats"])

    filtered_records, filter_stats = filter_records(normalized_records)
    filtered_collection = make_filtered_collection(filtered_records, filter_stats)
    write_json(output_dir / "filtered_danmaku.json", filtered_collection)
    write_csv(output_dir / "filtered_danmaku.csv", filtered_records)
    write_json(output_dir / "filter_stats.json", filter_stats)

    feature_entries = records_to_feature_entries(filtered_records)
    density5 = build_density(feature_entries, 5) if feature_entries else []
    density10 = build_density(feature_entries, 10) if feature_entries else []
    burst_info = detect_bursts(density5, max_bursts=args.max_bursts) if density5 else {"stats": {}, "bursts": []}
    bursts = characterize_bursts(burst_info["bursts"], feature_entries, burst_info["stats"]) if feature_entries else []
    feature_rows = build_feature_rows(feature_entries, density5, bursts)

    write_csv(output_dir / "density_5s.csv", density5)
    write_csv(output_dir / "density_10s.csv", density10)
    write_json(
        output_dir / "burst_events.json",
        {
            "source": {"input": str(args.input), "window_seconds": 5, "pipeline": "layer123"},
            "stats": burst_info["stats"],
            "bursts": bursts,
        },
    )
    write_csv(output_dir / "feature_danmaku.csv", feature_rows)
    write_json(output_dir / "feature_danmaku.json", feature_rows)

    manifest = build_manifest(
        args=args,
        input_format=input_format,
        normalized_records=normalized_records,
        filtered_records=filtered_records,
        feature_rows=feature_rows,
        bursts=bursts,
        normalization_warnings=normalization_warnings,
        filter_stats=filter_stats,
    )
    write_json(output_dir / "layer123_manifest.json", manifest)
    print(f"Layer 1-3 pipeline completed: {len(feature_rows)} rows, {len(bursts)} bursts")
    print(f"Output: {output_dir}")
    return 0


def resolve_input_format(input_path: Path, requested: str) -> str:
    if requested != "auto":
        return requested
    if input_path.suffix.lower() in {".json", ".jsonl"}:
        return "normalized-json"
    return "xml"


def build_manifest(
    args: argparse.Namespace,
    input_format: str,
    normalized_records: list[dict],
    filtered_records: list[dict],
    feature_rows: list[dict],
    bursts: list[dict],
    normalization_warnings: list[str],
    filter_stats: dict,
) -> dict:
    return {
        "format_version": "layer123_manifest_v1",
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "github_branch": "Danmaku-analysis-agent",
        "source_commit": git_commit_sha(),
        "input": str(args.input),
        "input_format": input_format,
        "video_id": args.video_id,
        "sport_type": args.sport_type,
        "schema_versions": {
            "normalization": NORMALIZATION_SCHEMA_VERSION,
            "filter": FILTER_SCHEMA_VERSION,
            "features": FEATURE_SCHEMA_VERSION,
        },
        "row_counts": {
            "normalized": len(normalized_records),
            "filtered": len(filtered_records),
            "features": len(feature_rows),
            "bursts": len(bursts),
        },
        "filter_label_counts": filter_stats.get("label_counts", {}),
        "warnings": {
            "normalization_count": len(normalization_warnings),
            "normalization_sample": normalization_warnings[:50],
        },
    }


def git_commit_sha() -> str:
    try:
        result = subprocess.run(
            ["git", "rev-parse", "HEAD"],
            cwd=Path(__file__).resolve().parents[2],
            check=True,
            capture_output=True,
            text=True,
        )
        return result.stdout.strip()
    except Exception:
        return ""


if __name__ == "__main__":
    raise SystemExit(main())
