using Serilog;
using Serilog.Formatting.Json;
using System.Diagnostics;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => {
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .Enrich.WithThreadId()
        .Enrich.WithProperty("service", "order-api")
        .WriteTo.Console(new JsonFormatter(renderMessage: true))
        .WriteTo.File(new JsonFormatter(renderMessage: true), "/app/logs/order-api.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7);
});

builder.Services.AddHttpClient("inventory", client => {
    client.BaseAddress = new Uri(builder.Configuration.GetValue<string>("InventoryService:BaseUrl") ?? "http://inventory-service");
    client.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddHttpClient("payment", client => {
    client.BaseAddress = new Uri(builder.Configuration.GetValue<string>("PaymentService:BaseUrl") ?? "http://payment-service");
    client.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c => {
    c.SwaggerDoc("v1", new() { 
        Title = "Order API", 
        Version = "v1",
        Description = "Demo API для тестирования ELK стека"
    });
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c => {
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Order API v1");
    c.RoutePrefix = "swagger";
});

app.MapGet("/", () => Results.Ok(new { 
    status = "ok", 
    service = "order-api", 
    swagger = "/swagger",
    testScenarios = new[] {
        new { itemId = 1, scenario = "success" },
        new { itemId = 2, scenario = "out_of_stock" },
        new { itemId = 3, scenario = "insufficient_funds" },
        new { itemId = 4, scenario = "inventory_error" },
        new { itemId = 5, scenario = "payment_error" },
        new { itemId = 6, scenario = "slow_inventory" },
        new { itemId = 7, scenario = "slow_payment" }
    }
}))
.WithName("HealthCheck")
.WithTags("Health");

app.MapPost("/orders", async (OrderRequest request, IHttpClientFactory factory, ILogger<Program> logger) => {
    var traceId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
    var orderId = Guid.NewGuid();
    
    logger.LogInformation("Received order {OrderId} itemId={ItemId} qty={Quantity} user={UserId} traceId={TraceId}", 
        orderId, request.ItemId, request.Quantity, request.UserId, traceId);

    var inventoryClient = factory.CreateClient("inventory");
    inventoryClient.DefaultRequestHeaders.Add("X-Trace-Id", traceId);
    

    // Check inventory
    InventoryResponse? inventoryResponse;
    try {
        var inventoryHttpResponse = await inventoryClient.PostAsJsonAsync("/inventory/check", request);
        if (!inventoryHttpResponse.IsSuccessStatusCode) {
            logger.LogWarning("Inventory returned {Status} traceId={TraceId}", inventoryHttpResponse.StatusCode, traceId);
            return Results.Problem("Inventory check failed", statusCode: (int)HttpStatusCode.BadGateway);
        }
        inventoryResponse = await inventoryHttpResponse.Content.ReadFromJsonAsync<InventoryResponse>();
    }
    catch (Exception ex) {
        logger.LogError(ex, "Inventory check failed traceId={TraceId}", traceId);
        return Results.Problem("Inventory service unavailable", statusCode: (int)HttpStatusCode.BadGateway);
    }

    if (inventoryResponse is null || !inventoryResponse.InStock) {
        logger.LogInformation("Order {OrderId} rejected - out of stock traceId={TraceId}", orderId, traceId);
        return Results.Json(new OrderResponse(orderId, "Rejected", traceId, "Out of stock"));
    }

    var paymentClient = factory.CreateClient("payment");
    paymentClient.DefaultRequestHeaders.Add("X-Trace-Id", traceId);

    // Process payment
    PaymentResponse? paymentResponse;
    try {
        var paymentHttpResponse = await paymentClient.PostAsJsonAsync("/payment/charge", request);
        if (!paymentHttpResponse.IsSuccessStatusCode) {
            logger.LogWarning("Payment returned {Status} traceId={TraceId}", paymentHttpResponse.StatusCode, traceId);
            return Results.Problem("Payment failed", statusCode: (int)HttpStatusCode.BadGateway);
        }
        paymentResponse = await paymentHttpResponse.Content.ReadFromJsonAsync<PaymentResponse>();
    }
    catch (Exception ex) {
        logger.LogError(ex, "Payment processing failed traceId={TraceId}", traceId);
        return Results.Problem("Payment service unavailable", statusCode: (int)HttpStatusCode.BadGateway);
    }

    if (paymentResponse is null) {
        logger.LogWarning("Payment response null traceId={TraceId}", traceId);
        return Results.Problem("Payment failed", statusCode: (int)HttpStatusCode.BadGateway);
    }

    if (paymentResponse.Status == "InsufficientFunds") {
        logger.LogInformation("Order {OrderId} rejected - insufficient funds traceId={TraceId}", orderId, traceId);
        return Results.Json(new OrderResponse(orderId, "Rejected", traceId, "Insufficient funds"));
    }

    logger.LogInformation("Order {OrderId} completed successfully traceId={TraceId}", orderId, traceId);
    return Results.Json(new OrderResponse(orderId, "Completed", traceId, "Order processed"));
})
.WithName("CreateOrder")
.WithTags("Orders");

app.Run();

record OrderRequest(int ItemId, int Quantity, string UserId);
record InventoryResponse(bool InStock, string? Reason);
record PaymentResponse(string Status, string? Reason);
record OrderResponse(Guid Id, string Status, string TraceId, string Message);