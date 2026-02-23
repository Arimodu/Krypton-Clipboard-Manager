using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Serilog;
using SharpHook;
using SharpHook.Data;

namespace Krypton_Desktop.Services;

/// <summary>
/// Manages global hotkey registration using SharpHook.
/// </summary>
public class HotkeyManager : IDisposable
{
    private readonly TaskPoolGlobalHook _hook;
    private readonly Action _onHotkeyPressed;
    private bool _isRunning;

    // Default hotkey: Ctrl+Shift+V
    private EventMask _modifiers = EventMask.Ctrl | EventMask.Shift;
    private KeyCode _keyCode = KeyCode.VcV;

    public bool IsRunning => _isRunning;

    public string CurrentHotkeyDisplay => GetHotkeyDisplayString();

    public HotkeyManager(Action onHotkeyPressed)
    {
        _onHotkeyPressed = onHotkeyPressed;
        _hook = new TaskPoolGlobalHook();
        _hook.KeyPressed += OnKeyPressed;
    }

    public void Start()
    {
        if (_isRunning) return;

        try
        {
            _hook.RunAsync();
            _isRunning = true;
            Log.Information("Hotkey manager started. Hotkey: {Hotkey}", CurrentHotkeyDisplay);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start hotkey manager");
        }
    }

    public void Stop()
    {
        if (!_isRunning) return;

        try
        {
            _hook.Dispose();
            _isRunning = false;
            Log.Information("Hotkey manager stopped");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to stop hotkey manager");
        }
    }

    /// <summary>
    /// Sets a new hotkey combination.
    /// </summary>
    public void SetHotkey(EventMask modifiers, KeyCode keyCode)
    {
        _modifiers = modifiers;
        _keyCode = keyCode;
        Log.Information("Hotkey changed to: {Hotkey}", CurrentHotkeyDisplay);
    }

    /// <summary>
    /// Sets hotkey from string representation (e.g., "Ctrl+Shift+V").
    /// </summary>
    public bool SetHotkeyFromString(string hotkeyString)
    {
        try
        {
            var (modifiers, keyCode) = ParseHotkeyString(hotkeyString);
            SetHotkey(modifiers, keyCode);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to parse hotkey string: {Hotkey}", hotkeyString);
            return false;
        }
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        // Check if the pressed key matches our hotkey
        if (e.Data.KeyCode == _keyCode && CheckModifiers(e.RawEvent.Mask))
        {
            Log.Debug("Hotkey pressed");
            _onHotkeyPressed();

            // Suppress the event to prevent it from reaching other applications
            e.SuppressEvent = true;
        }
    }

    private bool CheckModifiers(EventMask currentMask)
    {
        // Check if required modifiers are pressed
        var ctrlRequired = (_modifiers & EventMask.Ctrl) != 0;
        var shiftRequired = (_modifiers & EventMask.Shift) != 0;
        var altRequired = (_modifiers & EventMask.Alt) != 0;
        var metaRequired = (_modifiers & EventMask.Meta) != 0;

        var ctrlPressed = (currentMask & EventMask.Ctrl) != 0;
        var shiftPressed = (currentMask & EventMask.Shift) != 0;
        var altPressed = (currentMask & EventMask.Alt) != 0;
        var metaPressed = (currentMask & EventMask.Meta) != 0;

        return ctrlRequired == ctrlPressed &&
               shiftRequired == shiftPressed &&
               altRequired == altPressed &&
               metaRequired == metaPressed;
    }

    private string GetHotkeyDisplayString()
    {
        var parts = new List<string>();

        if ((_modifiers & EventMask.Ctrl) != 0) parts.Add("Ctrl");
        if ((_modifiers & EventMask.Shift) != 0) parts.Add("Shift");
        if ((_modifiers & EventMask.Alt) != 0) parts.Add("Alt");
        if ((_modifiers & EventMask.Meta) != 0) parts.Add("Win");

        parts.Add(GetKeyName(_keyCode));

        return string.Join("+", parts);
    }

    private static string GetKeyName(KeyCode keyCode)
    {
        // Convert KeyCode to display name
        var name = keyCode.ToString();
        if (name.StartsWith("Vc"))
            name = name[2..];
        return name;
    }

    private static (EventMask modifiers, KeyCode keyCode) ParseHotkeyString(string hotkeyString)
    {
        var modifiers = EventMask.None;
        var keyCode = KeyCode.VcUndefined;

        var parts = hotkeyString.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var part in parts)
        {
            var lower = part.ToLowerInvariant();
            switch (lower)
            {
                case "ctrl":
                case "control":
                    modifiers |= EventMask.Ctrl;
                    break;
                case "shift":
                    modifiers |= EventMask.Shift;
                    break;
                case "alt":
                    modifiers |= EventMask.Alt;
                    break;
                case "win":
                case "meta":
                case "super":
                    modifiers |= EventMask.Meta;
                    break;
                default:
                    // Try to parse as a key
                    if (Enum.TryParse<KeyCode>($"Vc{part}", true, out var parsed))
                    {
                        keyCode = parsed;
                    }
                    break;
            }
        }

        if (keyCode == KeyCode.VcUndefined)
            throw new ArgumentException($"Could not parse key from hotkey string: {hotkeyString}");

        return (modifiers, keyCode);
    }

    public void Dispose()
    {
        Stop();
    }
}
