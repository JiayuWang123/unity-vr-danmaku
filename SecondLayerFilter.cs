// Legacy reference only.
// The active layer-2 implementation is now Python:
//   second_layer_filter.py
//   danmaku-burst-map/python/burst_map/prefilter.py

using System.Collections.Generic;

public static class SecondLayerFilter
{
    // 分析类关键词
    private static readonly HashSet<string> analysisWords = new HashSet<string>
    {
        "梅西", "姆巴佩", "C罗", "哈兰德", "亚马尔",
        "进球", "破门", "助攻", "越位", "点球",
        "任意球", "角球", "传中", "反击", "控球",
        "世界波", "绝杀", "帽子戏法", "扑救", "VAR"

    };

    // 情绪类关键词
    private static readonly HashSet<string> atmosphereWords = new HashSet<string>
    {
        "哈哈", "哈哈哈", "666", "牛逼", "封神",
        "卧槽", "太帅了", "绝了", "离谱", "燃",
        "泪目", "笑死", "逆天", "无敌", "精彩"

    };

    // 元信息关键词
    private static readonly HashSet<string> metaWords = new HashSet<string>
    {
       "打卡", "签到", "空降", "前排", "第一",
       "考古", "二刷", "三刷", "补课", "来了",
       "有人吗", "集合", "报到"

    };

    // 噪声关键词
    private static readonly HashSet<string> noiseWords = new HashSet<string>
    {
        "111111", "222222", "333333", "......",
        "？？？？", "!!!!!!", "aaaa", "bbbb"
    };

    public static string Classify(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "REMOVE_NOISE";

        foreach (string word in noiseWords)
        {
            if (text.Contains(word))
                return "REMOVE_NOISE";
        }

        foreach (string word in metaWords)
        {
            if (text.Contains(word))
                return "DOWNRANK_META";
        }

        foreach (string word in atmosphereWords)
        {
            if (text.Contains(word))
                return "KEEP_ATMOSPHERE";
        }

        foreach (string word in analysisWords)
        {
            if (text.Contains(word))
                return "KEEP_ANALYSIS";
        }

        // 默认保留
        return "KEEP_ANALYSIS";
    }
}
