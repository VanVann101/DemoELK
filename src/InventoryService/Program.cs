using Serilog;
using Serilog.Formatting.Display;
using Serilog.Formatting.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => {
    var logstashHost = context.Configuration["Logstash:Host"] ?? "logstash";
    var logstashPort = context.Configuration.GetValue<int?>("Logstash:Port") ?? 5000;
    var logstashUrl = $"http://{logstashHost}:{logstashPort}";

    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .Enrich.WithThreadId()
        .WriteTo.Console(new JsonFormatter())
        .WriteTo.File(new JsonFormatter(), "/app/logs/inventory-service.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
        .WriteTo.Http(logstashUrl, queueLimitBytes: null,
        textFormatter: new MessageTemplateTextFormatter(
                "inventory | {Timestamp:o} | level={Level} | item={ItemId} | qty={Quantity} | user={UserId} | traceId={TraceId} | msg={Message:lj}", null));
});

var app = builder.Build();

var random = Random.Shared;
var stock = new Dictionary<int, int>
{
    { 1, 100 },
    { 2, 3 },
    { 3, 0 }
};

app.MapGet("/", () => Results.Ok(new { status = "ok", service = "inventory-service" }));

app.MapPost("/inventory/check", async (InventoryRequest request, ILogger<Program> logger, HttpContext httpContext) => {
    // Extract traceId from headers
    var traceId = httpContext.Request.Headers["X-Trace-Id"].FirstOrDefault() ?? "unknown";
    
    // Simulate variable latency.
    var delayMs = random.Next(50, 400);
    await Task.Delay(delayMs);

    // Simulate occasional server error.
    if (random.NextDouble() < 0.1) {
        logger.LogError("Inventory internal error for item {ItemId} traceId={TraceId}", request.ItemId, traceId);
        return Results.StatusCode(StatusCodes.Status500InternalServerError);
    }

    if (!stock.TryGetValue(request.ItemId, out var available)) {
        logger.LogInformation("Item not found {ItemId} traceId={TraceId}", request.ItemId, traceId);
        return Results.Json(new InventoryResponse(false, "Item not found"));
    }

    if (available <= 0 || available < request.Quantity) {
        logger.LogInformation("Out of stock for item {ItemId} traceId={TraceId}", request.ItemId, traceId);
        return Results.Json(new InventoryResponse(false, "Out of stock"));
    }

    // Occasionally behave slow to show latency.
    if (random.NextDouble() < 0.1) {
        await Task.Delay(1000);
    }

    logger.LogInformation("Item available {ItemId} qty {Quantity} traceId={TraceId}", request.ItemId, request.Quantity, traceId);
    return Results.Json(new InventoryResponse(true, null));
});

app.Run();

record InventoryRequest(int ItemId, int Quantity, string UserId);

record InventoryResponse(bool InStock, string? Reason);

