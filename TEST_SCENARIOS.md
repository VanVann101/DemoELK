# Тестовые сценарии

Проект использует детерминированные сценарии на основе `itemId` для упрощения демонстрации и тестирования.

## Сценарии

### 1. itemId = 1: ✅ Успешный заказ
**Поведение:**
- Inventory: товар доступен (50ms)
- Payment: оплата успешна (100ms)
- Order: статус `Completed`

**Логи:**
```
[inventory] Item available itemId=1 qty=2
[payment] Payment approved for user demo-user
[order] Order completed
```

**Запрос:**
```bash
curl -X POST http://localhost:8080/orders \
  -H "Content-Type: application/json" \
  -d '{"itemId":1,"quantity":2,"userId":"demo-user"}'
```

---

### 2. itemId = 2: ❌ Нет на складе
**Поведение:**
- Inventory: товара нет (50ms)
- Payment: не вызывается
- Order: статус `Rejected`, причина "Out of stock"

**Логи:**
```
[inventory] Out of stock for item itemId=2
[order] Inventory out of stock
```

**Запрос:**
```bash
curl -X POST http://localhost:8080/orders \
  -H "Content-Type: application/json" \
  -d '{"itemId":2,"quantity":2,"userId":"demo-user"}'
```

---

### 3. itemId = 3: 💳 Недостаточно средств
**Поведение:**
- Inventory: товар доступен (50ms)
- Payment: недостаточно средств (100ms)
- Order: статус `Rejected`, причина "Insufficient funds"

**Логи:**
```
[inventory] Item available itemId=3 qty=2
[payment] Payment declined for user demo-user - insufficient funds
[order] Payment declined
```

**Запрос:**
```bash
curl -X POST http://localhost:8080/orders \
  -H "Content-Type: application/json" \
  -d '{"itemId":3,"quantity":2,"userId":"demo-user"}'
```

---

### 4. itemId = 4: 🔥 Ошибка в Inventory Service
**Поведение:**
- Inventory: возвращает 500 Internal Server Error (50ms)
- Payment: не вызывается
- Order: возвращает 502 Bad Gateway

**Логи:**
```
[inventory] Inventory internal error for item itemId=4
[order] Inventory check failed
```

**Запрос:**
```bash
curl -X POST http://localhost:8080/orders \
  -H "Content-Type: application/json" \
  -d '{"itemId":4,"quantity":2,"userId":"demo-user"}'
```

---

### 5. itemId = 5: 🔥 Ошибка в Payment Service
**Поведение:**
- Inventory: товар доступен (50ms)
- Payment: возвращает 500 Internal Server Error (100ms)
- Order: возвращает 502 Bad Gateway

**Логи:**
```
[inventory] Item available itemId=5 qty=2
[payment] External processor failure for user demo-user
[order] Payment processing failed
```

**Запрос:**
```bash
curl -X POST http://localhost:8080/orders \
  -H "Content-Type: application/json" \
  -d '{"itemId":5,"quantity":2,"userId":"demo-user"}'
```

---

### 6. itemId = 6: 🐌 Медленный Inventory Service
**Поведение:**
- Inventory: товар доступен, но с задержкой 2 секунды
- Payment: оплата успешна (100ms)
- Order: статус `Completed`, но долгое время обработки

**Логи:**
```
[inventory] Slow processing for item itemId=6
[inventory] Item available itemId=6 qty=2 (через 2 сек)
[payment] Payment approved for user demo-user
[order] Order completed
```

**Запрос:**
```bash
curl -X POST http://localhost:8080/orders \
  -H "Content-Type: application/json" \
  -d '{"itemId":6,"quantity":2,"userId":"demo-user"}'
```

**Использование:** Демонстрация проблем с производительностью и таймаутами.

---

### 7. itemId = 7: 🐌 Медленный Payment Service
**Поведение:**
- Inventory: товар доступен (50ms)
- Payment: оплата успешна, но с задержкой 2 секунды
- Order: статус `Completed`, но долгое время обработки

**Логи:**
```
[inventory] Item available itemId=7 qty=2
[payment] Slow payment processing for user demo-user
[payment] Payment approved for user demo-user (через 2 сек)
[order] Order completed
```

**Запрос:**
```bash
curl -X POST http://localhost:8080/orders \
  -H "Content-Type: application/json" \
  -d '{"itemId":7,"quantity":2,"userId":"demo-user"}'
```

**Использование:** Демонстрация проблем с производительностью внешних сервисов.

---

## Использование в Swagger UI

1. Откройте http://localhost:8080/swagger
2. Раскройте `POST /orders`
3. Нажмите "Try it out"
4. Измените `itemId` на нужный сценарий (1-7)
5. Нажмите "Execute"
6. Скопируйте `traceId` из ответа
7. Найдите логи в Kibana: `traceId:"<ваш-traceId>"`

## Поиск в Kibana

### Все успешные заказы:
```
service:"order-api" AND msg:"Order completed"
```

### Все ошибки:
```
level:"error"
```

### Конкретный сценарий (например, недостаточно средств):
```
msg:"insufficient funds"
```

### Медленные запросы (по traceId):
```
traceId:"<traceId>" AND (msg:"Slow processing" OR msg:"Slow payment")
```
