#!/usr/bin/env python3
"""
从 Bilibili XML 弹幕中分层随机抽样，生成人工标注任务包。

用法:
  python Tools/sample_danmaku_for_annotation.py \\
    --input path/to/danmaku.xml \\
    --output Assets/Annotation/pilot_300 \\
    --n 300 --annotators A,B,C --seed 42

  python Tools/sample_danmaku_for_annotation.py \\
    --input path/to/danmaku.xml \\
    --output Assets/Annotation/full_2000 \\
    --n 2000 --bin-sec 60 --burst-oversample 1.5 \\
    --annotators A,B,C --seed 2025
"""
from __future__ import annotations

import argparse
import csv
import hashlib
import json
import math
import random
import xml.etree.ElementTree as ET
from collections import defaultdict
from dataclasses import dataclass, asdict
from pathlib import Path
from typing import Dict, List, Optional, Tuple


@dataclass
class DanmakuItem:
    id: str
    time_sec: float
    text: str
    bin_index: int
    density_weight: float = 1.0


def parse_bilibili_xml(path: Path) -> List[DanmakuItem]:
    tree = ET.parse(path)
    root = tree.getroot()
    items: List[DanmakuItem] = []

    for idx, node in enumerate(root.findall("d")):
        p_attr = node.attrib.get("p", "")
        if not p_attr:
            continue
        parts = p_attr.split(",")
        try:
            time_sec = float(parts[0])
        except (ValueError, IndexError):
            continue
        text = (node.text or "").strip()
        if not text:
            continue
        items.append(
            DanmakuItem(
                id=f"d_{idx:06d}",
                time_sec=time_sec,
                text=text,
                bin_index=-1,
            )
        )
    return items


def assign_bins(items: List[DanmakuItem], bin_sec: float) -> Tuple[float, int]:
    if not items:
        return 0.0, 0
    max_t = max(i.time_sec for i in items)
    n_bins = max(1, int(math.ceil(max_t / bin_sec)))
    for item in items:
        item.bin_index = min(int(item.time_sec // bin_sec), n_bins - 1)
    return max_t, n_bins


def compute_density_weights(items: List[DanmakuItem], bin_sec: float, burst_oversample: float) -> None:
    """高密度时间 bin 提高被抽中概率（burst oversample）。"""
    if burst_oversample <= 1.0:
        return

    counts: Dict[int, int] = defaultdict(int)
    for item in items:
        counts[item.bin_index] += 1

    if not counts:
        return

    avg = sum(counts.values()) / len(counts)
    threshold = avg * 1.5

    for item in items:
        c = counts[item.bin_index]
        if c >= threshold:
            item.density_weight = burst_oversample
        else:
            item.density_weight = 1.0


def weighted_sample_without_replacement(pool: List[DanmakuItem], k: int, rng: random.Random) -> List[DanmakuItem]:
    if k >= len(pool):
        return list(pool)

    remaining = list(pool)
    chosen: List[DanmakuItem] = []

    for _ in range(k):
        weights = [item.density_weight for item in remaining]
        total = sum(weights)
        pick = rng.uniform(0, total)
        acc = 0.0
        for i, item in enumerate(remaining):
            acc += weights[i]
            if acc >= pick:
                chosen.append(item)
                remaining.pop(i)
                break
    return chosen


def stratified_sample(
    items: List[DanmakuItem],
    n: int,
    rng: random.Random,
    min_per_bin: int = 1,
) -> List[DanmakuItem]:
    """每个时间 bin 至少抽 min_per_bin 条，剩余名额按 bin 大小比例分配。"""
    if n >= len(items):
        return sorted(items, key=lambda x: x.time_sec)

    by_bin: Dict[int, List[DanmakuItem]] = defaultdict(list)
    for item in items:
        by_bin[item.bin_index].append(item)

    selected: List[DanmakuItem] = []
    selected_ids = set()

    # 每 bin 保底
    for b in sorted(by_bin.keys()):
        pool = by_bin[b]
        k = min(min_per_bin, len(pool))
        picks = weighted_sample_without_replacement(pool, k, rng)
        for p in picks:
            if p.id not in selected_ids:
                selected.append(p)
                selected_ids.add(p.id)

    # 剩余名额按 bin 弹幕数量比例分配
    remaining = n - len(selected)
    if remaining <= 0:
        return sorted(selected[:n], key=lambda x: x.time_sec)

    total_count = sum(len(v) for v in by_bin.values())
    for b in sorted(by_bin.keys()):
        pool = [x for x in by_bin[b] if x.id not in selected_ids]
        if not pool or remaining <= 0:
            continue
        quota = max(1, int(round(remaining * len(by_bin[b]) / total_count)))
        quota = min(quota, len(pool), remaining)
        picks = weighted_sample_without_replacement(pool, quota, rng)
        for p in picks:
            selected.append(p)
            selected_ids.add(p.id)
            remaining -= 1
            if remaining <= 0:
                break

    # 若仍不足，全局补齐
    if len(selected) < n:
        rest = [x for x in items if x.id not in selected_ids]
        extra = weighted_sample_without_replacement(rest, n - len(selected), rng)
        selected.extend(extra)

    return sorted(selected[:n], key=lambda x: x.time_sec)


def make_overlap_set(all_samples: List[DanmakuItem], overlap_n: int, rng: random.Random) -> List[DanmakuItem]:
    if overlap_n <= 0:
        return []
    k = min(overlap_n, len(all_samples))
    return rng.sample(all_samples, k)


def assign_pairs(
    samples: List[DanmakuItem],
    annotators: List[str],
    overlap_ids: set,
    rng: random.Random,
) -> List[dict]:
    """
    每条样本分配给 2 位标注者（轮换）。
    overlap 集合内：全员都标（预仲裁用）。
    """
    if len(annotators) < 2:
        raise ValueError("至少需要 2 名标注者")

    rows = []
    for i, item in enumerate(samples):
        if item.id in overlap_ids:
            assigned = annotators[:]
        else:
            a = annotators[i % len(annotators)]
            b = annotators[(i + 1) % len(annotators)]
            assigned = [a, b] if a != b else [a, annotators[(i + 2) % len(annotators)]]
        rows.append(
            {
                "sample_id": item.id,
                "time_sec": round(item.time_sec, 3),
                "text": item.text,
                "bin_index": item.bin_index,
                "annotator_1": assigned[0],
                "annotator_2": assigned[1] if len(assigned) > 1 else "",
                "is_overlap_pilot": item.id in overlap_ids,
                "adjudicator": annotators[-1] if len(annotators) >= 3 else "",
            }
        )
    return rows


def export_annotation_template(out_dir: Path, samples: List[DanmakuItem]) -> None:
    """导出空白标注字段，供 Excel / Label Studio 使用。"""
    fields = [
        "sample_id", "time_sec", "text", "annotator",
        "filter_label", "content_primary", "emotion_primary",
        "is_information_sharing", "is_sentiment_expression",
        "camp", "vr_utility", "burst_id", "notes",
    ]
    path = out_dir / "annotation_template.csv"
    with path.open("w", encoding="utf-8-sig", newline="") as f:
        w = csv.writer(f)
        w.writerow(fields)
        for s in samples:
            w.writerow([s.id, f"{s.time_sec:.3f}", s.text, ""] + [""] * 9)


def main() -> None:
    parser = argparse.ArgumentParser(description="分层随机抽取弹幕用于人工标注")
    parser.add_argument("--input", required=True, help="Bilibili XML 弹幕文件")
    parser.add_argument("--output", required=True, help="输出目录")
    parser.add_argument("--n", type=int, default=300, help="抽样总数（500 或 2000）")
    parser.add_argument("--bin-sec", type=float, default=60.0, help="分层时间窗（秒）")
    parser.add_argument("--burst-oversample", type=float, default=1.5, help="高密度 bin 过采样权重")
    parser.add_argument("--min-per-bin", type=int, default=1, help="每个时间 bin 至少抽取条数")
    parser.add_argument("--overlap", type=int, default=0, help="全员重复标注的预仲裁条数（如 200）")
    parser.add_argument("--annotators", default="A,B,C", help="标注者代号，逗号分隔，最后一人作第三人")
    parser.add_argument("--seed", type=int, default=42, help="随机种子（论文需报告）")
    parser.add_argument("--video-id", default="worldcup_final", help="视频标识")
    args = parser.parse_args()

    rng = random.Random(args.seed)
    input_path = Path(args.input)
    out_dir = Path(args.output)
    out_dir.mkdir(parents=True, exist_ok=True)

    items = parse_bilibili_xml(input_path)
    if not items:
        raise SystemExit(f"未从 {input_path} 解析到弹幕")

    duration_sec, n_bins = assign_bins(items, args.bin_sec)
    compute_density_weights(items, args.bin_sec, args.burst_oversample)

    samples = stratified_sample(items, args.n, rng, min_per_bin=args.min_per_bin)
    overlap_n = args.overlap or min(200, max(50, args.n // 5))
    overlap_items = make_overlap_set(samples, overlap_n, rng)
    overlap_ids = {x.id for x in overlap_items}

    annotators = [a.strip() for a in args.annotators.split(",") if a.strip()]
    assignments = assign_pairs(samples, annotators, overlap_ids, rng)

    manifest = {
        "schema_version": "1.0",
        "video_id": args.video_id,
        "source_file": str(input_path),
        "sampling": {
            "method": "stratified_random_by_time_bin",
            "n_total": len(samples),
            "n_source": len(items),
            "duration_sec": round(duration_sec, 2),
            "bin_sec": args.bin_sec,
            "n_bins": n_bins,
            "burst_oversample": args.burst_oversample,
            "min_per_bin": args.min_per_bin,
            "overlap_pilot_n": len(overlap_items),
            "random_seed": args.seed,
            "references": [
                "Zhang et al. CHI 2025 CoKnowledge (random + IRR)",
                "He et al. CSCW 2021 (dual-axis info/sentiment)",
            ],
        },
        "annotators": annotators,
        "samples": [
            {
                **asdict(s),
                "video_clip_hint_sec": [max(0, s.time_sec - 5), s.time_sec + 5],
            }
            for s in samples
        ],
    }

    (out_dir / "samples_to_label.json").write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8"
    )

    with (out_dir / "assignment_rotation.csv").open("w", encoding="utf-8-sig", newline="") as f:
        w = csv.DictWriter(f, fieldnames=list(assignments[0].keys()))
        w.writeheader()
        w.writerows(assignments)

    with (out_dir / "adjudication_queue_template.csv").open("w", encoding="utf-8-sig", newline="") as f:
        w = csv.writer(f)
        w.writerow(
            [
                "sample_id", "time_sec", "text",
                "label_a", "label_b", "agree",
                "adjudicator_label", "adjudicator_notes",
            ]
        )

    export_annotation_template(out_dir, samples)

    # 按标注者拆分任务包
    per_annotator: Dict[str, List[dict]] = defaultdict(list)
    for row in assignments:
        for key in ("annotator_1", "annotator_2"):
            who = row[key]
            if who:
                per_annotator[who].append(row)

    for who, rows in per_annotator.items():
        p = out_dir / f"task_{who}.csv"
        with p.open("w", encoding="utf-8-sig", newline="") as f:
            w = csv.DictWriter(f, fieldnames=list(rows[0].keys()))
            w.writeheader()
            w.writerows(rows)

    readme = f"""# 标注任务包

- 源文件: {input_path.name}
- 抽样: {len(samples)} / {len(items)} 条
- 视频时长约: {duration_sec:.0f}s, {n_bins} 个 {args.bin_sec}s 时间窗
- 预仲裁 overlap: {len(overlap_items)} 条（全员重复标）
- 随机种子: {args.seed}
- 标注者: {', '.join(annotators)}

## 文件说明
- samples_to_label.json — 完整样本清单
- assignment_rotation.csv — 双人轮换分配
- task_A.csv / task_B.csv — 各人任务
- annotation_template.csv — 空白标注表
- adjudication_queue_template.csv — 不一致项第三人裁决

## 标注时请观看 time_sec ±5s 视频片段（CoKnowledge 要求）
"""
    (out_dir / "README.txt").write_text(readme, encoding="utf-8")

    print(f"完成: {len(samples)} 条 -> {out_dir}")
    print(f"  overlap pilot: {len(overlap_items)} 条")
    print(f"  时长 {duration_sec:.0f}s, bins={n_bins}")


if __name__ == "__main__":
    main()
