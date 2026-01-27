# Примеры форматов логов

## Архитектура
Все сервисы пишут логи **только в файлы**, Filebeat читает файлы и отправляет в Logstash.

**Поток данных:**
```
Сервисы → Файлы (/app/logs/*.log) → Filebeat → Logstash (5044) → Elasticsearch → Kibana
```

**Обработка форматов в Filebeat:**
- **order-api**: Filebeat парсит JSON (`json.keys_under_root: true`)
- **inventory-service**: Filebeat отправляет как plain text (парсинг в Logstash через grok)
- **payment-service**: Filebeat отправляет как plain text (парсинг в Logstash через grok)

## 1. Order API (JSON формат)

### Исходный лог в Serilog:
```csharp
logger.LogInformation("Order completed {@Order} traceId={TraceId}", saved, traceId);
```

### Результат в JSON:
```json
{
  "@timestamp": "2026-01-27T10:30:00.123Z",
  "Level": "Information",
  "MessageTemplate": "Order completed {@Order} traceId={TraceId}",
  "Properties": {
    "service": "order-api",
    "Order": {
      "Id": "abc-123",
      "ItemId": 1,
      "Quantity": 2,
      "UserId": "demo-user",
      "Status": "Completed"
    },
    "TraceId": "f1e2d3c4b5a6"
  }
}
```

### После обработки Logstash:
```json
{
  "@timestamp": "2026-01-27T10:30:00.123Z",
  "service": "order-api",
  "level": "information",
  "traceId": "f1e2d3c4b5a6",
  "msg": "Order completed {@Order} traceId={TraceId}",
  "Order": { ... }
}
```

**Важно:** Параметры `{TraceId}` и `{@Order}` НЕ подставляются в текст сообщения, а сохраняются как отдельные поля. Это структурированное логирование!

---

## 2. Inventory Service (Key/Value формат)

### Исходный лог в Serilog:
```csharp
logger.LogInformation("Item available {ItemId} qty {Quantity} traceId={TraceId}", 
    request.ItemId, request.Quantity, traceId);
```

### Результат в файле/HTTP:
```
inventory | 2026-01-27T10:30:00.123Z | level=Information | item=1 | qty=2 | user=demo-user | traceId=f1e2d3c4b5a6 | msg=Item available
```

### После обработки Logstash:
```json
{
  "@timestamp": "2026-01-27T10:30:00.123Z",
  "service": "inventory-service",
  "level": "Information",
  "itemId": "1",
  "quantity": "2",
  "userId": "demo-user",
  "traceId": "f1e2d3c4b5a6",
  "msg": "Item available"
}
```

---

## 3. Payment Service (Logfmt формат)

### Исходный лог в Serilog:
```csharp
logger.LogInformation("Payment approved for user {UserId} traceId={TraceId}", 
    request.UserId, traceId);
```

### Результат в файле/HTTP:
```
timestamp="2026-01-27T10:30:00.123Z" level=Information service=payment-service user="demo-user" traceId="f1e2d3c4b5a6" message="Payment approved for user demo-user"
```

### После обработки Logstash:
```json
{
  "@timestamp": "2026-01-27T10:30:00.123Z",
  "service": "payment-service",
  "level": "Information",
  "userId": "demo-user",
  "traceId": "f1e2d3c4b5a6",
  "msg": "Payment approved for user demo-user"
}
```

---

## Поиск в Kibana

### По TraceId (найти все логи одного запроса):
```
traceId:"f1e2d3c4b5a6"
```

### По сервису:
```
service:"order-api"
```

### По уровню логирования:
```
level:"error" OR level:"Error"
```

### Комбинированный поиск:
```
service:"payment-service" AND level:"error" AND traceId:"f1e2d3c4b5a6"
```
