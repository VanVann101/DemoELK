from locust import HttpUser, task, between
import random

    #Симуляция пользователей, создающих заказы
class OrderUser(HttpUser): 
    # Пауза между запросами от одного пользователя (1-3 секунды)
    wait_time = between(1, 3)
    
    @task(50)  # 50% - успешные заказы (самый частый сценарий)
    def create_successful_order(self):
        self.client.post("/orders", json={
            "itemId": 1,
            "quantity": random.randint(1, 5),
            "userId": f"user-{random.randint(1, 100)}"
        }, name="Success")
    
    @task(15)  # 15% - товара нет на складе
    def create_out_of_stock_order(self):
        self.client.post("/orders", json={
            "itemId": 2,
            "quantity": random.randint(1, 10),
            "userId": f"user-{random.randint(1, 100)}"
        }, name="Out of Stock")
    
    @task(10)  # 10% - недостаточно средств
    def create_insufficient_funds_order(self):
        self.client.post("/orders", json={
            "itemId": 3,
            "quantity": random.randint(1, 5),
            "userId": f"user-{random.randint(1, 100)}"
        }, name="Insufficient Funds")
    
    @task(5)  # 5% - ошибка в inventory service => возвращаем 500
    def create_inventory_error_order(self):
        self.client.post("/orders", json={
            "itemId": 4,
            "quantity": random.randint(1, 5),
            "userId": f"user-{random.randint(1, 100)}"
        }, name="Inventory Error")
    
    @task(5)  # 5% - ошибка в payment service => возвращаем 500
    def create_payment_error_order(self):
        self.client.post("/orders", json={
            "itemId": 5,
            "quantity": random.randint(1, 5),
            "userId": f"user-{random.randint(1, 100)}"
        }, name="Payment Error")
    
    @task(10)  # 10% - медленный inventory, но запрос успешный
    def create_slow_inventory_order(self):
        self.client.post("/orders", json={
            "itemId": 6,
            "quantity": random.randint(1, 5),
            "userId": f"user-{random.randint(1, 100)}"
        }, name="Slow Inventory")
    
    @task(5)  # 5% - медленный payment, но запрос успешный
    def create_slow_payment_order(self):
        self.client.post("/orders", json={
            "itemId": 7,
            "quantity": random.randint(1, 5),
            "userId": f"user-{random.randint(1, 100)}"
        }, name="Slow Payment")
    
    @task(1)  # Иногда - проверка health endpoint
    def check_health(self):
        self.client.get("/", name="Health Check")


    #Пиковая нагрузка - только быстрые успешные заказы.
    #Будем использовать этот класс для симуляции часа пик.
class PeakHourUser(HttpUser):
    wait_time = between(0.5, 1.5)
    
    @task
    def create_order(self):
        self.client.post("/orders", json={
            "itemId": 1,
            "quantity": random.randint(1, 3),
            "userId": f"peak-user-{random.randint(1, 200)}"
        })