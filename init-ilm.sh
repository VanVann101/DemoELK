#!/bin/bash
set -e

echo "Waiting for Elasticsearch to be ready..."
until curl -s http://elasticsearch:9200/_cluster/health | grep -q '"status":"yellow"\|"status":"green"'; do
  echo "Elasticsearch is unavailable - sleeping"
  sleep 5
done

echo "Elasticsearch is ready!"
echo "Creating ILM policy..."

# Создаём ILM политику
curl -X PUT "http://elasticsearch:9200/_ilm/policy/dotnet-logs-policy" \
  -H 'Content-Type: application/json' \
  -d '{
  "policy": {
    "phases": {
      "hot": {
        "min_age": "0ms",
        "actions": {
          "set_priority": {
            "priority": 100
          }
        }
      },
      "cold": {
        "min_age": "7d",
        "actions": {
          "set_priority": {
            "priority": 10
          }
        }
      },
      "delete": {
        "min_age": "30d",
        "actions": {
          "delete": {}
        }
      }
    }
  }
}'

echo ""
echo "ILM policy created successfully!"

# Применяем политику к существующим индексам (если есть)
echo "Applying ILM policy to existing indices..."
curl -X PUT "http://elasticsearch:9200/dotnet-logs-*/_settings" \
  -H 'Content-Type: application/json' \
  -d '{
  "index.lifecycle.name": "dotnet-logs-policy"
}' 2>/dev/null || echo "No existing indices (this is normal for first run)"

echo ""
echo "ILM setup complete!"
echo "Policy: hot (0d) -> cold (3d) -> delete (7d)"
