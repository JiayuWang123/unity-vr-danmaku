using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// 为「球队球员」滚动弹幕中的人名/队名添加 TMP 富文本高亮。
/// </summary>
public static class FarInfoEntityHighlightUtil
{
    public static string BuildHighlightedText(
        string text,
        SemanticDanmakuSettings settings,
        DanmakuSemanticCategory category)
    {
        if (string.IsNullOrEmpty(text)
            || settings == null
            || !settings.tickerEntityHighlightEnabled
            || category != DanmakuSemanticCategory.EntityRelated)
        {
            return text;
        }

        var names = new List<string>();
        if (settings.tickerHighlightPlayerNames != null)
            names.AddRange(settings.tickerHighlightPlayerNames);
        if (settings.tickerHighlightTeamNames != null)
            names.AddRange(settings.tickerHighlightTeamNames);

        if (names.Count == 0)
            return text;

        names.Sort((a, b) => b.Length.CompareTo(a.Length));

        var spans = new List<(int start, int end)>();
        for (int i = 0; i < names.Count; i++)
        {
            string name = names[i];
            if (string.IsNullOrWhiteSpace(name))
                continue;

            int idx = 0;
            while (idx < text.Length)
            {
                idx = text.IndexOf(name, idx, System.StringComparison.Ordinal);
                if (idx < 0)
                    break;

                spans.Add((idx, idx + name.Length));
                idx += name.Length;
            }
        }

        if (spans.Count == 0)
            return text;

        spans = MergeSpans(spans);
        string hex = ColorUtility.ToHtmlStringRGB(settings.tickerEntityHighlightColor);
        var sb = new StringBuilder(text.Length + spans.Count * 24);
        int cursor = 0;

        for (int i = 0; i < spans.Count; i++)
        {
            (int start, int end) = spans[i];
            if (start > cursor)
                sb.Append(text, cursor, start - cursor);

            sb.Append("<color=#").Append(hex).Append('>');
            sb.Append(text, start, end - start);
            sb.Append("</color>");
            cursor = end;
        }

        if (cursor < text.Length)
            sb.Append(text, cursor, text.Length - cursor);

        return sb.ToString();
    }

    public static void ApplyTickerText(
        TextMeshProUGUI label,
        SemanticDanmakuRecord record,
        SemanticDanmakuSettings config)
    {
        if (label == null || config == null)
            return;

        bool useHighlight = config.tickerEntityHighlightEnabled
            && record != null
            && record.category == DanmakuSemanticCategory.EntityRelated;

        label.richText = useHighlight;
        label.text = useHighlight
            ? BuildHighlightedText(record.text, config, record.category)
            : record != null ? record.text : string.Empty;
    }

    static List<(int start, int end)> MergeSpans(List<(int start, int end)> spans)
    {
        spans.Sort((a, b) => a.start.CompareTo(b.start));
        var merged = new List<(int start, int end)>();
        for (int i = 0; i < spans.Count; i++)
        {
            (int start, int end) = spans[i];
            if (merged.Count == 0 || start > merged[merged.Count - 1].end)
            {
                merged.Add((start, end));
                continue;
            }

            (int lastStart, int lastEnd) = merged[merged.Count - 1];
            merged[merged.Count - 1] = (lastStart, Mathf.Max(lastEnd, end));
        }

        return merged;
    }
}
