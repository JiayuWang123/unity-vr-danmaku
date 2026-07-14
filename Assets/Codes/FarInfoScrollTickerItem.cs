using TMPro;
using UnityEngine;

/// <summary>
/// 单条 Far Info 滚动弹幕：从右向左滚动。
/// 透明度按「在视口内的 X 位置」逐字计算——滚到边缘的文字变淡。
/// </summary>
public class FarInfoScrollTickerItem : MonoBehaviour
{
    RectTransform root;
    RectTransform viewportRect;
    TextMeshProUGUI label;
    float scrollX;
    float contentWidth;
    float rowHeight;
    float scrollSpeed;
    float viewportHalfWidth;
    float edgeFadeWidth;
    float maxAlpha;
    Color32 textBaseColor;
    System.Action<FarInfoScrollTickerItem> onFinished;

    public int RowIndex { get; private set; }
    public bool IsFinished { get; private set; }

    public void Initialize(
        SemanticDanmakuRecord record,
        SemanticDanmakuSettings config,
        TMP_FontAsset font,
        Transform viewport,
        int rowIndex,
        float rowHeightPx,
        float viewportWidthPx,
        float scrollSpeedPxPerSec,
        System.Action<FarInfoScrollTickerItem> onDone)
    {
        onFinished = onDone;
        RowIndex = rowIndex;
        rowHeight = rowHeightPx;
        viewportRect = viewport as RectTransform;
        viewportHalfWidth = viewportWidthPx * 0.5f;
        scrollSpeed = scrollSpeedPxPerSec;
        edgeFadeWidth = Mathf.Max(8f, config != null ? config.tickerEdgeFadeWidth : 80f);
        maxAlpha = config != null ? config.GetLayerAlpha(CurvedCloudLayerKind.FarInfo) : 0.88f;
        IsFinished = false;

        BuildVisual(config, font, viewport);
        ApplyRecord(record, config);
        MeasureContentWidth();

        scrollX = viewportHalfWidth;
        UpdateLocalPosition();
        UpdateSpatialFade();
    }

    void BuildVisual(SemanticDanmakuSettings config, TMP_FontAsset font, Transform viewport)
    {
        root = GetComponent<RectTransform>();
        if (root == null)
            root = gameObject.AddComponent<RectTransform>();

        transform.SetParent(viewport, false);
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 1f);
        root.pivot = new Vector2(0f, 1f);
        root.sizeDelta = new Vector2(config.tickerLaneWidth, rowHeight);

        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(transform, false);
        var textRt = textGo.GetComponent<RectTransform>();
        textRt.anchorMin = new Vector2(0f, 0.5f);
        textRt.anchorMax = new Vector2(0f, 0.5f);
        textRt.pivot = new Vector2(0f, 0.5f);
        textRt.anchoredPosition = Vector2.zero;
        textRt.sizeDelta = new Vector2(config.tickerLaneWidth, rowHeight);

        label = textGo.AddComponent<TextMeshProUGUI>();
        label.raycastTarget = false;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Overflow;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        if (font != null)
            label.font = font;
    }

    void ApplyRecord(SemanticDanmakuRecord record, SemanticDanmakuSettings config)
    {
        if (label == null)
            return;

        label.text = record != null ? record.text : string.Empty;
        label.fontSize = config.farInfoFontSize;
        label.color = config.BuildTextColor(record, CurvedCloudLayerKind.FarInfo);
        TmpDanmakuTextUtility.ApplyReadableStyle(label, config.farInfoOutlineWidth, config.farInfoOutlineColor);
        label.ForceMeshUpdate(true, true);
        textBaseColor = label.color;
    }

    void MeasureContentWidth()
    {
        float textWidth = label != null
            ? label.GetPreferredValues(label.text, 0f, rowHeight).x
            : 0f;

        contentWidth = Mathf.Max(8f, textWidth);
        if (root != null)
            root.sizeDelta = new Vector2(contentWidth, rowHeight);
    }

    void Update()
    {
        if (IsFinished)
            return;

        scrollX -= scrollSpeed * Time.deltaTime;
        UpdateLocalPosition();

        if (scrollX + contentWidth < -viewportHalfWidth - edgeFadeWidth)
            Finish();
    }

    void LateUpdate()
    {
        if (IsFinished)
            return;

        UpdateSpatialFade();
    }

    void UpdateLocalPosition()
    {
        if (root == null)
            return;

        root.anchoredPosition = new Vector2(scrollX, -RowIndex * rowHeight);
    }

    void UpdateSpatialFade()
    {
        if (viewportRect == null || label == null)
            return;

        float viewLeft = -viewportHalfWidth;
        float viewRight = viewportHalfWidth;

        label.ForceMeshUpdate(false, false);
        TMP_TextInfo textInfo = label.textInfo;
        if (textInfo == null || textInfo.characterCount == 0)
            return;

        for (int i = 0; i < textInfo.characterCount; i++)
        {
            TMP_CharacterInfo ch = textInfo.characterInfo[i];
            if (!ch.isVisible)
                continue;

            int matIndex = ch.materialReferenceIndex;
            int vertexIndex = ch.vertexIndex;
            Color32[] colors = textInfo.meshInfo[matIndex].colors32;

            float midX = GetViewportX(label.rectTransform, ch.bottomLeft, ch.topRight);
            float alpha = SpatialEdgeAlpha(midX, viewLeft, viewRight);
            byte a = (byte)Mathf.Clamp(Mathf.RoundToInt(textBaseColor.a * alpha * 255f), 0, 255);

            colors[vertexIndex + 0].a = a;
            colors[vertexIndex + 1].a = a;
            colors[vertexIndex + 2].a = a;
            colors[vertexIndex + 3].a = a;
        }

        label.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
    }

    float GetViewportX(RectTransform rt, Vector3 bottomLeft, Vector3 topRight)
    {
        Vector3 localMid = (bottomLeft + topRight) * 0.5f;
        Vector3 world = rt.TransformPoint(localMid);
        return viewportRect.InverseTransformPoint(world).x;
    }

    float SpatialEdgeAlpha(float viewportX, float viewLeft, float viewRight)
    {
        if (edgeFadeWidth <= 0.001f)
            return maxAlpha;

        float fromLeft = Smooth01(Mathf.InverseLerp(viewLeft, viewLeft + edgeFadeWidth, viewportX));
        float fromRight = Smooth01(Mathf.InverseLerp(viewRight, viewRight - edgeFadeWidth, viewportX));
        return maxAlpha * fromLeft * fromRight;
    }

    static float Smooth01(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    void Finish()
    {
        if (IsFinished)
            return;

        IsFinished = true;
        onFinished?.Invoke(this);
        Destroy(gameObject);
    }

    public void ForceFinish()
    {
        Finish();
    }
}
