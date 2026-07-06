using System;
using UnityEngine;
using UnityEngine.Video;

[Serializable]
public class AtmosphereLightScheduleFile
{
    public string default_mode = "neutral";
    public AtmosphereLightScheduleEvent[] events;
}

[Serializable]
public class AtmosphereLightScheduleEvent
{
    public string id;
    public float start_sec;
    public float stop_sec;
    public string mode;
    public string note;
}

public class AtmosphereLightScheduleController : MonoBehaviour
{
    [Header("JSON 路径（相对 StreamingAssets）")]
    public string jsonRelativePath = "AudioData/atmosphere_light_schedule_1min.json";

    [Header("引用")]
    public VideoPlayer videoPlayer;
    public AtmosphereLightController atmosphereController;
    public bool onlyWhenVideoPlaying = true;

    private AtmosphereLightScheduleEvent[] events = Array.Empty<AtmosphereLightScheduleEvent>();
    private AtmosphereMode defaultMode = AtmosphereMode.Neutral;
    private int activeIndex = -1;

    private void Reset()
    {
        videoPlayer = FindObjectOfType<VideoPlayer>();
        atmosphereController = FindObjectOfType<AtmosphereLightController>();
    }

    private void Start()
    {
        LoadSchedule();
    }

    public void LoadSchedule()
    {
        AtmosphereLightScheduleFile file = StreamingAssetsReader.ReadJson<AtmosphereLightScheduleFile>(jsonRelativePath);
        if (file == null || file.events == null)
        {
            Debug.LogWarning($"AtmosphereLightScheduleController: failed to load {jsonRelativePath}");
            return;
        }

        events = file.events;
        defaultMode = ParseMode(file.default_mode);
        activeIndex = -1;
        Debug.Log($"AtmosphereLightScheduleController: loaded {events.Length} light events.");
    }

    private void Update()
    {
        if (videoPlayer == null || atmosphereController == null || events.Length == 0)
            return;

        if (onlyWhenVideoPlaying && !videoPlayer.isPlaying)
            return;

        float t = (float)videoPlayer.time;
        int bestIndex = -1;

        for (int i = 0; i < events.Length; i++)
        {
            AtmosphereLightScheduleEvent e = events[i];
            if (t >= e.start_sec && t <= e.stop_sec)
                bestIndex = i;
        }

        if (bestIndex == activeIndex)
            return;

        activeIndex = bestIndex;

        if (activeIndex < 0)
        {
            atmosphereController.SetMode(defaultMode);
            return;
        }

        AtmosphereLightScheduleEvent current = events[activeIndex];
        AtmosphereMode mode = ParseMode(current.mode);
        if (mode == AtmosphereMode.Goal)
            atmosphereController.TriggerGoal();
        else
            atmosphereController.SetMode(mode);
    }

    private static AtmosphereMode ParseMode(string mode)
    {
        if (string.IsNullOrEmpty(mode))
            return AtmosphereMode.Neutral;

        switch (mode.ToLowerInvariant())
        {
            case "excited":
                return AtmosphereMode.Excited;
            case "tension":
                return AtmosphereMode.Tension;
            case "goal":
                return AtmosphereMode.Goal;
            default:
                return AtmosphereMode.Neutral;
        }
    }
}
