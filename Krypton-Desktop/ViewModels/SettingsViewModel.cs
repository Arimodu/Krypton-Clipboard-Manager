using System;
using System.Reactive;
using Krypton_Desktop.Services;
using ReactiveUI;
using Serilog;

namespace Krypton_Desktop.ViewModels;

public class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settingsService;
    private readonly HotkeyManager _hotkeyManager;
    private readonly StartupService _startupService;
    private readonly Action _closeAction;
    private readonly Action<string, string>? _showNotificationAction;

    private string _hotkey;
    private int _maxHistoryItems;
    private string? _serverAddress;
    private int _serverPort;
    private string? _apiKey;
    private bool _autoConnect;
    private bool _startMinimized;
    private bool _startWithWindows;
    private bool _alwaysSearchServer;
    private bool _isRecordingHotkey;
    private string _hotkeyDisplayText;

    public string Hotkey
    {
        get => _hotkey;
        set => this.RaiseAndSetIfChanged(ref _hotkey, value);
    }

    public int MaxHistoryItems
    {
        get => _maxHistoryItems;
        set => this.RaiseAndSetIfChanged(ref _maxHistoryItems, value);
    }

    public string? ServerAddress
    {
        get => _serverAddress;
        set => this.RaiseAndSetIfChanged(ref _serverAddress, value);
    }

    public int ServerPort
    {
        get => _serverPort;
        set => this.RaiseAndSetIfChanged(ref _serverPort, value);
    }

    public string? ApiKey
    {
        get => _apiKey;
        set => this.RaiseAndSetIfChanged(ref _apiKey, value);
    }

    public bool AutoConnect
    {
        get => _autoConnect;
        set => this.RaiseAndSetIfChanged(ref _autoConnect, value);
    }

    public bool StartMinimized
    {
        get => _startMinimized;
        set => this.RaiseAndSetIfChanged(ref _startMinimized, value);
    }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set => this.RaiseAndSetIfChanged(ref _startWithWindows, value);
    }

    public bool AlwaysSearchServer
    {
        get => _alwaysSearchServer;
        set => this.RaiseAndSetIfChanged(ref _alwaysSearchServer, value);
    }

    public bool IsRecordingHotkey
    {
        get => _isRecordingHotkey;
        set => this.RaiseAndSetIfChanged(ref _isRecordingHotkey, value);
    }

    public string HotkeyDisplayText
    {
        get => _hotkeyDisplayText;
        set => this.RaiseAndSetIfChanged(ref _hotkeyDisplayText, value);
    }

    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }
    public ReactiveCommand<Unit, Unit> RecordHotkeyCommand { get; }
    public ReactiveCommand<Unit, Unit> TestNotificationCommand { get; }

#if DEBUG
    public bool ShowDebugOptions => true;
#else
    public bool ShowDebugOptions => false;
#endif

    public SettingsViewModel(
        SettingsService settingsService,
        HotkeyManager hotkeyManager,
        StartupService startupService,
        Action closeAction,
        Action<string, string>? showNotificationAction = null)
    {
        _settingsService = settingsService;
        _hotkeyManager = hotkeyManager;
        _startupService = startupService;
        _closeAction = closeAction;
        _showNotificationAction = showNotificationAction;

        // Load current settings
        var settings = _settingsService.Settings;
        _hotkey = settings.Hotkey;
        _maxHistoryItems = settings.MaxHistoryItems;
        _serverAddress = settings.ServerAddress;
        _serverPort = settings.ServerPort;
        _apiKey = settings.ApiKey;
        _autoConnect = settings.AutoConnect;
        _startMinimized = settings.StartMinimized;
        // Use actual startup state from registry/system instead of just saved setting
        _startWithWindows = _startupService.IsStartupEnabled;
        _alwaysSearchServer = settings.AlwaysSearchServer;
        _hotkeyDisplayText = _hotkey;

        SaveCommand = ReactiveCommand.Create(Save);
        CancelCommand = ReactiveCommand.Create(Cancel);
        RecordHotkeyCommand = ReactiveCommand.Create(StartRecordingHotkey);
        TestNotificationCommand = ReactiveCommand.Create(TestNotification);
    }

    private void TestNotification()
    {
        _showNotificationAction?.Invoke("Test Notification", "This is a test notification from Krypton Clipboard Manager.");
    }

    private void Save()
    {
        var settings = _settingsService.Settings;

        settings.Hotkey = _hotkey;
        settings.MaxHistoryItems = _maxHistoryItems;
        settings.ServerAddress = _serverAddress;
        settings.ServerPort = _serverPort;
        settings.ApiKey = _apiKey;
        settings.AutoConnect = _autoConnect;
        settings.StartMinimized = _startMinimized;
        settings.StartWithWindows = _startWithWindows;
        settings.AlwaysSearchServer = _alwaysSearchServer;

        _settingsService.Save();

        // Apply hotkey change
        if (_hotkeyManager.SetHotkeyFromString(_hotkey))
        {
            Log.Information("Hotkey updated to: {Hotkey}", _hotkey);
        }

        // Apply startup setting
        var currentStartupEnabled = _startupService.IsStartupEnabled;
        if (_startWithWindows != currentStartupEnabled)
        {
            if (_startupService.SetStartupEnabled(_startWithWindows))
            {
                Log.Information("Startup with system: {Enabled}", _startWithWindows);
            }
        }

        _closeAction();
    }

    private void Cancel()
    {
        _closeAction();
    }

    private void StartRecordingHotkey()
    {
        IsRecordingHotkey = true;
        HotkeyDisplayText = "Press a key combination...";
    }

    public void StopRecordingHotkey(string? newHotkey)
    {
        IsRecordingHotkey = false;
        if (!string.IsNullOrEmpty(newHotkey))
        {
            Hotkey = newHotkey;
            HotkeyDisplayText = newHotkey;
        }
        else
        {
            HotkeyDisplayText = Hotkey;
        }
    }
}
