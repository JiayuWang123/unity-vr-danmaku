using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Video;

/// <summary>
/// 根据情绪弹幕密度驱动 skybox 切换：
/// 默认 sky11 → 窗口内 positive 占优且达标时渐变 light；
/// negative 占优且达标时渐变 dark；双方都存在时取条数更多的一方；平局回 sky11。
/// （与 ✦/☁ 粒子共用同一窗口、阈值与强度曲线）
/// </summary>
public class EmotionSkyboxBlendController : MonoBehaviour
{
    [Header("场景引用")]
    public VideoPlayer videoPlayer;
    public Material skyboxBlendMaterial;

    [Header("Skybox 贴图")]
    [Tooltip("默认基础 skybox（sky11）")]
    public Texture baseTexture;
    [Tooltip("positive 占优时混合目标")]
    public Texture lightTexture;
    [Tooltip("negative 占优时混合目标")]
    public Texture darkTexture;

    [Header("情绪 JSON")]
    public string emotionJsonFileName = "classify/emotional_interactions_emotion_from_excel.json";

    [Header("触发条件（滑动时间窗，与 ✦/☁ 粒子相同）")]
    [Tooltip("统计最近多少秒内的情绪弹幕")]
    public float windowSeconds = 14f;
    [Tooltip("窗口内至少多少条 positive 才参与 light 竞争")]
    public int minPositiveToTrigger = 2;
    [Tooltip("窗口内至少多少条 negative 才参与 dark 竞争")]
    public int minNegativeToTrigger = 2;
    [Tooltip("达到此条数时 blend 接近满强度")]
    public int countForFullBlend = 5;

    [Header("渐变时序（秒）")]
    public float fadeInDuration = 2.5f;
    public float fadeOutDuration = 2.5f;

    static readonly int TexAId = Shader.PropertyToID("_TexA");
    static readonly int TexBId = Shader.PropertyToID("_TexB");
    static readonly int BlendId = Shader.PropertyToID("_Blend");

    Material _runtimeMat;
    float _lastBlend = -1f;

    readonly List<EmotionEvent> recentEvents = new();
    readonly List<EmotionRecord> records = new();
    int recordIdx;
    double lastVideoTime = -1d;

    EmotionDensityUtil.SkyboxDominance appliedDominance = EmotionDensityUtil.SkyboxDominance.None;
    float currentBlend;

    [Serializable]
    class EmotionRecord
    {
        public float time;
        public bool isPositive;
    }

    struct EmotionEvent
    {
        public float time;
        public bool isPositive;
    }

    [Serializable]
    class EmotionItemDto
    {
        public string 弹幕内容;
        public float 新视频中的时间;
        public string 正反面情绪;
    }

    [Serializable]
    class EmotionFileDto
    {
        public EmotionItemDto[] items;
    }

    void Awake()
    {
        InitializeSkybox();
    }

    void Reset()
    {
        videoPlayer = FindObjectOfType<VideoPlayer>();
    }

    void Start()
    {
        if (videoPlayer == null)
            videoPlayer = FindObjectOfType<VideoPlayer>();

        if (_runtimeMat == null)
            InitializeSkybox();

        LoadEmotionRecords();
        SeekRecords(videoPlayer != null ? videoPlayer.time : 0d);
    }

    void InitializeSkybox()
    {
        if (skyboxBlendMaterial == null)
            skyboxBlendMaterial = Resources.Load<Material>("Textrue/Materials/SkyboxEmotionBlend");

        if (skyboxBlendMaterial == null || skyboxBlendMaterial.shader == null)
        {
            Debug.LogError("[EmotionSkybox] 缺少 SkyboxEmotionBlend 材质，已回退到 Stadium.skybox。");
            ApplyFallbackStadiumSkybox();
            return;
        }

        _runtimeMat = new Material(skyboxBlendMaterial);
        EnsureTextures();

        if (baseTexture == null)
        {
            Debug.LogError("[EmotionSkybox] sky11 贴图未找到，已回退到 Stadium.skybox。");
            ApplyFallbackStadiumSkybox();
            return;
        }

        _runtimeMat.SetTexture(TexAId, baseTexture);
        ApplyBlend(0f);
        RenderSettings.skybox = _runtimeMat;
        DynamicGI.UpdateEnvironment();
        Debug.Log("[EmotionSkybox] 天空盒已初始化：base=sky11, positive→light / negative→dark。");
    }

    void ApplyFallbackStadiumSkybox()
    {
        Material stadium = Resources.Load<Material>("Textrue/Materials/Stadium");
        if (stadium != null)
        {
            RenderSettings.skybox = stadium;
            DynamicGI.UpdateEnvironment();
        }
    }

    void Update()
    {
        if (_runtimeMat == null || videoPlayer == null)
            return;

        double videoTime = videoPlayer.time;
        if (lastVideoTime >= 0d && videoTime + 0.5 < lastVideoTime)
        {
            SeekRecords(videoTime);
            recentEvents.Clear();
            ResetBlendState();
        }
        lastVideoTime = videoTime;

        if (!videoPlayer.isPrepared)
            return;

        IngestRecordsUpTo(videoTime);
        PruneRecentEvents((float)videoTime);
        UpdateSkyboxBlend(Time.deltaTime);
    }

    void EnsureTextures()
    {
        if (baseTexture == null)
            baseTexture = LoadPanoramaTexture("Textrue/sky11");
        if (lightTexture == null)
            lightTexture = LoadPanoramaTexture("Textrue/light");
        if (darkTexture == null)
            darkTexture = LoadPanoramaTexture("Textrue/dark");
    }

    static Texture LoadPanoramaTexture(string resourcesPath)
    {
        Texture tex = Resources.Load<Texture2D>(resourcesPath);
        if (tex != null)
            return tex;

        return Resources.Load<Texture>(resourcesPath);
    }

    void LoadEmotionRecords()
    {
        records.Clear();
        string path = Path.Combine(Application.streamingAssetsPath, emotionJsonFileName.Replace('\\', '/'));
        if (!File.Exists(path))
        {
            Debug.LogError($"[EmotionSkybox] JSON 未找到: {path}");
            return;
        }

        var file = JsonUtility.FromJson<EmotionFileDto>(
            File.ReadAllText(path, System.Text.Encoding.UTF8).Trim());
        if (file?.items == null)
            return;

        for (int i = 0; i < file.items.Length; i++)
        {
            EmotionItemDto item = file.items[i];
            if (item == null) continue;

            records.Add(new EmotionRecord
            {
                time = item.新视频中的时间,
                isPositive = EmotionDensityUtil.IsPositiveSentiment(item.正反面情绪)
            });
        }

        records.Sort((a, b) => a.time.CompareTo(b.time));
        Debug.Log($"[EmotionSkybox] 加载 {records.Count} 条情绪弹幕。");
    }

    void SeekRecords(double time)
    {
        recordIdx = 0;
        while (recordIdx < records.Count && records[recordIdx].time < (float)time)
            recordIdx++;
    }

    void IngestRecordsUpTo(double videoTime)
    {
        while (recordIdx < records.Count && records[recordIdx].time <= (float)videoTime)
        {
            recentEvents.Add(new EmotionEvent
            {
                time = records[recordIdx].time,
                isPositive = records[recordIdx].isPositive
            });
            recordIdx++;
        }
    }

    void PruneRecentEvents(float videoTime)
    {
        float minTime = videoTime - windowSeconds;
        for (int i = recentEvents.Count - 1; i >= 0; i--)
        {
            if (recentEvents[i].time < minTime)
                recentEvents.RemoveAt(i);
        }
    }

    void CountSentiment(out int positive, out int negative)
    {
        positive = 0;
        negative = 0;
        for (int i = 0; i < recentEvents.Count; i++)
        {
            if (recentEvents[i].isPositive) positive++;
            else negative++;
        }
    }

    void UpdateSkyboxBlend(float deltaTime)
    {
        CountSentiment(out int positive, out int negative);
        EmotionDensityUtil.SkyboxDominance dominance = EmotionDensityUtil.ResolveSkyboxDominance(
            positive, negative, minPositiveToTrigger, minNegativeToTrigger);

        if (dominance != appliedDominance)
        {
            if (appliedDominance != EmotionDensityUtil.SkyboxDominance.None
                && dominance != EmotionDensityUtil.SkyboxDominance.None
                && dominance != appliedDominance)
            {
                currentBlend = 0f;
            }

            appliedDominance = dominance;
            ApplyOverlayTexture(dominance);
        }

        float targetBlend = 0f;
        switch (dominance)
        {
            case EmotionDensityUtil.SkyboxDominance.Positive:
                targetBlend = EmotionDensityUtil.MapCountToIntensity(
                    positive, minPositiveToTrigger, countForFullBlend);
                break;
            case EmotionDensityUtil.SkyboxDominance.Negative:
                targetBlend = EmotionDensityUtil.MapCountToIntensity(
                    negative, minNegativeToTrigger, countForFullBlend);
                break;
        }

        float fadeSpeed = targetBlend > currentBlend
            ? 1f / Mathf.Max(0.01f, fadeInDuration)
            : 1f / Mathf.Max(0.01f, fadeOutDuration);
        currentBlend = Mathf.MoveTowards(currentBlend, targetBlend, fadeSpeed * deltaTime);
        ApplyBlend(currentBlend);
    }

    void ApplyOverlayTexture(EmotionDensityUtil.SkyboxDominance dominance)
    {
        Texture overlay = dominance switch
        {
            EmotionDensityUtil.SkyboxDominance.Positive => lightTexture,
            EmotionDensityUtil.SkyboxDominance.Negative => darkTexture,
            _ => null
        };

        if (overlay != null)
            _runtimeMat.SetTexture(TexBId, overlay);
    }

    void ResetBlendState()
    {
        appliedDominance = EmotionDensityUtil.SkyboxDominance.None;
        currentBlend = 0f;
        ApplyBlend(0f);
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
}
