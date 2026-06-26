# Layer 4/5 Qwen Agent Methodology

## Role In The Pipeline

Layer 4/5 is the first large-model-assisted layer of the Sports Danmaku
Intelligence Pipeline:

```text
Layer 1 normalization
  -> Layer 2 rule pre-filter
  -> Layer 3 feature extraction and burst detection
  -> Layer 4/5 Qwen Agent scoring and interpretation
  -> Unity VR / TTS / audio mapping
```

The MVP intentionally skips small-model training. A small classifier can still
be added later, but it is not necessary for the first research prototype
because layer 2 and layer 3 already give deterministic, low-cost evidence. Qwen
is reserved for ambiguous, high-value, and burst-related cases where local rules
cannot reliably judge sarcasm, context, or VR usefulness.

## Why Not Send Every Danmaku To Qwen

Sending every comment to a large model is simple but weak for research and
engineering:

- Cost grows with full comment count instead of event importance.
- Duplicate reactions such as `啊啊啊`, `哈哈哈`, and `666666` can waste tokens.
- LLM output becomes harder to audit if there is no local routing reason.
- Burst interpretation needs context windows, not isolated comments only.

The router therefore creates `llm_candidates.json`. Each candidate records:

- `candidate_reason`: why it was selected.
- `routing_priority`: local priority score.
- `source_burst_id`: burst window that caused routing, if any.
- `feature_evidence`: compact layer-3 evidence passed into the prompt.

For a roughly 10-minute World Cup clip, the current defaults are deliberately
larger than the initial smoke test:

```text
--max-comments-per-burst 60
--max-global-comments 250
--max-total-candidates 800
```

These are caps, not guaranteed calls. If the clip has fewer high-value
candidates, the actual number will be smaller.

## Alibaba Cloud Configuration

The live client uses Alibaba Cloud Model Studio / DashScope's
OpenAI-compatible chat completions API.

Required:

```powershell
$env:DASHSCOPE_API_KEY="your-api-key"
```

Optional:

```powershell
$env:DASHSCOPE_BASE_URL="https://dashscope.aliyuncs.com/compatible-mode/v1"
$env:DASHSCOPE_WORKSPACE_ID="your-workspace-id"
```

If `DASHSCOPE_BASE_URL` is not set and `DASHSCOPE_WORKSPACE_ID` is provided,
the CLI builds a workspace URL using the selected region:

```text
https://{WorkspaceId}.{region}.maas.aliyuncs.com/compatible-mode/v1
```

The default model is `qwen-plus`. Use `qwen-long` later if a test needs much
longer context windows.

## Prompt And Schema

The Agent has two live tasks:

1. Per-comment scoring. Qwen returns:
   - `analysis_value_score`
   - `atmosphere_value_score`
   - `tts_value_score`
   - `vr_display_value_score`
   - `emotion_label`
   - `content_label`
   - `confidence`
   - `reason`

2. Burst interpretation. Qwen returns:
   - `burst_title`
   - `dominant_emotion`
   - `content_topic`
   - `representative_comments`
   - `vr_scene_suggestion`
   - `confidence`
   - `reason`

The client requests JSON output with `response_format={"type":"json_object"}`.
The local schema layer then validates every response:

- numeric scores are clamped to `0.0-1.0`;
- unknown enum values fall back to safe defaults;
- missing fields are filled from the candidate or burst evidence;
- bad requests are written to `llm_errors.json` and do not stop the whole run.

## Outputs

Layer 4/5 writes:

- `llm_candidates.json/csv`
- `llm_scored_danmaku.json/csv`
- `burst_agent_summaries.json/csv`
- `vr_mapping_events.json/csv`
- `tts_candidates.json/csv`
- `layer45_manifest.json`
- `llm_errors.json` only when failures occur

`vr_mapping_events.json` is the first Unity-facing draft. It contains event
time range, peak time, dominant emotion, intensity, display mode, anchor hint,
candidate TTS lines, representative danmaku IDs, confidence, and evidence.

## Trust And Confidence

The output is credible for a first research prototype because Qwen is not
allowed to invent the whole analysis from raw text alone. It receives local
evidence from layers 1-3, and each output remains linked to `danmaku_id` and
`burst_id`.

Confidence comes from four places:

- Layer 2 rule confidence, such as `KEEP_ANALYSIS` or `KEEP_ATMOSPHERE`.
- Layer 3 feature evidence, such as sports terms, emotion terms, duplicates,
  density z-score, and burst proximity.
- Qwen's own `confidence` and `reason`.
- Human audit samples, which should be added later as a small validation set.

This means layer 4/5 is not final truth. It is a structured, explainable
interpretation layer that prepares the project for fourth-layer scoring
research and fifth-layer Agent behavior.

## References

Project references:

- `VR体育弹幕智能分析Agent框架.md`
- GitHub branch:
  `https://github.com/JiayuWang123/unity-vr-danmaku/tree/Danmaku-analysis-agent`
- Local layer 1-3 integration:
  `danmaku-burst-map/python/run_layer123_pipeline.py`

Alibaba / Qwen references:

- Alibaba Cloud Model Studio Qwen API reference:
  `https://help.aliyun.com/zh/model-studio/qwen-api-reference/`
- Alibaba Cloud OpenAI-compatible Chat Completions:
  `https://help.aliyun.com/zh/model-studio/qwen-api-via-openai-chat-completions`
- Qwen GitHub:
  `https://github.com/QwenLM/Qwen`

Research references:

- DanmuA11y, arXiv 2501.15711. Supports preserving danmaku as social/audio
  evidence rather than deleting short reactions.
- Visual-Textual Emotion Analysis with Deep Coupled Video and Danmu Neural
  Networks, arXiv 1811.07485. Supports treating danmu as time-synchronized
  emotion evidence.
- DanModCap, arXiv 2408.02574. Supports using LLMs for contextual danmaku
  interpretation and moderation tooling.
- Exploring Danmaku Content Moderation, arXiv 2411.04529. Supports dynamic,
  context-aware judgment instead of fixed deletion rules.

## Local Test Commands

Run layers 1-3:

```powershell
python .\danmaku-burst-map\python\run_layer123_pipeline.py `
  --input .\danmaku-burst-map\examples\layer3_feature_sample.xml `
  --output .\outputs\layer123_sample `
  --sport-type football
```

Run layer 4/5 mock:

```powershell
python .\danmaku-burst-map\python\run_layer45_agent.py `
  --input-dir .\outputs\layer123_sample `
  --output .\outputs\layer45_sample `
  --mock
```

Run layer 4/5 live with a bounded test:

```powershell
python .\danmaku-burst-map\python\run_layer45_agent.py `
  --input-dir .\outputs\layer123_worldcup `
  --output .\outputs\layer45_worldcup `
  --model qwen-plus `
  --max-comments-per-burst 60 `
  --max-global-comments 250 `
  --max-total-candidates 800
```
