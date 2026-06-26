# Unity VR Danmaku

This repository contains a Unity VR danmaku prototype plus a Python-first
sports danmaku analysis agent.

## Repository Structure

```text
unity-vr-danmaku/
├── Assets/                    # Original Unity VR project assets and scripts
├── Packages/                  # Unity package manifest
├── ProjectSettings/           # Unity project settings
├── danmaku-analysis-agent/    # Python sports danmaku analysis system
│   ├── README.md
│   ├── requirements.txt
│   ├── configs/
│   ├── data/
│   ├── scripts/
│   ├── agents/
│   ├── resources/
│   ├── outputs/
│   └── docs/
└── docs/                      # Repository-level notes
```

The active analysis pipeline is under `danmaku-analysis-agent/`. The old C#
second-layer prototype has been removed; layer 1-5 research code is now Python.
Unity remains the VR runtime target; the analysis agent exports structured JSON
that can later be consumed by Unity, audio, TTS, or visualization scripts.

## Current Research Work

This branch now integrates the group's first two Python layers with the later
feature-extraction and Qwen Agent work:

- Layer 1, built on the group normalizer, parses Bilibili XML or normalized
  JSON into stable records with `danmaku_id`, `time_sec`, `text_raw`,
  `text_norm`, display metadata, source metadata, warnings, and stats.
- Layer 2, built on the group Python pre-filter, assigns transparent rule
  labels: `KEEP_ANALYSIS`, `KEEP_ATMOSPHERE`, `DOWNRANK_META`, and
  `REMOVE_NOISE`. Records are flagged rather than physically deleted.
- Layer 3 extracts explainable features for every danmaku: text structure,
  lexicon hits, duplicate statistics, density z-score, burst proximity, and
  quality flags.
- Layer 4/5 skips small-model training for the MVP and uses Alibaba Cloud Qwen
  only on routed candidates. It outputs per-comment scores, burst summaries,
  VR mapping events, and TTS candidates.

The design keeps the responsibilities separated: layers 1-3 produce local,
deterministic evidence; layer 4/5 interprets only bounded high-value or
ambiguous candidates.

## Analysis Pipeline

```text
Bilibili XML / normalized JSON
  -> Layer 1 normalization
  -> Layer 2 Python rule pre-filter
  -> Layer 3 feature extraction and burst detection
  -> Layer 4/5 Qwen Agent scoring, burst interpretation, VR/TTS planning
```

Run layers 1-3:

```powershell
python .\danmaku-analysis-agent\scripts\run_layer123_pipeline.py `
  --input "path\to\danmaku.xml" `
  --output ".\outputs\layer123_example" `
  --sport-type football
```

Layer 1-3 output files include:

- `normalized_danmaku.json/csv`
- `filtered_danmaku.json/csv`
- `feature_danmaku.json/csv`
- `density_5s.csv`, `density_10s.csv`
- `burst_events.json`
- `layer123_manifest.json`

Run layer 4/5 locally without API calls:

```powershell
python .\danmaku-analysis-agent\scripts\run_layer45_agent.py `
  --input-dir ".\outputs\layer123_example" `
  --output ".\outputs\layer45_example" `
  --mock
```

Layer 4/5 output files include:

- `llm_candidates.json/csv`
- `llm_scored_danmaku.json/csv`
- `burst_agent_summaries.json/csv`
- `vr_mapping_events.json/csv`
- `tts_candidates.json/csv`
- `layer45_manifest.json`

For live Qwen calls, configure credentials locally through environment
variables or command-line options. Do not commit `.env`, API keys, endpoint
secrets, or generated outputs.

The default layer 4/5 candidate budget is set for a roughly 10-minute sports
clip with many comments: 60 comments per burst, 250 global candidates, and 800
total candidates. These are caps, so smaller clips use fewer model calls.

## Why The Results Are Auditable

- Every output keeps `danmaku_id`, so records can be traced back through all
  layers.
- Layer 2 records the matched terms, rule reason, and rule confidence.
- Layer 3 records measurable evidence instead of hidden classifications.
- Layer 4/5 records candidate routing reasons, model mode, token usage, error
  counts, and schema-validated scores in `layer45_manifest.json`.
- Generated outputs and secrets are ignored by `.gitignore`; only code,
  configs, docs, lexicons, and small samples should be committed.

## Documentation

- `danmaku-analysis-agent/README.md`: detailed pipeline usage.
- `danmaku-analysis-agent/docs/layer45_qwen_agent.md`: Qwen Agent method,
  schemas, confidence logic, and references.
- `danmaku-analysis-agent/docs/methodology.md`: burst-map methodology.
