using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace MediatR.AspNetCore.Endpoints
{
    public class MediatorEndpointOptions
    {
        public JsonSerializerOptions JsonSerializerOptions { get; set; }

        public Func<HttpContext, Exception, Task> OnDeserializeError { get; set; } = (_, _) =>
        {
            return Task.CompletedTask;
        };
    }
}
