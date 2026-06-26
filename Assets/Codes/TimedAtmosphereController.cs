using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;

[Serializable]
public class TimedAtmosphereEvent
{
    [Tooltip("从视频第几秒切换氛围")]
    public float startTime;

    [Tooltip("到第几秒结束；-1 表示一直持续到下一个事件")]
    public float stopTime = -1f;

    public AtmosphereMode mode = AtmosphereMode.Neutral;

    [Tooltip("备注，例如：67秒进球")]
    public string note;
}

public class TimedAtmosphereController : MonoBehaviour
{
    public VideoPlayer videoPlayer;
    public AtmosphereLightController atmosphereController;
    public bool onlyWhenVideoPlaying = true;

    public List<TimedAtmosphereEvent> events = new List<TimedAtmosphereEvent>();

    private int activeIndex = -1;

    private void Reset()
    {
        videoPlayer = FindObjectOfType<VideoPlayer>();
        atmosphereController = FindObjectOfType<AtmosphereLightController>();
    }

    private void Update()
    {
        if (videoPlayer == null || atmosphereController == null || events == null || events.Count == 0)
            return;

        if (onlyWhenVideoPlaying && !videoPlayer.isPlaying)
            return;

        float t = (float)videoPlayer.time;
        int bestIndex = -1;

        for (int i = 0; i < events.Count; i++)
        {
            TimedAtmosphereEvent e = events[i];
            bool inWindow = t >= e.startTime && (e.stopTime < 0f || t <= e.stopTime);
            if (inWindow)
                bestIndex = i;
        }

        if (bestIndex == activeIndex)
            return;

        activeIndex = bestIndex;
        if (activeIndex < 0)
            return;

        TimedAtmosphereEvent current = events[activeIndex];
        if (current.mode == AtmosphereMode.Goal)
            atmosphereController.TriggerGoal();
        else
            atmosphereController.SetMode(current.mode);
    }
}
