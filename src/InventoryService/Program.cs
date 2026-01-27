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

// Deterministic test scenarios based on itemId
var testScenarios = new Dictionary<int, string>
{
    { 1, "success" },           // Всё OK
    { 2, "out_of_stock" },      // Нет на складе
    { 3, "insufficient_funds" }, // Недостаточно средств
    { 4, "inventory_error" },   // Ошибка в inventory service
    { 5, "payment_error" },     // Ошибка в payment service
    { 6, "slow_inventory" },    // Медленный inventory (задержка)
    { 7, "slow_payment" }       // Медленный payment (задержка)
};

app.MapGet("/", () => Results.Ok(new { 
    status = "ok", 
    service = "inventory-service",
    testScenarios = testScenarios.Select(kv => new { itemId = kv.Key, scenario = kv.Value })
}));

app.MapPost("/inventory/check", async (InventoryRequest request, ILogger<Program> logger, HttpContext httpContext) => {
    var traceId = httpContext.Request.Headers["X-Trace-Id"].FirstOrDefault() ?? "unknown";
    
    // Deterministic behavior based on itemId
    switch (request.ItemId)
    {
        case 1: // Success scenario
        case 3: // Success in inventory, will fail in payment
        case 5: // Success in inventory, will fail in payment
        case 7: // Success with delay
            await Task.Delay(50);
            logger.LogInformation("Item available {ItemId} qty {Quantity} traceId={TraceId}", request.ItemId, request.Quantity, traceId);
            return Results.Json(new InventoryResponse(true, null));
            
        case 2: // Out of stock
            await Task.Delay(50);
            logger.LogWarning("Out of stock for item {ItemId} traceId={TraceId}", request.ItemId, traceId);
            return Results.Json(new InventoryResponse(false, "Out of stock"));
            
        case 4: // Inventory service error
            await Task.Delay(50);
            logger.LogError("Inventory internal error for item {ItemId} traceId={TraceId}", request.ItemId, traceId);
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
            
        case 6: // Slow inventory
            logger.LogInformation("Slow processing for item {ItemId} traceId={TraceId}", request.ItemId, traceId);
            await Task.Delay(2000); // 2 second delay
            logger.LogInformation("Item available {ItemId} qty {Quantity} traceId={TraceId}", request.ItemId, request.Quantity, traceId);
            return Results.Json(new InventoryResponse(true, null));
            
        default: // Unknown item
            await Task.Delay(50);
            logger.LogWarning("Item not found {ItemId} traceId={TraceId}", request.ItemId, traceId);
            return Results.Json(new InventoryResponse(false, "Item not found"));
    }
});

app.Run();

record InventoryRequest(int ItemId, int Quantity, string UserId);

record InventoryResponse(bool InStock, string? Reason);

