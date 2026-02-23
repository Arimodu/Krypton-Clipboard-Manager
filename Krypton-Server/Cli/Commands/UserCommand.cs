using System.CommandLine;
using System.CommandLine.Invocation;
using Krypton.Server.Configuration;
using Krypton.Server.Database;
using Krypton.Server.Database.Entities;
using Krypton.Server.Database.Repositories;

namespace Krypton.Server.Cli.Commands;

public static class UserCommand
{
    public static Command Create()
    {
        var command = new Command("user", "User management commands");

        command.AddCommand(CreateListCommand());
        command.AddCommand(CreateAddCommand());
        command.AddCommand(CreateDeleteCommand());
        command.AddCommand(CreateSetAdminCommand());

        return command;
    }

    private static Command CreateListCommand()
    {
        var command = new Command("list", "List all users");

        var configOption = new Option<string?>(
            name: "--config",
            description: "Path to config file");
        configOption.AddAlias("-c");

        command.AddOption(configOption);

        command.SetHandler(async (InvocationContext context) =>
        {
            var configPath = context.ParseResult.GetValueForOption(configOption);
            var config = LoadConfig(configPath);
            await using var dbContext = CreateContext(config);
            var repo = new UserRepository(dbContext);

            var users = await repo.GetAllAsync();

            Console.WriteLine($"{"ID",-36} {"Username",-20} {"Admin",-6} {"Active",-6} {"Created",-20}");
            Console.WriteLine(new string('-', 90));

            foreach (var user in users)
            {
                Console.WriteLine($"{user.Id} {user.Username,-20} {(user.IsAdmin ? "Yes" : "No"),-6} {(user.IsActive ? "Yes" : "No"),-6} {user.CreatedAt:yyyy-MM-dd HH:mm}");
            }
        });

        return command;
    }

    private static Command CreateAddCommand()
    {
        var command = new Command("add", "Add a new user");

        var usernameArg = new Argument<string>(
            name: "username",
            description: "Username for the new user");

        var adminOption = new Option<bool>(
            name: "--admin",
            description: "Make user an admin");
        adminOption.AddAlias("-a");

        var configOption = new Option<string?>(
            name: "--config",
            description: "Path to config file");
        configOption.AddAlias("-c");

        command.AddArgument(usernameArg);
        command.AddOption(adminOption);
        command.AddOption(configOption);

        command.SetHandler(async (InvocationContext context) =>
        {
            var username = context.ParseResult.GetValueForArgument(usernameArg);
            var isAdmin = context.ParseResult.GetValueForOption(adminOption);
            var configPath = context.ParseResult.GetValueForOption(configOption);

            var config = LoadConfig(configPath);
            await using var dbContext = CreateContext(config);
            var repo = new UserRepository(dbContext);

            if (await repo.ExistsAsync(username))
            {
                Console.WriteLine($"Error: User '{username}' already exists.");
                context.ExitCode = 1;
                return;
            }

            Console.Write("Password: ");
            var password = ReadPassword();
            Console.WriteLine();

            if (password.Length < 8)
            {
                Console.WriteLine("Error: Password must be at least 8 characters.");
                context.ExitCode = 1;
                return;
            }

            Console.Write("Confirm password: ");
            var confirm = ReadPassword();
            Console.WriteLine();

            if (password != confirm)
            {
                Console.WriteLine("Error: Passwords do not match.");
                context.ExitCode = 1;
                return;
            }

            var user = new User
            {
                Username = username,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                IsAdmin = isAdmin
            };

            await repo.CreateAsync(user);
            Console.WriteLine($"User '{username}' created successfully.");
        });

        return command;
    }

    private static Command CreateDeleteCommand()
    {
        var command = new Command("delete", "Delete a user");

        var usernameArg = new Argument<string>(
            name: "username",
            description: "Username to delete");

        var configOption = new Option<string?>(
            name: "--config",
            description: "Path to config file");
        configOption.AddAlias("-c");

        var forceOption = new Option<bool>(
            name: "--force",
            description: "Skip confirmation");
        forceOption.AddAlias("-f");

        command.AddArgument(usernameArg);
        command.AddOption(configOption);
        command.AddOption(forceOption);

        command.SetHandler(async (InvocationContext context) =>
        {
            var username = context.ParseResult.GetValueForArgument(usernameArg);
            var configPath = context.ParseResult.GetValueForOption(configOption);
            var force = context.ParseResult.GetValueForOption(forceOption);

            var config = LoadConfig(configPath);
            await using var dbContext = CreateContext(config);
            var repo = new UserRepository(dbContext);

            var user = await repo.GetByUsernameAsync(username);
            if (user == null)
            {
                Console.WriteLine($"Error: User '{username}' not found.");
                context.ExitCode = 1;
                return;
            }

            if (!force)
            {
                Console.Write($"Are you sure you want to delete user '{username}'? This will also delete all their clipboard entries. [y/N]: ");
                var confirm = Console.ReadLine();
                if (!confirm?.Equals("y", StringComparison.OrdinalIgnoreCase) == true)
                {
                    Console.WriteLine("Cancelled.");
                    return;
                }
            }

            await repo.DeleteAsync(user.Id);
            Console.WriteLine($"User '{username}' deleted.");
        });

        return command;
    }

    private static Command CreateSetAdminCommand()
    {
        var command = new Command("set-admin", "Grant or revoke admin privileges");

        var usernameArg = new Argument<string>(
            name: "username",
            description: "Username to modify");

        var revokeOption = new Option<bool>(
            name: "--revoke",
            description: "Revoke admin privileges instead of granting");
        revokeOption.AddAlias("-r");

        var configOption = new Option<string?>(
            name: "--config",
            description: "Path to config file");
        configOption.AddAlias("-c");

        command.AddArgument(usernameArg);
        command.AddOption(revokeOption);
        command.AddOption(configOption);

        command.SetHandler(async (InvocationContext context) =>
        {
            var username = context.ParseResult.GetValueForArgument(usernameArg);
            var revoke = context.ParseResult.GetValueForOption(revokeOption);
            var configPath = context.ParseResult.GetValueForOption(configOption);

            var config = LoadConfig(configPath);
            await using var dbContext = CreateContext(config);
            var repo = new UserRepository(dbContext);

            var user = await repo.GetByUsernameAsync(username);
            if (user == null)
            {
                Console.WriteLine($"Error: User '{username}' not found.");
                context.ExitCode = 1;
                return;
            }

            user.IsAdmin = !revoke;
            await repo.UpdateAsync(user);

            Console.WriteLine(revoke
                ? $"Admin privileges revoked from '{username}'."
                : $"Admin privileges granted to '{username}'.");
        });

        return command;
    }

    private static ServerConfiguration LoadConfig(string? configPath)
    {
        configPath ??= ConfigurationLoader.GetDefaultConfigPath();
        if (!File.Exists(configPath))
        {
            Console.WriteLine($"Config file not found: {configPath}");
            Console.WriteLine("Run 'krypton-server setup' first.");
            Environment.Exit(1);
        }
        return ConfigurationLoader.Load(configPath);
    }

    private static KryptonDbContext CreateContext(ServerConfiguration config)
    {
        var options = DatabaseFactory.CreateOptions(config.Database);
        return new KryptonDbContext(options);
    }

    private static string ReadPassword()
    {
        var password = "";
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
                break;
            if (key.Key == ConsoleKey.Backspace && password.Length > 0)
            {
                password = password[..^1];
                Console.Write("\b \b");
            }
            else if (!char.IsControl(key.KeyChar))
            {
                password += key.KeyChar;
                Console.Write("*");
            }
        }
        return password;
    }
}
