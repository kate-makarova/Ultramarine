using CliWrap;
using CliWrap.Buffered;
using Spectre.Console;
using UltramarineCli.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace UltramarineCli.Commands
{
    internal class DatabaseManager
    {
        string path;
        string dbConfigPath => Path.Combine(path, "config", "database.yaml");
        public DatabaseManager(string path)
        {
            this.path = path;
        }
        public async Task ConfigureDatabase()
        {
            AnsiConsole.MarkupLine("[bold blue]Setting up Database Configuration[/]");

            var accountName = AnsiConsole.Ask<string>("Enter the [green]Account Name[/] (e.g., my-app-db):");
            var partitionKey = AnsiConsole.Ask<string>("Enter the [green]Partition Key[/] (e.g., /userId or /tenantId):");

            // Ensure the partition key starts with /
            if (!partitionKey.StartsWith("/")) partitionKey = "/" + partitionKey;

            var config = new
            {
                projectSettings = new
                {
                    accountName,
                    partitionKey,
                    databaseName = "UltramarineDB"
                },
                local = new
                {
                    connectionString = "AccountEndpoint=https://localhost:8081;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==",
                    containerName = "items-local"
                },
                production = new
                {
                    connectionString = "",
                    containerName = "items-prod",
                    resourceGroup = $"{accountName}-rg"
                }
            };

            var yaml = new SerializerBuilder().Build().Serialize(config);
            File.WriteAllText(dbConfigPath, yaml);

            AnsiConsole.MarkupLine("[green]✔[/] Local configuration saved to [underline]config/database.yaml[/].");
        }

        public async Task DeployDatabase()
        {
            var deserializer = new DeserializerBuilder()
    .WithNamingConvention(CamelCaseNamingConvention.Instance)
    .IgnoreUnmatchedProperties()
    .Build();
            // 1. Load the existing YAML
            var yamlContent = File.ReadAllText(dbConfigPath);
            var config = deserializer.Deserialize<DatabaseConfig>(yamlContent);

            string rg = config.Production.ResourceGroup;
            string name = config.ProjectSettings.AccountName;
            string pk = config.ProjectSettings.PartitionKey;
            string db = config.ProjectSettings.DatabaseName;
            string cn = config.Production.ContainerName;

            var az = AzureCli.GetAzCommand();

            await AnsiConsole.Status().StartAsync("Deploying Infrastructure...", async ctx =>
            {
                // 2. Ensure Resource Group exists
                ctx.Status($"Checking Resource Group: [bold]{rg}[/]...");
                await Cli.Wrap(az)
                    .WithArguments($"-IBm azure.cli group create --name {rg} --location eastus")
                    .ExecuteAsync();

                // 3. Create CosmosDB Account
                ctx.Status($"Provisioning CosmosDB: [bold]{name}[/]...");
                await Cli.Wrap(az)
                    .WithArguments($"-IBm azure.cli cosmosdb create --name {name} --resource-group {rg} --capabilities EnableServerless")
                    .ExecuteAsync();

                // 4. Create the SQL Database & Container (using the Partition Key from the YAML)
                ctx.Status($"Creating container with PK: [yellow]{pk}[/]...");
                await Cli.Wrap(az)
                    .WithArguments($"-IBm azure.cli cosmosdb sql database create --account-name {name} --resource-group {rg} --name {db}")
                    .ExecuteAsync();

                await Cli.Wrap(az)
                    .WithArguments($"-IBm azure.cli cosmosdb sql container create --account-name {name} --resource-group {rg} --database-name {db} --name {cn} --partition-key-path {pk}")
                    .ExecuteAsync();

                // 5. Fetch the Connection String
                ctx.Status("Fetching production connection string...");
                var result = await Cli.Wrap(az)
                    .WithArguments($"-IBm azure.cli cosmosdb keys list --name {name} --resource-group {rg} --type connection-strings --query \"connectionStrings[0].connectionString\" -o tsv")
                    .ExecuteBufferedAsync();

                // 6. UPDATE THE YAML (The "Sync" step)
                config.Production.ConnectionString = result.StandardOutput.Trim();
                var updatedYaml = new SerializerBuilder().Build().Serialize(config);
                File.WriteAllText("config/database.yaml", updatedYaml);
            });

            AnsiConsole.MarkupLine("[green]✔[/] Production deployment complete and YAML updated.");
        }

        public static async Task<bool> IsDeployed(string accountName, string resourceGroup)
        {
            var az = AzureCli.GetAzCommand();
            var check = await Cli.Wrap(az)
                .WithArguments($"-IBm azure.cli cosmosdb show --name {accountName} --resource-group {resourceGroup}")
                .WithValidation(CommandResultValidation.None)
                .ExecuteAsync();

            return check.ExitCode == 0;
        }
    }
}
