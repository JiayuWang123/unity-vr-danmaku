# Danmaku Burst Map Usage

This tool generates burst-map analysis deliverables from a Bilibili danmaku XML
file. A filtered JSON file is not required.

## Python

```bash
python danmaku-burst-map/python/run_burst_map.py \
  --input path/to/danmaku.xml \
  --output outputs/danmaku_burst_map_generalized \
  --config danmaku-burst-map/configs/default.yaml
```

The Python implementation writes CSV, JSON, Markdown, HTML, optional XLSX, and
PNG charts when plotting dependencies are installed. Without optional packages,
the core tables and report are still generated.

Layer-3 feature output is enabled by default. To run the older burst-map flow
without per-comment features, add:

```bash
--no-write-features
```

## MATLAB

```matlab
run_burst_map("path/to/danmaku.xml", ...
  "outputs/danmaku_burst_map_generalized", ...
  "danmaku-burst-map/configs/default.yaml")
```

The MATLAB implementation mirrors the XML-only workflow with lightweight topic
evidence extraction.

## Main Outputs

- `normalized_danmaku.csv`
- `feature_danmaku.csv`
- `feature_danmaku.json`
- `density_5s.csv`
- `density_10s.csv`
- `burst_events.csv`
- `burst_characterization.csv`
- `burst_events.json`
- `analysis_report.md`
- `density_curve_5s.png` or `density_curve_5s.svg`
- `burst_timeline.png`
- `burst_peak_bar_chart.png`
- `burst_duration_chart.png`
- `topic_keyword_evidence_chart.png`
- `emotion_content_summary.png`
- `burst_heatmap.png`
- `burst_events_pretty.html`
- `analysis_results.xlsx` when `openpyxl` is installed

All chart labels, table headers, and report section headings are English.
Original danmaku comments are preserved in their source language.

