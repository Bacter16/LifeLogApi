using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

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
        var config = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var validKeys = config.GetSection("ApiKeys").Get<string[]>() ?? [];

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
