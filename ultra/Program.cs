using CliWrap;
using CliWrap.Buffered;
using CliWrap.EventStream;
using Spectre.Console;
using UltramarineCli.Commands;

AnsiConsole.Write(new FigletText("Ultramarine").Color(Color.Blue));
AnsiConsole.MarkupLine("[bold blue]Ultramarine CLI v1.0.0[/] - Let's deploy your backend.");

// 1. Get the target directory from arguments or default to "."
string targetDir = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();

// 2. Convert to an absolute path so there is no ambiguity
string absoluteTargetDir = Path.GetFullPath(targetDir);

// Determine the correct command name based on the OS

string cmd = AzureCli.GetAzCommand();
var cliArgs = cmd.Contains("python.exe")
    ? "-IBm azure.cli --version" // Direct Python call
    : "--version";               // Standard call

var azCheck = await Cli.Wrap(cmd)
    .WithArguments(cliArgs)
    .WithValidation(CommandResultValidation.None)
    .ExecuteBufferedAsync();

if (azCheck.ExitCode == 0 || azCheck.StandardOutput.Contains("azure-cli"))
{
    AnsiConsole.MarkupLine("[green]✔[/] Azure CLI is ready.");
} else
{
    AnsiConsole.Write(new Rule("[red]FATAL ERROR[/]").RuleStyle("grey"));
    AnsiConsole.MarkupLine("\n[bold red]FATAL:[/] Azure CLI is not installed or not accessible.");
    AnsiConsole.MarkupLine("[blue]Hint:[/] Please install Azure CLI from [underline]https://learn.microsoft.com/en-us/cli/azure/install-azure-cli[/] and ensure it's in your system PATH.\n");
    return 1;
}

if(await AzureCli.IsLoggedIn())
{
    AnsiConsole.MarkupLine("[green]✔[/] Azure CLI is logged in.");
} else
{
    AnsiConsole.MarkupLine("[yellow]⚠[/] Azure CLI is not logged in. Logging in is reqired for deployment, but not reuired for local development.");
}

// --- 2. FOLDER CHECK: ULTRAMARINE PROJECT ---
// Construct the path: ./config/ultramarine.yaml


// 3. Construct the config path relative to that target
string configPath = Path.Combine(absoluteTargetDir, "config", "ultramarine.yaml");

if (!File.Exists(configPath))
{
    AnsiConsole.Write(new Rule("[red]Wrong Directory[/]").RuleStyle("grey"));
    AnsiConsole.MarkupLine("\n[bold red]FATAL:[/] No Ultramarine project found in this folder.");
    AnsiConsole.MarkupLine("[grey]Expected file at:[/] [underline]{0}[/]", configPath);
    AnsiConsole.MarkupLine("[blue]Hint:[/] Are you in the root of your El Boletin project?\n");
    return 1;
}

AnsiConsole.MarkupLine("[green]✔[/] Ultramarine project detected.");

var c = true;
// --- 3. MAIN MENU LOOP ---
while (c) {
    ConfigManager configManager = new ConfigManager(absoluteTargetDir);
    c = await configManager.CheckConfigAsync();
}
return 0;