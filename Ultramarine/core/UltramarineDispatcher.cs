using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ultramarine.core.Functions;

namespace Ultramarine.core
{
#if DEBUG
    public class UltramarineDispatcher
    {
        private readonly IServiceProvider _serviceProvider;

        // The host injects the ServiceProvider here
        public UltramarineDispatcher(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        [Function("UltramarineMain")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", "put", "delete", Route = "{*route}")] HttpRequestData req,
            string route)
        {
            var className = route.Split('/').Last().Replace("-", "");

            // 2. Reflectively find the class
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .FirstOrDefault(t =>
                    string.Equals(t.Name, className, StringComparison.OrdinalIgnoreCase) &&
                    typeof(EndpointFunction).IsAssignableFrom(t) &&
                    !t.IsAbstract);

            if (type == null)
            {
                var errorResponse = req.CreateResponse(System.Net.HttpStatusCode.NotFound);
                await errorResponse.WriteStringAsync($"Ultramarine: Endpoint class '{className}' not found.");
                return new ObjectResult(errorResponse);
            }

            // 3. Instantiate and Execute
            var handler = (EndpointFunction)ActivatorUtilities.CreateInstance(_serviceProvider, type);
            // Pass the real Azure Function context into your base class
            var result = await handler.HandleAsync();
            return result is IActionResult res ? res : new OkObjectResult(result);
        }
    }
#endif
}
