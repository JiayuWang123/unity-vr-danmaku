using System;
using UnityEngine;

[Serializable]
public class PopUpZoneAnchorLayout
{
    [Tooltip("左/中锚点本地坐标（相对 screen）")]
    public Vector3 primaryLocalPosition = new Vector3(-1.2f, 0f, -0.8f);
    [Tooltip("右锚点本地坐标；若与 primary 相同则自动取 X 镜像")]
    public Vector3 secondaryLocalPosition = new Vector3(1.2f, 0f, -0.8f);
    [Tooltip("勾选后 secondary 忽略上方数值，改用 primary 的 X 取反")]
    public bool mirrorSecondaryOnX = true;
    [Tooltip("第三个锚点（主要用于 Far 区中间/右侧）")]
    public Vector3 tertiaryLocalPosition = new Vector3(0.45f, 0.48f, 0.28f);
    public bool useTertiaryAnchor = false;
}

[DisallowMultipleComponent]
public class PopUpDanmakuSettings : MonoBehaviour
{
    [Header("JSON 文件（StreamingAssets/PopUpDanmaku/）")]
    public string nearJsonFileName = "pop_up_near.json";
    public string midJsonFileName = "pop_up_mid.json";
    public string farJsonFileName = "pop_up_far.json";

    [Header("停留与淡入淡出（秒）")]
    public float nearDwellDuration = 2f;
    public float midDwellDuration = 3f;
    public float farDwellDuration = 4.5f;
    public float fadeInDuration = 0.25f;
    public float fadeOutDuration = 0.35f;

    [Header("各区视觉")]
    public float nearFontSize = 56f;
    public float midFontSize = 44f;
    public float farFontSize = 34f;
    [Range(0f, 1f)] public float nearAlpha = 1f;
    [Range(0f, 1f)] public float midAlpha = 1f;
    [Range(0f, 1f)] public float farAlpha = 0.95f;
    public Color nearFallbackColor = new Color(1f, 0.95f, 0.6f, 1f);
    public Color midFallbackColor = new Color(0.85f, 0.98f, 1f, 1f);
    public Color farFallbackColor = new Color(0.82f, 0.78f, 1f, 1f);

    [Header("可读性（看不清时优先调这里）")]
    [Tooltip("World Space 标签整体缩放，越大字越大")]
    public float worldLabelScale = 0.0035f;
    public Vector2 labelSize = new Vector2(900f, 160f);
    [Tooltip("Canvas 渲染顺序，越大越在视频前面")]
    public int canvasSortingOrder = 200;
    [Tooltip("勾选后用下方 Fallback Color 作为字色；不勾选则用 JSON 颜色十进制")]
    public bool forceHighContrastText = true;
    [Range(0f, 1f)] public float outlineWidth = 0.28f;
    public Color outlineColor = new Color(0f, 0f, 0f, 0.85f);

    [Header("锚点布局（相对 screen 本地坐标）")]
    [Tooltip("已改用 Scene 中的 PopUpDanmakuZoneFrame 调节框；仅在没有区域框且 autoSetupAnchors=true 时使用")]
    public PopUpZoneAnchorLayout nearLayout = new PopUpZoneAnchorLayout
    {
        primaryLocalPosition = new Vector3(-1.35f, 0.05f, -0.85f),
        secondaryLocalPosition = new Vector3(1.35f, 0.05f, -0.85f),
        mirrorSecondaryOnX = false
    };
    [Tooltip("Mid=中景屏缘；|X| 越小越贴近屏幕边缘")]
    public PopUpZoneAnchorLayout midLayout = new PopUpZoneAnchorLayout
    {
        primaryLocalPosition = new Vector3(-0.72f, -0.08f, 0.08f),
        secondaryLocalPosition = new Vector3(0.72f, -0.08f, 0.08f),
        mirrorSecondaryOnX = false
    };
    [Tooltip("Far=远景屏上方；Y 越大越高，Z 越大越靠屏幕后")]
    public PopUpZoneAnchorLayout farLayout = new PopUpZoneAnchorLayout
    {
        primaryLocalPosition = new Vector3(0f, 0.42f, 0.22f),
        secondaryLocalPosition = new Vector3(-0.45f, 0.48f, 0.28f),
        tertiaryLocalPosition = new Vector3(0.45f, 0.48f, 0.28f),
        mirrorSecondaryOnX = false,
        useTertiaryAnchor = true
    };

    [Header("并发与 Seek")]
    public int maxConcurrentNear = 4;
    public int maxConcurrentMid = 4;
    public int maxConcurrentFar = 3;
    public bool clearSpawnedOnSeek = true;
    public float seekResetThresholdSeconds = 0.5f;

    public float GetDwell(PopUpDanmakuZone zone)
    {
        switch (zone)
        {
            case PopUpDanmakuZone.Near: return nearDwellDuration;
            case PopUpDanmakuZone.Mid: return midDwellDuration;
            default: return farDwellDuration;
        }
    }

    public float GetFontSize(PopUpDanmakuZone zone)
    {
        switch (zone)
        {
            case PopUpDanmakuZone.Near: return nearFontSize;
            case PopUpDanmakuZone.Mid: return midFontSize;
            default: return farFontSize;
        }
    }

    public float GetAlpha(PopUpDanmakuZone zone)
    {
        switch (zone)
        {
            case PopUpDanmakuZone.Near: return nearAlpha;
            case PopUpDanmakuZone.Mid: return midAlpha;
            default: return farAlpha;
        }
    }

    public Color GetFallbackColor(PopUpDanmakuZone zone)
    {
        switch (zone)
        {
            case PopUpDanmakuZone.Near: return nearFallbackColor;
            case PopUpDanmakuZone.Mid: return midFallbackColor;
            default: return farFallbackColor;
        }
    }

    public int GetMaxConcurrent(PopUpDanmakuZone zone)
    {
        switch (zone)
        {
            case PopUpDanmakuZone.Near: return maxConcurrentNear;
            case PopUpDanmakuZone.Mid: return maxConcurrentMid;
            default: return maxConcurrentFar;
        }
    }

    public PopUpZoneAnchorLayout GetLayout(PopUpDanmakuZone zone)
    {
        switch (zone)
        {
            case PopUpDanmakuZone.Near: return nearLayout;
            case PopUpDanmakuZone.Mid: return midLayout;
            default: return farLayout;
        }
    }

    public Color BuildTextColor(PopUpDanmakuZone zone, PopUpDanmakuRecord record)
    {
        Color baseColor = forceHighContrastText || record == null
            ? GetFallbackColor(zone)
            : PopUpDanmakuLoader.RecordToColor(record, GetFallbackColor(zone));

        baseColor.a = GetAlpha(zone);
        return baseColor;
    }

    void OnValidate()
    {
        worldLabelScale = Mathf.Max(0.0001f, worldLabelScale);
        nearAlpha = Mathf.Clamp01(nearAlpha);
        midAlpha = Mathf.Clamp01(midAlpha);
        farAlpha = Mathf.Clamp01(farAlpha);

        if (!Application.isPlaying)
            return;

        PopUpDanmakuController controller = GetComponent<PopUpDanmakuController>();
        if (controller != null)
            controller.RefreshAllActiveVisuals();
    }
}
