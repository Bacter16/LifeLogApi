using Microsoft.EntityFrameworkCore;
using LifeLog.Infrastructure.Data;
using LifeLog.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Database
builder.Services.AddDbContext<LifeLogDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Background worker (IConfiguration injected automatically via DI)
builder.Services.AddHostedService<GeminiWorker>();

// MVC controllers
builder.Services.AddControllers();

// OpenAPI (dev only)
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Request timing middleware
app.Use(async (context, next) =>
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    await next();
    sw.Stop();
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("{Method} {Path} → {Status} ({ElapsedMs}ms)",
        context.Request.Method,
        context.Request.Path,
        context.Response.StatusCode,
        sw.ElapsedMilliseconds);
});

app.UseHttpsRedirection();
app.MapControllers();
app.Run();
