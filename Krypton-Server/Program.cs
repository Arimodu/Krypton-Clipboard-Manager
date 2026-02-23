using System.CommandLine;
using Krypton.Server.Cli;

var rootCommand = CliBuilder.Build();
return await rootCommand.InvokeAsync(args);
