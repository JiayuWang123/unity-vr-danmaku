using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.Video;

/// <summary>
/// 情绪符号粒子系统。
/// 读取情绪 JSON，通过滑动时间窗统计密度：
///   positive 达标 → ★ 星星散落（与 light.png 同一条件）
///   negative 达标 → ？ 单独散落（互不影响，可同时存在）
///
/// 场景使用方式：
///   1. 在任意 GameObject 上挂载此脚本。
///   2. 将 screen 下的 CurvedDanmakuCloudRig 拖进 cloudRig（或留空自动查找）。
///   3. 在 Inspector 里调好 fontAsset，其余参数保持默认即可。
/// </summary>
public class EmotionIconParticleController : MonoBehaviour
{
    [Header("场景引用")]
    public VideoPlayer videoPlayer;
    [Tooltip("留空则自动在场景中查找")]
    public CurvedDanmakuCloudRig cloudRig;
    public TMP_FontAsset fontAsset;

    [Header("情绪 JSON（与 EmotionSkyboxBlendController 共用同一文件）")]
    public string emotionJsonFileName = "classify/emotional_interactions_emotion_from_excel.json";

    [Header("触发条件（与 EmotionSkybox 对齐）")]
    [Tooltip("统计最近多少秒内的情绪弹幕（建议与 Skybox 相同，默认 14）")]
    public float windowSeconds = 14f;
    [Tooltip("★ 星星：窗口内至少多少条 positive（与 light.png 相同）")]
    public int minPositiveToTrigger = 2;
    [Tooltip("positive 需比 negative 至少多多少条（0 = positive ≥ negative 即可）")]
    public int dominanceMargin = 0;
    [Tooltip("？ 问号：窗口内至少多少条 negative（单独判断，不与 positive 比较）")]
    public int minNegativeToTrigger = 2;

    [Header("粒子外观")]
    public string positiveIcon = "★";
    public string negativeIcon = "？";
    [Range(0.002f, 0.06f)]
    [Tooltip("粒子基础世界尺寸（米）；默认很小，像远处飘落的符号")]
    public float baseWorldScale = 0.012f;
    [Range(0f, 0.6f)]
    [Tooltip("根据弹幕长度对尺寸的影响幅度（0 = 全部同尺寸）")]
    public float sizeVariation = 0.25f;
    public Color positiveColor = new Color(1f, 0.92f, 0.3f, 0.95f);
    public Color negativeColor = new Color(0.55f, 0.78f, 1f, 0.95f);
    public int canvasSortOrder = 230;

    [Header("从天而降")]
    [Tooltip("在 NearEmotion 区域顶边之上，最低生成高度（米）")]
    public float spawnHeightMin = 1.2f;
    [Tooltip("在 NearEmotion 区域顶边之上，最高生成高度（米）")]
    public float spawnHeightMax = 2.8f;

    [Header("粒子运动")]
    [Range(1.5f, 8f)] public float lifetime = 3.5f;
    [Range(0.1f, 1.5f)] public float fallSpeed = 0.55f;
    [Range(0f, 0.2f)] public float horizontalDrift = 0.04f;
    [Range(0f, 0.5f)] public float fadeInFraction = 0.15f;
    [Range(0f, 0.5f)] public float fadeOutFraction = 0.35f;

    [Header("爆发节奏")]
    [Range(1, 16)] public int burstCount = 8;
    [Tooltip("同一方向两次爆发的最短间隔（秒）；越小越频繁")]
    public float burstCooldown = 1f;
    [Tooltip("同次爆发内相邻粒子的生成间隔（秒）")]
    public float inBurstGap = 0.06f;
    [Tooltip("窗口内 positive 已达标时，每来一条新 positive 弹幕额外撒 1 颗★")]
    public bool spawnStarOnEachPositiveEvent = true;
    [Tooltip("窗口内 negative 已达标时，每来一条新 negative 弹幕额外撒 1 个？")]
    public bool spawnQuestionOnEachNegativeEvent = true;

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

        LoadRecords();
        SeekRecords(videoPlayer != null ? videoPlayer.time : 0d);
    }

    void Update()
    {
        if (videoPlayer == null) return;

        double vt = videoPlayer.time;

        // Seek 检测
        if (lastVT >= 0d && vt + 0.5 < lastVT)
        {
            SeekRecords(vt);
            recentEvents.Clear();
            RecycleAll();
        }
        lastVT = vt;

        if (!videoPlayer.isPrepared) return;

        // 刷新相机引用（VR 启动可能延迟）
        if (viewCamera == null || !viewCamera.enabled)
            viewCamera = DanmakuCameraUtility.ResolveViewCamera();

        IngestRecords(vt);
        PruneWindow((float)vt);
        TryBurst((float)vt);
    }

    // ── 数据管理 ──────────────────────────────────

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

        if (isPositive && spawnStarOnEachPositiveEvent
            && EmotionDensityUtil.ShouldTriggerPositive(pos, neg, minPositiveToTrigger, dominanceMargin))
        {
            SpawnParticle(true, length);
        }
        else if (!isPositive && spawnQuestionOnEachNegativeEvent
            && EmotionDensityUtil.ShouldTriggerNegative(neg, minNegativeToTrigger))
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

    // ── 爆发逻辑 ──────────────────────────────────

    void TryBurst(float now)
    {
        CountSentiment(out int posCount, out int negCount);

        if (EmotionDensityUtil.ShouldTriggerPositive(posCount, negCount, minPositiveToTrigger, dominanceMargin)
            && now >= nextPosBurstTime)
        {
            float avgLen = AverageLength(true);
            StartCoroutine(SpawnBurst(true, avgLen));
            nextPosBurstTime = now + burstCooldown;
        }

        if (EmotionDensityUtil.ShouldTriggerNegative(negCount, minNegativeToTrigger)
            && now >= nextNegBurstTime)
        {
            float avgLen = AverageLength(false);
            StartCoroutine(SpawnBurst(false, avgLen));
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

    IEnumerator SpawnBurst(bool isPositive, float avgLength)
    {
        for (int i = 0; i < burstCount; i++)
        {
            SpawnParticle(isPositive, avgLength);
            if (inBurstGap > 0f)
                yield return new WaitForSeconds(inBurstGap);
        }
    }

    // ── 粒子生成 ──────────────────────────────────

    void SpawnParticle(bool isPositive, float avgLength)
    {
        Vector3 worldPos = GetRandomSpawnPosition();

        // 尺寸：基础 + 长度影响（归一化到 0~20 字范围内）
        float lenRatio = Mathf.Clamp01((avgLength - 1f) / 19f);
        float scale = baseWorldScale * (1f + sizeVariation * lenRatio);

        // 垂直下落（世界坐标 Y 轴），带轻微水平漂移
        float driftX = UnityEngine.Random.Range(-horizontalDrift, horizontalDrift);
        float driftZ = UnityEngine.Random.Range(-horizontalDrift, horizontalDrift);
        Vector3 vel = Vector3.down * fallSpeed + new Vector3(driftX, 0f, driftZ);

        string icon   = isPositive ? positiveIcon : negativeIcon;
        Color  color  = isPositive ? positiveColor : negativeColor;

        EmotionIconParticle p = GetOrCreateParticle();
        p.Activate(icon, color, scale, worldPos, vel,
                   lifetime, fadeInFraction, fadeOutFraction,
                   fontAsset, canvasSortOrder,
                   viewCamera != null ? viewCamera.transform : null);
    }

    Vector3 GetRandomSpawnPosition()
    {
        if (nearLayer != null)
        {
            // 在 NearEmotion 弧面宽度内随机取水平位置，从该区域顶边之上的「天空」生成
            float u = UnityEngine.Random.value;
            Vector3 localAnchor = nearLayer.GetLocalPosition(u, UnityEngine.Random.Range(0.2f, 1f), 0f);
            Vector3 worldAnchor = nearLayer.transform.TransformPoint(localAnchor);

            Vector3 localTop = nearLayer.GetLocalPosition(u, 1f, 0f);
            Vector3 worldTop = nearLayer.transform.TransformPoint(localTop);

            float heightAbove = UnityEngine.Random.Range(spawnHeightMin, spawnHeightMax);
            return new Vector3(worldAnchor.x, worldTop.y + heightAbove, worldAnchor.z);
        }

        // 无曲面层：在相机前方上空随机生成
        Camera cam = viewCamera;
        if (cam == null) return Vector3.up * 3f;

        Vector3 forward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
        Vector3 right = cam.transform.right;
        Vector3 basePos = cam.transform.position + forward * 2f;
        float h = UnityEngine.Random.Range(spawnHeightMin, spawnHeightMax);
        return basePos
            + right * UnityEngine.Random.Range(-1f, 1f)
            + Vector3.up * h;
    }

    // ── 对象池 ────────────────────────────────────

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
