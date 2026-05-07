// ═══════════════════════════════════════════════════════════════
// CHIEF RACING — Session Manager
// Manages session lifecycle: detect, start, track laps, end.
// ═══════════════════════════════════════════════════════════════

using ChiefLogger.Data;

namespace ChiefLogger.Core;

public class SessionManager : IDisposable
{
    private readonly IRacingSdk _sdk;
    private readonly ChiefDatabase _db;
    private readonly TelemetryRecorder _recorder;
    private readonly EventHooks _events;

    private Thread? _monitorThread;
    private CancellationTokenSource? _cts;
    private bool _running;

    // Current state
    private string _currentSessionId = "";
    private int _currentLap = -1;
    private float _bestLapTime = float.MaxValue;
    private int _bestLapNumber = 0;
    private int _lastIncidents = 0;
    private float _lapStartFuel = 0f;
    private float _sessionStartFuel = 0f;
    private int _sessionTotalLaps = 0;
    private bool _isLogging;

    // Lap aggregation
    private float _lapMaxSpeed = 0f;
    private float _lapMinSpeed = float.MaxValue;
    private float _lapThrottleSum = 0f;
    private float _lapBrakeSum = 0f;
    private float _lapMaxLatG = 0f;
    private float _lapMaxLongG = 0f;
    private int _lapSampleCount = 0;
    private int _lapGearChanges = 0;
    private int _lastGear = 0;

    // Public state
    public bool IsLogging => _isLogging;
    public string CurrentSessionId => _currentSessionId;
    public int CurrentLap => _currentLap;
    public float BestLapTime => _bestLapTime == float.MaxValue ? 0 : _bestLapTime;
    public LoggedSession? ActiveSession { get; private set; }

    // Events
    public event Action<LoggedSession>? OnSessionStarted;
    public event Action<LoggedSession>? OnSessionEnded;
    public event Action<LoggedLap>? OnLapCompleted;
    public event Action<string>? OnStatusChanged;

    public SessionManager(IRacingSdk sdk, ChiefDatabase db, TelemetryRecorder recorder, EventHooks events)
    {
        _sdk = sdk;
        _db = db;
        _recorder = recorder;
        _events = events;
    }

    // ═══ START / STOP MONITORING ═══

    public void StartMonitoring()
    {
        if (_running) return;
        _running = true;
        _cts = new CancellationTokenSource();
        _monitorThread = new Thread(MonitorLoop)
        {
            Name = "ChiefSessionMonitor",
            IsBackground = true
        };
        _monitorThread.Start();
    }

    public void StopMonitoring()
    {
        _running = false;
        _cts?.Cancel();
        if (_isLogging) StopLogging();
        _monitorThread?.Join(2000);
    }

    // ═══ MANUAL LOG CONTROL ═══

    public void StartLogging()
    {
        if (_isLogging || !_sdk.IsConnected) return;

        var sessionInfo = _sdk.UpdateSessionInfo() ?? _sdk.CurrentSession;
        _currentSessionId = Guid.NewGuid().ToString("N")[..16];

        ActiveSession = new LoggedSession
        {
            SessionId = _currentSessionId,
            IRacingSubSessionId = sessionInfo.SubSessionId,
            DriverName = sessionInfo.DriverName,
            DriverId = sessionInfo.DriverId,
            CarName = sessionInfo.CarName,
            CarScreenName = sessionInfo.CarScreenName,
            CarId = sessionInfo.CarId,
            TrackName = sessionInfo.TrackName,
            TrackDisplayName = sessionInfo.TrackDisplayName,
            TrackId = sessionInfo.TrackId,
            TrackConfig = sessionInfo.TrackConfig,
            SessionType = sessionInfo.SessionType,
            AirTemp = sessionInfo.AirTemp,
            TrackTemp = sessionInfo.TrackTemp,
            Skies = sessionInfo.Skies,
            WindSpeed = sessionInfo.WindSpeed,
            Humidity = sessionInfo.Humidity,
            StartedAt = DateTime.UtcNow.ToString("o"),
            SessionInfoYaml = sessionInfo.RawYaml,
        };

        _db.InsertSession(ActiveSession);
        _recorder.Start(_currentSessionId);

        _currentLap = -1;
        _bestLapTime = float.MaxValue;
        _bestLapNumber = 0;
        _lastIncidents = 0;
        _sessionTotalLaps = 0;
        _sessionStartFuel = _sdk.GetFloat("FuelLevel");
        _isLogging = true;

        _events.FireEvent("session_started", _currentSessionId, 0, 0f,
            $"Session started: {sessionInfo.CarScreenName} at {sessionInfo.TrackDisplayName}");

        OnSessionStarted?.Invoke(ActiveSession);
        OnStatusChanged?.Invoke("Logging...");
    }

    public void StopLogging()
    {
        if (!_isLogging) return;

        _recorder.Stop();

        // Finalize session
        if (ActiveSession != null)
        {
            var fuelNow = _sdk.IsConnected ? _sdk.GetFloat("FuelLevel") : 0f;
            var fuelUsed = _sessionStartFuel - fuelNow;
            var fuelPerLap = _sessionTotalLaps > 0 ? fuelUsed / _sessionTotalLaps : 0f;
            var position = _sdk.IsConnected ? _sdk.GetInt("PlayerCarPosition") : 0;
            var incidents = _sdk.IsConnected ? _sdk.GetInt("PlayerCarMyIncidentCount") : 0;

            _db.UpdateSessionEnd(_currentSessionId,
                DateTime.UtcNow.ToString("o"),
                _sessionTotalLaps,
                _bestLapTime == float.MaxValue ? 0 : _bestLapTime,
                _bestLapNumber, position, incidents, fuelUsed, fuelPerLap);

            ActiveSession.EndedAt = DateTime.UtcNow.ToString("o");
            ActiveSession.TotalLaps = _sessionTotalLaps;
            ActiveSession.BestLapTime = _bestLapTime == float.MaxValue ? 0 : _bestLapTime;
            ActiveSession.IncidentCount = incidents;

            // Queue for sync
            _db.EnqueueSync("session", _currentSessionId);

            _events.FireEvent("session_ended", _currentSessionId, _currentLap, 0f,
                $"Session ended: {_sessionTotalLaps} laps, best {_bestLapTime:F3}s");

            OnSessionEnded?.Invoke(ActiveSession);
        }

        _isLogging = false;
        OnStatusChanged?.Invoke("Stopped");
    }

    // ═══ MONITOR LOOP ═══

    private void MonitorLoop()
    {
        while (_running && !(_cts?.Token.IsCancellationRequested ?? true))
        {
            try
            {
                if (!_sdk.IsConnected)
                {
                    Thread.Sleep(500);
                    continue;
                }

                if (!_isLogging)
                {
                    Thread.Sleep(200);
                    continue;
                }

                // Check for session info updates
                _sdk.UpdateSessionInfo();

                // Track laps
                var lap = _sdk.GetInt("Lap");
                if (lap > 0 && lap != _currentLap)
                {
                    if (_currentLap > 0)
                    {
                        CompleteLap(_currentLap);
                    }
                    StartNewLap(lap);
                }

                // Aggregate telemetry for lap stats
                if (_isLogging && _currentLap > 0)
                {
                    var speed = _sdk.GetFloat("Speed");
                    var throttle = _sdk.GetFloat("Throttle");
                    var brake = _sdk.GetFloat("Brake");
                    var gear = _sdk.GetInt("Gear");

                    _lapMaxSpeed = Math.Max(_lapMaxSpeed, speed);
                    if (speed > 1f) _lapMinSpeed = Math.Min(_lapMinSpeed, speed);
                    _lapThrottleSum += throttle;
                    _lapBrakeSum += brake;
                    _lapMaxLatG = Math.Max(_lapMaxLatG, Math.Abs(_sdk.GetFloat("LatAccel")));
                    _lapMaxLongG = Math.Max(_lapMaxLongG, Math.Abs(_sdk.GetFloat("LongAccel")));
                    _lapSampleCount++;

                    if (gear != _lastGear && _lastGear != 0) _lapGearChanges++;
                    _lastGear = gear;

                    // Check coaching events
                    var incidents = _sdk.GetInt("PlayerCarMyIncidentCount");
                    if (incidents > _lastIncidents)
                    {
                        var incDiff = incidents - _lastIncidents;
                        _events.FireEvent("incident", _currentSessionId, _currentLap,
                            _sdk.GetFloat("LapDistPct"), $"+{incDiff}x incident", "warning");
                        _lastIncidents = incidents;
                    }

                    // Fuel warning
                    var fuel = _sdk.GetFloat("FuelLevel");
                    if (fuel < 2.0f && fuel > 0.1f)
                    {
                        _events.FireEvent("fuel_warning", _currentSessionId, _currentLap,
                            _sdk.GetFloat("LapDistPct"), $"Low fuel: {fuel:F1}L", "warning");
                    }
                }

                Thread.Sleep(50); // 20Hz monitoring loop
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SESSION] Monitor error: {ex.Message}");
                Thread.Sleep(200);
            }
        }
    }

    // ═══ LAP TRACKING ═══

    private void StartNewLap(int lapNumber)
    {
        _currentLap = lapNumber;
        _sessionTotalLaps = Math.Max(_sessionTotalLaps, lapNumber);
        _lapMaxSpeed = 0f;
        _lapMinSpeed = float.MaxValue;
        _lapThrottleSum = 0f;
        _lapBrakeSum = 0f;
        _lapMaxLatG = 0f;
        _lapMaxLongG = 0f;
        _lapSampleCount = 0;
        _lapGearChanges = 0;
        _lapStartFuel = _sdk.GetFloat("FuelLevel");

        _events.FireEvent("lap_started", _currentSessionId, lapNumber, 0f,
            $"Lap {lapNumber} started");
    }

    private void CompleteLap(int lapNumber)
    {
        var lapTime = _sdk.GetFloat("LapLastLapTime");
        if (lapTime <= 0) return; // Invalid

        var fuelNow = _sdk.GetFloat("FuelLevel");
        var fuelUsed = _lapStartFuel - fuelNow;
        var incidents = _sdk.GetInt("PlayerCarMyIncidentCount");

        var lap = new LoggedLap
        {
            SessionId = _currentSessionId,
            LapNumber = lapNumber,
            LapTime = lapTime,
            IsValid = lapTime > 0 && (incidents - _lastIncidents) == 0,
            IncidentsThisLap = Math.Max(0, incidents - _lastIncidents),
            FuelUsed = Math.Max(0, fuelUsed),
            FuelRemaining = fuelNow,
            MaxSpeed = _lapMaxSpeed,
            MinSpeed = _lapMinSpeed == float.MaxValue ? 0 : _lapMinSpeed,
            AvgThrottle = _lapSampleCount > 0 ? _lapThrottleSum / _lapSampleCount : 0,
            AvgBrake = _lapSampleCount > 0 ? _lapBrakeSum / _lapSampleCount : 0,
            MaxLatG = _lapMaxLatG,
            MaxLongG = _lapMaxLongG,
            Position = _sdk.GetInt("PlayerCarPosition"),
            DeltaToBest = lapTime - (_bestLapTime == float.MaxValue ? lapTime : _bestLapTime),
            GearChanges = _lapGearChanges,
        };

        _db.InsertLap(lap);

        if (lapTime < _bestLapTime)
        {
            _bestLapTime = lapTime;
            _bestLapNumber = lapNumber;
        }

        _events.FireEvent("lap_completed", _currentSessionId, lapNumber, 1f,
            $"Lap {lapNumber}: {lapTime:F3}s (best: {_bestLapTime:F3}s)");

        OnLapCompleted?.Invoke(lap);
    }

    public void Dispose()
    {
        StopMonitoring();
        _cts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
