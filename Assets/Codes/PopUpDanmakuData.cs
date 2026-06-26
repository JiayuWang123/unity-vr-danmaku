using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public enum PopUpDanmakuZone
{
    Near,
    Mid,
    Far
}

[Serializable]
public class PopUpDanmakuRecord
{
    public string 弹幕内容;
    public float 出现时间;
    public int 弹幕模式;
    public int 字体大小;
    public int 颜色十进制;
    public string 发送时间;
    public string 用户加密ID;
    public int 字数;
}

[Serializable]
public class PopUpDanmakuRecordList
{
    public PopUpDanmakuRecord[] items;
}

public static class PopUpDanmakuLoader
{
    public static List<PopUpDanmakuRecord> LoadFromStreamingAssets(string fileName)
    {
        string path = Path.Combine(Application.streamingAssetsPath, "PopUpDanmaku", fileName);
        if (!File.Exists(path))
        {
            Debug.LogWarning($"Pop-up danmaku JSON not found: {path}");
            return new List<PopUpDanmakuRecord>();
        }

        string json = File.ReadAllText(path).Trim();
        if (string.IsNullOrEmpty(json))
            return new List<PopUpDanmakuRecord>();

        if (json.StartsWith("["))
            json = "{\"items\":" + json + "}";

        PopUpDanmakuRecordList list = JsonUtility.FromJson<PopUpDanmakuRecordList>(json);
        if (list?.items == null || list.items.Length == 0)
        {
            Debug.LogWarning($"Pop-up danmaku JSON is empty or invalid: {path}");
            return new List<PopUpDanmakuRecord>();
        }

        List<PopUpDanmakuRecord> records = new List<PopUpDanmakuRecord>(list.items);
        records.Sort((a, b) => a.出现时间.CompareTo(b.出现时间));
        Debug.Log($"Loaded {records.Count} pop-up danmaku entries from {fileName}");
        return records;
    }

    public static Color RecordToColor(PopUpDanmakuRecord record, Color fallback)
    {
        if (record == null)
            return fallback;

        int rgb = Mathf.Clamp(record.颜色十进制, 0, 0xFFFFFF);
        return new Color(
            ((rgb >> 16) & 0xFF) / 255f,
            ((rgb >> 8) & 0xFF) / 255f,
            (rgb & 0xFF) / 255f,
            1f);
    }
}
