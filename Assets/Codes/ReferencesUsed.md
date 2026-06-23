# References Used For Danmaku Parsing

This implementation uses the Reference papers as design guidance for filtering, normalization, and Unity playback behavior.

## Primary References

- `Reference/Explore/DanmuA11y_Making Time-Synced On-Screen Video Comments.pdf`
  - Used for the core time-synced comment model, readability concerns, overload reduction, and keeping comments aligned with video time.
- `Reference/Explore/DanmuA11y_Making Time-Synced On-Screen Video Comments_billingual.pdf`
  - Used as a bilingual cross-check for the same accessibility and synchronized-comment ideas.
- `Reference/DanmuScript/Making_sense_of_danmu_Coherence_in_massive_anonymous_chats_on_Bilibili.com.pdf`
  - Used for treating repeated comments as collective signals while still limiting near-duplicate spam in dense windows.
- `Reference/Explore/CoKnowledge_Supporting Assimilation of Time-synced Collective.pdf`
  - Used for time-window aggregation and density control so high-volume segments remain readable.
- `Reference/Explore/Danmaku Avatar_Enabling Asynchronous Co-viewing Experiences in Virtual Reality via Danmaku.pdf`
  - Used for VR-friendly playback assumptions: asynchronous comments should preserve social presence without overwhelming the viewer.

## Supporting References

- `Reference/DanmuScript/VAD_A_Video_Affective_Dataset_With_Danmu.pdf`
  - Used to justify retaining short affective reactions when they carry emotional or event-response value.
- `Reference/DanmuScript/Visual-Texual_Emotion_Analysis_With_Deep_Coupled_Video_and_Danmu_Neural_Networks.pdf`
  - Used to keep emotional cues as priority signals rather than filtering only by text length.

## Rules Derived From The References

- Keep video time as the primary ordering key.
- Preserve necessary Bilibili metadata: mode, font size, color, anonymous user hash, and danmaku id.
- Remove empty, malformed, symbol-only, and low-information comments.
- Downgrade near-duplicates inside a short time window instead of deleting all repeated social reactions.
- Limit entries per time window to reduce visual overload.
- Keep short sports reactions such as `牛逼`, `进了`, `好球`, and `goal` because they can mark emotionally important moments.
- Assign tracks during preprocessing so Unity playback is stable and does not need to solve layout from scratch at runtime.
