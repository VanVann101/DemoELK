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
        .Enrich.WithProperty("service", "payment-service")
        .WriteTo.Console(new JsonFormatter())
        .WriteTo.File(
            new MessageTemplateTextFormatter("timestamp=\"{Timestamp:o}\" level={Level} service=payment-service user=\"{UserId}\" traceId=\"{TraceId}\" message=\"{Message:lj}\"{NewLine}", null),
            "/app/logs/payment-service.log", 
            rollingInterval: RollingInterval.Day, 
            retainedFileCountLimit: 7);
});

var app = builder.Build();

// Deterministic test scenarios based on itemId
var testScenarios = new Dictionary<int, string>
{
    { 1, "success" },
    { 2, "out_of_stock" },
    { 3, "insufficient_funds" },
    { 4, "inventory_error" },
    { 5, "payment_error" },
    { 6, "slow_inventory" },
    { 7, "slow_payment" }
};

app.MapGet("/", () => Results.Ok(new { 
    status = "ok", 
    service = "payment-service",
    testScenarios = testScenarios.Select(kv => new { itemId = kv.Key, scenario = kv.Value })
}));

app.MapPost("/payment/charge", async (PaymentRequest request, ILogger<Program> logger, HttpContext httpContext) =>
{
    var traceId = httpContext.Request.Headers["X-Trace-Id"].FirstOrDefault() ?? "unknown";
    
    // Deterministic behavior based on itemId
    switch (request.ItemId)
    {
        case 1: // Success
        case 2: // Success (but will fail in inventory)
        case 4: // Success (but will fail in inventory)
        case 6: // Success (but slow in inventory)
            await Task.Delay(100);
            logger.LogInformation("Payment approved for user {UserId} traceId={TraceId}", request.UserId, traceId);
            return Results.Json(new PaymentResponse("Success", null));
            
        case 3: // Insufficient funds
            await Task.Delay(100);
            logger.LogWarning("Payment declined for user {UserId} - insufficient funds traceId={TraceId}", request.UserId, traceId);
            return Results.Json(new PaymentResponse("InsufficientFunds", "Balance too low"));
            
        case 5: // Payment service error
            await Task.Delay(100);
            logger.LogError("External processor failure for user {UserId} traceId={TraceId}", request.UserId, traceId);
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
            
        case 7: // Slow payment
            logger.LogInformation("Slow payment processing for user {UserId} traceId={TraceId}", request.UserId, traceId);
            await Task.Delay(2000); // 2 second delay
            logger.LogInformation("Payment approved for user {UserId} traceId={TraceId}", request.UserId, traceId);
            return Results.Json(new PaymentResponse("Success", null));
            
        default: // Unknown scenario - success
            await Task.Delay(100);
            logger.LogInformation("Payment approved for user {UserId} traceId={TraceId}", request.UserId, traceId);
            return Results.Json(new PaymentResponse("Success", null));
    }
});

app.Run();

record PaymentRequest(int ItemId, int Quantity, string UserId);
record PaymentResponse(string Status, string? Reason);

