# Emotion TTS Audio Danmaku MVP

This local MVP plays short emotion danmaku as spatial TTS in `pop_upScene`.

## What Changed

- Added `Assets/Codes/AudioScheduleData.cs`.
- Added `Assets/Codes/AudioDanmakuController.cs`.
- Added `Assets/Codes/VideoProgressBarController.cs`.
- Added `Tools/generate_emotion_tts_schedule.py`.
- Added `Tools/generate_emotion_tts_clips.ps1`.
- Generated local Unity runtime data:
  - `Assets/StreamingAssets/audio_schedule.json`
  - `Assets/StreamingAssets/emotion_tts_candidates.json`
  - `Assets/StreamingAssets/emotion_tts_manifest.json`
  - `Assets/StreamingAssets/emotion_tts_mount_table.csv`
  - `Assets/StreamingAssets/emotion_tts_mount_table.md`
  - `Assets/StreamingAssets/emotion_tts_mount_table_source_format.json`
  - `Assets/StreamingAssets/Audio/TTS/emotion_tts_001.wav` through `emotion_tts_045.wav`
- Updated `Assets/Scenes/pop_upScene.unity`:
  - Added `EmotionTTSController`.
  - Set `affectVideoVolume` to `false`, so TTS does not change the original video audio volume.
  - Added a runtime video progress bar controller to the same object.
  - Disabled the old timed particle, atmosphere light, skybox blend, and fixed timed-audio demo controllers.

No GitHub push or commit was made.

## How It Works

`EmotionTTSController` reads `Assets/StreamingAssets/audio_schedule.json` and triggers clips by `VideoPlayer.time`.

Each event keeps the original danmaku time in `original_time_sec`. `start_sec` usually matches that time exactly. If two selected danmaku have the exact same timestamp, the second one may be offset by only `0.12s` so the attack is less harsh while still sounding simultaneous.

Spatial anchors map to the existing scene directions:

- `seat_front` -> `AudioScource_Font` / `AudioScource_Front`
- `seat_back` -> `AudioScource_Back`
- `seat_left` -> `AudioScource_Left`
- `seat_right` -> `AudioScource_Right`

The controller creates runtime `TTSAnchor_*` objects at those positions and uses 3D audio sources with overlap enabled.

The current schedule excludes punctuation-only danmaku such as `？？？` and `！！！`. Short emotional text is kept, but pure punctuation is not converted into spoken filler for this version.

Video audio is intentionally isolated from audio danmaku. `audio_schedule.json` has `duck_video` disabled for all events, and `EmotionTTSController.affectVideoVolume` is off in the scene.

## Adjust Volume In Inspector

Select `EmotionTTSController` in `Assets/Scenes/pop_upScene.unity`.

- `Tts Playback Gain`: master volume for all audio danmaku. Current value is `2.2`.
- `Max Event Volume Scale`: safety cap for one event after all multipliers.
- `Amused Fan Volume`: extra multiplier for laughter reactions.
- `Tense Fan Volume`: extra multiplier for nervous or tense reactions.
- `Chant Fan Volume`: extra multiplier for chants or short cheers.
- `Excited Fan Volume`: extra multiplier for positive or excited reactions.
- `Neutral Fan Volume`: fallback multiplier for unknown roles.
- `Tts Spatial Blend`: `1` is fully spatial, `0` is fully 2D. Current value is `0.85`.
- `Tts Min Distance`: distance before 3D attenuation starts. Current value is `6`.
- `Tts Max Distance`: distance where the voice becomes quiet. Current value is `40`.

Final playback volume is `event volume * Tts Playback Gain * role volume`, capped by `Max Event Volume Scale`. The current mix assumes the `screen` video AudioSource volume is around `0.5`, with `Tts Playback Gain` at `2.2`. If TTS is still too quiet, first raise `Tts Playback Gain` toward `3`; if it feels too close to flat 2D audio, raise `Tts Spatial Blend` back toward `1`.

## Regenerate The Schedule

Run from the Unity project folder:

```powershell
python .\Tools\generate_emotion_tts_schedule.py --target-count 45 --audio-ext wav
```

This reads the W2 emotion table from the SURF workspace and rewrites the schedule, candidates, manifest, and mount tables.

## Regenerate Local TTS Audio

Run from the Unity project folder:

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\generate_emotion_tts_clips.ps1 -Overwrite
```

The current generated clips use local Windows SAPI voices only. On this machine, Chinese lines use `Microsoft Huihui Desktop`; pure Latin/English lines use an English SAPI voice such as `Microsoft Zira Desktop` when available. This avoids API keys and network calls. The generator applies different rate, pitch, volume, emphasis, and short pause profiles by role:

- `amused_fan`: faster, higher, and brighter, for laughter.
- `tense_fan`: slower, lower, and more weighted, for nervous reactions.
- `chant_fan`: loudest and strongly emphasized, for chants or repeated cheers.
- `excited_fan`: bright, loud, and moderately emphasized, for positive reactions.

The script also adds natural punctuation before synthesis, such as making short chants end with `!` and tense lines settle with a period. The schedule text is unchanged; only the rendered local `.wav` files are affected.

The first two Chinese tense TTS clips are rendered with a faster event-specific profile, so the opening reactions sound more immediate than the later tense comments.

## Generate Website TTS Audio

The preferred higher-quality voice path is now the front-end UI at:

```text
https://www.text-to-speech.cn/
```

Use the visible website UI only. The site page warns against captured backend API calls, so this project does not include a hidden API caller.

First build the website task list:

```powershell
python .\Tools\build_website_tts_voice_plan.py
```

This writes:

```text
Assets/StreamingAssets/emotion_tts_website_voice_plan.json
Assets/StreamingAssets/emotion_tts_website_voice_plan.csv
Assets/StreamingAssets/emotion_tts_website_voice_plan.md
```

The current plan keeps the Unity clip names unchanged and assigns 45 events across 13 website voices, including `zh-CN-XiaoxiaoNeural`, `zh-CN-XiaohanNeural`, `zh-CN-XiaochenNeural`, `zh-CN-XiaomengNeural`, `zh-CN-XiaoshuangNeural`, `zh-CN-XiaoyouNeural`, `zh-CN-XiaoyiNeural`, `zh-CN-XiaomoNeural`, `zh-CN-YunxiNeural`, `zh-CN-YunjianNeural`, `zh-CN-YunyangNeural`, `zh-CN-YunhaoNeural`, and `zh-CN-YunfengNeural`.

Recommended website settings:

- Language: `中文（普通话，简体）`.
- Format: `riff-16khz-16bit-mono-pcm` / WAV.
- Put only the row's danmaku text into the website text box. Use `website_input_text` / `text`, not SSML.
- Use each row's `voice`, `style`, `styledegree`, `rate`, `pitch`, and `volume` as normal website controls when practical.
- If the website reports that a chosen voice does not support the chosen style, keep the voice and switch the style to `cheerful`, `chat`, or `excited` based on the row's role.
- Save or rename each download to the row's `download_filename`, such as `emotion_tts_001.wav`.

The first two Chinese tense lines are intentionally faster in the website plan:

- `emotion_tts_001`: `+22%`
- `emotion_tts_002`: `+20%`

After downloading the website clips, put them in a folder such as:

```text
Tools/WebsiteTTSDownloads/
```

Then import them over the existing Unity TTS clips:

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\import_website_tts_downloads.ps1 `
  -DownloadDir .\Tools\WebsiteTTSDownloads `
  -Overwrite
```

If the website downloads in the exact same order as the voice plan but the names are hard to control, this fallback maps files by `LastWriteTime`:

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\import_website_tts_downloads.ps1 `
  -DownloadDir .\Tools\WebsiteTTSDownloads `
  -UseSortedFiles `
  -Overwrite
```

Keep the website output as `.wav` unless you also regenerate `audio_schedule.json` with another extension. The Unity controller can load `.mp3`, but the current schedule points to `.wav`.

If the website says the visitor daily free quota is full, log in or switch to an account with available quota in the browser, then continue from the first missing file in `Assets/StreamingAssets/emotion_tts_website_import_manifest.json`. The current import script is safe to rerun with `-Overwrite`; it imports files that exist and reports missing ones.

## Video Progress Bar

`VideoProgressBarController` is mounted on `EmotionTTSController`. At runtime it creates a bottom progress bar named `VideoProgressCanvas / VideoProgressSlider`.

- Drag the slider to seek the `screen` `VideoPlayer`.
- Press the right arrow or left arrow to jump by `10s`.
- The audio danmaku controller is notified after every seek, stops any old TTS voice, and restarts its event index from the new video time.
- The normal danmaku playback controller is also notified, clears spawned text danmaku, and restarts from the new video time.

Inspector settings on `EmotionTTSController`:

- `Create Runtime Ui`: automatically creates the progress bar during Play mode.
- `Bottom Offset`, `Width`, `Height`: layout of the progress bar.
- `Enable Keyboard Shortcuts` and `Keyboard Seek Step Seconds`: arrow-key seek behavior.

## How To Check In Unity

1. Open `Assets/Scenes/pop_upScene.unity`.
2. Press Play.
3. Confirm `EmotionTTSController` is enabled.
4. Confirm these old demo controllers are disabled:
   - `EffectController`
   - `TimedAtmosphereController`
   - `TimedSkyboxBlendController`
   - `TimeAudioController`
   - `AtmosphereController`
5. Watch the video and compare against `Assets/StreamingAssets/emotion_tts_mount_table.md`.

Good quick check points:

- `00:03.338`: "这气氛吓人啊" from `seat_left`.
- `01:18.405`: "这才是坚毅的眼神！" from `seat_back`.
- `03:07.519` to `03:12.662`: multiple laugh reactions from different anchors.
- `03:47.977`: two laugh comments near the same timestamp, split across `seat_right` and `seat_front`.
- `09:00.242`: "什么时候看都激动的比赛" from `seat_left`.

## Mount Table

Use this file as the authoritative playback checklist:

```text
Assets/StreamingAssets/emotion_tts_mount_table.md
```

The CSV version is also available:

```text
Assets/StreamingAssets/emotion_tts_mount_table.csv
```

The source-format JSON version matches the W2 source file shape, with top-level `category`, `category_zh`, `count`, `sentiment_counts`, and `items`. Each item keeps the source-style fields `弹幕内容`, `长度`, `新视频中的时间`, and `正反面情绪`, plus TTS mount fields such as `TTS朗读文本`, `TTS角色`, `TTS空间锚点`, and `TTS音频文件`:

```text
Assets/StreamingAssets/emotion_tts_mount_table_source_format.json
```
