using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using System.IO;
using UltramarineCli.Models;
using Yarp.ReverseProxy.Configuration;

namespace UltramarineCli.Commands
{
    internal class RouterManager
    {
        string routerPath;
        string projectPath;
        RouterConfig routerConfig;
        private InMemoryConfigProvider _configProvider;
        public RouterManager(string path)
        {
            projectPath = path;
            var deserializer = new YamlDotNet.Serialization.DeserializerBuilder()
                .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.CamelCaseNamingConvention.Instance)
                .Build();
            routerPath = Path.Combine(path, "config", "router.yaml");
            routerConfig = deserializer.Deserialize<RouterConfig>(File.ReadAllText(routerPath)) ?? new RouterConfig { Routes = new List<RouteEntry>() };
        }

            public (IReadOnlyList<RouteConfig>, IReadOnlyList<ClusterConfig>) TranslateRouterToYarp()
        {
            var routes = new List<RouteConfig>();
            var clusters = new List<ClusterConfig>();

            foreach (var entry in routerConfig.Routes)
            {
                string routeId = $"{entry.Service}-{entry.Path.Replace("/", "-").Trim('-')}-route";
                string clusterKey = $"{entry.Service}-cluster";
                // 1. Define the Route
                routes.Add(new RouteConfig
                {
                    RouteId = routeId,
                    ClusterId = clusterKey,
                    Match = new RouteMatch { Path = entry.Path + "/{**catch-all}" },
                    Metadata = new Dictionary<string, string>
            {
                { "Privileges", string.Join(",", entry.Auth.Privileges) },
                { "AuthRequired", entry.Auth.Required.ToString() }
            }
                });

                // 2. Define the Cluster (The destination, e.g., local Azure Function)
                if (!clusters.Any(x => x.ClusterId == clusterKey))
                {
                    clusters.Add(new ClusterConfig
                    {
                        ClusterId = $"{entry.Service}-cluster",
                        Destinations = new Dictionary<string, DestinationConfig>
            {
                { "local", new DestinationConfig { Address = "http://localhost:7071" } } // Default Function port
            }
                    });
                }
            }

            return (routes, clusters);
        }

        public async Task StartLocalRouterAsync()
        {
            var (routes, clusters) = TranslateRouterToYarp();

            var builder = WebApplication.CreateBuilder();
            _configProvider = new InMemoryConfigProvider(routes, clusters);
            builder.Services.AddSingleton<IProxyConfigProvider>(_configProvider);

            // Register YARP with our translated config
            builder.Services.AddReverseProxy();

            var app = builder.Build();

            // The "Beautiful" Part: Local Privilege Check Middleware
            app.UseRouting();
            _ = app.UseEndpoints(endpoints =>
            {
                endpoints.MapReverseProxy(proxyPipeline =>
                {
                    proxyPipeline.Use(async (context, next) =>
                    {
                        var proxyFeature = context.GetReverseProxyFeature();
                        var routeMetadata = proxyFeature.Route.Config.Metadata;

                        if (routeMetadata.TryGetValue("AuthRequired", out var authReq) && authReq == "True")
                        {
                            // Mock check: Look for a header called 'X-Ultramarine-Privileges'
                            var userPrivs = context.Request.Headers["X-Ultramarine-Privileges"].ToString().Split(',');
                            var requiredPrivs = routeMetadata["Privileges"].Split(',');

                            if (!requiredPrivs.All(p => userPrivs.Contains(p)))
                            {
                                context.Response.StatusCode = 403;
                                await context.Response.WriteAsync($"Ultramarine Security: Missing {string.Join(", ", requiredPrivs)}");
                                return; // Block the request
                            }
                        }
                        await next(); // Proceed to the Function
                    });
                });
            });

            await app.StartAsync();
            SetupRouterWatcher();
        }

        private void SetupRouterWatcher()
        {
            var configDirectory = Path.Combine(projectPath, "config");
            var watcher = new FileSystemWatcher(configDirectory, "router.yaml");

            // Watch for more types of changes
            watcher.NotifyFilter = NotifyFilters.LastWrite
                                 | NotifyFilters.FileName
                                 | NotifyFilters.Size;

            // Handle both Changed and Created (common for IDE "Safe Saves")
            watcher.Changed += OnRouterFileChanged;
            watcher.Created += OnRouterFileChanged;
            watcher.Changed += (s, e) =>
            {
                try
                {
                    // Give the OS a millisecond to release the file lock
                    Thread.Sleep(100);

                    // Reload the YAML
                    var deserializer = new YamlDotNet.Serialization.DeserializerBuilder()
                .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.CamelCaseNamingConvention.Instance)
                .Build();
                   
                    this.routerConfig = deserializer.Deserialize<RouterConfig>(File.ReadAllText(routerPath)) ?? new RouterConfig { Routes = new List<RouteEntry>() };
                    var (newRoutes, newClusters) = TranslateRouterToYarp();

                    // Update YARP instantly!
                    _configProvider.Update(newRoutes, newClusters);

                    AnsiConsole.MarkupLine("[bold cyan]↻[/] Router configuration hot-reloaded.");
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Error reloading router.yaml:[/] {ex.Message}");
                }
            };

            watcher.EnableRaisingEvents = true;
        }

        private async void OnRouterFileChanged(object sender, FileSystemEventArgs e)
        {
            try
            {
                // IMPORTANT: Wait a heartbeat for the editor to release the file lock
                await Task.Delay(200);

                // Reload and Update
                var deserializer = new YamlDotNet.Serialization.DeserializerBuilder()
              .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.CamelCaseNamingConvention.Instance)
              .Build();

                this.routerConfig = deserializer.Deserialize<RouterConfig>(File.ReadAllText(routerPath)) ?? new RouterConfig { Routes = new List<RouteEntry>() };

                var (newRoutes, newClusters) = TranslateRouterToYarp();

                _configProvider.Update(newRoutes, newClusters);

                AnsiConsole.MarkupLine("[bold cyan]↻[/] Router configuration hot-reloaded!");
            }
            catch (IOException)
            {
                // If the file is still locked, we'll catch it on the next event
            }
        }
    }
    }
