using System.Collections;
using UnityEngine;
using UnityEngine.Video;

/// <summary>
/// 按 VideoPlayer 时间轴在两张 Panoramic Skybox 之间平滑混合。
/// 默认：9s 起 3s 淡入暗图，12s 起 3s 淡回亮图。
/// </summary>
public class TimedSkyboxBlendController : MonoBehaviour
{
    [Header("视频时间轴")]
    public VideoPlayer videoPlayer;
    public bool onlyWhenVideoPlaying = true;
    public bool autoPlayVideo = false;
    [Min(0)] public int autoPlayDelayFrames = 2;

    [Header("Skybox 混合材质（Skybox/Panoramic Blend）")]
    public Material skyboxBlendMaterial;

    [Header("变暗")]
    [Tooltip("从视频第几秒开始淡入暗图")]
    public float fadeToDarkStartTime = 9f;

    [Tooltip("淡入暗图时长（秒）")]
    public float fadeToDarkDuration = 3f;

    [Header("变亮")]
    [Tooltip("从视频第几秒开始淡回亮图（默认 = 变暗结束时刻）")]
    public float fadeToBrightStartTime = 12f;

    [Tooltip("淡回亮图时长（秒）")]
    public float fadeToBrightDuration = 3f;

    static readonly int BlendId = Shader.PropertyToID("_Blend");

    Material _runtimeMat;
    float _lastBlend = -1f;

    void Reset()
    {
        videoPlayer = FindObjectOfType<VideoPlayer>();
    }

    void Start()
    {
        StartCoroutine(StartSequence());
    }

    IEnumerator StartSequence()
    {
        if (autoPlayVideo && videoPlayer != null)
        {
            for (int i = 0; i < autoPlayDelayFrames; i++)
                yield return null;

            if (!videoPlayer.isPrepared)
                videoPlayer.Prepare();

            while (!videoPlayer.isPrepared)
                yield return null;

            videoPlayer.Play();
        }

        if (skyboxBlendMaterial == null)
        {
            Debug.LogWarning("[TimedSkyboxBlendController] 未指定 skyboxBlendMaterial。");
            yield break;
        }

        _runtimeMat = new Material(skyboxBlendMaterial);
        _runtimeMat.SetFloat(BlendId, 0f);
        RenderSettings.skybox = _runtimeMat;
        ApplyBlend(0f);
    }

    void Update()
    {
        if (_runtimeMat == null)
            return;

        if (videoPlayer == null)
        {
            ApplyBlend(0f);
            return;
        }

        if (onlyWhenVideoPlaying && !videoPlayer.isPlaying)
            return;

        float t = (float)videoPlayer.time;
        ApplyBlend(CalculateBlend(t));
    }

    float CalculateBlend(float videoTime)
    {
        float darkEnd = fadeToDarkStartTime + fadeToDarkDuration;
        float brightEnd = fadeToBrightStartTime + fadeToBrightDuration;

        if (videoTime < fadeToDarkStartTime)
            return 0f;

        if (videoTime < darkEnd)
            return Smooth01((videoTime - fadeToDarkStartTime) / fadeToDarkDuration);

        if (videoTime < fadeToBrightStartTime)
            return 1f;

        if (videoTime < brightEnd)
            return 1f - Smooth01((videoTime - fadeToBrightStartTime) / fadeToBrightDuration);

        return 0f;
    }

    static float Smooth01(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    void ApplyBlend(float blend)
    {
        blend = Mathf.Clamp01(blend);
        if (Mathf.Approximately(blend, _lastBlend))
            return;

        _lastBlend = blend;
        _runtimeMat.SetFloat(BlendId, blend);
    }

    void OnDestroy()
    {
        if (_runtimeMat != null)
            Destroy(_runtimeMat);
    }

    void OnValidate()
    {
        if (fadeToBrightStartTime < fadeToDarkStartTime + fadeToDarkDuration)
            fadeToBrightStartTime = fadeToDarkStartTime + fadeToDarkDuration;
    }
}
