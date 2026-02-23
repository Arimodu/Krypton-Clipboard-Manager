using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Krypton_Desktop.ViewModels;

namespace Krypton_Desktop.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (DataContext is not SettingsViewModel vm)
            return;

        if (e.Key == Key.Escape)
        {
            if (vm.IsRecordingHotkey)
            {
                vm.StopRecordingHotkey(null);
                e.Handled = true;
            }
            else
            {
                Close();
                e.Handled = true;
            }
            return;
        }

        if (!vm.IsRecordingHotkey)
            return;

        // Build hotkey string from pressed keys
        var parts = new List<string>();

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            parts.Add("Ctrl");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            parts.Add("Shift");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            parts.Add("Alt");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Meta))
            parts.Add("Win");

        // Only accept if a modifier is pressed and a non-modifier key is pressed
        if (parts.Count > 0 && !IsModifierKey(e.Key))
        {
            parts.Add(GetKeyName(e.Key));
            var hotkey = string.Join("+", parts);
            vm.StopRecordingHotkey(hotkey);
            e.Handled = true;
        }
    }

    private static bool IsModifierKey(Key key)
    {
        return key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin;
    }

    private static string GetKeyName(Key key)
    {
        return key switch
        {
            Key.D0 => "0",
            Key.D1 => "1",
            Key.D2 => "2",
            Key.D3 => "3",
            Key.D4 => "4",
            Key.D5 => "5",
            Key.D6 => "6",
            Key.D7 => "7",
            Key.D8 => "8",
            Key.D9 => "9",
            Key.OemMinus => "-",
            Key.OemPlus => "=",
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            Key.OemPipe => "\\",
            Key.OemSemicolon => ";",
            Key.OemQuotes => "'",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.OemQuestion => "/",
            Key.OemTilde => "`",
            _ => key.ToString()
        };
    }
}
