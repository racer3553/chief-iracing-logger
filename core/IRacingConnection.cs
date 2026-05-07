// ═══════════════════════════════════════════════════════════════
// CHIEF RACING — iRacing Connection Manager
// High-level connection lifecycle: poll, connect, disconnect.
// ═══════════════════════════════════════════════════════════════

namespace ChiefLogger.Core;

public class IRacingConnection : IDisposable
{
    private readonly IRacingSdk _sdk;
    private Thread? _pollThread;
    private CancellationTokenSource? _cts;
    private bool _running;
    private bool _wasConnected;

    public bool IsConnected => _sdk.IsConnected;
    public IRacingSdk Sdk => _sdk;

    // Events
    public event Action? OnConnected;
    public event Action? OnDisconnected;
    public event Action<SessionInfo>? OnSessionInfoChanged;

    public IRacingConnection()
    {
        _sdk = new IRacingSdk();
    }

    // ═══ START / STOP POLLING ═══

    public void StartPolling()
    {
        if (_running) return;
        _running = true;
        _cts = new CancellationTokenSource();
        _pollThread = new Thread(PollLoop)
        {
            Name = "ChiefIRacingPoller",
            IsBackground = true
        };
        _pollThread.Start();
    }

    public void StopPolling()
    {
        _running = false;
        _cts?.Cancel();
        _pollThread?.Join(2000);
        _sdk.Disconnect();
    }

    // ═══ POLL LOOP ═══

    private void PollLoop()
    {
        while (_running && !(_cts?.Token.IsCancellationRequested ?? true))
        {
            try
            {
                if (!_sdk.IsConnected)
                {
                    if (_sdk.TryConnect())
                    {
                        if (!_wasConnected)
                        {
                            _wasConnected = true;
                            OnConnected?.Invoke();

                            // Read initial session info
                            var info = _sdk.UpdateSessionInfo();
                            if (info != null) OnSessionInfoChanged?.Invoke(info);
                        }
                    }
                    else
                    {
                        if (_wasConnected)
                        {
                            _wasConnected = false;
                            OnDisconnected?.Invoke();
                        }
                        Thread.Sleep(2000); // Wait before retry
                        continue;
                    }
                }

                // Check for session info updates
                var newInfo = _sdk.UpdateSessionInfo();
                if (newInfo != null)
                {
                    OnSessionInfoChanged?.Invoke(newInfo);
                }

                Thread.Sleep(1000); // Check every second
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[IRACING] Poll error: {ex.Message}");
                if (_wasConnected)
                {
                    _wasConnected = false;
                    _sdk.Disconnect();
                    OnDisconnected?.Invoke();
                }
                Thread.Sleep(3000);
            }
        }
    }

    public void Dispose()
    {
        StopPolling();
        _sdk.Dispose();
        _cts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
