using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Video;
using UnityEngine.XR.Interaction.Toolkit.UI;

/// <summary>
/// 社交互动/喊话/分享弹幕：固定在头显视角下方、随头部转动的科技感聊天面板。
/// 平时收起为一条小状态条，有新弹幕只提示红点，点击展开后以聊天室样式滚动显示。
/// 与 MemeBubbleController（表情包/玩梗气泡）完全独立，互不影响。
/// </summary>
[DisallowMultipleComponent]
public class SocializationPanelController : MonoBehaviour
{
    [Header("场景引用")]
    public VideoPlayer videoPlayer;
    [Tooltip("留空则自动使用 Camera.main（头显主相机）")]
    public Transform headTransform;
    public TMP_FontAsset fontAsset;

    [Header("JSON")]
    public string jsonFileName = "classify/socialization.json";

    [Header("头显跟随（相对相机本地坐标，Z 为前方距离）")]
    [Tooltip("展开后面板的位置")]
    public Vector3 localOffset = new Vector3(0f, -0.14f, 0.85f);
    [Tooltip("收起时「点击展开聊天室」按钮的位置（默认在视野更靠下）")]
    public Vector3 collapsedLocalOffset = new Vector3(0f, -0.34f, 0.85f);
    [Tooltip("面板倾斜角度：X 为俯仰（正值让顶部往里倾，像平板立起来一点看向你），Y 为左右偏转，Z 为翻滚。\n" +
        "Play 模式下可在 Hierarchy 里选中 Main Camera 下的 SocializationPanelRoot，用旋转工具(E)实时试角度，\n" +
        "满意后把 Inspector 里 Rotation 的数值抄回这里的 Local Euler Angles（退出 Play 模式后手动改）。")]
    public Vector3 localEulerAngles = new Vector3(12f, 0f, 0f);
    [Range(0.0006f, 0.004f)] public float canvasScale = 0.0016f;

    [Header("折叠条 / 展开面板大小")]
    public Vector2 collapsedSize = new Vector2(300f, 64f);
    [Tooltip("横向拉宽一些，做成横屏平板的比例")]
    public Vector2 expandedSize = new Vector2(700f, 420f);

    [Header("聊天消息")]
    [Tooltip("内存里最多保留多少条历史消息（超出后最早的会被移除）；面板支持拖动滚动条查看历史")]
    [Range(6, 80)] public int maxVisibleMessages = 30;
    [Tooltip("新消息到达时，若用户当前正停留在底部附近，则自动滚到最新；否则不打扰用户翻看历史")]
    [Range(0f, 0.3f)] public float autoScrollThreshold = 0.06f;
    [Range(12f, 40f)] public float fontSize = 22f;
    [Range(12f, 40f)] public float titleFontSize = 24f;
    [Tooltip("「聊天室」标题文字偏移：X 正值往右，Y 负值往下（相对标题栏左上角）")]
    public Vector2 titleTextOffset = new Vector2(8f, -4f);

    [Header("弹幕气泡（与表情包气泡同款矩形+尖角形状）")]
    public float avatarSize = 46f;
    public float bubbleTextPaddingH = 22f;
    public float bubbleTextPaddingV = 14f;
    public float bubbleTailWidth = 16f;
    public float bubbleTailHeight = 12f;
    public float bubbleTailSideInset = 10f;
    [Range(1f, 6f)] public float bubbleBorderWidth = 2.5f;
    public Color chatBubbleFillColor = new Color(0.07f, 0.1f, 0.22f, 0.8f);
    public Color chatBubbleBorderColor = new Color(0.35f, 0.85f, 1f, 0.92f);
    public Color[] avatarPalette =
    {
        new Color(0.3f, 0.85f, 1f, 1f),
        new Color(0.66f, 0.4f, 1f, 1f),
        new Color(1f, 0.45f, 0.75f, 1f),
        new Color(0.4f, 1f, 0.85f, 1f)
    };

    [Header("赛博朋克配色")]
    public Color panelFillColor = new Color(0.04f, 0.06f, 0.15f, 0.6f);
    public Color borderColorA = new Color(0.25f, 0.9f, 1f, 0.95f);
    public Color borderColorB = new Color(0.66f, 0.36f, 1f, 0.95f);
    public Color glowColor = new Color(0.42f, 0.55f, 1f, 0.55f);
    public Color messageTextColor = new Color(0.87f, 0.95f, 1f, 1f);
    public Color titleTextColor = new Color(0.75f, 0.92f, 1f, 1f);
    public Color redDotColor = new Color(1f, 0.22f, 0.3f, 1f);
    [Range(1f, 10f)] public float borderWidth = 3f;
    [Range(4, 60)] public int cornerRadius = 20;
    [Range(0, 60)] public int glowSize = 22;
    [Range(0, 300)] public int canvasSortOrder = 220;

    [Header("滚动条")]
    [Range(4f, 14f)] public float scrollbarWidth = 8f;
    public Color scrollbarTrackColor = new Color(0.12f, 0.14f, 0.18f, 0.45f);
    public Color scrollbarHandleColor = new Color(0.52f, 0.54f, 0.58f, 0.88f);

    [Header("聊天列表边距")]
    [Tooltip("对话框整体向右偏移，避免左侧头像/尖角被裁切")]
    public float chatContentPaddingLeft = 14f;

    RectTransform expandedRoot;
    RectTransform collapsedRoot;
    RectTransform collapsedRt;
    RectTransform expandedRt;
    GameObject collapsedGo;
    GameObject expandedGo;
    RectTransform contentRt;
    ScrollRect scrollRect;
    Scrollbar verticalScrollbar;
    GameObject redDotGo;
    TMP_FontAsset resolvedFont;
    TextMeshProUGUI measureLabel;

    bool isExpanded;
    bool hasUnread;
    bool initialized;
    int messageSeq;

    readonly List<SocializationRecord> records = new();
    readonly Queue<GameObject> activeItems = new();
    int recordIdx;
    double lastVT = -1d;

    [Serializable]
    class SocializationRecord
    {
        public string 弹幕内容;
        public int 长度;
        public float 新视频中的时间;
    }

    [Serializable]
    class SocializationCollection
    {
        public SocializationRecord[] items;
    }

    void OnDisable()
    {
        initialized = false;
    }

    void Start()
    {
        if (initialized) return;
        initialized = true;

        if (videoPlayer == null)
            videoPlayer = FindObjectOfType<VideoPlayer>();
        if (headTransform == null && Camera.main != null)
            headTransform = Camera.main.transform;

        EnsureFontAsset();
        EnsureEventSystem();
        BuildUi();
        LoadJson();
        PreloadFontCharacters();
        SeekIndex(videoPlayer != null ? videoPlayer.time : 0d);

        if (records.Count == 0)
            Debug.LogError("[SocializationPanel] 未加载到弹幕数据，请检查 classify/socialization.json");
    }

    void Update()
    {
        if (videoPlayer == null || expandedRoot == null) return;

        double vt = videoPlayer.time;
        if (lastVT >= 0d && vt + 0.5 < lastVT)
        {
            SeekIndex(vt);
            ClearMessages();
        }
        lastVT = vt;

        if (!videoPlayer.isPrepared) return;

        while (recordIdx < records.Count && records[recordIdx].新视频中的时间 <= (float)vt)
        {
            SpawnMessage(records[recordIdx].弹幕内容);
            recordIdx++;
        }
    }

    void EnsureFontAsset()
    {
        if (fontAsset != null)
        {
            resolvedFont = fontAsset;
            return;
        }

        var popUp = FindObjectOfType<PopUpDanmakuController>();
        if (popUp != null && popUp.fontAsset != null)
        {
            resolvedFont = popUp.fontAsset;
            return;
        }

        var meme = FindObjectOfType<MemeBubbleController>();
        if (meme != null && meme.fontAsset != null)
            resolvedFont = meme.fontAsset;
    }

    void LoadJson()
    {
        records.Clear();
        string[] candidates = { jsonFileName, "classify/socialization.json" };

        foreach (string candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            string path = Path.Combine(Application.streamingAssetsPath, candidate.Replace('\\', '/'));
            if (!File.Exists(path)) continue;

            var col = JsonUtility.FromJson<SocializationCollection>(
                File.ReadAllText(path, System.Text.Encoding.UTF8).Trim());
            if (col?.items == null || col.items.Length == 0) continue;

            records.AddRange(col.items);
            records.Sort((a, b) => a.新视频中的时间.CompareTo(b.新视频中的时间));
            jsonFileName = candidate.Replace('\\', '/');
            Debug.Log($"[SocializationPanel] 加载 {records.Count} 条：{path}");
            return;
        }

        Debug.LogError("[SocializationPanel] JSON 未找到。");
    }

    void PreloadFontCharacters()
    {
        if (resolvedFont == null || records.Count == 0) return;
        var sb = new System.Text.StringBuilder(512);
        foreach (var r in records) sb.Append(r.弹幕内容);
        resolvedFont.TryAddCharacters(sb.ToString(), out _);
    }

    void SeekIndex(double fromTime)
    {
        recordIdx = 0;
        while (recordIdx < records.Count && records[recordIdx].新视频中的时间 < (float)fromTime)
            recordIdx++;
    }

    // ---------- UI 构建 ----------

    void BuildUi()
    {
        BuildExpandedRoot();
        BuildCollapsedRoot();
        SetExpanded(false);
    }

    void BuildExpandedRoot()
    {
        var rootGo = new GameObject("SocializationExpandedRoot", typeof(RectTransform));
        expandedRoot = rootGo.GetComponent<RectTransform>();
        SetupWorldCanvas(rootGo, expandedSize);

        if (headTransform != null)
        {
            rootGo.transform.SetParent(headTransform, false);
            rootGo.transform.localPosition = localOffset;
            rootGo.transform.localRotation = Quaternion.Euler(localEulerAngles);
        }
        rootGo.transform.localScale = Vector3.one * canvasScale;

        BuildExpandedPanel();
    }

    void BuildCollapsedRoot()
    {
        var rootGo = new GameObject("SocializationCollapsedRoot", typeof(RectTransform));
        collapsedRoot = rootGo.GetComponent<RectTransform>();
        SetupWorldCanvas(rootGo, collapsedSize);

        if (headTransform != null)
        {
            rootGo.transform.SetParent(headTransform, false);
            rootGo.transform.localPosition = collapsedLocalOffset;
            rootGo.transform.localRotation = Quaternion.Euler(localEulerAngles);
        }
        rootGo.transform.localScale = Vector3.one * canvasScale;

        BuildCollapsedBar();
    }

    void SetupWorldCanvas(GameObject rootGo, Vector2 size)
    {
        var canvas = rootGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = canvasSortOrder;
        rootGo.AddComponent<CanvasScaler>();
        rootGo.AddComponent<GraphicRaycaster>();
        rootGo.AddComponent<TrackedDeviceGraphicRaycaster>();

        var rt = rootGo.GetComponent<RectTransform>();
        rt.sizeDelta = size;
    }

    void BuildCollapsedBar()
    {
        collapsedGo = new GameObject("CollapsedBar", typeof(RectTransform));
        collapsedGo.transform.SetParent(collapsedRoot, false);
        collapsedRt = collapsedGo.GetComponent<RectTransform>();
        Stretch(collapsedRt, 0f, 0f);

        var bgSprite = SocializationPanelShapeUtil.CreatePanel(
            Mathf.RoundToInt(collapsedRoot.sizeDelta.x), Mathf.RoundToInt(collapsedRoot.sizeDelta.y),
            Mathf.RoundToInt(collapsedRoot.sizeDelta.y * 0.5f), Mathf.Max(4, glowSize / 2), borderWidth,
            panelFillColor, borderColorA, borderColorB, glowColor);

        var bgImg = collapsedGo.AddComponent<Image>();
        SocializationPanelShapeUtil.Apply(bgImg, bgSprite);

        var button = collapsedGo.AddComponent<Button>();
        button.targetGraphic = bgImg;
        button.onClick.AddListener(() => SetExpanded(true));
        collapsedGo.AddComponent<UiPressEffect>();

        var labelGo = MakeChild(collapsedRt, "Label");
        var labelRt = labelGo.GetComponent<RectTransform>();
        Stretch(labelRt, -18f, -6f);
        var label = labelGo.AddComponent<TextMeshProUGUI>();
        ApplyFont(label);
        label.text = "点击展开聊天室";
        label.fontSize = fontSize * 0.72f;
        label.color = titleTextColor;
        label.alignment = TextAlignmentOptions.Center;
        label.overflowMode = TextOverflowModes.Overflow;

        redDotGo = new GameObject("RedDot", typeof(RectTransform));
        redDotGo.transform.SetParent(collapsedRt, false);
        var dotRt = redDotGo.GetComponent<RectTransform>();
        dotRt.anchorMin = new Vector2(1f, 1f);
        dotRt.anchorMax = new Vector2(1f, 1f);
        dotRt.pivot = new Vector2(0.5f, 0.5f);
        dotRt.sizeDelta = new Vector2(22f, 22f);
        dotRt.anchoredPosition = new Vector2(-8f, -8f);
        var dotImg = redDotGo.AddComponent<Image>();
        var dotSprite = SocializationPanelShapeUtil.CreateGlowDot(22, redDotColor,
            new Color(1f, 0.55f, 0.6f, 1f), new Color(1f, 0.2f, 0.3f, 0.65f));
        SocializationPanelShapeUtil.Apply(dotImg, dotSprite);
        redDotGo.SetActive(false);
    }

    void BuildExpandedPanel()
    {
        expandedGo = new GameObject("ExpandedPanel", typeof(RectTransform));
        expandedGo.transform.SetParent(expandedRoot, false);
        expandedRt = expandedGo.GetComponent<RectTransform>();
        Stretch(expandedRt, 0f, 0f);

        var bgSprite = SocializationPanelShapeUtil.CreatePanel(
            Mathf.RoundToInt(expandedRoot.sizeDelta.x), Mathf.RoundToInt(expandedRoot.sizeDelta.y),
            cornerRadius, glowSize, borderWidth,
            panelFillColor, borderColorA, borderColorB, glowColor);
        var bgGo = MakeChild(expandedRt, "Background");
        Stretch(bgGo.GetComponent<RectTransform>(), 0f, 0f);
        var bgImg = bgGo.AddComponent<Image>();
        SocializationPanelShapeUtil.Apply(bgImg, bgSprite);

        BuildFloatingDecor(expandedRt);

        // 标题栏
        var headerGo = MakeChild(expandedRt, "Header");
        var headerRt = headerGo.GetComponent<RectTransform>();
        headerRt.anchorMin = new Vector2(0f, 1f);
        headerRt.anchorMax = new Vector2(1f, 1f);
        headerRt.pivot = new Vector2(0.5f, 1f);
        headerRt.sizeDelta = new Vector2(0f, 56f);
        headerRt.anchoredPosition = Vector2.zero;

        var titleGo = MakeChild(headerRt, "Title");
        var titleRt = titleGo.GetComponent<RectTransform>();
        titleRt.anchorMin = new Vector2(0f, 0f);
        titleRt.anchorMax = new Vector2(1f, 1f);
        titleRt.offsetMin = new Vector2(24f + titleTextOffset.x, titleTextOffset.y);
        titleRt.offsetMax = new Vector2(-56f, titleTextOffset.y);
        var title = titleGo.AddComponent<TextMeshProUGUI>();
        ApplyFont(title);
        title.text = "聊天室";
        title.fontSize = titleFontSize;
        title.color = titleTextColor;
        title.alignment = TextAlignmentOptions.MidlineLeft;
        title.overflowMode = TextOverflowModes.Overflow;

        var closeGo = MakeChild(headerRt, "CollapseButton");
        var closeRt = closeGo.GetComponent<RectTransform>();
        closeRt.anchorMin = new Vector2(1f, 0.5f);
        closeRt.anchorMax = new Vector2(1f, 0.5f);
        closeRt.pivot = new Vector2(1f, 0.5f);
        closeRt.sizeDelta = new Vector2(40f, 40f);
        closeRt.anchoredPosition = new Vector2(-16f, 0f);
        var closeImg = closeGo.AddComponent<Image>();
        var closeSprite = SocializationPanelShapeUtil.CreatePanel(40, 40, 10, 8, 2f,
            new Color(0.1f, 0.14f, 0.24f, 0.8f), borderColorA, borderColorB, glowColor);
        SocializationPanelShapeUtil.Apply(closeImg, closeSprite);
        var closeButton = closeGo.AddComponent<Button>();
        closeButton.targetGraphic = closeImg;
        closeButton.onClick.AddListener(() => SetExpanded(false));
        closeGo.AddComponent<UiPressEffect>();
        var closeLabelGo = MakeChild(closeRt, "X");
        Stretch(closeLabelGo.GetComponent<RectTransform>(), 0f, 0f);
        var closeLabel = closeLabelGo.AddComponent<TextMeshProUGUI>();
        ApplyFont(closeLabel);
        closeLabel.text = "×";
        closeLabel.fontSize = 26f;
        closeLabel.color = titleTextColor;
        closeLabel.alignment = TextAlignmentOptions.Center;

        // 分隔线
        var lineGo = MakeChild(expandedRt, "HeaderLine");
        var lineRt = lineGo.GetComponent<RectTransform>();
        lineRt.anchorMin = new Vector2(0f, 1f);
        lineRt.anchorMax = new Vector2(1f, 1f);
        lineRt.pivot = new Vector2(0.5f, 1f);
        lineRt.sizeDelta = new Vector2(-40f, 2f);
        lineRt.anchoredPosition = new Vector2(0f, -56f);
        var lineImg = lineGo.AddComponent<Image>();
        lineImg.color = new Color(borderColorA.r, borderColorA.g, borderColorA.b, 0.5f);

        // 聊天裁剪区：整块内容（含滚动条）严格限制在面板内，底部多留边避免对话框探出
        var clipGo = MakeChild(expandedRt, "ChatClipFrame");
        var clipRt = clipGo.GetComponent<RectTransform>();
        clipRt.anchorMin = Vector2.zero;
        clipRt.anchorMax = Vector2.one;
        clipRt.offsetMin = new Vector2(14f, 24f);
        clipRt.offsetMax = new Vector2(-14f, -58f);
        clipGo.AddComponent<RectMask2D>();

        var scrollGo = MakeChild(clipRt, "ScrollView");
        var scrollRt = scrollGo.GetComponent<RectTransform>();
        Stretch(scrollRt, 0f, 0f);

        var viewportGo = MakeChild(scrollRt, "Viewport");
        var viewportRt = viewportGo.GetComponent<RectTransform>();
        viewportRt.anchorMin = Vector2.zero;
        viewportRt.anchorMax = Vector2.one;
        viewportRt.offsetMin = new Vector2(4f, 6f);
        viewportRt.offsetMax = new Vector2(-(scrollbarWidth + 8f), -4f);
        viewportGo.AddComponent<RectMask2D>();

        var contentGo = MakeChild(viewportRt, "Content");
        contentRt = contentGo.GetComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;

        var layout = contentGo.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.spacing = 12f;
        layout.padding = new RectOffset(
            Mathf.RoundToInt(chatContentPaddingLeft), 0, 6, 10);

        var fitter = contentGo.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        // 竖直滚动条（细轨道 + 小滑块）
        var scrollbarGo = MakeChild(scrollRt, "Scrollbar");
        var scrollbarRt = scrollbarGo.GetComponent<RectTransform>();
        scrollbarRt.anchorMin = new Vector2(1f, 0f);
        scrollbarRt.anchorMax = new Vector2(1f, 1f);
        scrollbarRt.pivot = new Vector2(1f, 0.5f);
        scrollbarRt.sizeDelta = new Vector2(scrollbarWidth, 0f);
        scrollbarRt.anchoredPosition = Vector2.zero;

        var trackImg = scrollbarGo.AddComponent<Image>();
        int trackW = Mathf.Max(4, Mathf.RoundToInt(scrollbarWidth));
        var trackSprite = SocializationPanelShapeUtil.CreateSliceablePill(
            trackW, trackW * 2, scrollbarTrackColor, scrollbarTrackColor, 0f);
        trackImg.sprite = trackSprite;
        trackImg.type = Image.Type.Sliced;
        trackImg.color = Color.white;
        trackImg.raycastTarget = true;

        var slidingAreaGo = MakeChild(scrollbarRt, "SlidingArea");
        var slidingAreaRt = slidingAreaGo.GetComponent<RectTransform>();
        slidingAreaRt.anchorMin = Vector2.zero;
        slidingAreaRt.anchorMax = Vector2.one;
        slidingAreaRt.offsetMin = new Vector2(1f, 4f);
        slidingAreaRt.offsetMax = new Vector2(-1f, -4f);

        var handleGo = MakeChild(slidingAreaRt, "Handle");
        var handleRt = handleGo.GetComponent<RectTransform>();
        handleRt.anchorMin = Vector2.zero;
        handleRt.anchorMax = Vector2.one;
        handleRt.pivot = new Vector2(0.5f, 0.5f);
        handleRt.offsetMin = Vector2.zero;
        handleRt.offsetMax = Vector2.zero;
        var handleImg = handleGo.AddComponent<Image>();
        int handleW = Mathf.Max(4, trackW - 2);
        var handleSprite = SocializationPanelShapeUtil.CreateSliceablePill(
            handleW, handleW * 2, scrollbarHandleColor, scrollbarHandleColor, 0f);
        handleImg.sprite = handleSprite;
        handleImg.type = Image.Type.Sliced;
        handleImg.color = Color.white;

        verticalScrollbar = scrollbarGo.AddComponent<Scrollbar>();
        verticalScrollbar.direction = Scrollbar.Direction.BottomToTop;
        verticalScrollbar.targetGraphic = handleImg;
        verticalScrollbar.handleRect = handleRt;

        scrollRect = scrollGo.AddComponent<ScrollRect>();
        scrollRect.content = contentRt;
        scrollRect.viewport = viewportRt;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.verticalScrollbar = verticalScrollbar;
        scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 18f;
    }

    void BuildFloatingDecor(RectTransform parent)
    {
        AddFloatingSquare(parent, new Vector2(-26f, 40f), 22f, 0.9f, -1f);
        AddFloatingSquare(parent, new Vector2(-16f, 8f), 17f, 1.15f, 1f);
        AddFloatingSquare(parent, new Vector2(22f, 52f), 15f, 1.05f, 1f, anchorRight: true);
        AddFloatingSquare(parent, new Vector2(18f, 12f), 19f, 0.85f, -1f, anchorRight: true);
    }

    void AddFloatingSquare(RectTransform parent, Vector2 anchoredPos, float size, float speed, float rotDir, bool anchorRight = false)
    {
        var go = new GameObject("FloatDecor", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(anchorRight ? 1f : 0f, 0.5f);
        rt.anchorMax = rt.anchorMin;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(size, size);
        rt.anchoredPosition = anchoredPos;
        rt.localRotation = Quaternion.Euler(0f, 0f, 45f);

        var img = go.AddComponent<Image>();
        var sprite = SocializationPanelShapeUtil.CreatePanel(
            Mathf.RoundToInt(size), Mathf.RoundToInt(size), Mathf.RoundToInt(size * 0.2f),
            Mathf.Max(3, Mathf.RoundToInt(size * 0.5f)), 1.6f,
            new Color(panelFillColor.r, panelFillColor.g, panelFillColor.b, 0.3f),
            borderColorA, borderColorB, glowColor);
        SocializationPanelShapeUtil.Apply(img, sprite);

        var decor = go.AddComponent<FloatingGlowDecor>();
        decor.floatAmplitude = size * 0.35f;
        decor.floatSpeed = speed;
        decor.rotateSpeed = 14f * rotDir;
    }

    void SetExpanded(bool expanded)
    {
        isExpanded = expanded;
        if (collapsedRoot != null) collapsedRoot.gameObject.SetActive(!expanded);
        if (expandedRoot != null) expandedRoot.gameObject.SetActive(expanded);

        if (expanded)
        {
            hasUnread = false;
            if (redDotGo != null) redDotGo.SetActive(false);
            ScrollToBottom();
        }
    }

    // ---------- 消息 ----------

    void SpawnMessage(string text)
    {
        if (contentRt == null) return;

        bool wasNearBottom = scrollRect == null || activeItems.Count == 0 || scrollRect.verticalNormalizedPosition <= autoScrollThreshold;

        var itemGo = new GameObject("ChatItem", typeof(RectTransform));
        itemGo.transform.SetParent(contentRt, false);
        var itemRt = itemGo.GetComponent<RectTransform>();
        itemRt.anchorMin = new Vector2(0f, 1f);
        itemRt.anchorMax = new Vector2(1f, 1f);
        itemRt.pivot = new Vector2(0.5f, 1f);

        var rowLayout = itemGo.AddComponent<HorizontalLayoutGroup>();
        rowLayout.childAlignment = TextAnchor.UpperLeft;
        rowLayout.childControlWidth = false;
        rowLayout.childControlHeight = false;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = false;
        rowLayout.spacing = 10f;
        rowLayout.padding = new RectOffset(0, 0, 2, 2);
        var rowFitter = itemGo.AddComponent<ContentSizeFitter>();
        rowFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        rowFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        var avatarGo = BuildAvatar(messageSeq);
        avatarGo.transform.SetParent(itemRt, false);

        var bubbleGo = BuildChatBubble(text);
        bubbleGo.transform.SetParent(itemRt, false);

        messageSeq++;

        itemGo.AddComponent<CanvasGroup>();
        var anim = itemGo.AddComponent<SocializationChatItem>();
        anim.Play();

        activeItems.Enqueue(itemGo);
        while (activeItems.Count > maxVisibleMessages)
        {
            var old = activeItems.Dequeue();
            if (old != null) Destroy(old);
        }

        if (isExpanded && wasNearBottom)
            ScrollToBottom();
        else
            SyncScrollbarSize();

        if (!isExpanded)
        {
            hasUnread = true;
            if (redDotGo != null) redDotGo.SetActive(true);
        }
    }

    void ScrollToBottom()
    {
        if (scrollRect == null) return;
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(contentRt);
        scrollRect.verticalNormalizedPosition = 0f;
        SyncScrollbarSize();
    }

    void SyncScrollbarSize()
    {
        if (verticalScrollbar == null || scrollRect?.viewport == null || contentRt == null) return;

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(contentRt);

        float viewportH = scrollRect.viewport.rect.height;
        float contentH = contentRt.rect.height;
        if (viewportH <= 1f) return;

        // 一页内容：滑块与轨道同高；内容越多滑块越短（宽度不变）
        verticalScrollbar.size = contentH <= viewportH + 1f
            ? 1f
            : Mathf.Clamp01(viewportH / contentH);
    }

    GameObject BuildAvatar(int seed)
    {
        var go = new GameObject("Avatar", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        var layoutEl = go.AddComponent<LayoutElement>();
        layoutEl.preferredWidth = avatarSize;
        layoutEl.preferredHeight = avatarSize;
        layoutEl.minWidth = avatarSize;
        layoutEl.minHeight = avatarSize;
        rt.sizeDelta = new Vector2(avatarSize, avatarSize);

        Color tint = (avatarPalette != null && avatarPalette.Length > 0)
            ? avatarPalette[Mathf.Abs(seed) % avatarPalette.Length]
            : borderColorA;

        var ringImg = go.AddComponent<Image>();
        var ringSprite = SocializationPanelShapeUtil.CreateGlowDot(
            Mathf.RoundToInt(avatarSize), new Color(0.06f, 0.08f, 0.18f, 0.85f), tint,
            new Color(tint.r, tint.g, tint.b, 0.55f), 7);
        SocializationPanelShapeUtil.Apply(ringImg, ringSprite);

        var coreGo = MakeChild(rt, "Core");
        var coreRt = coreGo.GetComponent<RectTransform>();
        coreRt.anchorMin = new Vector2(0.5f, 0.5f);
        coreRt.anchorMax = new Vector2(0.5f, 0.5f);
        coreRt.pivot = new Vector2(0.5f, 0.5f);
        float coreSize = avatarSize * 0.46f;
        coreRt.sizeDelta = new Vector2(coreSize, coreSize);
        var coreImg = coreGo.AddComponent<Image>();
        var coreSprite = SocializationPanelShapeUtil.CreateGlowDot(Mathf.RoundToInt(coreSize), tint, tint, Color.clear, 0);
        SocializationPanelShapeUtil.Apply(coreImg, coreSprite);

        return go;
    }

    GameObject BuildChatBubble(string text)
    {
        float maxContentWidth = Mathf.Max(60f, GetAvailableBubbleWidth() - bubbleTextPaddingH * 2f);
        Vector2 pref = CalcBubbleContentSize(text, maxContentWidth);

        int bodyW = Mathf.RoundToInt(Mathf.Clamp(pref.x + bubbleTextPaddingH * 2f, fontSize * 3f, GetAvailableBubbleWidth()));
        int bodyH = Mathf.RoundToInt(Mathf.Clamp(pref.y + bubbleTextPaddingV * 2f, fontSize + bubbleTextPaddingV * 2f, fontSize * 10f + bubbleTextPaddingV * 2f));
        int tailW = Mathf.RoundToInt(bubbleTailWidth);
        int tailH = Mathf.RoundToInt(bubbleTailHeight);
        int tailInset = Mathf.RoundToInt(bubbleTailSideInset);

        var go = new GameObject("Bubble", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(bodyW, bodyH + tailH);

        var layoutEl = go.AddComponent<LayoutElement>();
        layoutEl.preferredWidth = bodyW;
        layoutEl.preferredHeight = bodyH + tailH;
        layoutEl.minWidth = bodyW;
        layoutEl.minHeight = bodyH + tailH;

        var shapeImg = go.AddComponent<Image>();
        var sprite = MemeBubbleShapeUtil.CreateUnifiedBubble(
            bodyW, bodyH, true, tailW, tailH, tailInset,
            Mathf.RoundToInt(bubbleBorderWidth), chatBubbleFillColor, chatBubbleBorderColor);
        MemeBubbleShapeUtil.ApplyUnifiedBubble(shapeImg, sprite);

        var textGo = MakeChild(rt, "Text");
        var textRt = textGo.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(bubbleTextPaddingH, tailH + bubbleTextPaddingV);
        textRt.offsetMax = new Vector2(-bubbleTextPaddingH, -bubbleTextPaddingV);

        var label = textGo.AddComponent<TextMeshProUGUI>();
        ApplyFont(label);
        if (resolvedFont != null) resolvedFont.TryAddCharacters(text, out _);
        label.text = text;
        label.fontSize = fontSize;
        label.color = messageTextColor;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.enableWordWrapping = true;
        label.overflowMode = TextOverflowModes.Overflow;

        return go;
    }

    float GetAvailableBubbleWidth()
    {
        // ChatClipFrame 左右各 14 + Viewport 内边距 + 滚动条预留 + 头像 + 间距
        const float clipInsetH = 28f;
        const float viewportPad = 12f;
        float scrollbarReserve = scrollbarWidth + 8f;
        float viewportWidth = expandedSize.x - clipInsetH - viewportPad - scrollbarReserve - chatContentPaddingLeft;
        return Mathf.Max(120f, viewportWidth - avatarSize - 10f - 8f);
    }

    void EnsureMeasureLabel()
    {
        if (measureLabel != null) return;

        var measureRoot = new GameObject("SocializationMeasure");
        measureRoot.transform.SetParent(transform, false);
        measureRoot.hideFlags = HideFlags.HideAndDontSave;

        var canvas = measureRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.enabled = false;

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(measureRoot.transform, false);
        var textRect = textGo.AddComponent<RectTransform>();
        textRect.sizeDelta = new Vector2(600f, 600f);

        measureLabel = textGo.AddComponent<TextMeshProUGUI>();
        measureLabel.enableWordWrapping = true;
        measureLabel.overflowMode = TextOverflowModes.Overflow;
        measureLabel.alignment = TextAlignmentOptions.TopLeft;
        if (resolvedFont != null) measureLabel.font = resolvedFont;
    }

    Vector2 CalcBubbleContentSize(string text, float maxContentWidth)
    {
        EnsureMeasureLabel();
        measureLabel.fontSize = fontSize;
        if (resolvedFont != null) measureLabel.font = resolvedFont;
        measureLabel.rectTransform.sizeDelta = new Vector2(maxContentWidth, 600f);
        measureLabel.text = text;
        measureLabel.ForceMeshUpdate();

        Vector2 pref = measureLabel.GetPreferredValues(text, maxContentWidth, 0f);

        if (pref.x < 2f || pref.y < 2f || pref.x > maxContentWidth * 3f)
        {
            int chars = Mathf.Max(1, text.Length);
            int estLines = Mathf.Max(1, Mathf.CeilToInt(chars * fontSize * 0.55f / maxContentWidth));
            pref.x = estLines == 1 ? Mathf.Min(chars * fontSize * 0.55f, maxContentWidth) : maxContentWidth;
            pref.y = estLines * fontSize * 1.25f;
        }

        return pref;
    }

    void ClearMessages()
    {
        while (activeItems.Count > 0)
        {
            var go = activeItems.Dequeue();
            if (go != null) Destroy(go);
        }
        hasUnread = false;
        if (redDotGo != null) redDotGo.SetActive(false);
        ScrollToBottom();
    }

    // ---------- 工具 ----------

    void ApplyFont(TextMeshProUGUI label)
    {
        if (resolvedFont != null) label.font = resolvedFont;
    }

    static GameObject MakeChild(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    static void Stretch(RectTransform rect, float expandX, float expandY)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(-expandX, -expandY);
        rect.offsetMax = new Vector2(expandX, expandY);
    }

    static void EnsureEventSystem()
    {
        var modules = FindObjectsOfType<XRUIInputModule>(true);
        if (modules.Length > 0) return;

        var eventSystems = FindObjectsOfType<EventSystem>(true);
        if (eventSystems.Length > 0)
        {
            var module = eventSystems[0].gameObject.AddComponent<XRUIInputModule>();
            module.enableMouseInput = true;
            module.enableTouchInput = true;
            module.enableBuiltinActionsAsFallback = true;
            return;
        }

        var eventSystemGo = new GameObject("EventSystem");
        eventSystemGo.AddComponent<EventSystem>();
        var created = eventSystemGo.AddComponent<XRUIInputModule>();
        created.enableMouseInput = true;
        created.enableTouchInput = true;
        created.enableBuiltinActionsAsFallback = true;
    }
}
