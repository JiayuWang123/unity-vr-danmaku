using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Video;

/// <summary>
/// 读取 atmosphere_schedule.json，驱动 AtmosphereLightController 切换 Neutral/Excited/Tension。
/// </summary>
[DefaultExecutionOrder(200)]
public class TimedAtmosphereScheduleController : MonoBehaviour
{
    public VideoPlayer videoPlayer;
    public AtmosphereLightController atmosphereController;
    public string scheduleFileName = "atmosphere_schedule.json";
    public bool onlyWhenVideoPlaying = true;

    AtmosphereScheduleFile schedule;
    int activeIndex = -1;

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
        if (atmosphereController == null)
            atmosphereController = FindObjectOfType<AtmosphereLightController>();
        LoadSchedule();
    }

    void Update()
    {
        if (schedule?.events == null || atmosphereController == null || videoPlayer == null)
            return;
        if (onlyWhenVideoPlaying && !videoPlayer.isPlaying)
            return;

        float t = (float)videoPlayer.time;
        int best = -1;
        for (int i = 0; i < schedule.events.Length; i++)
        {
            var e = schedule.events[i];
            if (t >= e.start_sec && t <= e.stop_sec)
                best = i;
        }

        if (best == activeIndex || best < 0)
            return;

        activeIndex = best;
        var mode = ParseMode(schedule.events[activeIndex].mode);
        atmosphereController.SetMode(mode);
    }

    void LoadSchedule()
    {
        string path = Path.Combine(Application.streamingAssetsPath, scheduleFileName.Replace('\\', '/'));
        if (!File.Exists(path))
        {
            Debug.LogWarning($"[AtmosphereSchedule] 未找到 {path}");
            return;
        }

        schedule = JsonUtility.FromJson<AtmosphereScheduleFile>(
            File.ReadAllText(path, System.Text.Encoding.UTF8));
        Debug.Log($"[AtmosphereSchedule] 加载 {schedule?.events?.Length ?? 0} 条氛围窗口。");
    }

    static AtmosphereMode ParseMode(string mode)
    {
        return mode switch
        {
            "Excited" => AtmosphereMode.Excited,
            "Tension" => AtmosphereMode.Tension,
            "Goal" => AtmosphereMode.Goal,
            _ => AtmosphereMode.Neutral
        };
    }
}
