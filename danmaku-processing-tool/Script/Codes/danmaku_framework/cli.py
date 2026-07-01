#!/usr/bin/env python3
"""CLI entrypoint for the XML danmaku processing pipeline."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from .pipeline import PipelineOptions, run_pipeline_for_path


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Process Bilibili XML into configurable danmaku JSON arrays.")
    parser.add_argument("xml", type=Path, help="Input Bilibili XML file.")
    parser.add_argument("-o", "--output-dir", type=Path, default=Path("Danmu/json"), help="Output directory. Defaults to Danmu/json.")
    parser.add_argument("--video-id", default="", help="Stable video id. Defaults to XML chatid or file stem.")
    parser.add_argument("--workspace-root", type=Path, default=Path.cwd(), help="Root used for relative response paths.")
    parser.add_argument("--raw-minimal", action="store_true", help="Also export a review JSON with only time_sec, time_mmss, and text_raw.")
    parser.add_argument("--no-normalize", action="store_true", help="Do not export normalized JSON.")
    parser.add_argument("--normalized-profile", choices=("full", "minimal"), default="full", help="Fields kept in normalized/filtered outputs. Defaults to full.")
    parser.add_argument("--no-filter", action="store_true", help="Disable stage-1 obvious-noise filtering.")
    parser.add_argument("--no-filter-outputs", action="store_true", help="Run filtering internally but do not write filtered/noise JSON.")
    parser.add_argument("--sample", action="store_true", help="Generate a time-stratified random sample JSON.")
    parser.add_argument("--sample-source", choices=("raw_minimal", "normalized", "filtered"), default="filtered", help="JSON stream to sample from. Defaults to filtered.")
    parser.add_argument("--sample-size", type=int, default=200, help="Number of sampled records. Defaults to 200.")
    parser.add_argument("--sample-strata", type=int, default=10, help="Number of time strata for sampling. Defaults to 10.")
    parser.add_argument("--sample-seed", type=int, default=20260630, help="Random seed for reproducible sampling.")
    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    if not args.xml.exists():
        print(f"Input XML does not exist: {args.xml}", file=sys.stderr)
        return 1

    options = PipelineOptions(
        export_raw_minimal=args.raw_minimal,
        normalize=not args.no_normalize,
        normalized_profile=args.normalized_profile,
        filter_enabled=not args.no_filter,
        export_filter_outputs=not args.no_filter_outputs,
        sample_enabled=args.sample,
        sample_source=args.sample_source,
        sample_size=args.sample_size,
        sample_strata=args.sample_strata,
        sample_seed=args.sample_seed,
    )

    try:
        result = run_pipeline_for_path(args.xml, args.output_dir, args.video_id, options)
    except Exception as exc:  # noqa: BLE001 - CLI should print readable errors.
        print(f"Failed to process XML: {exc}", file=sys.stderr)
        return 1

    print(json.dumps(result.to_response(args.workspace_root), ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
