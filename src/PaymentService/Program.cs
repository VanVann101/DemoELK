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
        .Enrich.WithProperty("service", "payment-service")
        .WriteTo.Console(new JsonFormatter())
        .WriteTo.File(
            new MessageTemplateTextFormatter("timestamp=\"{Timestamp:o}\" level={Level} service=payment-service user=\"{UserId}\" message=\"{Message:lj}\"{NewLine}", null),
            "/app/logs/payment-service.log", 
            rollingInterval: RollingInterval.Day, 
            retainedFileCountLimit: 7)
        .WriteTo.Http(logstashUrl, queueLimitBytes: null, 
            textFormatter: new MessageTemplateTextFormatter("timestamp=\"{Timestamp:o}\" level={Level} service=payment-service user=\"{UserId}\" message=\"{Message:lj}\"", null));
});

var app = builder.Build();
var random = Random.Shared;

app.MapGet("/", () => Results.Ok(new { status = "ok", service = "payment-service" }));

app.MapPost("/payment/charge", async (PaymentRequest request, ILogger<Program> logger) =>
{
    await Task.Delay(random.Next(100, 500));

    var roll = random.NextDouble();
    if (roll < 0.15)
    {
        logger.LogInformation("Payment declined for user {UserId}", request.UserId);
        return Results.Json(new PaymentResponse("InsufficientFunds", "Balance too low"));
    }

    if (roll < 0.25)
    {
        logger.LogError("External processor failure for user {UserId}", request.UserId);
        return Results.StatusCode(StatusCodes.Status500InternalServerError);
    }

    logger.LogInformation("Payment approved for user {UserId}", request.UserId);
    return Results.Json(new PaymentResponse("Success", null));
});

app.Run();

record PaymentRequest(int ItemId, int Quantity, string UserId);
record PaymentResponse(string Status, string? Reason);

