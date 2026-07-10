using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Video;

/// <summary>
/// 按视频时间轴 3D 播放 TTS 弹幕。
/// 排期来源：StreamingAssets/Audio/tts_candidates_no_overlap.json（time_sec 为触发时刻）。
/// 每条 mp3 在整个播放过程中只播一次；只有真正的 Seek（倒退/暂停时拖动进度条）
/// 才会强制重播当时应播的那一条，正常播放中的卡顿（时间前跳）不会导致重播。
/// </summary>
[DefaultExecutionOrder(200)]
public class AudioDanmakuController : MonoBehaviour
{
    [Header("时间轴")]
    public VideoPlayer videoPlayer;

    [Tooltip("弹幕排期（相对 StreamingAssets）；time_sec 为触发时刻")]
    public string candidatesFileName = "Audio/tts_candidates_no_overlap.json";

    [Tooltip("找不到 candidatesFileName 时的兼容回退")]
    public string scheduleFileName = "audio_schedule.json";

    [Header("空间锚点（对应 spatial_anchor）")]
    public Transform anchorLeft;
    public Transform anchorRight;
    public Transform anchorFront;
    public Transform anchorBack;

    [Header("视频解说")]
    public AudioSource videoCommentarySource;
    [Tooltip("全程视频解说音量（TTS 播放时也不变，除非开启 Ducking）")]
    [Range(0f, 1f)] public float videoCommentaryVolume = 1f;
    [Tooltip("TTS 播放时是否压低视频解说")]
    public bool enableVideoDucking;
    [Range(0.05f, 1f)] public float duckVideoVolume = 0.35f;

    [Header("TTS")]
    [Range(0.5f, 1000f)] public float ttsPlaybackGain = 1.35f;

    [Header("加载")]
    public bool preloadOnStart;
    public int maxConcurrentPreloads = 2;

    AudioScheduleFile schedule;
    readonly Dictionary<string, AudioClip> clipCache = new();
    readonly HashSet<string> loadingPaths = new();
    readonly Dictionary<string, AudioSource> anchorSources = new();
    readonly HashSet<string> startedClips = new();
    int nextEventIndex;
    int playbackGeneration;
    float lastVideoTime = -1f;
    int activeDuckCount;
    bool initialized;

    void Start()
    {
        StartCoroutine(InitWhenReady());
    }

    IEnumerator InitWhenReady()
    {
        yield return null;
        yield return null;

        if (videoPlayer == null)
            videoPlayer = FindObjectOfType<VideoPlayer>();

        if (videoCommentarySource == null && videoPlayer != null)
            videoCommentarySource = videoPlayer.GetComponent<AudioSource>();

        ApplyVideoCommentaryVolume();
        EnsureAnchors();
        LoadSchedule();

        float t = videoPlayer != null ? (float)videoPlayer.time : 0f;
        ResetPlaybackState(t, forcePlayActiveEvent: false);
        lastVideoTime = t;

        if (preloadOnStart && schedule?.events != null)
            StartCoroutine(PreloadClipsGradually());
    }

    void Update()
    {
        if (!initialized || videoPlayer == null || schedule?.events == null)
            return;

        if (!videoPlayer.isPrepared)
            return;

        float t = (float)videoPlayer.time;

        if (ShouldResetForSeek(t))
            ResetPlaybackState(t, forcePlayActiveEvent: true);

        lastVideoTime = t;

        if (!videoPlayer.isPlaying)
            return;

        DispatchEventsUpTo(t);
    }

    /// <summary>
    /// 只有倒退（循环重播/用户往回拖）或暂停时的跳动（拖动进度条）才算真正的 Seek。
    /// 正常播放时往前的时间跳动（哪怕是加载卡顿造成的一次性大跳），不算 Seek，
    /// 否则会把当时正在播的那条弹幕误判成"用户拖动"而强制重播，造成开头几条反复播放。
    /// </summary>
    bool ShouldResetForSeek(float videoTime)
    {
        if (lastVideoTime < 0f)
            return false;

        float delta = videoTime - lastVideoTime;

        if (delta < -0.05f)
            return true;

        if (!videoPlayer.isPlaying && Mathf.Abs(delta) > 0.05f)
            return true;

        return false;
    }

    void ResetPlaybackState(float t, bool forcePlayActiveEvent)
    {
        playbackGeneration++;
        StopAllTts();
        activeDuckCount = 0;
        ApplyVideoCommentaryVolume();
        startedClips.Clear();

        // 只把"已经完整播完"的弹幕标记为已播放；正落在 t 位置的那一条不标记，
        // 这样倒退回它的时间范围内时还能再听到。
        for (int i = 0; i < schedule.events.Length; i++)
        {
            var ev = schedule.events[i];
            if (ev.start_sec + ev.duration_sec < t)
                startedClips.Add(ev.audio_clip);
        }

        nextEventIndex = FindFirstIndexAtOrAfter(t);

        if (!forcePlayActiveEvent)
            return;

        int activeIdx = FindActiveEventIndex(t);
        if (activeIdx < 0)
            return;

        var activeEv = schedule.events[activeIdx];
        TryStartEvent(activeEv, Mathf.Max(0f, t - activeEv.start_sec));
        if (nextEventIndex <= activeIdx)
            nextEventIndex = activeIdx + 1;
    }

    int FindActiveEventIndex(float t)
    {
        for (int i = 0; i < schedule.events.Length; i++)
        {
            var ev = schedule.events[i];
            if (ev.start_sec <= t && t < ev.start_sec + ev.duration_sec)
                return i;
        }

        return -1;
    }

    int FindFirstIndexAtOrAfter(float timeSec)
    {
        int low = 0;
        int high = schedule.events.Length;
        while (low < high)
        {
            int mid = low + (high - low) / 2;
            if (schedule.events[mid].start_sec < timeSec)
                low = mid + 1;
            else
                high = mid;
        }

        return low;
    }

    void DispatchEventsUpTo(float t)
    {
        while (nextEventIndex < schedule.events.Length &&
               schedule.events[nextEventIndex].start_sec <= t)
        {
            TryStartEvent(schedule.events[nextEventIndex], 0f);
            nextEventIndex++;
        }
    }

    bool TryStartEvent(AudioScheduleEvent ev, float clipStartOffset)
    {
        if (string.IsNullOrWhiteSpace(ev.audio_clip))
            return false;

        if (startedClips.Contains(ev.audio_clip))
            return false;

        startedClips.Add(ev.audio_clip);
        StartCoroutine(PlayEvent(ev, clipStartOffset));
        return true;
    }

    void StopAllTts()
    {
        foreach (var src in anchorSources.Values)
        {
            if (src != null && src.isPlaying)
                src.Stop();
        }
    }

    void EnsureAnchors()
    {
        if (anchorFront == null)
        {
            var front = GameObject.Find("AudioScource_Font") ?? GameObject.Find("AudioScource_Front");
            anchorFront = GetOrCreateTtsAnchor(front != null ? front.transform : null, "TTSAnchor_Front");
        }
        if (anchorBack == null)
        {
            var back = GameObject.Find("AudioScource_Back");
            anchorBack = GetOrCreateTtsAnchor(back != null ? back.transform : null, "TTSAnchor_Back");
        }
        if (anchorLeft == null)
        {
            var left = GameObject.Find("AudioScource_Left");
            anchorLeft = GetOrCreateTtsAnchor(left != null ? left.transform : null, "TTSAnchor_Left");
        }
        if (anchorRight == null)
        {
            var right = GameObject.Find("AudioScource_Right");
            anchorRight = GetOrCreateTtsAnchor(right != null ? right.transform : null, "TTSAnchor_Right");
        }

        RegisterAnchorSource("seat_front", anchorFront);
        RegisterAnchorSource("seat_back", anchorBack);
        RegisterAnchorSource("seat_left", anchorLeft);
        RegisterAnchorSource("seat_right", anchorRight);
    }

    Transform GetOrCreateTtsAnchor(Transform reference, string objectName)
    {
        var existing = GameObject.Find(objectName);
        if (existing != null)
            return existing.transform;

        var go = new GameObject(objectName);
        if (reference != null)
        {
            go.transform.SetParent(reference.parent, worldPositionStays: true);
            go.transform.position = reference.position;
            go.transform.rotation = reference.rotation;
        }
        return go.transform;
    }

    void RegisterAnchorSource(string key, Transform anchor)
    {
        if (anchor == null) return;

        // StadiumAudioBootstrap 的四个方向锚点已经带有循环播放环境声的 AudioSource。
        // TTS 不能复用它，否则继承 loop=true 后每条语音都会循环播放多遍。
        const string sourceObjectName = "TTS_OneShot_Source";
        Transform sourceTransform = anchor.Find(sourceObjectName);
        if (sourceTransform == null)
        {
            var sourceObject = new GameObject(sourceObjectName);
            sourceTransform = sourceObject.transform;
            sourceTransform.SetParent(anchor, false);
        }

        var src = sourceTransform.GetComponent<AudioSource>();
        if (src == null)
            src = sourceTransform.gameObject.AddComponent<AudioSource>();

        src.spatialBlend = 1f;
        src.playOnAwake = false;
        src.loop = false;
        src.priority = 0;
        src.minDistance = 1f;
        src.maxDistance = 25f;
        anchorSources[key] = src;
    }

    void LoadSchedule()
    {
        if (TryLoadFromCandidates())
            return;

        Debug.LogWarning($"[AudioDanmaku] 未找到 {candidatesFileName}，回退到 {scheduleFileName}。");
        LoadFromGeneratedSchedule();
    }

    bool TryLoadFromCandidates()
    {
        if (string.IsNullOrWhiteSpace(candidatesFileName))
            return false;

        string path = Path.Combine(Application.streamingAssetsPath, candidatesFileName.Replace('\\', '/'));
        if (!File.Exists(path))
            return false;

        string rawText = File.ReadAllText(path, System.Text.Encoding.UTF8).Trim();
        var wrapper = JsonUtility.FromJson<TtsCandidateArrayWrapper>("{\"items\":" + rawText + "}");
        if (wrapper?.items == null || wrapper.items.Length == 0)
        {
            Debug.LogError($"[AudioDanmaku] {candidatesFileName} 无有效条目。");
            return false;
        }

        var events = new AudioScheduleEvent[wrapper.items.Length];
        for (int i = 0; i < wrapper.items.Length; i++)
        {
            var c = wrapper.items[i];
            string id = $"tts_{i + 1:000}";
            float start = c.time_sec;
            float duration = c.duration_sec > 0f ? c.duration_sec : Mathf.Max(1.5f, (c.text?.Length ?? 0) * 0.22f);

            events[i] = new AudioScheduleEvent
            {
                id = id,
                start_sec = start,
                duration_sec = duration,
                end_sec = start + duration,
                speaker_role = c.speaker_role,
                text = c.text,
                audio_clip = $"Audio/TTS/{id}.mp3",
                volume = c.volume,
                spatial_anchor = c.spatial_anchor,
                priority = c.priority,
                duck_video = !c.can_overlap_crowd,
                tts_mix_type = c.tts_mix_type,
                preferred_crowd_duck_volume = c.preferred_crowd_duck_volume
            };
        }

        schedule = new AudioScheduleFile { events = events };
        System.Array.Sort(schedule.events, (a, b) => a.start_sec.CompareTo(b.start_sec));
        initialized = true;
        Debug.Log($"[AudioDanmaku] 已加载 {schedule.events.Length} 条 TTS 排期。");
        return true;
    }

    void LoadFromGeneratedSchedule()
    {
        string path = Path.Combine(Application.streamingAssetsPath, scheduleFileName.Replace('\\', '/'));
        if (!File.Exists(path))
        {
            Debug.LogError($"[AudioDanmaku] 未找到排期文件：{path}");
            return;
        }

        schedule = JsonUtility.FromJson<AudioScheduleFile>(File.ReadAllText(path, System.Text.Encoding.UTF8));
        if (schedule?.events == null || schedule.events.Length == 0)
        {
            Debug.LogError("[AudioDanmaku] audio_schedule.json 无有效 events。");
            return;
        }

        if (enableVideoDucking && schedule.duck_video_volume > 0f)
            duckVideoVolume = schedule.duck_video_volume;

        System.Array.Sort(schedule.events, (a, b) => a.start_sec.CompareTo(b.start_sec));
        initialized = true;
        Debug.Log($"[AudioDanmaku] 加载 {schedule.events.Length} 条 TTS 排期。");
    }

    IEnumerator PreloadClipsGradually()
    {
        int inFlight = 0;
        foreach (var ev in schedule.events)
        {
            if (string.IsNullOrWhiteSpace(ev.audio_clip) || clipCache.ContainsKey(ev.audio_clip))
                continue;

            while (inFlight >= Mathf.Max(1, maxConcurrentPreloads))
                yield return null;

            inFlight++;
            yield return LoadClip(ev.audio_clip);
            inFlight--;
            yield return null;
        }
    }

    IEnumerator LoadClip(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            yield break;

        if (clipCache.TryGetValue(relativePath, out var cached) && cached != null)
            yield break;

        if (loadingPaths.Contains(relativePath))
        {
            while (loadingPaths.Contains(relativePath))
                yield return null;
            yield break;
        }

        string path = Path.Combine(Application.streamingAssetsPath, relativePath.Replace('\\', '/'));
        if (!File.Exists(path))
        {
            Debug.LogWarning($"[AudioDanmaku] 缺少音频：{path}");
            yield break;
        }

        loadingPaths.Add(relativePath);
        string url = "file:///" + path.Replace('\\', '/');
        using var req = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.MPEG);
        if (req.downloadHandler is DownloadHandlerAudioClip handler)
            handler.streamAudio = true;

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"[AudioDanmaku] 加载失败 {relativePath}: {req.error}");
            loadingPaths.Remove(relativePath);
            yield break;
        }

        clipCache[relativePath] = DownloadHandlerAudioClip.GetContent(req);
        loadingPaths.Remove(relativePath);
    }

    IEnumerator PlayEvent(AudioScheduleEvent ev, float clipStartOffset)
    {
        int generation = playbackGeneration;

        if (!clipCache.TryGetValue(ev.audio_clip, out var clip) || clip == null)
        {
            yield return LoadClip(ev.audio_clip);
            if (generation != playbackGeneration)
                yield break;

            if (!clipCache.TryGetValue(ev.audio_clip, out clip) || clip == null)
                yield break;
        }

        string anchorKey = string.IsNullOrWhiteSpace(ev.spatial_anchor) ? "seat_front" : ev.spatial_anchor;
        if (!anchorSources.TryGetValue(anchorKey, out var src) || src == null)
            anchorSources.TryGetValue("seat_front", out src);
        if (src == null)
            yield break;

        if (generation != playbackGeneration)
            yield break;

        if (enableVideoDucking && ev.duck_video)
            BeginDuck();

        src.clip = clip;
        src.volume = Mathf.Max(0f, ev.volume * ttsPlaybackGain);
        src.time = Mathf.Clamp(clipStartOffset, 0f, Mathf.Max(0f, clip.length - 0.01f));
        src.Play();

        float wait = Mathf.Max(0.05f, clip.length - src.time);
        while (wait > 0f)
        {
            if (generation != playbackGeneration)
                yield break;

            wait -= Time.deltaTime;
            yield return null;
        }

        if (generation != playbackGeneration)
            yield break;

        if (enableVideoDucking && ev.duck_video)
            EndDuck();
    }

    void ApplyVideoCommentaryVolume()
    {
        if (videoCommentarySource != null)
            videoCommentarySource.volume = videoCommentaryVolume;

        if (videoPlayer != null)
            videoPlayer.SetDirectAudioVolume(0, videoCommentaryVolume);
    }

    void BeginDuck()
    {
        activeDuckCount++;
        if (videoCommentarySource != null)
            videoCommentarySource.volume = duckVideoVolume;
        if (videoPlayer != null)
            videoPlayer.SetDirectAudioVolume(0, duckVideoVolume);
    }

    void EndDuck()
    {
        activeDuckCount = Mathf.Max(0, activeDuckCount - 1);
        if (activeDuckCount == 0)
            ApplyVideoCommentaryVolume();
    }
}
