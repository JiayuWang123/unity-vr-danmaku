using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Far Info 单侧滚动区：裁剪窗口内多条弹幕从右向左滚动，不越过中线到另一侧。
/// </summary>
public class FarInfoScrollTickerLane : MonoBehaviour
{
    readonly List<FarInfoScrollTickerItem> activeItems = new();
    readonly HashSet<int> occupiedRows = new();

    CurvedDanmakuSurfaceLayer surfaceLayer;
    SemanticDanmakuSettings settings;
    TMP_FontAsset fontAsset;
    bool isLeftCluster;
    bool mergedSingleLane;
    float horizontalOffsetMeters;
    RectTransform viewport;
    RectMask2D viewportMask;
    TextMeshProUGUI categoryTitleLabel;
    Image panelBackgroundImage;
    Sprite panelBackgroundSprite;
    int lastBackgroundWidth;
    int lastBackgroundHeight;

    public int ActiveCount => activeItems.Count;

    public void Initialize(
        CurvedDanmakuSurfaceLayer layer,
        SemanticDanmakuSettings config,
        TMP_FontAsset font,
        bool leftCluster,
        float horizontalOffset,
        bool mergedLane = false)
    {
        surfaceLayer = layer;
        settings = config;
        fontAsset = font;
        isLeftCluster = leftCluster;
        mergedSingleLane = mergedLane;
        horizontalOffsetMeters = horizontalOffset;
        EnsureViewport();
        UpdateLanePose();
    }

    public bool TryEnqueue(SemanticDanmakuRecord record)
    {
        if (record == null || settings == null || viewport == null)
            return false;

        if (activeItems.Count >= GetMaxConcurrentItems())
            return false;

        int row = AcquireRow();
        if (row < 0)
            return false;

        var itemGo = new GameObject("TickerItem");
        var item = itemGo.AddComponent<FarInfoScrollTickerItem>();
        item.Initialize(
            record,
            settings,
            fontAsset,
            viewport,
            row,
            settings.tickerRowHeight,
            GetLaneWidth(),
            settings.tickerScrollSpeed,
            OnItemFinished);

        activeItems.Add(item);
        return true;
    }

    public void ClearAll()
    {
        for (int i = activeItems.Count - 1; i >= 0; i--)
        {
            if (activeItems[i] != null)
                activeItems[i].ForceFinish();
        }

        activeItems.Clear();
        occupiedRows.Clear();
    }

    void LateUpdate()
    {
        UpdateLanePose();
        UpdateTitleLayout();
        CleanupList();
    }

    float GetTitleBandHeight()
    {
        if (settings == null || !settings.showTickerCategoryTitles)
            return 0f;

        return settings.tickerCategoryTitleTopPadding
            + settings.tickerCategoryTitleHeight
            + settings.tickerCategoryTitleBottomPadding;
    }

    void UpdateTitleLayout()
    {
        if (settings == null)
            return;

        float titleBand = GetTitleBandHeight();
        if (viewport != null)
        {
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = new Vector2(0f, -titleBand);
        }

        if (categoryTitleLabel != null)
        {
            RectTransform titleRt = categoryTitleLabel.rectTransform;
            titleRt.anchoredPosition = new Vector2(0f, -settings.tickerCategoryTitleTopPadding);
            titleRt.sizeDelta = new Vector2(GetLaneWidth(), settings.tickerCategoryTitleHeight);
        }

        RectTransform laneRt = GetComponent<RectTransform>();
        if (laneRt != null)
            laneRt.sizeDelta = new Vector2(GetLaneWidth(), GetTotalLaneHeight());

        RefreshPanelBackgroundIfNeeded();
    }

    void RefreshPanelBackgroundIfNeeded()
    {
        if (panelBackgroundImage == null || !panelBackgroundImage.enabled)
            return;

        int width = Mathf.RoundToInt(GetLaneWidth());
        int height = Mathf.RoundToInt(GetTotalLaneHeight());
        if (width == lastBackgroundWidth && height == lastBackgroundHeight)
            return;

        lastBackgroundWidth = width;
        lastBackgroundHeight = height;
        RefreshPanelBackgroundSprite();
    }

    void UpdateLanePose()
    {
        if (surfaceLayer == null || settings == null)
            return;

        float scale = settings.worldLabelScale;
        float outwardShift = 0f;
        float x = horizontalOffsetMeters;

        if (!mergedSingleLane)
        {
            float extraFull = settings.tickerOutwardWidthExtra * scale;
            outwardShift = extraFull * 0.5f + settings.tickerOutwardHorizontalExtraMeters;
            if (isLeftCluster)
                x -= outwardShift;
            else
                x += outwardShift;
        }

        transform.localPosition = surfaceLayer.GetClusterFlatLocalPosition(
            x,
            settings.tickerVerticalOffsetMeters,
            0f,
            0f);

        Camera cam = DanmakuCameraUtility.ResolveViewCamera();
        if (cam == null)
            return;

        Vector3 toCamera = cam.transform.position - transform.position;
        if (toCamera.sqrMagnitude < 0.0001f)
            return;

        Quaternion faceCamera = Quaternion.LookRotation(toCamera.normalized, Vector3.up);
        float inwardYaw = mergedSingleLane
            ? 0f
            : (isLeftCluster ? settings.tickerInwardYawDegrees : -settings.tickerInwardYawDegrees);
        float spreadYaw = mergedSingleLane
            ? 0f
            : (isLeftCluster ? settings.tickerPanelSpreadDegrees : -settings.tickerPanelSpreadDegrees);
        Vector3 extra = settings.tickerExtraEulerOffset;
        transform.rotation = faceCamera * Quaternion.Euler(extra.x, inwardYaw + spreadYaw + extra.y, extra.z);
    }

    void EnsureViewport()
    {
        if (viewport != null)
            return;

        var canvas = GetComponent<Canvas>();
        if (canvas == null)
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
        }

        canvas.sortingOrder = settings.canvasSortingOrder;

        var laneRt = GetComponent<RectTransform>();
        if (laneRt == null)
            laneRt = gameObject.AddComponent<RectTransform>();

        laneRt.sizeDelta = new Vector2(GetLaneWidth(), GetTotalLaneHeight());
        laneRt.pivot = new Vector2(0.5f, 1f);
        laneRt.anchorMin = laneRt.anchorMax = laneRt.pivot;
        laneRt.localScale = Vector3.one * settings.worldLabelScale;

        Camera viewCamera = DanmakuCameraUtility.ResolveViewCamera();
        if (viewCamera != null)
            canvas.worldCamera = viewCamera;

        EnsureCategoryTitle();
        EnsurePanelBackground();

        var viewportGo = new GameObject("Viewport", typeof(RectTransform));
        viewportGo.transform.SetParent(transform, false);
        viewport = viewportGo.GetComponent<RectTransform>();
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        float titleInset = GetTitleBandHeight();
        viewport.offsetMin = Vector2.zero;
        viewport.offsetMax = new Vector2(0f, -titleInset);

        viewportMask = viewportGo.AddComponent<RectMask2D>();
        viewportMask.enabled = true;
    }

    void EnsureCategoryTitle()
    {
        if (settings == null || !settings.showTickerCategoryTitles)
            return;

        if (categoryTitleLabel == null)
        {
            var titleGo = new GameObject("CategoryTitle", typeof(RectTransform));
            titleGo.transform.SetParent(transform, false);
            var titleRt = titleGo.GetComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0.5f, 1f);
            titleRt.anchorMax = new Vector2(0.5f, 1f);
            titleRt.pivot = new Vector2(0.5f, 1f);
            titleRt.anchoredPosition = new Vector2(0f, -settings.tickerCategoryTitleTopPadding);
            titleRt.sizeDelta = new Vector2(GetLaneWidth(), settings.tickerCategoryTitleHeight);

            categoryTitleLabel = titleGo.AddComponent<TextMeshProUGUI>();
            categoryTitleLabel.raycastTarget = false;
            categoryTitleLabel.enableWordWrapping = false;
            categoryTitleLabel.overflowMode = TextOverflowModes.Overflow;
            categoryTitleLabel.alignment = TextAlignmentOptions.Center;
            categoryTitleLabel.fontStyle = FontStyles.Bold;
        }

        categoryTitleLabel.text = settings.GetTickerCategoryTitle(isLeftCluster);
        categoryTitleLabel.fontSize = settings.GetTickerCategoryTitleFontSize();
        categoryTitleLabel.color = settings.tickerCategoryTitleColor;
        if (fontAsset != null)
            categoryTitleLabel.font = fontAsset;
    }

    void EnsurePanelBackground()
    {
        if (settings == null || !settings.tickerPanelBackgroundEnabled)
        {
            if (panelBackgroundImage != null)
                panelBackgroundImage.enabled = false;
            return;
        }

        if (panelBackgroundImage == null)
        {
            var bgGo = new GameObject("PanelBackground", typeof(RectTransform));
            bgGo.transform.SetParent(transform, false);
            bgGo.transform.SetAsFirstSibling();

            var bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;

            panelBackgroundImage = bgGo.AddComponent<Image>();
            panelBackgroundImage.raycastTarget = false;
        }

        panelBackgroundImage.enabled = true;
        RefreshPanelBackgroundSprite();
    }

    void RefreshPanelBackgroundSprite()
    {
        if (panelBackgroundImage == null || settings == null)
            return;

        int width = Mathf.RoundToInt(GetLaneWidth());
        int height = Mathf.RoundToInt(GetTotalLaneHeight());
        settings.ResolveTickerPanelStyle(
            out Color fillColor,
            out Color borderColorA,
            out Color borderColorB,
            out Color glowColor,
            out float borderWidth,
            out int cornerRadius,
            out int glowSize);

        int corner = Mathf.Clamp(cornerRadius, 0, Mathf.Min(width, height) / 2);

        DestroyPanelBackgroundSprite();
        panelBackgroundSprite = SocializationPanelShapeUtil.CreatePanel(
            width,
            height,
            corner,
            Mathf.Max(0, glowSize),
            borderWidth,
            fillColor,
            borderColorA,
            borderColorB,
            glowColor);
        SocializationPanelShapeUtil.Apply(panelBackgroundImage, panelBackgroundSprite);
    }

    void DestroyPanelBackgroundSprite()
    {
        if (panelBackgroundSprite == null)
            return;

        if (panelBackgroundSprite.texture != null)
            Object.Destroy(panelBackgroundSprite.texture);
        Object.Destroy(panelBackgroundSprite);
        panelBackgroundSprite = null;
    }

    void OnDestroy()
    {
        DestroyPanelBackgroundSprite();
    }

    float GetTotalLaneHeight()
    {
        if (settings == null)
            return 360f;

        return settings.tickerViewportHeight + GetTitleBandHeight();
    }

    float GetLaneWidth()
    {
        if (settings == null)
            return 420f;

        return settings.GetTickerLaneWidth(mergedSingleLane);
    }

    int GetMaxConcurrentItems()
    {
        if (settings == null)
            return 3;

        int perLane = Mathf.Max(1, settings.maxConcurrentFarInfo);
        return mergedSingleLane ? perLane * 2 : perLane;
    }

    int AcquireRow()
    {
        int maxRows = GetMaxConcurrentItems();
        for (int row = 0; row < maxRows; row++)
        {
            if (!occupiedRows.Contains(row))
            {
                occupiedRows.Add(row);
                return row;
            }
        }

        return -1;
    }

    void OnItemFinished(FarInfoScrollTickerItem item)
    {
        if (item != null)
            occupiedRows.Remove(item.RowIndex);

        activeItems.Remove(item);
    }

    void CleanupList()
    {
        for (int i = activeItems.Count - 1; i >= 0; i--)
        {
            if (activeItems[i] == null)
                activeItems.RemoveAt(i);
        }
    }
}
