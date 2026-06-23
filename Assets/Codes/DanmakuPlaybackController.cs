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
    public int trackCount = 12;
    public bool hideTemplateOnStart = true;
    public bool clearSpawnedOnSeek = true;
    public float seekResetThresholdSeconds = 0.5f;

    private readonly List<DanmakuEntry> entries = new List<DanmakuEntry>();
    private readonly List<GameObject> spawnedDanmaku = new List<GameObject>();
    private int currentIndex;
    private double lastVideoTime = -1d;

    private void Start()
    {
        if (hideTemplateOnStart && danmakuPrefab != null)
            danmakuPrefab.SetActive(false);

        LoadJson();
    }

    private void Update()
    {
        if (videoPlayer == null || entries.Count == 0)
            return;

        double videoTime = videoPlayer.time;
        if (ShouldResetForSeek(videoTime))
        {
            currentIndex = FindFirstIndexAtOrAfter((float)videoTime);
            if (clearSpawnedOnSeek)
                ClearSpawnedDanmaku();
        }

        lastVideoTime = videoTime;
        if (!videoPlayer.isPlaying)
            return;

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
        if (videoPlayer != null)
            currentIndex = FindFirstIndexAtOrAfter((float)videoPlayer.time);

        Debug.Log($"Loaded {entries.Count} filtered danmaku entries from {jsonFileName}");
    }

    private void SpawnDanmaku(DanmakuEntry entry)
    {
        if (danmakuPrefab == null || danmakuCanvas == null)
            return;

        GameObject instance = Instantiate(danmakuPrefab, danmakuCanvas);
        instance.SetActive(true);
        spawnedDanmaku.Add(instance);

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
        int safeTrackCount = Mathf.Max(1, trackCount);
        float step = height / safeTrackCount;
        float top = height * 0.5f;
        int clampedTrack = Mathf.Clamp(trackIndex, 0, safeTrackCount - 1);
        return top - clampedTrack * step - verticalPadding;
    }

    private bool ShouldResetForSeek(double videoTime)
    {
        if (lastVideoTime < 0d)
            return false;

        return videoTime + seekResetThresholdSeconds < lastVideoTime;
    }

    private int FindFirstIndexAtOrAfter(float timeSeconds)
    {
        int low = 0;
        int high = entries.Count;
        while (low < high)
        {
            int mid = low + (high - low) / 2;
            if (entries[mid].timeSeconds < timeSeconds)
                low = mid + 1;
            else
                high = mid;
        }

        return low;
    }

    private void ClearSpawnedDanmaku()
    {
        for (int i = spawnedDanmaku.Count - 1; i >= 0; i--)
        {
            if (spawnedDanmaku[i] != null)
                Destroy(spawnedDanmaku[i]);
        }

        spawnedDanmaku.Clear();
    }
}
