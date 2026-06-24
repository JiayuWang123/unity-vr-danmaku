# Danmaku Burst Map Current Version Snapshot

This snapshot preserves the MATLAB single-match analysis version before the
generalized XML-only refactor.

- Sample: 2025 Worlds Finals, KT vs T1, Game 5 Bilibili danmaku.
- Runtime: MATLAB R2026a was used during generation.
- Script: `Scipt-Codes/analyze_danmaku_burst_map.m`
- Outputs: `outputs/danmaku_burst_map/`
- Purpose: baseline version for comparison and rollback before the generalized
  Python/MATLAB project implementation.

Run from the project root with:

```matlab
run("Scipt-Codes/analyze_danmaku_burst_map.m")
```

The preserved outputs are the generated charts, CSV files, JSON, and markdown
report from the single-match workflow.
