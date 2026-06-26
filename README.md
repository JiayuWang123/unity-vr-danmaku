# Danmaku Analysis Agent

This branch contains the first three layers of the Sports Danmaku Intelligence
Pipeline:

```text
Bilibili XML
  -> Layer 1 normalized_danmaku.json
  -> Layer 2 filtered_danmaku.json
  -> Layer 3 feature_danmaku.json + burst_events.json
  -> Layer 4 scoring/classification
```

The active research pipeline is Python-first. Unity runtime scripts remain in
`Assets/`, but the structured analysis outputs are generated from terminal
commands and can later be consumed by Unity, audio, TTS, or Agent layers.

## Run Layers 1-3

```powershell
python .\danmaku-burst-map\python\run_layer123_pipeline.py `
  --input "C:\path\to\danmaku.xml" `
  --output ".\outputs\layer123_example" `
  --video-id BVxxxx `
  --sport-type football
```

The output directory contains:

- `normalized_danmaku.json/csv`: Layer 1 normalized records.
- `filtered_danmaku.json/csv`: Layer 2 labels plus evidence.
- `feature_danmaku.json/csv`: Layer 3 per-comment features.
- `density_5s.csv`, `density_10s.csv`, `burst_events.json`: timeline evidence.
- `normalization_stats.json`, `filter_stats.json`, `layer123_manifest.json`.

`REMOVE_NOISE` records are not deleted by default. They are flagged so later
research steps can audit what was downranked or suppressed.

## Layer 1 Only

## Usage

```powershell
python danmaku_normalizer.py "C:\path\to\danmaku.xml" --sport-type football -o "danmaku.json"
```

Optional video ID:

```powershell
python danmaku_normalizer.py "C:\path\to\danmaku.xml" --video-id BVxxxx --sport-type football -o "danmaku.json"
```

Use `--jsonl` to export one JSON object per line:

```powershell
python danmaku_normalizer.py "C:\path\to\danmaku.xml" --sport-type football --jsonl -o "danmaku.jsonl"
```

## Output Fields

- `danmaku_id`: danmaku unique ID
- `video_id`: video ID or chat ID
- `sport_type`: sport type, such as `football` or `esports`
- `time_sec`: danmaku timestamp in seconds
- `text_raw`: original danmaku text
- `text_norm`: normalized danmaku text
- `mode`: danmaku mode code
- `mode_name`: danmaku mode name
- `font_size`: font size
- `color_hex`: danmaku color
- `user_hash`: anonymous user hash
- `source`: data source
- `source_file`: source filename

## Layer 2 Only

Classify one text:

```powershell
python second_layer_filter.py --text "哈哈哈牛逼"
```

Filter a normalized JSON file:

```powershell
python second_layer_filter.py --input normalized_danmaku.json --output filtered_danmaku.json
```

The old `SecondLayerFilter.cs` file is retained as a legacy reference only.
The active layer-2 implementation is `second_layer_filter.py` and
`danmaku-burst-map/python/burst_map/prefilter.py`.
