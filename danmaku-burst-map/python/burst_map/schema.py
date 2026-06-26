"""Stable field names and labels for layer-3 danmaku feature extraction."""

FEATURE_SCHEMA_VERSION = "layer3_features_v1"

FEATURE_FIELDS = [
    "schema_version",
    "danmaku_id",
    "time_sec",
    "text_raw",
    "text_norm",
    "length",
    "char_repeat_ratio",
    "punctuation_ratio",
    "symbol_ratio",
    "digit_ratio",
    "emoji_or_emoticon_count",
    "has_sports_terms",
    "has_emotion_terms",
    "has_meta_noise_terms",
    "has_toxic_terms",
    "has_meme_terms",
    "matched_terms",
    "same_text_global_count",
    "duplicate_count_in_5s",
    "time_window_density",
    "density_z_score",
    "near_burst_peak",
    "is_short_reaction",
    "is_symbol_only",
    "is_repetition_pattern",
    "feature_quality_flags",
]

LEXICON_CATEGORIES = [
    "sports",
    "emotion",
    "meta_noise",
    "toxic",
    "meme",
]
