using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;

[Serializable]
public class TtsAudioScheduleFile
{
    public float duck_video_volume = 0.35f;
    public float duck_video_tail_sec = 0.3f;
    public float duck_crowd_volume = 0.38f;
    public TtsAudioScheduleEvent[] events;
}

[Serializable]
public class TtsAudioScheduleEvent
{
    public string id;
    public float start_sec;
    public float duration_sec;
    public float end_sec;
    public string text;
    public string audio_clip;
    public float volume = 1f;
    public float speed = 1f;
    public float pitch = 1f;
    public string spatial_anchor;
    public bool duck_video = true;
    public string emotion;
    public string speaker_gender;
}

public class TtsAudioScheduleController : MonoBehaviour
{
    [Header("JSON 路径（相对 StreamingAssets）")]
    public string jsonRelativePath = "AudioData/audio_schedule_1min.json";

    [Header("时间轴")]
    public VideoPlayer videoPlayer;
    public bool onlyWhenVideoPlaying = true;

    [Header("TTS 音源")]
    public AudioSource ttsAudioSource;
    public bool autoCreateTtsSource = true;
    public bool followMainCamera = true;

    [Header("视频原声 ducking")]
    public AudioSource videoAudioSource;

    [Header("环境音 ducking（拖 JsonScheduleManager 上的 Crowd 脚本）")]
    public CrowdAudioScheduleController crowdController;

    [Header("播放模式")]
    [Tooltip("开启后按 JSON 顺序连续播放，句间仅留极短间隔，保证全程有人声")]
    public bool playBackToBack = true;
    public float gapBetweenLinesSec = 0.15f;
    public float queueStartSec = 0.5f;

    [Header("调试")]
    public bool logPlayedLines = true;
    public bool preloadClipsOnStart = true;

    private TtsAudioScheduleEvent[] events = Array.Empty<TtsAudioScheduleEvent>();
    private float duckVideoVolume = 0.35f;
    private float duckVideoTailSec = 0.3f;
    private float duckCrowdVolume = 0.38f;
    private float originalVideoVolume = 1f;
    private float duckReleaseTimer;
    private double lastVideoTime = -1d;
    private readonly HashSet<int> playedEvents = new HashSet<int>();
    private readonly Dictionary<string, AudioClip> clipCache = new Dictionary<string, AudioClip>();
    private Coroutine queueCoroutine;
    private int queueIndex = -1;

    private void Reset()
    {
        videoPlayer = FindObjectOfType<VideoPlayer>();
        crowdController = FindObjectOfType<CrowdAudioScheduleController>();
    }

    private void Start()
    {
        EnsureTtsSource();
        CacheVideoVolume();
        LoadSchedule();

        if (crowdController == null)
            crowdController = GetComponent<CrowdAudioScheduleController>();
    }

    private void EnsureTtsSource()
    {
        if (ttsAudioSource != null)
        {
            ConfigureTtsSource(ttsAudioSource);
            return;
        }

        if (!autoCreateTtsSource)
            return;

        GameObject go = new GameObject("TTS_AudioSource_2D");
        go.transform.SetParent(transform);
        ttsAudioSource = go.AddComponent<AudioSource>();
        ConfigureTtsSource(ttsAudioSource);
    }

    private static void ConfigureTtsSource(AudioSource src)
    {
        src.playOnAwake = false;
        src.spatialBlend = 0f;
        src.volume = 1f;
        src.priority = 32;
        src.bypassListenerEffects = false;
    }

    private void CacheVideoVolume()
    {
        if (videoAudioSource != null)
            originalVideoVolume = videoAudioSource.volume;
    }

    public void LoadSchedule()
    {
        clipCache.Clear();
        playedEvents.Clear();
        StopQueue();

        TtsAudioScheduleFile file = StreamingAssetsReader.ReadJson<TtsAudioScheduleFile>(jsonRelativePath);
        if (file == null || file.events == null)
        {
            Debug.LogError($"TtsAudioScheduleController: 无法加载 {jsonRelativePath}");
            return;
        }

        events = file.events;
        duckVideoVolume = file.duck_video_volume > 0f ? file.duck_video_volume : 0.35f;
        duckVideoTailSec = file.duck_video_tail_sec > 0f ? file.duck_video_tail_sec : 0.3f;
        duckCrowdVolume = file.duck_crowd_volume > 0f ? file.duck_crowd_volume : 0.38f;

        if (preloadClipsOnStart)
            PreloadClips();

        if (events.Length > 0)
        {
            string firstClip = events[0].audio_clip ?? "";
            if (!firstClip.Contains("TTS"))
                Debug.LogWarning($"[TTS] 当前 JSON 第一条不是 TTS mp3（{firstClip}）。请确认 Inspector 里 json 指向 audio_schedule_1min.json");
        }

        Debug.Log($"TtsAudioScheduleController: 已加载 {events.Length} 条人声（{jsonRelativePath}），缓存 clip {clipCache.Count} 个。");
    }

    private void PreloadClips()
    {
        foreach (TtsAudioScheduleEvent e in events)
        {
            if (string.IsNullOrEmpty(e.audio_clip) || clipCache.ContainsKey(e.audio_clip))
                continue;

            AudioClip clip = ScheduleAudioLoader.LoadClip(e.audio_clip);
            if (clip != null)
            {
                clipCache[e.audio_clip] = clip;
                Debug.Log($"[TTS Preload OK] {e.audio_clip} ({clip.length:F1}s)");
            }
            else
            {
                Debug.LogError($"[TTS Preload FAIL] 找不到 {e.audio_clip}，请运行 python Tools/generate_tts_1min.py");
            }
        }
    }

    private void Update()
    {
        if (videoPlayer == null || events.Length == 0 || ttsAudioSource == null)
            return;

        if (onlyWhenVideoPlaying && !videoPlayer.isPlaying)
            return;

        if (followMainCamera && Camera.main != null)
            ttsAudioSource.transform.position = Camera.main.transform.position;

        float t = (float)videoPlayer.time;

        if (lastVideoTime >= 0d && t + 0.05f < lastVideoTime)
        {
            playedEvents.Clear();
            StopQueue();
        }

        lastVideoTime = videoPlayer.time;

        if (playBackToBack)
        {
            if (queueCoroutine == null && queueIndex < 0 && t >= queueStartSec)
                queueCoroutine = StartCoroutine(PlayBackToBackQueue());

            return;
        }

        for (int i = 0; i < events.Length; i++)
        {
            if (playedEvents.Contains(i))
                continue;

            TtsAudioScheduleEvent e = events[i];
            if (t >= e.start_sec)
            {
                PlayEvent(e);
                playedEvents.Add(i);
            }
        }

        UpdateVideoDucking();
    }

    private IEnumerator PlayBackToBackQueue()
    {
        queueIndex = 0;

        while (queueIndex < events.Length)
        {
            TtsAudioScheduleEvent e = events[queueIndex];
            AudioClip clip = PlayEvent(e);
            if (clip != null)
                yield return new WaitForSeconds(clip.length + gapBetweenLinesSec);

            queueIndex++;
        }

        queueCoroutine = null;
    }

    private void StopQueue()
    {
        if (queueCoroutine != null)
        {
            StopCoroutine(queueCoroutine);
            queueCoroutine = null;
        }

        queueIndex = -1;
    }

    private AudioClip PlayEvent(TtsAudioScheduleEvent e)
    {
        if (!clipCache.TryGetValue(e.audio_clip, out AudioClip clip) || clip == null)
        {
            clip = ScheduleAudioLoader.LoadClip(e.audio_clip);
            if (clip == null)
            {
                Debug.LogError($"[TTS] 播放失败，缺少 mp3: {e.audio_clip} | 文案: {e.text}");
                return null;
            }

            clipCache[e.audio_clip] = clip;
        }

        float vol = Mathf.Clamp01(e.volume);
        ttsAudioSource.PlayOneShot(clip, vol);

        float duckDuration = Mathf.Max(clip.length, e.duration_sec) + duckVideoTailSec;

        if (e.duck_video && videoAudioSource != null)
        {
            videoAudioSource.volume = originalVideoVolume * duckVideoVolume;
            duckReleaseTimer = Mathf.Max(duckReleaseTimer, duckDuration);
        }

        if (crowdController != null)
            crowdController.DuckCrowd(duckCrowdVolume, duckDuration);

        if (logPlayedLines)
        {
            string tag = string.Equals(e.speaker_gender, "female", StringComparison.OrdinalIgnoreCase) ? "[女声]" : "[男声]";
            Debug.Log($"[TTS {e.start_sec:F1}s] {tag} ▶ {e.text}  ({e.audio_clip})");
        }

        return clip;
    }

    private void UpdateVideoDucking()
    {
        if (videoAudioSource == null || duckReleaseTimer <= 0f)
            return;

        duckReleaseTimer -= Time.deltaTime;
        if (duckReleaseTimer <= 0f)
            videoAudioSource.volume = originalVideoVolume;
    }
}
