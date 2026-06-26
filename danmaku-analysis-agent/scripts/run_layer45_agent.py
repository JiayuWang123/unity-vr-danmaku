#!/usr/bin/env python3
"""Run the layer-4/5 Qwen Agent over layer-1/2/3 danmaku outputs."""

from __future__ import annotations

import argparse
import json
import subprocess
from pathlib import Path
import sys

AGENTS_DIR = Path(__file__).resolve().parents[1] / "agents"
if str(AGENTS_DIR) not in sys.path:
    sys.path.insert(0, str(AGENTS_DIR))

from burst_map.alibaba_qwen_client import DEFAULT_MODEL, QwenClient
from burst_map.llm_routing import load_bursts, load_feature_rows, select_llm_candidates
from burst_map.llm_schema import build_vr_mapping_event, make_manifest
from run_burst_map import write_csv, write_json


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Run layer 4/5 LLM routing, scoring, burst summaries, and VR/TTS planning.")
    parser.add_argument("--input-dir", required=True, type=Path, help="Directory produced by run_layer123_pipeline.py.")
    parser.add_argument("--output", required=True, type=Path, help="Layer 4/5 output directory.")
    parser.add_argument("--model", default=DEFAULT_MODEL, help="Alibaba Qwen model name, default qwen-plus.")
    parser.add_argument("--base-url", default="", help="Optional OpenAI-compatible base URL. Defaults to DashScope compatible endpoint.")
    parser.add_argument("--workspace-id", default="", help="Optional Alibaba Cloud Model Studio workspace id.")
    parser.add_argument("--region", default="cn-beijing", help="Region used with --workspace-id, default cn-beijing.")
    parser.add_argument("--mock", action="store_true", help="Run without API calls and generate deterministic local mock outputs.")
    parser.add_argument("--dry-run", action="store_true", help="Generate candidates and mock outputs without sending API requests.")
    parser.add_argument("--batch-size", type=int, default=20, help="Per-comment scoring batch size for Qwen calls.")
    parser.add_argument("--max-comments-per-burst", type=int, default=60, help="Candidate cap per burst window.")
    parser.add_argument("--max-global-comments", type=int, default=250, help="Candidate cap outside burst-specific routing.")
    parser.add_argument("--max-total-candidates", type=int, default=800, help="Global candidate cap for a larger 10-minute test video.")
    parser.add_argument("--include-noise-near-burst", action="store_true", help="Allow REMOVE_NOISE records near burst peaks when they carry sports/emotion evidence.")
    parser.add_argument("--timeout-seconds", type=int, default=60)
    parser.add_argument("--max-retries", type=int, default=1)
    parser.add_argument("--temperature", type=float, default=0.2)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    args.output.mkdir(parents=True, exist_ok=True)

    feature_path = args.input_dir / "feature_danmaku.json"
    burst_path = args.input_dir / "burst_events.json"
    if not feature_path.exists():
        raise FileNotFoundError(f"Missing layer-3 feature file: {feature_path}")
    if not burst_path.exists():
        raise FileNotFoundError(f"Missing burst file: {burst_path}")

    feature_rows = load_feature_rows(feature_path)
    bursts = load_bursts(burst_path)
    candidates = select_llm_candidates(
        feature_rows,
        bursts,
        max_comments_per_burst=args.max_comments_per_burst,
        max_global_comments=args.max_global_comments,
        max_total_candidates=args.max_total_candidates,
        include_noise_near_burst=args.include_noise_near_burst,
    )
    write_json(args.output / "llm_candidates.json", candidates)
    write_csv(args.output / "llm_candidates.csv", candidates)

    client = QwenClient(
        model=args.model,
        base_url=args.base_url or None,
        workspace_id=args.workspace_id or None,
        region=args.region,
        timeout_seconds=args.timeout_seconds,
        temperature=args.temperature,
        max_retries=args.max_retries,
        dry_run=args.dry_run,
        mock=args.mock,
    )
    scored_comments, score_errors = client.score_comments(candidates, batch_size=args.batch_size)
    write_json(args.output / "llm_scored_danmaku.json", scored_comments)
    write_csv(args.output / "llm_scored_danmaku.csv", scored_comments)

    burst_summaries, summary_errors = client.summarize_bursts(bursts, candidates, scored_comments)
    write_json(args.output / "burst_agent_summaries.json", burst_summaries)
    write_csv(args.output / "burst_agent_summaries.csv", burst_summaries)

    scored_by_burst = group_scored_by_burst(scored_comments)
    burst_by_id = {str(row.get("burst_id", "")): row for row in bursts}
    vr_events = [
        build_vr_mapping_event(summary, burst_by_id.get(str(summary.get("burst_id", "")), {}), scored_by_burst.get(str(summary.get("burst_id", "")), []))
        for summary in burst_summaries
    ]
    write_json(args.output / "vr_mapping_events.json", vr_events)
    write_csv(args.output / "vr_mapping_events.csv", flatten_vr_events(vr_events))

    tts_candidates = build_tts_candidates(scored_comments)
    write_json(args.output / "tts_candidates.json", tts_candidates)
    write_csv(args.output / "tts_candidates.csv", tts_candidates)

    errors = score_errors + summary_errors
    if errors:
        write_json(args.output / "llm_errors.json", errors)

    manifest = make_manifest(
        input_dir=str(args.input_dir),
        output_dir=str(args.output),
        model=args.model,
        mode="mock" if args.mock else "dry_run" if args.dry_run else "live",
        candidate_count=len(candidates),
        scored_count=len(scored_comments),
        burst_count=len(burst_summaries),
        usage={**client.usage, "source_commit": git_commit_sha()},
        errors=errors,
        config={
            "batch_size": args.batch_size,
            "max_comments_per_burst": args.max_comments_per_burst,
            "max_global_comments": args.max_global_comments,
            "max_total_candidates": args.max_total_candidates,
            "include_noise_near_burst": args.include_noise_near_burst,
            "temperature": args.temperature,
        },
    )
    write_json(args.output / "layer45_manifest.json", manifest)

    print(f"Layer 4/5 Agent completed: {len(candidates)} candidates, {len(scored_comments)} scored comments, {len(burst_summaries)} burst summaries")
    print(f"Mode: {manifest['mode']}; total tokens: {client.usage.get('total_tokens', 0)}")
    print(f"Output: {args.output}")
    return 0


def group_scored_by_burst(scored_comments: list[dict]) -> dict[str, list[dict]]:
    grouped: dict[str, list[dict]] = {}
    for row in scored_comments:
        burst_id = str(row.get("source_burst_id", ""))
        if burst_id:
            grouped.setdefault(burst_id, []).append(row)
    return grouped


def flatten_vr_events(events: list[dict]) -> list[dict]:
    rows = []
    for event in events:
        row = dict(event)
        row["tts_lines"] = json.dumps(row.get("tts_lines", []), ensure_ascii=False)
        row["representative_danmaku_ids"] = ";".join(row.get("representative_danmaku_ids", []))
        row["evidence"] = json.dumps(row.get("evidence", {}), ensure_ascii=False)
        rows.append(row)
    return rows


def build_tts_candidates(scored_comments: list[dict]) -> list[dict]:
    rows = []
    for row in sorted(scored_comments, key=lambda item: float(item.get("tts_value_score", 0.0)), reverse=True):
        if float(row.get("tts_value_score", 0.0)) < 0.65:
            continue
        if row.get("content_label") in {"noise", "viewer_meta"}:
            continue
        text = str(row.get("text_norm", "")).strip()
        if not text or len(text) > 80:
            continue
        rows.append(
            {
                "danmaku_id": row.get("danmaku_id", ""),
                "time_sec": row.get("time_sec", 0.0),
                "text_norm": text,
                "tts_value_score": row.get("tts_value_score", 0.0),
                "emotion_label": row.get("emotion_label", ""),
                "content_label": row.get("content_label", ""),
                "confidence": row.get("confidence", 0.0),
                "source_burst_id": row.get("source_burst_id", ""),
            }
        )
    return rows


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
