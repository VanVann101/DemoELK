using Serilog;
using Serilog.Formatting.Json;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
public class Program {
    private static void Main(string[] args) {
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
            client.BaseAddress = new Uri(builder.Configuration.GetValue<string>("InventoryService:BaseUrl")
                                         ?? "http://inventory-service");
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        builder.Services.AddHttpClient("payment", client => {
            client.BaseAddress = new Uri(builder.Configuration.GetValue<string>("PaymentService:BaseUrl")
                                         ?? "http://payment-service");
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        builder.Services.AddSingleton<OrderRepository>();

        // Add Swagger
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(c => {
            c.SwaggerDoc("v1", new() { 
                Title = "Order API", 
                Version = "v1",
                Description = "Demo API для управления заказами с интеграцией ELK стека"
            });
        });

        var app = builder.Build();

        // Enable Swagger UI
        app.UseSwagger();
        app.UseSwaggerUI(c => {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "Order API v1");
            c.RoutePrefix = "swagger";
        });

        app.MapGet("/", () => Results.Ok(new { status = "ok", service = "order-api", swagger = "/swagger" }))
            .WithName("HealthCheck")
            .WithTags("Health");

        app.MapPost("/orders", async (OrderRequest request, IHttpClientFactory factory, OrderRepository repository, ILogger<Program> logger, HttpContext httpContext) => {
            var traceId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
            logger.LogInformation("Received order {@Order} traceId={TraceId}", request, traceId);

            var inventoryClient = factory.CreateClient("inventory");
            var paymentClient = factory.CreateClient("payment");

            // Add traceId to request headers
            inventoryClient.DefaultRequestHeaders.Add("X-Trace-Id", traceId);
            paymentClient.DefaultRequestHeaders.Add("X-Trace-Id", traceId);

            InventoryResponse? inventoryResponse;
            try {
                var inventoryHttpResponse = await inventoryClient.PostAsJsonAsync("/inventory/check", request);
                if (!inventoryHttpResponse.IsSuccessStatusCode) {
                    logger.LogWarning("Inventory returned non-success {Status} traceId={TraceId}", inventoryHttpResponse.StatusCode, traceId);
                    return Results.Problem("Inventory check failed", statusCode: (int)HttpStatusCode.BadGateway);
                }

                inventoryResponse = await inventoryHttpResponse.Content.ReadFromJsonAsync<InventoryResponse>();
            }
            catch (Exception ex) {
                logger.LogError(ex, "Inventory check failed traceId={TraceId}", traceId);
                return Results.Problem("Inventory service unavailable", statusCode: (int)HttpStatusCode.BadGateway);
            }

            if (inventoryResponse is null) {
                logger.LogWarning("Inventory response null traceId={TraceId}", traceId);
                return Results.Problem("Inventory check failed", statusCode: (int)HttpStatusCode.BadGateway);
            }

            if (!inventoryResponse.InStock) {
                logger.LogInformation("Inventory out of stock traceId={TraceId}", traceId);
                var orderOut = repository.AddOrder(request, OrderStatus.Rejected, "Out of stock");
                return Results.Json(new OrderResponse(orderOut.Id, orderOut.Status.ToString(), traceId, "Out of stock"));
            }

            PaymentResponse? paymentResponse;
            try {
                var paymentHttpResponse = await paymentClient.PostAsJsonAsync("/payment/charge", request);
                if (!paymentHttpResponse.IsSuccessStatusCode) {
                    logger.LogWarning("Payment returned non-success {Status} traceId={TraceId}", paymentHttpResponse.StatusCode, traceId);
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
                logger.LogInformation("Payment declined traceId={TraceId}", traceId);
                var orderDeclined = repository.AddOrder(request, OrderStatus.Rejected, "Insufficient funds");
                return Results.Json(new OrderResponse(orderDeclined.Id, orderDeclined.Status.ToString(), traceId, "Insufficient funds"));
            }

            if (paymentResponse.Status == "Error") {
                logger.LogError("Payment error traceId={TraceId}", traceId);
                var orderError = repository.AddOrder(request, OrderStatus.Failed, "Payment error");
                return Results.Problem("Payment error", statusCode: (int)HttpStatusCode.BadGateway);
            }

            var saved = repository.AddOrder(request, OrderStatus.Completed, "Ok");
            logger.LogInformation("Order completed {@Order} traceId={TraceId}", saved, traceId);
            return Results.Json(new OrderResponse(saved.Id, saved.Status.ToString(), traceId, "Order processed"));
        })
        .WithName("CreateOrder")
        .WithTags("Orders");

        app.MapGet("/orders/{id:guid}", (Guid id, OrderRepository repository) => {
            var order = repository.Get(id);
            return order is null ? Results.NotFound() : Results.Ok(order);
        })
        .WithName("GetOrder")
        .WithTags("Orders");

        app.Run();
    }
}

record OrderRequest(int ItemId, int Quantity, string UserId);

record InventoryResponse(bool InStock, string? Reason);

record PaymentResponse(string Status, string? Reason);

record OrderResponse(Guid Id, string Status, string TraceId, string Message);

enum OrderStatus
{
    Completed,
    Rejected,
    Failed
}

class OrderRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int ItemId { get; set; }
    public int Quantity { get; set; }
    public string UserId { get; set; } = string.Empty;
    public OrderStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

class OrderRepository
{
    private readonly ConcurrentDictionary<Guid, OrderRecord> _orders = new();

    public OrderRecord AddOrder(OrderRequest request, OrderStatus status, string message)
    {
        var record = new OrderRecord
        {
            ItemId = request.ItemId,
            Quantity = request.Quantity,
            UserId = request.UserId,
            Status = status,
            Message = message
        };
        _orders[record.Id] = record;
        return record;
    }

    public OrderRecord? Get(Guid id) => _orders.TryGetValue(id, out var value) ? value : null;
}