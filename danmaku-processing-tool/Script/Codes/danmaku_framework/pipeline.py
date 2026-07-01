"""Configurable end-to-end XML danmaku processing pipeline."""

from __future__ import annotations

import re
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

from .core import (
    parse_bilibili_xml,
    parse_bilibili_xml_bytes,
    project_records,
    sample_by_time_strata,
    split_stage1_noise,
    write_json_array,
)


SAFE_NAME_RE = re.compile(r"[^0-9A-Za-z\u4e00-\u9fff._-]+")
PROFILE_CHOICES = {"full", "minimal"}
SAMPLE_SOURCE_CHOICES = {"raw_minimal", "normalized", "filtered"}


@dataclass(frozen=True)
class PipelineOptions:
    export_raw_minimal: bool = False
    normalize: bool = True
    normalized_profile: str = "full"
    filter_enabled: bool = True
    export_filter_outputs: bool = True
    sample_enabled: bool = False
    sample_source: str = "filtered"
    sample_size: int = 200
    sample_strata: int = 10
    sample_seed: int = 20260630

    def validate(self) -> None:
        if self.normalized_profile not in PROFILE_CHOICES:
            raise ValueError("normalized_profile must be full or minimal")
        if self.sample_source not in SAMPLE_SOURCE_CHOICES:
            raise ValueError("sample_source must be raw_minimal, normalized, or filtered")
        if self.sample_size <= 0:
            raise ValueError("sample_size must be greater than 0")
        if self.sample_strata <= 0:
            raise ValueError("sample_strata must be greater than 0")
        if not self.normalize and (self.filter_enabled or self.sample_source in {"normalized", "filtered"}):
            raise ValueError("filtering or normalized/filtered sampling requires normalization")
        if self.sample_source == "filtered" and not self.filter_enabled:
            raise ValueError("filtered sampling requires basic filtering")


@dataclass(frozen=True)
class PipelineResult:
    video_id: str
    input_count: int
    raw_minimal_count: int = 0
    normalized_count: int = 0
    kept_count: int = 0
    noise_count: int = 0
    sample_count: int = 0
    sample_source: str = ""
    warning_count: int = 0
    noise_reason_counts: dict[str, int] = field(default_factory=dict)
    files: dict[str, Path] = field(default_factory=dict)

    def to_response(self, workspace_root: Path) -> dict[str, Any]:
        def rel(path: Path) -> str:
            return path.resolve().relative_to(workspace_root.resolve()).as_posix()

        return {
            "video_id": self.video_id,
            "input_count": self.input_count,
            "raw_minimal_count": self.raw_minimal_count,
            "normalized_count": self.normalized_count,
            "kept_count": self.kept_count,
            "noise_count": self.noise_count,
            "sample_count": self.sample_count,
            "sample_source": self.sample_source,
            "warning_count": self.warning_count,
            "noise_reason_counts": self.noise_reason_counts,
            "files": {key: rel(path) for key, path in self.files.items()},
        }


def sanitize_name(value: str, fallback: str = "danmaku") -> str:
    name = SAFE_NAME_RE.sub("_", value.strip()).strip("._- ")
    return name or fallback


def output_path(output_dir: Path, video_id: str, suffix: str) -> Path:
    safe_video_id = sanitize_name(video_id)
    return output_dir / f"{safe_video_id}_{suffix}.json"


def run_pipeline_for_path(
    xml_path: Path,
    output_dir: Path,
    video_id: str = "",
    options: PipelineOptions | None = None,
) -> PipelineResult:
    parse_result = parse_bilibili_xml(xml_path, video_id)
    return write_pipeline_outputs(parse_result.video_id, parse_result.records, parse_result.warnings, output_dir, options)


def run_pipeline_for_bytes(
    xml_bytes: bytes,
    source_file: str,
    output_dir: Path,
    video_id: str = "",
    options: PipelineOptions | None = None,
) -> PipelineResult:
    parse_result = parse_bilibili_xml_bytes(xml_bytes, source_file, video_id)
    return write_pipeline_outputs(parse_result.video_id, parse_result.records, parse_result.warnings, output_dir, options)


def write_pipeline_outputs(
    video_id: str,
    parsed_records: list[dict[str, Any]],
    warnings: list[str],
    output_dir: Path,
    options: PipelineOptions | None = None,
) -> PipelineResult:
    opts = options or PipelineOptions()
    opts.validate()

    files: dict[str, Path] = {}
    raw_minimal_records: list[dict[str, Any]] = []
    normalized_records: list[dict[str, Any]] = []
    filtered_records: list[dict[str, Any]] = []
    noise_records: list[dict[str, Any]] = []
    reason_counts: dict[str, int] = {}

    if opts.export_raw_minimal:
        raw_minimal_records = project_records(parsed_records, "raw_minimal")
        path = output_path(output_dir, video_id, "raw_minimal_danmaku")
        write_json_array(path, raw_minimal_records)
        files["raw_minimal"] = path

    if opts.normalize:
        normalized_records = project_records(parsed_records, opts.normalized_profile)
        path = output_path(output_dir, video_id, "normalized_danmaku")
        write_json_array(path, normalized_records)
        files["normalized"] = path

    if opts.filter_enabled:
        filtered_records, noise_records, reason_counts = split_stage1_noise(parsed_records, opts.normalized_profile)
        if opts.export_filter_outputs:
            filtered_path = output_path(output_dir, video_id, "filtered_danmaku")
            noise_path = output_path(output_dir, video_id, "noise_danmaku")
            write_json_array(filtered_path, filtered_records)
            write_json_array(noise_path, noise_records)
            files["filtered"] = filtered_path
            files["noise"] = noise_path

    sample_count = 0
    if opts.sample_enabled:
        if opts.sample_source == "raw_minimal" and not raw_minimal_records:
            sample_source_records = project_records(parsed_records, "raw_minimal")
        else:
            sample_source_records = select_sample_source(
                opts.sample_source,
                raw_minimal_records,
                normalized_records,
                filtered_records,
            )
        sampled_records = sample_by_time_strata(
            sample_source_records,
            opts.sample_size,
            opts.sample_strata,
            opts.sample_seed,
        )
        sample_count = len(sampled_records)
        sample_path = output_path(output_dir, video_id, f"sample_{opts.sample_source}_{opts.sample_size}")
        write_json_array(sample_path, sampled_records)
        files["sample"] = sample_path

    return PipelineResult(
        video_id=video_id,
        input_count=len(parsed_records),
        raw_minimal_count=len(raw_minimal_records),
        normalized_count=len(normalized_records),
        kept_count=len(filtered_records),
        noise_count=len(noise_records),
        sample_count=sample_count,
        sample_source=opts.sample_source if opts.sample_enabled else "",
        warning_count=len(warnings),
        noise_reason_counts=reason_counts,
        files=files,
    )


def select_sample_source(
    source: str,
    raw_minimal_records: list[dict[str, Any]],
    normalized_records: list[dict[str, Any]],
    filtered_records: list[dict[str, Any]],
) -> list[dict[str, Any]]:
    if source == "raw_minimal":
        return raw_minimal_records
    if source == "normalized":
        return normalized_records
    if source == "filtered":
        return filtered_records
    raise ValueError(f"Unknown sample source: {source}")
