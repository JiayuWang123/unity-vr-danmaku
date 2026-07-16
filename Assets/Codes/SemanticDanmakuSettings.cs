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

    [Header("顶层双簇静态堆叠（farInfoUseScrollTicker 关闭时生效）")]
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

    [Header("Far Info 滚动条（左右分区，互不过界）")]
    [Tooltip("开启后，Far 层两簇信息弹幕改为裁剪区内从右向左滚动")]
    public bool farInfoUseScrollTicker = true;
    [Tooltip("单侧滚动区宽度（Canvas 像素）")]
    public float tickerLaneWidth = 420f;
    [Tooltip("左右栏各自向外侧额外加宽（Canvas 像素）：左栏往左、右栏往右；靠中心一侧内边保持不变")]
    public float tickerOutwardWidthExtra = 400f;
    [Tooltip("左右栏整体再向外侧平移（米）：左栏往左、右栏往右")]
    public float tickerOutwardHorizontalExtraMeters = 0.45f;
    [Tooltip("单侧滚动区可见高度（Canvas 像素）")]
    public float tickerViewportHeight = 360f;
    [Tooltip("每条滚动弹幕占用行高（Canvas 像素）")]
    public float tickerRowHeight = 88f;
    [Tooltip("滚动速度（Canvas 像素/秒）")]
    public float tickerScrollSpeed = 72f;
    [Tooltip("滚动区相对 Far 层中心的竖直偏移（米）；负值向下")]
    public float tickerVerticalOffsetMeters = -0.15f;
    [Tooltip("喇叭图标尺寸（Canvas 像素）")]
    public float tickerSpeakerSize = 34f;
    [Tooltip("喇叭与文字间距（Canvas 像素）")]
    public float tickerTextGap = 8f;
    public Color tickerSpeakerColor = new Color(1f, 0.82f, 0.25f, 1f);
    public Sprite tickerSpeakerSprite;
    public string tickerSpeakerSpriteResource = string.Empty;
    [Tooltip("滚动条相对相机左右内扣角度（度）；0 = 正面朝向头显")]
    public float tickerInwardYawDegrees = 0f;
    [Tooltip("左右栏对称旋转（度）：左栏逆时针、右栏顺时针，角度相同；绕竖直轴相对朝向相机内扣/外展")]
    [Range(-45f, 45f)] public float tickerPanelSpreadDegrees = 0f;
    [Tooltip("滚动条额外欧拉角微调（度）。若文字反了可试 Y=180")]
    public Vector3 tickerExtraEulerOffset = Vector3.zero;
    [Tooltip("滚动区左右缘渐变宽度（像素）：文字滚到该范围内按位置变淡")]
    public float tickerEdgeFadeWidth = 80f;
    [Tooltip("将左右两栏合并为居中单栏，两类弹幕混合滚动")]
    public bool tickerMergeCategoriesSingleLane = false;
    [Tooltip("合并单栏总宽度（像素）；<=0 时自动 = 2×(lane+outward)+160")]
    public float tickerMergedLaneWidth = 0f;

    [Header("Far Info 分类小标题")]
    public bool showTickerCategoryTitles = true;
    public string tickerLeftCategoryTitle = "比赛内容";
    public string tickerRightCategoryTitle = "球队球员";
    [Tooltip("小标题区高度（Canvas 像素）")]
    public float tickerCategoryTitleHeight = 56f;
    [Tooltip("小标题距面板顶部的内边距（像素）")]
    public float tickerCategoryTitleTopPadding = 12f;
    [Tooltip("小标题与下方滚动区之间的间距（像素）")]
    public float tickerCategoryTitleBottomPadding = 8f;
    [Tooltip("小标题字号；留 0 则自动 = Far Info 字号 + 8")]
    public float tickerCategoryTitleFontSize = 0f;
    public Color tickerCategoryTitleColor = new Color(0.28f, 0.72f, 1f, 1f);

    [Header("Far Info 面板背景（与聊天室同款）")]
    [Tooltip("为左右两栏绘制半透明底 + 青紫渐变描边 + 外发光")]
    public bool tickerPanelBackgroundEnabled = true;
    [Tooltip("启动时自动同步 SocializationPanelController 的面板配色与圆角")]
    public bool syncTickerPanelStyleFromChatPanel = true;
    public Color tickerPanelFillColor = new Color(0.04f, 0.06f, 0.15f, 0.6f);
    public Color tickerPanelBorderColorA = new Color(0.25f, 0.9f, 1f, 0.95f);
    public Color tickerPanelBorderColorB = new Color(0.66f, 0.36f, 1f, 0.95f);
    public Color tickerPanelGlowColor = new Color(0.42f, 0.55f, 1f, 0.55f);
    [Range(1f, 10f)] public float tickerPanelBorderWidth = 3f;
    [Range(4, 60)] public int tickerPanelCornerRadius = 20;
    [Range(0, 60)] public int tickerPanelGlowSize = 22;

    [Header("Far Info 球员球队高亮")]
    [Tooltip("「球队球员」类弹幕：将匹配到的人名/队名用蓝色标出；合并单栏时同样生效")]
    public bool tickerEntityHighlightEnabled = true;
    public Color tickerEntityHighlightColor = new Color(0.28f, 0.72f, 1f, 1f);
    public string[] tickerHighlightPlayerNames =
    {
        "姆巴佩", "迪马利亚", "迪玛利亚", "巴蒂斯图塔", "劳塔罗", "蒙铁尔", "内马尔",
        "大马丁", "阿圭罗", "马拉多纳", "瓦拉内", "登贝莱", "梅西", "科曼", "洛里",
        "恩佐", "特奥", "小蜘蛛", "巴佩", "C罗", "c罗", "罗哥", "登子", "马丁", "塔罗"
    };
    public string[] tickerHighlightTeamNames =
    {
        "阿根廷", "法国", "沙特", "西班牙"
    };

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

    public float GetTickerCategoryTitleFontSize()
    {
        return tickerCategoryTitleFontSize > 0f
            ? tickerCategoryTitleFontSize
            : farInfoFontSize + 8f;
    }

    public string GetTickerCategoryTitle(bool leftCluster)
    {
        return leftCluster ? tickerLeftCategoryTitle : tickerRightCategoryTitle;
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

    public float GetTickerLaneWidth(bool mergedSingleLane)
    {
        if (!mergedSingleLane)
            return tickerLaneWidth + tickerOutwardWidthExtra;

        if (tickerMergedLaneWidth > 0f)
            return tickerMergedLaneWidth;

        return (tickerLaneWidth + tickerOutwardWidthExtra) * 2f + 160f;
    }

    public Color BuildTickerTextColor(SemanticDanmakuRecord record)
    {
        Color baseColor = Color.white;
        float layerAlpha = GetLayerAlpha(CurvedCloudLayerKind.FarInfo);
        float recordAlpha = record != null ? Mathf.Clamp01(record.alpha) : 1f;
        baseColor.a = layerAlpha * recordAlpha;
        return baseColor;
    }

    public void ResolveTickerPanelStyle(
        out Color fillColor,
        out Color borderColorA,
        out Color borderColorB,
        out Color glowColor,
        out float borderWidth,
        out int cornerRadius,
        out int glowSize)
    {
        if (syncTickerPanelStyleFromChatPanel)
        {
            var chat = FindObjectOfType<SocializationPanelController>();
            if (chat != null)
            {
                fillColor = chat.panelFillColor;
                borderColorA = chat.borderColorA;
                borderColorB = chat.borderColorB;
                glowColor = chat.glowColor;
                borderWidth = chat.borderWidth;
                cornerRadius = chat.cornerRadius;
                glowSize = chat.glowSize;
                return;
            }
        }

        fillColor = tickerPanelFillColor;
        borderColorA = tickerPanelBorderColorA;
        borderColorB = tickerPanelBorderColorB;
        glowColor = tickerPanelGlowColor;
        borderWidth = tickerPanelBorderWidth;
        cornerRadius = tickerPanelCornerRadius;
        glowSize = tickerPanelGlowSize;
    }
}
