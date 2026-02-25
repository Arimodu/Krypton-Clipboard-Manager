using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using Serilog;

namespace Krypton_Desktop.Services.Platform;

/// <summary>
/// Linux clipboard listener that uses OS-level event notifications instead of
/// data polling.
///
/// Strategy (tried in order):
///   1. Wayland  — spawns <c>wl-paste --watch</c> which invokes a shell command on every
///                 clipboard change; a <see cref="FileSystemWatcher"/> (backed by inotify)
///                 detects the resulting file touch and fires <see cref="ClipboardChanged"/>.
///   2. X11      — opens a display connection, subscribes to XFixes SelectionNotify
///                 events for the CLIPBOARD atom via <c>XFixesSelectSelectionInput</c>,
///                 and runs a background event loop; truly event-driven with zero data
///                 reads until a change is confirmed.
///   3. Fallback — returns <c>false</c>; caller should fall back to polling.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxClipboardListener : IClipboardListener
{
    // ── X11 / XFixes P/Invoke ────────────────────────────────────────────────

    [DllImport("libX11.so.6", CharSet = CharSet.Ansi)]
    private static extern IntPtr XOpenDisplay(string? display);

    [DllImport("libX11.so.6")]
    private static extern void XCloseDisplay(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XDefaultRootWindow(IntPtr display);

    [DllImport("libX11.so.6", CharSet = CharSet.Ansi)]
    private static extern IntPtr XInternAtom(IntPtr display, string atomName, bool onlyIfExists);

    [DllImport("libX11.so.6")]
    private static extern int XNextEvent(IntPtr display, out XEvent xEvent);

    [DllImport("libX11.so.6")]
    private static extern int XPending(IntPtr display);

    [DllImport("libXfixes.so.3")]
    private static extern bool XFixesQueryExtension(IntPtr display, out int eventBase, out int errorBase);

    [DllImport("libXfixes.so.3")]
    private static extern void XFixesSelectSelectionInput(IntPtr display, IntPtr window,
        IntPtr selection, uint eventMask);

    /// <summary>
    /// XEvent is a 192-byte union on 64-bit Linux. We only read the first int (type).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 192)]
    private struct XEvent
    {
        public int type;
    }

    private const uint XFixesSetSelectionOwnerNotifyMask = 1u;

    // ── State ────────────────────────────────────────────────────────────────

    private volatile bool _isRunning;
    private bool _isDisposed;

    // Wayland resources
    private FileSystemWatcher? _watcher;
    private Process? _watchProcess;
    private string? _signalFile;

    // X11 resources
    private Thread? _x11Thread;
    private IntPtr _x11Display;

    public string OS => "Linux";
    // Set during Start() once the active strategy is determined
    public string Method { get; private set; } = string.Empty;
    public ClipboardListenerType ListenerType { get; private set; } = ClipboardListenerType.EventDriven;
    public int? PollIntervalMs { get => null; set { } }

    public event EventHandler? ClipboardChanged;
    public bool IsListening => _isRunning;

    // ── Public API ───────────────────────────────────────────────────────────

    public bool Start()
    {
        if (_isRunning) return true;

        // Try Wayland first (WAYLAND_DISPLAY is set on native Wayland sessions)
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
        {
            if (TryStartWayland())
            {
                _isRunning = true;
                return true;
            }
        }

        // Try X11 (DISPLAY is set on X11 and XWayland sessions)
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
        {
            if (TryStartX11())
            {
                _isRunning = true;
                return true;
            }
        }

        return false;
    }

    public void Stop()
    {
        if (!_isRunning) return;
        _isRunning = false;

        StopWayland();
        StopX11();

        Log.Information("Linux clipboard listener stopped");
    }

    // ── Wayland ──────────────────────────────────────────────────────────────

    private bool TryStartWayland()
    {
        try
        {
            // Use a temp file as an inotify-compatible signal: wl-paste --watch runs
            // a shell command that appends to this file on each clipboard change.
            // FileSystemWatcher (backed by inotify) is event-driven at the OS level.
            _signalFile = Path.Combine(Path.GetTempPath(), $"krypton-clip-{Environment.ProcessId}");
            File.WriteAllText(_signalFile, string.Empty);

            _watcher = new FileSystemWatcher(Path.GetTempPath(), Path.GetFileName(_signalFile))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = false
            };
            _watcher.Changed += OnSignalFileChanged;

            // wl-paste --watch executes the given command each time the clipboard changes.
            // The shell command appends one byte to the signal file, triggering inotify.
            var psi = new ProcessStartInfo
            {
                FileName = "wl-paste",
                Arguments = $"--watch sh -c \"echo 1 >> '{_signalFile}'\"",
                UseShellExecute = false,
                RedirectStandardError = false,
                CreateNoWindow = true
            };
            _watchProcess = Process.Start(psi);

            if (_watchProcess == null || _watchProcess.HasExited)
            {
                CleanupWayland();
                return false;
            }

            _watcher.EnableRaisingEvents = true;

            Method = "Wayland inotify";
            ListenerType = ClipboardListenerType.EventDriven;
            Log.Information("Linux clipboard listener started (Wayland / wl-paste --watch + inotify)");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to start Wayland clipboard listener");
            CleanupWayland();
            return false;
        }
    }

    private void OnSignalFileChanged(object sender, FileSystemEventArgs e)
    {
        if (_isRunning)
            ClipboardChanged?.Invoke(this, EventArgs.Empty);
    }

    private void StopWayland()
    {
        if (_watchProcess != null)
        {
            try { _watchProcess.Kill(); } catch { /* already exited */ }
            _watchProcess.Dispose();
            _watchProcess = null;
        }
        CleanupWayland();
    }

    private void CleanupWayland()
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnSignalFileChanged;
            _watcher.Dispose();
            _watcher = null;
        }

        if (_signalFile != null && File.Exists(_signalFile))
        {
            try { File.Delete(_signalFile); } catch { /* best effort */ }
            _signalFile = null;
        }
    }

    // ── X11 / XFixes ─────────────────────────────────────────────────────────

    private bool TryStartX11()
    {
        try
        {
            // Open a dedicated display connection for the event loop thread.
            var display = XOpenDisplay(null);
            if (display == IntPtr.Zero)
            {
                Log.Warning("Linux clipboard listener: XOpenDisplay returned null");
                return false;
            }

            var root = XDefaultRootWindow(display);
            var clipboard = XInternAtom(display, "CLIPBOARD", false);

            if (!XFixesQueryExtension(display, out int eventBase, out _))
            {
                Log.Warning("Linux clipboard listener: XFixes extension not available");
                XCloseDisplay(display);
                return false;
            }

            XFixesSelectSelectionInput(display, root, clipboard, XFixesSetSelectionOwnerNotifyMask);

            _x11Display = display;
            _x11Thread = new Thread(() => X11EventLoop(display, eventBase, root, clipboard))
            {
                IsBackground = true,
                Name = "LinuxClipboardListener"
            };
            _x11Thread.Start();

            Method = "X11 XFixes";
            ListenerType = ClipboardListenerType.EventDriven;
            Log.Information("Linux clipboard listener started (X11 XFixes SelectionNotify)");
            return true;
        }
        catch (DllNotFoundException ex)
        {
            Log.Warning(ex, "libX11/libXfixes not available; X11 clipboard listener skipped");
            return false;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to start X11 clipboard listener");
            return false;
        }
    }

    private void X11EventLoop(IntPtr display, int eventBase, IntPtr root, IntPtr clipboard)
    {
        try
        {
            while (_isRunning)
            {
                // XPending returns the number of queued events without blocking.
                // We only call the blocking XNextEvent when there is actually something
                // to process, avoiding an unkillable blocking call during Stop().
                if (XPending(display) > 0)
                {
                    XNextEvent(display, out var xEvent);

                    // eventBase + 0 == XFixesSelectionNotify
                    // Since we only registered XFixesSetSelectionOwnerNotifyMask,
                    // this fires when another process writes to the CLIPBOARD selection.
                    if (xEvent.type == eventBase && _isRunning)
                        ClipboardChanged?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    // Short sleep to avoid busy-waiting. This is NOT clipboard polling —
                    // we only read the clipboard in CheckClipboardAsync once the event fires.
                    Thread.Sleep(50);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error in X11 clipboard event loop");
        }
        finally
        {
            if (display != IntPtr.Zero)
                XCloseDisplay(display);
        }
    }

    private void StopX11()
    {
        // Setting _isRunning = false causes the event loop to exit on its next
        // XPending check (within 50 ms). No explicit unblock needed.
        _x11Thread?.Join(500);
        _x11Thread = null;
        _x11Display = IntPtr.Zero;
    }

    // ── IDisposable ──────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Stop();
        GC.SuppressFinalize(this);
    }

    ~LinuxClipboardListener()
    {
        Dispose();
    }
}
