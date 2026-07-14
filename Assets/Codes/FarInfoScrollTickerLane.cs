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
    float horizontalOffsetMeters;
    RectTransform viewport;
    RectMask2D viewportMask;
    TextMeshProUGUI categoryTitleLabel;

    public int ActiveCount => activeItems.Count;

    public void Initialize(
        CurvedDanmakuSurfaceLayer layer,
        SemanticDanmakuSettings config,
        TMP_FontAsset font,
        bool leftCluster,
        float horizontalOffset)
    {
        surfaceLayer = layer;
        settings = config;
        fontAsset = font;
        isLeftCluster = leftCluster;
        horizontalOffsetMeters = horizontalOffset;
        EnsureViewport();
        UpdateLanePose();
    }

    public bool TryEnqueue(SemanticDanmakuRecord record)
    {
        if (record == null || settings == null || viewport == null)
            return false;

        if (activeItems.Count >= settings.maxConcurrentFarInfo)
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
        CleanupList();
    }

    void UpdateLanePose()
    {
        if (surfaceLayer == null || settings == null)
            return;

        float scale = settings.worldLabelScale;
        float halfBase = settings.tickerLaneWidth * scale * 0.5f;
        float extraFull = settings.tickerOutwardWidthExtra * scale;
        float outwardShift = extraFull * 0.5f + settings.tickerOutwardHorizontalExtraMeters;

        // 保持靠屏幕中心一侧的内边不变，只把外侧加宽/外推（左栏往 -X，右栏往 +X）
        float x = horizontalOffsetMeters;
        if (isLeftCluster)
            x -= outwardShift;
        else
            x += outwardShift;

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
        float inwardYaw = isLeftCluster ? settings.tickerInwardYawDegrees : -settings.tickerInwardYawDegrees;
        Vector3 extra = settings.tickerExtraEulerOffset;
        transform.rotation = faceCamera * Quaternion.Euler(extra.x, inwardYaw + extra.y, extra.z);
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

        var viewportGo = new GameObject("Viewport", typeof(RectTransform));
        viewportGo.transform.SetParent(transform, false);
        viewport = viewportGo.GetComponent<RectTransform>();
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        float titleInset = settings.showTickerCategoryTitles ? settings.tickerCategoryTitleHeight : 0f;
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
            titleRt.anchoredPosition = Vector2.zero;
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

    float GetTotalLaneHeight()
    {
        if (settings == null)
            return 360f;

        float titleHeight = settings.showTickerCategoryTitles ? settings.tickerCategoryTitleHeight : 0f;
        return settings.tickerViewportHeight + titleHeight;
    }

    float GetLaneWidth()
    {
        if (settings == null)
            return 420f;

        return settings.tickerLaneWidth + settings.tickerOutwardWidthExtra;
    }

    int AcquireRow()
    {
        int maxRows = Mathf.Max(1, settings.maxConcurrentFarInfo);
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
