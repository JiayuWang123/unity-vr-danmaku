using System;
using System.Collections.Generic;

[Serializable]
public class DanmakuEntry
{
    public float timeSeconds;
    public string text;
    public int mode;
    public string modeName;
    public int fontSize;
    public string colorHex;
    public string userHash;
    public string danmakuId;
    public string sourceFile;
    public int trackIndex;
    public float priorityScore;
}

[Serializable]
public class DanmakuCollection
{
    public string formatVersion = "1.0";
    public string generatedAtUtc;
    public List<DanmakuEntry> entries = new List<DanmakuEntry>();
    public DanmakuProcessingStats stats = new DanmakuProcessingStats();
}

[Serializable]
public class DanmakuProcessingStats
{
    public int sourceFileCount;
    public int parsedCount;
    public int keptCount;
    public int skippedCount;
    public float firstTimeSeconds;
    public float lastTimeSeconds;
    public List<DanmakuModeCount> modeCounts = new List<DanmakuModeCount>();
    public List<DanmakuFilterReasonCount> filterReasons = new List<DanmakuFilterReasonCount>();
}

[Serializable]
public class DanmakuModeCount
{
    public string mode;
    public int count;
}

[Serializable]
public class DanmakuFilterReasonCount
{
    public string reason;
    public int count;
}
