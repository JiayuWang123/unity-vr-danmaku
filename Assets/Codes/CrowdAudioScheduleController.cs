using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;

[Serializable]
public class CrowdAudioScheduleFile
{
    public CrowdAudioBedConfig bed;
    public CrowdAudioScheduleEvent[] events;
}

[Serializable]
public class CrowdAudioBedConfig
{
    public string clip = "Audio/正常阶段声音/normal";
    public float volume = 0.22f;
    public bool loop = true;
}

[Serializable]
public class CrowdAudioScheduleEvent
{
    public string id;
    public string burst_id;
    public float start_sec;
    public float stop_sec;
    public string clip;
    public float volume = 1f;
    public string spatial;
    public string[] targets;
    public bool loop;
    public string note;
}

public class CrowdAudioScheduleController : MonoBehaviour
{
    [Header("JSON 路径（相对 StreamingAssets）")]
    public string jsonRelativePath = "AudioData/crowd_audio_schedule_1min.json";

    [Header("时间轴")]
    public VideoPlayer videoPlayer;
    public bool onlyWhenVideoPlaying = true;

    [Header("全程底噪（独立音源，不与 TTS 抢同一个）")]
    public AudioSource bedAudioSource;
    public bool autoCreateBedSource = true;

    [Header("四方向前景音源")]
    public AudioSource sourceFront;
    public AudioSource sourceBack;
    public AudioSource sourceLeft;
    public AudioSource sourceRight;

    private CrowdAudioScheduleEvent[] events = Array.Empty<CrowdAudioScheduleEvent>();
    private CrowdAudioBedConfig bedConfig;
    private readonly HashSet<int> triggeredStarts = new HashSet<int>();
    private readonly Dictionary<AudioSource, float> originalVolumes = new Dictionary<AudioSource, float>();
    private double lastVideoTime = -1d;

    public AudioSource[] AllCrowdSources
    {
        get
        {
            return new[] { bedAudioSource, sourceFront, sourceBack, sourceLeft, sourceRight };
        }
    }

    private void Reset()
    {
        videoPlayer = FindObjectOfType<VideoPlayer>();
        AutoFindDirectionalSources();
    }

    private void Start()
    {
        AutoFindDirectionalSources();
        EnsureBedSource();
        CacheOriginalVolumes();
        LoadSchedule();
        StartBedLayer();
    }

    private void EnsureBedSource()
    {
        if (bedAudioSource != null || !autoCreateBedSource)
            return;

        GameObject go = new GameObject("CrowdBed_AudioSource");
        go.transform.SetParent(transform);
        bedAudioSource = go.AddComponent<AudioSource>();
        bedAudioSource.playOnAwake = false;
        bedAudioSource.loop = true;
        bedAudioSource.spatialBlend = 0f;
        bedAudioSource.volume = 0.28f;
    }

    private void AutoFindDirectionalSources()
    {
        AudioSource[] all = FindObjectsOfType<AudioSource>(true);
        foreach (AudioSource src in all)
        {
            if (src == bedAudioSource || src == null)
                continue;

            string n = src.gameObject.name.ToLowerInvariant();
            if (sourceFront == null && (n.Contains("front") || n.Contains("font")))
                sourceFront = src;
            else if (sourceBack == null && n.Contains("back"))
                sourceBack = src;
            else if (sourceLeft == null && n.Contains("left"))
                sourceLeft = src;
            else if (sourceRight == null && n.Contains("right"))
                sourceRight = src;
        }
    }

    private void CacheOriginalVolumes()
    {
        foreach (AudioSource src in AllCrowdSources)
            RegisterOriginalVolume(src);
    }

    private void RegisterOriginalVolume(AudioSource src)
    {
        if (src != null && !originalVolumes.ContainsKey(src))
            originalVolumes[src] = src.volume;
    }

    public void LoadSchedule()
    {
        CrowdAudioScheduleFile file = StreamingAssetsReader.ReadJson<CrowdAudioScheduleFile>(jsonRelativePath);
        if (file == null)
        {
            Debug.LogWarning($"CrowdAudioScheduleController: failed to load {jsonRelativePath}");
            return;
        }

        bedConfig = file.bed;
        events = file.events ?? Array.Empty<CrowdAudioScheduleEvent>();
        triggeredStarts.Clear();
        Debug.Log($"CrowdAudioScheduleController: loaded bed + {events.Length} overlay events.");
    }

    private void StartBedLayer()
    {
        if (bedAudioSource == null)
            return;

        string clipPath = bedConfig != null ? bedConfig.clip : "Audio/正常阶段声音/normal";
        AudioClip clip = ScheduleAudioLoader.LoadClip(clipPath);
        if (clip == null)
        {
            Debug.LogWarning($"Crowd bed clip not found: {clipPath}");
            return;
        }

        bedAudioSource.clip = clip;
        bedAudioSource.loop = bedConfig == null || bedConfig.loop;
        bedAudioSource.volume = bedConfig != null ? bedConfig.volume : 0.22f;
        bedAudioSource.Play();
    }

    private void Update()
    {
        if (videoPlayer == null)
            return;

        if (onlyWhenVideoPlaying && !videoPlayer.isPlaying)
            return;

        float t = (float)videoPlayer.time;

        if (lastVideoTime >= 0d && t + 0.05f < lastVideoTime)
            triggeredStarts.Clear();

        lastVideoTime = videoPlayer.time;

        for (int i = 0; i < events.Length; i++)
        {
            CrowdAudioScheduleEvent e = events[i];
            bool inWindow = t >= e.start_sec && t <= e.stop_sec;

            if (inWindow)
            {
                if (!triggeredStarts.Contains(i))
                {
                    PlayEvent(e);
                    triggeredStarts.Add(i);
                }
            }
            else if (triggeredStarts.Contains(i))
            {
                StopEventTargets(e);
                triggeredStarts.Remove(i);
            }
        }
    }

    public void DuckCrowd(float multiplier, float durationSec)
    {
        foreach (AudioSource src in AllCrowdSources)
        {
            if (src == null || !originalVolumes.ContainsKey(src))
                continue;

            src.volume = originalVolumes[src] * multiplier;
        }

        CancelInvoke(nameof(RestoreCrowdVolumes));
        Invoke(nameof(RestoreCrowdVolumes), durationSec);
    }

    private void RestoreCrowdVolumes()
    {
        foreach (KeyValuePair<AudioSource, float> pair in originalVolumes)
        {
            if (pair.Key != null)
                pair.Key.volume = pair.Value;
        }
    }

    private void PlayEvent(CrowdAudioScheduleEvent e)
    {
        AudioClip clip = ScheduleAudioLoader.LoadClip(e.clip);
        if (clip == null)
            return;

        foreach (AudioSource src in ResolveTargets(e))
        {
            if (src == null || src == bedAudioSource)
                continue;

            src.clip = clip;
            src.volume = e.volume;
            src.loop = e.loop;
            src.Play();
        }
    }

    private void StopEventTargets(CrowdAudioScheduleEvent e)
    {
        foreach (AudioSource src in ResolveTargets(e))
        {
            if (src == null || src == bedAudioSource)
                continue;

            src.Stop();
            if (originalVolumes.TryGetValue(src, out float vol))
                src.volume = vol;
        }
    }

    private List<AudioSource> ResolveTargets(CrowdAudioScheduleEvent e)
    {
        List<AudioSource> list = new List<AudioSource>();

        if (e.targets == null || e.targets.Length == 0 || string.Equals(e.spatial, "surround", StringComparison.OrdinalIgnoreCase))
        {
            AddIfNotNull(list, sourceFront);
            AddIfNotNull(list, sourceBack);
            AddIfNotNull(list, sourceLeft);
            AddIfNotNull(list, sourceRight);
            return list;
        }

        foreach (string target in e.targets)
        {
            switch (target.ToLowerInvariant())
            {
                case "front": AddIfNotNull(list, sourceFront); break;
                case "back": AddIfNotNull(list, sourceBack); break;
                case "left": AddIfNotNull(list, sourceLeft); break;
                case "right": AddIfNotNull(list, sourceRight); break;
            }
        }

        return list;
    }

    private static void AddIfNotNull(List<AudioSource> list, AudioSource src)
    {
        if (src != null && !list.Contains(src))
            list.Add(src);
    }
}
