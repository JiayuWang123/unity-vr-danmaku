# XML-only Danmaku Burst Map

This tool converts a Bilibili danmaku XML file into burst-map deliverables for
research and presentation use. It does not require a pre-filtered JSON file.

The workflow:

1. Parse XML comments.
2. Normalize and score comment text.
3. Detect replay-aware burst windows.
4. Extract source-text evidence.
5. Generate topic, emotion, and content labels.
6. Export charts, CSV/JSON tables, HTML, XLSX, and Markdown report.

See `docs/burst_map_usage.md` for commands and `docs/methodology.md` for the
analysis method.
