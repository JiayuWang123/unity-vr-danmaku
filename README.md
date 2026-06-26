# Danmaku Analysis Agent

Read Bilibili danmaku XML files and export normalized JSON records.

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
