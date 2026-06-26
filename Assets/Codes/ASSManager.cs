using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Video;
using TMPro;

public class DanmakuItem
{
    public float time;
    public string text;
    public float y;
}

public class ASSManager : MonoBehaviour
{
    public VideoPlayer myVideoPlayer;
    public RectTransform danmakuCanvas;
    public GameObject danmakuPrefab;

    private List<DanmakuItem> danmakuList = new List<DanmakuItem>();
    private int currentIndex = 0;

    private static readonly Regex AssTagRegex = new Regex(@"\{[^}]*\}", RegexOptions.Compiled);
    private static readonly Regex MoveTagRegex = new Regex(@"\\move\s*\(\s*[-\d.]+,\s*([-\d.]+)", RegexOptions.Compiled);

    void Start()
    {
        string filePath = Path.Combine(Application.streamingAssetsPath, "test.ass");
        if (!File.Exists(filePath))
        {
            Debug.LogWarning($"ASS �ļ�������: {filePath}");
            return;
        }

        string[] lines = File.ReadAllLines(filePath);
        foreach (string line in lines)
        {
            if (!line.StartsWith("Dialogue:"))
                continue;

            string data = line.Substring("Dialogue:".Length).TrimStart();
            string[] parts = data.Split(new char[] { ',' }, 10);
            if (parts.Length < 10)
                continue;

            string rawText = parts[9];
            string text = StripAssTags(rawText);
            if (!ContainsChinese(text))
                continue;

            if (!TryParseAssTime(parts[1], out float startTime))
                continue;

            DanmakuItem item = new DanmakuItem();
            item.time = startTime;
            item.text = text;
            item.y = TryParseAssMoveY(rawText, out float assY)
                ? AssYToCanvasY(assY, danmakuCanvas.rect.height)
                : UnityEngine.Random.Range(-danmakuCanvas.rect.height * 0.4f, danmakuCanvas.rect.height * 0.4f);

            danmakuList.Add(item);
        }

        danmakuList.Sort((a, b) => a.time.CompareTo(b.time));
        Debug.Log($"�ɹ������� {danmakuList.Count} �����ĵ�Ļ��");
    }

    void Update()
    {
        if (myVideoPlayer == null || !myVideoPlayer.isPlaying)
            return;

        while (currentIndex < danmakuList.Count && myVideoPlayer.time >= danmakuList[currentIndex].time)
        {
            SpawnDanmaku(danmakuList[currentIndex]);
            currentIndex++;
        }
    }

    void SpawnDanmaku(DanmakuItem item)
    {
        if (danmakuPrefab == null || danmakuCanvas == null)
            return;

        GameObject go = Instantiate(danmakuPrefab, danmakuCanvas);
        go.SetActive(true);

        TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
        if (label != null)
            label.text = item.text;

        RectTransform rect = go.GetComponent<RectTransform>();
        float startX = danmakuCanvas.rect.width * 0.5f + 100f;
        rect.anchoredPosition = new Vector2(startX, item.y);

        if (go.GetComponent<DanmakuMover>() == null)
            go.AddComponent<DanmakuMover>();
    }

    static string StripAssTags(string raw)
    {
        return AssTagRegex.Replace(raw, "").Replace("\\N", "\n").Trim();
    }

    static bool ContainsChinese(string text)
    {
        foreach (char c in text)
        {
            if (c >= 0x4E00 && c <= 0x9FFF)
                return true;
        }

        return false;
    }

    static bool TryParseAssMoveY(string rawText, out float assY)
    {
        Match match = MoveTagRegex.Match(rawText);
        if (match.Success && float.TryParse(match.Groups[1].Value, out assY))
            return true;

        assY = 0f;
        return false;
    }

    static float AssYToCanvasY(float assY, float canvasHeight)
    {
        return canvasHeight * 0.5f - assY;
    }

    static bool TryParseAssTime(string timeText, out float seconds)
    {
        try
        {
            seconds = (float)TimeSpan.Parse(timeText).TotalSeconds;
            return true;
        }
        catch (FormatException)
        {
            seconds = 0f;
            return false;
        }
    }
}
