using System;
using System.Reactive;
using Krypton_Desktop.Services.Platform;
using ReactiveUI;

namespace Krypton_Desktop.ViewModels;

[System.Runtime.Versioning.SupportedOSPlatform("macos")]
public class MacOSPermissionsViewModel : ViewModelBase
{
    private readonly Action _closeAction;

    private bool _inputMonitoringGranted;
    private bool _accessibilityGranted;

    public bool InputMonitoringGranted
    {
        get => _inputMonitoringGranted;
        private set => this.RaiseAndSetIfChanged(ref _inputMonitoringGranted, value);
    }

    public bool AccessibilityGranted
    {
        get => _accessibilityGranted;
        private set => this.RaiseAndSetIfChanged(ref _accessibilityGranted, value);
    }

    public bool AllGranted => InputMonitoringGranted && AccessibilityGranted;

    public ReactiveCommand<Unit, Unit> RequestInputMonitoringCommand { get; }
    public ReactiveCommand<Unit, Unit> RequestAccessibilityCommand   { get; }
    public ReactiveCommand<Unit, Unit> RefreshCommand                { get; }
    public ReactiveCommand<Unit, Unit> RestartCommand                { get; }
    public ReactiveCommand<Unit, Unit> CloseCommand                  { get; }

    public MacOSPermissionsViewModel(Action closeAction)
    {
        _closeAction = closeAction;

        RequestInputMonitoringCommand = ReactiveCommand.Create(RequestInputMonitoring);
        RequestAccessibilityCommand   = ReactiveCommand.Create(RequestAccessibility);
        RefreshCommand                = ReactiveCommand.Create(Refresh);
        RestartCommand                = ReactiveCommand.Create(MacOSPermissions.RestartApp);
        CloseCommand                  = ReactiveCommand.Create(_closeAction);

        Refresh();
    }

    private void Refresh()
    {
        InputMonitoringGranted = MacOSPermissions.HasInputMonitoringPermission();
        AccessibilityGranted   = MacOSPermissions.HasAccessibilityPermission();
        this.RaisePropertyChanged(nameof(AllGranted));
    }

    private void RequestInputMonitoring()
    {
        MacOSPermissions.RequestInputMonitoringPermission();
        // Open settings directly — the system prompt for Input Monitoring is unreliable
        // on newer macOS; opening the pane is more dependable.
        MacOSPermissions.OpenInputMonitoringSettings();
        Refresh();
    }

    private void RequestAccessibility()
    {
        MacOSPermissions.RequestAccessibilityPermission();
        Refresh();
    }
}
