using System;

[Serializable]
public class AudioScheduleFile
{
    public string schema_version;
    public string video_id;
    public string time_base;
    public float duck_video_volume = 0.18f;
    public float default_min_gap_sec = 0f;
    public AudioScheduleEvent[] events;
    public ScheduleStats schedule_stats;
}

[Serializable]
public class AudioScheduleEvent
{
    public string id;
    public float start_sec;
    public float original_time_sec;
    public string time_mmss;
    public float duration_sec;
    public float end_sec;
    public string zone;
    public string camp;
    public string speaker_role;
    public string text;
    public string text_raw;
    public string spoken_text;
    public string audio_clip;
    public float volume = 0.8f;
    public string spatial_anchor;
    public float priority;
    public bool duck_video = true;
    public bool allow_overlap = true;
    public string tts_mix_type;
    public string sentiment;
    public string emotion_hint;
    public string source_comment_id;
    public int duplicate_count = 1;
}

[Serializable]
public class ScheduleStats
{
    public int input_candidates;
    public int scheduled;
    public int dropped;
    public int punctuation_events;
    public int overlap_allowed_events;
}
