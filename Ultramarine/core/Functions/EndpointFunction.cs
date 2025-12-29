using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Ultramarine.core.Functions;

public abstract class EndpointFunction
{
    private readonly ILogger _logger;

    public EndpointFunction(ILogger logger)
    {
        _logger = logger;
    }

    [Function("EndpointFunction")]
    public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req)
    {
        _logger.LogInformation("C# HTTP trigger function processed a request.");
        var result = await HandleAsync();

        // 3. Wrap the result in an IActionResult if it isn't one
        return result is IActionResult res ? res : new OkObjectResult(result);
    }

    public abstract Task<object> HandleAsync();
}