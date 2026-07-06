using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;

[Serializable]
public class ParticleScheduleFile
{
    public ParticleScheduleEvent[] events;
}

[Serializable]
public class ParticleScheduleEvent
{
    public string id;
    public float start_sec;
    public string effect = "goal_burst";
    public string burst_id;
    public string note;
}

[Serializable]
public class ParticleEffectBinding
{
    public string effectId;
    public ParticleSystem system;
}

public class ParticleScheduleController : MonoBehaviour
{
    [Header("JSON 路径（相对 StreamingAssets）")]
    public string jsonRelativePath = "AudioData/particle_schedule_1min.json";

    [Header("引用")]
    public VideoPlayer videoPlayer;
    public ParticleSystem particleSystem;
    public bool onlyWhenVideoPlaying = true;

    [Header("多特效（留空则运行时从 template 克隆）")]
    public ParticleSystem templateSystem;
    public ParticleEffectBinding[] effectBindings = Array.Empty<ParticleEffectBinding>();

    private ParticleScheduleEvent[] events = Array.Empty<ParticleScheduleEvent>();
    private readonly Dictionary<string, ParticleSystem> effectLookup = new Dictionary<string, ParticleSystem>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<int> triggered = new HashSet<int>();
    private double lastVideoTime = -1d;

    private void Reset()
    {
        videoPlayer = FindObjectOfType<VideoPlayer>();
        particleSystem = FindObjectOfType<ParticleSystem>();
        templateSystem = particleSystem;
    }

    private void Start()
    {
        if (templateSystem == null)
            templateSystem = particleSystem;

        PrepareTemplate(templateSystem);
        BuildEffectLookup();

        LoadSchedule();
    }

    private static void PrepareTemplate(ParticleSystem ps)
    {
        if (ps == null)
            return;

        var main = ps.main;
        main.playOnAwake = false;
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void BuildEffectLookup()
    {
        effectLookup.Clear();

        foreach (ParticleEffectBinding binding in effectBindings)
        {
            if (binding?.system == null || string.IsNullOrEmpty(binding.effectId))
                continue;

            PrepareTemplate(binding.system);
            effectLookup[binding.effectId] = binding.system;
        }

        if (templateSystem == null)
            return;

        CreateRuntimeEffect("goal_burst", new Color(1f, 0.85f, 0.1f), 0.35f, 6f, 80);
        CreateRuntimeEffect("confetti", new Color(0.2f, 0.9f, 0.4f), 0.22f, 5f, 120);
        CreateRuntimeEffect("spark_blue", new Color(0.45f, 0.75f, 1f), 0.18f, 7f, 60);
        CreateRuntimeEffect("tension_red", new Color(1f, 0.25f, 0.2f), 0.28f, 5.5f, 70);
        CreateRuntimeEffect("chant_green", new Color(0.3f, 1f, 0.45f), 0.25f, 4.5f, 90);
        CreateRuntimeEffect("dispute_flash", new Color(1f, 0.95f, 0.95f), 0.3f, 8f, 50);
        CreateRuntimeEffect("finale_cascade", new Color(1f, 0.95f, 0.6f), 0.4f, 6.5f, 140);
        CreateRuntimeEffect("buildup_pulse", new Color(0.9f, 0.55f, 1f), 0.2f, 4f, 65);
        CreateRuntimeEffect("counter_rush", new Color(0.35f, 0.55f, 1f), 0.26f, 7.5f, 75);

        if (!effectLookup.ContainsKey("default"))
            effectLookup["default"] = templateSystem;
    }

    private void CreateRuntimeEffect(string effectId, Color color, float size, float speed, short burstCount)
    {
        if (effectLookup.ContainsKey(effectId))
            return;

        ParticleSystem ps = Instantiate(templateSystem, templateSystem.transform.parent);
        ps.name = "PS_" + effectId;
        ApplyPreset(ps, color, size, speed, burstCount);
        effectLookup[effectId] = ps;
    }

    private static void ApplyPreset(ParticleSystem ps, Color color, float size, float speed, short burstCount)
    {
        var main = ps.main;
        main.startColor = color;
        main.startSize = size;
        main.startSpeed = speed;
        main.loop = false;
        main.playOnAwake = false;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, burstCount) });

        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    public void LoadSchedule()
    {
        ParticleScheduleFile file = StreamingAssetsReader.ReadJson<ParticleScheduleFile>(jsonRelativePath);
        if (file == null || file.events == null)
        {
            Debug.LogWarning($"ParticleScheduleController: failed to load {jsonRelativePath}");
            return;
        }

        events = file.events;
        triggered.Clear();
        Debug.Log($"ParticleScheduleController: loaded {events.Length} particle events, effects={effectLookup.Count}.");
    }

    private void Update()
    {
        if (videoPlayer == null || events.Length == 0)
            return;

        if (onlyWhenVideoPlaying && !videoPlayer.isPlaying)
            return;

        float t = (float)videoPlayer.time;

        if (lastVideoTime >= 0d && t + 0.05f < lastVideoTime)
            triggered.Clear();

        lastVideoTime = videoPlayer.time;

        for (int i = 0; i < events.Length; i++)
        {
            if (triggered.Contains(i))
                continue;

            ParticleScheduleEvent e = events[i];
            if (t >= e.start_sec)
            {
                PlayEffect(e);
                triggered.Add(i);
            }
        }
    }

    private void PlayEffect(ParticleScheduleEvent e)
    {
        string effectId = string.IsNullOrEmpty(e.effect) ? "goal_burst" : e.effect;
        if (!effectLookup.TryGetValue(effectId, out ParticleSystem ps) || ps == null)
        {
            ps = templateSystem != null ? templateSystem : particleSystem;
            if (ps == null)
                return;
        }

        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.Play();
        Debug.Log($"[Particle {e.start_sec:F1}s] {effectId} | {e.note}");
    }
}
