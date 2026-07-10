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
    Sprite speakerSprite;

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
        EnsureSpeakerSprite();
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
            speakerSprite,
            settings.tickerSpeakerColor,
            viewport,
            row,
            settings.tickerRowHeight,
            settings.tickerLaneWidth,
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

        transform.localPosition = surfaceLayer.GetClusterFlatLocalPosition(
            horizontalOffsetMeters,
            settings.tickerVerticalOffsetMeters,
            0f,
            0f);

        Camera cam = DanmakuCameraUtility.ResolveViewCamera();
        if (cam == null)
            return;

        // 用世界空间 billboard，避免 FarInfoLayer 自身旋转导致面板侧对相机。
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

        laneRt.sizeDelta = new Vector2(settings.tickerLaneWidth, settings.tickerViewportHeight);
        laneRt.localScale = Vector3.one * settings.worldLabelScale;

        Camera viewCamera = DanmakuCameraUtility.ResolveViewCamera();
        if (viewCamera != null)
            canvas.worldCamera = viewCamera;

        var viewportGo = new GameObject("Viewport", typeof(RectTransform));
        viewportGo.transform.SetParent(transform, false);
        viewport = viewportGo.GetComponent<RectTransform>();
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = Vector2.zero;
        viewport.offsetMax = Vector2.zero;

        viewportMask = viewportGo.AddComponent<RectMask2D>();
        viewportMask.enabled = true;
    }

    void EnsureSpeakerSprite()
    {
        if (settings.tickerSpeakerSprite != null)
        {
            speakerSprite = settings.tickerSpeakerSprite;
            return;
        }

        if (!string.IsNullOrWhiteSpace(settings.tickerSpeakerSpriteResource))
        {
            Texture2D tex = Resources.Load<Texture2D>(settings.tickerSpeakerSpriteResource);
            if (tex != null)
            {
                speakerSprite = Sprite.Create(
                    tex,
                    new Rect(0f, 0f, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f),
                    100f);
                return;
            }
        }

        speakerSprite = CreateFallbackSpeakerSprite();
    }

    static Sprite CreateFallbackSpeakerSprite()
    {
        const int size = 32;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;

        Color clear = new Color(0f, 0f, 0f, 0f);
        Color fill = Color.white;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool hornBody = x >= 8 && x <= 18 && y >= 12 && y <= 20;
                bool hornBell = x >= 18 && x <= 28 && y >= 9 && y <= 23 && (x - 18) >= Mathf.Abs(y - 16) * 0.35f;
                bool handle = x >= 10 && x <= 14 && y >= 8 && y <= 12;
                tex.SetPixel(x, y, hornBody || hornBell || handle ? fill : clear);
            }
        }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
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

    void OnDestroy()
    {
        if (speakerSprite != null
            && settings != null
            && settings.tickerSpeakerSprite == null
            && string.IsNullOrWhiteSpace(settings.tickerSpeakerSpriteResource))
        {
            Destroy(speakerSprite.texture);
            Destroy(speakerSprite);
        }
    }
}
