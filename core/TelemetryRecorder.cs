// ═══════════════════════════════════════════════════════════════
// CHIEF RACING — Telemetry Recorder
// Records iRacing telemetry at configurable sample rate.
// Buffers samples and flushes to SQLite in batches.
// ═══════════════════════════════════════════════════════════════

using System.Text.Json;
using ChiefLogger.Data;

namespace ChiefLogger.Core;

public class TelemetryRecorder : IDisposable
{
    private readonly IRacingSdk _sdk;
    private readonly ChiefDatabase _db;
    private readonly List<TelemetryRecord> _buffer = new();
    private readonly object _bufferLock = new();

    private Thread? _recordThread;
    private CancellationTokenSource? _cts;
    private bool _recording;

    public string CurrentSessionId { get; set; } = "";
    public int SampleRateHz { get; set; } = 20;
    public int BatchSize { get; set; } = 200;
    public bool IsRecording => _recording;
    public int BufferedSamples => _buffer.Count;
    public long TotalSamplesRecorded { get; private set; }

    // Events
    public event Action<TelemetrySample>? OnSample;
    public event Action<int>? OnBatchFlushed;

    public TelemetryRecorder(IRacingSdk sdk, ChiefDatabase db)
    {
        _sdk = sdk;
        _db = db;
    }

    // ═══ START / STOP ═══

    public void Start(string sessionId)
    {
        if (_recording) return;

        CurrentSessionId = sessionId;
        _recording = true;
        _cts = new CancellationTokenSource();
        TotalSamplesRecorded = 0;

        _recordThread = new Thread(RecordLoop)
        {
            Name = "ChiefTelemetryRecorder",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal
        };
        _recordThread.Start();
    }

    public void Stop()
    {
        if (!_recording) return;

        _recording = false;
        _cts?.Cancel();
        _recordThread?.Join(2000);

        // Flush remaining buffer
        FlushBuffer();
    }

    // ═══ RECORD LOOP ═══

    private void RecordLoop()
    {
        var intervalMs = 1000.0 / SampleRateHz;
        var nextSampleTime = DateTime.UtcNow;

        while (_recording && !(_cts?.Token.IsCancellationRequested ?? true))
        {
            try
            {
                if (!_sdk.IsConnected)
                {
                    Thread.Sleep(100);
                    continue;
                }

                // Wait for next sample time
                var now = DateTime.UtcNow;
                if (now < nextSampleTime)
                {
                    var waitMs = (int)(nextSampleTime - now).TotalMilliseconds;
                    if (waitMs > 0) Thread.Sleep(Math.Min(waitMs, 100));
                    continue;
                }

                nextSampleTime = now.AddMilliseconds(intervalMs);

                // Read sample from SDK
                var sample = _sdk.ReadSample();
                OnSample?.Invoke(sample);

                // Convert to record
                var record = new TelemetryRecord
                {
                    SessionId = CurrentSessionId,
                    LapNumber = sample.Lap,
                    TimestampMs = sample.TimestampMs,
                    LapDistPct = sample.LapDistPct,
                    Speed = sample.Speed,
                    Throttle = sample.Throttle,
                    Brake = sample.Brake,
                    Steering = sample.SteeringAngle,
                    Gear = sample.Gear,
                    RPM = sample.RPM,
                    FuelLevel = sample.FuelLevel,
                    LapTime = sample.LapCurrentLapTime,
                    Delta = sample.LapDeltaToSessionBestLap,
                    LatAccel = sample.LatAccel,
                    LongAccel = sample.LongAccel,
                    Yaw = sample.Yaw,
                    YawRate = sample.YawRate,
                    BrakeBias = sample.BrakesBias,
                    Position = sample.TrackPosition,
                    Incidents = sample.IncidentCount,
                };

                // Tire data as JSON
                if (sample.LFTireTemp != null)
                {
                    record.TireTemps = JsonSerializer.Serialize(new
                    {
                        LF = sample.LFTireTemp, RF = sample.RFTireTemp,
                        LR = sample.LRTireTemp, RR = sample.RRTireTemp
                    });
                }
                if (sample.LFWear != null)
                {
                    record.TireWear = JsonSerializer.Serialize(new
                    {
                        LF = sample.LFWear, RF = sample.RFWear,
                        LR = sample.LRWear, RR = sample.RRWear
                    });
                }

                lock (_bufferLock)
                {
                    _buffer.Add(record);
                    TotalSamplesRecorded++;

                    if (_buffer.Count >= BatchSize)
                    {
                        FlushBufferLocked();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TELEMETRY] Error: {ex.Message}");
                Thread.Sleep(50);
            }
        }
    }

    // ═══ BUFFER FLUSH ═══

    private void FlushBuffer()
    {
        lock (_bufferLock) FlushBufferLocked();
    }

    private void FlushBufferLocked()
    {
        if (_buffer.Count == 0) return;

        try
        {
            var batch = new List<TelemetryRecord>(_buffer);
            _buffer.Clear();
            _db.InsertTelemetryBatch(batch);
            OnBatchFlushed?.Invoke(batch.Count);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TELEMETRY] Flush error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
