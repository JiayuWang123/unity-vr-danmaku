using UnityEngine;

public static class ScheduleAudioLoader
{
    public static AudioClip LoadClip(string resourcePath)
    {
        if (string.IsNullOrEmpty(resourcePath))
            return null;

        AudioClip clip = Resources.Load<AudioClip>(resourcePath);
        if (clip == null)
            Debug.LogWarning($"Audio clip not found in Resources: {resourcePath}");

        return clip;
    }
}
