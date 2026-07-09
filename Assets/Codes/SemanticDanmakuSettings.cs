using System;
using UnityEngine;

[DisallowMultipleComponent]
public class SemanticDanmakuSettings : MonoBehaviour
{
    [Header("数据源")]
    public bool useLegacyPopUpJson = false;
    [Tooltip("勾选后只读取下面两个「顶层双分类」JSON 文件，且只在 Far Info 层以两簇的形式展示")]
    public bool useFarLayerCategoryFiles = true;
    public string farLayerFileA = "comment_on_the_game_the_play.json";
    public string farLayerFileB = "critique_athletes_teams_referees.json";
    public string classifiedJsonFileName = "information_critical_commentary_summary.json";
    public string legacyNearJsonFileName = "pop_up_near.json";
    public string legacyMidJsonFileName = "pop_up_mid.json";
    public string legacyFarJsonFileName = "pop_up_far.json";

    [Header("顶层双簇聚类布局（仅 useFarLayerCategoryFiles 生效）")]
    [Tooltip("两簇中心相对正前方的水平间距（度）。会换算成同一平面上的左右偏移，不会把两簇放到不同深度。")]
    public float clusterHalfGapDegrees = 16f;
    [Tooltip("两簇绕竖直轴朝中间「内扣」的角度（度）。0 = 左右两簇完全同平面、平行；增大 = 左簇向右倾、右簇向左倾。")]
    public float clusterInwardTiltDegrees = 0f;
    [Tooltip("同一簇内，弹幕的垂直行间距（米）。值越大越不容易重叠，堆不下时会一直往上延伸。")]
    public float clusterRowSpacing = 0.2f;
    [Tooltip("同一簇内，每一行并列的弹幕数量。设为 1 最安全（不会因为文字长短不同而重叠）。")]
    public int clusterColumnsPerRow = 1;
    [Tooltip("同一簇内，并列弹幕之间的水平间距（米）。仅在 Columns Per Row 大于 1 时生效。")]
    public float clusterColumnSpacing = 0.55f;
    [Tooltip("两簇堆叠的起始高度（米），会在 Far Layer 的 Vertical Offset 基础上叠加。")]
    public float clusterBaseRowOffset = 0f;
    [Tooltip("放在左边的分类")]
    public DanmakuSemanticCategory leftClusterCategory = DanmakuSemanticCategory.MatchHistory;
    [Tooltip("放在右边的分类")]
    public DanmakuSemanticCategory rightClusterCategory = DanmakuSemanticCategory.EntityRelated;

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

    [Header("最远层（Far Info 双簇）描边")]
    [Range(0f, 1f)]
    [Tooltip("仅 FarInfoLayer 两簇信息弹幕生效；0 = 不加描边")]
    public float farInfoOutlineWidth = 0.22f;
    public Color farInfoOutlineColor = new Color(0f, 0f, 0f, 0.9f);

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
