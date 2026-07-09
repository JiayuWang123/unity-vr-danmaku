using UnityEngine;
using UnityEngine.Video;

/// <summary>
/// 退出 Play 或物体禁用时停止解码，避免 VideoPlayer 在后台继续占用内存。
/// 挂在带 VideoPlayer 的 screen 物体上即可。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(VideoPlayer))]
public class VideoPlayerCleanup : MonoBehaviour
{
    VideoPlayer videoPlayer;

    void Awake()
    {
        videoPlayer = GetComponent<VideoPlayer>();
    }

    void OnDisable()
    {
        if (!Application.isPlaying || videoPlayer == null)
            return;

        if (videoPlayer.isPlaying)
            videoPlayer.Stop();
    }
}
