using System.IO;
using UnityEngine;

public static class StreamingAssetsReader
{
    public static string ReadText(string relativePath)
    {
        string fullPath = Path.Combine(Application.streamingAssetsPath, relativePath);

        if (!File.Exists(fullPath))
        {
            Debug.LogError($"StreamingAssets file not found: {fullPath}");
            return null;
        }

        return File.ReadAllText(fullPath);
    }

    public static T ReadJson<T>(string relativePath) where T : class
    {
        string json = ReadText(relativePath);
        if (string.IsNullOrEmpty(json))
            return null;

        return JsonUtility.FromJson<T>(json);
    }
}
