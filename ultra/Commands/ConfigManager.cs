using Spectre.Console;
using UltramarineCli.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace UltramarineCli.Commands
{
    internal class ConfigManager
    {

        string path;
        string dbPath;
        string queuePath;
        IDeserializer deserializer;
        public ConfigManager(string path)
        {
            this.path = path;
            // --- 1. CONFIG CHECKER LOGIC ---
            deserializer = new DeserializerBuilder()
     .WithNamingConvention(CamelCaseNamingConvention.Instance)
     .Build();
            dbPath = Path.Combine(path, "config", "database.yaml");
            queuePath = Path.Combine(path, "config", "queue.yaml");
        }
        public async Task<CliState> CheckConfigAsync(CliState state)
        {
            // Status variables
            var dbConfig = File.Exists(dbPath) ? deserializer.Deserialize<DatabaseConfig>(File.ReadAllText(dbPath)) : null;
            var queueConfig = File.Exists(queuePath) ? deserializer.Deserialize<QueueConfig>(File.ReadAllText(queuePath)) : null;

            if (state == CliState.SHOW_TABLE_AND_MENU) {
            // --- 2. DISPLAY CURRENT STATUS ---
            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("Component");
            table.AddColumn("Local Configuration");
            table.AddColumn("Deployment");

            // Database Row
            if (dbConfig != null)
                table.AddRow("Database", "[green]Configured[/]", $"Name: {dbConfig.ProjectSettings.DatabaseName}");
            else
                table.AddRow("Database", "[red]Not Configured[/]", "-");

            // Queue Row
            if (queueConfig != null)
                table.AddRow("Queue", "[green]Configured[/]", $"Name: {queueConfig.ProjectSettings.QueueName}");
            else
                table.AddRow("Queue", "[red]Not Configured[/]", "-");

            AnsiConsole.Write(table);
            }

            if (state == CliState.SHOW_TABLE_AND_MENU || state == CliState.SHOW_MENU_ONLY)
            {

                // --- 3. THE INTERACTIVE MENU ---
                var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("\n[yellow]What would you like to do?[/]")
                    .PageSize(10)
                    .AddChoices(new[] {
            "Start local environment",
            "Configure Router",
            "Configure Database",
            "Configure Queue",
            "Refresh Status",
            "Exit"
                    }));

                // --- 4. ACTION HANDLER ---
                switch (choice)
                {
                    case "Start local environment":
                        var localEnvManager = new LocalManager(path);
                        await localEnvManager.StartLocalEnvironment();
                        return CliState.SHOW_MENU_ONLY;
                    case "Configure Database":
                        var dbManager = new DatabaseManager(path);
                        await dbManager.ConfigureDatabase();
                        return CliState.SHOW_TABLE_AND_MENU;
                    case "Configure Queue":
                        var queueManager = new QueueManager(path);
                        await queueManager.ConfigureQueue();
                        return CliState.SHOW_TABLE_AND_MENU;
                    case "Exit":
                        return CliState.EXIT;
                }
            }
            return CliState.SHOW_NOTHING;
        }
    }
}
