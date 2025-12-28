using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using System;
using System.Collections.Generic;
using System.Text;

namespace Ultramarine.core.Mddleware
{
    public class AuthMiddleware : IFunctionsWorkerMiddleware
    {
        public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
        {
            var httpRequestData = await context.GetHttpRequestDataAsync();

            // Example: Check for a Bearer token or custom header
            if (!httpRequestData.Headers.TryGetValues("Authorization", out var values))
            {
                var response = httpRequestData.CreateResponse();
                response.StatusCode = System.Net.HttpStatusCode.Unauthorized;
                context.GetInvocationResult().Value = response;
                return; // Short-circuit: The function never runs
            }

            // Add user info to context for use in the Function
            context.Items["User"] = "AuthenticatedUser123";

            await next(context);
        }
    }
}
