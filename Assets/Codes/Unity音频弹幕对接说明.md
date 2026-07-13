# Unity 音频弹幕对接说明

> **版本：** v0.1  
> **适用场景：** `pop_upScene` — 2022 世界杯决赛类体育 VR 弹幕  
> **维护：** 音频 / 氛围组（Unity）  
> **对接对象：** 弹幕分析组、文字弹幕组  

本文档说明：**分析组输出的 JSON 如何与 Unity 音频系统对接**，以及双方需要补齐的字段与流程。

---

## 1. 总体原则

### 1.1 不要按原始时间戳全量播放

一场 90 分钟比赛可能有 **数千条** 弹幕。若按 `time_sec` 原样触发语音，会出现：

- 多条语音重叠
- 被视频解说盖住
- 上一条未播完下一条已开始
- 无法手动排期

因此采用 **两层结构**：

```text
┌─────────────────────────────────────────────────────────────┐
│  分析组 Pipeline（已有 / 计划中）                              │
│  原始 XML → 筛选打分 → 分类 → 爆发点 → TTS 候选               │
└──────────────────────────┬──────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────┐
│  Audio Scheduler（分析组或 Unity 前处理，需新增）               │
│  去重叠 · 最小间隔 · 优先级 · 区域过滤 · Ducking 区间          │
└──────────────────────────┬──────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────┐
│  Unity 运行时                                                 │
│  audio_schedule.json + crowd_audio_schedule.json            │
│  → 3D 空间播放 · 视频音量 Ducking · 联动灯光/粒子              │
└─────────────────────────────────────────────────────────────┘
```

### 1.2 两类音频必须分开

| 类型 | 分析组字段 | Unity 表现 | 示例 |
|------|-----------|-----------|------|
| **观众声浪** `AUDIO_CROWD` | `burst_events.json` → `audio_crowd_level` | 预录 clip（欢呼/嘘声/心跳），4 方向环境音 | 「啊啊啊进了」 |
| **虚拟讨论** `TTS_DISCUSSION` | `tts_candidates.json` | TTS 或预生成 mp3，3D 空间单点播放 | 「这球越位了吧？」 |

**规则：** `KEEP_ATMOSPHERE` + 高 `atmosphere_value_score` → 声浪 / 粒子，**不一定**进 TTS。  
**规则：** 高 `tts_value_score` + `TTS_DISCUSSION` → 才进入语音讨论排期。

---

## 2. 分工

| 职责 | 负责方 | 交付物 |
|------|--------|--------|
| 弹幕解析、筛选、打分、分类 | 分析组 | `filtered_danmaku.json`, `classified_danmaku.json` |
| 爆发点检测与 VR 设计提示 | 分析组 | `burst_events.json` |
| TTS 文本聚合（不说原弹幕） | 分析组 | `tts_candidates.json` |
| **播放时间排期**（防重叠） | 分析组 **或** 音频组 Python 脚本 | `audio_schedule.json`, `crowd_audio_schedule.json` |
| TTS 音频文件生成（可选） | 分析组 **或** 音频组 | `Assets/Audio/TTS/{segment_id}.mp3` |
| Unity 读取 JSON、3D 播放、Ducking | 音频组 | `AudioDanmakuController.cs` 等 |
| 3D 文字弹幕展示 | 文字弹幕组 | `DanmakuPlaybackController.cs`（已有） |
| 灯光 / 粒子 / 后处理联动 | 音频组 | `TimedAtmosphereController` 等（已有） |

---

## 3. 分析组现有输出（对齐《VR体育弹幕智能分析Agent框架》）

### 3.1 `filtered_danmaku.json` — 筛选与打分

分析组 **已有**。音频组主要关心：

```json
{
  "entries": [
    {
      "id": "d_000001",
      "time_sec": 192.4,
      "text": "啊啊啊啊进了",
      "filter_label": "KEEP_ATMOSPHERE",
      "analysis_value_score": 0.45,
      "atmosphere_value_score": 0.96,
      "tts_value_score": 0.12
    }
  ]
}
```

| 字段 | 音频用途 |
|------|----------|
| `filter_label` | `KEEP_ANALYSIS` 才考虑 TTS；`KEEP_ATMOSPHERE` 倾向声浪 |
| `tts_value_score` | **≥ 0.6** 建议才进入 TTS 候选（阈值可协商） |
| `atmosphere_value_score` | 驱动 `crowd_audio_schedule` 强度 |

### 3.2 `classified_danmaku.json` — VR 用途标签

```json
{
  "vr_utility": ["ATMOSPHERE_PARTICLE", "AUDIO_CROWD"]
}
```

| `vr_utility` 值 | 音频组处理 |
|-----------------|-----------|
| `AUDIO_CROWD` | → `crowd_audio_schedule.json` |
| `TTS_DISCUSSION` | → `tts_candidates.json` → `audio_schedule.json` |
| `TEXT_DISPLAY` | 文字组处理，音频组不播 |
| `SUPPRESS` | 忽略 |

### 3.3 `burst_events.json` — 爆发点与氛围

```json
{
  "burst_id": "F-1",
  "start_sec": 185,
  "peak_sec": 192,
  "end_sec": 205,
  "dominant_emotion": "joy",
  "vr_design_hint": {
    "particle_intensity": 0.95,
    "audio_crowd_level": 0.9,
    "text_display_limit": 15,
    "decay_seconds": 12,
    "tts_enabled": false
  }
}
```

音频组用法：

- `start_sec` ~ `end_sec`：环境欢呼 / 粒子 / 灯光窗口（与现有 `TimedAmbientAudioController` 一致）
- `audio_crowd_level`：音量倍率 `0.0 ~ 1.0`
- `tts_enabled`：`false` 时该爆发点 **不** 插入 TTS，只做声浪

### 3.4 `tts_candidates.json` — TTS 候选（分析组已有框架）

```json
{
  "video_id": "football_001",
  "tts_segments": [
    {
      "segment_id": "tts_001",
      "burst_id": "F-2",
      "time_sec": 348.0,
      "speaker_role": "angry_fan",
      "text": "这个 VAR 判罚太有争议了，大家都在质疑是不是越位。",
      "source_comment_ids": ["d_123", "d_129"],
      "emotion": "anger_confusion",
      "speed": 1.05,
      "pitch": 0.95,
      "volume": 0.8,
      "priority": 0.9
    }
  ]
}
```

**说明：** 文本应由 TTS Script Agent **聚合改写**，不要直接读「啊啊啊」「666」类原弹幕。

---

## 4. 需要新增的字段（请分析组确认或补齐）

当前 `tts_candidates.json` **缺少** Unity 排期与播放所需字段。建议在分析组输出或 Scheduler 阶段补充：

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `segment_id` | string | ✅ | 全局唯一，对应音频文件名 |
| `duration_sec` | float | ✅ | 预估或实测时长；无 mp3 时可按字数估算（中文约 4 字/秒 × speed） |
| `zone` | string | ✅ | 观众区域，见 §5 |
| `camp` | string | 建议 | `argentina` / `france` / `neutral` — 阵营过滤 |
| `priority` | float | ✅ | `0.0~1.0`，冲突时高优先级保留 |
| `spatial_anchor` | string | 建议 | `seat_left` / `seat_right` / `seat_back` / `seat_front` |
| `audio_clip` | string | 二选一 | 相对路径，如 `Audio/TTS/tts_001.mp3` |
| `duck_video` | bool | 建议 | 播放时是否压低视频解说音量，默认 `true` |
| `min_gap_after_sec` | float | 可选 | 本条播完后强制留白，默认 `0.8` |

**TTS 音频二选一：**

1. **预生成 mp3**（推荐 MVP）：分析组或音频组离线生成，Unity 只播放文件  
2. **运行时 TTS**：Unity 调第三方 API（延迟与成本需评估，Phase 2 考虑）

---

## 5. 区域与阵营（Zone / Camp）

VR 场景计划支持观众切换视角/阵营。音频需能 **按区域过滤**。

### 5.1 `zone` — 当前用户所在区域

| 值 | 含义 | Unity 行为 |
|----|------|-----------|
| `argentina` | 阿根廷观众席 | 仅在该 zone 激活时播放 |
| `france` | 法国观众席 | 同上 |
| `neutral` | 中立 / 全场 | 任意 zone 都播放 |
| `all` | 全局环境 | 不受 zone 切换影响（如整体欢呼） |

### 5.2 与文字弹幕前缀对齐（可选）

若文字组采用弹幕前缀 `[ARG]` / `[FRA]` / `[NEU]`，音频 `camp` 字段应与之对应，便于同一套数据驱动文字 + 声音。

---

## 6. Audio Scheduler 规则（需实现）

无论由分析组 Python 还是 Unity 编辑器工具实现，**进入 Unity 之前**必须完成排期。

### 6.1 默认参数（可协商）

| 规则 | 默认值 |
|------|--------|
| 同时播放的 TTS 条数 | **1** |
| 两条 TTS 最小间隔 | **0.8 秒** |
| 单段 TTS 最长 | **8 秒**（超长截断或降 priority） |
| 10 分钟窗口最多 TTS 条数 | **30~60**（按 priority 截断） |
| 与视频解说冲突 | `duck_video: true` 时视频音量 × **0.35**，持续 `duration + 0.3s` |
| 与 `burst` 声浪重叠 | TTS 开始时间可 **延后** 至 `burst.end_sec + 0.5s`，或降低 crowd 音量 |

### 6.2 优先级冲突处理

1. 同一 `time_sec` 多条 TTS → 保留 `priority` 最高  
2. 仍冲突 → 低优先级 **顺延** 到 `上一条结束 + min_gap_after_sec`  
3. 顺延超过 **5 秒** → 丢弃并写入 `schedule_stats.dropped_segments`

### 6.3 输入 / 输出

```text
输入: tts_candidates.json + burst_events.json（可选，用于避让声浪）
输出: audio_schedule.json
```

---

## 7. Unity 最终消费格式

### 7.1 `audio_schedule.json` — 虚拟讨论（TTS）

放置路径建议：`Assets/StreamingAssets/audio_schedule.json`（或 `Resources/`，与文字弹幕 JSON 策略统一即可）。

```json
{
  "schema_version": "1.0",
  "video_id": "football_001",
  "time_base": "video_sec",
  "duck_video_volume": 0.35,
  "default_min_gap_sec": 0.8,
  "events": [
    {
      "id": "tts_001",
      "start_sec": 348.0,
      "duration_sec": 4.2,
      "end_sec": 352.2,
      "zone": "neutral",
      "camp": "neutral",
      "speaker_role": "angry_fan",
      "text": "这个 VAR 判罚太有争议了……",
      "audio_clip": "Audio/TTS/tts_001.mp3",
      "volume": 0.8,
      "spatial_anchor": "seat_left",
      "priority": 0.9,
      "duck_video": true,
      "burst_id": "F-2",
      "source_comment_ids": ["d_123", "d_129"]
    }
  ],
  "schedule_stats": {
    "input_candidates": 120,
    "scheduled": 45,
    "dropped": 75,
    "max_overlap_resolved": 12
  }
}
```

| 字段 | Unity 用法 |
|------|-------------|
| `start_sec` / `end_sec` | 对照 `VideoPlayer.time` 触发 |
| `audio_clip` | `Resources.Load` 或 `StreamingAssets` 路径 |
| `spatial_anchor` | 映射到场景内空物体（Left/Right/Front/Back 观众席） |
| `zone` / `camp` | `ZoneManager` 过滤（未实现则播放 `neutral` + `all`） |
| `duck_video` | 播放期间降低 `screen` 上 `AudioSource` 音量 |

### 7.2 `crowd_audio_schedule.json` — 观众声浪

与现有 `TimedAmbientAudioController` 对齐，可由 `burst_events.json` 自动生成。

```json
{
  "schema_version": "1.0",
  "video_id": "football_001",
  "events": [
    {
      "id": "crowd_F-1",
      "burst_id": "F-1",
      "start_sec": 185.0,
      "stop_sec": 205.0,
      "clip": "Audio/欢呼声/short.mp3",
      "volume": 0.9,
      "zone": "all",
      "spatial": "surround",
      "targets": ["front", "back", "left", "right"],
      "loop": false,
      "note": "进球爆发欢呼"
    }
  ]
}
```

| `spatial` | 说明 |
|-----------|------|
| `surround` | 4 方向 `AmbientAudioGroup` 同时播 |
| `directional` | 仅 `targets` 中指定方向 |

**现有脚本映射：** `TimedAmbientAudioController` 的 `TimedAudioEvent`（`startTime`, `stopTime`, `audioSource`）可由本 JSON 导入或手工对照填写。

---

## 8. Unity 侧实现状态

### 8.1 已有（音频 / 氛围组）

| 组件 | 脚本 | 作用 |
|------|------|------|
| 4 方向环境音 | `TimedAmbientAudioController.cs` | 按视频时间播放 Front/Back/Left/Right |
| 灯光氛围 | `AtmosphereLightController.cs` | Neutral / Excited / Tension / Goal |
| 定时氛围切换 | `TimedAtmosphereController.cs` | 视频时间驱动模式 |
| 粒子 | `TimedParticleController.cs` | 进球等时间点触发 |

时间轴统一参考：**场景中 `screen` 的 `VideoPlayer.time`**。

### 8.2 待开发

| 组件 | 说明 |
|------|------|
| `AudioDanmakuController.cs` | 读取 `audio_schedule.json`，3D 播放 TTS，Ducking |
| `CrowdAudioScheduleLoader.cs` | 读取 `crowd_audio_schedule.json`，或扩展 `TimedAmbientAudioController` |
| `ZoneManager.cs` | 区域切换时过滤 `zone` / `camp` |
| Audio Scheduler 工具 | Python 或 Unity Editor，生成 §7 两个 JSON |

### 8.3 文字弹幕组（已有，供对齐）

| 组件 | 脚本 |
|------|------|
| JSON 弹幕播放 | `DanmakuPlaybackController.cs` |
| 数据结构 | `DanmakuEntry` / `DanmakuCollection` |

文字与音频 **共用同一时间轴**，但 **不共用同一 JSON**；通过相同的 `video_id` 与 `time_sec` 对齐。

---

## 9. 端到端数据流（MVP）

```text
1. Bilibili XML
      ↓
2. 分析组 Pipeline
      → filtered_danmaku.json
      → classified_danmaku.json
      → burst_events.json
      → tts_candidates.json
      ↓
3. Audio Scheduler（新增）
      → crowd_audio_schedule.json   （可由 burst 自动生成）
      → audio_schedule.json         （TTS 排期后）
      ↓
4. 可选：TTS 离线生成 mp3 → Assets/Audio/TTS/
      ↓
5. Unity pop_upScene
      → TimedAmbientAudioController / CrowdAudioScheduleLoader
      → AudioDanmakuController
      → TimedAtmosphereController + TimedParticleController
      → DanmakuPlaybackController（文字，并行）
```

---

## 10. 待分析组确认的 6 个问题

请分析组在下次对接时明确回复：

1. **`tts_candidates.json` 是否会包含 `duration_sec` 和 `segment_id`？** 若否，由哪一方负责估算/生成？  
2. **是否会输出 `camp` / `zone`（阿根廷 / 法国 / 中立）？** 依据是关键词、LLM 还是人工规则？  
3. **TTS 音频由谁生成？** 预生成 mp3 还是 Unity 运行时合成？  
4. **Audio Scheduler 由谁实现？** 分析组 Python 输出最终 `audio_schedule.json`，还是音频组接收 `tts_candidates` 后排期？  
5. **`burst_events.vr_design_hint.tts_enabled` 是否会在 Phase 1 就输出？**  
6. **10 分钟比赛片段的目标 TTS 条数** 是否有上限预期（建议 30~60）？

---

## 11. MVP 验收标准

| # | 条件 |
|---|------|
| 1 | 分析组提供至少 1 个视频的 `tts_candidates.json` + `burst_events.json` |
| 2 | Scheduler 产出 `audio_schedule.json`，任意 60 秒内 **无 TTS 重叠** |
| 3 | Unity 播放时 TTS 从正确 `spatial_anchor` 方向可听 |
| 4 | TTS 播放期间视频解说音量明显降低（Ducking） |
| 5 | 爆发点 `crowd` 声浪与灯光 / 粒子时间对齐（误差 < 0.5s） |
| 6 | 切换 zone 后，仅播放匹配 `zone` / `neutral` / `all` 的音频事件 |

---

## 12. 附录：与现有 TimedAudioEvent 的对照

现有 Inspector 配置：

```csharp
// TimedAmbientAudioController.cs
public class TimedAudioEvent {
    public AudioSource audioSource;
    public float startTime;
    public float stopTime;      // -1 = 不自动停
    public bool restartOnEnter;
    public string note;
}
```

| JSON (`crowd_audio_schedule`) | TimedAudioEvent |
|-------------------------------|-----------------|
| `start_sec` | `startTime` |
| `stop_sec` | `stopTime` |
| `targets[]` → 对应 AudioSource | `audioSource` |
| `note` | `note` |

TTS 排期 **不** 复用 `TimedAudioEvent`，需独立 `AudioDanmakuController`（一次性 Play，非窗口循环）。

---

## 13. 修订记录

| 版本 | 日期 | 说明 |
|------|------|------|
| v0.1 | 2026-06-26 | 初稿：对齐分析组 Agent 框架，补充 Scheduler 与 Unity 消费格式 |
