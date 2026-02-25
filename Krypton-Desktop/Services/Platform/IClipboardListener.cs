using System;

namespace Krypton_Desktop.Services.Platform;

/// <summary>
/// Broad category of how a listener detects clipboard changes.
/// Used for programmatic branching; display strings are built from
/// <see cref="IClipboardListener.OS"/> and <see cref="IClipboardListener.Method"/>.
/// </summary>
public enum ClipboardListenerType
{
    /// <summary>OS-level event notification with no clipboard data reads until a change is confirmed.</summary>
    EventDriven,
    /// <summary>Polls a cheap metadata value (e.g. a change counter) rather than clipboard data.</summary>
    EfficientPolling,
}

/// <summary>
/// Common contract for all platform clipboard listeners.
/// </summary>
public interface IClipboardListener : IDisposable
{
    /// <summary>Display name of the operating system (e.g. "Windows", "macOS", "Linux").</summary>
    string OS { get; }

    /// <summary>Human-readable name of the specific mechanism used (e.g. "WM_CLIPBOARDUPDATE", "NSPasteboard changeCount", "X11 XFixes").</summary>
    string Method { get; }

    /// <summary>Broad category of this listener's detection strategy.</summary>
    ClipboardListenerType ListenerType { get; }

    /// <summary>
    /// Poll interval in milliseconds for polling-based listeners; <c>null</c> for
    /// event-driven ones where the concept does not apply.
    /// Settable to allow runtime reconfiguration without restarting the listener.
    /// </summary>
    int? PollIntervalMs { get; set; }

    bool IsListening { get; }

    event EventHandler? ClipboardChanged;

    /// <summary>Starts the listener. Returns <c>false</c> if the mechanism is unavailable on this system.</summary>
    bool Start();

    void Stop();
}
