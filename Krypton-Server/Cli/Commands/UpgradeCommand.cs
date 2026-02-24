using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using Krypton.Server.Services;
using Krypton.Shared.Protocol;

namespace Krypton.Server.Cli.Commands;

public static class UpgradeCommand
{
    public static Command Create()
    {
        var command = new Command("upgrade", "Check for updates and upgrade the server binary");

        command.SetHandler(async (InvocationContext context) =>
        {
            await RunUpgradeAsync(context.GetCancellationToken());
        });

        return command;
    }

    private static async Task RunUpgradeAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("Checking for updates...");

        var updateService = new UpdateService();
        var result = await updateService.GetLatestReleaseAsync();

        if (result == null)
        {
            Console.WriteLine("Error: Could not fetch release information from GitHub.");
            Environment.Exit(1);
            return;
        }

        var latestVersion = result.Value.Version;
        var currentVersion = PacketConstants.AppVersion;

        if (!System.Version.TryParse(latestVersion, out var latest) ||
            !System.Version.TryParse(currentVersion, out var current))
        {
            Console.WriteLine($"Error: Could not parse version strings (current={currentVersion}, latest={latestVersion}).");
            Environment.Exit(1);
            return;
        }

        if (latest <= current)
        {
            Console.WriteLine($"Already up to date (v{currentVersion}).");
            return;
        }

        Console.WriteLine($"  Current version : v{currentVersion}");
        Console.WriteLine($"  Latest version  : v{latestVersion}");
        Console.Write("Proceed with upgrade? [Y/n]: ");
        var answer = Console.ReadLine();
        if (answer?.Equals("n", StringComparison.OrdinalIgnoreCase) == true)
        {
            Console.WriteLine("Upgrade cancelled.");
            return;
        }

        var tmpPath = Path.Combine(Path.GetTempPath(), "krypton-server-update");

        Console.WriteLine($"Downloading v{latestVersion}...");
        var progress = new Progress<double>(p =>
        {
            var percent = (int)(p * 100);
            var filled = (int)(p * 40);
            var bar = new string('#', filled) + new string('-', 40 - filled);
            Console.Write($"\r  [{bar}] {percent,3}%");
        });

        try
        {
            await updateService.DownloadAsync(result.Value.DownloadUrl, tmpPath, progress);
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"Error: Download failed: {ex.Message}");
            Environment.Exit(1);
            return;
        }

        Console.WriteLine();
        Console.WriteLine("Download complete. Applying update...");

        var currentExe = Process.GetCurrentProcess().MainModule?.FileName
            ?? Environment.GetCommandLineArgs()[0];

        try
        {
            // On Linux, copying over a running binary is safe — the OS keeps the
            // old inode open for the current process while the new file takes effect
            // on the next execution.
            File.Copy(tmpPath, currentExe, overwrite: true);
            File.Delete(tmpPath);

            // Make the new binary executable
            if (!OperatingSystem.IsWindows())
            {
                Process.Start("chmod", $"+x \"{currentExe}\"")?.WaitForExit();
            }

            Console.WriteLine("Upgrade complete.");

            // If running under systemd, restart the service
            if (Environment.GetEnvironmentVariable("INVOCATION_ID") != null)
            {
                Console.WriteLine("Restarting systemd service...");
                Process.Start("systemctl", "restart krypton-server")?.WaitForExit();
            }
            else
            {
                Console.WriteLine("Restart the server to use the new version.");
            }

            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: Failed to replace binary: {ex.Message}");
            Console.WriteLine($"You can manually replace '{currentExe}' with '{tmpPath}'.");
            Environment.Exit(1);
        }
    }
}
