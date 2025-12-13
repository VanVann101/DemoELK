# ELK + .NET demo

Демо стенд для аттестации: три минимальных микросервиса и стек Elasticsearch/Kibana/Logstash в одном `docker-compose`.

## Сервисы
- `order-api` — принимает заказы, ходит в `inventory-service` и `payment-service`, пишет логи.
- `inventory-service` — проверяет наличие товара, иногда отвечает 500/долгие ответы.
- `payment-service` — имитирует оплату: успех / недостаточно средств / 500.
- `logstash` — принимает разнородные логи, нормализует и кладёт в Elasticsearch.
- `elasticsearch`, `kibana` — хранение и визуализация логов.

Все сервисы логируют в Logstash → Elasticsearch через Serilog.

## Запуск
```bash
docker compose up --build
```
- Order API: http://localhost:8080
- Kibana: http://localhost:5601 (security выключен для простоты)
- Logstash TCP input: tcp://localhost:5000
- Elasticsearch: http://localhost:9200

Остановить: `docker compose down -v` (удалит данные ES).

## Проверка сценария
Отправить заказ:
```bash
curl -X POST http://localhost:8080/orders ^
  -H "Content-Type: application/json" ^
  -d "{\"itemId\":1,\"quantity\":2,\"userId\":\"demo-user\"}"
```
Ответ содержит `traceId` (для наглядности), логи — в Kibana Discover.

Просмотр конкретного заказа:
```bash
curl http://localhost:8080/orders/{orderId}
```

## Что смотреть в Kibana
- **Discover**: индекс `dotnet-logs-*` — нормализованные Logstashом логи (inventory в текстовом K/V формате, payment/order в JSON).

## Поведение для демо
- `inventory-service`: 10% 500, 10% дополнительная задержка ~1s, учёт остатка по товару.
- `payment-service`: ~15% `InsufficientFunds`, ~10% 500, остальное — успех.
- `order-api`: сохраняет заказы в памяти (статус `Completed/Rejected/Failed`) и возвращает TraceId.

