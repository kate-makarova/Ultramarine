using Spectre.Console;
using UltramarineCli.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace UltramarineCli.Commands
{
    internal class ConfigManager
    {

        string path;
        public ConfigManager(string path)
        {
            this.path = path;
        }
        public async Task<bool> CheckConfigAsync()
        {
            // --- 1. CONFIG CHECKER LOGIC ---
            var deserializer = new DeserializerBuilder()
     .WithNamingConvention(CamelCaseNamingConvention.Instance)
     .Build();
            string dbPath = Path.Combine(path, "config", "database.yaml");
            string queuePath = Path.Combine(path, "config", "queue.yaml");

            // Status variables
            var dbConfig = File.Exists(dbPath) ? deserializer.Deserialize<DatabaseConfig>(File.ReadAllText(dbPath)) : null;
            var queueConfig = File.Exists(queuePath) ? deserializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(queuePath)) : null;

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
                table.AddRow("Queue", "[green]Configured[/]", $"Name: {queueConfig.GetValueOrDefault("Name", "N/A")}");
            else
                table.AddRow("Queue", "[red]Not Configured[/]", "-");

            AnsiConsole.Write(table);

            // --- 3. THE INTERACTIVE MENU ---
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("\n[yellow]What would you like to do?[/]")
                    .PageSize(10)
                    .AddChoices(new[] {
            "Configure Database",
            "Configure Queue",
            "Refresh Status",
            "Exit"
                    }));

            // --- 4. ACTION HANDLER ---
            switch (choice)
            {
                case "Configure Database":
                    var dbManager = new DatabaseManager(path);
                    await dbManager.ConfigureDatabase();
                    return true;
                case "Configure Queue":
                    AnsiConsole.MarkupLine("[blue]Queue onfiguration logic coming soon...[/]");
                    return true;
                case "Exit":
                    return false;
            }
            return false;
        }
    }
}
