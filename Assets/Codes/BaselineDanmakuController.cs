using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// baseline (A1) 场景的传统 2D 滚动弹幕系统。
/// 只接收单一 JSON（time_sec / new_video_time_sec / text），
/// 弹幕从右向左滚动，全部为白色，效果等同于在电脑/平板上看弹幕视频，
/// 只是把显示区域搬到了视频屏幕前方的世界空间画布上。
/// </summary>
[DisallowMultipleComponent]
public class BaselineDanmakuController : MonoBehaviour
{
    [Header("场景引用（留空自动查找 \"screen\"）")]
    public VideoPlayer videoPlayer;
    public Transform screenTransform;
    public TMP_FontAsset fontAsset;

    [Header("数据源（单一 JSON，字段：time_sec / new_video_time_sec / text）")]
    public string jsonFileName = "Baseline/baseline_danmaku_full.json";

    [Header("显示区域（相对 screen 的局部米数，自动抵消 screen 的非等比缩放）")]
    [Tooltip("显示区域中心相对 screen 的局部坐标（米，已抵消 screen 缩放，可参考 MidZoneFrame/VideoPlaybackUI 的位置微调）")]
    public Vector3 localPosition = new Vector3(0f, 3f, 1f);
    [Tooltip("显示区域宽 x 高（米），弹幕只会出现在这个矩形范围内，超出会被裁剪")]
    public Vector2 displayAreaSize = new Vector2(8f, 3f);
    [Tooltip("勾选后自动抵消 screen（或任意父物体）的非等比缩放，让上面的米数真正对应物理米")]
    public bool autoNormalizeParentScale = true;

    [Header("文字外观")]
    [Range(8f, 96f)] public float fontSize = 34f;
    [Tooltip("像素到米的换算比例，决定弹幕整体物理大小（越小文字越小）")]
    [Range(0.0005f, 0.02f)] public float worldScale = 0.006f;
    public Color textColor = Color.white;
    [Tooltip("描边宽度，0 表示不描边（改为加粗以保证可读性）")]
    public float outlineWidth = 0.18f;
    public Color outlineColor = new Color(0f, 0f, 0f, 0.85f);

    [Header("运动 / 并发（传统滚动弹幕）")]
    [Tooltip("从右向左的滚动速度，单位：像素/秒")]
    public float scrollSpeed = 260f;
    [Tooltip("弹幕轨道（行）数量，越多越不容易重叠，但每行会更矮")]
    [Range(1, 20)] public int trackCount = 8;
    [Tooltip("同一时刻屏幕上最多允许的弹幕条数")]
    [Range(1, 80)] public int maxConcurrent = 30;
    [Tooltip("同一轨道复用之间的安全间隔（秒），避免紧贴追尾")]
    public float laneSafetyGapSeconds = 0.12f;
    public float rightSpawnPadding = 40f;
    public float leftDestroyPadding = 80f;

    [Header("Seek / 清理")]
    public bool clearSpawnedOnSeek = true;
    public float seekResetThresholdSeconds = 0.5f;

    readonly List<BaselineDanmakuRecord> entries = new List<BaselineDanmakuRecord>();
    readonly List<RectTransform> activeLabels = new List<RectTransform>();
    float[] laneFreeAtVideoTime;
    int currentIndex;
    double lastVideoTime = -1d;

    Transform normalizedRoot;
    RectTransform canvasRect;
    RectTransform viewportRect;

    void Awake()
    {
        ResolveReferences();
        BuildHierarchy();
    }

    void OnEnable()
    {
        NormalizeParentScale();
    }

    void Start()
    {
        LoadJson();
    }

    void ResolveReferences()
    {
        if (screenTransform == null)
        {
            var screenGo = GameObject.Find("screen");
            if (screenGo != null)
                screenTransform = screenGo.transform;
        }

        if (videoPlayer == null && screenTransform != null)
            videoPlayer = screenTransform.GetComponent<VideoPlayer>();
    }

    void NormalizeParentScale()
    {
        if (!autoNormalizeParentScale || normalizedRoot == null)
            return;

        Transform parent = normalizedRoot.parent;
        if (parent == null)
        {
            normalizedRoot.localScale = Vector3.one;
            return;
        }

        Vector3 parentScale = parent.lossyScale;
        Vector3 target = new Vector3(
            Mathf.Abs(parentScale.x) > 0.0001f ? 1f / parentScale.x : 1f,
            Mathf.Abs(parentScale.y) > 0.0001f ? 1f / parentScale.y : 1f,
            Mathf.Abs(parentScale.z) > 0.0001f ? 1f / parentScale.z : 1f);
        normalizedRoot.localScale = target;
    }

    void BuildHierarchy()
    {
        if (screenTransform == null)
        {
            Debug.LogWarning("BaselineDanmakuController: 未找到 screen，无法定位显示区域。");
            return;
        }

        Transform existingRoot = screenTransform.Find("BaselineDanmakuRoot");
        if (existingRoot != null)
            Destroy(existingRoot.gameObject);

        var rootGo = new GameObject("BaselineDanmakuRoot");
        normalizedRoot = rootGo.transform;
        normalizedRoot.SetParent(screenTransform, false);
        normalizedRoot.localPosition = localPosition;
        normalizedRoot.localRotation = Quaternion.identity;
        NormalizeParentScale();

        var canvasGo = new GameObject("BaselineDanmakuCanvas", typeof(RectTransform));
        canvasGo.transform.SetParent(normalizedRoot, false);
        canvasGo.transform.localPosition = Vector3.zero;
        canvasGo.transform.localRotation = Quaternion.identity;

        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 150;
        canvasGo.AddComponent<CanvasScaler>();

        canvasRect = canvasGo.GetComponent<RectTransform>();
        Vector2 pixelSize = new Vector2(
            displayAreaSize.x / Mathf.Max(0.0001f, worldScale),
            displayAreaSize.y / Mathf.Max(0.0001f, worldScale));
        canvasRect.sizeDelta = pixelSize;
        canvasRect.pivot = new Vector2(0.5f, 0.5f);
        canvasRect.localScale = Vector3.one * worldScale;

        var viewportGo = new GameObject("Viewport", typeof(RectTransform));
        viewportGo.transform.SetParent(canvasRect, false);
        viewportRect = viewportGo.GetComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = Vector2.zero;
        viewportRect.offsetMax = Vector2.zero;
        viewportGo.AddComponent<RectMask2D>();

        ResetLanes();
    }

    void ResetLanes()
    {
        laneFreeAtVideoTime = new float[Mathf.Max(1, trackCount)];
        for (int i = 0; i < laneFreeAtVideoTime.Length; i++)
            laneFreeAtVideoTime[i] = float.NegativeInfinity;
    }

    public void LoadJson()
    {
        entries.Clear();
        currentIndex = 0;

        string path = Path.Combine(Application.streamingAssetsPath, jsonFileName);
        if (!File.Exists(path))
        {
            Debug.LogWarning($"BaselineDanmakuController: JSON not found: {path}");
            return;
        }

        string json = File.ReadAllText(path);
        if (json.TrimStart().StartsWith("["))
            json = "{\"items\":" + json + "}";

        BaselineDanmakuArrayWrapper wrapper = JsonUtility.FromJson<BaselineDanmakuArrayWrapper>(json);
        if (wrapper == null || wrapper.items == null)
        {
            Debug.LogWarning($"BaselineDanmakuController: JSON 解析失败或为空: {path}");
            return;
        }

        entries.AddRange(wrapper.items);
        entries.Sort((a, b) => a.new_video_time_sec.CompareTo(b.new_video_time_sec));

        if (videoPlayer != null)
            currentIndex = FindFirstIndexAtOrAfter((float)videoPlayer.time);

        Debug.Log($"BaselineDanmakuController: loaded {entries.Count} entries from {jsonFileName}");
    }

    void Update()
    {
        if (videoPlayer == null || entries.Count == 0 || viewportRect == null)
            return;

        double videoTime = videoPlayer.time;
        if (ShouldResetForSeek(videoTime))
        {
            currentIndex = FindFirstIndexAtOrAfter((float)videoTime);
            if (clearSpawnedOnSeek)
                ClearSpawned();
        }

        lastVideoTime = videoTime;
        if (!videoPlayer.isPlaying)
            return;

        CleanupDestroyedLabels();

        while (currentIndex < entries.Count && entries[currentIndex].new_video_time_sec <= videoTime)
        {
            TrySpawn(entries[currentIndex], (float)videoTime);
            currentIndex++;
        }
    }

    void CleanupDestroyedLabels()
    {
        for (int i = activeLabels.Count - 1; i >= 0; i--)
        {
            if (activeLabels[i] == null)
                activeLabels.RemoveAt(i);
        }
    }

    void TrySpawn(BaselineDanmakuRecord entry, float videoTime)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.text) || viewportRect == null)
            return;

        if (activeLabels.Count >= maxConcurrent)
            return;

        int lane = PickFreeLane(videoTime);
        if (lane < 0)
            return;

        SpawnLabel(entry, lane, videoTime);
    }

    int PickFreeLane(float videoTime)
    {
        if (laneFreeAtVideoTime == null || laneFreeAtVideoTime.Length != Mathf.Max(1, trackCount))
            ResetLanes();

        for (int i = 0; i < laneFreeAtVideoTime.Length; i++)
        {
            if (videoTime >= laneFreeAtVideoTime[i])
                return i;
        }

        return -1;
    }

    void SpawnLabel(BaselineDanmakuRecord entry, int lane, float videoTime)
    {
        var go = new GameObject("BaselineDanmaku", typeof(RectTransform));
        go.transform.SetParent(viewportRect, false);

        var label = go.AddComponent<TextMeshProUGUI>();
        label.raycastTarget = false;
        label.richText = false;
        label.text = entry.text;
        label.color = textColor;
        label.fontSize = fontSize;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Overflow;
        label.alignment = TextAlignmentOptions.Midline;
        if (fontAsset != null)
            label.font = fontAsset;

        TmpDanmakuTextUtility.ApplyReadableStyle(label, outlineWidth, outlineColor);

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);

        Vector2 preferred = label.GetPreferredValues(entry.text, 0f, 0f);
        float textWidth = Mathf.Max(20f, preferred.x);
        float textHeight = preferred.y > 0f ? preferred.y : fontSize * 1.4f;
        rect.sizeDelta = new Vector2(textWidth, textHeight);

        float halfViewportWidth = viewportRect.rect.width * 0.5f;
        float startX = halfViewportWidth + rightSpawnPadding + textWidth * 0.5f;
        float y = TrackToLocalY(lane);
        rect.anchoredPosition = new Vector2(startX, y);

        var mover = go.AddComponent<DanmakuMover>();
        mover.canvas = viewportRect;
        mover.speed = Mathf.Max(1f, scrollSpeed);
        mover.destroyPadding = textWidth * 0.5f + leftDestroyPadding;

        activeLabels.Add(rect);

        float totalDistance = startX - (-halfViewportWidth - mover.destroyPadding);
        float travelSeconds = totalDistance / mover.speed;
        if (lane >= 0 && lane < laneFreeAtVideoTime.Length)
            laneFreeAtVideoTime[lane] = videoTime + travelSeconds + Mathf.Max(0f, laneSafetyGapSeconds);
    }

    float TrackToLocalY(int lane)
    {
        int safeTrackCount = Mathf.Max(1, trackCount);
        float height = viewportRect.rect.height;
        float step = height / safeTrackCount;
        float top = height * 0.5f;
        int clamped = Mathf.Clamp(lane, 0, safeTrackCount - 1);
        return top - step * (clamped + 0.5f);
    }

    bool ShouldResetForSeek(double videoTime)
    {
        if (lastVideoTime < 0d)
            return false;

        return videoTime + seekResetThresholdSeconds < lastVideoTime;
    }

    int FindFirstIndexAtOrAfter(float timeSeconds)
    {
        int low = 0;
        int high = entries.Count;
        while (low < high)
        {
            int mid = low + (high - low) / 2;
            if (entries[mid].new_video_time_sec < timeSeconds)
                low = mid + 1;
            else
                high = mid;
        }

        return low;
    }

    void ClearSpawned()
    {
        for (int i = activeLabels.Count - 1; i >= 0; i--)
        {
            if (activeLabels[i] != null)
                Destroy(activeLabels[i].gameObject);
        }

        activeLabels.Clear();
        ResetLanes();
    }
}
