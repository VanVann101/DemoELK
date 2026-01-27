# ELK + .NET demo

Демо стенд для аттестации: три минимальных микросервиса и стек Elasticsearch/Kibana/Logstash в одном `docker-compose`.

## Сервисы
- `order-api` — принимает заказы, ходит в `inventory-service` и `payment-service`, пишет логи.
- `inventory-service` — проверяет наличие товара, иногда отвечает 500/долгие ответы.
- `payment-service` — имитирует оплату: успех / недостаточно средств / 500.
- `filebeat` — собирает логи из файлов сервисов и отправляет в Logstash.
- `logstash` — принимает логи от Filebeat (порт 5044) и напрямую от сервисов (порт 5000), нормализует и кладёт в Elasticsearch.
- `elasticsearch`, `kibana` — хранение и визуализация логов.

Все сервисы логируют двумя способами:
1. В файлы `/app/logs/*.log` (собираются Filebeat)
2. Напрямую в Logstash через HTTP (порт 5000)

## Запуск
```bash
docker compose up --build
```
- Order API: http://localhost:8080
- Kibana: http://localhost:5601 (security выключен для простоты)
- Logstash TCP input: tcp://localhost:5000
- Logstash Beats input: tcp://localhost:5044
- Elasticsearch: http://localhost:9200

Остановить: `docker compose down -v` (удалит данные ES и логи).

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
- **Discover**: индекс `dotnet-logs-*` — нормализованные Logstashом логи из разных форматов:
  - **order-api**: JSON формат
  - **inventory-service**: Key/Value текстовый формат
  - **payment-service**: Logfmt формат
  
Все форматы парсятся Logstash и приводятся к единой структуре с полями: `service`, `level`, `timestamp`, `userId`, `message`.

## Архитектура логирования
Проект демонстрирует два подхода к сбору логов:
1. **Filebeat** → читает файлы логов → отправляет в Logstash (порт 5044) → Logstash парсит → Elasticsearch
2. **Direct HTTP** → Serilog.Sinks.Http → отправляет в Logstash (порт 5000) → Logstash парсит → Elasticsearch

**Разделение ответственности:**
- **Filebeat**: легковесный shipper, только читает и отправляет строки логов
- **Logstash**: парсит все форматы (JSON, Key/Value, Logfmt), нормализует и обогащает данные
- **Elasticsearch**: хранит нормализованные логи

Оба потока попадают в один индекс `dotnet-logs-*` для единого анализа.

## Форматы логов
Проект демонстрирует работу с тремя разными форматами логов:
1. **order-api**: JSON (полный структурированный формат)
2. **inventory-service**: Key/Value текстовый формат (custom template)
3. **payment-service**: Logfmt формат (key="value" pairs)

Logstash нормализует все три формата в единую структуру для Elasticsearch.

## Распределённая трассировка (Distributed Tracing)
Проект реализует простую распределённую трассировку через HTTP заголовок `X-Trace-Id`:
1. **order-api** генерирует уникальный `traceId` для каждого запроса
2. Передаёт `traceId` в заголовках HTTP запросов к inventory-service и payment-service
3. Все три сервиса логируют один и тот же `traceId`
4. В Kibana можно найти все логи одного запроса по полю `traceId`

Пример поиска в Kibana: `traceId:"abc123def456"` покажет весь путь запроса через все сервисы.

## Поведение для демо
- `inventory-service`: 10% 500, 10% дополнительная задержка ~1s, учёт остатка по товару.
- `payment-service`: ~15% `InsufficientFunds`, ~10% 500, остальное — успех.
- `order-api`: сохраняет заказы в памяти (статус `Completed/Rejected/Failed`) и возвращает TraceId.

