using Spectre.Console;
using UltramarineCli.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace UltramarineCli.Commands
{
    internal class QueueManager
    {
        string path;
        string queueConfigPath;
        public QueueManager(string projectPath)
        {
            path = projectPath;
            queueConfigPath = System.IO.Path.Combine(path, "config", "queue.yaml");
        }
        public async Task ConfigureQueue()
        {
            AnsiConsole.MarkupLine("[bold blue]Setting up Queue Configuration[/]");

            var queueName = AnsiConsole.Ask<string>("Enter the [green]Queue Name[/] (e.g., email-tasks):");
            var maxRetries = AnsiConsole.Ask<int>("How many [green]retry attempts[/] before moving to error queue? (default 5):", 5);

            var config = new QueueConfig
            {
                ProjectSettings = new QueueSettings
                {
                    QueueName = queueName,
                    MaxDeliveryAttempts = maxRetries
                },
                Local = new ConnectionInfo
                {
                    ConnectionString = "UseDevelopmentStorage=true", // Points to Azurite local emulator
                    ContainerName = queueName
                },
                Production = new ConnectionInfo
                {
                    ResourceGroup = "ultramarine-resources",
                    ContainerName = queueName
                }
            };

            var serializer = new SerializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build();
            File.WriteAllText(queueConfigPath, serializer.Serialize(config));

            AnsiConsole.MarkupLine("[green]✔[/] Queue configuration saved to [underline]config/queue.yaml[/].");
        }
    }
}
