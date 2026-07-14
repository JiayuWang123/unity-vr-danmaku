using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// 情绪弹幕：靠近用户下方区域，像气泡一样从下往上升起，纯文字 + 辉光，无对话框底图。
/// positive 随机取暖色（红/橙/黄），negative 随机取冷色（蓝/紫）。
/// </summary>
public class EmotionBubbleController : MonoBehaviour
{
    [Header("场景引用")]
    public VideoPlayer videoPlayer;
    [Tooltip("留空则自动查找名为 screen 的物体；仅用于可能的调试参考，不再作为气泡坐标系（screen 的旋转/缩放不规则，直接用它当父物体会导致方向完全不直觉）")]
    public Transform screenTransform;
    public TMP_FontAsset fontAsset;

    [Header("JSON")]
    public string jsonFileName = "classify/emotion.json";

    [Header("出生位置（真实米数，基准点=开始播放时你头显所在的位置和朝向）")]
    [Tooltip("出生点相对你左右的范围（米），负数在左，正数在右")]
    public Vector2 spawnHorizontalRange = new Vector2(-0.5f, 0.5f);
    [Tooltip("出生高度相对你视线的偏移（米），负数=比视线低多少米，越小越靠下方")]
    public float spawnBaseHeight = -0.5f;
    [Tooltip("出生高度的随机抖动范围（米）")]
    public float spawnHeightJitter = 0.06f;
    [Tooltip("出生点在你正前方的距离（米），越小越贴近你的脸；建议 0.8~1.5")]
    public float spawnForwardOffset = 1.0f;
    [Tooltip("同一时刻气泡之间的最小水平间距（米），避免刚出生就完全叠在一起")]
    public float minHorizontalGap = 0.05f;
    [Tooltip("同一时刻气泡之间的最小垂直间距（米）；只要有一个方向错开足够远（比如老气泡已经升高了）就不算重叠")]
    public float minVerticalGap = 0.06f;
    [Tooltip("找不到不重叠的位置时最多重试几次；调大能进一步减少重叠，但太拥挤时可能让新气泡稍微延后出现")]
    public int spawnPlacementAttempts = 14;

    [Header("上升运动")]
    [Tooltip("气泡上升速度（米/秒）")]
    public float riseSpeed = 0.15f;
    [Tooltip("每条气泡的上升速度随机浮动比例")]
    [Range(0f, 0.6f)] public float riseSpeedJitter = 0.25f;

    [Header("时间控制（秒）")]
    public float fadeInDuration = 0.35f;
    public float dwellMin = 1.6f;
    public float dwellMax = 3f;
    public float fadeOutDuration = 0.9f;

    [Header("字号与大小")]
    [Range(8f, 72f)] public float fontSize = 30f;
    [Tooltip("气泡整体 3D 大小，单位已是真实米数（VR 里看起来太大/太小时优先调这个）")]
    [Range(0.001f, 0.02f)] public float worldScale = 0.005f;
    public float maxTextWidth = 480f;
    [Tooltip("暂留阶段的最大不透明度")]
    [Range(0f, 1f)] public float maxAlpha = 0.95f;

    [Header("辉光效果（整句话套一层圆角矩形描边 + 柔和外发光，圆角比最初版本小一些，没那么像胶囊）")]
    [Tooltip("辉光向外扩散的柔和范围（像素），越大辉光越明显")]
    [Range(0, 60)] public int glowSize = 26;
    [Tooltip("辉光强度，越大辉光越明显")]
    [Range(0f, 1f)] public float glowIntensity = 0.6f;
    [Range(0, 300)] public int canvasSortOrder = 215;

    [Header("颜色倾向")]
    [Tooltip("positive 弹幕从这些暖色里随机挑一个")]
    public Color[] positivePalette =
    {
        new Color(1f, 0.35f, 0.3f, 1f),
        new Color(1f, 0.55f, 0.15f, 1f),
        new Color(1f, 0.8f, 0.2f, 1f)
    };
    [Tooltip("negative 弹幕从这些冷色里随机挑一个")]
    public Color[] negativePalette =
    {
        new Color(0.3f, 0.55f, 1f, 1f),
        new Color(0.45f, 0.35f, 1f, 1f),
        new Color(0.7f, 0.3f, 0.95f, 1f)
    };
    [Tooltip("在挑选出的基础色上做一点随机色相偏移，让同类颜色也有变化（度）")]
    [Range(0f, 40f)] public float hueJitterDegrees = 12f;
    [Tooltip("在挑选出的基础色上做一点随机明暗抖动")]
    [Range(0f, 0.3f)] public float brightnessJitter = 0.12f;

    [Header("并发")]
    public int maxConcurrent = 6;
    public float minSpawnGap = 0.15f;

    readonly List<EmotionBubbleRecord> records = new();
    readonly List<EmotionBubbleInstance> activeInstances = new();

    int recordIdx;
    double lastVT = -1d;
    float lastSpawnTime = -999f;
    Transform bubbleRoot;
    bool initialized;
    bool bubbleRootAligned;

    TextMeshProUGUI measureLabel;

    [Serializable]
    class EmotionBubbleRecord
    {
        public string 弹幕内容;
        public int 长度;
        public float 新视频中的时间;
        public string 正反面情绪;
    }

    [Serializable]
    class EmotionBubbleCollection
    {
        public EmotionBubbleRecord[] items;
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
        bubbleRootAligned = false;
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
        EnsureMeasureCanvas();
        EnsureBubbleRoot();
        LoadJson();
        PreloadFontCharacters();
        SeekIndex(videoPlayer != null ? videoPlayer.time : 0d);

        if (records.Count == 0)
            Debug.LogError("[EmotionBubble] 未加载到弹幕数据，请检查 classify/emotion.json");
    }

    void Update()
    {
        CleanupActiveList();

        if (!bubbleRootAligned)
            AlignBubbleRootToViewer();

        if (videoPlayer == null || bubbleRoot == null) return;

        double vt = videoPlayer.time;
        if (lastVT >= 0d && vt + 0.5 < lastVT)
        {
            SeekIndex(vt);
            ClearActiveBubbles();
            lastSpawnTime = -999f;
        }
        lastVT = vt;

        if (!videoPlayer.isPrepared) return;

        while (recordIdx < records.Count && records[recordIdx].新视频中的时间 <= (float)vt)
        {
            if (!TrySpawnRecord(records[recordIdx]))
                break;
            recordIdx++;
        }
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

        var popUp = FindObjectOfType<PopUpDanmakuController>();
        if (popUp != null && popUp.fontAsset != null)
            fontAsset = popUp.fontAsset;
    }

    void EnsureBubbleRoot()
    {
        if (bubbleRoot != null) return;

        var existing = transform.Find("EmotionBubbleRoot");
        if (existing != null)
        {
            bubbleRoot = existing;
        }
        else
        {
            var go = new GameObject("EmotionBubbleRoot");
            go.transform.SetParent(transform, false);
            bubbleRoot = go.transform;
        }

        AlignBubbleRootToViewer();
    }

    /// <summary>
    /// screen 的旋转/缩放很不规则（本项目里大约转了 86°、三轴缩放也不一样），
    /// 之前把气泡挂在它下面时，“前后”“左右”对应的世界方向完全不是直觉认为的样子——
    /// 调整前移量或左右范围时，气泡实际只会在你视角里左右挪动，永远靠不近你。
    ///
    /// 现在改为：不再依赖 screen 的坐标轴，而是在播放开始时，以你（头显摄像机）当时
    /// 所在的位置和水平朝向为基准，重新搭一套坐标——
    /// Z 轴 = 你当时正前方（水平），X 轴 = 你的右手方向，Y 轴 =世界正上方。
    /// 这样"前移量"一定是"离你更近"，"左右范围"一定是"你视角里的左右"，不会再跑偏。
    /// 这个基准只在对齐成功的那一刻捕捉一次，之后固定不动（气泡不会跟着你的头转）。
    /// </summary>
    void AlignBubbleRootToViewer()
    {
        if (bubbleRoot == null) return;

        Camera cam = DanmakuCameraUtility.ResolveViewCamera();
        if (cam == null) return;

        Transform camT = cam.transform;
        Vector3 forward = camT.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f) forward = camT.up;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
        forward.Normalize();

        bubbleRoot.position = camT.position;
        bubbleRoot.rotation = Quaternion.LookRotation(forward, Vector3.up);
        bubbleRoot.localScale = Vector3.one;
        bubbleRootAligned = true;
    }

    void EnsureMeasureCanvas()
    {
        if (measureLabel != null) return;

        var root = new GameObject("EmotionBubbleMeasure");
        root.transform.SetParent(transform, false);
        root.hideFlags = HideFlags.HideAndDontSave;

        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.enabled = false;

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(root.transform, false);
        var textRect = textGo.AddComponent<RectTransform>();
        textRect.sizeDelta = new Vector2(maxTextWidth, 300f);

        measureLabel = textGo.AddComponent<TextMeshProUGUI>();
        measureLabel.enableWordWrapping = true;
        measureLabel.overflowMode = TextOverflowModes.Overflow;
        measureLabel.alignment = TextAlignmentOptions.Center;
        if (fontAsset != null) measureLabel.font = fontAsset;
    }

    void LoadJson()
    {
        records.Clear();
        string[] candidates =
        {
            jsonFileName,
            "classify/emotion.json",
            "classify/emotional_interactions_emotion_from_excel.json"
        };

        foreach (string candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            string path = Path.Combine(Application.streamingAssetsPath, candidate.Replace('\\', '/'));
            if (!File.Exists(path)) continue;

            var col = JsonUtility.FromJson<EmotionBubbleCollection>(
                File.ReadAllText(path, System.Text.Encoding.UTF8).Trim());
            if (col?.items == null || col.items.Length == 0) continue;

            records.AddRange(col.items);
            records.Sort((a, b) => a.新视频中的时间.CompareTo(b.新视频中的时间));
            int removed = TtsDisplayedTextFilter.RemoveTtsTexts(records, r => r.弹幕内容);
            jsonFileName = candidate.Replace('\\', '/');
            Debug.Log($"[EmotionBubble] 加载 {records.Count} 条（已过滤 {removed} 条 TTS 重复）：{path}");
            return;
        }

        Debug.LogError("[EmotionBubble] JSON 未找到。");
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

    bool TrySpawnRecord(EmotionBubbleRecord rec)
    {
        if (rec != null && TtsDisplayedTextFilter.IsTtsText(rec.弹幕内容))
            return true;

        CleanupActiveList();
        if (activeInstances.Count >= maxConcurrent) return false;
        if (Time.time - lastSpawnTime < minSpawnGap) return false;

        bool isPositive = !string.Equals(rec.正反面情绪?.Trim(), "negative", StringComparison.OrdinalIgnoreCase);
        Color color = PickColor(isPositive);

        Vector2 textSize = CalcTextSize(rec.弹幕内容);
        float halfWidthLocal = textSize.x * worldScale * 0.5f;
        float halfHeightLocal = textSize.y * worldScale * 0.5f;
        float y = spawnBaseHeight + UnityEngine.Random.Range(-spawnHeightJitter, spawnHeightJitter);

        if (!TryPickSpawnX(halfWidthLocal, halfHeightLocal, y, out float x))
            return false; // 附近位置都太挤，这次先不生成，下一帧再试（不会强行叠在一起）

        float z = spawnForwardOffset;

        var go = BuildBubbleGo(rec.弹幕内容, color, textSize);
        go.transform.SetParent(bubbleRoot, false);
        go.transform.localPosition = new Vector3(x, y, z);
        go.transform.localRotation = Quaternion.identity;

        float dwell = UnityEngine.Random.Range(dwellMin, dwellMax);
        float riseSpeedInstance = riseSpeed * UnityEngine.Random.Range(1f - riseSpeedJitter, 1f + riseSpeedJitter);

        var inst = go.AddComponent<EmotionBubbleInstance>();
        inst.Initialize(fadeInDuration, dwell, fadeOutDuration, worldScale, maxAlpha, riseSpeedInstance, halfWidthLocal, halfHeightLocal, OnBubbleFinished);
        activeInstances.Add(inst);
        lastSpawnTime = Time.time;
        return true;
    }

    bool TryPickSpawnX(float halfWidthLocal, float halfHeightLocal, float y, out float x)
    {
        int attempts = Mathf.Max(1, spawnPlacementAttempts);
        x = 0f;
        for (int i = 0; i < attempts; i++)
        {
            x = UnityEngine.Random.Range(spawnHorizontalRange.x, spawnHorizontalRange.y);
            if (IsFarEnoughFromActive(x, y, halfWidthLocal, halfHeightLocal))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 用左右 + 上下两个方向一起判断是否会视觉重叠。
    /// 之前只看左右间距，哪怕老气泡已经升起来跟新气泡完全不在一个高度，也会被当成"占位"，
    /// 结果反而让新气泡被挤到别的地方、或者在真正拥挤时判断不出来。
    /// 现在只有左右和上下都靠得太近才算重叠，任意一个方向错开够远就放行。
    /// </summary>
    bool IsFarEnoughFromActive(float x, float y, float halfWidthLocal, float halfHeightLocal)
    {
        for (int i = 0; i < activeInstances.Count; i++)
        {
            var inst = activeInstances[i];
            if (inst == null) continue;

            float minDistX = halfWidthLocal + inst.HalfWidth + minHorizontalGap;
            float minDistY = halfHeightLocal + inst.HalfHeight + minVerticalGap;

            bool overlapX = Mathf.Abs(x - inst.transform.localPosition.x) < minDistX;
            bool overlapY = Mathf.Abs(y - inst.transform.localPosition.y) < minDistY;

            if (overlapX && overlapY)
                return false;
        }
        return true;
    }

    Color PickColor(bool isPositive)
    {
        Color[] palette = isPositive ? positivePalette : negativePalette;
        Color baseColor = (palette != null && palette.Length > 0)
            ? palette[UnityEngine.Random.Range(0, palette.Length)]
            : (isPositive ? Color.red : Color.blue);

        Color.RGBToHSV(baseColor, out float h, out float s, out float v);
        h = Mathf.Repeat(h + UnityEngine.Random.Range(-hueJitterDegrees, hueJitterDegrees) / 360f, 1f);
        v = Mathf.Clamp01(v + UnityEngine.Random.Range(-brightnessJitter, brightnessJitter));
        Color result = Color.HSVToRGB(h, s, v);
        result.a = 1f;
        return result;
    }

    void OnBubbleFinished(EmotionBubbleInstance inst)
    {
        activeInstances.Remove(inst);
    }

    void ClearActiveBubbles()
    {
        for (int i = activeInstances.Count - 1; i >= 0; i--)
        {
            var inst = activeInstances[i];
            if (inst != null)
            {
                UiSpriteCleanupUtil.DestroyGeneratedSprites(inst.gameObject);
                Destroy(inst.gameObject);
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

    Vector2 CalcTextSize(string text)
    {
        if (measureLabel == null) EnsureMeasureCanvas();

        measureLabel.fontSize = fontSize;
        if (fontAsset != null) measureLabel.font = fontAsset;
        measureLabel.rectTransform.sizeDelta = new Vector2(maxTextWidth, 300f);
        measureLabel.text = text;
        measureLabel.ForceMeshUpdate();

        Vector2 pref = measureLabel.GetPreferredValues(text, maxTextWidth, 0f);

        if (pref.x < 2f || pref.y < 2f || pref.x > maxTextWidth * 3f)
        {
            int chars = Mathf.Max(1, text.Length);
            int estLines = Mathf.Max(1, Mathf.CeilToInt(chars * fontSize * 0.55f / maxTextWidth));
            pref.x = estLines == 1 ? Mathf.Min(chars * fontSize * 0.55f, maxTextWidth) : maxTextWidth;
            pref.y = estLines * fontSize * 1.25f;
        }

        float w = Mathf.Clamp(pref.x, fontSize * 1.2f, maxTextWidth);
        float h = Mathf.Max(pref.y, fontSize * 1.2f);
        return new Vector2(w, h);
    }

    GameObject BuildBubbleGo(string text, Color color, Vector2 textSize)
    {
        int coreW = Mathf.Max(8, Mathf.RoundToInt(textSize.x));
        int coreH = Mathf.Max(8, Mathf.RoundToInt(textSize.y));

        var root = new GameObject("EmotionBubble");
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = canvasSortOrder;

        var rt = root.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(coreW, coreH);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        root.AddComponent<CanvasGroup>();

        var textGo = MakeChild(root.transform, "Text");
        var textRt = textGo.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;
        textRt.pivot = new Vector2(0.5f, 0.5f);

        var label = textGo.AddComponent<TextMeshProUGUI>();
        if (fontAsset != null)
        {
            label.font = fontAsset;
            fontAsset.TryAddCharacters(text, out _);
        }
        label.text = text;
        label.fontSize = fontSize;
        label.color = color;
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = true;
        label.overflowMode = TextOverflowModes.Overflow;
        label.ForceMeshUpdate();

        if (glowSize > 0 && glowIntensity > 0f)
            BuildWholeBoxGlow(root.transform, color, coreW, coreH);

        return root;
    }

    /// <summary>
    /// 回到最初版本的做法：整句话套一层描边 + 柔和外发光，保证一定看得见。
    /// 圆角比例调小了一些（不再是 coreH/2 的全胶囊两端半圆），看起来没那么"生硬"，
    /// 但本质还是一个整体的圆角矩形背板，不是贴着每个字轮廓的形状（那个方案试了几轮效果都不稳定，先放弃）。
    /// </summary>
    void BuildWholeBoxGlow(Transform parent, Color color, int coreW, int coreH)
    {
        var glowGo = MakeChild(parent, "Glow");
        glowGo.transform.SetAsFirstSibling();
        var glowRt = glowGo.GetComponent<RectTransform>();
        glowRt.anchorMin = glowRt.anchorMax = glowRt.pivot = new Vector2(0.5f, 0.5f);
        glowRt.sizeDelta = new Vector2(coreW + glowSize * 2, coreH + glowSize * 2);

        var img = glowGo.AddComponent<Image>();
        Color edgeColor = new Color(color.r, color.g, color.b, glowIntensity);
        Color outerGlowColor = new Color(color.r, color.g, color.b, glowIntensity * 0.55f);
        int cornerRadius = Mathf.RoundToInt(Mathf.Min(coreW, coreH) * 0.22f);
        Sprite sprite = SocializationPanelShapeUtil.CreatePanel(
            coreW, coreH, cornerRadius, glowSize, 1f,
            Color.clear, edgeColor, edgeColor, outerGlowColor);
        SocializationPanelShapeUtil.Apply(img, sprite);
    }

    static GameObject MakeChild(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }
}
