using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Video;

/// <summary>
/// 读取 crowd_audio_schedule.json，按 tts_mix_type 推荐的环境音在四周 AudioSource 播放。
/// 同一方向多段重叠时取 volume 最高的一段（底噪 0.22，TTS 窗口内叠加上层 0.45+）。
/// </summary>
[DefaultExecutionOrder(200)]
public class CrowdAudioScheduleController : MonoBehaviour
{
    [Header("时间轴")]
    public VideoPlayer videoPlayer;
    public string scheduleFileName = "crowd_audio_schedule.json";
    public bool onlyWhenVideoPlaying = true;

    [Header("四方向音源")]
    public AudioSource sourceFront;
    public AudioSource sourceBack;
    public AudioSource sourceLeft;
    public AudioSource sourceRight;

    [Header("clip 映射（key 对应 JSON 里的 clip 字段）")]
    public CrowdClipBinding[] clipBindings;

    CrowdAudioScheduleFile schedule;
    readonly Dictionary<string, AudioClip> clipMap = new();
    readonly Dictionary<string, AudioSource> dirSources = new();
    readonly Dictionary<string, AppliedCrowdState> applied = new();
    bool initialized;

    struct AppliedCrowdState
    {
        public AudioClip clip;
        public float volume;
        public bool loop;
    }

    void Awake()
    {
        BuildClipMap();
    }

    void Start()
    {
        StartCoroutine(InitWhenReady());
    }

    IEnumerator InitWhenReady()
    {
        yield return null;
        yield return null;

        CacheDirectionalSources();
        if (videoPlayer == null)
            videoPlayer = FindObjectOfType<VideoPlayer>();
        LoadSchedule();
    }

    void Update()
    {
        if (!initialized || videoPlayer == null || schedule?.events == null)
            return;
        if (onlyWhenVideoPlaying && !videoPlayer.isPlaying)
            return;

        float t = (float)videoPlayer.time;
        foreach (var pair in dirSources)
            ApplyBestEventForSource(pair.Key, pair.Value, t);
    }

    void BuildClipMap()
    {
        clipMap.Clear();
        if (clipBindings == null) return;
        foreach (var b in clipBindings)
        {
            if (b != null && !string.IsNullOrWhiteSpace(b.clipKey) && b.clip != null)
                clipMap[b.clipKey] = b.clip;
        }
    }

    void CacheDirectionalSources()
    {
        if (sourceFront == null)
        {
            var go = GameObject.Find("AudioScource_Font") ?? GameObject.Find("AudioScource_Front");
            if (go != null) sourceFront = go.GetComponent<AudioSource>();
        }
        if (sourceBack == null)
        {
            var go = GameObject.Find("AudioScource_Back");
            if (go != null) sourceBack = go.GetComponent<AudioSource>();
        }
        if (sourceLeft == null)
        {
            var go = GameObject.Find("AudioScource_Left");
            if (go != null) sourceLeft = go.GetComponent<AudioSource>();
        }
        if (sourceRight == null)
        {
            var go = GameObject.Find("AudioScource_Right");
            if (go != null) sourceRight = go.GetComponent<AudioSource>();
        }

        RegisterDir("front", sourceFront);
        RegisterDir("back", sourceBack);
        RegisterDir("left", sourceLeft);
        RegisterDir("right", sourceRight);
    }

    void RegisterDir(string key, AudioSource src)
    {
        if (src == null) return;
        src.loop = true;
        src.playOnAwake = false;
        dirSources[key] = src;
    }

    void LoadSchedule()
    {
        string path = Path.Combine(Application.streamingAssetsPath, scheduleFileName.Replace('\\', '/'));
        if (!File.Exists(path))
        {
            Debug.LogError($"[CrowdAudio] 未找到：{path}，请先运行 Tools/generate_audio_schedules.py");
            return;
        }

        schedule = JsonUtility.FromJson<CrowdAudioScheduleFile>(File.ReadAllText(path, System.Text.Encoding.UTF8));
        if (schedule?.events == null || schedule.events.Length == 0)
        {
            Debug.LogError("[CrowdAudio] crowd_audio_schedule.json 无 events。");
            return;
        }

        initialized = true;
        Debug.Log($"[CrowdAudio] 加载 {schedule.events.Length} 条环境音窗口（覆盖 10 分钟）。");
    }

    void ApplyBestEventForSource(string dirKey, AudioSource src, float t)
    {
        CrowdAudioEvent best = null;
        float bestVol = -1f;

        foreach (var ev in schedule.events)
        {
            if (t < ev.start_sec || t > ev.stop_sec)
                continue;
            if (!TargetsDir(ev, dirKey))
                continue;
            if (!clipMap.ContainsKey(ev.clip))
                continue;
            if (ev.volume > bestVol)
            {
                bestVol = ev.volume;
                best = ev;
            }
        }

        string stateKey = src.GetInstanceID().ToString();
        if (best == null)
        {
            if (applied.ContainsKey(stateKey))
            {
                src.Stop();
                applied.Remove(stateKey);
            }
            return;
        }

        var clip = clipMap[best.clip];
        if (applied.TryGetValue(stateKey, out var prev) &&
            prev.clip == clip &&
            Mathf.Approximately(prev.volume, best.volume) &&
            prev.loop == best.loop &&
            src.isPlaying)
            return;

        src.clip = clip;
        src.volume = best.volume;
        src.loop = best.loop;
        src.Play();
        applied[stateKey] = new AppliedCrowdState { clip = clip, volume = best.volume, loop = best.loop };
    }

    static bool TargetsDir(CrowdAudioEvent ev, string dirKey)
    {
        if (ev.targets == null || ev.targets.Length == 0)
            return true;
        foreach (var target in ev.targets)
        {
            if (target == dirKey)
                return true;
        }
        return false;
    }
}
