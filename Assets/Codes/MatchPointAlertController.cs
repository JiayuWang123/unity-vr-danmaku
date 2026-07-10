using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// 赛点/剧透弹幕提醒：在画面顶部弹出一条像警戒线/施工警示带一样的长条横幅（斜纹边框+半透明底+文字），
/// 弹出时同步触发手柄震动。跟其它"氛围向"弹幕（情绪气泡/梗图气泡）不同，这个是"提醒向"的，
/// 所以固定一次只显示一条，多条同时触发就排队依次弹出，可以单独控制排队间隔。
/// </summary>
public class MatchPointAlertController : MonoBehaviour
{
    [Header("场景引用")]
    public VideoPlayer videoPlayer;
    [Tooltip("留空则自动查找名为 screen 的物体；只在没有指定 alertAnchor 时用来算兜底位置")]
    public Transform screenTransform;
    [Tooltip("横幅弹出的基准点，只用它的世界位置（挂在哪个物体底下都行）。" +
              "朝向不用这个物体自带的朝向——横幅的朝向由代码自动算，始终自动面向观众，不会有文字镜像反的问题。" +
              "留空则退回用 screenTransform + fallbackLocalOffset 兜底")]
    public Transform alertAnchor;
    public TMP_FontAsset fontAsset;

    [Header("JSON")]
    public string jsonFileName = "classify/matchpoint.json";

    [Header("排队节奏")]
    [Tooltip("没有指定 alertAnchor 时的兜底偏移：在 screenTransform 的朝向下，相对它的位置偏移多少米")]
    public Vector3 fallbackLocalOffset = new Vector3(0f, 0.35f, 0.05f);
    [Tooltip("一次只显示一条；如果短时间内有多条一起触发，会排队依次弹出。这个是上一条消失后到下一条出现之间的间隔时间（秒）")]
    public float queueGapDuration = 0.4f;

    [Header("横幅大小（警戒带长条）")]
    [Range(8f, 96f)] public float fontSize = 40f;
    [Tooltip("横幅的像素尺寸（决定文字排布/换行的框，不是最终米数），横向明显大于纵向，做成长条")]
    public Vector2 bannerPixelSize = new Vector2(900f, 130f);
    [Tooltip("横幅整体缩放到真实米数的系数，越大横幅越大，嫌太大就调小这个")]
    [Range(0.0005f, 0.01f)] public float worldScale = 0.0025f;
    public float textPaddingH = 60f;
    public float textPaddingV = 16f;
    [Tooltip("四周斜纹警戒边框的厚度（像素）")]
    [Range(0, 60)] public int barBorderThickness = 14;
    [Tooltip("斜纹一条条纹的宽度（像素），越小条纹越密")]
    [Range(2f, 80f)] public float stripeWidthPixels = 26f;
    [Range(0f, 1f)] public float maxAlpha = 0.97f;

    [Header("外观配色")]
    [Tooltip("中间填充色，透明度就是这个颜色的 Alpha 通道——想要背景框更透一点就把 A 调小")]
    public Color backgroundColor = new Color(0.08f, 0.02f, 0.02f, 0.72f);
    [Tooltip("警戒斜纹的颜色 A（比如黄）")]
    public Color stripeColorA = new Color(1f, 0.82f, 0.1f, 1f);
    [Tooltip("警戒斜纹的颜色 B（比如黑/深色）")]
    public Color stripeColorB = new Color(0.08f, 0.06f, 0.04f, 1f);
    public Color textColor = new Color(1f, 0.95f, 0.7f, 1f);
    [Range(0, 300)] public int canvasSortOrder = 230;

    [Header("时间控制（秒）")]
    public float fadeInDuration = 0.25f;
    public float dwellMin = 2.2f;
    public float dwellMax = 3.2f;
    public float fadeOutDuration = 0.5f;

    [Header("手柄震动")]
    public bool enableHaptics = true;
    [Tooltip("震动强度")]
    [Range(0f, 1f)] public float hapticAmplitude = 0.7f;
    [Tooltip("震动持续时间（秒）")]
    public float hapticDuration = 0.15f;

    readonly List<MatchPointRecord> records = new();
    readonly Queue<MatchPointRecord> pending = new();

    int recordIdx;
    double lastVT = -1d;
    float lastFinishedAt = -999f;
    Transform root;
    MatchPointAlertInstance current;
    bool initialized;
    bool rootAligned;

    [Serializable]
    class MatchPointRecord
    {
        public string 弹幕内容;
        public int 长度;
        public float 新视频中的时间;
    }

    [Serializable]
    class MatchPointCollection
    {
        public MatchPointRecord[] items;
    }

    void OnDestroy()
    {
        ClearActiveAlert();
    }

    void OnDisable()
    {
        if (Application.isPlaying)
        {
            ClearActiveAlert();
            pending.Clear();
        }

        initialized = false;
        rootAligned = false;
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

        EnsureFontAsset();
        EnsureRoot();
        LoadJson();
        PreloadFontCharacters();
        SeekIndex(videoPlayer != null ? videoPlayer.time : 0d);

        if (records.Count == 0)
            Debug.LogError("[MatchPointAlert] 未加载到弹幕数据，请检查 classify/matchpoint.json");
    }

    void Update()
    {
        if (!rootAligned) AlignRoot();
        if (videoPlayer == null || root == null) return;

        double vt = videoPlayer.time;
        if (lastVT >= 0d && vt + 0.5 < lastVT)
        {
            SeekIndex(vt);
            ClearActiveAlert();
            pending.Clear();
        }
        lastVT = vt;

        if (videoPlayer.isPrepared)
        {
            while (recordIdx < records.Count && records[recordIdx].新视频中的时间 <= (float)vt)
            {
                pending.Enqueue(records[recordIdx]);
                recordIdx++;
            }
        }

        TryAdvanceQueue();
    }

    void EnsureFontAsset()
    {
        if (fontAsset != null) return;

        var meme = FindObjectOfType<MemeBubbleController>();
        if (meme != null && meme.fontAsset != null)
        {
            fontAsset = meme.fontAsset;
            return;
        }

        var emotion = FindObjectOfType<EmotionBubbleController>();
        if (emotion != null && emotion.fontAsset != null)
            fontAsset = emotion.fontAsset;
    }

    void EnsureRoot()
    {
        if (root != null) return;

        var existing = transform.Find("MatchPointAlertRoot");
        if (existing != null)
        {
            root = existing;
        }
        else
        {
            var go = new GameObject("MatchPointAlertRoot");
            go.transform.SetParent(transform, false);
            root = go.transform;
        }

        AlignRoot();
    }

    /// <summary>
    /// 位置用 alertAnchor（或兜底偏移）的世界坐标——这个可以随便挂在别的物体底下。
    /// 但朝向不用锚点自带的朝向：锚点如果是挂在别的系统底下，它的旋转往往是给那个系统内部算法用的参数，
    /// 不代表"正面朝观众"，直接拿来用很容易导致文字镜像反。所以朝向改成始终用相机位置反算
    /// （跟 PopUpDanmakuInstance 的 Billboard 是同一个公式），保证横幅始终正面朝着观众。
    /// </summary>
    void AlignRoot()
    {
        if (root == null) return;

        Vector3 targetPos;
        if (alertAnchor != null)
            targetPos = alertAnchor.position;
        else if (screenTransform != null)
            targetPos = screenTransform.position + screenTransform.rotation * fallbackLocalOffset;
        else
            targetPos = root.position;

        root.position = targetPos;
        root.localScale = Vector3.one;

        Camera cam = DanmakuCameraUtility.ResolveViewCamera();
        if (cam != null)
        {
            root.rotation = Quaternion.LookRotation(root.position - cam.transform.position);
            rootAligned = true;
        }
        else if (alertAnchor != null)
        {
            root.rotation = alertAnchor.rotation;
        }
        else if (screenTransform != null)
        {
            root.rotation = screenTransform.rotation;
        }
    }

    void LoadJson()
    {
        records.Clear();
        string[] candidates = { jsonFileName, "classify/matchpoint.json" };

        foreach (string candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            string path = Path.Combine(Application.streamingAssetsPath, candidate.Replace('\\', '/'));
            if (!File.Exists(path)) continue;

            var col = JsonUtility.FromJson<MatchPointCollection>(
                File.ReadAllText(path, System.Text.Encoding.UTF8).Trim());
            if (col?.items == null || col.items.Length == 0) continue;

            records.AddRange(col.items);
            records.Sort((a, b) => a.新视频中的时间.CompareTo(b.新视频中的时间));
            jsonFileName = candidate.Replace('\\', '/');
            Debug.Log($"[MatchPointAlert] 加载 {records.Count} 条：{path}");
            return;
        }

        Debug.LogError("[MatchPointAlert] JSON 未找到。");
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

    void TryAdvanceQueue()
    {
        if (current != null) return;
        if (pending.Count == 0) return;
        if (Time.time - lastFinishedAt < queueGapDuration) return;

        MatchPointRecord rec = pending.Dequeue();
        SpawnAlert(rec);
    }

    void SpawnAlert(MatchPointRecord rec)
    {
        var go = BuildBannerGo(rec.弹幕内容);
        go.transform.SetParent(root, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;

        float dwell = UnityEngine.Random.Range(dwellMin, dwellMax);
        var inst = go.AddComponent<MatchPointAlertInstance>();
        inst.Initialize(fadeInDuration, dwell, fadeOutDuration, worldScale, maxAlpha, OnAlertFinished);
        current = inst;

        if (enableHaptics)
            XRHapticsUtility.PulseAllControllers(hapticAmplitude, hapticDuration);
    }

    void OnAlertFinished(MatchPointAlertInstance inst)
    {
        if (current == inst) current = null;
        lastFinishedAt = Time.time;
    }

    void ClearActiveAlert()
    {
        if (current != null)
        {
            UiSpriteCleanupUtil.DestroyGeneratedSprites(current.gameObject);
            Destroy(current.gameObject);
            current = null;
        }
        lastFinishedAt = -999f;
    }

    GameObject BuildBannerGo(string text)
    {
        int coreW = Mathf.Max(8, Mathf.RoundToInt(bannerPixelSize.x));
        int coreH = Mathf.Max(8, Mathf.RoundToInt(bannerPixelSize.y));

        var root = new GameObject("MatchPointAlert");
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = canvasSortOrder;

        var rt = root.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(coreW, coreH);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        root.AddComponent<CanvasGroup>();

        var bgGo = MakeChild(root.transform, "Background");
        var bgRt = bgGo.GetComponent<RectTransform>();
        bgRt.anchorMin = bgRt.anchorMax = bgRt.pivot = new Vector2(0.5f, 0.5f);
        bgRt.sizeDelta = new Vector2(coreW, coreH);

        var bgImg = bgGo.AddComponent<Image>();
        Sprite sprite = SocializationPanelShapeUtil.CreateHazardBar(
            coreW, coreH, barBorderThickness, stripeWidthPixels,
            stripeColorA, stripeColorB, backgroundColor);
        SocializationPanelShapeUtil.Apply(bgImg, sprite);

        var textGo = MakeChild(root.transform, "Text");
        var textRt = textGo.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(textPaddingH, textPaddingV);
        textRt.offsetMax = new Vector2(-textPaddingH, -textPaddingV);
        textRt.pivot = new Vector2(0.5f, 0.5f);

        var label = textGo.AddComponent<TextMeshProUGUI>();
        if (fontAsset != null)
        {
            label.font = fontAsset;
            fontAsset.TryAddCharacters(text, out _);
        }
        label.text = text;
        label.color = textColor;
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = true;
        label.overflowMode = TextOverflowModes.Overflow;
        // 长条横幅装不下太长的字就自动缩字号兜底，保证不会溢出
        label.enableAutoSizing = true;
        label.fontSizeMin = Mathf.Min(14f, fontSize * 0.5f);
        label.fontSizeMax = fontSize;
        label.fontSize = fontSize;
        label.ForceMeshUpdate();

        return root;
    }

    static GameObject MakeChild(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }
}
