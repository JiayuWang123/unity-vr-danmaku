using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public class ClassifiedDanmakuRecordDto
{
    public string text;
    public string 弹幕内容;
    public float timeSeconds;
    public float 出现时间;
    public string semanticLayer;
    public string category;
    public string colorHex;
    public int 颜色十进制;
    public float alpha = 1f;
    public int charCount;
    public int 字数;
}

[Serializable]
public class ClassifiedDanmakuRecordListDto
{
    public ClassifiedDanmakuRecordDto[] items;
}

[Serializable]
public class InformationCommentaryItemDto
{
    public string 弹幕内容;
    public int 长度;
    public float 新视频中的时间;
    public string 信息细分类别;
    public string 信息细分类别英文;
}

[Serializable]
public class InformationCommentaryFileDto
{
    public string 信息类别;
    public string information_category;
    public string category;
    public string category_zh;
    public int count;
    public InformationCommentaryItemDto[] items;
}

public static class SemanticDanmakuLoader
{
    public static List<SemanticDanmakuRecord> LoadLegacyPopUpFiles(
        string nearJsonFileName,
        string midJsonFileName,
        string farJsonFileName)
    {
        var records = new List<SemanticDanmakuRecord>();
        records.AddRange(MapLegacyFile(nearJsonFileName, DanmakuSemanticLayer.Emotion, DanmakuSemanticCategory.Emotion));
        records.AddRange(MapLegacyFile(midJsonFileName, DanmakuSemanticLayer.Info, DanmakuSemanticCategory.EntityRelated));
        records.AddRange(MapLegacyFile(farJsonFileName, DanmakuSemanticLayer.Info, DanmakuSemanticCategory.MatchHistory));
        records.Sort((a, b) => a.timeSeconds.CompareTo(b.timeSeconds));
        return records;
    }

    public static List<SemanticDanmakuRecord> LoadClassifiedFile(string fileName)
    {
        string path = Path.Combine(Application.streamingAssetsPath, "SemanticDanmaku", fileName);
        if (!File.Exists(path))
        {
            Debug.LogWarning($"Semantic danmaku JSON not found: {path}");
            return new List<SemanticDanmakuRecord>();
        }

        string json = File.ReadAllText(path).Trim();
        if (string.IsNullOrEmpty(json))
            return new List<SemanticDanmakuRecord>();

        if (json.Contains("\"信息细分类别\"") || json.Contains("\"新视频中的时间\""))
            return LoadInformationCommentaryJson(json, fileName);

        if (json.StartsWith("["))
            json = "{\"items\":" + json + "}";

        ClassifiedDanmakuRecordListDto list = JsonUtility.FromJson<ClassifiedDanmakuRecordListDto>(json);
        if (list?.items == null || list.items.Length == 0)
        {
            Debug.LogWarning($"Semantic danmaku JSON is empty or invalid: {path}");
            return new List<SemanticDanmakuRecord>();
        }

        var records = new List<SemanticDanmakuRecord>(list.items.Length);
        for (int i = 0; i < list.items.Length; i++)
            records.Add(FromClassifiedDto(list.items[i], fileName));

        records.Sort((a, b) => a.timeSeconds.CompareTo(b.timeSeconds));
        Debug.Log($"Loaded {records.Count} classified danmaku entries from {fileName}");
        return records;
    }

    // 按文件整体分类的模式：每个 JSON 文件顶层带一个 category/category_zh，
    // 文件里所有条目都归到同一个分类（不需要每条都带细分类别字段）。
    public static List<SemanticDanmakuRecord> LoadFarLayerCategoryFiles(string fileNameA, string fileNameB)
    {
        var records = new List<SemanticDanmakuRecord>();
        records.AddRange(LoadSingleCategoryFile(fileNameA));
        records.AddRange(LoadSingleCategoryFile(fileNameB));
        records.Sort((a, b) => a.timeSeconds.CompareTo(b.timeSeconds));
        return records;
    }

    static List<SemanticDanmakuRecord> LoadSingleCategoryFile(string fileName)
    {
        var records = new List<SemanticDanmakuRecord>();
        if (string.IsNullOrWhiteSpace(fileName))
            return records;

        string path = ClassifyDanmakuJsonLoader.ResolveStreamingAssetPath(fileName);
        if (path == null || !File.Exists(path))
        {
            Debug.LogWarning($"Semantic danmaku JSON not found: {fileName}");
            return records;
        }

        string json = File.ReadAllText(path).Trim();
        if (string.IsNullOrEmpty(json))
            return records;

        if (json.StartsWith("[") || json.Contains("\"text\""))
            return LoadV2CategoryArray(fileName, path, json);

        InformationCommentaryFileDto file = JsonUtility.FromJson<InformationCommentaryFileDto>(json);
        if (file?.items == null || file.items.Length == 0)
        {
            Debug.LogWarning($"Category danmaku JSON is empty or invalid: {fileName}");
            return records;
        }

        DanmakuSemanticCategory category = ParseFileLevelCategory(file);
        for (int i = 0; i < file.items.Length; i++)
        {
            InformationCommentaryItemDto item = file.items[i];
            if (item == null || string.IsNullOrWhiteSpace(item.弹幕内容))
                continue;

            records.Add(new SemanticDanmakuRecord
            {
                text = item.弹幕内容,
                timeSeconds = item.新视频中的时间,
                charCount = item.长度,
                semanticLayer = DanmakuSemanticLayer.Info,
                category = category,
                alpha = 1f,
                sourceFile = fileName
            });
        }

        Debug.Log($"Loaded {records.Count} entries from {fileName} as {category}");
        return records;
    }

    static List<SemanticDanmakuRecord> LoadV2CategoryArray(string fileName, string path, string json)
    {
        var records = new List<SemanticDanmakuRecord>();
        if (!ClassifyDanmakuJsonLoader.TryLoadEntries(fileName, out List<ClassifyDanmakuJsonLoader.DanmakuTextEntry> entries, out _))
        {
            Debug.LogWarning($"Category danmaku JSON is empty or invalid: {path}");
            return records;
        }

        DanmakuSemanticCategory category = ClassifyDanmakuJsonLoader.InferFarLayerCategory(fileName);
        for (int i = 0; i < entries.Count; i++)
        {
            ClassifyDanmakuJsonLoader.DanmakuTextEntry entry = entries[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.text))
                continue;

            records.Add(new SemanticDanmakuRecord
            {
                text = entry.text,
                timeSeconds = entry.timeSec,
                charCount = entry.length,
                semanticLayer = DanmakuSemanticLayer.Info,
                category = category,
                alpha = 1f,
                sourceFile = fileName
            });
        }

        Debug.Log($"Loaded {records.Count} entries from {fileName} as {category}");
        return records;
    }

    static DanmakuSemanticCategory ParseFileLevelCategory(InformationCommentaryFileDto file)
    {
        string raw = string.Join(" ", new[]
        {
            file.category,
            file.category_zh,
            file.信息类别,
            file.information_category
        }).ToLowerInvariant();

        if (raw.Contains("game") || raw.Contains("play") || raw.Contains("比赛") || raw.Contains("历史"))
            return DanmakuSemanticCategory.MatchHistory;

        if (raw.Contains("athlete") || raw.Contains("team") || raw.Contains("referee")
            || raw.Contains("球员") || raw.Contains("球队") || raw.Contains("裁判"))
            return DanmakuSemanticCategory.EntityRelated;

        return DanmakuSemanticCategory.Unknown;
    }

    static List<SemanticDanmakuRecord> LoadInformationCommentaryJson(string json, string fileName)
    {
        InformationCommentaryFileDto file = JsonUtility.FromJson<InformationCommentaryFileDto>(json);
        if (file?.items == null || file.items.Length == 0)
        {
            Debug.LogWarning($"Information commentary JSON is empty or invalid: {fileName}");
            return new List<SemanticDanmakuRecord>();
        }

        var records = new List<SemanticDanmakuRecord>(file.items.Length);
        for (int i = 0; i < file.items.Length; i++)
        {
            SemanticDanmakuRecord record = FromInformationCommentaryItem(file.items[i], fileName);
            if (record != null)
                records.Add(record);
        }

        records.Sort((a, b) => a.timeSeconds.CompareTo(b.timeSeconds));
        Debug.Log($"Loaded {records.Count} information commentary danmaku entries from {fileName}");
        return records;
    }

    static IEnumerable<SemanticDanmakuRecord> MapLegacyFile(string fileName, DanmakuSemanticLayer layer, DanmakuSemanticCategory category)
    {
        List<PopUpDanmakuRecord> legacyRecords = PopUpDanmakuLoader.LoadFromStreamingAssets(fileName);
        for (int i = 0; i < legacyRecords.Count; i++)
        {
            SemanticDanmakuRecord record = SemanticDanmakuRecord.FromLegacyPopUp(legacyRecords[i], fileName, layer, category);
            if (record != null)
                yield return record;
        }
    }

    static SemanticDanmakuRecord FromInformationCommentaryItem(InformationCommentaryItemDto dto, string sourceFile)
    {
        if (dto == null || string.IsNullOrWhiteSpace(dto.弹幕内容))
            return null;

        DanmakuSemanticCategory category = ParseChineseSubcategory(dto.信息细分类别, dto.信息细分类别英文);
        return new SemanticDanmakuRecord
        {
            text = dto.弹幕内容,
            timeSeconds = dto.新视频中的时间,
            charCount = dto.长度,
            semanticLayer = DanmakuSemanticLayer.Info,
            category = category,
            alpha = 1f,
            sourceFile = sourceFile
        };
    }

    static SemanticDanmakuRecord FromClassifiedDto(ClassifiedDanmakuRecordDto dto, string sourceFile)
    {
        string text = !string.IsNullOrWhiteSpace(dto.text) ? dto.text : dto.弹幕内容;
        float time = dto.timeSeconds > 0f ? dto.timeSeconds : dto.出现时间;

        return new SemanticDanmakuRecord
        {
            text = text,
            timeSeconds = time,
            charCount = dto.charCount > 0 ? dto.charCount : dto.字数,
            semanticLayer = ParseLayer(dto.semanticLayer),
            category = ParseCategory(dto.category),
            colorHex = dto.colorHex,
            colorDecimal = dto.颜色十进制,
            alpha = dto.alpha <= 0f ? 1f : dto.alpha,
            sourceFile = sourceFile
        };
    }

    public static Color ResolveColor(SemanticDanmakuRecord record, Color fallback)
    {
        if (record == null)
            return fallback;

        if (!string.IsNullOrWhiteSpace(record.colorHex) && ColorUtility.TryParseHtmlString(record.colorHex, out Color parsedHex))
            return parsedHex;

        if (record.colorDecimal > 0)
        {
            int rgb = Mathf.Clamp(record.colorDecimal, 0, 0xFFFFFF);
            return new Color(
                ((rgb >> 16) & 0xFF) / 255f,
                ((rgb >> 8) & 0xFF) / 255f,
                (rgb & 0xFF) / 255f,
                1f);
        }

        return fallback;
    }

    static DanmakuSemanticCategory ParseChineseSubcategory(string chinese, string english)
    {
        string raw = (chinese ?? string.Empty) + " " + (english ?? string.Empty);
        raw = raw.ToLowerInvariant();

        if (raw.Contains("比赛") || raw.Contains("历史") || raw.Contains("game") || raw.Contains("play"))
            return DanmakuSemanticCategory.MatchHistory;

        if (raw.Contains("梗") || raw.Contains("搞笑") || raw.Contains("meme") || raw.Contains("jok"))
            return DanmakuSemanticCategory.MemeJoke;

        if (raw.Contains("球员") || raw.Contains("球队") || raw.Contains("裁判") || raw.Contains("解说")
            || raw.Contains("athlete") || raw.Contains("team") || raw.Contains("referee") || raw.Contains("commentator"))
            return DanmakuSemanticCategory.EntityRelated;

        return DanmakuSemanticCategory.Unknown;
    }

    static DanmakuSemanticLayer ParseLayer(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return DanmakuSemanticLayer.Info;

        switch (raw.Trim().ToLowerInvariant())
        {
            case "emotion":
                return DanmakuSemanticLayer.Emotion;
            case "social":
                return DanmakuSemanticLayer.Social;
            case "inert":
                return DanmakuSemanticLayer.Inert;
            default:
                return DanmakuSemanticLayer.Info;
        }
    }

    static DanmakuSemanticCategory ParseCategory(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return DanmakuSemanticCategory.Unknown;

        switch (raw.Trim().ToLowerInvariant())
        {
            case "match_history":
            case "matchhistory":
                return DanmakuSemanticCategory.MatchHistory;
            case "entity_related":
            case "entityrelated":
                return DanmakuSemanticCategory.EntityRelated;
            case "meme_joke":
            case "memejoke":
                return DanmakuSemanticCategory.MemeJoke;
            case "emotion":
                return DanmakuSemanticCategory.Emotion;
            case "social":
                return DanmakuSemanticCategory.Social;
            case "inert":
                return DanmakuSemanticCategory.Inert;
            default:
                return DanmakuSemanticCategory.Unknown;
        }
    }
}
