using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Serilog;

namespace Krypton_Desktop.Services;

/// <summary>
/// Manages the system tray icon and context menu.
/// </summary>
public class TrayIconService : IDisposable
{
    private TrayIcon? _trayIcon;
    private NativeMenuItem? _connectItem;
    private NativeMenuItem? _updateItem;
    private readonly Action _showPopupAction;
    private readonly Action _showSettingsAction;
    private readonly Action _showStatusAction;
    private readonly Action _showLoginAction;
    private readonly Action _disconnectAction;
    private readonly Func<bool> _isConnectedFunc;
    private readonly Action _exitAction;
    private Action? _pendingNotificationAction;

    public TrayIconService(
        Action showPopupAction,
        Action showSettingsAction,
        Action showStatusAction,
        Action showLoginAction,
        Action disconnectAction,
        Func<bool> isConnectedFunc,
        Action exitAction)
    {
        _showPopupAction = showPopupAction;
        _showSettingsAction = showSettingsAction;
        _showStatusAction = showStatusAction;
        _showLoginAction = showLoginAction;
        _disconnectAction = disconnectAction;
        _isConnectedFunc = isConnectedFunc;
        _exitAction = exitAction;
    }

    public void Initialize()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        var menu = new NativeMenu();

        var showItem = new NativeMenuItem("Show Clipboard History");
        showItem.Click += (_, _) => _showPopupAction();
        menu.Add(showItem);

        menu.Add(new NativeMenuItemSeparator());

        _connectItem = new NativeMenuItem("Connect to Server...");
        _connectItem.Click += (_, _) => OnConnectItemClicked();
        menu.Add(_connectItem);

        menu.Add(new NativeMenuItemSeparator());

        var settingsItem = new NativeMenuItem("Settings");
        settingsItem.Click += (_, _) => _showSettingsAction();
        menu.Add(settingsItem);

        var statusItem = new NativeMenuItem("Status");
        statusItem.Click += (_, _) => _showStatusAction();
        menu.Add(statusItem);

        menu.Add(new NativeMenuItemSeparator());

        var exitItem = new NativeMenuItem("Exit");
        exitItem.Click += (_, _) => _exitAction();
        menu.Add(exitItem);

        _trayIcon = new TrayIcon
        {
            ToolTipText = "Krypton Clipboard Manager",
            Menu = menu,
            IsVisible = true
        };

        // Load icon from resources
        try
        {
            var iconUri = new Uri("avares://Krypton-Desktop/Assets/avalonia-logo.ico");
            using var iconStream = Avalonia.Platform.AssetLoader.Open(iconUri);
            _trayIcon.Icon = new WindowIcon(iconStream);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load tray icon");
        }

        _trayIcon.Clicked += (_, _) => OnTrayIconClicked();

        // Add to tray icons collection
        TrayIcon.SetIcons(Application.Current, [_trayIcon]);

        Log.Information("Tray icon initialized");
    }

    public void SetUpdateAvailable(string version, Action onClick)
    {
        if (_trayIcon?.Menu == null)
            return;

        if (_updateItem != null)
        {
            _updateItem.Header = $"⬆ Update Available (v{version})";
            return;
        }

        _updateItem = new NativeMenuItem($"⬆ Update Available (v{version})");
        _updateItem.Click += (_, _) => onClick();
        _trayIcon.Menu.Items.Insert(0, _updateItem);
    }

    public void SetTooltip(string text)
    {
        if (_trayIcon != null)
        {
            _trayIcon.ToolTipText = text;
        }
    }

    public void UpdateConnectionStatus()
    {
        if (_connectItem == null)
            return;

        if (_isConnectedFunc())
        {
            _connectItem.Header = "Disconnect from Server";
        }
        else
        {
            _connectItem.Header = "Connect to Server...";
        }
    }

    private void OnConnectItemClicked()
    {
        if (_isConnectedFunc())
        {
            _disconnectAction();
        }
        else
        {
            _showLoginAction();
        }
    }

    public void ShowNotification(string title, string message, Action? onClick = null)
    {
        // Store the action to be invoked when tray icon is clicked after showing notification
        _pendingNotificationAction = onClick;

        Log.Information("Notification: {Title} - {Message}", title, message);

        // Platform-specific notification
        if (OperatingSystem.IsWindows())
        {
            ShowWindowsNotification(title, message);
        }
        else
        {
            // For other platforms, just update the tooltip temporarily
            SetTooltip($"{title}: {message}");
        }
    }

    private void ShowWindowsNotification(string title, string message)
    {
        try
        {
            // Use Windows toast notification via PowerShell (simple approach)
            var script = $@"
                [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
                [Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] | Out-Null

                $template = @""
<toast>
    <visual>
        <binding template=""ToastGeneric"">
            <text>{EscapeXml(title)}</text>
            <text>{EscapeXml(message)}</text>
        </binding>
    </visual>
</toast>
""@
                $xml = New-Object Windows.Data.Xml.Dom.XmlDocument
                $xml.LoadXml($template)
                $toast = New-Object Windows.UI.Notifications.ToastNotification $xml
                [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('Krypton').Show($toast)
            ";

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script.Replace("\"", "\\\"")}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to show Windows notification");
        }
    }

    private static string EscapeXml(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }

    private void OnTrayIconClicked()
    {
        // Check if there's a pending notification action
        if (_pendingNotificationAction != null)
        {
            var action = _pendingNotificationAction;
            _pendingNotificationAction = null;
            action();
        }
        else
        {
            _showPopupAction();
        }
    }

    public void Dispose()
    {
        if (_trayIcon != null)
        {
            _trayIcon.IsVisible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
    }
}
