# Danmaku Burst Map Methodology

## XML-only normalization

The analyzer parses Bilibili XML `<d>` nodes and standardizes each comment into
time, display metadata, raw text, cleaned text, duplicate/spam indicators,
priority score, and evidence weight.

Raw XML timestamps are used for density and burst detection. Cleaned and
weighted comments are used for topic, emotion, and content evidence.

## Replay-aware burst detection

Recorded Bilibili videos often contain opening check-ins, repost comments, or
viewer behavior spikes that are not live match events. To avoid overfitting to
these artifacts, the baseline excludes an opening guard period:

```text
opening_guard = max(60 seconds, video_duration * 2%)
```

The baseline uses robust statistics:

```text
candidate_threshold = max(P90, median + 3 * robust_sigma)
strong_threshold = max(P95, median + 6 * robust_sigma)
```

Adjacent hot windows are merged into burst events.

## Evidence-first labels

Topic, emotion, and content labels are generated from source comments, not from
invented event knowledge. Each label includes terms, representative comments,
and a confidence score.

Default burst kinds:

- `gameplay_peak`
- `result_or_celebration_peak`
- `viewer_behavior_peak`
- `chat_meta_peak`
- `opening_artifact_peak`
- `uncategorized_peak`

## Optional extensions

The Python implementation can be extended with `jieba`, TF-IDF clustering,
BERTopic, sentence-transformers, or an LLM/agent labeler. LLM labels must be
limited to extracted evidence terms and representative comments.
