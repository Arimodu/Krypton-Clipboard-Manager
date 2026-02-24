using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using Serilog;

namespace Krypton_Desktop.Services.Platform;

/// <summary>
/// macOS clipboard listener that polls NSPasteboard.changeCount via the Objective-C
/// runtime. Checking the change count is far cheaper than reading the full clipboard
/// text, so we can poll at a short interval without meaningful CPU overhead.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacOSClipboardListener : IClipboardListener
{
    private int _pollIntervalMs = 250;

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern IntPtr objc_getClass(string name);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern IntPtr sel_registerName(string name);

    // Used for methods that return an Objective-C object (id)
    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    // Used for methods that return NSInteger (nativeint / long on 64-bit macOS)
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend_nint(IntPtr receiver, IntPtr selector);

    private Timer? _timer;
    private IntPtr _pasteboard;
    private IntPtr _changeCountSel;
    private nint _lastChangeCount;
    private volatile bool _isRunning;
    private bool _isDisposed;

    public string OS => "macOS";
    public string Method => "NSPasteboard changeCount";
    public ClipboardListenerType ListenerType => ClipboardListenerType.EfficientPolling;
    public int? PollIntervalMs
    {
        get => _pollIntervalMs;
        set
        {
            if (value is null) return;
            _pollIntervalMs = value.Value;
            if (_isRunning) _timer?.Change(value.Value, value.Value);
        }
    }

    public event EventHandler? ClipboardChanged;
    public bool IsListening => _isRunning;

    public bool Start()
    {
        if (_isRunning) return true;

        try
        {
            var nsPasteboardClass = objc_getClass("NSPasteboard");
            if (nsPasteboardClass == IntPtr.Zero)
            {
                Log.Error("macOS clipboard listener: NSPasteboard class not found");
                return false;
            }

            var generalPasteboardSel = sel_registerName("generalPasteboard");
            _changeCountSel = sel_registerName("changeCount");

            _pasteboard = objc_msgSend(nsPasteboardClass, generalPasteboardSel);
            if (_pasteboard == IntPtr.Zero)
            {
                Log.Error("macOS clipboard listener: failed to get NSPasteboard.generalPasteboard");
                return false;
            }

            // Snapshot the current count so the first poll doesn't spuriously fire
            _lastChangeCount = objc_msgSend_nint(_pasteboard, _changeCountSel);

            _isRunning = true;
            _timer = new Timer(Poll, null, _pollIntervalMs, _pollIntervalMs);

            Log.Information("macOS clipboard listener started (polling changeCount every {Interval}ms)", _pollIntervalMs);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start macOS clipboard listener");
            return false;
        }
    }

    public void Stop()
    {
        if (!_isRunning) return;

        _isRunning = false;
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);

        Log.Information("macOS clipboard listener stopped");
    }

    private void Poll(object? state)
    {
        if (!_isRunning) return;

        try
        {
            var currentCount = objc_msgSend_nint(_pasteboard, _changeCountSel);
            if (currentCount == _lastChangeCount) return;

            _lastChangeCount = currentCount;
            ClipboardChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error polling NSPasteboard changeCount");
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Stop();
        _timer?.Dispose();
        GC.SuppressFinalize(this);
    }

    ~MacOSClipboardListener()
    {
        Dispose();
    }
}
