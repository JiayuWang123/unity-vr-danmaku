using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Video;

[RequireComponent(typeof(SemanticDanmakuSettings))]
public class SemanticDanmakuController : MonoBehaviour
{
    public VideoPlayer videoPlayer;
    public CurvedDanmakuCloudRig cloudRig;
    public HeadLockedSocialPanel socialPanel;
    public TMP_FontAsset fontAsset;

    SemanticDanmakuSettings settings;
    SemanticDanmakuInstance labelTemplate;
    readonly DynamicInfoCloudLayout cloudLayout = new DynamicInfoCloudLayout();
    readonly List<SemanticDanmakuRecord> records = new List<SemanticDanmakuRecord>();
    readonly List<SemanticDanmakuInstance> spawnedInstances = new List<SemanticDanmakuInstance>();

    int playbackIndex;
    double lastVideoTime = -1d;
    float nextLayoutRefreshTime;
    int spawnedLogCount;

    void Awake()
    {
        settings = GetComponent<SemanticDanmakuSettings>();
    }

    void OnDisable()
    {
        if (Application.isPlaying)
            ClearSpawnedInstances();
    }

    void Start()
    {
        if (videoPlayer == null)
        {
            GameObject screen = GameObject.Find("screen");
            if (screen != null)
                videoPlayer = screen.GetComponent<VideoPlayer>();
        }

        if (cloudRig == null)
            cloudRig = FindObjectOfType<CurvedDanmakuCloudRig>();

        EnsureCloudRig();

        if (cloudRig != null)
        {
            cloudRig.NormalizeParentScale();
            cloudRig.ResolveLayers();
        }

        if (socialPanel == null)
            socialPanel = FindObjectOfType<HeadLockedSocialPanel>();

        EnsureLabelTemplate();
        LoadRecords();
        ResetPlaybackIndex();
        RefreshLayout(true);
        LogSetupStatus();
    }

    void EnsureCloudRig()
    {
        if (cloudRig != null)
            return;

        GameObject screen = GameObject.Find("screen");
        if (screen == null)
            return;

        Transform rigTransform = screen.transform.Find("CurvedDanmakuCloudRig");
        if (rigTransform == null)
            return;

        cloudRig = rigTransform.GetComponent<CurvedDanmakuCloudRig>();
        if (cloudRig == null)
            cloudRig = rigTransform.gameObject.AddComponent<CurvedDanmakuCloudRig>();
    }

    void LogSetupStatus()
    {
        if (videoPlayer == null)
            Debug.LogWarning("SemanticDanmaku: 未绑定 VideoPlayer，弹幕不会生成。");

        if (fontAsset == null)
            Debug.LogWarning("SemanticDanmaku: 未绑定 Font Asset，文字可能不可见。");

        if (cloudRig == null)
        {
            Debug.LogWarning("SemanticDanmaku: 找不到 CurvedDanmakuCloudRig。请在 screen 下创建并挂脚本。");
            return;
        }

        if (cloudRig.nearEmotionLayer == null || cloudRig.midInfoLayer == null || cloudRig.farInfoLayer == null)
            Debug.LogWarning("SemanticDanmaku: 曲面层未齐（Near/Mid/Far）。请在 Cloud Rig 下创建三个 Layer 子物体。");

        Debug.Log($"SemanticDanmaku 就绪：records={records.Count}, near={(cloudRig.nearEmotionLayer != null)}, mid={(cloudRig.midInfoLayer != null)}, far={(cloudRig.farInfoLayer != null)}");

        Camera cam = DanmakuCameraUtility.ResolveViewCamera();
        if (cam == null)
        {
            Debug.LogWarning("SemanticDanmaku: 找不到可用的摄像机（Camera.main 为空或未启用），billboard 朝向可能不对。");
        }

        Debug.Log($"SemanticDanmaku 诊断: CloudRig.localScale={cloudRig.transform.localScale}, " +
            $"cam={(cam != null ? cam.name : "null")} camPos={(cam != null ? cam.transform.position.ToString() : "-")}");

        LogLayerDiagnostics("Near", cloudRig.nearEmotionLayer, cam);
        LogLayerDiagnostics("Mid", cloudRig.midInfoLayer, cam);
        LogLayerDiagnostics("Far", cloudRig.farInfoLayer, cam);
    }

    static void LogLayerDiagnostics(string label, CurvedDanmakuSurfaceLayer layer, Camera cam)
    {
        if (layer == null)
        {
            Debug.Log($"SemanticDanmaku 诊断 [{label}]: 层不存在。");
            return;
        }

        Vector3 centerWorld = layer.GetWorldPosition(0.5f, 0.5f);
        float distToCam = cam != null ? Vector3.Distance(centerWorld, cam.transform.position) : -1f;
        Debug.Log($"SemanticDanmaku 诊断 [{label}]: radius={layer.radius}, lossyScale={layer.transform.lossyScale}, " +
            $"centerWorldPos={centerWorld}, 距摄像机={distToCam}m");
    }

    void Update()
    {
        if (videoPlayer == null)
            return;

        double videoTime = videoPlayer.time;
        if (ShouldResetForSeek(videoTime))
        {
            ResetPlaybackIndex(videoTime);
            if (settings.clearSpawnedOnSeek)
                ClearSpawnedInstances();
            RefreshLayout(true);
        }

        lastVideoTime = videoTime;

        if (Time.time >= nextLayoutRefreshTime)
            RefreshLayout(false);

        if (!videoPlayer.isPlaying)
        {
            if (Time.frameCount % 300 == 0)
                Debug.Log("SemanticDanmaku: 视频未播放，弹幕等待中。请点播放或确认 VideoPlayer 在运行。");
            return;
        }

        CleanupSpawnedList();
        while (playbackIndex < records.Count && records[playbackIndex].timeSeconds <= videoTime)
        {
            if (!TrySpawnRecord(records[playbackIndex]))
                break;

            playbackIndex++;
        }
    }

    void LoadRecords()
    {
        records.Clear();

        if (settings.useLegacyPopUpJson)
        {
            records.AddRange(SemanticDanmakuLoader.LoadLegacyPopUpFiles(
                settings.legacyNearJsonFileName,
                settings.legacyMidJsonFileName,
                settings.legacyFarJsonFileName));
        }
        else
        {
            records.AddRange(SemanticDanmakuLoader.LoadClassifiedFile(settings.classifiedJsonFileName));
        }

        Debug.Log($"SemanticDanmakuController loaded {records.Count} records.");
    }

    bool TrySpawnRecord(SemanticDanmakuRecord record)
    {
        if (record == null || string.IsNullOrWhiteSpace(record.text))
            return true;

        if (record.semanticLayer == DanmakuSemanticLayer.Inert)
            return true;

        if (record.semanticLayer == DanmakuSemanticLayer.Social)
        {
            if (socialPanel != null)
                socialPanel.Enqueue(record);
            return true;
        }

        CurvedDanmakuSurfaceLayer surfaceLayer;
        float u;
        float v;
        float radiusOffset = 0f;
        CurvedCloudLayerKind layerKind;

        if (record.semanticLayer == DanmakuSemanticLayer.Emotion)
        {
            if (settings.maxConcurrentEmotion <= 0)
                return true;

            DynamicInfoCloudLayout.ClusterPlacement placement = cloudLayout.GetEmotionPlacement(cloudRig);
            surfaceLayer = placement.layer;
            u = placement.u;
            v = placement.v;
            radiusOffset = placement.radiusOffset;
            layerKind = CurvedCloudLayerKind.NearEmotion;
        }
        else
        {
            if (!cloudLayout.TryGetSpawnPlacement(record.category, out DynamicInfoCloudLayout.ClusterPlacement placement, out u, out v, out radiusOffset))
                return true;

            surfaceLayer = placement.layer;
            layerKind = surfaceLayer != null ? surfaceLayer.layerKind : CurvedCloudLayerKind.MidInfo;
        }

        if (surfaceLayer == null)
        {
            Debug.LogWarning($"SemanticDanmaku: 无曲面层，跳过「{record.text}」({record.semanticLayer}/{record.category})");
            return true;
        }

        if (CountActiveOnLayer(layerKind) >= settings.GetMaxConcurrent(layerKind))
            return true;

        SemanticDanmakuInstance instance = Instantiate(labelTemplate, surfaceLayer.transform, false);
        instance.gameObject.SetActive(true);
        instance.Initialize(record, settings, surfaceLayer, layerKind, u, v, radiusOffset);
        spawnedInstances.Add(instance);

        if (spawnedLogCount < 5)
        {
            spawnedLogCount++;
            Camera cam = DanmakuCameraUtility.ResolveViewCamera();
            float dist = cam != null ? Vector3.Distance(instance.transform.position, cam.transform.position) : -1f;
            Debug.Log($"SemanticDanmaku 诊断: 生成「{record.text}」layer={layerKind} worldPos={instance.transform.position} 距摄像机={dist}m localScale={instance.transform.lossyScale}");
        }

        return true;
    }

    void RefreshLayout(bool force)
    {
        if (!force && videoPlayer == null)
            return;

        float videoTime = videoPlayer != null ? (float)videoPlayer.time : 0f;
        cloudLayout.Rebuild(records, videoTime, settings.layoutWindowSeconds, cloudRig);
        nextLayoutRefreshTime = Time.time + settings.layoutRefreshInterval;
    }

    int CountActiveOnLayer(CurvedCloudLayerKind layerKind)
    {
        CleanupSpawnedList();
        int count = 0;
        for (int i = 0; i < spawnedInstances.Count; i++)
        {
            if (spawnedInstances[i] != null && spawnedInstances[i].LayerKind == layerKind)
                count++;
        }

        return count;
    }

    void CleanupSpawnedList()
    {
        for (int i = spawnedInstances.Count - 1; i >= 0; i--)
        {
            if (spawnedInstances[i] == null)
                spawnedInstances.RemoveAt(i);
        }
    }

    bool ShouldResetForSeek(double videoTime)
    {
        if (lastVideoTime < 0d)
            return false;

        return videoTime + settings.seekResetThresholdSeconds < lastVideoTime;
    }

    void ResetPlaybackIndex(double videoTime = -1d)
    {
        if (videoTime < 0d && videoPlayer != null)
            videoTime = videoPlayer.time;

        playbackIndex = FindFirstIndexAtOrAfter((float)videoTime);
        lastVideoTime = videoTime;
    }

    static int FindFirstIndexAtOrAfter(float timeSeconds, List<SemanticDanmakuRecord> source)
    {
        int low = 0;
        int high = source.Count;
        while (low < high)
        {
            int mid = low + (high - low) / 2;
            if (source[mid].timeSeconds < timeSeconds)
                low = mid + 1;
            else
                high = mid;
        }

        return low;
    }

    int FindFirstIndexAtOrAfter(float timeSeconds)
    {
        return FindFirstIndexAtOrAfter(timeSeconds, records);
    }

    void ClearSpawnedInstances()
    {
        for (int i = spawnedInstances.Count - 1; i >= 0; i--)
        {
            if (spawnedInstances[i] != null)
                Destroy(spawnedInstances[i]);
        }

        spawnedInstances.Clear();
        cloudLayout.ResetSpawnCounters();
    }

    void EnsureLabelTemplate()
    {
        if (labelTemplate == null)
        {
            GameObject root = new GameObject("SemanticDanmakuLabelTemplate");
            root.transform.SetParent(transform, false);
            root.SetActive(false);

            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = settings.canvasSortingOrder;

            RectTransform rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = settings.labelSize;
            rect.localScale = Vector3.one * settings.worldLabelScale;
            root.AddComponent<CanvasGroup>();

            GameObject textGo = new GameObject("Text");
            textGo.transform.SetParent(root.transform, false);
            RectTransform textRect = textGo.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            TextMeshProUGUI label = textGo.AddComponent<TextMeshProUGUI>();
            label.alignment = TextAlignmentOptions.Center;
            label.richText = false;
            labelTemplate = root.AddComponent<SemanticDanmakuInstance>();
        }

        TextMeshProUGUI templateLabel = labelTemplate.GetComponentInChildren<TextMeshProUGUI>();
        if (fontAsset != null && templateLabel != null)
            templateLabel.font = fontAsset;
        else if (fontAsset == null)
            Debug.LogWarning("SemanticDanmakuController: Font Asset 未绑定，弹幕文字无法显示。请在 Inspector 里指定 SIMHEI SDF。");
    }
}
