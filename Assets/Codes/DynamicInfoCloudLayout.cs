using System.Collections.Generic;
using UnityEngine;

public class DynamicInfoCloudLayout
{
    public struct ClusterPlacement
    {
        public DanmakuSemanticCategory category;
        public CurvedDanmakuSurfaceLayer layer;
        public float u;
        public float v;
        public float weight;
        public float radiusOffset;
    }

    readonly Dictionary<DanmakuSemanticCategory, ClusterPlacement> placements = new Dictionary<DanmakuSemanticCategory, ClusterPlacement>();
    readonly List<ClusterPlacement> sortedPlacements = new List<ClusterPlacement>();
    readonly Dictionary<DanmakuSemanticCategory, int> spawnCounters = new Dictionary<DanmakuSemanticCategory, int>();

    public IReadOnlyList<ClusterPlacement> Placements => sortedPlacements;

    public void ResetSpawnCounters()
    {
        spawnCounters.Clear();
    }

    public void Rebuild(IReadOnlyList<SemanticDanmakuRecord> records, float videoTime, float windowSeconds, CurvedDanmakuCloudRig rig)
    {
        placements.Clear();
        sortedPlacements.Clear();

        if (rig == null)
            return;

        var weights = new Dictionary<DanmakuSemanticCategory, float>();
        float start = videoTime - windowSeconds;
        float end = videoTime + windowSeconds * 0.25f;

        for (int i = 0; i < records.Count; i++)
        {
            SemanticDanmakuRecord record = records[i];
            if (record.semanticLayer != DanmakuSemanticLayer.Info)
                continue;

            if (record.timeSeconds < start || record.timeSeconds > end)
                continue;

            if (!weights.ContainsKey(record.category))
                weights[record.category] = 0f;

            weights[record.category] += 1f;
        }

        if (weights.Count == 0)
        {
            EnsureFallbackClusters(rig);
            return;
        }

        foreach (KeyValuePair<DanmakuSemanticCategory, float> pair in weights)
        {
            sortedPlacements.Add(new ClusterPlacement
            {
                category = pair.Key,
                weight = pair.Value
            });
        }

        sortedPlacements.Sort((a, b) => b.weight.CompareTo(a.weight));

        for (int i = 0; i < sortedPlacements.Count; i++)
        {
            ClusterPlacement placement = sortedPlacements[i];
            bool useMid = ShouldUseMidLayer(placement.category, i, sortedPlacements.Count);
            CurvedDanmakuSurfaceLayer layer = useMid
                ? rig.midInfoLayer != null ? rig.midInfoLayer : rig.farInfoLayer
                : rig.farInfoLayer != null ? rig.farInfoLayer : rig.midInfoLayer;

            placement.layer = layer;
            placement.radiusOffset = GetCategoryRadiusOffset(placement.category);
            placement.u = 0.5f;
            placement.v = 0.5f;
            sortedPlacements[i] = placement;
            placements[placement.category] = placement;
        }
    }

    public bool TryGetSpawnPlacement(DanmakuSemanticCategory category, out ClusterPlacement placement, out float u, out float v, out float radiusOffset)
    {
        if (!TryGetPlacement(category, out placement))
        {
            u = 0.5f;
            v = 0.5f;
            radiusOffset = 0f;
            return false;
        }

        if (!spawnCounters.ContainsKey(category))
            spawnCounters[category] = 0;

        int slotIndex = spawnCounters[category]++;
        SpreadWithinCluster(placement, slotIndex, out u, out v);
        radiusOffset = placement.radiusOffset;
        return true;
    }

    // 同一类别的弹幕只在左右两侧排布（中间留给视频），
    // 左右交替出现；类别之间靠前后距离（radiusOffset）和纵向分区区分。
    static void SpreadWithinCluster(ClusterPlacement cluster, int slotIndex, out float u, out float v)
    {
        GetSideBands(cluster, out float leftMin, out float leftMax, out float rightMin, out float rightMax);

        bool leftSide = slotIndex % 2 == 0;
        int sideSlot = slotIndex / 2;

        const int cols = 3;
        const int rows = 4;
        int slot = sideSlot % (cols * rows);
        int col = slot % cols;
        int row = slot / cols;

        int shuffledCol = (col * 2 + row) % cols;
        float uMin = leftSide ? leftMin : rightMin;
        float uMax = leftSide ? leftMax : rightMax;
        u = Mathf.Lerp(uMin, uMax, (shuffledCol + 0.5f) / cols);

        GetVerticalBand(cluster, out float vMin, out float vMax);
        float rowOffset = leftSide ? 0f : 0.5f / rows;
        float rowT = (row + 0.5f) / rows;
        rowT = rowT + rowOffset;
        if (rowT > 1f)
            rowT -= 1f / rows;
        v = Mathf.Lerp(vMin, vMax, rowT);
    }

    static void GetSideBands(ClusterPlacement cluster, out float leftMin, out float leftMax, out float rightMin, out float rightMax)
    {
        float deadHalf = 0.18f;
        if (cluster.layer != null)
        {
            deadHalf = cluster.layer.centerDeadZoneHalfWidth;
            if (cluster.layer.layerKind == CurvedCloudLayerKind.NearEmotion)
                deadHalf = 0f;
        }

        const float edgeMargin = 0.03f;
        const float deadMargin = 0.04f;
        leftMin = edgeMargin;
        leftMax = 0.5f - deadHalf - deadMargin;
        rightMin = 0.5f + deadHalf + deadMargin;
        rightMax = 1f - edgeMargin;

        if (leftMax <= leftMin + 0.05f)
        {
            leftMin = 0.04f;
            leftMax = 0.38f;
        }

        if (rightMax <= rightMin + 0.05f)
        {
            rightMin = 0.62f;
            rightMax = 0.96f;
        }
    }

    static void GetVerticalBand(ClusterPlacement cluster, out float vMin, out float vMax)
    {
        bool useMid = cluster.layer != null && cluster.layer.layerKind == CurvedCloudLayerKind.MidInfo;
        if (useMid)
        {
            vMin = 0.28f;
            vMax = 0.72f;
            return;
        }

        switch (cluster.category)
        {
            case DanmakuSemanticCategory.MatchHistory:
                vMin = 0.62f;
                vMax = 0.8f;
                break;
            case DanmakuSemanticCategory.MemeJoke:
                vMin = 0.82f;
                vMax = 0.98f;
                break;
            default:
                vMin = 0.68f;
                vMax = 0.92f;
                break;
        }
    }

    // 用「前后距离」而不是左右位置来区分远层里的不同分类。
    static float GetCategoryRadiusOffset(DanmakuSemanticCategory category)
    {
        switch (category)
        {
            case DanmakuSemanticCategory.MatchHistory:
                return -0.4f;
            case DanmakuSemanticCategory.MemeJoke:
                return 0.4f;
            case DanmakuSemanticCategory.EntityRelated:
                return 0f;
            default:
                return 0f;
        }
    }

    void EnsureFallbackClusters(CurvedDanmakuCloudRig rig)
    {
        AddFallback(DanmakuSemanticCategory.EntityRelated, rig.midInfoLayer, 1f);
        AddFallback(DanmakuSemanticCategory.MatchHistory, rig.farInfoLayer != null ? rig.farInfoLayer : rig.midInfoLayer, 0.8f);
        AddFallback(DanmakuSemanticCategory.MemeJoke, rig.farInfoLayer != null ? rig.farInfoLayer : rig.midInfoLayer, 0.6f);
    }

    static bool ShouldUseMidLayer(DanmakuSemanticCategory category, int index, int count)
    {
        switch (category)
        {
            case DanmakuSemanticCategory.EntityRelated:
                return true;
            case DanmakuSemanticCategory.MatchHistory:
            case DanmakuSemanticCategory.MemeJoke:
                return false;
            default:
                return index < Mathf.CeilToInt(count * 0.5f);
        }
    }

    void AddFallback(DanmakuSemanticCategory category, CurvedDanmakuSurfaceLayer layer, float weight)
    {
        if (layer == null)
            return;

        var placement = new ClusterPlacement
        {
            category = category,
            layer = layer,
            u = 0.5f,
            v = 0.5f,
            weight = weight,
            radiusOffset = GetCategoryRadiusOffset(category)
        };

        placements[category] = placement;
        sortedPlacements.Add(placement);
    }

    public bool TryGetPlacement(DanmakuSemanticCategory category, out ClusterPlacement placement)
    {
        if (placements.TryGetValue(category, out placement))
            return true;

        if (category == DanmakuSemanticCategory.Unknown && sortedPlacements.Count > 0)
        {
            placement = sortedPlacements[0];
            return true;
        }

        placement = default;
        return false;
    }

    public ClusterPlacement GetEmotionPlacement(CurvedDanmakuCloudRig rig)
    {
        CurvedDanmakuSurfaceLayer layer = rig != null ? rig.nearEmotionLayer : null;
        return new ClusterPlacement
        {
            category = DanmakuSemanticCategory.Emotion,
            layer = layer,
            u = 0.5f,
            v = 0.35f,
            weight = 1f,
            radiusOffset = 0f
        };
    }
}
