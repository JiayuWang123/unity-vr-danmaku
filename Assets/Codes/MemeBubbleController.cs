using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// 梗/搞笑弹幕：统一气泡样式，在视频左右两侧 Mid 层弹出。
/// </summary>
public class MemeBubbleController : MonoBehaviour
{
    [Header("场景引用")]
    public VideoPlayer videoPlayer;
    public Transform screenTransform;
    [Tooltip("Mid 区域框；留空则自动查找 zone=Mid 的 PopUpDanmakuZoneFrame")]
    public PopUpDanmakuZoneFrame midZoneFrame;
    public TMP_FontAsset fontAsset;

    [Header("JSON")]
    public string jsonFileName = "classify/memes_joking.json";

    [Header("随机位置（Mid 中层区域，靠视频一侧对齐）")]
    [Tooltip("气泡靠视频一侧对齐锚点后，再往外侧留出的间距（screen 本地单位）")]
    public float sideOutwardOffset = 0.06f;
    [Tooltip("Y 方向随机范围（相对 Mid 区域高度）")]
    [Range(0.2f, 1f)] public float verticalRangeRatio = 0.75f;
    [Tooltip("相对视频平面向相机偏移（screen 本地 Z）；可略压住视频边缘，但不会被挡住")]
    public float spawnForwardOffset = 0.22f;
    [Tooltip("同侧相邻气泡之间的最小垂直间距（screen 本地单位）")]
    public float bubbleVerticalGap = 0.04f;

    [Header("字号与气泡大小 ← 字体在这里调")]
    [Tooltip("弹幕文字字号（调大/调小文字）")]
    [Range(8f, 96f)] public float fontSize = 28f;
    [Tooltip("气泡整体 3D 大小（VR 里看起来很大时优先调小这个）")]
    [Range(0.001f, 0.008f)] public float worldScale = 0.002f;
    public float maxBubbleWidth = 380f;
    public float minBubbleWidth = 140f;
    public float textPaddingH = 22f;
    public float textPaddingV = 14f;
    public float iconSize = 44f;
    [Tooltip("对话框底部小尖角宽度/高度（像素）")]
    public float tailWidth = 16f;
    public float tailHeight = 12f;
    [Tooltip("尖角从外缘往中间平移量，留出外侧一条底边横线（像素）")]
    public float tailSideInset = 10f;

    [Header("外观（默认与聊天室气泡同款赛博朋克配色）")]
    public Sprite laughingIcon;
    public Color bubbleBgColor = new Color(0.07f, 0.1f, 0.22f, 0.8f);
    public Color bubbleBorderColor = new Color(0.35f, 0.85f, 1f, 0.92f);
    public Color textColor = new Color(0.87f, 0.95f, 1f, 1f);
    [Range(1f, 8f)] public float bubbleBorderWidth = 2.5f;
    [Tooltip("启动时若存在 SocializationPanelController，则同步其气泡配色与尺寸")]
    public bool syncStyleFromChatPanel = true;
    [Range(0, 300)] public int canvasSortOrder = 210;

    [Header("时间控制")]
    public float dwellMin = 2f;
    public float dwellMax = 5f;
    public float fadeOut = 0.45f;
    public float minSpawnGap = 0.8f;
    public int maxConcurrent = 2;

    readonly List<MemeBubbleRecord> records = new();
    readonly List<MemeBubbleInstance> activeInstances = new();

    int recordIdx;
    double lastVT = -1d;
    float lastSpawnTime = -999f;
    Transform bubbleRoot;
    Transform midAnchorLeft;
    Transform midAnchorRight;
    bool initialized;

    TextMeshProUGUI measureLabel;

    [Serializable]
    class MemeBubbleRecord
    {
        public string 弹幕内容;
        public int 长度;
        public float 新视频中的时间;
    }

    [Serializable]
    class MemeBubbleCollection
    {
        public MemeBubbleRecord[] items;
    }

    void OnDestroy()
    {
        ClearActiveBubbles();
    }

    void OnDisable()
    {
        if (Application.isPlaying)
            ClearActiveBubbles();

        initialized = false;
    }

    void Start()
    {
        if (initialized) return;
        initialized = true;

        if (videoPlayer == null)
            videoPlayer = FindObjectOfType<VideoPlayer>();

        if (screenTransform == null)
        {
            var go = GameObject.Find("screen");
            if (go != null) screenTransform = go.transform;
        }

        if (midZoneFrame == null)
            midZoneFrame = FindMidZoneFrame();

        EnsureFontAsset();
        if (syncStyleFromChatPanel)
            ApplyStyleFromChatPanel();
        EnsureMeasureCanvas();
        EnsureBubbleRoot();
        CacheMidAnchors();
        LoadJson();
        PreloadFontCharacters();
        SeekIndex(videoPlayer != null ? videoPlayer.time : 0d);

        if (records.Count == 0)
            Debug.LogError("[MemeBubble] 未加载到弹幕数据，请检查 classify/memes_joking.json");
    }

    void Update()
    {
        CleanupActiveList();
        if (videoPlayer == null || screenTransform == null || bubbleRoot == null) return;

        double vt = videoPlayer.time;
        if (lastVT >= 0d && vt + 0.5 < lastVT)
        {
            SeekIndex(vt);
            ClearActiveBubbles();
            lastSpawnTime = -999f;
        }
        lastVT = vt;

        if (!videoPlayer.isPrepared) return;

        // 只有成功生成才推进 index，避免并发满/间隔不够时丢弹幕
        while (recordIdx < records.Count && records[recordIdx].新视频中的时间 <= (float)vt)
        {
            if (!TrySpawnRecord(records[recordIdx]))
                break;
            recordIdx++;
        }
    }

    void EnsureFontAsset()
    {
        var popUp = FindObjectOfType<PopUpDanmakuController>();
        if (popUp != null && popUp.fontAsset != null)
            fontAsset = popUp.fontAsset;
    }

    void ApplyStyleFromChatPanel()
    {
        var chat = FindObjectOfType<SocializationPanelController>();
        if (chat == null) return;

        bubbleBgColor = chat.chatBubbleFillColor;
        bubbleBorderColor = chat.chatBubbleBorderColor;
        textColor = chat.messageTextColor;
        textPaddingH = chat.bubbleTextPaddingH;
        textPaddingV = chat.bubbleTextPaddingV;
        tailWidth = chat.bubbleTailWidth;
        tailHeight = chat.bubbleTailHeight;
        tailSideInset = chat.bubbleTailSideInset;
        bubbleBorderWidth = chat.bubbleBorderWidth;
    }

    void EnsureBubbleRoot()
    {
        if (bubbleRoot != null) return;

        if (screenTransform != null)
        {
            var existing = screenTransform.Find("MemeBubbleRoot");
            if (existing != null)
            {
                bubbleRoot = existing;
                return;
            }
        }

        var go = new GameObject("MemeBubbleRoot");
        if (screenTransform != null)
        {
            go.transform.SetParent(screenTransform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
        }
        bubbleRoot = go.transform;
    }

    static PopUpDanmakuZoneFrame FindMidZoneFrame()
    {
        var frames = FindObjectsOfType<PopUpDanmakuZoneFrame>();
        for (int i = 0; i < frames.Length; i++)
        {
            if (frames[i].zone == PopUpDanmakuZone.Mid)
                return frames[i];
        }
        return frames.Length > 0 ? frames[0] : null;
    }

    void CacheMidAnchors()
    {
        midAnchorLeft = null;
        midAnchorRight = null;
        if (midZoneFrame == null) return;

        midZoneFrame.EnsureAnchors();
        foreach (var anchor in midZoneFrame.Anchors)
        {
            if (anchor == null) continue;
            if (anchor.name.Contains("Left"))
                midAnchorLeft = anchor.transform;
            else if (anchor.name.Contains("Right"))
                midAnchorRight = anchor.transform;
        }
    }

    float GetMidHalfHeight()
    {
        return midZoneFrame != null ? midZoneFrame.frameSize.y * 0.5f : 0.4f;
    }

    void EnsureMeasureCanvas()
    {
        if (measureLabel != null) return;

        var root = new GameObject("MemeBubbleMeasure");
        root.transform.SetParent(transform, false);
        root.hideFlags = HideFlags.HideAndDontSave;

        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.enabled = false;

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(root.transform, false);
        var textRect = textGo.AddComponent<RectTransform>();
        textRect.sizeDelta = new Vector2(maxBubbleWidth, 400f);

        measureLabel = textGo.AddComponent<TextMeshProUGUI>();
        measureLabel.enableWordWrapping = true;
        measureLabel.overflowMode = TextOverflowModes.Overflow;
        measureLabel.alignment = TextAlignmentOptions.MidlineLeft;
        if (fontAsset != null) measureLabel.font = fontAsset;
    }

    void LoadJson()
    {
        records.Clear();
        string[] candidates = { jsonFileName, "classify/memes_joking.json", "PopUpDanmaku/pop_up_meme.json" };

        foreach (string candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            string path = Path.Combine(Application.streamingAssetsPath, candidate.Replace('\\', '/'));
            if (!File.Exists(path)) continue;

            var col = JsonUtility.FromJson<MemeBubbleCollection>(
                File.ReadAllText(path, System.Text.Encoding.UTF8).Trim());
            if (col?.items == null || col.items.Length == 0) continue;

            records.AddRange(col.items);
            records.Sort((a, b) => a.新视频中的时间.CompareTo(b.新视频中的时间));
            jsonFileName = candidate.Replace('\\', '/');
            Debug.Log($"[MemeBubble] 加载 {records.Count} 条：{path}");
            return;
        }

        Debug.LogError("[MemeBubble] JSON 未找到。");
    }

    void PreloadFontCharacters()
    {
        if (fontAsset == null || records.Count == 0) return;
        var sb = new System.Text.StringBuilder(512);
        foreach (var r in records) sb.Append(r.弹幕内容);
        fontAsset.TryAddCharacters(sb.ToString(), out _);
    }

    void SeekIndex(double fromTime)
    {
        recordIdx = 0;
        while (recordIdx < records.Count && records[recordIdx].新视频中的时间 < (float)fromTime)
            recordIdx++;
    }

    bool TrySpawnRecord(MemeBubbleRecord rec)
    {
        CleanupActiveList();
        if (activeInstances.Count >= maxConcurrent) return false;
        if (Time.time - lastSpawnTime < minSpawnGap) return false;

        float estHalfH = EstimateBubbleLocalHalfHeight(rec.弹幕内容);
        bool preferLeft = UnityEngine.Random.value < 0.5f;
        bool isLeft = preferLeft;
        if (!TryFindNonOverlappingPosition(preferLeft, estHalfH, out Vector3 localPos))
        {
            if (!TryFindNonOverlappingPosition(!preferLeft, estHalfH, out localPos))
                return false;
            isLeft = !preferLeft;
        }

        var go = BuildBubbleGo(rec.弹幕内容, isLeft);
        float actualHalfH = GetBubbleLocalHalfHeight(go, worldScale);
        if (!CanPlaceAt(localPos.y, actualHalfH, isLeft))
        {
            if (!TryFindNonOverlappingPosition(isLeft, actualHalfH, out localPos))
            {
                UiSpriteCleanupUtil.DestroyGeneratedSprites(go);
                Destroy(go);
                return false;
            }
        }

        float dwell = Mathf.Clamp(rec.长度 * 0.22f, dwellMin, dwellMax);

        go.transform.SetParent(bubbleRoot, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.identity;

        var inst = go.AddComponent<MemeBubbleInstance>();
        inst.Initialize(dwell, fadeOut, worldScale, isLeft, actualHalfH, OnBubbleFinished);
        activeInstances.Add(inst);
        lastSpawnTime = Time.time;
        return true;
    }

    void OnBubbleFinished(MemeBubbleInstance inst)
    {
        activeInstances.Remove(inst);
    }

    void ClearActiveBubbles()
    {
        for (int i = activeInstances.Count - 1; i >= 0; i--)
        {
            if (activeInstances[i] != null)
            {
                UiSpriteCleanupUtil.DestroyGeneratedSprites(activeInstances[i].gameObject);
                Destroy(activeInstances[i].gameObject);
            }
        }
        activeInstances.Clear();
    }

    void CleanupActiveList()
    {
        for (int i = activeInstances.Count - 1; i >= 0; i--)
        {
            if (activeInstances[i] == null)
                activeInstances.RemoveAt(i);
        }
    }

    float EstimateBubbleLocalHalfHeight(string text)
    {
        float iconW = laughingIcon != null ? iconSize : 0f;
        float contentMaxW = Mathf.Max(40f, maxBubbleWidth - iconW - textPaddingH * 2.5f);
        Vector2 bodySize = CalcBubbleSize(text, contentMaxW, iconW);
        return (bodySize.y + tailHeight) * worldScale * 0.5f;
    }

    static float GetBubbleLocalHalfHeight(GameObject go, float scale)
    {
        var rt = go.GetComponent<RectTransform>();
        return rt != null ? rt.sizeDelta.y * scale * 0.5f : 0f;
    }

    bool CanPlaceAt(float centerY, float localHalfHeight, bool leftSide)
    {
        if (localHalfHeight <= 0f) return false;

        float baseY = GetAnchorBaseLocalY(leftSide);
        float halfRange = GetMidHalfHeight() * verticalRangeRatio;
        float yMin = baseY - halfRange + localHalfHeight;
        float yMax = baseY + halfRange - localHalfHeight;
        if (centerY < yMin || centerY > yMax) return false;

        for (int i = 0; i < activeInstances.Count; i++)
        {
            var inst = activeInstances[i];
            if (inst == null || inst.IsLeft != leftSide) continue;

            float minDist = localHalfHeight + inst.LayoutHalfHeight + bubbleVerticalGap;
            if (Mathf.Abs(centerY - inst.transform.localPosition.y) < minDist)
                return false;
        }

        return true;
    }

    bool TryFindNonOverlappingPosition(bool leftSide, float localHalfHeight, out Vector3 localPos)
    {
        localPos = default;
        if (localHalfHeight <= 0f) return false;

        float baseY = GetAnchorBaseLocalY(leftSide);
        float halfRange = GetMidHalfHeight() * verticalRangeRatio;
        float yMin = baseY - halfRange + localHalfHeight;
        float yMax = baseY + halfRange - localHalfHeight;
        if (yMin > yMax) return false;

        const int attempts = 32;
        for (int i = 0; i < attempts; i++)
        {
            float y = UnityEngine.Random.Range(yMin, yMax);
            if (!CanPlaceAt(y, localHalfHeight, leftSide)) continue;
            localPos = ComposeAnchorLocalPos(leftSide, y);
            return true;
        }

        if (TryFindLargestGapCenter(leftSide, localHalfHeight, yMin, yMax, out float gapY))
        {
            localPos = ComposeAnchorLocalPos(leftSide, gapY);
            return true;
        }

        return false;
    }

    bool TryFindLargestGapCenter(bool leftSide, float localHalfHeight, float yMin, float yMax, out float centerY)
    {
        centerY = (yMin + yMax) * 0.5f;
        var blocked = new List<(float min, float max)>();

        for (int i = 0; i < activeInstances.Count; i++)
        {
            var inst = activeInstances[i];
            if (inst == null || inst.IsLeft != leftSide) continue;
            float cy = inst.transform.localPosition.y;
            float blockHalf = localHalfHeight + inst.LayoutHalfHeight + bubbleVerticalGap;
            blocked.Add((cy - blockHalf, cy + blockHalf));
        }

        blocked.Sort((a, b) => a.min.CompareTo(b.min));
        var merged = new List<(float min, float max)>();
        for (int i = 0; i < blocked.Count; i++)
        {
            if (merged.Count == 0 || blocked[i].min > merged[merged.Count - 1].max)
                merged.Add(blocked[i]);
            else
            {
                var last = merged[merged.Count - 1];
                merged[merged.Count - 1] = (last.min, Mathf.Max(last.max, blocked[i].max));
            }
        }

        float bestLen = -1f;
        float cursor = yMin;

        for (int i = 0; i <= merged.Count; i++)
        {
            float next = i < merged.Count ? merged[i].min : yMax;
            if (next > cursor)
            {
                float len = next - cursor;
                if (len > bestLen)
                {
                    bestLen = len;
                    centerY = (cursor + next) * 0.5f;
                }
            }

            if (i < merged.Count)
                cursor = Mathf.Max(cursor, merged[i].max);
        }

        return bestLen >= 0f && CanPlaceAt(centerY, localHalfHeight, leftSide);
    }

    float GetAnchorBaseLocalY(bool leftSide)
    {
        Transform anchor = leftSide ? midAnchorLeft : midAnchorRight;
        if (anchor != null && bubbleRoot != null)
            return bubbleRoot.InverseTransformPoint(anchor.position).y;

        return midZoneFrame != null ? midZoneFrame.transform.localPosition.y : 0f;
    }

    Vector3 ComposeAnchorLocalPos(bool leftSide, float centerY)
    {
        Transform anchor = leftSide ? midAnchorLeft : midAnchorRight;

        if (anchor != null && bubbleRoot != null)
        {
            Vector3 local = bubbleRoot.InverseTransformPoint(anchor.position);
            local.x += leftSide ? -sideOutwardOffset : sideOutwardOffset;
            local.y = centerY;
            local.z = ResolveSpawnLocalZ(local.z);
            return local;
        }

        float halfW = midZoneFrame != null ? midZoneFrame.frameSize.x * 0.5f : 0.64f;
        Vector3 frameLocal = midZoneFrame != null ? midZoneFrame.transform.localPosition : Vector3.zero;
        float x = leftSide
            ? frameLocal.x - halfW - sideOutwardOffset
            : frameLocal.x + halfW + sideOutwardOffset;
        float z = ResolveSpawnLocalZ(frameLocal.z);
        return new Vector3(x, centerY, z);
    }

    /// <summary>Mid 锚点常在视频平面后方；保证 Z 在 screen 本地视频平面（≈0）朝向相机一侧。</summary>
    float ResolveSpawnLocalZ(float anchorLocalZ)
    {
        const float videoPlaneLocalZ = 0f;
        return Mathf.Max(anchorLocalZ + spawnForwardOffset, videoPlaneLocalZ + spawnForwardOffset);
    }

    GameObject BuildBubbleGo(string text, bool isLeft)
    {
        float iconW = laughingIcon != null ? iconSize : 0f;
        float contentMaxW = Mathf.Max(40f, maxBubbleWidth - iconW - textPaddingH * 2.5f);
        Vector2 bodySize = CalcBubbleSize(text, contentMaxW, iconW);
        int bodyH = Mathf.RoundToInt(bodySize.y);
        int totalW = Mathf.RoundToInt(bodySize.x);
        int tailH = Mathf.RoundToInt(tailHeight);
        int tailW = Mathf.RoundToInt(tailWidth);
        int tailInset = Mathf.RoundToInt(tailSideInset);

        var root = new GameObject("MemeBubble");
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = canvasSortOrder;

        var rt = root.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(totalW, bodyH + tailH);
        // 靠视频一侧对齐：左侧气泡右缘贴锚点，右侧气泡左缘贴锚点
        rt.pivot = isLeft ? new Vector2(1f, 0.5f) : new Vector2(0f, 0.5f);
        rt.anchorMin = rt.pivot;
        rt.anchorMax = rt.pivot;
        root.AddComponent<CanvasGroup>();

        // 一体化对话框底图（填充 + 外圈描边）
        var shapeGo = MakeChild(root.transform, "BubbleShape");
        StretchFill(shapeGo, 0f, 0f);
        var shapeImg = shapeGo.AddComponent<Image>();
        var bubbleSprite = MemeBubbleShapeUtil.CreateUnifiedBubble(
            totalW, bodyH, isLeft, tailW, tailH, tailInset,
            Mathf.RoundToInt(bubbleBorderWidth), bubbleBgColor, bubbleBorderColor);
        MemeBubbleShapeUtil.ApplyUnifiedBubble(shapeImg, bubbleSprite);

        // 文字区在矩形主体内（避开底部尖角）
        var contentGo = MakeChild(root.transform, "Content");
        var contentRt = contentGo.GetComponent<RectTransform>();
        contentRt.anchorMin = Vector2.zero;
        contentRt.anchorMax = Vector2.one;
        contentRt.offsetMin = new Vector2(0f, tailH);
        contentRt.offsetMax = Vector2.zero;

        if (laughingIcon != null)
        {
            var iconGo = MakeChild(contentGo.transform, "Icon");
            var iconRt = iconGo.GetComponent<RectTransform>();
            iconRt.anchorMin = new Vector2(isLeft ? 0f : 1f, 0.5f);
            iconRt.anchorMax = new Vector2(isLeft ? 0f : 1f, 0.5f);
            iconRt.pivot = new Vector2(isLeft ? 0f : 1f, 0.5f);
            iconRt.sizeDelta = new Vector2(iconSize, iconSize);
            iconRt.anchoredPosition = new Vector2(isLeft ? textPaddingH : -textPaddingH, 0f);
            var iconImg = iconGo.AddComponent<Image>();
            iconImg.sprite = laughingIcon;
            iconImg.preserveAspect = true;
        }

        var textGo = MakeChild(contentGo.transform, "Text");
        var textRt = textGo.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        float left = isLeft ? textPaddingH * 1.5f + iconW : textPaddingH;
        float right = isLeft ? textPaddingH : textPaddingH * 1.5f + iconW;
        textRt.offsetMin = new Vector2(left, textPaddingV);
        textRt.offsetMax = new Vector2(-right, -textPaddingV);

        var label = textGo.AddComponent<TextMeshProUGUI>();
        if (fontAsset != null)
        {
            label.font = fontAsset;
            fontAsset.TryAddCharacters(text, out _);
        }
        label.text = text;
        label.fontSize = fontSize;
        label.color = textColor;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.enableWordWrapping = true;
        label.overflowMode = TextOverflowModes.Overflow;
        label.ForceMeshUpdate();

        return root;
    }

    Vector2 CalcBubbleSize(string text, float contentMaxW, float iconW)
    {
        if (measureLabel == null) EnsureMeasureCanvas();

        measureLabel.fontSize = fontSize;
        if (fontAsset != null) measureLabel.font = fontAsset;
        measureLabel.rectTransform.sizeDelta = new Vector2(contentMaxW, 400f);
        measureLabel.text = text;
        measureLabel.ForceMeshUpdate();

        Vector2 pref = measureLabel.GetPreferredValues(text, contentMaxW, 0f);

        if (pref.x < 2f || pref.y < 2f || pref.x > contentMaxW * 3f)
        {
            int chars = Mathf.Max(1, text.Length);
            int estLines = Mathf.Max(1, Mathf.CeilToInt(chars * fontSize * 0.55f / contentMaxW));
            pref.x = estLines == 1 ? Mathf.Min(chars * fontSize * 0.55f, contentMaxW) : contentMaxW;
            pref.y = estLines * fontSize * 1.25f;
        }

        float w = Mathf.Clamp(pref.x + iconW + textPaddingH * 2.5f, minBubbleWidth, maxBubbleWidth);
        float h = Mathf.Clamp(pref.y + textPaddingV * 2f, fontSize + textPaddingV * 2f, fontSize * 5f + textPaddingV * 2f);
        return new Vector2(w, h);
    }

    static void StretchFill(GameObject go, float expand, float expandY)
    {
        var r = go.GetComponent<RectTransform>();
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = new Vector2(-expand, -expandY);
        r.offsetMax = new Vector2(expand, expandY);
    }

    static GameObject MakeChild(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        return go;
    }
}
