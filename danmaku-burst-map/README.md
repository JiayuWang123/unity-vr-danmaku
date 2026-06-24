# XML-only Danmaku Burst Map

`danmaku-burst-map` is an independent analysis tool inside the
`unity-vr-danmaku` research repository. It is intentionally separated from the
Unity runtime code: Unity can consume the exported CSV/JSON/image assets later,
but this package can be run by itself from a terminal with only a Bilibili XML
danmaku file.

The tool turns one XML file into burst-event tables, evidence-based topic and
emotion labels, charts, an HTML preview, an optional Excel workbook, and a
Markdown report. A pre-filtered JSON file is not required.

## Quick Start

Use Python and pass only the XML file path:

```powershell
python .\danmaku-burst-map\python\run_burst_map.py `
  --input "path\to\danmaku.xml" `
  --output ".\outputs\danmaku_burst_map_example" `
  --config ".\danmaku-burst-map\configs\default.yaml"
```

The MATLAB version has the same XML-only input model:

```matlab
run_burst_map("path/to/danmaku.xml", ...
  "outputs/danmaku_burst_map_example", ...
  "danmaku-burst-map/configs/default.yaml")
```

## Python Dependencies

The core CSV, JSON, Markdown, and HTML exports use the Python standard library.
Install optional packages for PNG charts, Excel output, and better Chinese text
handling:

```powershell
python -m pip install -r .\danmaku-burst-map\python\requirements.txt
```

## Output Files

The output directory contains:

- `normalized_danmaku.csv` and `normalized_danmaku.json`: parsed XML comments
  with cleaned text, display metadata, duplicate/spam flags, and evidence
  scores.
- `density_5s.csv` and `density_10s.csv`: raw XML danmaku density by time
  window.
- `burst_events.csv` and `burst_events.json`: detected burst windows, peak
  times, density, duration, baseline multiplier, and burst kind.
- `burst_characterization.csv`: topic label, dominant emotion, content mix,
  evidence terms, representative source comments, and confidence values.
- `analysis_report.md`: report-ready summary of the generated analysis.
- `burst_events_pretty.html`: readable browser preview of the burst table.
- `analysis_results.xlsx`: optional formatted workbook when `openpyxl` is
  installed.
- `density_curve_5s.png`: full-video density curve with thresholds and burst
  markers.
- `burst_timeline.png`: readable event timeline with burst duration bars,
  peak-density markers, labels, and burst-kind colors.
- `burst_peak_bar_chart.png`: peak density comparison.
- `burst_duration_chart.png`: burst duration comparison.
- `topic_keyword_evidence_chart.png`: high-frequency evidence terms from the
  original danmaku text.
- `emotion_content_summary.png`: aggregate emotion/content distribution.
- `burst_heatmap.png`: emotion evidence strength by burst.

Chart titles, axes, legends, and table headers are English. Original danmaku
comments are preserved in their source language.

## Method Overview

The analyzer follows an evidence-first pipeline:

1. Parse every Bilibili XML `<d>` node and read the timestamp from the `p`
   attribute.
2. Normalize comment text while preserving the original raw danmaku string.
3. Compute density from all XML comments, not from a filtered subset.
4. Detect replay-aware bursts with robust statistics.
5. For each burst, collect a context window around the burst event.
6. Extract terms, repeated phrases, representative comments, emotion cues, and
   content cues from the real comments in that window.
7. Export tables and charts using those extracted evidence values.

## Burst Detection Logic

Recorded Bilibili videos can have artificial opening peaks caused by check-ins,
reposts, and viewer behavior. The detector therefore excludes an opening guard
period when estimating the baseline:

```text
opening_guard = max(60 seconds, video_duration * 2%)
```

It then computes robust thresholds from 5-second windows:

```text
candidate_threshold = max(P90, median + 3 * robust_sigma)
strong_threshold = max(P95, median + 6 * robust_sigma)
```

Adjacent candidate windows are merged into a burst event. Events are ranked by
peak density, with an opening-peak penalty so the beginning of a replay does
not automatically dominate the result.

## Labeling Logic

Labels are not manually invented. Each burst label is generated from source XML
comments inside the burst context window:

- `evidence_terms` comes from token and n-gram frequency.
- `representative_comments` are selected from real comments with high evidence
  weight, useful length, and keyword coverage.
- `dominant_emotion` comes from matched emotion cue terms.
- `content_mix` comes from matched content cue terms.
- `topic_label` combines the strongest terms, representative comments, content
  mix, and emotion evidence.

The default rules are intentionally transparent and editable in
`python/run_burst_map.py`. They cover esports and general sports discourse such
as gameplay reactions, player/team evaluation, tactics, memes, chat behavior,
celebration, recap, and dispute.

## Heatmap Meaning

`burst_heatmap.png` now shows emotion evidence strength, not a binary 0/1
presence flag.

For each burst:

```text
emotion_strength = matched cue count for this emotion /
                   all matched emotion cue counts in this burst
```

For example, if a burst has 20 matched emotion cues and 15 of them match
`excitement`, the heatmap value for `excitement` is `0.75`. This makes the
heatmap interpretable as a normalized evidence share. It is still a transparent
rule-based text analysis, so it reflects visible language cues rather than a
hidden semantic model or manually assigned event knowledge.

## Project Layout

```text
danmaku-burst-map/
  configs/                 Default and sport-specific configs
  docs/                    Usage and methodology notes
  examples/                Small example placeholders
  matlab/                  MATLAB XML-only implementation
  python/                  Python XML-only implementation
  resources/               Stopwords, dictionaries, and label taxonomy
```

## Git Workflow Used For This Branch

This package lives on:

```text
Huwuw/danmaku-burst-map-generalization
```

The branch keeps the analysis tool independent from Unity scenes and runtime
assets. When updating the tool, commit only files under `danmaku-burst-map/`
and intentional sample outputs, leaving unrelated Unity project changes
unstaged unless they are part of a separate Unity task.

## More Details

See:

- `docs/burst_map_usage.md`
- `docs/methodology.md`
