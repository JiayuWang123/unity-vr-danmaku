# Examples

Large XML and video files should not be committed to the repository.

To test locally, place an XML file anywhere on disk and run:

```bash
python danmaku-burst-map/python/run_burst_map.py ^
  --input C:\path\to\danmaku.xml ^
  --output outputs\danmaku_burst_map_generalized
```

For regression checks, use small XML snippets only.

