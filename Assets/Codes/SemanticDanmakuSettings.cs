using System;
using UnityEngine;

[DisallowMultipleComponent]
public class SemanticDanmakuSettings : MonoBehaviour
{
    [Header("数据源")]
    public bool useLegacyPopUpJson = false;
    public string classifiedJsonFileName = "information_critical_commentary_summary.json";
    public string legacyNearJsonFileName = "pop_up_near.json";
    public string legacyMidJsonFileName = "pop_up_mid.json";
    public string legacyFarJsonFileName = "pop_up_far.json";

    [Header("布局")]
    public float layoutWindowSeconds = 8f;
    public float layoutRefreshInterval = 0.5f;

    [Header("停留与淡入淡出（秒）")]
    public float emotionDwellDuration = 1.8f;
    public float midInfoDwellDuration = 3f;
    public float farInfoDwellDuration = 4f;
    public float fadeInDuration = 0.25f;
    public float fadeOutDuration = 0.35f;

    [Header("视觉")]
    public float emotionFontSize = 40f;
    public float midInfoFontSize = 36f;
    public float farInfoFontSize = 34f;
    [Range(0f, 1f)] public float emotionLayerAlpha = 1f;
    [Range(0f, 1f)] public float midInfoLayerAlpha = 0.95f;
    [Range(0f, 1f)] public float farInfoLayerAlpha = 0.88f;
    public Color infoFallbackColor = new Color(0.92f, 0.95f, 1f, 1f);
    public Color emotionFallbackColor = Color.white;
    public float worldLabelScale = 0.0045f;
    public Vector2 labelSize = new Vector2(900f, 160f);
    public int canvasSortingOrder = 220;
    [Range(0f, 1f)] public float outlineWidth = 0f;
    public Color outlineColor = new Color(0f, 0f, 0f, 0.85f);

    [Header("并发与 Seek")]
    public int maxConcurrentEmotion = 0;
    public int maxConcurrentMidInfo = 4;
    public int maxConcurrentFarInfo = 3;
    public bool clearSpawnedOnSeek = true;
    public float seekResetThresholdSeconds = 0.5f;

    public float GetDwell(CurvedCloudLayerKind layerKind)
    {
        switch (layerKind)
        {
            case CurvedCloudLayerKind.NearEmotion:
                return emotionDwellDuration;
            case CurvedCloudLayerKind.FarInfo:
                return farInfoDwellDuration;
            default:
                return midInfoDwellDuration;
        }
    }

    public float GetFontSize(CurvedCloudLayerKind layerKind)
    {
        switch (layerKind)
        {
            case CurvedCloudLayerKind.NearEmotion:
                return emotionFontSize;
            case CurvedCloudLayerKind.FarInfo:
                return farInfoFontSize;
            default:
                return midInfoFontSize;
        }
    }

    public float GetLayerAlpha(CurvedCloudLayerKind layerKind)
    {
        switch (layerKind)
        {
            case CurvedCloudLayerKind.NearEmotion:
                return emotionLayerAlpha;
            case CurvedCloudLayerKind.FarInfo:
                return farInfoLayerAlpha;
            default:
                return midInfoLayerAlpha;
        }
    }

    public int GetMaxConcurrent(CurvedCloudLayerKind layerKind)
    {
        switch (layerKind)
        {
            case CurvedCloudLayerKind.NearEmotion:
                return maxConcurrentEmotion;
            case CurvedCloudLayerKind.FarInfo:
                return maxConcurrentFarInfo;
            default:
                return maxConcurrentMidInfo;
        }
    }

    public Color BuildTextColor(SemanticDanmakuRecord record, CurvedCloudLayerKind layerKind)
    {
        Color baseColor = SemanticDanmakuLoader.ResolveColor(
            record,
            layerKind == CurvedCloudLayerKind.NearEmotion ? emotionFallbackColor : infoFallbackColor);

        float layerAlpha = GetLayerAlpha(layerKind);
        float recordAlpha = record != null ? Mathf.Clamp01(record.alpha) : 1f;
        baseColor.a = layerAlpha * recordAlpha;
        return baseColor;
    }
}
