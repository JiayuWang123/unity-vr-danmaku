using System;
using System.Collections.Generic;

[Serializable]
public class AudioScheduleFile
{
    public string schema_version;
    public string video_id;
    public float duck_video_volume = 0.35f;
    public AudioScheduleEvent[] events;
    public ScheduleStats schedule_stats;
}

[Serializable]
public class AudioScheduleEvent
{
    public string id;
    public float start_sec;
    public float duration_sec;
    public float end_sec;
    public string zone;
    public string camp;
    public string speaker_role;
    public string text;
    public string audio_clip;
    public float volume = 0.8f;
    public string spatial_anchor;
    public float priority;
    public bool duck_video = true;
    public string tts_mix_type;
    public float preferred_crowd_duck_volume;
}

[Serializable]
public class ScheduleStats
{
    public int input_candidates;
    public int scheduled;
    public int dropped;
}

[Serializable]
public class CrowdAudioScheduleFile
{
    public string schema_version;
    public string video_id;
    public CrowdAudioEvent[] events;
}

[Serializable]
public class CrowdAudioEvent
{
    public string id;
    public float start_sec;
    public float stop_sec;
    public string clip;
    public float volume = 0.5f;
    public string zone;
    public string spatial;
    public string[] targets;
    public bool loop;
    public string note;
}

[Serializable]
public class AtmosphereScheduleFile
{
    public string schema_version;
    public string video_id;
    public AtmosphereScheduleEvent[] events;
}

[Serializable]
public class AtmosphereScheduleEvent
{
    public float start_sec;
    public float stop_sec;
    public string mode;
    public string note;
}

[Serializable]
public class CrowdClipBinding
{
    public string clipKey;
    public UnityEngine.AudioClip clip;
}
