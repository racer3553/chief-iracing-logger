// ═══════════════════════════════════════════════════════════════
// CHIEF RACING — Voice Engine
// Local text-to-speech with priority queue and message expiration.
// Uses PowerShell + Windows SAPI COM for TTS (no external dependencies).
// ═══════════════════════════════════════════════════════════════

using System.Diagnostics;

namespace ChiefLogger.Core;

// ═══════════════════════════════════════
// VOICE MESSAGE
// ═══════════════════════════════════════

public record VoiceMessage(
    string Text,
    string Priority,                // spotter, safety, coaching, info
    DateTime QueuedAt,
    float MaxAgeSeconds             // 0 = never expires
);

// ═══════════════════════════════════════
// VOICE ENGINE
// ═══════════════════════════════════════

public class VoiceEngine : IDisposable
{
    private readonly PriorityQueue<VoiceMessage, int> _messageQueue = new();
    private readonly Thread _processingThread;
    private readonly CancellationTokenSource _cancelToken = new();
    private volatile bool _isDisposed = false;
    private volatile Process? _currentSpeechProcess = null;
    private readonly object _queueLock = new();
    private readonly object _processLock = new();

    // Configuration
    private bool _enabled = true;
    private float _rate = 1.0f;
    private string _voice = ""; // Empty = system default

    // Message expiration (seconds)
    private const float SpotterExpiration = 0f;       // Never expires
    private const float SafetyExpiration = 0f;        // Never expires
    private const float CoachingExpiration = 5f;      // 5 seconds
    private const float InfoExpiration = 10f;         // 10 seconds

    // Priority levels (lower = higher priority)
    private const int PrioritySpotter = 0;
    private const int PrioritySafety = 1;
    private const int PriorityCoaching = 2;
    private const int PriorityInfo = 3;

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public float Rate
    {
        get => _rate;
        set => _rate = Math.Max(0.1f, Math.Min(2.0f, value));
    }

    public string Voice
    {
        get => _voice;
        set => _voice = value ?? "";
    }

    public bool IsSpeaking
    {
        get
        {
            lock (_processLock)
            {
                return _currentSpeechProcess != null && !_currentSpeechProcess.HasExited;
            }
        }
    }

    public VoiceEngine()
    {
        _processingThread = new Thread(ProcessQueueWorker)
        {
            IsBackground = true,
            Name = "ChiefVoiceProcessor"
        };
        _processingThread.Start();
    }

    // ═══ ENQUEUE MESSAGE ═══
    public void Enqueue(string text, string priority)
    {
        if (_isDisposed) return;
        if (!_enabled) return;
        if (string.IsNullOrWhiteSpace(text)) return;

        var msg = new VoiceMessage(
            Text: text.Trim(),
            Priority: priority.ToLower(),
            QueuedAt: DateTime.UtcNow,
            MaxAgeSeconds: GetMaxAge(priority)
        );

        lock (_queueLock)
        {
            int priorityLevel = GetPriorityLevel(priority);

            // Check if higher priority message should interrupt current speech
            bool shouldInterrupt = ShouldInterrupt(priorityLevel);

            _messageQueue.Enqueue(msg, priorityLevel);

            if (shouldInterrupt)
            {
                CancelCurrent();
            }
        }
    }

    // ═══ CANCEL CURRENT ═══
    public void CancelCurrent()
    {
        lock (_processLock)
        {
            if (_currentSpeechProcess != null && !_currentSpeechProcess.HasExited)
            {
                try
                {
                    _currentSpeechProcess.Kill();
                    _currentSpeechProcess.WaitForExit(500);
                }
                catch { }
            }
            _currentSpeechProcess = null;
        }
    }

    // ═══ CANCEL ALL ═══
    public void CancelAll()
    {
        lock (_queueLock)
        {
            // Clear queue
            while (_messageQueue.Count > 0)
            {
                _messageQueue.Dequeue();
            }
        }

        CancelCurrent();
    }

    // ═══ PROCESSING WORKER ═══
    private void ProcessQueueWorker()
    {
        while (!_cancelToken.Token.IsCancellationRequested)
        {
            try
            {
                VoiceMessage? msg = null;

                lock (_queueLock)
                {
                    // Get next message that hasn't expired
                    while (_messageQueue.Count > 0)
                    {
                        var candidate = _messageQueue.Peek();

                        // Check expiration
                        if (candidate.MaxAgeSeconds > 0)
                        {
                            var age = (DateTime.UtcNow - candidate.QueuedAt).TotalSeconds;
                            if (age > candidate.MaxAgeSeconds)
                            {
                                _messageQueue.Dequeue(); // Discard expired message
                                continue;
                            }
                        }

                        msg = _messageQueue.Dequeue();
                        break;
                    }
                }

                // Process message outside lock
                if (msg != null)
                {
                    SpeakMessage(msg);
                }
                else
                {
                    // No message available; wait a bit
                    Thread.Sleep(100);
                }
            }
            catch
            {
                Thread.Sleep(100);
            }
        }
    }

    // ═══ SPEAK MESSAGE ═══
    private void SpeakMessage(VoiceMessage msg)
    {
        if (!_enabled) return;

        // Wait for any current speech to finish
        lock (_processLock)
        {
            if (_currentSpeechProcess != null && !_currentSpeechProcess.HasExited)
            {
                try
                {
                    _currentSpeechProcess.WaitForExit(5000);
                }
                catch { }
            }

            _currentSpeechProcess = null;
        }

        // Build PowerShell command for TTS
        string psCommand = BuildPowerShellCommand(msg.Text);

        try
        {
            lock (_processLock)
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -Command \"{psCommand}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                _currentSpeechProcess = Process.Start(psi);

                if (_currentSpeechProcess != null)
                {
                    // Wait for process to complete (max 30s per message)
                    bool finished = _currentSpeechProcess.WaitForExit(30000);

                    if (!finished)
                    {
                        try { _currentSpeechProcess.Kill(); }
                        catch { }
                    }

                    _currentSpeechProcess = null;
                }
            }
        }
        catch
        {
            // TTS failed silently; continue processing queue
        }
    }

    // ═══ BUILD POWERSHELL COMMAND ═══
    private string BuildPowerShellCommand(string text)
    {
        // Escape quotes and special characters for PowerShell
        text = text.Replace("\"", "\\\"").Replace("$", "`$");

        // Build the SAPI command
        string command = @"
Add-Type -AssemblyName System.Speech;
$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer;
$synth.Rate = " + _rate + @";
";

        // Set voice if specified
        if (!string.IsNullOrEmpty(_voice))
        {
            command += @"$synth.SelectVoice('" + _voice + @"');
";
        }

        command += "$synth.Speak(\"" + text + "\");";

        return command;
    }

    // ═══ PRIORITY LOGIC ═══
    private int GetPriorityLevel(string priority)
    {
        return priority.ToLower() switch
        {
            "spotter" => PrioritySpotter,
            "safety" => PrioritySafety,
            "coaching" => PriorityCoaching,
            "info" => PriorityInfo,
            _ => PriorityInfo
        };
    }

    private float GetMaxAge(string priority)
    {
        return priority.ToLower() switch
        {
            "spotter" => SpotterExpiration,
            "safety" => SafetyExpiration,
            "coaching" => CoachingExpiration,
            "info" => InfoExpiration,
            _ => InfoExpiration
        };
    }

    private bool ShouldInterrupt(int newPriority)
    {
        // Spotter can interrupt anything
        if (newPriority == PrioritySpotter) return true;

        // Safety can interrupt coaching and info
        if (newPriority == PrioritySafety && IsSpeaking)
        {
            lock (_queueLock)
            {
                if (_messageQueue.Count > 0)
                {
                    var nextMsg = _messageQueue.Peek();
                    int nextPriority = GetPriorityLevel(nextMsg.Priority);
                    return nextPriority >= PriorityCoaching;
                }
            }
        }

        return false;
    }

    // ═══ CLEANUP ═══
    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        CancelAll();
        _cancelToken.Cancel();

        try
        {
            if (_processingThread.IsAlive)
            {
                _processingThread.Join(2000);
            }
        }
        catch { }

        _cancelToken?.Dispose();
    }
}
