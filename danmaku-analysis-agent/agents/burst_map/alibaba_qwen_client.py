"""Alibaba Cloud Qwen client for the layer-4/5 danmaku Agent."""

from __future__ import annotations

import json
import os
import time
import urllib.error
import urllib.request
from typing import Any, Sequence

from .llm_schema import normalize_burst_summary, normalize_comment_score


DEFAULT_MODEL = "qwen-plus"
DEFAULT_BASE_URL = "https://dashscope.aliyuncs.com/compatible-mode/v1"


class QwenClient:
    """Small standard-library client for DashScope's OpenAI-compatible API."""

    def __init__(
        self,
        model: str = DEFAULT_MODEL,
        api_key: str | None = None,
        base_url: str | None = None,
        workspace_id: str | None = None,
        region: str = "cn-beijing",
        timeout_seconds: int = 60,
        temperature: float = 0.2,
        max_retries: int = 1,
        dry_run: bool = False,
        mock: bool = False,
    ) -> None:
        self.model = model
        self.api_key = api_key or os.environ.get("DASHSCOPE_API_KEY", "")
        self.base_url = resolve_base_url(base_url, workspace_id, region)
        self.timeout_seconds = timeout_seconds
        self.temperature = temperature
        self.max_retries = max_retries
        self.dry_run = dry_run
        self.mock = mock
        self.usage = {
            "prompt_tokens": 0,
            "completion_tokens": 0,
            "total_tokens": 0,
            "request_count": 0,
            "mock_request_count": 0,
        }

    def score_comments(self, candidates: Sequence[dict], batch_size: int = 20) -> tuple[list[dict], list[dict]]:
        scored: list[dict] = []
        errors: list[dict] = []
        candidates = list(candidates)
        for start in range(0, len(candidates), batch_size):
            batch = candidates[start : start + batch_size]
            try:
                if self.mock or self.dry_run:
                    scored.extend(mock_score_comments(batch, self.model))
                    self.usage["mock_request_count"] += 1
                    continue
                response = self._chat_json(comment_scoring_messages(batch))
                usage_total = self._record_usage(response)
                raw_comments = response.get("comments", [])
                raw_by_id = {str(item.get("danmaku_id", "")): item for item in raw_comments if isinstance(item, dict)}
                for candidate in batch:
                    raw = raw_by_id.get(str(candidate.get("danmaku_id")), {})
                    scored.append(normalize_comment_score(raw, candidate, self.model, usage_total))
            except Exception as exc:
                errors.append({"task": "score_comments", "batch_start": start, "error": str(exc)})
                for candidate in batch:
                    scored.append(normalize_comment_score(mock_comment_score(candidate, fallback=True), candidate, "fallback-local", 0))
        return scored, errors

    def summarize_bursts(self, bursts: Sequence[dict], candidates: Sequence[dict], scored_comments: Sequence[dict]) -> tuple[list[dict], list[dict]]:
        summaries: list[dict] = []
        errors: list[dict] = []
        candidates_by_burst = group_by_burst(candidates)
        scored_by_burst = group_by_burst(scored_comments)
        for burst in bursts:
            burst_id = str(burst.get("burst_id", ""))
            burst_candidates = candidates_by_burst.get(burst_id, [])
            burst_scores = scored_by_burst.get(burst_id, [])
            try:
                if self.mock or self.dry_run:
                    summaries.append(mock_burst_summary(burst, burst_candidates, burst_scores, self.model))
                    self.usage["mock_request_count"] += 1
                    continue
                response = self._chat_json(burst_summary_messages(burst, burst_candidates, burst_scores))
                usage_total = self._record_usage(response)
                summaries.append(normalize_burst_summary(response, burst, self.model, usage_total))
            except Exception as exc:
                errors.append({"task": "summarize_burst", "burst_id": burst_id, "error": str(exc)})
                summaries.append(mock_burst_summary(burst, burst_candidates, burst_scores, "fallback-local"))
        return summaries, errors

    def _chat_json(self, messages: list[dict]) -> dict:
        if not self.api_key:
            raise RuntimeError("DASHSCOPE_API_KEY is not set. Use --mock for local tests without API calls.")
        payload = {
            "model": self.model,
            "messages": messages,
            "temperature": self.temperature,
            "response_format": {"type": "json_object"},
        }
        data = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        request = urllib.request.Request(
            f"{self.base_url.rstrip('/')}/chat/completions",
            data=data,
            method="POST",
            headers={
                "Authorization": f"Bearer {self.api_key}",
                "Content-Type": "application/json",
            },
        )
        last_error: Exception | None = None
        for attempt in range(self.max_retries + 1):
            try:
                with urllib.request.urlopen(request, timeout=self.timeout_seconds) as response:
                    raw = json.loads(response.read().decode("utf-8"))
                self.usage["request_count"] += 1
                content = raw.get("choices", [{}])[0].get("message", {}).get("content", "{}")
                parsed = json.loads(content)
                if isinstance(parsed, dict):
                    parsed["_usage"] = raw.get("usage", {})
                    parsed["_request_id"] = raw.get("id", "")
                    return parsed
                raise ValueError("Model returned JSON, but not a JSON object.")
            except (urllib.error.URLError, TimeoutError, json.JSONDecodeError, ValueError) as exc:
                last_error = exc
                if attempt < self.max_retries:
                    time.sleep(1.5 * (attempt + 1))
        raise RuntimeError(f"Qwen request failed: {last_error}")

    def _record_usage(self, response: dict) -> int:
        usage = response.get("_usage", {}) if isinstance(response, dict) else {}
        prompt = int(usage.get("prompt_tokens", 0) or 0)
        completion = int(usage.get("completion_tokens", 0) or 0)
        total = int(usage.get("total_tokens", prompt + completion) or 0)
        self.usage["prompt_tokens"] += prompt
        self.usage["completion_tokens"] += completion
        self.usage["total_tokens"] += total
        return total


def resolve_base_url(base_url: str | None, workspace_id: str | None, region: str) -> str:
    explicit = base_url or os.environ.get("DASHSCOPE_BASE_URL") or os.environ.get("ALIBABA_QWEN_BASE_URL")
    if explicit:
        return explicit.rstrip("/")
    workspace = workspace_id or os.environ.get("DASHSCOPE_WORKSPACE_ID", "")
    if workspace:
        return f"https://{workspace}.{region}.maas.aliyuncs.com/compatible-mode/v1"
    return DEFAULT_BASE_URL


def comment_scoring_messages(candidates: Sequence[dict]) -> list[dict]:
    compact = [
        {
            "danmaku_id": item.get("danmaku_id", ""),
            "time_sec": item.get("time_sec", 0.0),
            "text_norm": item.get("text_norm", ""),
            "filter_label": item.get("filter_label", ""),
            "source_burst_id": item.get("source_burst_id", ""),
            "candidate_reason": item.get("candidate_reason", ""),
            "feature_evidence": item.get("feature_evidence", {}),
        }
        for item in candidates
    ]
    return [
        {
            "role": "system",
            "content": (
                "You are a sports danmaku analysis agent. Score each comment for research and VR use. "
                "Return only a JSON object with key comments. Scores must be numbers from 0 to 1."
            ),
        },
        {
            "role": "user",
            "content": (
                "For each danmaku, output danmaku_id, analysis_value_score, atmosphere_value_score, "
                "tts_value_score, vr_display_value_score, emotion_label, content_label, confidence, and reason. "
                "Use these emotion labels: excitement, joy, tension, anger, sarcasm, confusion, disappointment, neutral, mixed. "
                "Use these content labels: gameplay_reaction, tactical_analysis, rule_or_referee_discussion, "
                "player_or_team_evaluation, celebration, viewer_meta, meme_or_slang, noise, unclear.\n\n"
                f"Danmaku candidates:\n{json.dumps(compact, ensure_ascii=False)}"
            ),
        },
    ]


def burst_summary_messages(burst: dict, candidates: Sequence[dict], scored_comments: Sequence[dict]) -> list[dict]:
    compact_candidates = [
        {
            "danmaku_id": item.get("danmaku_id", ""),
            "time_sec": item.get("time_sec", 0.0),
            "text_norm": item.get("text_norm", ""),
            "candidate_reason": item.get("candidate_reason", ""),
        }
        for item in candidates[:80]
    ]
    compact_scores = [
        {
            "danmaku_id": item.get("danmaku_id", ""),
            "analysis_value_score": item.get("analysis_value_score", 0.0),
            "atmosphere_value_score": item.get("atmosphere_value_score", 0.0),
            "tts_value_score": item.get("tts_value_score", 0.0),
            "vr_display_value_score": item.get("vr_display_value_score", 0.0),
            "emotion_label": item.get("emotion_label", ""),
            "content_label": item.get("content_label", ""),
        }
        for item in scored_comments[:80]
    ]
    return [
        {
            "role": "system",
            "content": (
                "You are a sports danmaku burst interpretation agent. Explain why a burst happened and how it can map to VR. "
                "Return only one JSON object."
            ),
        },
        {
            "role": "user",
            "content": (
                "Output burst_id, burst_title, start_sec, end_sec, peak_sec, dominant_emotion, content_topic, "
                "representative_comments, vr_scene_suggestion, confidence, and reason.\n\n"
                f"Layer-3 burst evidence:\n{json.dumps(burst, ensure_ascii=False)}\n\n"
                f"Selected danmaku:\n{json.dumps(compact_candidates, ensure_ascii=False)}\n\n"
                f"Per-comment scores:\n{json.dumps(compact_scores, ensure_ascii=False)}"
            ),
        },
    ]


def mock_score_comments(candidates: Sequence[dict], model: str) -> list[dict]:
    return [normalize_comment_score(mock_comment_score(candidate), candidate, f"{model}-mock", 0) for candidate in candidates]


def mock_comment_score(candidate: dict, fallback: bool = False) -> dict:
    evidence = candidate.get("feature_evidence", {})
    label = candidate.get("filter_label", "")
    text = str(candidate.get("text_norm", ""))
    analysis = 0.25
    atmosphere = 0.25
    tts = 0.3
    vr = 0.25
    if label == "KEEP_ANALYSIS":
        analysis += 0.35
        tts += 0.25
    if label == "KEEP_ATMOSPHERE":
        atmosphere += 0.35
        vr += 0.25
    if evidence.get("has_sports_terms"):
        analysis += 0.25
        tts += 0.15
    if evidence.get("has_emotion_terms"):
        atmosphere += 0.25
        vr += 0.2
    if evidence.get("near_burst_peak"):
        vr += 0.25
        atmosphere += 0.1
    if evidence.get("duplicate_count_in_5s", 0) and int(evidence.get("duplicate_count_in_5s", 0)) >= 4:
        tts -= 0.25
        analysis -= 0.15
    if label in {"DOWNRANK_META", "REMOVE_NOISE"}:
        analysis -= 0.2
        tts -= 0.2
    content = "gameplay_reaction" if evidence.get("has_sports_terms") else "meme_or_slang" if evidence.get("has_meme_terms") else "viewer_meta" if label == "DOWNRANK_META" else "unclear"
    emotion = "excitement" if evidence.get("has_emotion_terms") or "啊" in text or "牛" in text else "neutral"
    return {
        "danmaku_id": candidate.get("danmaku_id", ""),
        "time_sec": candidate.get("time_sec", 0.0),
        "text_norm": candidate.get("text_norm", ""),
        "source_burst_id": candidate.get("source_burst_id", ""),
        "analysis_value_score": analysis,
        "atmosphere_value_score": atmosphere,
        "tts_value_score": tts,
        "vr_display_value_score": vr,
        "emotion_label": emotion,
        "content_label": content,
        "confidence": 0.45 if fallback else 0.62,
        "reason": "Local mock score from layer-2 label, layer-3 evidence, and burst proximity.",
    }


def mock_burst_summary(burst: dict, candidates: Sequence[dict], scored_comments: Sequence[dict], model: str) -> dict:
    representative = [str(item.get("text_norm", "")) for item in candidates[:6] if item.get("text_norm")]
    best_emotion = majority_label(scored_comments, "emotion_label", "mixed")
    best_content = majority_label(scored_comments, "content_label", "unclear")
    return normalize_burst_summary(
        {
            "burst_id": burst.get("burst_id", ""),
            "burst_title": burst.get("topic_label") or f"{burst.get('burst_id', '')} danmaku burst",
            "start_sec": burst.get("start_seconds", 0.0),
            "end_sec": burst.get("end_seconds", 0.0),
            "peak_sec": burst.get("peak_seconds", 0.0),
            "dominant_emotion": best_emotion,
            "content_topic": best_content,
            "representative_comments": representative,
            "vr_scene_suggestion": "Use burst intensity to drive crowd atmosphere; show high-analysis comments near the field or scoreboard.",
            "confidence": 0.6,
            "reason": "Local mock summary generated from selected candidates and layer-3 burst evidence.",
        },
        burst,
        f"{model}-mock" if not model.endswith("mock") else model,
        0,
    )


def majority_label(rows: Sequence[dict], key: str, default: str) -> str:
    counts: dict[str, float] = {}
    for row in rows:
        label = str(row.get(key, ""))
        if not label:
            continue
        weight = float(row.get("confidence", 0.5) or 0.5)
        counts[label] = counts.get(label, 0.0) + weight
    if not counts:
        return default
    return max(counts.items(), key=lambda item: item[1])[0]


def group_by_burst(rows: Sequence[dict]) -> dict[str, list[dict]]:
    grouped: dict[str, list[dict]] = {}
    for row in rows:
        burst_id = str(row.get("source_burst_id") or row.get("burst_id") or "")
        if burst_id:
            grouped.setdefault(burst_id, []).append(dict(row))
    return grouped
