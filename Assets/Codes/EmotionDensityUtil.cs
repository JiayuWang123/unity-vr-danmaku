/// <summary>
/// 情绪弹幕滑动窗口统计与触发判定（Skybox / 粒子共用同一套规则）。
/// </summary>
public static class EmotionDensityUtil
{
    public static bool IsPositiveSentiment(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return true;
        string s = raw.Trim().ToLowerInvariant();
        return s == "positive" || s == "pos" || s.Contains("正");
    }

    /// <summary>
    /// positive 星星 + light.png：最近窗口内 positive 够多，且不低于 negative。
    /// </summary>
    public static bool ShouldTriggerPositive(int positiveCount, int negativeCount, int minCount, int dominanceMargin)
    {
        return positiveCount >= minCount && positiveCount >= negativeCount + dominanceMargin;
    }

    /// <summary>
    /// negative 问号：单独判断，只看 negative 数量，不与 positive 比较。
    /// </summary>
    public static bool ShouldTriggerNegative(int negativeCount, int minCount)
    {
        return negativeCount >= minCount;
    }
}
