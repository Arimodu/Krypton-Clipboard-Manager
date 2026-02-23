using System;
using System.Reactive;
using ReactiveUI;
using Krypton_Desktop.Services;

namespace Krypton_Desktop.ViewModels;

public class StatusViewModel : ViewModelBase
{
    private readonly ServerConnectionService _connectionService;
    private readonly ClipboardMonitorService _clipboardMonitor;
    private readonly SettingsService _settingsService;
    private readonly Action _closeAction;

    public ReactiveCommand<Unit, Unit> CloseCommand { get; }

    public StatusViewModel(
        ServerConnectionService connectionService,
        ClipboardMonitorService clipboardMonitor,
        SettingsService settingsService,
        Action closeAction)
    {
        _connectionService = connectionService;
        _clipboardMonitor = clipboardMonitor;
        _settingsService = settingsService;
        _closeAction = closeAction;

        CloseCommand = ReactiveCommand.Create(Close);

        // Subscribe to connection state changes
        _connectionService.StateChanged += (_, _) =>
        {
            this.RaisePropertyChanged(nameof(ConnectionStatus));
            this.RaisePropertyChanged(nameof(IsConnected));
            this.RaisePropertyChanged(nameof(StatusColor));
        };
    }

    public string ConnectionStatus => _connectionService.CurrentState switch
    {
        ConnectionState.Disconnected => "Disconnected",
        ConnectionState.Connecting => "Connecting...",
        ConnectionState.Connected => "Connected (not authenticated)",
        ConnectionState.Authenticated => "Connected & Authenticated",
        ConnectionState.Reconnecting => "Reconnecting...",
        _ => "Unknown"
    };

    public bool IsConnected => _connectionService.IsAuthenticated;

    public string StatusColor => _connectionService.CurrentState switch
    {
        ConnectionState.Authenticated => "#4CAF50",
        ConnectionState.Connected => "#FFC107",
        ConnectionState.Connecting or ConnectionState.Reconnecting => "#2196F3",
        _ => "#F44336"
    };

    public string ServerAddress => string.IsNullOrEmpty(_settingsService.Settings.ServerAddress)
        ? "Not configured"
        : $"{_settingsService.Settings.ServerAddress}:{_settingsService.Settings.ServerPort}";

    public string AuthenticatedUser => _connectionService.AuthenticatedUsername ?? "Not authenticated";

    public string ClipboardMonitoringMode => _clipboardMonitor.CurrentMode switch
    {
        Services.ClipboardMonitoringMode.EventDriven => "Event-driven (Windows)",
        Services.ClipboardMonitoringMode.Polling => "Polling (500ms)",
        _ => "Stopped"
    };

    public int OfflineQueueCount => _connectionService.QueuedEntriesCount;

    public bool HasOfflineQueue => OfflineQueueCount > 0;

    public string LastSyncTime => _connectionService.LastSyncTime.HasValue
        ? _connectionService.LastSyncTime.Value.ToString("g")
        : "Never";

    public string AppVersion => Krypton.Shared.Protocol.PacketConstants.ClientVersion;

    public void Close()
    {
        _closeAction();
    }
}
