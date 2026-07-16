using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 场景 ↔ TTS 排期 JSON ↔ mp3 目录 的对应关系。
/// A2 → Audio/TTS；B2 → Audio/TTS2；Test → Audio/TTS3（互不覆盖）。
/// </summary>
public static class TtsSceneCatalog
{
    public struct Profile
    {
        public string candidatesFile;
        public string clipFolder;
    }

    public static Profile Resolve(string candidatesOverride = null)
    {
        string candidates = string.IsNullOrWhiteSpace(candidatesOverride)
            ? ResolveCandidatesForScene(SceneManager.GetActiveScene().name)
            : candidatesOverride.Replace('\\', '/');

        return new Profile
        {
            candidatesFile = candidates,
            clipFolder = InferClipFolder(candidates)
        };
    }

    public static string ResolveCandidatesForScene(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
            return "Audio/tts_candidates_no_overlap.json";

        if (string.Equals(sceneName, "B2", StringComparison.OrdinalIgnoreCase)
            || string.Equals(sceneName, "B1", StringComparison.OrdinalIgnoreCase))
            return "Audio/tts_candidates_no_overlap2.json";

        if (string.Equals(sceneName, "Test", StringComparison.OrdinalIgnoreCase))
            return "Audio/tts_candidates_no_overlap3.json";

        // A1/A2 及其他默认走第一套
        return "Audio/tts_candidates_no_overlap.json";
    }

    public static string InferClipFolder(string candidatesFile)
    {
        if (string.IsNullOrWhiteSpace(candidatesFile))
            return "Audio/TTS";

        string lower = candidatesFile.Replace('\\', '/').ToLowerInvariant();
        if (lower.Contains("no_overlap3") || lower.Contains("_3.json") || lower.EndsWith("3.json"))
            return "Audio/TTS3";

        if (lower.Contains("no_overlap2") || lower.Contains("_2.json") || lower.EndsWith("2.json"))
            return "Audio/TTS2";

        return "Audio/TTS";
    }

    public static string BuildClipRelativePath(string clipFolder, int zeroBasedIndex)
    {
        string folder = string.IsNullOrWhiteSpace(clipFolder) ? "Audio/TTS" : clipFolder.Replace('\\', '/').TrimEnd('/');
        return $"{folder}/tts_{zeroBasedIndex + 1:000}.mp3";
    }
}
