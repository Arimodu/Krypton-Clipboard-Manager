using System.CommandLine;
using System.CommandLine.Invocation;
using Krypton.Server.Configuration;
using Krypton.Server.Database;
using Krypton.Server.Database.Entities;
using Krypton.Server.Database.Repositories;

namespace Krypton.Server.Cli.Commands;

public static class ApiKeyCommand
{
    public static Command Create()
    {
        var command = new Command("apikey", "API key management commands");

        command.AddCommand(CreateListCommand());
        command.AddCommand(CreateGenerateCommand());
        command.AddCommand(CreateRevokeCommand());

        return command;
    }

    private static Command CreateListCommand()
    {
        var command = new Command("list", "List API keys");

        var userOption = new Option<string?>(
            name: "--user",
            description: "Filter by username");
        userOption.AddAlias("-u");

        var configOption = new Option<string?>(
            name: "--config",
            description: "Path to config file");
        configOption.AddAlias("-c");

        command.AddOption(userOption);
        command.AddOption(configOption);

        command.SetHandler(async (InvocationContext context) =>
        {
            var username = context.ParseResult.GetValueForOption(userOption);
            var configPath = context.ParseResult.GetValueForOption(configOption);

            var config = LoadConfig(configPath);
            await using var dbContext = CreateContext(config);
            var keyRepo = new ApiKeyRepository(dbContext);
            var userRepo = new UserRepository(dbContext);

            IEnumerable<ApiKey> keys;

            if (!string.IsNullOrEmpty(username))
            {
                var user = await userRepo.GetByUsernameAsync(username);
                if (user == null)
                {
                    Console.WriteLine($"Error: User '{username}' not found.");
                    context.ExitCode = 1;
                    return;
                }
                keys = await keyRepo.GetByUserIdAsync(user.Id);
            }
            else
            {
                keys = await keyRepo.GetAllAsync();
            }

            Console.WriteLine($"{"Key",-40} {"User",-15} {"Name",-20} {"Created",-18} {"Revoked",-8}");
            Console.WriteLine(new string('-', 105));

            foreach (var key in keys)
            {
                var displayKey = key.Key.Length > 16
                    ? key.Key[..16] + "..."
                    : key.Key;
                Console.WriteLine($"{displayKey,-40} {key.User?.Username ?? "?",-15} {key.Name,-20} {key.CreatedAt:yyyy-MM-dd HH:mm} {(key.IsRevoked ? "Yes" : "No"),-8}");
            }
        });

        return command;
    }

    private static Command CreateGenerateCommand()
    {
        var command = new Command("generate", "Generate a new API key for a user");

        var usernameArg = new Argument<string>(
            name: "username",
            description: "Username to generate key for");

        var nameOption = new Option<string>(
            name: "--name",
            description: "Name/description for the key",
            getDefaultValue: () => "CLI Generated");
        nameOption.AddAlias("-n");

        var configOption = new Option<string?>(
            name: "--config",
            description: "Path to config file");
        configOption.AddAlias("-c");

        command.AddArgument(usernameArg);
        command.AddOption(nameOption);
        command.AddOption(configOption);

        command.SetHandler(async (InvocationContext context) =>
        {
            var username = context.ParseResult.GetValueForArgument(usernameArg);
            var name = context.ParseResult.GetValueForOption(nameOption)!;
            var configPath = context.ParseResult.GetValueForOption(configOption);

            var config = LoadConfig(configPath);
            await using var dbContext = CreateContext(config);
            var keyRepo = new ApiKeyRepository(dbContext);
            var userRepo = new UserRepository(dbContext);

            var user = await userRepo.GetByUsernameAsync(username);
            if (user == null)
            {
                Console.WriteLine($"Error: User '{username}' not found.");
                context.ExitCode = 1;
                return;
            }

            var created = await keyRepo.CreateAsync(user.Id, name);

            Console.WriteLine($"API key generated for user '{username}':");
            Console.WriteLine();
            Console.WriteLine($"  {created.Key}");
            Console.WriteLine();
            Console.WriteLine("Save this key securely - it cannot be displayed again.");
        });

        return command;
    }

    private static Command CreateRevokeCommand()
    {
        var command = new Command("revoke", "Revoke an API key");

        var keyArg = new Argument<string>(
            name: "key",
            description: "API key to revoke (or prefix)");

        var configOption = new Option<string?>(
            name: "--config",
            description: "Path to config file");
        configOption.AddAlias("-c");

        command.AddArgument(keyArg);
        command.AddOption(configOption);

        command.SetHandler(async (InvocationContext context) =>
        {
            var key = context.ParseResult.GetValueForArgument(keyArg);
            var configPath = context.ParseResult.GetValueForOption(configOption);

            var config = LoadConfig(configPath);
            await using var dbContext = CreateContext(config);
            var keyRepo = new ApiKeyRepository(dbContext);

            var apiKey = await keyRepo.GetByKeyAsync(key);
            if (apiKey == null)
            {
                // Try to find by prefix
                var allKeys = await keyRepo.GetAllAsync();
                apiKey = allKeys.FirstOrDefault(k => k.Key.StartsWith(key));
            }

            if (apiKey == null)
            {
                Console.WriteLine("Error: API key not found.");
                context.ExitCode = 1;
                return;
            }

            if (apiKey.IsRevoked)
            {
                Console.WriteLine("API key is already revoked.");
                return;
            }

            await keyRepo.RevokeAsync(apiKey.Id);
            Console.WriteLine($"API key revoked for user '{apiKey.User?.Username}'.");
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
}
