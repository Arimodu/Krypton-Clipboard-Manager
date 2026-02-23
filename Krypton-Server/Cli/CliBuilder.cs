using System.CommandLine;
using Krypton.Server.Cli.Commands;
using Krypton.Server.Configuration;

namespace Krypton.Server.Cli;

public static class CliBuilder
{
    public static RootCommand Build()
    {
        var rootCommand = new RootCommand("Krypton Server - Clipboard sync server");

        // Global options
        var configOption = new Option<string>(
            name: "--config",
            description: "Path to config file",
            getDefaultValue: ConfigurationLoader.GetDefaultConfigPath);
        configOption.AddAlias("-c");

        //var verboseOption = new Option<bool>(
        //    name: "--verbose",
        //    description: "Enable verbose output");
        //verboseOption.AddAlias("-v");

        rootCommand.AddGlobalOption(configOption);
        //rootCommand.AddGlobalOption(verboseOption);

        // Add commands
        rootCommand.AddCommand(SetupCommand.Create());
        rootCommand.AddCommand(StartCommand.Create());
        rootCommand.AddCommand(UserCommand.Create());
        rootCommand.AddCommand(ApiKeyCommand.Create());
        rootCommand.AddCommand(CleanupCommand.Create());

        return rootCommand;
    }
}
