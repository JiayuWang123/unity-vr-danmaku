using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// 加载 TTS 排期文本，供各视觉弹幕系统在展示前过滤「已由语音读出」的条目。
/// 仅过滤：整句完全一致（含得/地/的归一化），或整句只差 1–2 个字。
/// </summary>
public static class TtsDisplayedTextFilter
{
    const string DefaultCandidatesFile = "Audio/tts_candidates_no_overlap.json";
    const string DefaultScheduleFile = "audio_schedule.json";

    static string candidatesFile = DefaultCandidatesFile;
    static string scheduleFile = DefaultScheduleFile;

    static HashSet<string> normalizedExact;
    static List<string> normalizedTtsTexts;
    static bool loaded;

    public static void Configure(string candidatesRelativePath, string scheduleRelativePath = null)
    {
        string nextCandidates = string.IsNullOrWhiteSpace(candidatesRelativePath)
            ? DefaultCandidatesFile
            : candidatesRelativePath.Replace('\\', '/');
        string nextSchedule = string.IsNullOrWhiteSpace(scheduleRelativePath)
            ? scheduleFile
            : scheduleRelativePath.Replace('\\', '/');

        if (loaded && string.Equals(candidatesFile, nextCandidates, StringComparison.Ordinal)
            && string.Equals(scheduleFile, nextSchedule, StringComparison.Ordinal))
            return;

        candidatesFile = nextCandidates;
        scheduleFile = nextSchedule;
        loaded = false;
        normalizedExact = null;
        normalizedTtsTexts = null;
    }

    public static void EnsureLoaded()
    {
        TtsSceneCatalog.Profile expected = TtsSceneCatalog.Resolve();
        if (loaded && !string.Equals(candidatesFile, expected.candidatesFile, StringComparison.Ordinal))
            Configure(expected.candidatesFile);

        if (loaded)
            return;

        normalizedExact = new HashSet<string>(StringComparer.Ordinal);
        normalizedTtsTexts = new List<string>();
        LoadCandidates(Path.Combine(Application.streamingAssetsPath, candidatesFile));
        LoadSchedule(Path.Combine(Application.streamingAssetsPath, scheduleFile));
        loaded = true;
        Debug.Log($"[TtsDisplayedTextFilter] 已加载 {normalizedExact.Count} 条 TTS 文本用于视觉过滤（{candidatesFile}）。");
    }

    public static bool IsTtsText(string text)
    {
        EnsureLoaded();
        string key = Normalize(text);
        if (string.IsNullOrEmpty(key))
            return false;

        if (normalizedExact.Contains(key))
            return true;

        for (int i = 0; i < normalizedTtsTexts.Count; i++)
        {
            if (IsNearMatch(key, normalizedTtsTexts[i]))
                return true;
        }

        return false;
    }

    public static int RemoveTtsTexts<T>(List<T> list, Func<T, string> getText)
    {
        if (list == null || list.Count == 0 || getText == null)
            return 0;

        EnsureLoaded();
        int removed = 0;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            T item = list[i];
            if (item == null)
            {
                list.RemoveAt(i);
                removed++;
                continue;
            }

            if (IsTtsText(getText(item)))
            {
                list.RemoveAt(i);
                removed++;
            }
        }

        return removed;
    }

    static bool IsNearMatch(string visual, string tts)
    {
        if (string.IsNullOrEmpty(visual) || string.IsNullOrEmpty(tts))
            return false;

        if (Math.Abs(visual.Length - tts.Length) > 2)
            return false;

        return LevenshteinDistance(visual, tts) <= 2;
    }

    static int LevenshteinDistance(string a, string b)
    {
        int n = a.Length;
        int m = b.Length;
        if (n == 0) return m;
        if (m == 0) return n;

        var prev = new int[m + 1];
        var curr = new int[m + 1];
        for (int j = 0; j <= m; j++)
            prev[j] = j;

        for (int i = 1; i <= n; i++)
        {
            curr[0] = i;
            for (int j = 1; j <= m; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(
                    Math.Min(curr[j - 1] + 1, prev[j] + 1),
                    prev[j - 1] + cost);
            }

            (prev, curr) = (curr, prev);
        }

        return prev[m];
    }

    static void LoadCandidates(string path)
    {
        if (!File.Exists(path))
            return;

        string json = File.ReadAllText(path).Trim();
        if (string.IsNullOrEmpty(json))
            return;

        if (json.StartsWith("["))
            json = "{\"items\":" + json + "}";

        TtsCandidateArrayWrapper wrapper = JsonUtility.FromJson<TtsCandidateArrayWrapper>(json);
        if (wrapper?.items == null)
            return;

        for (int i = 0; i < wrapper.items.Length; i++)
            AddText(wrapper.items[i]?.text);
    }

    static void LoadSchedule(string path)
    {
        if (!File.Exists(path))
            return;

        string json = File.ReadAllText(path).Trim();
        if (string.IsNullOrEmpty(json))
            return;

        AudioScheduleFile schedule = JsonUtility.FromJson<AudioScheduleFile>(json);
        if (schedule?.events == null)
            return;

        for (int i = 0; i < schedule.events.Length; i++)
            AddText(schedule.events[i]?.text);
    }

    static void AddText(string text)
    {
        string key = Normalize(text);
        if (string.IsNullOrEmpty(key))
            return;

        if (normalizedExact.Add(key))
            normalizedTtsTexts.Add(key);
    }

    static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        text = text.Trim().Normalize(NormalizationForm.FormKC);

        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c) || c == '\u200b' || c == '\ufeff')
                continue;

            if (IsIgnorablePunctuation(c))
                continue;

            sb.Append(NormalizeSpokenChar(char.ToLowerInvariant(c)));
        }

        return sb.ToString();
    }

    static char NormalizeSpokenChar(char c)
    {
        switch (c)
        {
            case '得':
            case '地':
                return '的';
            default:
                return c;
        }
    }

    static bool IsIgnorablePunctuation(char c)
    {
        switch (c)
        {
            case '.':
            case ',':
            case '!':
            case '?':
            case ';':
            case ':':
            case '"':
            case '\'':
            case '，':
            case '。':
            case '！':
            case '？':
            case '；':
            case '：':
            case '、':
            case '…':
            case '·':
            case '「':
            case '」':
            case '『':
            case '』':
            case '（':
            case '）':
            case '(':
            case ')':
            case '[':
            case ']':
            case '{':
            case '}':
            case '-':
            case '—':
            case '~':
            case '～':
            case '“':
            case '”':
            case '‘':
            case '’':
            case '/':
            case '\\':
            case '|':
                return true;
            default:
                return false;
        }
    }
}
