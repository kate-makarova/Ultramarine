using CliWrap;
using CliWrap.Buffered;
using Spectre.Console;

namespace UltramarineCli.Commands
{
    public class AzureCli
    {
        public static string GetAzCommand()
        {
            if (OperatingSystem.IsWindows())
            {
                // Path for 64-bit installation (matches your debug log)
                string path64 = @"C:\Program Files\Microsoft SDKs\Azure\CLI2\python.exe";
                // Path for 32-bit installation
                string path32 = @"C:\Program Files (x86)\Microsoft SDKs\Azure\CLI2\python.exe";

                if (File.Exists(path64)) return $"\"{path64}\"";
                if (File.Exists(path32)) return $"\"{path32}\"";

                return "az.cmd"; // Fallback
            }
            return "az";
        }

        public static async Task<bool> IsLoggedIn()
        {
            var az = GetAzCommand();

            try
            {
                // We use 'account show' because it fails immediately if not logged in
                var result = await Cli.Wrap(az)
                    .WithArguments("-IBm azure.cli account show")
                    .WithValidation(CommandResultValidation.None)
                    .ExecuteBufferedAsync();

                // ExitCode 0 means we found an active subscription
                return result.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        public static async Task Login()
        {
            var az = GetAzCommand();
            await AnsiConsole.Status().StartAsync("Logging into Azure CLI...", async ctx =>
            {
                // -IBm azure.cli is handled inside your GetAzCommand or added here
                await Cli.Wrap(az)
                    .WithArguments("-IBm azure.cli login")
                    .ExecuteAsync();
            });
        }

        public static async Task EnsureResourceGroup(string rgName, string location = "eastus")
        {
            var az = GetAzCommand();

            await AnsiConsole.Status().StartAsync($"Ensuring Resource Group [bold]{rgName}[/] exists...", async ctx =>
            {
                // -IBm azure.cli is handled inside your GetAzCommand or added here
                await Cli.Wrap(az)
                    .WithArguments($"-IBm azure.cli group create --name {rgName} --location {location}")
                    .ExecuteAsync();
            });
        }

        public static async Task InstallQueue(string rgName)
        {
            var az = GetAzCommand();
            var storageName = $"ultrastorage{Guid.NewGuid().ToString()[..8]}"; // Storage names must be alphanumeric
            var queueName = "ultramarine-tasks";

            await AnsiConsole.Status().StartAsync("Provisioning Message Queue...", async ctx =>
            {
                // 1. Create Storage Account
                ctx.Status("Creating Storage Account...");
                await Cli.Wrap(az)
                    .WithArguments($"-IBm azure.cli storage account create --name {storageName} --resource-group {rgName} --location eastus --sku Standard_LRS")
                    .ExecuteAsync();

                // 2. Get Connection String
                ctx.Status("Retrieving Storage Connection String...");
                var connResult = await Cli.Wrap(az)
                    .WithArguments($"-IBm azure.cli storage account show-connection-string --name {storageName} --resource-group {rgName} --query connectionString -o tsv")
                    .ExecuteBufferedAsync();

                var connectionString = connResult.StandardOutput.Trim();

                // 3. Create the Queue itself
                ctx.Status($"Creating queue: {queueName}...");
                await Cli.Wrap(az)
                    .WithArguments($"-IBm azure.cli storage queue create --name {queueName} --connection-string \"{connectionString}\"")
                    .ExecuteAsync();

                // 4. Save to YAML (The Glue)
                var config = new
                {
                    StorageAccount = storageName,
                    QueueName = queueName,
                    ConnectionString = connectionString
                };

                var yaml = new YamlDotNet.Serialization.SerializerBuilder().Build().Serialize(config);
                File.WriteAllText("config/queue.yaml", yaml);
            });

            AnsiConsole.MarkupLine($"[green]✔[/] Queue infrastructure ready. Config saved to [blue]config/queue.yaml[/].");
        }

        public static async Task<bool> ResourceExists(string type, string name, string rg)
        {
            var az = GetAzCommand();

            // Example for CosmosDB: az cosmosdb show --name {name} --resource-group {rg}
            // We use --query to see if we get a result
            var result = await Cli.Wrap(az)
                .WithArguments($"-IBm azure.cli {type} show --name {name} --resource-group {rg}")
                .WithValidation(CommandResultValidation.None)
                .ExecuteAsync();

            return result.ExitCode == 0;
        }
    }
}
