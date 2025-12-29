using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

if (builder.Environment.IsDevelopment())
{
    // You can add local-only services here if needed
    // The Dispatcher is automatically found by the host because it has the [Function] attribute
    Console.WriteLine(">>> Ultramarine: Development Mode - Dispatcher Active");
}

builder.Build().Run();
