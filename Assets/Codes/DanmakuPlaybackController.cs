using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.Video;

public class DanmakuPlaybackController : MonoBehaviour
{
    public VideoPlayer videoPlayer;
    public RectTransform danmakuCanvas;
    public GameObject danmakuPrefab;
    public string jsonFileName = "filtered_danmaku.json";
    public float rightPadding = 120f;
    public float verticalPadding = 48f;

    private readonly List<DanmakuEntry> entries = new List<DanmakuEntry>();
    private int currentIndex;

    private void Start()
    {
        LoadJson();
    }

    private void Update()
    {
        if (videoPlayer == null || !videoPlayer.isPlaying || entries.Count == 0)
            return;

        double videoTime = videoPlayer.time;
        while (currentIndex < entries.Count && entries[currentIndex].timeSeconds <= videoTime)
        {
            SpawnDanmaku(entries[currentIndex]);
            currentIndex++;
        }
    }

    public void LoadJson()
    {
        entries.Clear();
        currentIndex = 0;

        string path = Path.Combine(Application.streamingAssetsPath, jsonFileName);
        if (!File.Exists(path))
        {
            Debug.LogWarning($"Danmaku JSON not found: {path}");
            return;
        }

        string json = File.ReadAllText(path);
        DanmakuCollection collection = JsonUtility.FromJson<DanmakuCollection>(json);
        if (collection == null || collection.entries == null)
        {
            Debug.LogWarning($"Danmaku JSON is empty or invalid: {path}");
            return;
        }

        entries.AddRange(collection.entries);
        entries.Sort((a, b) => a.timeSeconds.CompareTo(b.timeSeconds));
        Debug.Log($"Loaded {entries.Count} filtered danmaku entries from {jsonFileName}");
    }

    private void SpawnDanmaku(DanmakuEntry entry)
    {
        if (danmakuPrefab == null || danmakuCanvas == null)
            return;

        GameObject instance = Instantiate(danmakuPrefab, danmakuCanvas);
        instance.SetActive(true);

        TextMeshProUGUI label = instance.GetComponent<TextMeshProUGUI>();
        if (label != null)
        {
            label.text = entry.text;
            if (ColorUtility.TryParseHtmlString(entry.colorHex, out Color parsedColor))
                label.color = parsedColor;
        }

        RectTransform rect = instance.GetComponent<RectTransform>();
        if (rect != null)
        {
            float startX = danmakuCanvas.rect.width * 0.5f + rightPadding;
            float y = TrackToCanvasY(entry.trackIndex);
            rect.anchoredPosition = new Vector2(startX, y);
        }

        DanmakuMover mover = instance.GetComponent<DanmakuMover>();
        if (mover == null)
            mover = instance.AddComponent<DanmakuMover>();
        mover.canvas = danmakuCanvas;
    }

    private float TrackToCanvasY(int trackIndex)
    {
        float height = Mathf.Max(1f, danmakuCanvas.rect.height - verticalPadding * 2f);
        int trackCount = 12;
        float step = height / trackCount;
        float top = height * 0.5f;
        int clampedTrack = Mathf.Clamp(trackIndex, 0, trackCount - 1);
        return top - clampedTrack * step - verticalPadding;
    }
}
