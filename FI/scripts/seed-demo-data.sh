#!/usr/bin/env bash
# Bkz. docs/CTO_REVIEW_ANALYSIS.md M17 (Product Proof) - "5 dakikada urunun degerini gosterebilmek"
# icin gercekci demo verisi uretir. `docker compose up` sonrasi calistirilmalidir.
#
# Kullanim:
#   cd FI && docker compose -f docker/docker-compose.yml up -d --build
#   bash scripts/seed-demo-data.sh
#   -> http://localhost:8080/Incidents adresini acin

set -euo pipefail

BASE_URL="${FI_BASE_URL:-http://localhost:8080}"

sign_stripe() {
  local secret="$1" body="$2" ts
  ts=$(date +%s)
  local sig
  sig=$(printf '%s' "${ts}.${body}" | openssl dgst -sha256 -hmac "$secret" | sed 's/^.* //')
  printf 't=%s,v1=%s' "$ts" "$sig"
}

create_integration() {
  local name="$1" provider="$2" criticality="$3"
  curl -s -X POST "$BASE_URL/api/v1/integrations" \
    -H "Content-Type: application/json" \
    -d "{\"name\":\"$name\",\"provider\":\"$provider\",\"environment\":\"production\",\"owner\":\"demo\",\"endpointUrl\":\"https://api.$provider.com\",\"businessCriticality\":\"$criticality\"}"
}

send_stripe_event() {
  local integration_id="$1" secret="$2" status_code="$3" error_code="$4" event_id="$5" customer_id="${6:-}"
  local object_json="\"id\":\"$event_id\""
  [ -n "$customer_id" ] && object_json="$object_json,\"customer\":\"$customer_id\""
  local body="{\"type\":\"charge.failed\",\"httpStatusCode\":$status_code,\"data\":{\"object\":{$object_json}},\"error\":{\"code\":\"$error_code\"}}"
  local sig
  sig=$(sign_stripe "$secret" "$body")
  curl -s -X POST "$BASE_URL/api/v1/webhooks/stripe/$integration_id/events" \
    -H "Content-Type: application/json" -H "Stripe-Signature: $sig" -d "$body" > /dev/null
}

echo "== FI demo verisi uretiliyor ($BASE_URL) =="

echo "-- Senaryo 1: Stripe API key rotasyonu sonrasi 401 patlamasi --"
OUT1=$(create_integration "Stripe Payments (Prod)" "stripe" "Critical")
INTEGRATION_1=$(echo "$OUT1" | grep -o '"integrationId":"[^"]*"' | cut -d'"' -f4)
SECRET_1=$(echo "$OUT1" | grep -o '"webhookSecret":"[^"]*"' | cut -d'"' -f4)

# Rotasyon -> CONFIG_CHANGE evidence kaynagi bunu incident'a otomatik iliskilendirecek.
curl -s -X POST "$BASE_URL/api/v1/integrations/$INTEGRATION_1/api-key/rotate" > /dev/null

for i in 1 2 3 4 5 6; do
  # ilk 3 event ayni musteriye ait - "kac musteri etkilendi" farkli bir sayi (4) gostersin.
  customer=$([ "$i" -le 3 ] && echo "cus_demo_a" || echo "cus_demo_$i")
  send_stripe_event "$INTEGRATION_1" "$SECRET_1" 401 "invalid_api_key" "ch_demo_auth_$i" "$customer"
  sleep 0.2
done

echo "-- Senaryo 2: Rate limit --"
OUT2=$(create_integration "Stripe Payments (Staging)" "stripe" "Medium")
INTEGRATION_2=$(echo "$OUT2" | grep -o '"integrationId":"[^"]*"' | cut -d'"' -f4)
SECRET_2=$(echo "$OUT2" | grep -o '"webhookSecret":"[^"]*"' | cut -d'"' -f4)
for i in 1 2 3; do
  send_stripe_event "$INTEGRATION_2" "$SECRET_2" 429 "rate_limit" "ch_demo_rate_$i"
  sleep 0.2
done

echo "-- Senaryo 3: Provider outage (5xx) --"
OUT3=$(create_integration "Stripe Payments (EU)" "stripe" "High")
INTEGRATION_3=$(echo "$OUT3" | grep -o '"integrationId":"[^"]*"' | cut -d'"' -f4)
SECRET_3=$(echo "$OUT3" | grep -o '"webhookSecret":"[^"]*"' | cut -d'"' -f4)
for i in 1 2; do
  send_stripe_event "$INTEGRATION_3" "$SECRET_3" 503 "" "ch_demo_outage_$i"
  sleep 0.2
done

echo ""
echo "== Event'ler gonderildi. Job zincirinin (classify->fingerprint->incident->evidence->AI, ~10-20sn) calismasi icin bekleniyor... =="
sleep 20

echo ""
echo "== Hazir. Dashboard: $BASE_URL/Incidents =="
