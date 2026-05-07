// ═══════════════════════════════════════════════════════════════
// CHIEF RACING — Session Mode Manager
// Controls global coaching behavior based on session type.
// NOT an ICoachingModule — utility for session configuration.
// ═══════════════════════════════════════════════════════════════

namespace ChiefLogger.Core.Elite;

using System;
using System.Collections.Generic;

/// <summary>
/// Session mode configuration. Determines coaching verbosity, feature
/// enablement, and call frequency based on session type.
/// </summary>
public class SessionModeConfig
{
    /// <summary>
    /// Maximum coaching calls per lap.
    /// </summary>
    public int MaxCallsPerLap { get; set; } = 5;

    /// <summary>
    /// Minimum gap (seconds) between consecutive coaching calls.
    /// </summary>
    public float MinGapBetweenCalls { get; set; } = 8f;

    /// <summary>
    /// Allow setup advice (e.g., brake bias, suspension).
    /// </summary>
    public bool SetupAdviceAllowed { get; set; } = true;

    /// <summary>
    /// Allow hardware advice (tire strategy, fuel).
    /// </summary>
    public bool HardwareAdviceAllowed { get; set; } = true;

    /// <summary>
    /// Enable racecraft module (positioning, passing, strategy).
    /// </summary>
    public bool RacecraftEnabled { get; set; } = true;

    /// <summary>
    /// Enable tire strategy coaching.
    /// </summary>
    public bool TireStrategyEnabled { get; set; } = true;

    /// <summary>
    /// Enable mental reset coaching.
    /// </summary>
    public bool MentalResetEnabled { get; set; } = true;

    /// <summary>
    /// Enable corner-specific coaching (braking points, turn-in, etc).
    /// </summary>
    public bool CornerCoachingEnabled { get; set; } = true;

    /// <summary>
    /// Enable one-lap-fix suggestions.
    /// </summary>
    public bool OneLapFixEnabled { get; set; } = true;

    /// <summary>
    /// Overall coaching verbosity: 0.0 (silent) to 1.0 (full).
    /// </summary>
    public float CoachingVerbosity { get; set; } = 0.7f;

    /// <summary>
    /// Enable A/B testing module.
    /// </summary>
    public bool ABTestingEnabled { get; set; } = false;
}

/// <summary>
/// Session mode manager. Switches coaching configuration based on
/// session type (practice, qualifying, race, testing).
/// </summary>
public class SessionModeManager
{
    private readonly object _lock = new();
    private string _currentMode = "practice";
    private SessionModeConfig _currentConfig = new();

    private readonly Dictionary<string, SessionModeConfig> _presets = new()
    {
        {
            "practice", new SessionModeConfig
            {
                MaxCallsPerLap = 8,
                MinGapBetweenCalls = 8f,
                SetupAdviceAllowed = true,
                HardwareAdviceAllowed = true,
                RacecraftEnabled = false,
                TireStrategyEnabled = true,
                MentalResetEnabled = true,
                CornerCoachingEnabled = true,
                OneLapFixEnabled = true,
                CoachingVerbosity = 0.7f,
                ABTestingEnabled = false,
            }
        },
        {
            "practice_pro", new SessionModeConfig
            {
                MaxCallsPerLap = 12,
                MinGapBetweenCalls = 6f,
                SetupAdviceAllowed = true,
                HardwareAdviceAllowed = true,
                RacecraftEnabled = false,
                TireStrategyEnabled = true,
                MentalResetEnabled = true,
                CornerCoachingEnabled = true,
                OneLapFixEnabled = true,
                CoachingVerbosity = 1.0f,
                ABTestingEnabled = false,
            }
        },
        {
            "qualifying", new SessionModeConfig
            {
                MaxCallsPerLap = 3,
                MinGapBetweenCalls = 12f,
                SetupAdviceAllowed = false,
                HardwareAdviceAllowed = false,
                RacecraftEnabled = false,
                TireStrategyEnabled = false,
                MentalResetEnabled = false,
                CornerCoachingEnabled = true,
                OneLapFixEnabled = false,
                CoachingVerbosity = 0.3f,
                ABTestingEnabled = false,
            }
        },
        {
            "race", new SessionModeConfig
            {
                MaxCallsPerLap = 5,
                MinGapBetweenCalls = 10f,
                SetupAdviceAllowed = false,
                HardwareAdviceAllowed = false,
                RacecraftEnabled = true,
                TireStrategyEnabled = true,
                MentalResetEnabled = true,
                CornerCoachingEnabled = true,
                OneLapFixEnabled = false,
                CoachingVerbosity = 0.5f,
                ABTestingEnabled = false,
            }
        },
        {
            "testing", new SessionModeConfig
            {
                MaxCallsPerLap = 10,
                MinGapBetweenCalls = 8f,
                SetupAdviceAllowed = true,
                HardwareAdviceAllowed = true,
                RacecraftEnabled = false,
                TireStrategyEnabled = true,
                MentalResetEnabled = true,
                CornerCoachingEnabled = true,
                OneLapFixEnabled = true,
                CoachingVerbosity = 0.8f,
                ABTestingEnabled = true,
            }
        },
    };

    public string CurrentMode
    {
        get
        {
            lock (_lock)
            {
                return _currentMode;
            }
        }
    }

    public SessionModeConfig CurrentConfig
    {
        get
        {
            lock (_lock)
            {
                return new SessionModeConfig
                {
                    MaxCallsPerLap = _currentConfig.MaxCallsPerLap,
                    MinGapBetweenCalls = _currentConfig.MinGapBetweenCalls,
                    SetupAdviceAllowed = _currentConfig.SetupAdviceAllowed,
                    HardwareAdviceAllowed = _currentConfig.HardwareAdviceAllowed,
                    RacecraftEnabled = _currentConfig.RacecraftEnabled,
                    TireStrategyEnabled = _currentConfig.TireStrategyEnabled,
                    MentalResetEnabled = _currentConfig.MentalResetEnabled,
                    CornerCoachingEnabled = _currentConfig.CornerCoachingEnabled,
                    OneLapFixEnabled = _currentConfig.OneLapFixEnabled,
                    CoachingVerbosity = _currentConfig.CoachingVerbosity,
                    ABTestingEnabled = _currentConfig.ABTestingEnabled,
                };
            }
        }
    }

    /// <summary>
    /// Set the session mode by name. Updates CurrentConfig based on preset.
    /// </summary>
    public void SetMode(string mode)
    {
        if (string.IsNullOrEmpty(mode))
            return;

        lock (_lock)
        {
            string normalizedMode = mode.ToLower().Trim();

            if (_presets.TryGetValue(normalizedMode, out var config))
            {
                _currentMode = normalizedMode;
                _currentConfig = new SessionModeConfig
                {
                    MaxCallsPerLap = config.MaxCallsPerLap,
                    MinGapBetweenCalls = config.MinGapBetweenCalls,
                    SetupAdviceAllowed = config.SetupAdviceAllowed,
                    HardwareAdviceAllowed = config.HardwareAdviceAllowed,
                    RacecraftEnabled = config.RacecraftEnabled,
                    TireStrategyEnabled = config.TireStrategyEnabled,
                    MentalResetEnabled = config.MentalResetEnabled,
                    CornerCoachingEnabled = config.CornerCoachingEnabled,
                    OneLapFixEnabled = config.OneLapFixEnabled,
                    CoachingVerbosity = config.CoachingVerbosity,
                    ABTestingEnabled = config.ABTestingEnabled,
                };
            }
        }
    }

    /// <summary>
    /// Automatically detect and set session mode based on iRacing session type string.
    /// Maps iRacing session names to Chief modes.
    /// </summary>
    public void AutoDetectMode(string iRacingSessionType)
    {
        if (string.IsNullOrEmpty(iRacingSessionType))
            return;

        string normalized = iRacingSessionType.ToLower().Trim();

        // Map iRacing session types to Chief modes
        string chiefMode = normalized switch
        {
            "practice" => "practice",
            "practice official" => "practice",
            "open practice" => "practice_pro",
            "qualifying" => "qualifying",
            "official qualifying" => "qualifying",
            "race" => "race",
            "official race" => "race",
            "testing" => "testing",
            "lone qualify" => "qualifying",
            _ => "practice" // Default to practice
        };

        SetMode(chiefMode);
    }

    /// <summary>
    /// Customize the current configuration for advanced users.
    /// </summary>
    public void CustomizeCurrentConfig(Action<SessionModeConfig> customizer)
    {
        if (customizer == null)
            return;

        lock (_lock)
        {
            customizer(_currentConfig);
        }
    }

    /// <summary>
    /// Get a configuration by name (without changing current mode).
    /// </summary>
    public SessionModeConfig? GetConfigByMode(string mode)
    {
        lock (_lock)
        {
            if (_presets.TryGetValue(mode.ToLower(), out var config))
            {
                return new SessionModeConfig
                {
                    MaxCallsPerLap = config.MaxCallsPerLap,
                    MinGapBetweenCalls = config.MinGapBetweenCalls,
                    SetupAdviceAllowed = config.SetupAdviceAllowed,
                    HardwareAdviceAllowed = config.HardwareAdviceAllowed,
                    RacecraftEnabled = config.RacecraftEnabled,
                    TireStrategyEnabled = config.TireStrategyEnabled,
                    MentalResetEnabled = config.MentalResetEnabled,
                    CornerCoachingEnabled = config.CornerCoachingEnabled,
                    OneLapFixEnabled = config.OneLapFixEnabled,
                    CoachingVerbosity = config.CoachingVerbosity,
                    ABTestingEnabled = config.ABTestingEnabled,
                };
            }
            return null;
        }
    }

    /// <summary>
    /// Reset to default mode (practice).
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            SetMode("practice");
        }
    }
}
