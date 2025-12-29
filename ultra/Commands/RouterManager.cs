using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using UltramarineCli.Models;
using Yarp.ReverseProxy.Configuration;

namespace UltramarineCli.Commands
{
    internal class RouterManager
    {
        private CancellationTokenSource? _cts;
        string routerPath;
        string projectPath;
        RouterConfig routerConfig;
        private InMemoryConfigProvider _configProvider;
        private WebApplication? app;
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

        public void BuildApp()
        {
            var (routes, clusters) = TranslateRouterToYarp();

            var builder = WebApplication.CreateBuilder();
            _configProvider = new InMemoryConfigProvider(routes, clusters);
            builder.Services.AddSingleton<IProxyConfigProvider>(_configProvider);

            // Register YARP with our translated config
            builder.Services.AddReverseProxy();

            this.app = builder.Build();

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
                        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                        bool authorized = true;
                        string authError = "";

                        if (routeMetadata.TryGetValue("AuthRequired", out var authReq) && authReq == "True")
                        {
                            // Mock check: Look for a header called 'X-Ultramarine-Privileges'
                            var userPrivs = context.Request.Headers["X-Ultramarine-Privileges"].ToString().Split(',');
                            var requiredPrivs = routeMetadata["Privileges"].Split(',');

                            if (!requiredPrivs.All(p => userPrivs.Contains(p)))
                            {
                                authorized = false;
                                authError = $"Missing: {string.Join(", ", requiredPrivs)}";
                            }

                            if (!authorized)
                            {
                                context.Response.StatusCode = 403;
                                LogRequest(context, stopwatch.ElapsedMilliseconds, "FORBIDDEN", "red", authError);
                                await context.Response.WriteAsync($"Ultramarine Security: {authError}");
                                return;
                            }
                        }

                        var result = await HandleLocalRequestAsync(context);
                        if (result != null)
                        {
                            stopwatch.Stop();
                            context.Response.StatusCode = 200;

                            // Log it through our beautiful logger
                            LogRequest(context, stopwatch.ElapsedMilliseconds, "OK (LOCAL)", "cyan");

                            await context.Response.WriteAsJsonAsync(result);
                            return;
                        }

                        await next(); // Proceed to the Function
                        stopwatch.Stop();

                        var color = context.Response.StatusCode >= 400 ? "yellow" : "green";
                        LogRequest(context, stopwatch.ElapsedMilliseconds, "OK", color);
                    });
                });
            });
        }

        public async Task<object?> HandleLocalRequestAsync(HttpContext context)
        {
            // Extract the class name from the URL path (e.g., /api/ObjectList -> ObjectList)
            var path = context.Request.Path.Value?.Trim('/');
            if (string.IsNullOrEmpty(path)) return null;

            // Convention: Take the last segment of the path as the Class Name
            var className = path.Split('/').Last();

            // 1. Scan the project for the class inheriting from EndpointFunction
            var endpointType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .FirstOrDefault(t =>
                    string.Equals(t.Name, className, StringComparison.OrdinalIgnoreCase) &&
                    typeof(EndpointFunction).IsAssignableFrom(t) &&
                    !t.IsAbstract);

            if (endpointType == null) return null;

            // 2. Instantiate and Inject
            var instance = (EndpointFunction)Activator.CreateInstance(endpointType)!;
            instance.Request = context.Request;
            // We could also inject Logger, Database, etc. here

            return await instance.HandleAsync();
        }

        public void SetUpRouter()
        {
            BuildApp();
            SetupRouterWatcher();
        }

        public async Task StartLocalRouterAsync()
        {
            _cts = new CancellationTokenSource();
            await app.StartAsync(_cts.Token);
        }

        public async Task StopAsync()
        {
            if (_cts != null)
            {
                await _cts.CancelAsync();
                await app.StopAsync();
            }
        }

        private void LogRequest(HttpContext context, long ms, string status, string color, string detail = "")
        {
            var method = context.Request.Method.PadRight(6);
            var path = context.Request.Path;
            var statusCode = context.Response.StatusCode;

            AnsiConsole.MarkupLine(
                $"[{color}]LOG[/] {DateTime.Now:HH:mm:ss} | " +
                $"[white]{method}[/] [bold]{path}[/] | " +
                $"[{color}]{statusCode} {status}[/] [grey]{ms}ms[/] " +
                $"[italic red]{detail}[/]");
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
