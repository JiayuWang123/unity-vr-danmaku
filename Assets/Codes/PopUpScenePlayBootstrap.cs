using System.Collections;
using UnityEngine;
using UnityEngine.Video;
using UnityEngine.XR;

/// <summary>
/// 进入 Play 后：摆正 Editor 相机、自动播放 screen 上的视频。
/// </summary>
public class PopUpScenePlayBootstrap : MonoBehaviour
{
    public VideoPlayer videoPlayer;
    public Transform screen;
    public float cameraDistance = 5f;
    public float prepareTimeoutSec = 20f;
    [Range(0.05f, 1f)] public float videoVolume = 0.28f;

    IEnumerator Start()
    {
        yield return null;

        if (screen == null)
        {
            var screenGo = GameObject.Find("screen");
            if (screenGo != null)
                screen = screenGo.transform;
        }

        if (videoPlayer == null)
            videoPlayer = screen != null ? screen.GetComponent<VideoPlayer>() : FindObjectOfType<VideoPlayer>();

        if (!XRSettings.isDeviceActive)
            SetupEditorCamera();

        if (videoPlayer == null)
        {
            Debug.LogError("[PopUpScenePlayBootstrap] 未找到 VideoPlayer。");
            yield break;
        }

        var audio = videoPlayer.GetComponent<AudioSource>();
        if (audio != null)
            audio.volume = videoVolume;
        videoPlayer.SetDirectAudioVolume(0, videoVolume);

        if (!videoPlayer.isPrepared)
            videoPlayer.Prepare();

        float elapsed = 0f;
        while (!videoPlayer.isPrepared && elapsed < prepareTimeoutSec)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (!videoPlayer.isPrepared)
        {
            Debug.LogError("[PopUpScenePlayBootstrap] 视频 Prepare 超时，请检查 VideoClip 是否存在。");
            yield break;
        }

        videoPlayer.Play();
        Debug.Log($"[PopUpScenePlayBootstrap] 视频已开始播放（时长 {videoPlayer.length:F1}s）。");
    }

    void SetupEditorCamera()
    {
        if (screen == null)
            return;

        var cam = Camera.main;
        if (cam == null)
            return;

        Vector3 target = screen.position;
        Vector3 viewDir = (-screen.forward).normalized;
        if (viewDir.sqrMagnitude < 0.01f)
            viewDir = (target - cam.transform.position).normalized;

        cam.transform.position = target + viewDir * cameraDistance + Vector3.up * 0.3f;

        var viewCtrl = cam.GetComponent<CameraViewController>();
        if (viewCtrl != null)
            viewCtrl.LookAtPoint(target);
        else
            cam.transform.LookAt(target);
    }
}
