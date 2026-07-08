using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Video;

/// <summary>
/// 读取 audio_schedule.json，按视频时间轴 3D 播放 TTS，并在播放期间压低视频解说音量。
/// </summary>
[DefaultExecutionOrder(200)]
public class AudioDanmakuController : MonoBehaviour
{
    [Header("时间轴")]
    public VideoPlayer videoPlayer;
    [Tooltip("留空则读取 StreamingAssets/audio_schedule.json")]
    public string scheduleFileName = "audio_schedule.json";

    [Header("空间锚点（对应 spatial_anchor）")]
    public Transform anchorLeft;
    public Transform anchorRight;
    public Transform anchorFront;
    public Transform anchorBack;

    [Header("Ducking")]
    public AudioSource videoCommentarySource;
    [Range(0.05f, 1f)] public float duckVideoVolume = 0.35f;

    [Header("TTS")]
    [Range(0.5f, 2f)] public float ttsPlaybackGain = 1.35f;

    [Header("加载")]
    [Tooltip("启动时预加载；大量 TTS 时建议关闭，避免 Play 卡顿")]
    public bool preloadOnStart;
    [Tooltip("预加载并发数，过大会在首帧阻塞主线程")]
    public int maxConcurrentPreloads = 2;

    AudioScheduleFile schedule;
    readonly Dictionary<string, AudioClip> clipCache = new();
    readonly HashSet<string> loadingPaths = new();
    readonly Dictionary<string, AudioSource> anchorSources = new();
    int nextEventIndex;
    float originalVideoVolume = 1f;
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

        if (videoCommentarySource != null)
            originalVideoVolume = videoCommentarySource.volume;

        EnsureAnchors();
        LoadSchedule();
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

        if (t + 0.5f < GetLastTime() && nextEventIndex > 0)
            nextEventIndex = 0;

        while (nextEventIndex < schedule.events.Length &&
               schedule.events[nextEventIndex].start_sec <= t)
        {
            var ev = schedule.events[nextEventIndex];
            if (t <= ev.end_sec + 0.05f)
                StartCoroutine(PlayEvent(ev));
            nextEventIndex++;
        }
    }

    float GetLastTime()
    {
        if (schedule?.events == null || schedule.events.Length == 0) return 0f;
        return schedule.events[schedule.events.Length - 1].start_sec;
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
        var src = anchor.GetComponent<AudioSource>();
        if (src == null)
            src = anchor.gameObject.AddComponent<AudioSource>();
        src.spatialBlend = 1f;
        src.playOnAwake = false;
        src.priority = 0;
        src.minDistance = 1f;
        src.maxDistance = 25f;
        anchorSources[key] = src;
    }

    void LoadSchedule()
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

        if (schedule.duck_video_volume > 0f)
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

    IEnumerator PlayEvent(AudioScheduleEvent ev)
    {
        if (!clipCache.TryGetValue(ev.audio_clip, out var clip) || clip == null)
        {
            yield return LoadClip(ev.audio_clip);
            if (!clipCache.TryGetValue(ev.audio_clip, out clip) || clip == null)
                yield break;
        }

        string anchorKey = string.IsNullOrWhiteSpace(ev.spatial_anchor) ? "seat_front" : ev.spatial_anchor;
        if (!anchorSources.TryGetValue(anchorKey, out var src) || src == null)
            anchorSources.TryGetValue("seat_front", out src);
        if (src == null)
            yield break;

        if (ev.duck_video)
            BeginDuck();

        src.clip = clip;
        src.volume = Mathf.Clamp(ev.volume * ttsPlaybackGain, 0f, 2f);
        src.Play();

        float wait = Mathf.Max(0.05f, ev.duration_sec > 0f ? ev.duration_sec : clip.length);
        yield return new WaitForSeconds(wait);

        if (ev.duck_video)
            EndDuck();
    }

    void BeginDuck()
    {
        activeDuckCount++;
        if (videoCommentarySource != null)
            videoCommentarySource.volume = duckVideoVolume;
    }

    void EndDuck()
    {
        activeDuckCount = Mathf.Max(0, activeDuckCount - 1);
        if (activeDuckCount == 0 && videoCommentarySource != null)
            videoCommentarySource.volume = originalVideoVolume;
    }
}
