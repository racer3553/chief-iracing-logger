// ═══════════════════════════════════════════════════════════════
// CHIEF RACING — iRacing Setup File Watcher
// Monitors Documents/iRacing/setups/ for new/changed .sto files.
// Copies setup files into Chief storage, parses values.
// ═══════════════════════════════════════════════════════════════

using System.Text.Json;
using ChiefLogger.Data;

namespace ChiefLogger.Core;

public class SetupWatcher : IDisposable
{
    private readonly ChiefDatabase _db;
    private readonly SetupParser _parser;
    private FileSystemWatcher? _watcher;
    private readonly string _watchFolder;
    private readonly string _storageFolder;
    private readonly HashSet<string> _processing = new();
    private readonly object _lock = new();

    // Current session context (set by SessionManager)
    public string CurrentCarName { get; set; } = "";
    public int CurrentCarId { get; set; }
    public string CurrentTrackName { get; set; } = "";
    public int CurrentTrackId { get; set; }

    // Events
    public event Action<SetupFile>? OnSetupDetected;
    public event Action<string>? OnError;

    public SetupWatcher(ChiefDatabase db, string watchFolder, string storageFolder)
    {
        _db = db;
        _parser = new SetupParser();
        _watchFolder = watchFolder;
        _storageFolder = storageFolder;

        Directory.CreateDirectory(_storageFolder);
    }

    // ═══ START / STOP ═══

    public void Start()
    {
        if (!Directory.Exists(_watchFolder))
        {
            OnError?.Invoke($"iRacing setup folder not found: {_watchFolder}");
            return;
        }

        _watcher = new FileSystemWatcher(_watchFolder)
        {
            Filter = "*.sto",
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
            EnableRaisingEvents = true,
        };

        _watcher.Created += OnFileEvent;
        _watcher.Changed += OnFileEvent;
        _watcher.Renamed += (s, e) => ProcessFile(e.FullPath);

        // Scan existing files on first start
        ScanExisting();
    }

    public void Stop()
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }
    }

    // ═══ FILE EVENTS ═══

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        // Debounce: iRacing writes files in multiple passes
        Task.Delay(500).ContinueWith(_ => ProcessFile(e.FullPath));
    }

    private void ProcessFile(string filePath)
    {
        lock (_lock)
        {
            if (_processing.Contains(filePath)) return;
            _processing.Add(filePath);
        }

        try
        {
            if (!File.Exists(filePath)) return;

            // Wait for file to be fully written
            WaitForFile(filePath);

            var fileName = Path.GetFileName(filePath);
            var content = File.ReadAllText(filePath);
            var fileInfo = new FileInfo(filePath);

            // Determine car/track from folder structure
            // iRacing: setups/{car_name}/{track_name}/setup.sto
            var (carFromPath, trackFromPath) = ParsePathContext(filePath);

            // Parse setup values
            var parsed = _parser.Parse(content);
            var parsedJson = JsonSerializer.Serialize(parsed, new JsonSerializerOptions { WriteIndented = false });

            // Copy to Chief storage
            var storedFileName = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{fileName}";
            var carFolder = SanitizeFolderName(carFromPath ?? CurrentCarName ?? "unknown");
            var storedDir = Path.Combine(_storageFolder, carFolder);
            Directory.CreateDirectory(storedDir);
            var storedPath = Path.Combine(storedDir, storedFileName);
            File.Copy(filePath, storedPath, overwrite: true);

            var setup = new SetupFile
            {
                FileId = Guid.NewGuid().ToString("N")[..16],
                FileName = fileName,
                FilePath = filePath,
                StoredPath = storedPath,
                CarName = carFromPath ?? CurrentCarName,
                CarId = CurrentCarId,
                TrackName = trackFromPath ?? CurrentTrackName,
                TrackId = CurrentTrackId,
                DetectedAt = DateTime.UtcNow.ToString("o"),
                ModifiedAt = fileInfo.LastWriteTimeUtc.ToString("o"),
                RawContent = content,
                ParsedValues = parsedJson,
            };

            _db.InsertSetupFile(setup);
            _db.EnqueueSync("setup", setup.FileId);

            OnSetupDetected?.Invoke(setup);
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Error processing {Path.GetFileName(filePath)}: {ex.Message}");
        }
        finally
        {
            lock (_lock) _processing.Remove(filePath);
        }
    }

    // ═══ INITIAL SCAN ═══

    private void ScanExisting()
    {
        try
        {
            var files = Directory.GetFiles(_watchFolder, "*.sto", SearchOption.AllDirectories);
            foreach (var file in files.OrderByDescending(f => new FileInfo(f).LastWriteTimeUtc).Take(50))
            {
                // Only process files modified in last 7 days
                var fi = new FileInfo(file);
                if (fi.LastWriteTimeUtc > DateTime.UtcNow.AddDays(-7))
                {
                    ProcessFile(file);
                }
            }
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Scan error: {ex.Message}");
        }
    }

    // ═══ HELPERS ═══

    private static (string? car, string? track) ParsePathContext(string filePath)
    {
        // iRacing folder structure: .../setups/{car}/{track}/file.sto
        // or: .../setups/{car}/file.sto (no track subfolder)
        var parts = filePath.Replace('\\', '/').Split('/');
        int setupIdx = -1;
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Equals("setups", StringComparison.OrdinalIgnoreCase))
            {
                setupIdx = i;
                break;
            }
        }

        if (setupIdx < 0 || setupIdx >= parts.Length - 2)
            return (null, null);

        string car = parts[setupIdx + 1];

        // If there's a subfolder between car and file, it's the track
        if (setupIdx + 3 <= parts.Length - 1)
        {
            string track = parts[setupIdx + 2];
            return (car, track);
        }

        return (car, null);
    }

    private static void WaitForFile(string path, int maxRetries = 10)
    {
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                return;
            }
            catch (IOException)
            {
                Thread.Sleep(200);
            }
        }
    }

    private static string SanitizeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
