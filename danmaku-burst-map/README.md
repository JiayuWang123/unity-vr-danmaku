# XML-only Danmaku Burst Map

`danmaku-burst-map` is an independent analysis tool inside the
`unity-vr-danmaku` research repository. It is intentionally separated from the
Unity runtime code: Unity can consume the exported CSV/JSON/image assets later,
but this package can be run by itself from a terminal with only a Bilibili XML
danmaku file.

The tool turns one XML file into layer-3 per-comment feature tables,
burst-event tables, evidence-based topic and emotion labels, charts, an HTML
preview, an optional Excel workbook, and a Markdown report. A pre-filtered JSON
file is not required.

## Quick Start

Run the integrated layer 1-3 pipeline:

```powershell
python .\danmaku-burst-map\python\run_layer123_pipeline.py `
  --input "path\to\danmaku.xml" `
  --output ".\outputs\layer123_example" `
  --sport-type football
```

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

- `filtered_danmaku.csv` and `filtered_danmaku.json`: layer-2 rule pre-filter
  labels (`KEEP_ANALYSIS`, `KEEP_ATMOSPHERE`, `DOWNRANK_META`,
  `REMOVE_NOISE`) with matched terms, reason, confidence, and display
  suppression flag. Noise is flagged rather than physically deleted.
- `normalized_danmaku.csv` and `normalized_danmaku.json`: parsed XML comments
  with cleaned text, display metadata, duplicate/spam flags, and evidence
  scores.
- `density_5s.csv` and `density_10s.csv`: raw XML danmaku density by time
  window.
- `feature_danmaku.csv` and `feature_danmaku.json`: layer-3 per-comment
  features for later scoring, agent routing, burst interpretation, and VR
  mapping. This layer records evidence only; it does not assign final filter,
  emotion, content, or VR utility labels.
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
- `layer123_manifest.json`: integrated pipeline manifest with schema versions,
  row counts, input path, branch name, commit hash, and warning counts.

Chart titles, axes, legends, and table headers are English. Original danmaku
comments are preserved in their source language.

## Method Overview

The analyzer follows an evidence-first pipeline:

1. Parse every Bilibili XML `<d>` node and read the timestamp from the `p`
   attribute.
2. Normalize comment text while preserving the original raw danmaku string.
3. Extract layer-3 features such as text structure ratios, lexicon hits,
   duplicate counts, window density, density z-score, and burst proximity.
4. Compute density from all XML comments, not from a filtered subset.
5. Detect replay-aware bursts with robust statistics.
6. For each burst, collect a context window around the burst event.
7. Extract terms, repeated phrases, representative comments, emotion cues, and
   content cues from the real comments in that window.
8. Export tables and charts using those extracted evidence values.

## Layer-3 Feature Extraction

The first implementation of the Sports Danmaku Intelligence Pipeline's third
layer is rule-based and local-only. It does not call an LLM or train a model.
Each feature row includes:

- base fields: `danmaku_id`, `time_sec`, `text_raw`, `text_norm`
- text structure: `length`, `char_repeat_ratio`, `punctuation_ratio`,
  `symbol_ratio`, `digit_ratio`, `emoji_or_emoticon_count`
- lexicon evidence: `has_sports_terms`, `has_emotion_terms`,
  `has_meta_noise_terms`, `has_toxic_terms`, `has_meme_terms`,
  `matched_terms`
- context statistics: `same_text_global_count`, `duplicate_count_in_5s`,
  `time_window_density`, `density_z_score`, `near_burst_peak`
- rule hints: `is_short_reaction`, `is_symbol_only`,
  `is_repetition_pattern`, `feature_quality_flags`

Editable lexicons live in `resources/lexicons/`. They are evidence sources for
later layers, not deletion rules.

## Layer-1 and Layer-2 Integration

Layer 1 lives in `python/burst_map/normalization.py`. It normalizes Bilibili
XML into stable records with `danmaku_id`, `time_sec`, `text_raw`,
`text_norm`, display metadata, source metadata, and stats. It tolerates bad
rows by collecting warnings instead of stopping the whole batch.

Layer 2 lives in `python/burst_map/prefilter.py`. It applies transparent,
low-cost rules before layer 3. Each record keeps all normalized fields and adds
`filter_label`, `filter_reason`, `filter_matched_terms`, `filter_confidence`,
and `removed_from_main_display`.

## Implementation Rationale

This layer implements feature extraction instead of direct classification
because sports danmaku has two useful signals at the same time:

- semantic signal: comments such as `这球越位了吧` or `门将站位太靠前` carry
  content that later layers can analyze or turn into TTS.
- atmosphere signal: comments such as `啊啊啊`, `哈哈哈`, `666666`, or
  repeated punctuation may be weak as standalone text but strong as crowd
  emotion, burst, or VR atmosphere evidence.

For that reason, this layer does not delete or label comments as final truth.
It records measurable evidence for later scoring:

- text structure features describe whether a comment is short, repetitive,
  punctuation-heavy, symbol-heavy, numeric, or emoji-like.
- lexicon features mark whether a comment contains sports, emotional,
  meta-viewing, toxic/risk, or meme/slang evidence.
- temporal features describe how the comment behaves in the video timeline:
  local duplicates, global repetition, window density, robust density z-score,
  and whether it is close to a detected burst peak.
- quality flags explain risk or uncertainty, for example `spam_like`,
  `high_character_repetition`, `meta_without_sports_context`, or
  `toxicity_risk`.

The design keeps expensive or ambiguous decisions out of layer 3. For example,
`哈哈哈` is only marked as a short emotional reaction and repeated-character
pattern here. A later scoring or Agent layer can decide whether that laughter
means joy, sarcasm, mockery, or low-value repetition by looking at local
context.

## References Used

The feature design is based on the project framework document and these
research/project references:

- DanmuA11y: Making Time-Synced On-Screen Video Comments Accessible to Blind
  and Low Vision Users via Multi-Viewer Audio Discussions. This supports the
  idea that danmaku can be transformed into organized audio/social discussion,
  so layer 3 preserves evidence for later TTS and crowd-audio decisions.
- Visual-Textual Emotion Analysis with Deep Coupled Video and Danmu Neural
  Networks. This supports treating danmu as time-synchronized emotional
  evidence rather than ordinary offline comments.
- DanModCap: Designing a Danmaku Moderation Tool for Video-Sharing Platforms
  that Leverages Impact Captions with Large Language Models. This supports the
  separation between cheap local evidence extraction and later LLM-assisted
  interpretation.
- Bilibili danmaku sentiment/AI-analysis GitHub examples, including
  `moshy2077/bilibili-danmaku-sentiment-analysis` and
  `Liu-Bot24/bilibili-danmaku`. These informed the local-first approach:
  parse/download/analyze cheaply first, then reserve heavier AI steps for
  higher-level interpretation.
- Existing repository work on XML normalization, duplicate handling, density
  curves, burst detection, and evidence-first burst characterization in
  `Huwuw/danmaku-burst-map-generalization`.

## Trust and Confidence

The result is credible because every output row is traceable back to visible
source evidence. Layer 3 does not claim hidden semantic certainty; it exposes
the evidence that later layers can inspect.

Confidence appears in two places:

- Per-comment feature confidence is represented as evidence strength rather
  than a single opaque probability. Important fields are `matched_terms`,
  `same_text_global_count`, `duplicate_count_in_5s`, `time_window_density`,
  `density_z_score`, `near_burst_peak`, and `feature_quality_flags`. A later
  layer can combine these into `analysis_value_score`, `atmosphere_value_score`,
  or `tts_value_score`.
- Burst-level confidence is already explicit in the existing outputs:
  `topic_confidence`, `emotion_confidence`, `content_confidence`, and
  normalized `emotion_scores` in `burst_events.json` and
  `burst_characterization.csv`.

The checks that make the first version trustworthy are:

- deterministic rules: the same XML produces the same features.
- transparent lexicons: evidence terms are editable in `resources/lexicons/`.
- robust timeline statistics: density z-score uses a median/MAD-style robust
  baseline, reducing sensitivity to a few extreme windows.
- no black-box deletion: noisy, repeated, toxic, or meta comments are flagged
  instead of silently removed.
- local smoke tests: the included `examples/layer3_feature_sample.xml` covers
  short reactions, repetition, referee/rule discussion, meta viewing behavior,
  and meme evidence; a real esports XML sample was also tested locally with
  matching normalized and feature row counts.

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
  resources/               Stopwords, dictionaries, lexicons, and taxonomy
```

## Git Workflow Used For This Branch

The burst-map baseline package previously lived on:

```text
Huwuw/danmaku-burst-map-generalization
```

The layer-3 feature extraction work should be developed on a separate branch
based on `origin/Danmaku-analysis-agent`, then merged only after local tests
pass. Keep commits focused on `danmaku-burst-map/` unless a later Unity runtime
task explicitly needs new consuming code.

## More Details

See:

- `docs/burst_map_usage.md`
- `docs/methodology.md`
