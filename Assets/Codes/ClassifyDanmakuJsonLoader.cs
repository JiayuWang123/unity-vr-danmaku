using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// 统一加载 classify/ 下的弹幕 JSON，兼容旧版（items + 中文字段）与 v2（数组 + text/new_video_time_sec）。
/// </summary>
public static class ClassifyDanmakuJsonLoader
{
    [Serializable]
    public class DanmakuTextEntry
    {
        public string text;
        public float timeSec;
        public int length;
        public string sentiment;
    }

    [Serializable]
    class FlexibleItemDto
    {
        public string text;
        public string 弹幕内容;
        public float new_video_time_sec;
        public float 新视频中的时间;
        public int length;
        public int 长度;
        public string sentiment;
        public string 正反面情绪;
    }

    [Serializable]
    class FlexibleListDto
    {
        public FlexibleItemDto[] items;
    }

    public static string ResolveStreamingAssetPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;

        relativePath = relativePath.Replace('\\', '/').Trim();
        string root = Application.streamingAssetsPath;

        string direct = Path.Combine(root, relativePath);
        if (File.Exists(direct))
            return direct;

        string fileName = Path.GetFileName(relativePath);
        if (string.IsNullOrEmpty(fileName))
            return null;

        string[] folders =
        {
            "",
            "classify",
            "SemanticDanmaku"
        };

        for (int i = 0; i < folders.Length; i++)
        {
            string folder = folders[i];
            string candidate = string.IsNullOrEmpty(folder)
                ? Path.Combine(root, fileName)
                : Path.Combine(root, folder, fileName);

            if (File.Exists(candidate))
                return candidate;

            string caseMatch = FindCaseInsensitiveFile(
                string.IsNullOrEmpty(folder) ? root : Path.Combine(root, folder),
                fileName);
            if (caseMatch != null)
                return caseMatch;
        }

        return null;
    }

    public static bool TryLoadEntries(string relativePath, out List<DanmakuTextEntry> entries, out string resolvedPath)
    {
        entries = new List<DanmakuTextEntry>();
        resolvedPath = ResolveStreamingAssetPath(relativePath);
        if (resolvedPath == null || !File.Exists(resolvedPath))
            return false;

        string json = File.ReadAllText(resolvedPath, Encoding.UTF8).Trim();
        if (string.IsNullOrEmpty(json))
            return false;

        if (json.StartsWith("["))
        {
            FlexibleListDto list = JsonUtility.FromJson<FlexibleListDto>("{\"items\":" + json + "}");
            if (list?.items == null || list.items.Length == 0)
                return false;

            for (int i = 0; i < list.items.Length; i++)
            {
                DanmakuTextEntry entry = FromFlexibleItem(list.items[i]);
                if (entry != null)
                    entries.Add(entry);
            }

            return entries.Count > 0;
        }

        FlexibleListDto legacy = JsonUtility.FromJson<FlexibleListDto>(json);
        if (legacy?.items != null && legacy.items.Length > 0)
        {
            for (int i = 0; i < legacy.items.Length; i++)
            {
                DanmakuTextEntry entry = FromFlexibleItem(legacy.items[i]);
                if (entry != null)
                    entries.Add(entry);
            }

            return entries.Count > 0;
        }

        return false;
    }

    public static DanmakuSemanticCategory InferFarLayerCategory(string relativePath)
    {
        string raw = (relativePath ?? string.Empty).ToLowerInvariant();
        if (raw.Contains("critique") || raw.Contains("athlete") || raw.Contains("team") || raw.Contains("referee"))
            return DanmakuSemanticCategory.EntityRelated;

        if (raw.Contains("comment") || raw.Contains("play") || raw.Contains("game"))
            return DanmakuSemanticCategory.MatchHistory;

        return DanmakuSemanticCategory.Unknown;
    }

    static DanmakuTextEntry FromFlexibleItem(FlexibleItemDto dto)
    {
        if (dto == null)
            return null;

        string text = !string.IsNullOrWhiteSpace(dto.text) ? dto.text : dto.弹幕内容;
        if (string.IsNullOrWhiteSpace(text))
            return null;

        float time = dto.new_video_time_sec > 0f ? dto.new_video_time_sec : dto.新视频中的时间;
        int length = dto.length > 0 ? dto.length : dto.长度;
        if (length <= 0)
            length = text.Length;

        string sentiment = !string.IsNullOrWhiteSpace(dto.sentiment) ? dto.sentiment : dto.正反面情绪;

        return new DanmakuTextEntry
        {
            text = text,
            timeSec = time,
            length = length,
            sentiment = sentiment
        };
    }

    static string FindCaseInsensitiveFile(string directory, string fileName)
    {
        if (!Directory.Exists(directory))
            return null;

        try
        {
            string[] files = Directory.GetFiles(directory);
            for (int i = 0; i < files.Length; i++)
            {
                if (string.Equals(Path.GetFileName(files[i]), fileName, StringComparison.OrdinalIgnoreCase))
                    return files[i];
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ClassifyDanmakuJsonLoader] 扫描目录失败：{directory} ({ex.Message})");
        }

        return null;
    }
}
