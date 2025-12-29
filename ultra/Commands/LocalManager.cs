using Azure.Storage.Queues;
using CliWrap;
using CliWrap.Buffered;
using Microsoft.Azure.Cosmos;
using Spectre.Console;
using System.Diagnostics;
using UltramarineCli.Models;

namespace UltramarineCli.Commands
{
    internal class LocalManager
    {
        string projectPath;
        DatabaseConfig dbConfig;
        QueueConfig queueConfig;
        LocalState localState;

        public LocalManager(string projectPath)
        {
            this.projectPath = projectPath;
            var deserializer = new YamlDotNet.Serialization.DeserializerBuilder()
                .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.CamelCaseNamingConvention.Instance)
                .Build();
            var dbConfigPath = Path.Combine(projectPath, "config", "database.yaml");
            var queueConfigPath = Path.Combine(projectPath, "config", "queue.yaml");
            dbConfig = deserializer.Deserialize<DatabaseConfig>(File.ReadAllText(dbConfigPath));
            queueConfig = deserializer.Deserialize<QueueConfig>(File.ReadAllText(queueConfigPath));
            localState = deserializer.Deserialize<LocalState>(File.ReadAllText(Path.Combine(projectPath, "config", "local-state.yaml"))) ?? new LocalState();
        }

        public async Task StartLocalEnvironment()
        {
            if (!await IsDockerRunningAsync())
            {
                AnsiConsole.MarkupLine("[red]Error:[/] Docker is not running!");
                AnsiConsole.MarkupLine("[yellow]Please start Docker Desktop and try again.[/]");
                return;
            }
            bool running = true;
            while (running)
            {
                var routerManager = new RouterManager(projectPath);

                await AnsiConsole.Status().StartAsync("Starting local emulators...", async ctx =>
                {
                    EnsureDockerNetworkExists("ultramarine-net");

                    // 1. Start Azurite (Queues)
                    ctx.Status("Launching Azurite (Queue Emulator)...");
                    await EnsureContainerRunning("ultra-azurite", "mcr.microsoft.com/azure-storage/azurite", "-p 10000:10000 -p 10001:10001 -p 10002:10002");

                    if (!localState.QueueProvisioned)
                    {
                        ctx.Status("[yellow]Provisioning Queue...[/]");
                        await InitializeQueueResources(); // Create Queues, etc.
                        localState.QueueProvisioned = true;
                    }

                    // 2. Start CosmosDB Emulator
                    ctx.Status("Launching CosmosDB Emulator...");
                    await EnsureContainerRunning("cosmos-db", "mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator", "-p 8081:8081 -p 10250-10255:10250-10255");

                    if (!localState.DatabaseProvisioned)
                    {
                        ctx.Status("[yellow]Provisioning Database...[/]");
                        await InitializeDatabaseResources(); // Create DB, Containers, etc.

                        localState.DatabaseProvisioned = true;
                    }

                    ctx.Status("Starting Functions container...");
                    StartFunctionsContainer();

                    routerManager.SetUpRouter();
                    ctx.Status("Starting local API router...");
                });

                await routerManager.StartLocalRouterAsync();

                AnsiConsole.MarkupLine("[green]✔[/] Local environment is [bold]UP[/].");
                AnsiConsole.MarkupLine("[grey]Press [white]Q[/] to stop environment and return to menu...[/]");
                AnsiConsole.MarkupLine("[bold blue]Ultramarine Gateway ready on http://localhost:5000[/]");
                AnsiConsole.MarkupLine("[grey]Azurite: http://localhost:10001[/]");
                AnsiConsole.MarkupLine("[grey]CosmosDB: https://localhost:8081/_explorer/index.html[/]");

                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Q)
                {
                    await AnsiConsole.Status().StartAsync("Shutting down...", async ctx =>
                    {
                        await routerManager.StopAsync();
                        // (Optional) Stop containers if you want to be clean
                    });

                    running = false; // Breaks the loop and returns to the Main Menu
                }
            }
        }

        public async Task<bool> IsDockerRunningAsync()
        {
            try
            {
                // Run 'docker info' to verify the daemon is responsive
                var result = await Cli.Wrap("docker")
                    .WithArguments("info")
                    .WithValidation(CommandResultValidation.None) // Don't throw if Docker isn't running
                    .ExecuteAsync();

                return result.ExitCode == 0;
            }
            catch
            {
                // This catches cases where 'docker' isn't even in the system PATH
                return false;
            }
        }

        private async Task EnsureContainerRunning(string name, string image, string args)
        {
            // Check if container exists (running or stopped)
            var check = await Cli.Wrap("docker")
                .WithArguments($"ps -a --filter name={name} --format \"{{{{.Names}}}}\"")
                .ExecuteBufferedAsync();

            if (check.StandardOutput.Contains(name))
            {
                // It exists, just start it
                await Cli.Wrap("docker").WithArguments($"start {name}").ExecuteAsync();
            }
            else
            {
                // It doesn't exist, run it for the first time
                await Cli.Wrap("docker").WithArguments($"run -d --name {name} {args} {image}").ExecuteAsync();
            }
        }

        public async Task InitializeDatabaseResources()
        {
            // 1. Create a handler that ignores SSL errors
            HttpClientHandler antisslHandler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (
                    HttpRequestMessage _,
                    System.Security.Cryptography.X509Certificates.X509Certificate2? _,
                    System.Security.Cryptography.X509Certificates.X509Chain? _,
                    System.Net.Security.SslPolicyErrors _) => true
            };

            // 2. Configure Cosmos to use this handler
            CosmosClientOptions options = new CosmosClientOptions
            {
                HttpClientFactory = () => new HttpClient(antisslHandler),
                ConnectionMode = ConnectionMode.Gateway, // Gateway mode is required for this bypass
                LimitToEndpoint = true
            };

            // 3. Initialize the client
            var cosmosClient = new CosmosClient(dbConfig.Local.ConnectionString, options);

            // Now this won't throw the SSL exception
            await cosmosClient.CreateDatabaseIfNotExistsAsync(dbConfig.ProjectSettings.DatabaseName);

            AnsiConsole.MarkupLine("[green]✔[/] Local Cosmos database initialized.");
        }

        public async Task InitializeQueueResources()
        {
            // Create local Queue
            var queueClient = new QueueClient(queueConfig.Local.ConnectionString, queueConfig.ProjectSettings.QueueName);
            await queueClient.CreateIfNotExistsAsync();

            AnsiConsole.MarkupLine("[green]✔[/] Local containers and queues initialized.");
        }

        public static void EnsureDockerNetworkExists(string networkName)
        {
            var checkInfo = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = $"network inspect {networkName}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            using var process = Process.Start(checkInfo);
            process?.WaitForExit();

            // If exit code is not 0, the network doesn't exist
            if (process?.ExitCode != 0)
            {
                AnsiConsole.MarkupLine($"[grey]Creating docker network: {networkName}...[/]");
                Process.Start("docker", $"network create {networkName}")?.WaitForExit();
            }
        }

        public void StartFunctionsContainer()
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "run -d " +
            "--name ultramarine-api " +
            "--network ultramarine-net " +
            "-p 7071:7071 " +
            $"-v \"{projectPath}:/src\" " + // Mount to /src
            "-w /src " +
            "-e AZURE_FUNCTIONS_ENVIRONMENT=Development " +
            "mcr.microsoft.com/dotnet/sdk:10.0 " +
            "/bin/bash -c \"" +
            // 1. Install Tools (only if not using a custom image)
            "apt-get update && apt-get install -y wget unzip && " +
            "wget https://github.com/Azure/azure-functions-core-tools/releases/download/4.0.6610/Azure.Functions.Cli.linux-x64.4.0.6610.zip -O /tmp/func.zip && " +
            "unzip -o /tmp/func.zip -d /usr/bin/func-cli && " +
            "chmod +x /usr/bin/func-cli/func && ln -sf /usr/bin/func-cli/func /usr/bin/func && " +

            // 2. THE CRITICAL STEP: Build to generate functions.metadata
            "dotnet publish -c Debug -o /home/site/wwwroot && " +

            // 3. Start from the output directory
            "cd /home/site/wwwroot && func start --dotnet-isolated --host 0.0.0.0 --port 7071\"",
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            Process.Start(startInfo);
        }
    }
}
