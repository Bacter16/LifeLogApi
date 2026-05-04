using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Authorization;

namespace LifeLog.Api.Filters;

/// <summary>
/// Action filter that enforces API key authentication via the X-Api-Key request header.
/// Valid keys are configured in appsettings.json under "ApiKeys".
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class ApiKeyAuthFilter : Attribute, IActionFilter
{
    private const string ApiKeyHeader = "X-Api-Key";

    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ActionDescriptor.EndpointMetadata.Any(m => m is AllowAnonymousAttribute))
        {
            return;
        }

        var config = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var configuredKeys = config.GetSection("ApiKeys").Get<string[]>() ?? [];
        var envKeys = (config["LIFELOG_API_KEYS"] ?? string.Empty)
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var validKeys = configuredKeys.Concat(envKeys).Where(k => !string.IsNullOrWhiteSpace(k)).ToArray();

        if (!context.HttpContext.Request.Headers.TryGetValue(ApiKeyHeader, out var providedKey)
            || !validKeys.Contains(providedKey.ToString()))
        {
            context.Result = new UnauthorizedObjectResult(new
            {
                error = "Invalid or missing API key.",
                hint = $"Provide a valid key in the '{ApiKeyHeader}' header."
            });
        }
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
