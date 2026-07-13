using UnityEngine;

/// <summary>
/// 情绪弹幕滑动窗口统计与触发判定（Skybox / ✦☁ 粒子共用）。
/// </summary>
public static class EmotionDensityUtil
{
    public enum SkyboxDominance
    {
        None,
        Positive,
        Negative
    }

    public static bool IsPositiveSentiment(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return true;
        string s = raw.Trim().ToLowerInvariant();
        return s == "positive" || s == "pos" || s.Contains("正");
    }

    /// <summary>
    /// 天空盒取窗口内数量更多且达标的一方；双方都存在时比数量，平局回 base。
    /// </summary>
    public static SkyboxDominance ResolveSkyboxDominance(
        int positiveCount,
        int negativeCount,
        int minPositive,
        int minNegative)
    {
        bool posActive = positiveCount >= minPositive;
        bool negActive = negativeCount >= minNegative;

        if (!posActive && !negActive)
            return SkyboxDominance.None;

        if (posActive && negActive)
        {
            if (positiveCount > negativeCount) return SkyboxDominance.Positive;
            if (negativeCount > positiveCount) return SkyboxDominance.Negative;
            return SkyboxDominance.None;
        }

        return posActive ? SkyboxDominance.Positive : SkyboxDominance.Negative;
    }

    /// <summary>✦ 粒子：窗口内 positive 条数达标即可（可与 ☁ 共存）。</summary>
    public static bool IsPositiveActive(int positiveCount, int minCount)
    {
        return positiveCount >= minCount;
    }

    /// <summary>☁ 粒子：窗口内 negative 条数达标即可（可与 ✦ 共存）。</summary>
    public static bool IsNegativeActive(int negativeCount, int minCount)
    {
        return negativeCount >= minCount;
    }

    /// <summary>把窗口内条数映射为 0~1 强度（天空盒 blend、粒子密度共用曲线）。</summary>
    public static float MapCountToIntensity(int count, int minTrigger, int countForFullBlend = 5)
    {
        if (count < minTrigger)
            return 0f;

        int span = Mathf.Max(1, countForFullBlend - minTrigger + 1);
        return Mathf.Clamp01((count - minTrigger + 1) / (float)span);
    }

    /// <summary>单次爆发粒子数：随窗口内该极性条数增加，上限 maxBurst。</summary>
    public static int MapCountToSpawnBurst(int count, int minTrigger, int maxBurst, int countForFullBlend = 5)
    {
        if (count < minTrigger)
            return 0;

        float intensity = MapCountToIntensity(count, minTrigger, countForFullBlend);
        int scaled = Mathf.RoundToInt(Mathf.Lerp(2f, maxBurst, intensity));
        return Mathf.Clamp(scaled, 1, maxBurst);
    }
}
