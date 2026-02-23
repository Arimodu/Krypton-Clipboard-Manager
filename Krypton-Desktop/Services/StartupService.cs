using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Serilog;

namespace Krypton_Desktop.Services;

/// <summary>
/// Manages application startup registration for the current platform.
/// </summary>
public class StartupService
{
    private const string AppName = "KryptonClipboard";

    /// <summary>
    /// Gets whether the application is configured to start with the system.
    /// </summary>
    public bool IsStartupEnabled
    {
        get
        {
            if (OperatingSystem.IsWindows())
                return IsWindowsStartupEnabled();
            if (OperatingSystem.IsMacOS())
                return IsMacOSStartupEnabled();
            if (OperatingSystem.IsLinux())
                return IsLinuxStartupEnabled();
            return false;
        }
    }

    /// <summary>
    /// Enables or disables startup registration.
    /// </summary>
    public bool SetStartupEnabled(bool enabled)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return SetWindowsStartup(enabled);
            if (OperatingSystem.IsMacOS())
                return SetMacOSStartup(enabled);
            if (OperatingSystem.IsLinux())
                return SetLinuxStartup(enabled);

            Log.Warning("Startup registration not supported on this platform");
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to set startup: {Enabled}", enabled);
            return false;
        }
    }

    #region Windows

    private static bool IsWindowsStartupEnabled()
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
            return key?.GetValue(AppName) != null;
        }
        catch
        {
            return false;
        }
    }

    private static bool SetWindowsStartup(bool enabled)
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);

            if (key == null)
            {
                Log.Error("Failed to open registry key for startup");
                return false;
            }

            if (enabled)
            {
                var exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath))
                {
                    exePath = Process.GetCurrentProcess().MainModule?.FileName;
                }

                if (string.IsNullOrEmpty(exePath))
                {
                    Log.Error("Could not determine executable path");
                    return false;
                }

                key.SetValue(AppName, $"\"{exePath}\"");
                Log.Information("Startup enabled: {Path}", exePath);
            }
            else
            {
                key.DeleteValue(AppName, false);
                Log.Information("Startup disabled");
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to set Windows startup");
            return false;
        }
    }

    #endregion

    #region macOS

    private static bool IsMacOSStartupEnabled()
    {
        if (!OperatingSystem.IsMacOS()) return false;

        var plistPath = GetMacOSPlistPath();
        return File.Exists(plistPath);
    }

    private static bool SetMacOSStartup(bool enabled)
    {
        if (!OperatingSystem.IsMacOS()) return false;

        var plistPath = GetMacOSPlistPath();
        var launchAgentsDir = Path.GetDirectoryName(plistPath)!;

        try
        {
            if (enabled)
            {
                Directory.CreateDirectory(launchAgentsDir);

                var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath))
                {
                    Log.Error("Could not determine executable path");
                    return false;
                }

                var plistContent = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<!DOCTYPE plist PUBLIC ""-//Apple//DTD PLIST 1.0//EN"" ""http://www.apple.com/DTDs/PropertyList-1.0.dtd"">
<plist version=""1.0"">
<dict>
    <key>Label</key>
    <string>com.krypton.clipboard</string>
    <key>ProgramArguments</key>
    <array>
        <string>{exePath}</string>
    </array>
    <key>RunAtLoad</key>
    <true/>
    <key>KeepAlive</key>
    <false/>
</dict>
</plist>";

                File.WriteAllText(plistPath, plistContent);
                Log.Information("macOS startup enabled: {Path}", plistPath);
            }
            else
            {
                if (File.Exists(plistPath))
                {
                    File.Delete(plistPath);
                    Log.Information("macOS startup disabled");
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to set macOS startup");
            return false;
        }
    }

    private static string GetMacOSPlistPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, "Library", "LaunchAgents", "com.krypton.clipboard.plist");
    }

    #endregion

    #region Linux

    private static bool IsLinuxStartupEnabled()
    {
        if (!OperatingSystem.IsLinux()) return false;

        var desktopFilePath = GetLinuxDesktopFilePath();
        return File.Exists(desktopFilePath);
    }

    private static bool SetLinuxStartup(bool enabled)
    {
        if (!OperatingSystem.IsLinux()) return false;

        var desktopFilePath = GetLinuxDesktopFilePath();
        var autostartDir = Path.GetDirectoryName(desktopFilePath)!;

        try
        {
            if (enabled)
            {
                Directory.CreateDirectory(autostartDir);

                var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath))
                {
                    Log.Error("Could not determine executable path");
                    return false;
                }

                var desktopContent = $@"[Desktop Entry]
Type=Application
Name=Krypton Clipboard
Comment=Cross-platform clipboard manager
Exec={exePath}
Icon=krypton
Terminal=false
Categories=Utility;
StartupNotify=false
X-GNOME-Autostart-enabled=true
";

                File.WriteAllText(desktopFilePath, desktopContent);
                Log.Information("Linux startup enabled: {Path}", desktopFilePath);
            }
            else
            {
                if (File.Exists(desktopFilePath))
                {
                    File.Delete(desktopFilePath);
                    Log.Information("Linux startup disabled");
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to set Linux startup");
            return false;
        }
    }

    private static string GetLinuxDesktopFilePath()
    {
        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(configHome, "autostart", "krypton-clipboard.desktop");
    }

    #endregion
}
