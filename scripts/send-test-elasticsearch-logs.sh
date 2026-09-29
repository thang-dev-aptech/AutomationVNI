#!/usr/bin/env bash
# Gửi 1 document log mỗi mức (DEBUG/INFO/WARN/ERROR/CRITICAL) lên Elasticsearch _bulk
# để kiểm tra pipeline + Kibana. Dùng cùng env với app (ES_*, CF_ACCESS_*).
#
# Cách dùng:
#   set -a && source .env && set +a   # hoặc export thủ công
#   ./scripts/send-test-elasticsearch-logs.sh
set -euo pipefail

: "${ES_URL:?ES_URL is required}"
: "${ES_PASSWORD:?ES_PASSWORD is required}"
: "${CF_ACCESS_CLIENT_ID:?CF_ACCESS_CLIENT_ID is required}"
: "${CF_ACCESS_CLIENT_SECRET:?CF_ACCESS_CLIENT_SECRET is required}"

ES_USER="${ES_USER:-elastic}"
LOG_SERVICE_NAME="${LOG_SERVICE_NAME:-automationvni}"
LOG_ENV="${LOG_ENV:-dev}"
ES_URL="${ES_URL%/}"
INDEX="app-logs-$(date -u +%Y.%m.%d)"
TS="$(date -u +%Y-%m-%dT%H:%M:%S.000Z)"

auth="$(printf '%s:%s' "$ES_USER" "$ES_PASSWORD" | base64 -w0 2>/dev/null || printf '%s:%s' "$ES_USER" "$ES_PASSWORD" | base64)"

bulk=""
for level in DEBUG INFO WARN ERROR CRITICAL; do
  meta=$(printf '{"index":{"_index":"%s"}}' "$INDEX")
  doc=$(printf '{"@timestamp":"%s","level":"%s","service":"%s","environment":"%s","message":"test log from send-test-elasticsearch-logs.sh (%s)","logger":"scripts.send-test-elasticsearch-logs","test":true}' \
    "$TS" "$level" "$LOG_SERVICE_NAME" "$LOG_ENV" "$level")
  bulk+="${meta}"$'\n'"${doc}"$'\n'
done

echo "POST ${ES_URL}/_bulk → index ${INDEX}"
resp="$(curl -sS -w '\nHTTP_CODE:%{http_code}\n' \
  -X POST "${ES_URL}/_bulk" \
  -H "Content-Type: application/x-ndjson" \
  -H "Authorization: Basic ${auth}" \
  -H "CF-Access-Client-Id: ${CF_ACCESS_CLIENT_ID}" \
  -H "CF-Access-Client-Secret: ${CF_ACCESS_CLIENT_SECRET}" \
  --max-time 15 \
  --data-binary "$bulk")"

echo "$resp"
echo
echo "Kiểm tra Kibana: index pattern app-logs-* , filter service=${LOG_SERVICE_NAME} và message chứa 'send-test-elasticsearch-logs'."
