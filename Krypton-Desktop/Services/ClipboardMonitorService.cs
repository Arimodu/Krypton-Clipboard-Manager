using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Krypton_Desktop.Models;
using Krypton_Desktop.Services.Platform;
using Serilog;
using TextCopy;

namespace Krypton_Desktop.Services;

public enum ClipboardMonitoringMode
{
    Stopped,
    Polling,
    /// <summary>
    /// Polls a cheap metadata counter (e.g. NSPasteboard.changeCount) rather than
    /// reading clipboard data. Faster and lighter than full polling, but not truly
    /// event-driven.
    /// </summary>
    EfficientPolling,
    EventDriven
}

/// <summary>
/// Monitors the system clipboard for changes.
/// Uses event-driven monitoring on Windows, falls back to polling on other platforms.
/// </summary>
public class ClipboardMonitorService : IDisposable
{
    private readonly ClipboardHistoryService _historyService;
    private readonly Timer _pollTimer;
    private WindowsClipboardListener? _windowsListener;
    private MacOSClipboardListener? _macOSListener;
    private LinuxClipboardListener? _linuxListener;
    private string? _lastClipboardHash;
    private bool _isMonitoring;
    private bool _isPasting;
    private ClipboardMonitoringMode _currentMode = ClipboardMonitoringMode.Stopped;

    public event EventHandler<ClipboardItem>? ClipboardChanged;

    public bool IsMonitoring => _isMonitoring;
    public ClipboardMonitoringMode CurrentMode => _currentMode;

    public ClipboardMonitorService(ClipboardHistoryService historyService)
    {
        _historyService = historyService;

        // Poll every 500ms (used as fallback or on non-Windows)
        _pollTimer = new Timer(PollClipboard, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        if (_isMonitoring) return;

        _isMonitoring = true;

        // Try event-driven monitoring on Windows
        if (OperatingSystem.IsWindows())
        {
            try
            {
                _windowsListener = new WindowsClipboardListener();
                _windowsListener.ClipboardChanged += OnWindowsClipboardChanged;

                if (_windowsListener.Start())
                {
                    _currentMode = ClipboardMonitoringMode.EventDriven;
                    Log.Information("Clipboard monitoring started (event-driven)");
                    return;
                }

                // Failed to start, clean up
                _windowsListener.ClipboardChanged -= OnWindowsClipboardChanged;
                _windowsListener.Dispose();
                _windowsListener = null;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to start Windows clipboard listener, falling back to polling");
                _windowsListener?.Dispose();
                _windowsListener = null;
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            try
            {
                _macOSListener = new MacOSClipboardListener();
                _macOSListener.ClipboardChanged += OnMacOSClipboardChanged;

                if (_macOSListener.Start())
                {
                    _currentMode = ClipboardMonitoringMode.EfficientPolling;
                    Log.Information("Clipboard monitoring started (macOS changeCount polling)");
                    return;
                }

                // Failed to start, clean up
                _macOSListener.ClipboardChanged -= OnMacOSClipboardChanged;
                _macOSListener.Dispose();
                _macOSListener = null;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to start macOS clipboard listener, falling back to polling");
                _macOSListener?.Dispose();
                _macOSListener = null;
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            try
            {
                _linuxListener = new LinuxClipboardListener();
                _linuxListener.ClipboardChanged += OnLinuxClipboardChanged;

                if (_linuxListener.Start())
                {
                    _currentMode = ClipboardMonitoringMode.EventDriven;
                    Log.Information("Clipboard monitoring started (Linux event-driven)");
                    return;
                }

                // Failed to start, clean up
                _linuxListener.ClipboardChanged -= OnLinuxClipboardChanged;
                _linuxListener.Dispose();
                _linuxListener = null;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to start Linux clipboard listener, falling back to polling");
                _linuxListener?.Dispose();
                _linuxListener = null;
            }
        }

        // Fall back to polling
        _currentMode = ClipboardMonitoringMode.Polling;
        _pollTimer.Change(500, 500);
        Log.Information("Clipboard monitoring started (polling)");
    }

    public void Stop()
    {
        if (!_isMonitoring) return;

        _isMonitoring = false;

        // Stop Windows listener if active
        if (OperatingSystem.IsWindows() && _windowsListener != null)
        {
            _windowsListener.ClipboardChanged -= OnWindowsClipboardChanged;
            _windowsListener.Stop();
            _windowsListener.Dispose();
            _windowsListener = null;
        }

        // Stop macOS listener if active
        if (OperatingSystem.IsMacOS() && _macOSListener != null)
        {
            _macOSListener.ClipboardChanged -= OnMacOSClipboardChanged;
            _macOSListener.Stop();
            _macOSListener.Dispose();
            _macOSListener = null;
        }

        // Stop Linux listener if active
        if (OperatingSystem.IsLinux() && _linuxListener != null)
        {
            _linuxListener.ClipboardChanged -= OnLinuxClipboardChanged;
            _linuxListener.Stop();
            _linuxListener.Dispose();
            _linuxListener = null;
        }

        // Stop polling timer
        _pollTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _currentMode = ClipboardMonitoringMode.Stopped;
        Log.Information("Clipboard monitoring stopped");
    }

    private async void OnWindowsClipboardChanged(object? sender, EventArgs e)
    {
        if (!_isMonitoring || _isPasting) return;

        try
        {
            // Small delay to ensure clipboard is ready
            await Task.Delay(50);
            await CheckClipboardAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error handling Windows clipboard change");
        }
    }

    private async void OnMacOSClipboardChanged(object? sender, EventArgs e)
    {
        if (!_isMonitoring || _isPasting) return;

        try
        {
            // Small delay to ensure the pasteboard has committed its new contents
            await Task.Delay(50);
            await CheckClipboardAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error handling macOS clipboard change");
        }
    }

    private async void OnLinuxClipboardChanged(object? sender, EventArgs e)
    {
        if (!_isMonitoring || _isPasting) return;

        try
        {
            // Small delay to allow the clipboard owner to finish writing
            await Task.Delay(50);
            await CheckClipboardAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error handling Linux clipboard change");
        }
    }

    /// <summary>
    /// Temporarily pauses monitoring while pasting to avoid capturing our own paste.
    /// </summary>
    public void BeginPaste()
    {
        _isPasting = true;
    }

    /// <summary>
    /// Resumes monitoring after a paste operation.
    /// </summary>
    public void EndPaste()
    {
        // Delay resuming to avoid capturing the paste
        Task.Delay(200).ContinueWith(_ => _isPasting = false);
    }

    /// <summary>
    /// Sets an image to the clipboard.
    /// </summary>
    public async Task SetImageAsync(byte[] pngBytes)
    {
        BeginPaste();
        try
        {
            _lastClipboardHash = ComputeHash(pngBytes);
            await ClipboardImageHelper.WriteImageAsync(pngBytes);
        }
        finally
        {
            EndPaste();
        }
    }

    /// <summary>
    /// Sets text to the clipboard.
    /// </summary>
    public async Task SetTextAsync(string text)
    {
        BeginPaste();
        try
        {
            await ClipboardService.SetTextAsync(text);
            _lastClipboardHash = ComputeHash(System.Text.Encoding.UTF8.GetBytes(text));
        }
        finally
        {
            EndPaste();
        }
    }

    private async void PollClipboard(object? state)
    {
        if (!_isMonitoring || _isPasting) return;

        try
        {
            await CheckClipboardAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error polling clipboard");
        }
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
        var hash = sha256.ComputeHash(content);
        return Convert.ToHexString(hash);
    }

    public void Dispose()
    {
        Stop();
        _pollTimer.Dispose();
        if (OperatingSystem.IsWindows())
            _windowsListener?.Dispose();
        if (OperatingSystem.IsMacOS())
            _macOSListener?.Dispose();
        if (OperatingSystem.IsLinux())
            _linuxListener?.Dispose();
    }
}
