// ═══════════════════════════════════════════════════════════════
// CHIEF RACING — Coaching Settings
// Configuration for the coaching engine.
// ═══════════════════════════════════════════════════════════════

using ChiefLogger.Data;

namespace ChiefLogger.Config;

/// <summary>
/// All coaching-related settings, kept separate from main app settings for clarity.
/// </summary>
public class CoachingSettings
{
    // ═══════════════════════════════════════
    // MASTER TOGGLES
    // ═══════════════════════════════════════

    /// <summary>Enable predictive coaching (look-ahead, pre-corner callouts)</summary>
    public bool PredictiveCoachEnabled { get; set; } = true;

    /// <summary>Enable reactive coaching (respond to mistakes, corrections)</summary>
    public bool ReactiveCoachEnabled { get; set; } = true;

    /// <summary>Use text-to-speech voice for coaching</summary>
    public bool VoiceEnabled { get; set; } = false;

    // ═══════════════════════════════════════
    // CORNER CALLOUT TOGGLES
    // ═══════════════════════════════════════

    /// <summary>Enable corner names and callouts</summary>
    public bool CornerCalloutsEnabled { get; set; } = true;

    /// <summary>Call out brake markers</summary>
    public bool BrakeMarkerCallouts { get; set; } = true;

    /// <summary>Suggest gear changes</summary>
    public bool GearCallouts { get; set; } = true;

    /// <summary>Call out throttle application points</summary>
    public bool ThrottleCallouts { get; set; } = true;

    /// <summary>Call out passing opportunities</summary>
    public bool PassingCallouts { get; set; } = true;

    // ═══════════════════════════════════════
    // FREQUENCY & TIMING
    // ═══════════════════════════════════════

    /// <summary>
    /// Coaching frequency: low, medium, high, pro
    /// low = only major mistakes, pro = continuous feedback
    /// </summary>
    public string CoachingFrequency { get; set; } = "medium";

    /// <summary>
    /// How many seconds before a corner to speak the callout.
    /// Values: 2.0, 4.0, 6.0
    /// </summary>
    public float SpeakBeforeCornerSeconds { get; set; } = 4.0f;

    /// <summary>
    /// Minimum time (in seconds) between coaching calls to avoid spam.
    /// </summary>
    public float MinTimeBetweenCalls { get; set; } = 3.0f;

    // ═══════════════════════════════════════
    // SUPPRESSION RULES
    // ═══════════════════════════════════════

    /// <summary>Don't talk while driver is heavily braking</summary>
    public bool MuteDuringBraking { get; set; } = true;

    /// <summary>Don't talk during side-by-side racing</summary>
    public bool MuteWhenSideBySide { get; set; } = true;

    /// <summary>Don't talk during the first lap (learning lap)</summary>
    public bool MuteFirstLap { get; set; } = true;

    // ═══════════════════════════════════════
    // SESSION MODE
    // ═══════════════════════════════════════

    /// <summary>
    /// Session mode: practice, qualifying, race
    /// Changes coaching style and what's relevant to discuss.
    /// </summary>
    public string SessionMode { get; set; } = "practice";

    // ═══════════════════════════════════════
    // VOICE SETTINGS
    // ═══════════════════════════════════════

    /// <summary>
    /// Speech rate multiplier. 0.5 = half speed, 2.0 = double speed.
    /// </summary>
    public float VoiceRate { get; set; } = 1.0f;

    /// <summary>
    /// Voice name/ID from system text-to-speech.
    /// Empty string = system default.
    /// </summary>
    public string VoiceName { get; set; } = "";

    // ═══════════════════════════════════════
    // LEARNING
    // ═══════════════════════════════════════

    /// <summary>Enable learning mode - track what instructions work</summary>
    public bool LearningEnabled { get; set; } = true;

    /// <summary>
    /// Only speak instructions with confidence score above this threshold.
    /// Range: 0.0 - 1.0
    /// </summary>
    public float MinConfidenceToSpeak { get; set; } = 0.3f;

    // ═══════════════════════════════════════
    // PERSISTENCE
    // ═══════════════════════════════════════

    /// <summary>
    /// Load all settings from the database.
    /// Returns a new CoachingSettings with defaults, updated with DB values.
    /// </summary>
    public static CoachingSettings LoadFromDatabase(ChiefDatabase db)
    {
        var settings = new CoachingSettings();

        settings.PredictiveCoachEnabled =
            db.GetSetting("coaching_predictive_enabled", "true").ToLower() == "true";
        settings.ReactiveCoachEnabled =
            db.GetSetting("coaching_reactive_enabled", "true").ToLower() == "true";
        settings.VoiceEnabled =
            db.GetSetting("coaching_voice_enabled", "false").ToLower() == "true";

        settings.CornerCalloutsEnabled =
            db.GetSetting("coaching_corner_callouts_enabled", "true").ToLower() == "true";
        settings.BrakeMarkerCallouts =
            db.GetSetting("coaching_brake_marker_callouts", "true").ToLower() == "true";
        settings.GearCallouts =
            db.GetSetting("coaching_gear_callouts", "true").ToLower() == "true";
        settings.ThrottleCallouts =
            db.GetSetting("coaching_throttle_callouts", "true").ToLower() == "true";
        settings.PassingCallouts =
            db.GetSetting("coaching_passing_callouts", "true").ToLower() == "true";

        settings.CoachingFrequency =
            db.GetSetting("coaching_frequency", "medium");
        settings.SpeakBeforeCornerSeconds =
            float.Parse(db.GetSetting("coaching_speak_before_seconds", "4.0"));
        settings.MinTimeBetweenCalls =
            float.Parse(db.GetSetting("coaching_min_time_between_calls", "3.0"));

        settings.MuteDuringBraking =
            db.GetSetting("coaching_mute_during_braking", "true").ToLower() == "true";
        settings.MuteWhenSideBySide =
            db.GetSetting("coaching_mute_side_by_side", "true").ToLower() == "true";
        settings.MuteFirstLap =
            db.GetSetting("coaching_mute_first_lap", "true").ToLower() == "true";

        settings.SessionMode =
            db.GetSetting("coaching_session_mode", "practice");

        settings.VoiceRate =
            float.Parse(db.GetSetting("coaching_voice_rate", "1.0"));
        settings.VoiceName =
            db.GetSetting("coaching_voice_name", "");

        settings.LearningEnabled =
            db.GetSetting("coaching_learning_enabled", "true").ToLower() == "true";
        settings.MinConfidenceToSpeak =
            float.Parse(db.GetSetting("coaching_min_confidence", "0.3"));

        return settings;
    }

    /// <summary>
    /// Save all settings to the database.
    /// </summary>
    public void SaveToDatabase(ChiefDatabase db)
    {
        db.SetSetting("coaching_predictive_enabled", PredictiveCoachEnabled.ToString());
        db.SetSetting("coaching_reactive_enabled", ReactiveCoachEnabled.ToString());
        db.SetSetting("coaching_voice_enabled", VoiceEnabled.ToString());

        db.SetSetting("coaching_corner_callouts_enabled", CornerCalloutsEnabled.ToString());
        db.SetSetting("coaching_brake_marker_callouts", BrakeMarkerCallouts.ToString());
        db.SetSetting("coaching_gear_callouts", GearCallouts.ToString());
        db.SetSetting("coaching_throttle_callouts", ThrottleCallouts.ToString());
        db.SetSetting("coaching_passing_callouts", PassingCallouts.ToString());

        db.SetSetting("coaching_frequency", CoachingFrequency);
        db.SetSetting("coaching_speak_before_seconds", SpeakBeforeCornerSeconds.ToString());
        db.SetSetting("coaching_min_time_between_calls", MinTimeBetweenCalls.ToString());

        db.SetSetting("coaching_mute_during_braking", MuteDuringBraking.ToString());
        db.SetSetting("coaching_mute_side_by_side", MuteWhenSideBySide.ToString());
        db.SetSetting("coaching_mute_first_lap", MuteFirstLap.ToString());

        db.SetSetting("coaching_session_mode", SessionMode);

        db.SetSetting("coaching_voice_rate", VoiceRate.ToString());
        db.SetSetting("coaching_voice_name", VoiceName);

        db.SetSetting("coaching_learning_enabled", LearningEnabled.ToString());
        db.SetSetting("coaching_min_confidence", MinConfidenceToSpeak.ToString());
    }
}
