using System;
using System.Threading;
using System.Threading.Tasks;
using Krypton_Desktop.Models;
using Krypton_Desktop.Services.Platform;
using Serilog;
using TextCopy;

namespace Krypton_Desktop.Services;

/// <summary>
/// Monitors the system clipboard for changes.
/// Tries a platform-specific <see cref="IClipboardListener"/> first; falls back to
/// a 500 ms polling timer if the listener is unavailable on this system.
/// </summary>
public class ClipboardMonitorService : IDisposable
{
    private readonly ClipboardHistoryService _historyService;
    private readonly Timer _pollTimer;
    private IClipboardListener? _activeListener;
    private string? _lastClipboardHash;
    private bool _isMonitoring;
    private bool _isPasting;

    public event EventHandler<ClipboardItem>? ClipboardChanged;

    public bool IsMonitoring => _isMonitoring;

    /// <summary>
    /// Human-readable description of the active monitoring mechanism, suitable for
    /// display in the status UI. Built by concatenating the active listener's
    /// <see cref="IClipboardListener.ListenerType"/>, <see cref="IClipboardListener.OS"/>,
    /// and <see cref="IClipboardListener.Method"/> properties.
    /// </summary>
    public string CurrentModeDescription =>
        !_isMonitoring ? "Stopped" :
        _activeListener is { IsListening: true } l
            ? $"{l.ListenerType} – {l.OS} / {l.Method}"
            : "Polling (500ms)";

    public ClipboardMonitorService(ClipboardHistoryService historyService)
    {
        _historyService = historyService;
        _pollTimer = new Timer(PollClipboard, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        if (_isMonitoring) return;
        _isMonitoring = true;

        _activeListener = CreatePlatformListener();

        if (_activeListener != null)
        {
            _activeListener.ClipboardChanged += OnListenerClipboardChanged;
            try
            {
                if (_activeListener.Start())
                {
                    Log.Information("Clipboard monitoring started ({Description})", CurrentModeDescription);
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to start clipboard listener, falling back to polling");
            }

            _activeListener.ClipboardChanged -= OnListenerClipboardChanged;
            _activeListener.Dispose();
            _activeListener = null;
        }

        // Fallback: data polling
        _pollTimer.Change(500, 500);
        Log.Information("Clipboard monitoring started ({Description})", CurrentModeDescription);
    }

    public void Stop()
    {
        if (!_isMonitoring) return;
        _isMonitoring = false;

        if (_activeListener != null)
        {
            _activeListener.ClipboardChanged -= OnListenerClipboardChanged;
            _activeListener.Stop();
            _activeListener.Dispose();
            _activeListener = null;
        }

        _pollTimer.Change(Timeout.Infinite, Timeout.Infinite);
        Log.Information("Clipboard monitoring stopped");
    }

    private static IClipboardListener? CreatePlatformListener()
    {
        if (OperatingSystem.IsWindows()) return new WindowsClipboardListener();
        if (OperatingSystem.IsMacOS())  return new MacOSClipboardListener();
        if (OperatingSystem.IsLinux())  return new LinuxClipboardListener();
        return null;
    }

    private async void OnListenerClipboardChanged(object? sender, EventArgs e)
    {
        if (!_isMonitoring || _isPasting) return;

        try
        {
            // Brief delay to let the clipboard owner finish writing before we read.
            await Task.Delay(50);
            await CheckClipboardAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error handling clipboard change event");
        }
    }

    /// <summary>Temporarily pauses monitoring while pasting to avoid capturing our own paste.</summary>
    public void BeginPaste() => _isPasting = true;

    /// <summary>Resumes monitoring after a paste operation.</summary>
    public void EndPaste() => Task.Delay(200).ContinueWith(_ => _isPasting = false);

    /// <summary>Sets an image to the clipboard.</summary>
    public async Task SetImageAsync(byte[] pngBytes)
    {
        BeginPaste();
        try
        {
            _lastClipboardHash = ComputeHash(pngBytes);
            await ClipboardImageHelper.WriteImageAsync(pngBytes);
        }
        finally { EndPaste(); }
    }

    /// <summary>Sets text to the clipboard.</summary>
    public async Task SetTextAsync(string text)
    {
        BeginPaste();
        try
        {
            await ClipboardService.SetTextAsync(text);
            _lastClipboardHash = ComputeHash(System.Text.Encoding.UTF8.GetBytes(text));
        }
        finally { EndPaste(); }
    }

    private async void PollClipboard(object? state)
    {
        if (!_isMonitoring || _isPasting) return;

        try { await CheckClipboardAsync(); }
        catch (Exception ex) { Log.Warning(ex, "Error polling clipboard"); }
    }

    private async Task CheckClipboardAsync()
    {
        // Try image BEFORE text — images take priority
        var pngBytes = await ClipboardImageHelper.TryReadImageAsPngAsync();
        if (pngBytes is { Length: > 0 })
        {
            var imageHash = ComputeHash(pngBytes);
            if (imageHash == _lastClipboardHash) return;
            _lastClipboardHash = imageHash;

            var imageItem = ClipboardItem.FromImage(pngBytes, Environment.MachineName);
            _historyService.Add(imageItem);
            ClipboardChanged?.Invoke(this, imageItem);
            Log.Debug("Clipboard changed: {Preview}", imageItem.Preview);
            return;
        }

        var text = await ClipboardService.GetTextAsync();
        if (string.IsNullOrEmpty(text)) return;

        var hash = ComputeHash(System.Text.Encoding.UTF8.GetBytes(text));
        if (hash == _lastClipboardHash) return;
        _lastClipboardHash = hash;

        var item = ClipboardItem.FromText(text);
        _historyService.Add(item);
        ClipboardChanged?.Invoke(this, item);
        Log.Debug("Clipboard changed: {Preview}", item.Preview[..Math.Min(50, item.Preview.Length)]);
    }

    private static string ComputeHash(byte[] content)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(content));
    }

    public void Dispose()
    {
        Stop();
        _pollTimer.Dispose();
        _activeListener?.Dispose();
    }
}
