using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Video;

/// <summary>
/// 情绪符号粒子系统。
/// 读取情绪 JSON，通过滑动时间窗统计密度：
///   positive 达标 → star 散落（数量随 positive 条数增加）
///   negative 达标 → rain 散落（数量随 negative 条数增加，可与 star 共存）
///   天空盒在同一窗口内取条数更多的一方切 light / dark。
/// </summary>
public class EmotionIconParticleController : MonoBehaviour
{
    [Header("场景引用")]
    public VideoPlayer videoPlayer;
    [Tooltip("留空则自动在场景中查找")]
    public CurvedDanmakuCloudRig cloudRig;

    [Header("情绪 JSON（与 EmotionSkyboxBlendController 共用同一文件）")]
    public string emotionJsonFileName = "classify/emotional_interactions_emotion_from_excel.json";

    [Header("触发条件（与 EmotionSkybox 对齐）")]
    [Tooltip("统计最近多少秒内的情绪弹幕")]
    public float windowSeconds = 14f;
    [Tooltip("star：窗口内至少多少条 positive")]
    public int minPositiveToTrigger = 2;
    [Tooltip("rain：窗口内至少多少条 negative")]
    public int minNegativeToTrigger = 2;
    [Tooltip("达到此条数时粒子爆发接近满强度")]
    public int countForFullBlend = 5;

    [Header("粒子外观")]
    public Sprite positiveSprite;
    public Sprite negativeSprite;
    [Tooltip("留空 positiveSprite 时从 Resources 加载")]
    public string positiveSpriteResource = "Textrue/star";
    [Tooltip("留空 negativeSprite 时从 Resources 加载")]
    public string negativeSpriteResource = "Textrue/rain";

    [Range(0.002f, 0.06f)]
    [Tooltip("粒子基础世界尺寸（米）；默认很小，像远处飘落的符号")]
    public float baseWorldScale = 0.012f;
    [Range(0f, 0.6f)]
    [Tooltip("根据弹幕长度对尺寸的影响幅度（0 = 全部同尺寸）")]
    public float sizeVariation = 0.25f;
    [Tooltip("图标 Canvas 宽度（像素）。只加宽不改高时增大此值，例如 64 → 96")]
    public float iconCanvasWidth = 64f;
    [Tooltip("图标 Canvas 高度（像素）")]
    public float iconCanvasHeight = 64f;
    public Color positiveColor = new Color(1f, 0.92f, 0.3f, 0.95f);
    public Color negativeColor = new Color(0.55f, 0.78f, 1f, 0.95f);
    public int canvasSortOrder = 230;

    [Header("从天而降")]
    [Tooltip("在 NearEmotion 区域顶边之上，最低生成高度（米）")]
    public float spawnHeightMin = 1.2f;
    [Tooltip("在 NearEmotion 区域顶边之上，最高生成高度（米）")]
    public float spawnHeightMax = 2.8f;
    [Tooltip("星星/雨滴散落的横向范围宽度，占 NearEmotion 曲面层宽度的比例（1=整层，2=两倍宽，最大 3）")]
    [Range(0.05f, 3f)]
    public float spawnRangeWidthFraction = 1f;
    [Tooltip("散落区域左右偏移（-1=靠左，0=居中，1=靠右）")]
    [Range(-1f, 1f)]
    public float spawnRangeCenterOffset = 0f;

    [Header("粒子运动")]
    [Range(1.5f, 8f)] public float lifetime = 3.5f;
    [Range(0.1f, 1.5f)] public float fallSpeed = 0.55f;
    [Range(0f, 0.2f)] public float horizontalDrift = 0.04f;
    [Range(0f, 0.5f)] public float fadeInFraction = 0.15f;
    [Range(0f, 0.5f)] public float fadeOutFraction = 0.35f;

    [Header("爆发节奏（与天空盒 fadeInDuration 建议相同）")]
    [Range(1, 16)] public int burstCount = 8;
    [Tooltip("同一极性两次爆发的间隔（秒）；与天空盒渐变时序对齐")]
    public float burstCooldown = 2.5f;
    [Tooltip("同次爆发内相邻粒子的生成间隔（秒）")]
    public float inBurstGap = 0.06f;
    [Tooltip("窗口内 positive 已达标时，每来一条新 positive 弹幕额外撒 1 颗 star")]
    public bool spawnOnEachPositiveEvent = true;
    [Tooltip("窗口内 negative 已达标时，每来一条新 negative 弹幕额外撒 1 滴 rain")]
    public bool spawnOnEachNegativeEvent = true;

    // ── 内部数据 ──────────────────────────────────
    readonly List<EmotionRecord> records = new();
    readonly List<EmotionEvent>  recentEvents = new();
    int recordIdx;
    double lastVT = -1d;

    float nextPosBurstTime;
    float nextNegBurstTime;

    readonly List<EmotionIconParticle> pool = new();
    Transform particleRoot;

    CurvedDanmakuSurfaceLayer nearLayer;
    Camera viewCamera;

    // ── 序列化辅助类 ──────────────────────────────
    [Serializable] class EmotionRecord { public float time; public bool isPositive; public int length; }
    [Serializable] class EmotionEvent  { public float time; public bool isPositive; public int length; }

    [Serializable] class Dto     { public string 弹幕内容; public float 新视频中的时间; public string 正反面情绪; public int 长度; }
    [Serializable] class FileDto { public Dto[] items; }

    // ─────────────────────────────────────────────
    void Start()
    {
        if (videoPlayer == null)
            videoPlayer = FindObjectOfType<VideoPlayer>();

        if (cloudRig == null)
            cloudRig = FindObjectOfType<CurvedDanmakuCloudRig>();

        if (cloudRig != null)
        {
            cloudRig.ResolveLayers();
            nearLayer = cloudRig.nearEmotionLayer;
        }

        viewCamera = DanmakuCameraUtility.ResolveViewCamera();

        particleRoot = new GameObject("EmotionIconParticles").transform;
        particleRoot.SetParent(transform, false);

        EnsureSprites();
        LoadRecords();
        SeekRecords(videoPlayer != null ? videoPlayer.time : 0d);
    }

    void Update()
    {
        if (videoPlayer == null) return;

        double vt = videoPlayer.time;

        if (lastVT >= 0d && vt + 0.5 < lastVT)
        {
            SeekRecords(vt);
            recentEvents.Clear();
            RecycleAll();
        }
        lastVT = vt;

        if (!videoPlayer.isPrepared) return;

        if (viewCamera == null || !viewCamera.enabled)
            viewCamera = DanmakuCameraUtility.ResolveViewCamera();

        IngestRecords(vt);
        PruneWindow((float)vt);
        TryBurst((float)vt);
    }

    void LoadRecords()
    {
        records.Clear();
        string path = Path.Combine(Application.streamingAssetsPath,
                                   emotionJsonFileName.Replace('\\', '/'));
        if (!File.Exists(path))
        {
            Debug.LogWarning($"[EmotionIconParticle] JSON 未找到: {path}");
            return;
        }

        var file = JsonUtility.FromJson<FileDto>(
            File.ReadAllText(path, System.Text.Encoding.UTF8).Trim());
        if (file?.items == null) return;

        for (int i = 0; i < file.items.Length; i++)
        {
            Dto d = file.items[i];
            if (d == null) continue;
            records.Add(new EmotionRecord
            {
                time = d.新视频中的时间,
                isPositive = EmotionDensityUtil.IsPositiveSentiment(d.正反面情绪),
                length = Mathf.Max(1, d.长度)
            });
        }
        records.Sort((a, b) => a.time.CompareTo(b.time));
        Debug.Log($"[EmotionIconParticle] 已加载 {records.Count} 条情绪记录。");
    }

    void SeekRecords(double time)
    {
        recordIdx = 0;
        while (recordIdx < records.Count && records[recordIdx].time < (float)time)
            recordIdx++;
    }

    void IngestRecords(double videoTime)
    {
        while (recordIdx < records.Count && records[recordIdx].time <= (float)videoTime)
        {
            EmotionRecord r = records[recordIdx];
            recentEvents.Add(new EmotionEvent
            {
                time = r.time,
                isPositive = r.isPositive,
                length = r.length
            });
            recordIdx++;

            TrySpawnOnNewEvent(r.isPositive, r.length);
        }
    }

    void TrySpawnOnNewEvent(bool isPositive, int length)
    {
        CountSentiment(out int pos, out int neg);

        if (isPositive && spawnOnEachPositiveEvent
            && EmotionDensityUtil.IsPositiveActive(pos, minPositiveToTrigger))
        {
            SpawnParticle(true, length);
        }
        else if (!isPositive && spawnOnEachNegativeEvent
            && EmotionDensityUtil.IsNegativeActive(neg, minNegativeToTrigger))
        {
            SpawnParticle(false, length);
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

    void PruneWindow(float videoTime)
    {
        float minT = videoTime - windowSeconds;
        for (int i = recentEvents.Count - 1; i >= 0; i--)
        {
            if (recentEvents[i].time < minT)
                recentEvents.RemoveAt(i);
        }
    }

    void TryBurst(float now)
    {
        CountSentiment(out int posCount, out int negCount);

        if (EmotionDensityUtil.IsPositiveActive(posCount, minPositiveToTrigger)
            && now >= nextPosBurstTime)
        {
            int spawnCount = EmotionDensityUtil.MapCountToSpawnBurst(
                posCount, minPositiveToTrigger, burstCount, countForFullBlend);
            float avgLen = AverageLength(true);
            StartCoroutine(SpawnBurst(true, spawnCount, avgLen));
            nextPosBurstTime = now + burstCooldown;
        }

        if (EmotionDensityUtil.IsNegativeActive(negCount, minNegativeToTrigger)
            && now >= nextNegBurstTime)
        {
            int spawnCount = EmotionDensityUtil.MapCountToSpawnBurst(
                negCount, minNegativeToTrigger, burstCount, countForFullBlend);
            float avgLen = AverageLength(false);
            StartCoroutine(SpawnBurst(false, spawnCount, avgLen));
            nextNegBurstTime = now + burstCooldown;
        }
    }

    float AverageLength(bool positive)
    {
        int sum = 0;
        int count = 0;
        for (int i = 0; i < recentEvents.Count; i++)
        {
            if (recentEvents[i].isPositive == positive)
            {
                sum += recentEvents[i].length;
                count++;
            }
        }
        return count > 0 ? sum / (float)count : 5f;
    }

    IEnumerator SpawnBurst(bool isPositive, int spawnCount, float avgLength)
    {
        for (int i = 0; i < spawnCount; i++)
        {
            SpawnParticle(isPositive, avgLength);
            if (inBurstGap > 0f)
                yield return new WaitForSeconds(inBurstGap);
        }
    }

    void SpawnParticle(bool isPositive, float avgLength)
    {
        Vector3 worldPos = GetRandomSpawnPosition();

        float lenRatio = Mathf.Clamp01((avgLength - 1f) / 19f);
        float scale = baseWorldScale * (1f + sizeVariation * lenRatio);

        float driftX = UnityEngine.Random.Range(-horizontalDrift, horizontalDrift);
        float driftZ = UnityEngine.Random.Range(-horizontalDrift, horizontalDrift);
        Vector3 vel = Vector3.down * fallSpeed + new Vector3(driftX, 0f, driftZ);

        Sprite sprite = isPositive ? positiveSprite : negativeSprite;
        Color  color  = isPositive ? positiveColor : negativeColor;

        EmotionIconParticle p = GetOrCreateParticle();
        p.Activate(sprite, color, scale,
                   new Vector2(iconCanvasWidth, iconCanvasHeight),
                   worldPos, vel,
                   lifetime, fadeInFraction, fadeOutFraction,
                   canvasSortOrder,
                   viewCamera != null ? viewCamera.transform : null);
    }

    void EnsureSprites()
    {
        if (positiveSprite == null && !string.IsNullOrWhiteSpace(positiveSpriteResource))
            positiveSprite = LoadSpriteFromResources(positiveSpriteResource);

        if (negativeSprite == null && !string.IsNullOrWhiteSpace(negativeSpriteResource))
            negativeSprite = LoadSpriteFromResources(negativeSpriteResource);

        if (positiveSprite == null || negativeSprite == null)
            Debug.LogWarning("[EmotionIconParticle] star/rain 贴图未加载，请检查 Resources/Textrue/star.png 与 rain.png。");
    }

    static Sprite LoadSpriteFromResources(string resourcesPath)
    {
        Texture2D tex = Resources.Load<Texture2D>(resourcesPath);
        if (tex == null)
        {
            Debug.LogWarning($"[EmotionIconParticle] Resources 贴图未找到: {resourcesPath}");
            return null;
        }

        return Sprite.Create(
            tex,
            new Rect(0f, 0f, tex.width, tex.height),
            new Vector2(0.5f, 0.5f),
            100f);
    }

    Vector3 GetRandomSpawnPosition()
    {
        if (nearLayer != null)
        {
            float u = SampleSpawnU();
            Vector3 localAnchor = nearLayer.GetLocalPositionExtended(u, UnityEngine.Random.Range(0.2f, 1f), 0f);
            Vector3 worldAnchor = nearLayer.transform.TransformPoint(localAnchor);

            Vector3 localTop = nearLayer.GetLocalPositionExtended(u, 1f, 0f);
            Vector3 worldTop = nearLayer.transform.TransformPoint(localTop);

            float heightAbove = UnityEngine.Random.Range(spawnHeightMin, spawnHeightMax);
            return new Vector3(worldAnchor.x, worldTop.y + heightAbove, worldAnchor.z);
        }

        Camera cam = viewCamera;
        if (cam == null) return Vector3.up * 3f;

        Vector3 forward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
        Vector3 right = cam.transform.right;
        Vector3 basePos = cam.transform.position + forward * 2f;
        float h = UnityEngine.Random.Range(spawnHeightMin, spawnHeightMax);
        float halfWidth = spawnRangeWidthFraction;
        float center = spawnRangeCenterOffset * Mathf.Max(0f, 1f - halfWidth);
        return basePos
            + right * UnityEngine.Random.Range(center - halfWidth, center + halfWidth)
            + Vector3.up * h;
    }

    float SampleSpawnU()
    {
        float half = spawnRangeWidthFraction * 0.5f;
        float center = (spawnRangeCenterOffset + 1f) * 0.5f;
        float uMin = center - half;
        float uMax = center + half;
        if (uMax - uMin < 0.001f)
            return center;
        return UnityEngine.Random.Range(uMin, uMax);
    }

    EmotionIconParticle GetOrCreateParticle()
    {
        for (int i = 0; i < pool.Count; i++)
        {
            if (pool[i] != null && !pool[i].IsActive)
                return pool[i];
        }

        var go = new GameObject("EmoIcon", typeof(RectTransform));
        go.transform.SetParent(particleRoot, false);
        var p = go.AddComponent<EmotionIconParticle>();
        pool.Add(p);
        return p;
    }

    void RecycleAll()
    {
        for (int i = 0; i < pool.Count; i++)
        {
            if (pool[i] != null && pool[i].IsActive)
                pool[i].Recycle();
        }
    }

    void OnDestroy()
    {
        RecycleAll();
    }

    void OnDisable()
    {
        if (Application.isPlaying)
        {
            RecycleAll();
            StopAllCoroutines();
        }
    }
}
