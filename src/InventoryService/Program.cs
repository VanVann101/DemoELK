using Serilog;
using Serilog.Formatting.Display;
using Serilog.Formatting.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => {
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .Enrich.WithThreadId()
        .Enrich.WithProperty("service", "inventory-service")
        .WriteTo.Console(new JsonFormatter())
        .WriteTo.File(
            new MessageTemplateTextFormatter("inventory | {Timestamp:o} | level={Level} | item={ItemId} | qty={Quantity} | user={UserId} | traceId={TraceId} | msg={Message:lj}{NewLine}", null),
            "/app/logs/inventory-service.log", 
            rollingInterval: RollingInterval.Day, 
            retainedFileCountLimit: 7);
});

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new { 
    status = "ok", 
    service = "inventory-service"
}));

app.MapPost("/inventory/check", async (InventoryRequest request, ILogger<Program> logger, HttpContext httpContext) => {
    var traceId = httpContext.Request.Headers["X-Trace-Id"].FirstOrDefault() ?? "unknown";
    
    switch (request.ItemId)
    {
        case 1: // Success
        case 3: // Success (will fail in payment)
        case 5: // Success (will fail in payment)
        case 7: // Success (slow payment)
            await Task.Delay(50);
            logger.LogInformation("Item available itemId={ItemId} qty={Quantity} user={UserId} traceId={TraceId}", 
                request.ItemId, request.Quantity, request.UserId, traceId);
            return Results.Json(new InventoryResponse(true, null));
            
        case 2: // Out of stock
            await Task.Delay(50);
            logger.LogWarning("Out of stock itemId={ItemId} user={UserId} traceId={TraceId}", 
                request.ItemId, request.UserId, traceId);
            return Results.Json(new InventoryResponse(false, "Out of stock"));
            
        case 4: // Inventory error
            await Task.Delay(50);
            logger.LogError("Internal error itemId={ItemId} user={UserId} traceId={TraceId}", 
                request.ItemId, request.UserId, traceId);
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
            
        case 6: // Slow inventory
            logger.LogInformation("Slow processing started itemId={ItemId} user={UserId} traceId={TraceId}", 
                request.ItemId, request.UserId, traceId);
            await Task.Delay(2000);
            logger.LogInformation("Item available after delay itemId={ItemId} qty={Quantity} user={UserId} traceId={TraceId}", 
                request.ItemId, request.Quantity, request.UserId, traceId);
            return Results.Json(new InventoryResponse(true, null));
            
        default:
            await Task.Delay(50);
            logger.LogWarning("Item not found itemId={ItemId} user={UserId} traceId={TraceId}", 
                request.ItemId, request.UserId, traceId);
            return Results.Json(new InventoryResponse(false, "Item not found"));
    }
});

app.Run();

record InventoryRequest(int ItemId, int Quantity, string UserId);
record InventoryResponse(bool InStock, string? Reason);