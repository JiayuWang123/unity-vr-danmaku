using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;

[Serializable]
public class TimedParticleEvent
{
    [Tooltip("要播放的粒子系统，例如 GoalParticles")]
    public ParticleSystem particleSystem;

    [Tooltip("从视频第几秒触发")]
    public float startTime;

    [Tooltip("备注，例如：67秒进球")]
    public string note;
}

public class TimedParticleController : MonoBehaviour
{
    public VideoPlayer videoPlayer;
    public bool onlyWhenVideoPlaying = true;

    [Header("粒子触发时间表")]
    public List<TimedParticleEvent> events = new List<TimedParticleEvent>();

    private readonly HashSet<int> triggered = new HashSet<int>();
    private double lastVideoTime = -1d;

    private void Start()
    {
        if (events == null)
            return;

        foreach (TimedParticleEvent e in events)
        {
            if (e.particleSystem == null)
                continue;

            var main = e.particleSystem.main;
            main.playOnAwake = false;
            e.particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    private void Reset()
    {
        videoPlayer = FindObjectOfType<VideoPlayer>();
    }

    private void Update()
    {
        if (videoPlayer == null || events == null || events.Count == 0)
            return;

        if (onlyWhenVideoPlaying && !videoPlayer.isPlaying)
            return;

        float t = (float)videoPlayer.time;

        if (lastVideoTime >= 0d && t + 0.05f < lastVideoTime)
            triggered.Clear();

        lastVideoTime = videoPlayer.time;

        for (int i = 0; i < events.Count; i++)
        {
            if (triggered.Contains(i))
                continue;

            TimedParticleEvent e = events[i];
            if (e.particleSystem == null)
                continue;

            if (t >= e.startTime)
            {
                e.particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                e.particleSystem.Play();
                triggered.Add(i);
            }
        }
    }
}
