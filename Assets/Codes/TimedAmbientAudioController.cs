using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;

[Serializable]
public class TimedAudioEvent
{
    [Tooltip("要控制的音源，例如 AudioSource_Front")]
    public AudioSource audioSource;

    [Tooltip("从视频第几秒开始播放")]
    public float startTime;

    [Tooltip("到第几秒停止；-1 表示不自动停止（配合 Loop 使用）")]
    public float stopTime = -1f;

    [Tooltip("进入时间段时是否从头播放")]
    public bool restartOnEnter = true;

    [Tooltip("仅用于 Inspector 备注，例如：进球欢呼")]
    public string note;
}

public class TimedAmbientAudioController : MonoBehaviour
{
    [Header("时间轴参考（通常拖 screen 上的 VideoPlayer）")]
    public VideoPlayer videoPlayer;

    [Header("是否在视频播放时才触发")]
    public bool onlyWhenVideoPlaying = true;

    [Header("每个音源的播放时间表")]
    public List<TimedAudioEvent> events = new List<TimedAudioEvent>();

    private readonly HashSet<int> triggeredStarts = new HashSet<int>();

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

        for (int i = 0; i < events.Count; i++)
        {
            TimedAudioEvent e = events[i];
            if (e.audioSource == null)
                continue;

            bool inWindow = t >= e.startTime && (e.stopTime < 0f || t <= e.stopTime);

            if (inWindow)
            {
                if (e.restartOnEnter && !triggeredStarts.Contains(i))
                {
                    e.audioSource.Stop();
                    e.audioSource.Play();
                    triggeredStarts.Add(i);
                }
                else if (!e.audioSource.isPlaying)
                {
                    e.audioSource.Play();
                }
            }
            else
            {
                if (e.stopTime >= 0f && t > e.stopTime && e.audioSource.isPlaying)
                    e.audioSource.Stop();

                if (t < e.startTime || (e.stopTime >= 0f && t > e.stopTime))
                    triggeredStarts.Remove(i);
            }
        }
    }

    public void ResetAllTriggers()
    {
        triggeredStarts.Clear();
        foreach (TimedAudioEvent e in events)
        {
            if (e.audioSource != null)
                e.audioSource.Stop();
        }
    }
}
