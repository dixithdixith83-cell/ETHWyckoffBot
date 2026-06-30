using System.Diagnostics;
using System.IO;
using Serilog;

namespace ETHWyckoffBot.Services;

public class ProcessCrashMonitor : IDisposable
{
    private readonly string _heartbeatFilePath;
    private readonly string _tradeStateFilePath;
    private CancellationTokenSource? _cts;
    private Task? _heartbeatTask;
    private bool _wasUncleanShutdown;

    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CrashThreshold = TimeSpan.FromMinutes(2);

    public bool WasUncleanShutdown => _wasUncleanShutdown;
    public DateTime LastHeartbeat { get; private set; }
    public DateTime ProcessStartTime { get; } = DateTime.UtcNow;

    public event Action? OnCrashDetected;

    public ProcessCrashMonitor()
    {
        _heartbeatFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ETHWyckoffBot", "heartbeat.txt");
        _tradeStateFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ETHWyckoffBot", "trade_state.json");

        var dir = Path.GetDirectoryName(_heartbeatFilePath);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }

    public void CheckPreviousCrash()
    {
        if (!File.Exists(_heartbeatFilePath))
        {
            Log.Information("[CrashMonitor] No previous heartbeat found — clean first start");
            _wasUncleanShutdown = false;
            return;
        }

        try
        {
            var lastHeartbeatStr = File.ReadAllText(_heartbeatFilePath).Trim();
            if (DateTime.TryParse(lastHeartbeatStr, out var lastHeartbeat))
            {
                LastHeartbeat = lastHeartbeat;
                var elapsed = DateTime.UtcNow - lastHeartbeat;

                if (elapsed > CrashThreshold)
                {
                    _wasUncleanShutdown = true;
                    Log.Warning($"[CrashMonitor] Unclean shutdown detected! Last heartbeat: {lastHeartbeat:u} ({elapsed.TotalMinutes:F1} min ago)");
                    OnCrashDetected?.Invoke();
                }
                else
                {
                    Log.Information($"[CrashMonitor] Clean shutdown — last heartbeat {elapsed.TotalSeconds:F0}s ago");
                    _wasUncleanShutdown = false;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"[CrashMonitor] Could not read previous heartbeat: {ex.Message}");
        }
    }

    public void Start()
    {
        CheckPreviousCrash();

        _cts = new CancellationTokenSource();
        _heartbeatTask = Task.Run(() => HeartbeatLoop(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _heartbeatTask?.Wait(2000);
        WriteHeartbeat();
    }

    private async Task HeartbeatLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            WriteHeartbeat();
            try { await Task.Delay(HeartbeatInterval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void WriteHeartbeat()
    {
        try
        {
            File.WriteAllText(_heartbeatFilePath, DateTime.UtcNow.ToString("O"));
        }
        catch (Exception ex)
        {
            Log.Warning($"[CrashMonitor] Heartbeat write failed: {ex.Message}");
        }
    }

    public void SaveTradeState(string stateJson)
    {
        try
        {
            File.WriteAllText(_tradeStateFilePath, stateJson);
        }
        catch (Exception ex)
        {
            Log.Warning($"[CrashMonitor] Trade state save failed: {ex.Message}");
        }
    }

    public string? LoadTradeState()
    {
        try
        {
            if (File.Exists(_tradeStateFilePath))
                return File.ReadAllText(_tradeStateFilePath);
        }
        catch (Exception ex)
        {
            Log.Warning($"[CrashMonitor] Trade state load failed: {ex.Message}");
        }
        return null;
    }

    public void ClearTradeState()
    {
        try
        {
            if (File.Exists(_tradeStateFilePath))
                File.Delete(_tradeStateFilePath);
        }
        catch { }
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
