using System;
using System.Collections.Generic;

/// <summary>
/// baseline (A1) 场景使用的单一时间轴弹幕记录。
/// 对应输入 JSON 字段：time_sec / new_video_time_sec / text。
/// </summary>
[Serializable]
public class BaselineDanmakuRecord
{
    public double time_sec;
    public float new_video_time_sec;
    public string text;
}

[Serializable]
public class BaselineDanmakuArrayWrapper
{
    public List<BaselineDanmakuRecord> items;
}
