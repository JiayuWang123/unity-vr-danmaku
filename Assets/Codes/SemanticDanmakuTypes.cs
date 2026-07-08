using System;

public enum DanmakuSemanticLayer
{
    Info,
    Emotion,
    Social,
    Inert
}

public enum DanmakuSemanticCategory
{
    Unknown,
    MatchHistory,
    EntityRelated,
    MemeJoke,
    Emotion,
    Social,
    Inert
}

public enum CurvedCloudLayerKind
{
    NearEmotion,
    MidInfo,
    FarInfo
}

[Serializable]
public class SemanticDanmakuRecord
{
    public string text;
    public float timeSeconds;
    public int charCount;
    public DanmakuSemanticLayer semanticLayer = DanmakuSemanticLayer.Info;
    public DanmakuSemanticCategory category = DanmakuSemanticCategory.Unknown;
    public string colorHex;
    public int colorDecimal = 16777215;
    public float alpha = 1f;
    public string sourceFile;

    public static SemanticDanmakuRecord FromLegacyPopUp(PopUpDanmakuRecord legacy, string sourceFile, DanmakuSemanticLayer layer, DanmakuSemanticCategory category)
    {
        if (legacy == null)
            return null;

        return new SemanticDanmakuRecord
        {
            text = legacy.弹幕内容,
            timeSeconds = legacy.出现时间,
            charCount = legacy.字数,
            semanticLayer = layer,
            category = category,
            colorDecimal = legacy.颜色十进制,
            alpha = 1f,
            sourceFile = sourceFile
        };
    }
}
