using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Video;

[DefaultExecutionOrder(200)]
public class AudioDanmakuController : MonoBehaviour
{
    [Header("Timeline")]
    public VideoPlayer videoPlayer;
    public string scheduleFileName = "audio_schedule.json";
    public bool onlyWhenVideoPlaying = true;
    public float seekResetThresholdSeconds = 0.35f;

    [Header("Spatial Anchors")]
    public Transform anchorLeft;
    public Transform anchorRight;
    public Transform anchorFront;
    public Transform anchorBack;

    [Header("Video Volume")]
    public AudioSource videoCommentarySource;
    [Range(0.05f, 1f)] public float duckVideoVolume = 0.18f;
    public bool affectVideoVolume = false;
    public bool restoreVideoVolumeOnStop = true;

    [Header("TTS Playback")]
    [Tooltip("Master volume multiplier for all audio danmaku.")]
    [Range(0.1f, 5f)] public float ttsPlaybackGain = 2.2f;
    [Range(0.5f, 5f)] public float maxEventVolumeScale = 4f;
    public bool allowOverlap = true;
    public int maxVoicesPerAnchor = 4;

    [Header("TTS Role Volume Multipliers")]
    [Range(0f, 3f)] public float amusedFanVolume = 1f;
    [Range(0f, 3f)] public float tenseFanVolume = 1f;
    [Range(0f, 3f)] public float chantFanVolume = 1f;
    [Range(0f, 3f)] public float excitedFanVolume = 1f;
    [Range(0f, 3f)] public float neutralFanVolume = 1f;

    [Header("TTS Spatial Mix")]
    [Range(0f, 1f)] public float ttsSpatialBlend = 0.85f;
    [Range(0.1f, 20f)] public float ttsMinDistance = 6f;
    [Range(1f, 100f)] public float ttsMaxDistance = 40f;

    [Header("Loading")]
    public bool preloadOnStart;
    public int maxConcurrentPreloads = 2;

    private AudioScheduleFile schedule;
    private readonly Dictionary<string, AudioClip> clipCache = new Dictionary<string, AudioClip>();
    private readonly HashSet<string> loadingPaths = new HashSet<string>();
    private readonly Dictionary<string, List<AudioSource>> anchorSources = new Dictionary<string, List<AudioSource>>();
    private int nextEventIndex;
    private int timelineVersion;
    private float originalVideoVolume = 1f;
    private int activeDuckCount;
    private bool initialized;
    private double lastVideoTime = -1d;

    private void Start()
    {
        StartCoroutine(InitWhenReady());
    }

    private IEnumerator InitWhenReady()
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

        if (preloadOnStart && schedule != null && schedule.events != null)
            StartCoroutine(PreloadClipsGradually());
    }

    private void Update()
    {
        if (!initialized || videoPlayer == null || schedule == null || schedule.events == null)
            return;

        ApplyTtsSourceSettings();

        float t = (float)videoPlayer.time;
        if (ShouldResetForSeek(t))
            ResetTimelineTo(t, true);
        lastVideoTime = videoPlayer.time;

        if (onlyWhenVideoPlaying && !videoPlayer.isPlaying)
            return;

        while (nextEventIndex < schedule.events.Length && schedule.events[nextEventIndex].start_sec <= t)
        {
            AudioScheduleEvent ev = schedule.events[nextEventIndex];
            if (t <= ev.end_sec + 0.25f)
                StartCoroutine(PlayEvent(ev, timelineVersion));
            nextEventIndex++;
        }
    }

    private bool ShouldResetForSeek(float videoTime)
    {
        if (lastVideoTime < 0d)
            return false;

        return Mathf.Abs(videoTime - (float)lastVideoTime) > seekResetThresholdSeconds;
    }

    public void NotifyVideoSeek(float timeSeconds)
    {
        if (!initialized || schedule == null || schedule.events == null)
            return;

        ResetTimelineTo(timeSeconds, true);
        lastVideoTime = timeSeconds;
    }

    private void ResetTimelineTo(float timeSeconds, bool stopPlayingVoices)
    {
        timelineVersion++;
        nextEventIndex = FindFirstIndexAtOrAfter(timeSeconds);
        if (stopPlayingVoices)
            StopAllTtsVoices();
    }

    private void StopAllTtsVoices()
    {
        foreach (KeyValuePair<string, List<AudioSource>> pair in anchorSources)
        {
            List<AudioSource> sources = pair.Value;
            if (sources == null)
                continue;

            for (int i = 0; i < sources.Count; i++)
            {
                if (sources[i] != null)
                    sources[i].Stop();
            }
        }

        activeDuckCount = 0;
        if (affectVideoVolume && restoreVideoVolumeOnStop && videoCommentarySource != null)
            videoCommentarySource.volume = originalVideoVolume;
    }

    private int FindFirstIndexAtOrAfter(float timeSeconds)
    {
        if (schedule == null || schedule.events == null)
            return 0;

        int low = 0;
        int high = schedule.events.Length;
        while (low < high)
        {
            int mid = low + (high - low) / 2;
            if (schedule.events[mid].start_sec < timeSeconds)
                low = mid + 1;
            else
                high = mid;
        }
        return low;
    }

    private void EnsureAnchors()
    {
        if (anchorFront == null)
        {
            GameObject front = GameObject.Find("AudioScource_Font");
            if (front == null)
                front = GameObject.Find("AudioScource_Front");
            anchorFront = GetOrCreateTtsAnchor(front != null ? front.transform : null, "TTSAnchor_Front");
        }

        if (anchorBack == null)
        {
            GameObject back = GameObject.Find("AudioScource_Back");
            anchorBack = GetOrCreateTtsAnchor(back != null ? back.transform : null, "TTSAnchor_Back");
        }

        if (anchorLeft == null)
        {
            GameObject left = GameObject.Find("AudioScource_Left");
            anchorLeft = GetOrCreateTtsAnchor(left != null ? left.transform : null, "TTSAnchor_Left");
        }

        if (anchorRight == null)
        {
            GameObject right = GameObject.Find("AudioScource_Right");
            anchorRight = GetOrCreateTtsAnchor(right != null ? right.transform : null, "TTSAnchor_Right");
        }

        RegisterAnchor("seat_front", anchorFront);
        RegisterAnchor("seat_back", anchorBack);
        RegisterAnchor("seat_left", anchorLeft);
        RegisterAnchor("seat_right", anchorRight);
    }

    private Transform GetOrCreateTtsAnchor(Transform reference, string objectName)
    {
        GameObject existing = GameObject.Find(objectName);
        if (existing != null)
            return existing.transform;

        GameObject go = new GameObject(objectName);
        if (reference != null)
        {
            go.transform.SetParent(reference.parent, true);
            go.transform.position = reference.position;
            go.transform.rotation = reference.rotation;
        }
        return go.transform;
    }

    private void RegisterAnchor(string key, Transform anchor)
    {
        if (anchor == null)
            return;

        if (!anchorSources.ContainsKey(key))
            anchorSources[key] = new List<AudioSource>();

        AudioSource src = anchor.GetComponent<AudioSource>();
        if (src == null)
            src = anchor.gameObject.AddComponent<AudioSource>();

        ConfigureTtsSource(src);
        anchorSources[key].Add(src);
    }

    private void ConfigureTtsSource(AudioSource src)
    {
        src.playOnAwake = false;
        src.loop = false;
        src.volume = 1f;
        src.spatialBlend = ttsSpatialBlend;
        src.dopplerLevel = 0f;
        src.priority = 16;
        src.minDistance = Mathf.Max(0.1f, ttsMinDistance);
        src.maxDistance = Mathf.Max(src.minDistance + 0.1f, ttsMaxDistance);
        src.rolloffMode = AudioRolloffMode.Logarithmic;
    }

    private void ApplyTtsSourceSettings()
    {
        foreach (KeyValuePair<string, List<AudioSource>> pair in anchorSources)
        {
            List<AudioSource> sources = pair.Value;
            if (sources == null)
                continue;

            for (int i = 0; i < sources.Count; i++)
            {
                if (sources[i] != null)
                    ConfigureTtsSource(sources[i]);
            }
        }
    }

    private void LoadSchedule()
    {
        string path = Path.Combine(Application.streamingAssetsPath, scheduleFileName.Replace('\\', '/'));
        if (!File.Exists(path))
        {
            Debug.LogError("[AudioDanmaku] Schedule file not found: " + path);
            return;
        }

        schedule = JsonUtility.FromJson<AudioScheduleFile>(File.ReadAllText(path, System.Text.Encoding.UTF8));
        if (schedule == null || schedule.events == null || schedule.events.Length == 0)
        {
            Debug.LogError("[AudioDanmaku] Schedule has no events: " + path);
            return;
        }

        if (schedule.duck_video_volume > 0f)
            duckVideoVolume = schedule.duck_video_volume;

        System.Array.Sort(schedule.events, (a, b) => a.start_sec.CompareTo(b.start_sec));
        if (videoPlayer != null)
            nextEventIndex = FindFirstIndexAtOrAfter((float)videoPlayer.time);

        initialized = true;
        Debug.Log("[AudioDanmaku] Loaded " + schedule.events.Length + " emotion TTS events.");
    }

    private IEnumerator PreloadClipsGradually()
    {
        int inFlight = 0;
        for (int i = 0; i < schedule.events.Length; i++)
        {
            string rel = schedule.events[i].audio_clip;
            if (string.IsNullOrEmpty(rel) || clipCache.ContainsKey(rel))
                continue;

            while (inFlight >= Mathf.Max(1, maxConcurrentPreloads))
                yield return null;

            inFlight++;
            yield return LoadClip(rel);
            inFlight--;
            yield return null;
        }
    }

    private IEnumerator LoadClip(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
            yield break;

        AudioClip cached;
        if (clipCache.TryGetValue(relativePath, out cached) && cached != null)
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
            Debug.LogWarning("[AudioDanmaku] Missing audio clip: " + path);
            yield break;
        }

        loadingPaths.Add(relativePath);
        string url = "file:///" + path.Replace('\\', '/');
        AudioType audioType = GuessAudioType(path);
        UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(url, audioType);
        DownloadHandlerAudioClip handler = req.downloadHandler as DownloadHandlerAudioClip;
        if (handler != null)
            handler.streamAudio = true;

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning("[AudioDanmaku] Failed to load " + relativePath + ": " + req.error);
            loadingPaths.Remove(relativePath);
            req.Dispose();
            yield break;
        }

        clipCache[relativePath] = DownloadHandlerAudioClip.GetContent(req);
        loadingPaths.Remove(relativePath);
        req.Dispose();
    }

    private static AudioType GuessAudioType(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".wav")
            return AudioType.WAV;
        if (ext == ".ogg")
            return AudioType.OGGVORBIS;
        if (ext == ".mp3")
            return AudioType.MPEG;
        return AudioType.UNKNOWN;
    }

    private IEnumerator PlayEvent(AudioScheduleEvent ev, int eventTimelineVersion)
    {
        if (ev == null || string.IsNullOrEmpty(ev.audio_clip))
            yield break;

        AudioClip clip;
        if (!clipCache.TryGetValue(ev.audio_clip, out clip) || clip == null)
        {
            yield return LoadClip(ev.audio_clip);
            if (!clipCache.TryGetValue(ev.audio_clip, out clip) || clip == null)
                yield break;
        }

        if (eventTimelineVersion != timelineVersion)
            yield break;

        AudioSource src = GetSourceForEvent(ev);
        if (src == null)
            yield break;

        if (ev.duck_video && affectVideoVolume)
            BeginDuck();

        float eventVolume = Mathf.Clamp(ev.volume * ttsPlaybackGain * GetRoleVolumeMultiplier(ev.speaker_role), 0f, maxEventVolumeScale);
        src.volume = 1f;
        if (!allowOverlap || !ev.allow_overlap)
            src.Stop();
        src.PlayOneShot(clip, eventVolume);

        float wait = ev.duration_sec > 0f ? ev.duration_sec : clip.length;
        yield return new WaitForSeconds(Mathf.Max(0.05f, wait));

        if (ev.duck_video && affectVideoVolume)
            EndDuck();
    }

    private AudioSource GetSourceForEvent(AudioScheduleEvent ev)
    {
        string key = string.IsNullOrEmpty(ev.spatial_anchor) ? "seat_front" : ev.spatial_anchor;
        List<AudioSource> sources;
        if (!anchorSources.TryGetValue(key, out sources) || sources.Count == 0)
            anchorSources.TryGetValue("seat_front", out sources);

        if (sources == null || sources.Count == 0)
            return null;

        for (int i = 0; i < sources.Count; i++)
        {
            if (!sources[i].isPlaying)
                return sources[i];
        }

        if (sources.Count < Mathf.Max(1, maxVoicesPerAnchor))
        {
            AudioSource extra = sources[0].gameObject.AddComponent<AudioSource>();
            ConfigureTtsSource(extra);
            sources.Add(extra);
            return extra;
        }

        return sources[0];
    }

    private float GetRoleVolumeMultiplier(string speakerRole)
    {
        switch (speakerRole)
        {
            case "amused_fan":
                return amusedFanVolume;
            case "tense_fan":
                return tenseFanVolume;
            case "chant_fan":
                return chantFanVolume;
            case "excited_fan":
                return excitedFanVolume;
            default:
                return neutralFanVolume;
        }
    }

    private void BeginDuck()
    {
        if (!affectVideoVolume)
            return;

        activeDuckCount++;
        if (videoCommentarySource != null)
            videoCommentarySource.volume = Mathf.Min(originalVideoVolume, duckVideoVolume);
    }

    private void EndDuck()
    {
        if (!affectVideoVolume)
            return;

        activeDuckCount = Mathf.Max(0, activeDuckCount - 1);
        if (activeDuckCount == 0 && videoCommentarySource != null)
            videoCommentarySource.volume = originalVideoVolume;
    }

    private void OnDisable()
    {
        if (affectVideoVolume && restoreVideoVolumeOnStop && videoCommentarySource != null)
            videoCommentarySource.volume = originalVideoVolume;
        activeDuckCount = 0;
    }
}
