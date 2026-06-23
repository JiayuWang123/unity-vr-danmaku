using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using UnityEngine;

public class DanmakuXmlNormalizer : MonoBehaviour
{
    [Header("Input")]
    public bool parseAllXmlInFolder = true;
    public string inputFolderPath = "";
    public string inputXmlFileName = "test_danmaku.xml";

    [Header("Output")]
    public string outputFileName = "filtered_danmaku.json";
    public bool writeToStreamingAssetsInEditor = true;

    [Header("Filtering")]
    public float densityWindowSeconds = 10f;
    public int maxEntriesPerWindow = 45;
    public float duplicateWindowSeconds = 3f;
    public int trackCount = 12;

    private static readonly Regex WhitespaceRegex = new Regex(@"\s+", RegexOptions.Compiled);
    private static readonly Regex SymbolOnlyRegex = new Regex(@"^[\p{P}\p{S}\s]+$", RegexOptions.Compiled);
    private static readonly Regex RepeatedSingleCharRegex = new Regex(@"^(.)\1{2,}$", RegexOptions.Compiled);

    private static readonly HashSet<string> ShortUsefulSportsReactions = new HashSet<string>
    {
        "牛逼", "进了", "好球", "漂亮", "绝了", "稳了", "燃", "帅", "强", "nb", "gg", "goal"
    };

    [ContextMenu("Normalize XML To JSON")]
    public void NormalizeXmlToJson()
    {
        DanmakuCollection collection = BuildCollection();
        string outputPath = ResolveOutputPath();
        WriteCollectionToJson(collection, outputPath);
        Debug.Log($"Danmaku JSON exported: {outputPath} ({collection.entries.Count} entries)");
    }

    public DanmakuCollection BuildCollection()
    {
        List<string> files = ResolveInputFiles();
        return BuildCollectionFromFiles(files, densityWindowSeconds, maxEntriesPerWindow, duplicateWindowSeconds, trackCount);
    }

    public static DanmakuCollection BuildCollectionFromFile(
        string filePath,
        float densityWindowSeconds = 10f,
        int maxEntriesPerWindow = 45,
        float duplicateWindowSeconds = 3f,
        int trackCount = 12)
    {
        return BuildCollectionFromFiles(
            File.Exists(filePath) ? new List<string> { filePath } : new List<string>(),
            densityWindowSeconds,
            maxEntriesPerWindow,
            duplicateWindowSeconds,
            trackCount);
    }

    public static DanmakuCollection BuildCollectionFromFiles(
        List<string> files,
        float densityWindowSeconds = 10f,
        int maxEntriesPerWindow = 45,
        float duplicateWindowSeconds = 3f,
        int trackCount = 12)
    {
        Dictionary<string, int> filterReasons = new Dictionary<string, int>();
        Dictionary<int, int> modeCounts = new Dictionary<int, int>();
        List<ParsedDanmaku> parsed = new List<ParsedDanmaku>();

        foreach (string file in files)
        {
            ParseFile(file, parsed, filterReasons, modeCounts);
        }

        parsed.Sort((a, b) => a.Entry.timeSeconds.CompareTo(b.Entry.timeSeconds));
        List<DanmakuEntry> kept = ApplyReferenceInspiredFiltering(parsed, filterReasons, densityWindowSeconds, maxEntriesPerWindow, duplicateWindowSeconds);
        AssignTracks(kept, trackCount);

        DanmakuCollection collection = new DanmakuCollection();
        collection.generatedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        collection.entries = kept;
        collection.stats.sourceFileCount = files.Count;
        collection.stats.parsedCount = parsed.Count;
        collection.stats.keptCount = kept.Count;
        collection.stats.skippedCount = filterReasons.Values.Sum();
        collection.stats.firstTimeSeconds = kept.Count == 0 ? 0f : kept[0].timeSeconds;
        collection.stats.lastTimeSeconds = kept.Count == 0 ? 0f : kept[kept.Count - 1].timeSeconds;
        collection.stats.modeCounts = modeCounts
            .OrderBy(pair => pair.Key)
            .Select(pair => new DanmakuModeCount { mode = GetModeName(pair.Key), count = pair.Value })
            .ToList();
        collection.stats.filterReasons = filterReasons
            .OrderByDescending(pair => pair.Value)
            .Select(pair => new DanmakuFilterReasonCount { reason = pair.Key, count = pair.Value })
            .ToList();

        return collection;
    }

    public static void WriteCollectionToJson(DanmakuCollection collection, string outputPath)
    {
        string directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        string json = JsonUtility.ToJson(collection, true);
        File.WriteAllText(outputPath, json);
    }

    private List<string> ResolveInputFiles()
    {
        string folder = string.IsNullOrWhiteSpace(inputFolderPath)
            ? Application.streamingAssetsPath
            : inputFolderPath;

        if (parseAllXmlInFolder)
        {
            return Directory.Exists(folder)
                ? Directory.GetFiles(folder, "*.xml").OrderBy(path => path).ToList()
                : new List<string>();
        }

        string filePath = Path.IsPathRooted(inputXmlFileName)
            ? inputXmlFileName
            : Path.Combine(folder, inputXmlFileName);
        return File.Exists(filePath) ? new List<string> { filePath } : new List<string>();
    }

    private string ResolveOutputPath()
    {
        string fileName = string.IsNullOrWhiteSpace(outputFileName) ? "filtered_danmaku.json" : outputFileName;
#if UNITY_EDITOR
        if (writeToStreamingAssetsInEditor)
            return Path.Combine(Application.streamingAssetsPath, fileName);
#endif
        return Path.Combine(Application.persistentDataPath, fileName);
    }

    private static void ParseFile(
        string filePath,
        List<ParsedDanmaku> parsed,
        Dictionary<string, int> filterReasons,
        Dictionary<int, int> modeCounts)
    {
        XmlDocument xmlDoc = new XmlDocument();
        try
        {
            xmlDoc.Load(filePath);
        }
        catch (Exception ex)
        {
            AddReason(filterReasons, "xml_load_failed");
            Debug.LogWarning($"Failed to load danmaku XML {filePath}: {ex.Message}");
            return;
        }

        XmlNodeList nodes = xmlDoc.SelectNodes("//d");
        if (nodes == null)
            return;

        foreach (XmlNode node in nodes)
        {
            if (!TryParseNode(node, Path.GetFileName(filePath), out ParsedDanmaku item, out string reason))
            {
                AddReason(filterReasons, reason);
                continue;
            }

            parsed.Add(item);
            modeCounts[item.Entry.mode] = modeCounts.TryGetValue(item.Entry.mode, out int count) ? count + 1 : 1;
        }
    }

    private static bool TryParseNode(XmlNode node, string sourceFile, out ParsedDanmaku parsed, out string reason)
    {
        parsed = null;
        reason = "";

        XmlAttribute pAttribute = node.Attributes == null ? null : node.Attributes["p"];
        if (pAttribute == null || string.IsNullOrWhiteSpace(pAttribute.Value))
        {
            reason = "missing_p_attribute";
            return false;
        }

        string[] fields = pAttribute.Value.Split(',');
        if (fields.Length < 9)
        {
            reason = "invalid_p_field_count";
            return false;
        }

        if (!float.TryParse(fields[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float timeSeconds) || timeSeconds < 0f)
        {
            reason = "invalid_time";
            return false;
        }

        if (!int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int mode))
        {
            reason = "invalid_mode";
            return false;
        }

        if (!int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int fontSize))
            fontSize = 25;

        if (!int.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int colorValue))
            colorValue = 16777215;

        string cleanedText = CleanText(node.InnerText);
        if (!IsInformativeText(cleanedText, out reason))
            return false;

        DanmakuEntry entry = new DanmakuEntry
        {
            timeSeconds = timeSeconds,
            text = cleanedText,
            mode = mode,
            modeName = GetModeName(mode),
            fontSize = fontSize,
            colorHex = ColorValueToHex(colorValue),
            userHash = fields[6],
            danmakuId = fields[7],
            sourceFile = sourceFile,
            trackIndex = 0,
            priorityScore = CalculateBasePriority(cleanedText, mode, fontSize)
        };

        parsed = new ParsedDanmaku(entry, NormalizeForDuplicateCheck(cleanedText));
        return true;
    }

    private static List<DanmakuEntry> ApplyReferenceInspiredFiltering(
        List<ParsedDanmaku> parsed,
        Dictionary<string, int> filterReasons,
        float densityWindowSeconds,
        int maxEntriesPerWindow,
        float duplicateWindowSeconds)
    {
        List<DanmakuEntry> kept = new List<DanmakuEntry>();
        Dictionary<string, float> lastSeenByText = new Dictionary<string, float>();

        int windowStart = 0;
        while (windowStart < parsed.Count)
        {
            float startTime = parsed[windowStart].Entry.timeSeconds;
            int windowEnd = windowStart;
            while (windowEnd < parsed.Count && parsed[windowEnd].Entry.timeSeconds < startTime + densityWindowSeconds)
                windowEnd++;

            List<ParsedDanmaku> candidates = new List<ParsedDanmaku>();
            for (int i = windowStart; i < windowEnd; i++)
            {
                ParsedDanmaku item = parsed[i];
                if (lastSeenByText.TryGetValue(item.NormalizedText, out float lastSeen)
                    && item.Entry.timeSeconds - lastSeen <= duplicateWindowSeconds)
                {
                    item.Entry.priorityScore -= 1.25f;
                    AddReason(filterReasons, "near_duplicate_downgraded");
                }

                candidates.Add(item);
            }

            foreach (ParsedDanmaku selected in candidates
                .OrderByDescending(item => item.Entry.priorityScore)
                .ThenBy(item => item.Entry.timeSeconds)
                .Take(maxEntriesPerWindow)
                .OrderBy(item => item.Entry.timeSeconds))
            {
                kept.Add(selected.Entry);
                lastSeenByText[selected.NormalizedText] = selected.Entry.timeSeconds;
            }

            int droppedByDensity = Math.Max(0, candidates.Count - maxEntriesPerWindow);
            for (int i = 0; i < droppedByDensity; i++)
                AddReason(filterReasons, "density_limit");

            windowStart = windowEnd;
        }

        kept.Sort((a, b) => a.timeSeconds.CompareTo(b.timeSeconds));
        return kept;
    }

    private static void AssignTracks(List<DanmakuEntry> entries, int trackCount)
    {
        int safeTrackCount = Mathf.Max(1, trackCount);
        float[] trackAvailableAt = new float[safeTrackCount];

        foreach (DanmakuEntry entry in entries)
        {
            int bestTrack = 0;
            float earliest = trackAvailableAt[0];
            for (int i = 1; i < safeTrackCount; i++)
            {
                if (trackAvailableAt[i] < earliest)
                {
                    bestTrack = i;
                    earliest = trackAvailableAt[i];
                }
            }

            entry.trackIndex = bestTrack;
            float estimatedVisibleSeconds = Mathf.Clamp(2.5f + entry.text.Length * 0.12f, 3f, 8f);
            trackAvailableAt[bestTrack] = Mathf.Max(trackAvailableAt[bestTrack], entry.timeSeconds) + estimatedVisibleSeconds;
        }
    }

    private static string CleanText(string text)
    {
        if (text == null)
            return "";

        string decoded = System.Net.WebUtility.HtmlDecode(text);
        return WhitespaceRegex.Replace(decoded, " ").Trim();
    }

    private static bool IsInformativeText(string text, out string reason)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            reason = "empty_text";
            return false;
        }

        string lower = text.ToLowerInvariant();
        if (ShortUsefulSportsReactions.Contains(lower))
        {
            reason = "";
            return true;
        }

        if (text.Length <= 1)
        {
            reason = "too_short";
            return false;
        }

        if (SymbolOnlyRegex.IsMatch(text))
        {
            reason = "symbol_only";
            return false;
        }

        if (RepeatedSingleCharRegex.IsMatch(text))
        {
            reason = "repeated_single_character";
            return false;
        }

        if (text.Length <= 2 && text.All(char.IsDigit))
        {
            reason = "short_number_only";
            return false;
        }

        reason = "";
        return true;
    }

    private static float CalculateBasePriority(string text, int mode, int fontSize)
    {
        int length = text.Length;
        float score = 1f;
        score += Mathf.Clamp(length / 12f, 0f, 2f);
        if (length >= 4 && length <= 32)
            score += 1f;
        if (ContainsCjk(text))
            score += 0.35f;
        if (ContainsEmotionCue(text))
            score += 0.45f;
        if (mode == 4 || mode == 5)
            score += 0.25f;
        if (fontSize > 25)
            score += 0.15f;

        return score;
    }

    private static bool ContainsCjk(string text)
    {
        foreach (char c in text)
        {
            if (c >= 0x4E00 && c <= 0x9FFF)
                return true;
        }

        return false;
    }

    private static bool ContainsEmotionCue(string text)
    {
        string lower = text.ToLowerInvariant();
        return lower.Contains("哈")
            || lower.Contains("笑")
            || lower.Contains("牛")
            || lower.Contains("帅")
            || lower.Contains("好")
            || lower.Contains("绝")
            || lower.Contains("哭")
            || lower.Contains("!")
            || lower.Contains("？")
            || lower.Contains("?");
    }

    private static string NormalizeForDuplicateCheck(string text)
    {
        return WhitespaceRegex.Replace(text.ToLowerInvariant(), "");
    }

    private static string ColorValueToHex(int colorValue)
    {
        int clamped = Mathf.Clamp(colorValue, 0, 0xFFFFFF);
        return $"#{clamped:X6}";
    }

    private static string GetModeName(int mode)
    {
        switch (mode)
        {
            case 1:
                return "scroll";
            case 4:
                return "bottom";
            case 5:
                return "top";
            default:
                return $"mode_{mode}";
        }
    }

    private static void AddReason(Dictionary<string, int> reasons, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            reason = "unknown";

        reasons[reason] = reasons.TryGetValue(reason, out int count) ? count + 1 : 1;
    }

    private class ParsedDanmaku
    {
        public ParsedDanmaku(DanmakuEntry entry, string normalizedText)
        {
            Entry = entry;
            NormalizedText = normalizedText;
        }

        public DanmakuEntry Entry { get; }
        public string NormalizedText { get; }
    }
}
