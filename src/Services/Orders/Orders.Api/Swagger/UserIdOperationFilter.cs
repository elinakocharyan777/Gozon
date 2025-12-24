using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Orders.Api.Swagger;

//Добавляет параметры <c>X-User-Id</c> и <c>user_id</c> в пользовательский интерфейс Swagger
public sealed class UserIdOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var path = context.ApiDescription.RelativePath ?? string.Empty;
        if (path.StartsWith("health", StringComparison.OrdinalIgnoreCase))
            return;

        operation.Parameters ??= new List<OpenApiParameter>();

        //  X-User-Id
        if (!operation.Parameters.Any(p => p.In == ParameterLocation.Header && p.Name == UserContext.HeaderName))
        {
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = UserContext.HeaderName,
                In = ParameterLocation.Header,
                Required = false,
                Description = "User identifier (alternative to user_id query parameter).",
                Schema = new OpenApiSchema { Type = "string" }
            });
        }

        // user_id
        if (!operation.Parameters.Any(p => p.In == ParameterLocation.Query && p.Name == "user_id"))
        {
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "user_id",
                In = ParameterLocation.Query,
                Required = false,
                Description = "User identifier (alternative to X-User-Id header).",
                Schema = new OpenApiSchema { Type = "string" }
            });
        }
    }
}
