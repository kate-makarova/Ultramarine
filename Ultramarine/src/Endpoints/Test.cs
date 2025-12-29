using Microsoft.Extensions.Logging;
using Ultramarine.core.Functions;

namespace Ultramarine.src.Endpoints
{
    public class Test : EndpointFunction
    {
        public Test(ILogger<Test> logger) : base(logger)
        {
        }

        public override async Task<object> HandleAsync()
        {
            var data = new { User = "Admin", Status = "Active" };
            return (object)data;
        }
    }
}
