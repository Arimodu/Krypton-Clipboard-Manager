using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Krypton.Shared.Protocol;
using Krypton_Desktop.Services;
using Krypton_Desktop.Services.Platform;
using Krypton_Desktop.ViewModels;
using Krypton_Desktop.Views;
using Microsoft.Extensions.DependencyInjection;
using Projektanker.Icons.Avalonia;
using Projektanker.Icons.Avalonia.MaterialDesign;
using Serilog;

namespace Krypton_Desktop;

public partial class App : Application
{
    private IServiceProvider? _serviceProvider;
    private TrayIconService? _trayIconService;
    private ClipboardMonitorService? _clipboardMonitor;
    private ServerConnectionService? _serverConnection;
    private HotkeyManager? _hotkeyManager;
    private ClipboardPopupWindow? _popupWindow;
    private SettingsWindow? _settingsWindow;
    private StatusWindow? _statusWindow;
    private LoginWindow? _loginWindow;
    private UpdateWindow? _updateWindow;
    private Views.MacOSPermissionsWindow? _macOSPermissionsWindow;

    public static IServiceProvider Services => ((App)Current!)._serviceProvider!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Hide Dock icon immediately — before Avalonia shows anything.
        // LSUIElement in Info.plist covers .app bundle runs; this covers bare-executable runs.
        if (OperatingSystem.IsMacOS())
            MacOSPermissions.HideFromDock();

        IconProvider.Current.Register<MaterialDesignIconProvider>();

        // Configure logging
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Krypton", "logs", "desktop-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();

        Log.Information("Krypton Desktop starting...");

        // Configure services
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Get services
            var settingsService = _serviceProvider.GetRequiredService<SettingsService>();
            _clipboardMonitor = _serviceProvider.GetRequiredService<ClipboardMonitorService>();
            _serverConnection = _serviceProvider.GetRequiredService<ServerConnectionService>();
            var historyService = _serviceProvider.GetRequiredService<ClipboardHistoryService>();

            // Configure history limit from settings
            historyService.MaxItems = settingsService.Settings.MaxHistoryItems;

            // Setup hotkey manager (callback must marshal to UI thread since SharpHook fires from background thread)
            _hotkeyManager = new HotkeyManager(() => Dispatcher.UIThread.Invoke(ShowPopup));
            _hotkeyManager.SetHotkeyFromString(settingsService.Settings.Hotkey);

            // On macOS, CGEventTap (used by SharpHook) is silently disabled if Input
            // Monitoring is not granted. Check BEFORE starting the hook so the tap
            // is created with the permission already active.
            if (OperatingSystem.IsMacOS() && !MacOSPermissions.AllPermissionsGranted())
            {
                Log.Warning("macOS permissions not fully granted — hotkey may be inactive until permissions are approved and Krypton is restarted");

                // Show the permissions window after the tray icon is ready.
                // Pragma suppresses CA1416: the IsMacOS() guard above guarantees
                // this branch is only reached on macOS.
#pragma warning disable CA1416
                _ = Task.Delay(1000).ContinueWith(_ => Dispatcher.UIThread.Post(ShowMacOSPermissions));
#pragma warning restore CA1416
            }

            _hotkeyManager.Start();

            // Setup server connection events
            _serverConnection.StateChanged += OnServerConnectionStateChanged;
            _serverConnection.ClipboardBroadcastReceived += OnClipboardBroadcastReceived;
            _serverConnection.ConnectionLost += OnConnectionLost;
            _serverConnection.ConnectionRestored += OnConnectionRestored;
            _serverConnection.ServerVersionMismatch += OnServerVersionMismatch;

            // Setup tray icon
            _trayIconService = new TrayIconService(
                ShowPopup,
                ShowSettings,
                ShowStatus,
                ShowLogin,
                DisconnectFromServer,
                () => _serverConnection?.IsAuthenticated ?? false,
                () => desktop.Shutdown(),
                OperatingSystem.IsMacOS() ? ShowMacOSPermissions : null);

            _trayIconService.Initialize();

            // Start clipboard monitoring
            _clipboardMonitor.Start();

            // Subscribe to clipboard changes to push to server
            _clipboardMonitor.ClipboardChanged += OnClipboardChanged;

            // Don't show main window - start minimized to tray
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Handle shutdown
            desktop.Exit += OnExit;

            Log.Information("Krypton Desktop started. Hotkey: {Hotkey}", _hotkeyManager.CurrentHotkeyDisplay);

            // Auto-connect if configured and API key is saved
            if (settingsService.Settings.AutoConnect && !string.IsNullOrEmpty(settingsService.Settings.ApiKey))
            {
                _ = AutoConnectAsync();
            }
            else if (string.IsNullOrEmpty(settingsService.Settings.ApiKey))
            {
                // No API key saved, show login dialog after a short delay
                _ = Task.Delay(1000).ContinueWith(_ => Dispatcher.UIThread.Post(ShowLogin));
            }

            // Check for desktop updates in the background
            _ = CheckForUpdatesAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Services
        services.AddSingleton<SettingsService>();
        services.AddSingleton<ClipboardHistoryService>();
        services.AddSingleton<ClipboardMonitorService>();
        services.AddSingleton<ServerConnectionService>();
        services.AddSingleton<StartupService>();

        // ViewModels
        services.AddTransient<MainWindowViewModel>();
    }

    private void ShowPopup()
    {
        if (_popupWindow != null && _popupWindow.IsVisible)
        {
            _popupWindow.Close();
            _popupWindow = null;
            return;
        }

        var historyService = _serviceProvider!.GetRequiredService<ClipboardHistoryService>();
        var clipboardMonitor = _serviceProvider!.GetRequiredService<ClipboardMonitorService>();

        _popupWindow = new ClipboardPopupWindow
        {
            DataContext = new ClipboardPopupViewModel(
                historyService,
                clipboardMonitor,
                _serverConnection,
                () =>
                {
                    _popupWindow?.Close();
                    _popupWindow = null;
                }),
            Topmost = true  // Ensure window appears above other windows when activated via hotkey
        };

        // Clean up reference when window is closed by any means
        _popupWindow.Closed += (_, _) => _popupWindow = null;

        // Position the window near the cursor
        PositionWindowNearCursor(_popupWindow);

        // On macOS in accessory/LSUIElement mode the process is a background app.
        // Without explicitly activating it first, the popup window opens behind
        // whatever was in the foreground when the hotkey fired.
        if (OperatingSystem.IsMacOS())
            MacOSPermissions.ActivateApp();

        _popupWindow.Show();
        _popupWindow.Activate();
    }

    private void PositionWindowNearCursor(Window window)
    {
        const int offset = 10;
        var windowWidth = window.Width;
        var windowHeight = window.Height;

        // Get cursor position
        var (cursorX, cursorY) = GetCursorPosition();

        // Get screen bounds - use primary screen as fallback
        var screens = window.Screens;
        var screen = screens.ScreenFromPoint(new Avalonia.PixelPoint(cursorX, cursorY))
                    ?? screens.Primary;

        if (screen == null)
        {
            // Fallback to center screen if no screen found
            window.WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterScreen;
            return;
        }

        var screenBounds = screen.WorkingArea;

        // Calculate position with offset from cursor
        double x = cursorX + offset;
        double y;

        // Determine if we should anchor to top or bottom of cursor
        var spaceBelow = screenBounds.Bottom - cursorY;
        var spaceAbove = cursorY - screenBounds.Y;

        if (spaceBelow >= windowHeight + offset)
        {
            // Enough space below - anchor top-left corner near cursor
            y = cursorY + offset;
        }
        else if (spaceAbove >= windowHeight + offset)
        {
            // Not enough space below, but enough above - anchor bottom-left corner near cursor
            y = cursorY - windowHeight - offset;
        }
        else
        {
            // Not enough space either way - fit to available space
            y = spaceBelow > spaceAbove
                ? cursorY + offset
                : screenBounds.Bottom - windowHeight - offset;
        }

        // Ensure window doesn't go off the right edge
        if (x + windowWidth > screenBounds.Right)
        {
            x = cursorX - windowWidth - offset;
        }

        // Ensure window doesn't go off the left edge
        if (x < screenBounds.X)
        {
            x = screenBounds.X + offset;
        }

        // Ensure window doesn't go off the top
        if (y < screenBounds.Y)
        {
            y = screenBounds.Y + offset;
        }

        // Ensure window doesn't go off the bottom
        if (y + windowHeight > screenBounds.Bottom)
        {
            y = screenBounds.Bottom - windowHeight - offset;
        }

        window.Position = new Avalonia.PixelPoint((int)x, (int)y);
    }

    private static (int X, int Y) GetCursorPosition()
    {
        if (OperatingSystem.IsWindows())
        {
            if (GetCursorPos(out var point))
            {
                return (point.X, point.Y);
            }
        }

        // Fallback: return center of primary screen
        return (500, 500);
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    private void ShowSettings()
    {
        if (_settingsWindow != null && _settingsWindow.IsVisible)
        {
            _settingsWindow.Activate();
            return;
        }

        var settingsService = _serviceProvider!.GetRequiredService<SettingsService>();
        var startupService = _serviceProvider!.GetRequiredService<StartupService>();

        _settingsWindow = new SettingsWindow
        {
            DataContext = new SettingsViewModel(
                settingsService,
                _hotkeyManager!,
                startupService,
                () =>
                {
                    _settingsWindow?.Close();
                    _settingsWindow = null;
                },
                (title, message) => _trayIconService?.ShowNotification(title, message))
        };

        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void ShowStatus()
    {
        if (_statusWindow != null && _statusWindow.IsVisible)
        {
            _statusWindow.Activate();
            return;
        }

        var settingsService = _serviceProvider!.GetRequiredService<SettingsService>();

        _statusWindow = new StatusWindow
        {
            DataContext = new StatusViewModel(
                _serverConnection!,
                _clipboardMonitor!,
                settingsService,
                () =>
                {
                    _statusWindow?.Close();
                    _statusWindow = null;
                })
        };

        _statusWindow.Show();
        _statusWindow.Activate();
    }

    private void ShowLogin()
    {
        if (_loginWindow != null && _loginWindow.IsVisible)
        {
            _loginWindow.Activate();
            return;
        }

        var settingsService = _serviceProvider!.GetRequiredService<SettingsService>();

        _loginWindow = new LoginWindow
        {
            DataContext = new LoginViewModel(
                _serverConnection!,
                settingsService,
                success =>
                {
                    _loginWindow?.Close();
                    _loginWindow = null;

                    if (success)
                    {
                        _trayIconService?.UpdateConnectionStatus();
                        Log.Information("Successfully connected and authenticated");

                        // Auto-download history if local cache is empty
                        _ = PullInitialHistoryAsync();
                    }
                })
        };

        _loginWindow.Show();
        _loginWindow.Activate();
    }

    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    private void ShowMacOSPermissions()
    {
        if (_macOSPermissionsWindow != null && _macOSPermissionsWindow.IsVisible)
        {
            _macOSPermissionsWindow.Activate();
            return;
        }

        _macOSPermissionsWindow = new Views.MacOSPermissionsWindow
        {
            DataContext = new ViewModels.MacOSPermissionsViewModel(() =>
            {
                _macOSPermissionsWindow?.Close();
                _macOSPermissionsWindow = null;
            })
        };

        _macOSPermissionsWindow.Closed += (_, _) => _macOSPermissionsWindow = null;

        MacOSPermissions.ActivateApp();
        _macOSPermissionsWindow.Show();
        _macOSPermissionsWindow.Activate();
    }

    private void DisconnectFromServer()
    {
        _ = _serverConnection?.DisconnectAsync();
        _trayIconService?.UpdateConnectionStatus();
    }

    private void OnServerConnectionStateChanged(object? sender, ConnectionState state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _trayIconService?.UpdateConnectionStatus();

            var tooltip = state switch
            {
                ConnectionState.Authenticated => "Krypton - Connected",
                ConnectionState.Connected => "Krypton - Connected (not authenticated)",
                ConnectionState.Connecting => "Krypton - Connecting...",
                _ => "Krypton Clipboard Manager"
            };
            _trayIconService?.SetTooltip(tooltip);
        });
    }

    private void OnClipboardBroadcastReceived(object? sender, Krypton.Shared.Protocol.ClipboardEntry entry)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var historyService = _serviceProvider?.GetRequiredService<ClipboardHistoryService>();
            if (historyService != null)
            {
                var item = Models.ClipboardItem.FromProto(entry);
                historyService.AddFromServer(item);

                if (item.TextContent != null)
                    _ = _clipboardMonitor?.SetTextAsync(item.TextContent);
            }
        });
    }

    private async void OnClipboardChanged(object? sender, Models.ClipboardItem item)
    {
        if (_serverConnection?.IsAuthenticated == true && item.TextContent != null)
        {
            var content = System.Text.Encoding.UTF8.GetBytes(item.TextContent);
            await _serverConnection.PushClipboardEntryAsync(
                Krypton.Shared.Protocol.ClipboardContentType.Text,
                content,
                item.Preview);
        }
    }

    private async System.Threading.Tasks.Task AutoConnectAsync()
    {
        const int maxRetries = 3;
        const int retryDelayMs = 5000;

        var settingsService = _serviceProvider!.GetRequiredService<SettingsService>();
        var settings = settingsService.Settings;

        if (string.IsNullOrEmpty(settings.ServerAddress) || string.IsNullOrEmpty(settings.ApiKey))
            return;

        Log.Information("Auto-connecting to server...");

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var connected = await _serverConnection!.ConnectAsync(settings.ServerAddress, settings.ServerPort);
                if (connected)
                {
                    var authenticated = await _serverConnection.AuthenticateWithApiKeyAsync(settings.ApiKey);
                    if (authenticated)
                    {
                        Log.Information("Auto-connect successful");
                        Dispatcher.UIThread.Post(() => _trayIconService?.UpdateConnectionStatus());

                        // Auto-download history if local cache is empty
                        await PullInitialHistoryAsync();
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Auto-connect attempt {Attempt} failed", attempt);
            }

            if (attempt < maxRetries)
            {
                Log.Information("Retrying in {Delay}ms...", retryDelayMs);
                await Task.Delay(retryDelayMs);
            }
        }

        // All attempts failed
        Log.Error("Auto-connect failed after {MaxRetries} attempts", maxRetries);
        Dispatcher.UIThread.Post(() =>
        {
            ShowNotification("Connection Failed",
                "Unable to connect to Krypton server. Click to configure.",
                ShowLogin);
        });
    }

    private void OnConnectionLost(object? sender, string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ShowNotification("Connection Lost",
                message + " Click to reconnect.",
                ShowLogin);
            _trayIconService?.UpdateConnectionStatus();
        });
    }

    private void OnConnectionRestored(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ShowNotification("Connection Restored",
                "Successfully reconnected to Krypton server.");
            _trayIconService?.UpdateConnectionStatus();
        });
    }

    private void OnServerVersionMismatch(object? sender, string message)
    {
        Dispatcher.UIThread.Post(() =>
            _trayIconService?.ShowNotification("Server Out of Date", message, null));
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var updateService = new UpdateService();
            var result = await updateService.GetLatestReleaseAsync();
            if (result == null)
                return;

            if (new Version(result.Value.Version) > new Version(PacketConstants.AppVersion))
            {
                var version = result.Value.Version;
                var downloadUrl = result.Value.DownloadUrl;
                Dispatcher.UIThread.Post(() =>
                    _trayIconService?.SetUpdateAvailable(version,
                        () => ShowUpdateWindow(version, downloadUrl)));
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to check for updates");
        }
    }

    private void ShowUpdateWindow(string version, string downloadUrl)
    {
        if (_updateWindow != null && _updateWindow.IsVisible)
        {
            _updateWindow.Activate();
            return;
        }

        _updateWindow = new UpdateWindow
        {
            DataContext = new UpdateViewModel(
                PacketConstants.AppVersion,
                version,
                downloadUrl,
                () =>
                {
                    _updateWindow?.Close();
                    _updateWindow = null;
                })
        };

        _updateWindow.Closed += (_, _) => _updateWindow = null;
        _updateWindow.Show();
        _updateWindow.Activate();
    }

    private async Task PullInitialHistoryAsync()
    {
        try
        {
            var historyService = _serviceProvider?.GetRequiredService<ClipboardHistoryService>();
            if (historyService == null || _serverConnection == null)
                return;

            // Only pull if local history is empty or very small
            if (historyService.Count >= 10)
            {
                Log.Debug("Local history has {Count} items, skipping initial pull", historyService.Count);
                return;
            }

            Log.Information("Pulling initial history from server...");
            var result = await _serverConnection.PullHistoryAsync(100, 0);
            if (result != null)
            {
                var items = result.Value.Entries.Select(Models.ClipboardItem.FromProto);
                historyService.LoadFromServer(items);
                Log.Information("Loaded {Count} entries from server", result.Value.Entries.Length);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to pull initial history");
        }
    }

    private void ShowNotification(string title, string message, Action? onClick = null)
    {
        // Use the tray icon to show notifications
        _trayIconService?.ShowNotification(title, message, onClick);
    }

    private void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        Log.Information("Krypton Desktop shutting down...");

        // macOS can hang during shutdown if the AppKit run-loop or a background
        // thread doesn't exit cleanly.  Schedule a hard exit after 4 seconds so
        // the process never gets stuck waiting indefinitely.
        if (OperatingSystem.IsMacOS())
            _ = Task.Delay(4000).ContinueWith(_ => Environment.Exit(0));

        _hotkeyManager?.Dispose();
        _clipboardMonitor?.Dispose();
        _serverConnection?.Dispose();
        _trayIconService?.Dispose();
        _popupWindow?.Close();
        _settingsWindow?.Close();
        _statusWindow?.Close();
        _loginWindow?.Close();
        _updateWindow?.Close();
        _macOSPermissionsWindow?.Close();

        // Save settings
        var settingsService = _serviceProvider?.GetService<SettingsService>();
        settingsService?.Save();

        Log.CloseAndFlush();
    }
}
