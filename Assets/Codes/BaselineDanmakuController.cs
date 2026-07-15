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
    [Tooltip("同一时刻屏幕上最多允许的弹幕条数（超出会先排队，稍后自动补发，不会永久丢失）")]
    [Range(1, 200)] public int maxConcurrent = 60;
    [Tooltip("同一轨道相邻两条弹幕之间的最小像素间隔。轨道会按“上一条弹幕的长度 + 这个间隔”自动计算何时可以复用，" +
             "而不是等上一条完全飘出屏幕，因此同一行可以连续排布多条弹幕")]
    public float laneGapPixels = 50f;
    [Tooltip("遇到并发上限或轨道被占满时是否排队等待、稍后补发（关闭则直接跳过该条弹幕）")]
    public bool queueOverflow = true;
    public float rightSpawnPadding = 40f;
    public float leftDestroyPadding = 80f;

    [Header("Seek / 清理")]
    public bool clearSpawnedOnSeek = true;
    public float seekResetThresholdSeconds = 0.5f;

    [Header("调试（Console 里查看排队情况）")]
    [Tooltip("勾选后，一旦有弹幕进入排队队列，会在 Console 里每隔一段时间打印当前排队条数和本次播放的峰值，" +
             "队列清空时也会打印一条提示。方便测试时确认当前密度/宽度设置是否真的会导致排队")]
    public bool logQueueStatus = true;
    [Tooltip("排队日志的打印间隔（秒），避免刷屏")]
    public float queueLogIntervalSeconds = 2f;

    readonly List<BaselineDanmakuRecord> entries = new List<BaselineDanmakuRecord>();
    readonly List<RectTransform> activeLabels = new List<RectTransform>();
    readonly Queue<BaselineDanmakuRecord> pendingQueue = new Queue<BaselineDanmakuRecord>();
    float[] laneFreeAtVideoTime;
    int currentIndex;
    double lastVideoTime = -1d;
    int peakQueueCount;
    bool queueWasNonEmpty;
    float nextQueueLogTime;

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
        DrainPendingQueue((float)videoTime);

        while (currentIndex < entries.Count && entries[currentIndex].new_video_time_sec <= videoTime)
        {
            BaselineDanmakuRecord entry = entries[currentIndex];
            currentIndex++;

            if (!TrySpawn(entry, (float)videoTime) && queueOverflow)
                pendingQueue.Enqueue(entry);
        }

        if (logQueueStatus)
            UpdateQueueDebugLog();
    }

    void UpdateQueueDebugLog()
    {
        int count = pendingQueue.Count;
        if (count > peakQueueCount)
            peakQueueCount = count;

        if (count > 0)
        {
            queueWasNonEmpty = true;
            if (Time.unscaledTime >= nextQueueLogTime)
            {
                Debug.Log($"BaselineDanmakuController: 排队等待中 {count} 条（本次播放峰值 {peakQueueCount} 条）");
                nextQueueLogTime = Time.unscaledTime + Mathf.Max(0.1f, queueLogIntervalSeconds);
            }
        }
        else if (queueWasNonEmpty)
        {
            Debug.Log($"BaselineDanmakuController: 排队已清空，补发完成（本次峰值 {peakQueueCount} 条）");
            queueWasNonEmpty = false;
        }
    }

    void DrainPendingQueue(float videoTime)
    {
        if (!queueOverflow)
        {
            pendingQueue.Clear();
            return;
        }

        int guard = pendingQueue.Count;
        for (int i = 0; i < guard; i++)
        {
            if (pendingQueue.Count == 0)
                break;

            BaselineDanmakuRecord entry = pendingQueue.Peek();
            if (!TrySpawn(entry, videoTime))
                break; // 排在最前面的补发不了，后面的顺序也不用再试了，保持先后顺序

            pendingQueue.Dequeue();
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

    /// <summary>
    /// 尝试立刻生成一条弹幕。返回 false 表示暂时没有空位（并发已满或轨道被占用），
    /// 调用方可以选择把这条弹幕放进排队队列，稍后再补发。
    /// </summary>
    bool TrySpawn(BaselineDanmakuRecord entry, float videoTime)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.text) || viewportRect == null)
            return true;

        if (activeLabels.Count >= maxConcurrent)
            return false;

        int lane = PickFreeLane(videoTime);
        if (lane < 0)
            return false;

        SpawnLabel(entry, lane, videoTime);
        return true;
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

        // 轨道只需要等"这一条的长度 + 间隔"对应的时间就能复用，
        // 不用等它完全飘出屏幕——这样同一行可以连续排布多条弹幕，只要彼此不重叠。
        float gapDistance = textWidth + Mathf.Max(0f, laneGapPixels);
        float gapSeconds = gapDistance / mover.speed;
        if (lane >= 0 && lane < laneFreeAtVideoTime.Length)
            laneFreeAtVideoTime[lane] = videoTime + gapSeconds;
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
        pendingQueue.Clear();
        peakQueueCount = 0;
        queueWasNonEmpty = false;
        ResetLanes();
    }
}
