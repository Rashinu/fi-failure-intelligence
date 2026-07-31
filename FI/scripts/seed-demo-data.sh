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
# Bkz. docs/CTO_REVIEW_ANALYSIS.md Due Diligence D7 - integrations CRUD/rotasyon artık admin
# Basic Auth gerektiriyor (bkz. AdminBasicAuthMiddleware). docker-compose.yml'deki fi-app
# varsayılanıyla eşleşiyor; gerçek bir dağıtımda FI_ADMIN_SECRET ile override edin.
ADMIN_SECRET="${FI_ADMIN_SECRET:-local-dev-admin-secret-change-me}"

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
    -u "admin:$ADMIN_SECRET" \
    -H "Content-Type: application/json" \
    -d "{\"name\":\"$name\",\"provider\":\"$provider\",\"environment\":\"production\",\"owner\":\"demo\",\"endpointUrl\":\"https://api.$provider.com\",\"businessCriticality\":\"$criticality\"}"
}

send_stripe_event() {
  local integration_id="$1" secret="$2" status_code="$3" error_code="$4" event_id="$5" customer_id="${6:-}" operation_ref="${7:-}"
  local object_json="\"id\":\"$event_id\""
  [ -n "$customer_id" ] && object_json="$object_json,\"customer\":\"$customer_id\""
  # Bkz. docs/product/M19_CLOSE_THE_PRODUCT_LOOP.md P0-A - StripeConnector "metadata" alanindan
  # operation_ref/operation_type/business_record_ref cikarir (gercek Stripe'in arbitrary
  # key-value metadata konvansiyonunu taklit eder).
  if [ -n "$operation_ref" ]; then
    object_json="$object_json,\"metadata\":{\"operation_ref\":\"$operation_ref\",\"operation_type\":\"PaymentSync\",\"business_record_ref\":\"subscription-$operation_ref\"}"
  fi
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
curl -s -X POST "$BASE_URL/api/v1/integrations/$INTEGRATION_1/api-key/rotate" -u "admin:$ADMIN_SECRET" > /dev/null

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

echo "-- Senaryo 4 (Golden Incident): PaymentSync credential failure --"
# Bkz. docs/product/M19_CLOSE_THE_PRODUCT_LOOP.md - M19'un tek amiral gemisi demo senaryosu.
# Prod credential rotate edilir, ardindan 43 teknik event / 12 PaymentSync operasyonu / 7 musteri
# uretilir - "43 event = 43 basarisiz is operasyonu" YANLIS varsayimini gorsel olarak reddeder.
OUT4=$(create_integration "PaymentService (Prod)" "stripe" "Critical")
INTEGRATION_4=$(echo "$OUT4" | grep -o '"integrationId":"[^"]*"' | cut -d'"' -f4)
SECRET_4=$(echo "$OUT4" | grep -o '"webhookSecret":"[^"]*"' | cut -d'"' -f4)

curl -s -X POST "$BASE_URL/api/v1/integrations/$INTEGRATION_4/api-key/rotate" -u "admin:$ADMIN_SECRET" > /dev/null

event_counter=0
for op in 1 2 3 4 5 6 7 8 9 10 11 12; do
  customer_index=$(( (op - 1) % 7 + 1 ))
  events_for_op=3
  [ "$op" -le 7 ] && events_for_op=4
  for i in $(seq 1 "$events_for_op"); do
    event_counter=$((event_counter + 1))
    send_stripe_event "$INTEGRATION_4" "$SECRET_4" 401 "invalid_api_key" "ch_paymentsync_${event_counter}" "cus_ps_${customer_index}" "payment-sync-$op"
    sleep 0.1
  done
done
echo "Golden incident: $event_counter event gonderildi (beklenen: 43 event / 12 operasyon / 7 musteri)"

echo ""
echo "== Event'ler gonderildi. Job zincirinin (classify->fingerprint->incident->evidence->AI, ~10-20sn) calismasi icin bekleniyor... =="
sleep 20

echo ""
echo "== Hazir. Dashboard: $BASE_URL/Incidents =="
