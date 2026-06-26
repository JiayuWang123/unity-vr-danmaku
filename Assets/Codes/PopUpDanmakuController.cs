using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Video;

[RequireComponent(typeof(PopUpDanmakuSettings))]
public class PopUpDanmakuController : MonoBehaviour
{
    public VideoPlayer videoPlayer;
    public Transform screenTransform;
    public TMP_FontAsset fontAsset;
    [Tooltip("使用 Scene 里的 Near/Mid/Far 区域框作为锚点（推荐）")]
    public bool useZoneFrames = true;
    public PopUpDanmakuZoneFrame nearZoneFrame;
    public PopUpDanmakuZoneFrame midZoneFrame;
    public PopUpDanmakuZoneFrame farZoneFrame;
    [Tooltip("未配置区域框时，才按 Settings 里的坐标自动生成锚点")]
    public bool autoSetupAnchors = false;

    PopUpDanmakuSettings settings;
    PopUpDanmakuInstance labelTemplate;

    readonly List<PopUpDanmakuRecord> nearRecords = new List<PopUpDanmakuRecord>();
    readonly List<PopUpDanmakuRecord> midRecords = new List<PopUpDanmakuRecord>();
    readonly List<PopUpDanmakuRecord> farRecords = new List<PopUpDanmakuRecord>();
    readonly List<PopUpDanmakuInstance> spawnedInstances = new List<PopUpDanmakuInstance>();

    PopUpDanmakuAnchor[] nearAnchors;
    PopUpDanmakuAnchor[] midAnchors;
    PopUpDanmakuAnchor[] farAnchors;

    int nearIndex;
    int midIndex;
    int farIndex;
    int nearSideToggle;
    int midSideToggle;
    int farSlotToggle;
    double lastVideoTime = -1d;

    Transform anchorRoot;

    void Awake()
    {
        settings = GetComponent<PopUpDanmakuSettings>();
    }

    void Start()
    {
        if (videoPlayer == null)
            Debug.LogWarning("PopUpDanmakuController: videoPlayer is not assigned.");

        if (screenTransform == null)
        {
            GameObject screen = GameObject.Find("screen");
            if (screen != null)
                screenTransform = screen.transform;
        }

        if (useZoneFrames && TryResolveAnchorsFromZoneFrames())
        {
            // 使用 Scene 中配置的区域框锚点
        }
        else if (autoSetupAnchors)
        {
            BuildAnchorLayout();
        }
        else
        {
            Debug.LogWarning("PopUpDanmakuController: 未找到区域框，也未启用 autoSetupAnchors。");
        }

        EnsureLabelTemplate();
        LoadAllJson();
        ResetPlaybackIndices();
    }

    void Update()
    {
        if (videoPlayer == null)
            return;

        double videoTime = videoPlayer.time;
        if (ShouldResetForSeek(videoTime))
        {
            ResetPlaybackIndices(videoTime);
            if (settings.clearSpawnedOnSeek)
                ClearSpawnedInstances();
        }

        lastVideoTime = videoTime;
        if (!videoPlayer.isPlaying)
            return;

        SpawnDueRecords(nearRecords, ref nearIndex, nearAnchors, ref nearSideToggle, PopUpDanmakuZone.Near, videoTime);
        SpawnDueRecords(midRecords, ref midIndex, midAnchors, ref midSideToggle, PopUpDanmakuZone.Mid, videoTime);
        SpawnDueRecords(farRecords, ref farIndex, farAnchors, ref farSlotToggle, PopUpDanmakuZone.Far, videoTime);
    }

    void LoadAllJson()
    {
        nearRecords.Clear();
        midRecords.Clear();
        farRecords.Clear();

        nearRecords.AddRange(PopUpDanmakuLoader.LoadFromStreamingAssets(settings.nearJsonFileName));
        midRecords.AddRange(PopUpDanmakuLoader.LoadFromStreamingAssets(settings.midJsonFileName));
        farRecords.AddRange(PopUpDanmakuLoader.LoadFromStreamingAssets(settings.farJsonFileName));
    }

    void SpawnDueRecords(
        List<PopUpDanmakuRecord> records,
        ref int index,
        PopUpDanmakuAnchor[] anchors,
        ref int sideToggle,
        PopUpDanmakuZone zone,
        double videoTime)
    {
        if (records == null || records.Count == 0 || anchors == null || anchors.Length == 0)
            return;

        CleanupSpawnedList();
        int activeCount = CountActiveInZone(zone);
        while (index < records.Count && records[index].出现时间 <= videoTime)
        {
            if (activeCount >= settings.GetMaxConcurrent(zone))
                break;

            if (!TrySpawnAtAnyAnchor(anchors, ref sideToggle, records[index], zone, out PopUpDanmakuInstance instance))
                break;

            spawnedInstances.Add(instance);
            activeCount++;
            index++;
        }
    }

    bool TrySpawnAtAnyAnchor(
        PopUpDanmakuAnchor[] anchors,
        ref int toggle,
        PopUpDanmakuRecord record,
        PopUpDanmakuZone zone,
        out PopUpDanmakuInstance instance)
    {
        instance = null;
        if (anchors == null || anchors.Length == 0)
            return false;

        for (int attempt = 0; attempt < anchors.Length; attempt++)
        {
            PopUpDanmakuAnchor anchor = anchors[toggle % anchors.Length];
            toggle++;
            if (anchor != null && anchor.TrySpawn(labelTemplate, record, settings, anchorRoot, out instance))
                return true;
        }

        return false;
    }

    void CleanupSpawnedList()
    {
        for (int i = spawnedInstances.Count - 1; i >= 0; i--)
        {
            if (spawnedInstances[i] == null)
                spawnedInstances.RemoveAt(i);
        }
    }

    int CountActiveInZone(PopUpDanmakuZone zone)
    {
        CleanupSpawnedList();
        int count = 0;
        for (int i = 0; i < spawnedInstances.Count; i++)
        {
            if (spawnedInstances[i].Zone == zone)
                count++;
        }

        return count;
    }

    bool ShouldResetForSeek(double videoTime)
    {
        if (lastVideoTime < 0d)
            return false;

        return videoTime + settings.seekResetThresholdSeconds < lastVideoTime;
    }

    void ResetPlaybackIndices(double videoTime = -1d)
    {
        if (videoTime < 0d && videoPlayer != null)
            videoTime = videoPlayer.time;

        nearIndex = FindFirstIndexAtOrAfter(nearRecords, (float)videoTime);
        midIndex = FindFirstIndexAtOrAfter(midRecords, (float)videoTime);
        farIndex = FindFirstIndexAtOrAfter(farRecords, (float)videoTime);
        lastVideoTime = videoTime;
    }

    static int FindFirstIndexAtOrAfter(List<PopUpDanmakuRecord> records, float timeSeconds)
    {
        int low = 0;
        int high = records.Count;
        while (low < high)
        {
            int mid = low + (high - low) / 2;
            if (records[mid].出现时间 < timeSeconds)
                low = mid + 1;
            else
                high = mid;
        }

        return low;
    }

    void ClearSpawnedInstances()
    {
        for (int i = spawnedInstances.Count - 1; i >= 0; i--)
        {
            if (spawnedInstances[i] != null)
                Destroy(spawnedInstances[i]);
        }

        spawnedInstances.Clear();
    }

    public void RefreshAllActiveVisuals()
    {
        CleanupSpawnedList();
        for (int i = 0; i < spawnedInstances.Count; i++)
        {
            if (spawnedInstances[i] != null)
                spawnedInstances[i].RefreshVisualSettings();
        }
    }

    bool TryResolveAnchorsFromZoneFrames()
    {
        if (nearZoneFrame == null || midZoneFrame == null || farZoneFrame == null)
            return false;

        nearZoneFrame.EnsureAnchors();
        midZoneFrame.EnsureAnchors();
        farZoneFrame.EnsureAnchors();

        anchorRoot = nearZoneFrame.transform.parent;
        nearAnchors = nearZoneFrame.GetAnchorArray();
        midAnchors = midZoneFrame.GetAnchorArray();
        farAnchors = farZoneFrame.GetAnchorArray();
        return nearAnchors.Length > 0 && midAnchors.Length > 0 && farAnchors.Length > 0;
    }

#if UNITY_EDITOR
    [ContextMenu("Create Zone Frame Guides Under Screen")]
    public void CreateZoneFrameGuidesInEditor()
    {
        if (screenTransform == null)
        {
            GameObject screen = GameObject.Find("screen");
            if (screen != null)
                screenTransform = screen.transform;
        }

        if (screenTransform == null)
        {
            Debug.LogError("PopUpDanmakuController: 找不到 screen，无法创建区域框。");
            return;
        }

        Transform zonesRoot = screenTransform.Find("PopUpDanmakuZones");
        if (zonesRoot == null)
        {
            GameObject rootGo = new GameObject("PopUpDanmakuZones");
            zonesRoot = rootGo.transform;
            zonesRoot.SetParent(screenTransform, false);
            zonesRoot.localPosition = Vector3.zero;
            zonesRoot.localRotation = Quaternion.identity;
            zonesRoot.localScale = Vector3.one;
        }

        DestroyZoneFrameIfExists(nearZoneFrame);
        DestroyZoneFrameIfExists(midZoneFrame);
        DestroyZoneFrameIfExists(farZoneFrame);

        nearZoneFrame = PopUpDanmakuZoneFrame.CreateFrame(
            zonesRoot,
            "NearZoneFrame",
            PopUpDanmakuZone.Near,
            new Vector3(-1.35f, 0.05f, -0.85f),
            new Vector2(2.2f, 1.2f),
            false);

        midZoneFrame = PopUpDanmakuZoneFrame.CreateFrame(
            zonesRoot,
            "MidZoneFrame",
            PopUpDanmakuZone.Mid,
            new Vector3(0f, -0.08f, 0.08f),
            new Vector2(1.6f, 0.9f),
            false);

        farZoneFrame = PopUpDanmakuZoneFrame.CreateFrame(
            zonesRoot,
            "FarZoneFrame",
            PopUpDanmakuZone.Far,
            new Vector3(0f, 0.42f, 0.22f),
            new Vector2(2.4f, 0.8f),
            true);

        UnityEditor.Undo.RegisterCreatedObjectUndo(zonesRoot.gameObject, "Create Pop-up Zone Frames");
        UnityEditor.EditorUtility.SetDirty(this);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
        Debug.Log("已创建 Near/Mid/Far 区域调节框。请在 Scene 视图拖动框体与锚点，Play 时线框会自动隐藏。");
    }

    static void DestroyZoneFrameIfExists(PopUpDanmakuZoneFrame frame)
    {
        if (frame != null)
            UnityEditor.Undo.DestroyObjectImmediate(frame.gameObject);
    }
#endif

    void BuildAnchorLayout()
    {
        if (screenTransform == null)
        {
            Debug.LogWarning("PopUpDanmakuController: screenTransform missing, cannot build anchors.");
            return;
        }

        if (anchorRoot != null)
            Destroy(anchorRoot.gameObject);

        GameObject rootGo = new GameObject("PopUpDanmakuAnchors");
        anchorRoot = rootGo.transform;
        anchorRoot.SetParent(screenTransform, false);
        anchorRoot.localPosition = Vector3.zero;
        anchorRoot.localRotation = Quaternion.identity;
        anchorRoot.localScale = Vector3.one;

        nearAnchors = BuildZoneAnchors(PopUpDanmakuZone.Near, "Near", 2);
        midAnchors = BuildZoneAnchors(PopUpDanmakuZone.Mid, "Mid", 2);
        farAnchors = BuildZoneAnchors(PopUpDanmakuZone.Far, "Far", 1);
    }

    PopUpDanmakuAnchor[] BuildZoneAnchors(PopUpDanmakuZone zone, string prefix, int maxConcurrent)
    {
        PopUpZoneAnchorLayout layout = settings.GetLayout(zone);
        Vector3 secondary = layout.mirrorSecondaryOnX
            ? new Vector3(-layout.primaryLocalPosition.x, layout.primaryLocalPosition.y, layout.primaryLocalPosition.z)
            : layout.secondaryLocalPosition;

        if (zone == PopUpDanmakuZone.Far)
        {
            var anchors = new List<PopUpDanmakuAnchor>
            {
                CreateAnchor(prefix + "Center", zone, 0, layout.primaryLocalPosition, maxConcurrent),
                CreateAnchor(prefix + "Left", zone, 1, layout.secondaryLocalPosition, maxConcurrent)
            };

            if (layout.useTertiaryAnchor)
                anchors.Add(CreateAnchor(prefix + "Right", zone, 2, layout.tertiaryLocalPosition, maxConcurrent));

            return anchors.ToArray();
        }

        return new[]
        {
            CreateAnchor(prefix + "Left", zone, 0, layout.primaryLocalPosition, maxConcurrent),
            CreateAnchor(prefix + "Right", zone, 1, secondary, maxConcurrent)
        };
    }

    PopUpDanmakuAnchor CreateAnchor(string name, PopUpDanmakuZone zone, int slotIndex, Vector3 localPosition, int maxConcurrent)
    {
        GameObject go = new GameObject(name);
        Transform t = go.transform;
        t.SetParent(anchorRoot, false);
        t.localPosition = localPosition;
        t.localRotation = Quaternion.identity;
        t.localScale = Vector3.one;

        PopUpDanmakuAnchor anchor = go.AddComponent<PopUpDanmakuAnchor>();
        anchor.zone = zone;
        anchor.slotIndex = slotIndex;
        anchor.maxConcurrent = maxConcurrent;
        return anchor;
    }

    void EnsureLabelTemplate()
    {
        if (labelTemplate != null)
            return;

        GameObject root = new GameObject("PopUpDanmakuLabelTemplate");
        root.transform.SetParent(transform, false);
        root.SetActive(false);

        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = settings.canvasSortingOrder;

        RectTransform rect = root.GetComponent<RectTransform>();
        rect.sizeDelta = settings.labelSize;
        rect.localScale = Vector3.one * settings.worldLabelScale;

        root.AddComponent<CanvasGroup>();

        GameObject textGo = new GameObject("Text");
        textGo.transform.SetParent(root.transform, false);
        RectTransform textRect = textGo.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        TextMeshProUGUI label = textGo.AddComponent<TextMeshProUGUI>();
        label.alignment = TextAlignmentOptions.Center;
        label.richText = false;
        if (fontAsset != null)
            label.font = fontAsset;

        labelTemplate = root.AddComponent<PopUpDanmakuInstance>();
    }
}
