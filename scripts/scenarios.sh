#!/usr/bin/env bash
# Tiffin: every scenario, end to end, against the running services.
#
# Each scenario is told through real HTTP and gRPC calls with real tokens from Keycloak. Every step
# states what it expects; the script exits non-zero if any expectation failed, so it is a demonstration
# and an end-to-end test at the same time.
#
#   scripts/scenarios.sh            # all scenarios
#   scripts/scenarios.sh S0         # only these
#
# Needs: the dependencies (scripts/up.sh), the services (scripts/run.sh all), curl, jq and grpcurl.
set -uo pipefail

source "$(dirname "$0")/lib.sh"
set +e
KEYCLOAK_URL="${KEYCLOAK_URL:-http://localhost:$(env_value KEYCLOAK_PORT 38180)}"
EDGE_ADDR="${EDGE_ADDR:-localhost:$(env_value GATEWAY_HTTPS_PORT 39443)}"
EDGE_URL="https://$EDGE_ADDR"
# The edge's certificate was made for this machine and is trusted by nobody, so every call names it.
EDGE_CA="${EDGE_CA:-$GATEWAY_CA}"
RABBITMQ_API="${RABBITMQ_API:-http://localhost:$(env_value RABBITMQ_UI_PORT 35673)/api}"
PAYLANE_URL="${PAYLANE_URL:-http://localhost:$(env_value WIREMOCK_PORT 38081)}"

# ------------------------------------------------------------------------------------ presentation
if [ -t 1 ]; then B=$'\e[1m'; G=$'\e[32m'; R=$'\e[31m'; Y=$'\e[33m'; N=$'\e[0m'; else B=; G=; R=; Y=; N=; fi
passed=0; failed=0; skipped=0
section() { printf '\n%s━━ %s%s\n' "$B" "$*" "$N"; }
say()     { printf '   %s\n' "$*"; }
show()    { jq -C "${1:-.}" <<<"$LAST" 2>/dev/null | sed 's/^/      /' || printf '      %s\n' "$LAST"; }
ok()      { passed=$((passed + 1)); printf '   %s✔%s %s\n' "$G" "$N" "$*"; }
bad()     { failed=$((failed + 1)); printf '   %s✘ %s%s\n' "$R" "$*" "$N"; }
skip()    { skipped=$((skipped + 1)); printf '   %s∅ skipped: %s%s\n' "$Y" "$*" "$N"; }
expect()  { local want="$1"; shift; if [ "$STATUS" = "$want" ]; then ok "$* → HTTP $STATUS"; else bad "$* → HTTP $STATUS, expected $want"; show; fi; }
# A check whose expectation is empty proves nothing: it is a step before it that failed, and it says so.
check()   { local actual="$1" want="$2"; shift 2; if [ -z "$want" ]; then bad "$*: nothing to compare with, an earlier step failed"; elif [ "$actual" = "$want" ]; then ok "$* = $actual"; else bad "$* = $actual, expected $want"; fi; }

# ------------------------------------------------------------------------------------ transport
LAST=""; STATUS=""
api() { # api METHOD URL [TOKEN] [JSON]  → sets STATUS and LAST
  local method="$1" url="$2" token="${3:-}" body="${4:-}" out
  out=$(mktemp)
  local args=(-s -m 20 -o "$out" -w '%{http_code}' -X "$method" "$url" -H 'Accept: application/json')
  case "$url" in https://*) args+=(--cacert "$EDGE_CA") ;; esac
  [ -n "$token" ] && args+=(-H "Authorization: Bearer $token")
  [ -n "${LANG_HEADER:-}" ] && args+=(-H "Accept-Language: $LANG_HEADER")
  [ -n "$body" ] && args+=(-H 'Content-Type: application/json' --data "$body")
  STATUS=$(curl "${args[@]}")
  LAST=$(cat "$out"); rm -f "$out"
}
grpc() { # grpc PORT METHOD [TOKEN] [JSON]  → sets LAST and GRPC_CODE ("OK", or the status the service answered)
  local args=(-plaintext -max-time 20)
  [ -n "${3:-}" ] && args+=(-H "authorization: Bearer $3")
  [ -n "${LANG_HEADER:-}" ] && args+=(-H "accept-language: $LANG_HEADER")
  local data="${4:-}"; [ -n "$data" ] || data='{}'
  # grpcurl prints a message with the names of the contract: order_id, not orderId.
  LAST=$(grpcurl "${args[@]}" -d "$data" "localhost:$1" "$2" 2>&1)
  if [ $? -eq 0 ]; then GRPC_CODE=OK; else GRPC_CODE=$(sed -n 's/^ *Code: *//p' <<<"$LAST" | head -1); fi
}
code() { jq -r '.errorCode // empty' <<<"$LAST"; }
psql_in() { # psql_in DATABASE SQL  → a read-only look into a service's own database
  compose exec -T "$( [ "$1" = tiffin_tracking ] && echo timescale || echo postgres )" psql -U "$(env_value POSTGRES_USER tiffin)" -d "$1" -Atc "$2"
}
rabbit_dead() { # how many messages wait in the broker's dead-letter queue
  curl -s -m 5 -u "$(env_value RABBITMQ_USER tiffin):$(env_value RABBITMQ_PASSWORD tiffin)" "$RABBITMQ_API/queues" \
    | jq -r '[.[] | select(.name == "wolverine-dead-letter-queue") | .messages] | add // 0' 2>/dev/null
}
paylane() { # paylane PATH ORDER_ID  → how often the provider was asked about this order
  [ -n "$2" ] || { echo "?"; return; }
  curl -s "$PAYLANE_URL/__admin/requests" | jq --arg p "/paylane/v1/$1" --arg id "$2" \
    '[.requests[] | select(.request.url == $p) | select((.request.headers["Idempotency-Key"] // "") | contains($id))] | length'
}

# ------------------------------------------------------------------------------------ the people and the places
RESTAURANTS="http://localhost:$(rest_port_of restaurants)"; ORDERING="http://localhost:$(rest_port_of ordering)"
KITCHEN="http://localhost:$(rest_port_of kitchen)"; DISPATCH="$(grpc_port_of dispatch)"
RUN="$(date +%H%M%S)"   # restaurants are named once per city, and the scenarios may be run again

open_restaurant() { # open_restaurant MANAGER_TOKEN NAME CURRENCY CODE PRICE [CODE PRICE ...]  → sets RESTAURANT_ID
  local token="$1" name="$2" currency="$3"; shift 3
  api POST "$RESTAURANTS/v1/restaurants" "$token" "$(jq -nc --arg n "$name" --arg c "$currency" '{name:$n, currency:$c}')"
  RESTAURANT_ID=$(jq -r '.restaurantId // empty' <<<"$LAST")
  while [ $# -gt 0 ]; do
    api PUT "$RESTAURANTS/v1/restaurants/$RESTAURANT_ID/menu/$1" "$token" "$(jq -nc --arg n "$1" --argjson p "$2" '{name:$n, price:$p}')"; shift 2
  done
  api POST "$RESTAURANTS/v1/restaurants/$RESTAURANT_ID/open" "$token"
}
order_body() { # order_body RESTAURANT_ID CODE QUANTITY EXPECTED_TOTAL PAYMENT_TOKEN
  jq -nc --arg r "$1" --arg c "$2" --argjson q "$3" --argjson t "$4" --arg p "$5" \
    '{restaurantId:$r, lines:[{code:$c, quantity:$q}], deliverTo:{recipient:"Sara Ahmadi", phone:"+989121234567", district:"Vanak", line:"12 Gandhi St, unit 4"}, paymentToken:$p, expectedTotal:$t}'
}
place() { # place TOKEN RESTAURANT_ID CODE QUANTITY EXPECTED_TOTAL PAYMENT_TOKEN  → sets ORDER_ID. Every call is a new order, so every call sends a new key.
  local out headers; out=$(mktemp); headers=$(mktemp)
  local args=(-s -m 20 -o "$out" -D "$headers" -w '%{http_code}' -X POST "$ORDERING/v1/orders" -H "Authorization: Bearer $1"
    -H "Idempotency-Key: ${IDEMPOTENCY_KEY:-$(uuidgen)}" -H 'Content-Type: application/json' --data "$(order_body "$2" "$3" "$4" "$5" "$6")")
  [ -n "${LANG_HEADER:-}" ] && args+=(-H "Accept-Language: $LANG_HEADER")
  STATUS=$(curl "${args[@]}"); LAST=$(cat "$out")
  REPLAYED=$(tr -d '\r' <"$headers" | awk -F': ' 'tolower($1) == "idempotency-replayed" { print tolower($2) }')
  ORDER_ID=$(jq -r '.orderId // empty' <<<"$LAST"); rm -f "$out" "$headers"
}
WAIT_SECONDS="${WAIT_SECONDS:-30}"
wait_for() { # wait_for ORDER_ID TOKEN STATUS...  → polls until the order reaches one of STATUS; sets ORDER_STATUS
  local id="$1" token="$2"; shift 2
  local deadline=$((SECONDS + WAIT_SECONDS))
  while [ $SECONDS -lt $deadline ]; do
    api GET "$ORDERING/v1/orders/$id" "$token"; ORDER_STATUS=$(jq -r '.status // empty' <<<"$LAST")
    for want in "$@"; do [ "$ORDER_STATUS" = "$want" ] && return 0; done
    sleep 0.5
  done
  return 1
}
reaches() { # reaches ORDER_ID TOKEN STATUS WHAT  → the order reaches the status, or the step fails
  if wait_for "$1" "$2" "$3"; then ok "$4 → $3"; else bad "$4 → $ORDER_STATUS, expected $3"; show '{status, cancellationReason, history}'; fi
}
wait_ticket() { # wait_ticket MANAGER_TOKEN ORDER_ID STATUS  → the Kitchen has the order's ticket in this status
  local deadline=$((SECONDS + WAIT_SECONDS))
  while [ $SECONDS -lt $deadline ]; do
    api GET "$KITCHEN/v1/kitchen/tickets?status=$3&size=200" "$1"
    [ "$(jq -r --arg id "$2" '[.items[]? | select(.orderId == $id)] | length' <<<"$LAST")" = 1 ] && return 0
    sleep 0.5
  done
  return 1
}
refunded() { # refunded ORDER_ID TOKEN  → the order says that the money went back
  local deadline=$((SECONDS + WAIT_SECONDS))
  while [ $SECONDS -lt $deadline ]; do
    api GET "$ORDERING/v1/orders/$1" "$2"; [ "$(jq -r .refunded <<<"$LAST")" = true ] && return 0; sleep 0.5
  done
  return 1
}
history() { jq -r '.history[] | "      \(.occurredOnUtc[11:23])  \(.status)\(if .note then "  (\(.note))" else "" end)"' <<<"$LAST"; }
token_for() { # a person signs in at the app (password grant: for a demonstration only)
  curl -s -X POST "$KEYCLOAK_URL/realms/tiffin/protocol/openid-connect/token" \
    -d grant_type=password -d client_id=tiffin-app -d "username=$1" -d "password=$1-lab" | jq -r '.access_token // empty'
}
service_token() { # a service authenticates as itself: client credentials
  curl -s -X POST "$KEYCLOAK_URL/realms/tiffin/protocol/openid-connect/token" \
    -d grant_type=client_credentials -d "client_id=tiffin-$1-service" -d "client_secret=lab-only-$1-secret" | jq -r '.access_token // empty'
}
claim() { # claim TOKEN JQ-FILTER
  local p; p=$(cut -d. -f2 <<<"$1" | tr '_-' '/+'); while [ $(( ${#p} % 4 )) -ne 0 ]; do p="$p="; done
  base64 -d <<<"$p" 2>/dev/null | jq -r "$2"
}
wanted() { [ ${#ONLY[@]} -eq 0 ] && return 0; local s; for s in "${ONLY[@]}"; do [ "$s" = "$1" ] && return 0; done; return 1; }
ONLY=("$@")

# ------------------------------------------------------------------------------------ S0
if wanted S0; then
section "S0  The platform stands: nine services, each its own, each behind a token"
SARA=$(token_for sara); ELIF=$(token_for elif)
[ -n "$SARA" ] && ok "sara signs in at Keycloak, not at a service" || bad "sara could not sign in: is Keycloak up? (scripts/up.sh)"
check "$(claim "$SARA" .tenant_id)" tehran "sara's city, from the group she is in"
check "$(claim "$ELIF" .tenant_id)" istanbul "elif's city"

for service in "${SERVICES[@]}"; do
  rest="$(rest_port_of "$service")"; rpc="$(grpc_port_of "$service")"; name="Tiffin.$(pascal "$service")"
  if is_ready "$service"; then ok "$service is ready"; else bad "$service is not ready (scripts/run.sh $service)"; continue; fi
  if [ "$rest" != "-" ]; then
    api GET "http://localhost:$rest/v1/platform/status";          expect 401 "$service, with no token"
    if [ "$service" = payments ]; then :; else
      api GET "http://localhost:$rest/v1/platform/status" "$SARA"; expect 200 "$service, with sara's token"
      check "$(jq -r .service <<<"$LAST")" "$name" "who answered"
    fi
  fi
  if [ "$rpc" != "-" ]; then
    grpc "$rpc" "tiffin.$service.v1.PlatformProbe/GetStatus";     check "$GRPC_CODE" Unauthenticated "$service over gRPC, with no token"
  fi
done

say "A token is issued for the services it names, and refused by the others."
KITCHEN_SERVICE=$(service_token kitchen)   # issued for Restaurants only
api GET "http://localhost:$(rest_port_of restaurants)/v1/platform/status" "$KITCHEN_SERVICE"; expect 200 "the Kitchen service at Restaurants"
api GET "http://localhost:$(rest_port_of access)/v1/platform/status" "$KITCHEN_SERVICE";      expect 401 "the Kitchen service at Access"
grpc "$(grpc_port_of payments)" tiffin.payments.v1.PlatformProbe/GetStatus "$SARA";   check "$GRPC_CODE" Unauthenticated "sara's token at Payments, which only services call"
ORDERING_SERVICE=$(service_token ordering) # issued for Payments and Restaurants
grpc "$(grpc_port_of payments)" tiffin.payments.v1.PlatformProbe/GetStatus "$ORDERING_SERVICE"; check "$GRPC_CODE" OK "the Ordering service at Payments"

say "Only Access may use Keycloak's admin API."
ACCESS_SERVICE=$(service_token access)
api GET "$KEYCLOAK_URL/admin/realms/tiffin/groups" "$ACCESS_SERVICE";   expect 200 "the Access service reads the groups of the realm"
api GET "$KEYCLOAK_URL/admin/realms/tiffin/groups" "$ORDERING_SERVICE"; expect 403 "the Ordering service asks the same"
api GET "$KEYCLOAK_URL/admin/realms/tiffin/groups" "$SARA";     expect 403 "sara asks the same"

say "The edge: one door, and no way in to the service that only services call."
if [ -s "$EDGE_CA" ]; then
  api GET "$EDGE_URL/v1/orders";                                                     expect 401 "the edge, an order with no token"
  api POST "$EDGE_URL/tiffin.payments.v1.PlatformProbe/GetStatus" "$ORDERING_SERVICE";       expect 404 "Payments through the edge, even with a good token"
else
  skip "the edge has no certificate yet: scripts/up.sh"
fi
fi

# ------------------------------------------------------------------------------------ the cast
# Signed in once. mina manages restaurants in Tehran, kemal in Istanbul; omid carries in Tehran.
if wanted S1 || wanted S2 || wanted S3 || wanted S4 || wanted S5 || wanted S6 || wanted S7 || wanted S8 || wanted S9 || wanted S10 || wanted S11 || wanted S12 || wanted S13 || wanted S14; then
  SARA=$(token_for sara); REZA=$(token_for reza); MINA=$(token_for mina); OMID=$(token_for omid)
  ELIF=$(token_for elif); KEMAL=$(token_for kemal)
  open_restaurant "$MINA" "Dizi Sara $RUN" IRR DIZI 450000 DOOGH 60000
  TEHRAN_RESTAURANT="$RESTAURANT_ID"
fi
on_duty()  { grpc "$DISPATCH" tiffin.dispatch.v1.Couriers/GoOnDuty "$1"; }
free_courier() { # on duty, and carrying nothing: what an earlier scenario left with the courier is handed over
  on_duty "$1"
  grpc "$DISPATCH" tiffin.dispatch.v1.Couriers/GetMyDelivery "$1"
  [ "$GRPC_CODE" = OK ] && grpc "$DISPATCH" tiffin.dispatch.v1.Couriers/CompleteDelivery "$1" "{\"order_id\":\"$(jq -r .order_id <<<"$LAST")\"}"
  return 0
}
off_duty() { grpc "$DISPATCH" tiffin.dispatch.v1.Couriers/GoOffDuty "$1"; }

# ------------------------------------------------------------------------------------ S1
if wanted S1; then
section "S1  The journey of one order: five services, five transactions, no transaction across two"
on_duty "$OMID"; check "$GRPC_CODE" OK "omid goes on duty, over gRPC"
place "$SARA" "$TEHRAN_RESTAURANT" DIZI 2 900000 tok_ok
expect 202 "sara orders two dizi: accepted, not yet an order that anybody cooks"
S1_ORDER="$ORDER_ID"
reaches "$S1_ORDER" "$SARA" Paid "Payments charged the card"
check "$(paylane authorizations "$S1_ORDER")" 1 "the provider was asked to charge, times"
check "$(psql_in tiffin_payments "select \"PaymentToken\" is null from payments.payments where \"OrderId\" = '$S1_ORDER'")" t "the card's token was erased the moment it was used"
if wait_ticket "$MINA" "$S1_ORDER" Pending; then ok "the Kitchen shows mina the order"; else bad "the ticket did not arrive in the Kitchen"; fi
api POST "$KITCHEN/v1/kitchen/tickets/$S1_ORDER/accept" "$MINA" '{"readyInMinutes":25}'; expect 200 "mina accepts: ready in 25 minutes"
reaches "$S1_ORDER" "$SARA" OutForDelivery "Dispatch found a courier"
check "$(jq -r .courierName <<<"$LAST")" "Omid Sadeghi" "who carries it"
grpc "$DISPATCH" tiffin.dispatch.v1.Couriers/GetMyDelivery "$OMID"
check "$(jq -r .order_id <<<"$LAST")" "$S1_ORDER" "omid's app shows him the order"
check "$(jq -r .district <<<"$LAST")" Vanak "and where it goes"
grpc "$DISPATCH" tiffin.dispatch.v1.Couriers/CompleteDelivery "$OMID" "{\"order_id\":\"$S1_ORDER\"}"; check "$GRPC_CODE" OK "omid hands it over"
reaches "$S1_ORDER" "$SARA" Delivered "the order heard it from the event stream"
history
check "$(jq -r '[.history[].status] | join(" ")' <<<"$LAST")" "Placed Paid Accepted OutForDelivery Delivered" "the order's life"
say "A handler that runs from a queue has no request. It works for the city of its message, and as a named actor."
check "$(psql_in tiffin_ordering "select count(*) from audit.entries where \"EntityId\" = '$S1_ORDER' and \"TenantId\" is distinct from 'tehran'")" 0 "audit records of the order without its city"
check "$(psql_in tiffin_ordering "select string_agg(distinct \"ActorUserName\", ' ' order by \"ActorUserName\") from audit.entries where \"EntityId\" = '$S1_ORDER' and \"ActorKind\" = 3")" \
  "CourierAssigned DeliveryCompleted KitchenAccepted PaymentAuthorized" "the system actors that moved it"
check "$(psql_in tiffin_kitchen "select \"ActorUserName\" || ' in ' || \"TenantId\" from audit.entries where \"Action\" = 'order-accepted' order by \"Id\" desc limit 1")" "mina in tehran" "who accepted, and where"
fi

# ------------------------------------------------------------------------------------ S2
if wanted S2; then
section "S2  The restaurant refuses: what was done is taken back"
place "$SARA" "$TEHRAN_RESTAURANT" DIZI 1 450000 tok_ok; expect 202 "sara orders"
S2_ORDER="$ORDER_ID"
reaches "$S2_ORDER" "$SARA" Paid "the card was charged"
wait_ticket "$MINA" "$S2_ORDER" Pending
api POST "$KITCHEN/v1/kitchen/tickets/$S2_ORDER/reject" "$MINA" '{"reason":"out of lamb"}'; expect 200 "mina refuses: out of lamb"
reaches "$S2_ORDER" "$SARA" Cancelled "the order"
check "$(jq -r .cancellationReason <<<"$LAST")" restaurant-refused "why"
if refunded "$S2_ORDER" "$SARA"; then ok "the money went back"; else bad "the order was not refunded"; fi
check "$(paylane refunds "$S2_ORDER")" 1 "the provider was asked to refund, times"
check "$(psql_in tiffin_payments "select \"Status\" from payments.payments where \"OrderId\" = '$S2_ORDER'")" Refunded "the payment"
LANG_HEADER=fa api POST "$KITCHEN/v1/kitchen/tickets/$S2_ORDER/accept" "$MINA" '{"readyInMinutes":20}'
expect 422 "mina changes her mind and accepts after all"; check "$(code)" TICKET_NOT_PENDING "rule K1"
fi

# ------------------------------------------------------------------------------------ S3
if wanted S3; then
section "S3  Nobody can carry it: two steps are taken back"
off_duty "$OMID"; check "$GRPC_CODE" OK "omid goes home"
place "$SARA" "$TEHRAN_RESTAURANT" DOOGH 3 180000 tok_ok; expect 202 "sara orders"
S3_ORDER="$ORDER_ID"
reaches "$S3_ORDER" "$SARA" Paid "the card was charged"
wait_ticket "$MINA" "$S3_ORDER" Pending
api POST "$KITCHEN/v1/kitchen/tickets/$S3_ORDER/accept" "$MINA" '{"readyInMinutes":10}'; expect 200 "mina accepts"
reaches "$S3_ORDER" "$SARA" Cancelled "the order"
check "$(jq -r .cancellationReason <<<"$LAST")" no-courier "why"
if wait_ticket "$MINA" "$S3_ORDER" Cancelled; then ok "the Kitchen was told to stop"; else bad "the ticket is not cancelled"; fi
if refunded "$S3_ORDER" "$SARA"; then ok "the money went back"; else bad "the order was not refunded"; fi
on_duty "$OMID"
fi

# ------------------------------------------------------------------------------------ S4
if wanted S4; then
section "S4  The bank refuses: nothing was done that has to be taken back"
place "$SARA" "$TEHRAN_RESTAURANT" DIZI 1 450000 tok_insufficient_funds; expect 202 "sara orders with a card that has no money"
S4_ORDER="$ORDER_ID"
reaches "$S4_ORDER" "$SARA" Cancelled "the order"
check "$(jq -r .cancellationReason <<<"$LAST")" payment-declined "why"
check "$(jq -r '.history[-1].note' <<<"$LAST")" INSUFFICIENT_FUNDS "the bank's code"
api GET "$KITCHEN/v1/kitchen/tickets?size=200" "$MINA"
check "$(jq -r --arg id "$S4_ORDER" '[.items[] | select(.orderId == $id)] | length' <<<"$LAST")" 0 "tickets the Kitchen ever had for it"
check "$(paylane refunds "$S4_ORDER")" 0 "refunds asked of the provider"
fi

# ------------------------------------------------------------------------------------ S5
if wanted S5; then
section "S5  Twice is once: a request that is repeated, and an order that is not"
KEY="$(uuidgen)"
orders_of_reza() { psql_in tiffin_ordering "select count(*) from ordering.orders where \"CustomerName\" = 'Reza Karimi'"; }
BEFORE="$(orders_of_reza)"
IDEMPOTENCY_KEY="$KEY" place "$REZA" "$TEHRAN_RESTAURANT" DIZI 1 450000 tok_ok; expect 202 "reza orders, with a key"
S5_ORDER="$ORDER_ID"; check "${REPLAYED:-false}" false "a stored answer"
IDEMPOTENCY_KEY="$KEY" place "$REZA" "$TEHRAN_RESTAURANT" DIZI 1 450000 tok_ok; expect 202 "his app did not see the answer and sends it again"
check "$ORDER_ID" "$S5_ORDER" "the same order"; check "${REPLAYED:-false}" true "a stored answer"
IDEMPOTENCY_KEY="$KEY" place "$REZA" "$TEHRAN_RESTAURANT" DIZI 2 900000 tok_ok
expect 422 "the same key with another order"; check "$(code)" KEY_REUSED "refused as"
api POST "$ORDERING/v1/orders" "$REZA" "$(order_body "$TEHRAN_RESTAURANT" DIZI 1 450000 tok_ok)"
expect 400 "an order without a key"; check "$(code)" KEY_REQUIRED "refused as"
reaches "$S5_ORDER" "$REZA" Paid "the one order"
check "$(( $(orders_of_reza) - BEFORE ))" 1 "orders reza has more than before"
check "$(paylane authorizations "$S5_ORDER")" 1 "charges asked of the provider"
api POST "$ORDERING/v1/orders/$S5_ORDER/cancel" "$REZA" '{"note":"ordered by mistake"}'; expect 200 "reza cancels it, before the restaurant cooks"
fi

# ------------------------------------------------------------------------------------ S6
if wanted S6; then
section "S6  Two cities: what belongs to one does not exist for the other"
open_restaurant "$KEMAL" "Lokanta $RUN" TRY KOFTE 320
ISTANBUL_RESTAURANT="$RESTAURANT_ID"
api GET "$RESTAURANTS/v1/restaurants?size=200" "$ELIF"
check "$(jq -r '[.items[].city] | unique | join(" ")' <<<"$LAST")" istanbul "the cities elif sees"
api GET "$RESTAURANTS/v1/restaurants?city=tehran&size=200" "$ELIF"
check "$(jq -r '[.items[].city] | unique | join(" ")' <<<"$LAST")" istanbul "and when she asks for Tehran, with her token"
api GET "$RESTAURANTS/v1/restaurants/$TEHRAN_RESTAURANT/menu" "$ELIF"; expect 404 "elif reads the menu of a restaurant of Tehran"
api PUT "$RESTAURANTS/v1/restaurants/$TEHRAN_RESTAURANT/menu/DIZI" "$KEMAL" '{"name":"Dizi","price":1}'; expect 404 "kemal changes a price in Tehran"
LANG_HEADER=tr place "$ELIF" "$TEHRAN_RESTAURANT" DIZI 1 450000 tok_ok
expect 404 "elif orders from Tehran"; check "$(code)" RESTAURANT_NOT_FOUND "refused as"; say "$(jq -r .detail <<<"$LAST")"
api GET "$ORDERING/v1/orders/${S1_ORDER:-$TEHRAN_RESTAURANT}" "$ELIF"; expect 404 "elif reads an order of Tehran"
place "$ELIF" "$ISTANBUL_RESTAURANT" KOFTE 2 640 tok_ok; expect 202 "elif orders in Istanbul, in lira"
S6_ORDER="$ORDER_ID"
reaches "$S6_ORDER" "$ELIF" Paid "her card was charged"
if wait_ticket "$KEMAL" "$S6_ORDER" Pending; then ok "kemal has the order"; else bad "the ticket did not arrive"; fi
api GET "$KITCHEN/v1/kitchen/tickets?size=200" "$MINA"
check "$(jq -r --arg id "$S6_ORDER" '[.items[] | select(.orderId == $id)] | length' <<<"$LAST")" 0 "times mina sees it"
api POST "$KITCHEN/v1/kitchen/tickets/$S6_ORDER/accept" "$MINA" '{"readyInMinutes":15}'; expect 404 "mina accepts an order of Istanbul"
api POST "$KITCHEN/v1/kitchen/tickets/$S6_ORDER/accept" "$KEMAL" '{"readyInMinutes":15}'; expect 200 "kemal accepts"
say "A courier of Tehran is on duty. Istanbul has none."
reaches "$S6_ORDER" "$ELIF" Cancelled "the order"
check "$(jq -r .cancellationReason <<<"$LAST")" no-courier "why"
check "$(psql_in tiffin_dispatch "select count(*) from dispatch.deliveries where \"OrderId\" = '$S6_ORDER'")" 0 "deliveries given to a courier of another city"
for database in ordering payments kitchen; do
  check "$(psql_in tiffin_$database "select count(*) from audit.entries where \"TenantId\" = 'istanbul' and \"OccurredAtUtc\" > now() - interval '5 minutes'" | awk '{print ($1 > 0) ? "yes" : "no"}')" yes "the audit trail of $database names Istanbul"
done
fi

# ------------------------------------------------------------------------------------ S7
if wanted S7; then
section "S7  The customer changes their mind: until the restaurant cooks, and not after"
place "$SARA" "$TEHRAN_RESTAURANT" DIZI 1 450000 tok_ok; S7_ORDER="$ORDER_ID"
reaches "$S7_ORDER" "$SARA" Paid "sara's order"
wait_ticket "$MINA" "$S7_ORDER" Pending
api POST "$ORDERING/v1/orders/$S7_ORDER/cancel" "$REZA" '{}'; expect 404 "reza cancels sara's order"
api POST "$ORDERING/v1/orders/$S7_ORDER/cancel" "$SARA" '{"note":"changed my mind"}'; expect 200 "sara cancels"
check "$(jq -r .cancellationReason <<<"$LAST")" cancelled-by-customer "why"
if wait_ticket "$MINA" "$S7_ORDER" Cancelled; then ok "the Kitchen was told to stop"; else bad "the ticket is not cancelled"; fi
if refunded "$S7_ORDER" "$SARA"; then ok "the money went back"; else bad "the order was not refunded"; fi
place "$SARA" "$TEHRAN_RESTAURANT" DIZI 1 450000 tok_ok; S7_SECOND="$ORDER_ID"
reaches "$S7_SECOND" "$SARA" Paid "a second order"
wait_ticket "$MINA" "$S7_SECOND" Pending
api POST "$KITCHEN/v1/kitchen/tickets/$S7_SECOND/accept" "$MINA" '{"readyInMinutes":30}'
wait_for "$S7_SECOND" "$SARA" Accepted OutForDelivery Cancelled
LANG_HEADER=fa api POST "$ORDERING/v1/orders/$S7_SECOND/cancel" "$SARA" '{}'
expect 422 "sara cancels after the restaurant accepted"; say "$(jq -r .detail <<<"$LAST")"
if [ "$ORDER_STATUS" = OutForDelivery ] || wait_for "$S7_SECOND" "$SARA" OutForDelivery; then
  grpc "$DISPATCH" tiffin.dispatch.v1.Couriers/CompleteDelivery "$OMID" "{\"order_id\":\"$S7_SECOND\"}"
fi
fi

# ------------------------------------------------------------------------------------ S8
if wanted S8; then
section "S8  Six orders, one courier: one goes, five are taken back, none is lost"
on_duty "$OMID"
grpc "$DISPATCH" tiffin.dispatch.v1.Couriers/GetMyDelivery "$OMID"
[ "$GRPC_CODE" = OK ] && grpc "$DISPATCH" tiffin.dispatch.v1.Couriers/CompleteDelivery "$OMID" "{\"order_id\":\"$(jq -r .order_id <<<"$LAST")\"}"
RACE=()
for n in 1 2 3 4 5 6; do
  if [ $((n % 2)) -eq 1 ]; then who="$SARA"; else who="$REZA"; fi
  place "$who" "$TEHRAN_RESTAURANT" DOOGH 1 60000 tok_ok; RACE+=("$ORDER_ID:$who")
done
paid=0; for entry in "${RACE[@]}"; do wait_for "${entry%%:*}" "${entry#*:}" Paid && wait_ticket "$MINA" "${entry%%:*}" Pending && paid=$((paid + 1)); done
check "$paid" 6 "orders that are paid and wait in the Kitchen"
say "mina accepts all six at the same moment. One courier is on duty."
for entry in "${RACE[@]}"; do
  curl -s -o /dev/null -X POST "$KITCHEN/v1/kitchen/tickets/${entry%%:*}/accept" -H "Authorization: Bearer $MINA" -H 'Content-Type: application/json' -d '{"readyInMinutes":20}' &
done
wait
gone=0; back=0; carried=""; lost=""; whose=""
for entry in "${RACE[@]}"; do
  wait_for "${entry%%:*}" "${entry#*:}" OutForDelivery Cancelled
  case "$ORDER_STATUS" in
    OutForDelivery) gone=$((gone + 1)); carried="${entry%%:*}" ;;
    Cancelled) back=$((back + 1)); lost="${entry%%:*}"; whose="${entry#*:}" ;;
  esac
done
check "$gone" 1 "orders that went with the courier"
check "$back" 5 "orders that were cancelled"
check "$(psql_in tiffin_dispatch "select count(*) from dispatch.deliveries where \"Status\" = 'Assigned' and \"CourierId\" = (select \"CourierId\" from dispatch.couriers where \"Name\" = 'Omid Sadeghi')")" 1 "orders omid carries"
refunds=0; for entry in "${RACE[@]}"; do [ "${entry%%:*}" = "$carried" ] || { refunded "${entry%%:*}" "${entry#*:}" && refunds=$((refunds + 1)); }; done
check "$refunds" 5 "orders that were paid back"
say "How often two orders reached for the courier at the same moment is told by Dispatch's log: $(grep -c 'DbUpdateConcurrencyException' "$REPO_ROOT/tmp/logs/dispatch.log" 2>/dev/null | head -1) collision(s) so far."
grpc "$DISPATCH" tiffin.dispatch.v1.Couriers/CompleteDelivery "$OMID" "{\"order_id\":\"$lost\"}"
check "$GRPC_CODE" NotFound "omid completes an order he never carried"
grpc "$DISPATCH" tiffin.dispatch.v1.Couriers/CompleteDelivery "$OMID" "{\"order_id\":\"$carried\"}"; check "$GRPC_CODE" OK "omid hands over the one he carries"
LANG_HEADER=fa grpc "$DISPATCH" tiffin.dispatch.v1.Couriers/CompleteDelivery "$OMID" "{\"order_id\":\"$carried\"}"
check "$GRPC_CODE" FailedPrecondition "and hands it over again"; say "$(sed -n 's/^ *Message: *//p' <<<"$LAST" | head -1)"
fi

# ------------------------------------------------------------------------------------ S9
if wanted S9; then
section "S9  The payment provider does not answer: once, and then for good"
curl -s -X POST "$PAYLANE_URL/__admin/scenarios/reset" >/dev/null
place "$SARA" "$TEHRAN_RESTAURANT" DOOGH 1 60000 tok_flaky; expect 202 "sara orders; the provider answers 503 the first time"
S9_ORDER="$ORDER_ID"
reaches "$S9_ORDER" "$SARA" Paid "the client tried again, and the card was charged"
check "$(paylane authorizations "$S9_ORDER")" 2 "the provider was asked, times, under one key"
api POST "$ORDERING/v1/orders/$S9_ORDER/cancel" "$SARA" '{}'
DEAD_BEFORE="$(rabbit_dead)"
place "$SARA" "$TEHRAN_RESTAURANT" DOOGH 1 60000 tok_psp_down; expect 202 "sara orders; the provider is down"
S9_DOWN="$ORDER_ID"
say "The client gives up, the broker tries again three times with a pause, and then the request is given up."
WAIT_SECONDS=90 reaches "$S9_DOWN" "$SARA" Cancelled "the order was told, and does not wait for ever"
check "$(jq -r '.history[-1].note' <<<"$LAST")" PROVIDER_UNAVAILABLE "what it was told"
# A message from RabbitMQ that is given up goes to the broker's own dead-letter queue, not to a table.
# The broker counts its queues every few seconds, so the look is repeated until the count has moved.
deadline=$((SECONDS + 20)); until [ "$(( $(rabbit_dead) - DEAD_BEFORE ))" -ge 1 ] || [ $SECONDS -ge $deadline ]; do sleep 1; done
check "$(( $(rabbit_dead) - DEAD_BEFORE ))" 1 "requests that wait in the broker's dead-letter queue, more than before"
fi

# ------------------------------------------------------------------------------------ S10
if wanted S10; then
section "S10 Roles and cities: decided at Access, kept by the identity provider, felt everywhere"
ACCESS="http://localhost:$(rest_port_of access)"
ALI=$(token_for ali); DENIZ=$(token_for deniz); NORA=$(token_for nora); SARA10=$(token_for sara); OMID10=$(token_for omid)
applied() { # applied TOKEN GRANT_ID [CITY]  → the decision reached the identity provider
  local deadline=$((SECONDS + WAIT_SECONDS))
  while [ $SECONDS -lt $deadline ]; do
    api GET "$ACCESS/v1/access/grants/$2${3:+?city=$3}" "$1"; [ "$(jq -r .state <<<"$LAST")" = Applied ] && return 0; sleep 0.5
  done
  return 1
}
api GET "$ACCESS/v1/access/people" "$SARA10"; expect 403 "sara, a customer, asks who lives in Tehran"
api GET "$ACCESS/v1/access/people?size=50" "$ALI"; expect 200 "ali, an admin of Tehran, asks the same"
check "$(jq -r '[.items[].city] | unique | join(" ")' <<<"$LAST")" tehran "the cities of the people he sees"
REZA_ID=$(jq -r '.items[] | select(.userName == "reza") | .personId' <<<"$LAST")
ALI_ID=$(jq -r '.items[] | select(.userName == "ali") | .personId' <<<"$LAST")
check "$(jq -r '.items[] | select(.userName == "reza") | .roles | join(" ")' <<<"$LAST")" customer "reza's roles"
api GET "$ACCESS/v1/access/people?city=istanbul&size=50" "$ALI"
check "$(jq -r '[.items[].city] | unique | join(" ")' <<<"$LAST")" tehran "and when he asks for Istanbul"
api GET "$ACCESS/v1/access/people?size=50" "$DENIZ"; ELIF_ID=$(jq -r '.items[] | select(.userName == "elif") | .personId' <<<"$LAST")
api GET "$ACCESS/v1/access/people/$ELIF_ID" "$ALI"; expect 404 "ali looks at elif, of Istanbul"
api PUT "$ACCESS/v1/access/people/$ELIF_ID/roles/courier" "$ALI"; expect 404 "ali makes elif a courier"

say "reza wants to carry orders."
REZA10=$(token_for reza)
grpc "$DISPATCH" tiffin.dispatch.v1.Couriers/GoOnDuty "$REZA10"; check "$GRPC_CODE" PermissionDenied "reza goes on duty, as a customer"
api PUT "$ACCESS/v1/access/people/$REZA_ID/roles/courier" "$ALI"; expect 202 "ali makes reza a courier: decided, and the identity provider is told afterwards"
GRANT=$(jq -r .grantId <<<"$LAST")
if applied "$ALI" "$GRANT"; then ok "the identity provider has it"; else bad "the decision was not applied"; show; fi
grpc "$DISPATCH" tiffin.dispatch.v1.Couriers/GoOnDuty "$REZA10"; check "$GRPC_CODE" PermissionDenied "reza's old token: it says what was true when it was issued"
REZA10=$(token_for reza)
check "$(claim "$REZA10" '.realm_access.roles | map(select(. == "courier")) | length')" 1 "reza signs in again: the role is in his token"
grpc "$DISPATCH" tiffin.dispatch.v1.Couriers/GoOnDuty "$REZA10"; check "$GRPC_CODE" OK "reza goes on duty"
grpc "$DISPATCH" tiffin.dispatch.v1.Couriers/GoOffDuty "$REZA10"
api PUT "$ACCESS/v1/access/people/$REZA_ID/roles/courier" "$ALI"; expect 202 "ali gives the role a second time"
if applied "$ALI" "$(jq -r .grantId <<<"$LAST")"; then ok "to give what somebody has changes nothing, and fails nothing"; else bad "the repeated decision was not applied"; fi
api DELETE "$ACCESS/v1/access/people/$REZA_ID/roles/courier" "$ALI"; expect 202 "ali takes the role away"
if applied "$ALI" "$(jq -r .grantId <<<"$LAST")"; then ok "the identity provider has it"; else bad "the decision was not applied"; fi
check "$(claim "$(token_for reza)" '.realm_access.roles | map(select(. == "courier")) | length')" 0 "reza signs in again: times the role is in his token"

say "What is not an admin's to give."
LANG_HEADER=fa api PUT "$ACCESS/v1/access/people/$REZA_ID/roles/city-admin" "$ALI"
expect 422 "ali makes reza an admin of the city"; check "$(code)" ROLE_NOT_YOURS_TO_GIVE "rule A2"; say "$(jq -r .detail <<<"$LAST")"
api PUT "$ACCESS/v1/access/people/$ALI_ID/roles/courier" "$ALI"; expect 422 "ali gives himself a role"; check "$(code)" OWN_ROLES "rule A3"
api PUT "$ACCESS/v1/access/people/$REZA_ID/roles/platform-admin" "$ALI"; expect 422 "ali makes reza an admin of the platform"; check "$(code)" ROLE_UNKNOWN "rule A1"
api PUT "$ACCESS/v1/access/people/$REZA_ID/roles/city-admin" "$NORA"; expect 400 "nora, an admin of the platform, names no city"
api PUT "$ACCESS/v1/access/people/$REZA_ID/roles/city-admin?city=istanbul" "$NORA"; expect 404 "nora looks for reza in Istanbul"
api PUT "$ACCESS/v1/access/people/$REZA_ID/roles/city-admin?city=tehran" "$NORA"; expect 202 "nora makes reza an admin of Tehran"
NORA_GRANT=$(jq -r .grantId <<<"$LAST")
if applied "$NORA" "$NORA_GRANT" tehran; then ok "the identity provider has it"; else bad "the decision was not applied"; fi
api DELETE "$ACCESS/v1/access/people/$REZA_ID/roles/city-admin?city=tehran" "$NORA"; expect 202 "and takes it away again"
applied "$NORA" "$(jq -r .grantId <<<"$LAST")" tehran
check "$(psql_in tiffin_access "select \"ActorUserName\" || ' decided, ' || (\"Metadata\"::jsonb ->> 'role') || ' in ' || (\"Metadata\"::jsonb ->> 'city') from audit.entries where \"Action\" = 'role-granted' order by \"Id\" desc limit 1")" \
  "nora decided, city-admin in tehran" "the audit trail"
check "$(psql_in tiffin_access "select count(*) from audit.entries where \"EntityType\" = 'Grant' and \"ActorKind\" = 3 and \"TenantId\" = 'tehran' and \"ActorUserName\" = 'ApplyGrant'" | awk '{print ($1 > 0) ? "yes" : "no"}')" yes "the step that told the identity provider is in the trail, with its city"
fi

# ------------------------------------------------------------------------------------ S11
if wanted S11; then
section "S11 A picture: announced to Media, sent to the store, known everywhere by its identifier"
MEDIA="http://localhost:$(rest_port_of media)"
say "The store behind Media: $(env_value MEDIA_STORE rustfs). Media speaks the S3 API to it, and its code does not know which one it is."
PICTURE=$(mktemp); trap 'rm -f "$PICTURE" "$PICTURE.back"' EXIT
# The smallest picture there is: one pixel, as PNG.
base64 -d > "$PICTURE" <<<'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=='
SIZE=$(wc -c < "$PICTURE" | tr -d ' ')
announce() { # announce TOKEN PURPOSE TYPE SIZE  → sets MEDIA_ID and UPLOAD_URL
  api POST "$MEDIA/v1/media/uploads" "$1" "$(jq -nc --arg p "$2" --arg t "$3" --argjson s "$4" '{purpose:$p, fileName:"front.png", contentType:$t, size:$s}')"
  MEDIA_ID=$(jq -r '.mediaId // empty' <<<"$LAST"); UPLOAD_URL=$(jq -r '.uploadUrl // empty' <<<"$LAST")
}
send() { curl -s -m 20 -o /dev/null -w '%{http_code}' -X PUT "$1" -H "Content-Type: $2" --data-binary "@$PICTURE"; } # send ADDRESS TYPE

announce "$MINA" restaurant-picture image/png "$SIZE"; expect 201 "mina announces a picture of $SIZE bytes"
check "$(sed -E 's#^(https?://[^/]+)/.*#\1#' <<<"$UPLOAD_URL")" "http://localhost:$(env_value MEDIA_S3_PORT 39000)" "the address she is given is the store's, not Media's"
api POST "$MEDIA/v1/media/$MEDIA_ID/confirm" "$MINA"; expect 422 "she confirms before she has sent anything"; check "$(code)" NOTHING_ARRIVED "refused as"
check "$(send "$UPLOAD_URL" text/plain)" 403 "she sends it as another type than the address was signed for: the store answers"
check "$(send "$UPLOAD_URL" image/png)" 200 "she sends the bytes to the store: the store answers"
api POST "$MEDIA/v1/media/$MEDIA_ID/confirm" "$MINA"; expect 200 "she confirms: Media asks the store what arrived"
check "$(jq -r .state <<<"$LAST")" Available "the file"
S11_PICTURE="$MEDIA_ID"
api POST "$MEDIA/v1/media/$MEDIA_ID/confirm" "$MINA"; expect 422 "she confirms again"; check "$(code)" FILE_NOT_PENDING "rule M4"

api PUT "$RESTAURANTS/v1/restaurants/$TEHRAN_RESTAURANT/picture" "$MINA" "{\"pictureId\":\"$S11_PICTURE\"}"; expect 200 "mina shows it at her restaurant"
api GET "$RESTAURANTS/v1/restaurants/$TEHRAN_RESTAURANT/menu?city=tehran"
check "$(jq -r .pictureId <<<"$LAST")" "$S11_PICTURE" "what the menu holds of the picture: its identifier"
api GET "$MEDIA/v1/media/$S11_PICTURE" "$SARA"; expect 200 "sara, of the same city, asks Media for it"
DOWNLOAD_URL=$(jq -r .downloadUrl <<<"$LAST")
check "$(curl -s -m 20 -o "$PICTURE.back" -w '%{http_code}' "$DOWNLOAD_URL")" 200 "she fetches the bytes from the store, over an address good for five minutes"
check "$(shasum -a 256 < "$PICTURE.back" | cut -c1-16)" "$(shasum -a 256 < "$PICTURE" | cut -c1-16)" "what came back is what was sent"
api GET "$MEDIA/v1/media/$S11_PICTURE" "$ELIF"; expect 404 "elif, of Istanbul, asks for it"
api GET "$MEDIA/v1/media/$S11_PICTURE"; expect 401 "and somebody who is not signed in"
api DELETE "$MEDIA/v1/media/$S11_PICTURE" "$SARA"; expect 403 "sara deletes mina's picture"; check "$(code)" NOT_THE_OWNER "refused as"

say "What a file may be is decided by what it is for."
LANG_HEADER=fa announce "$MINA" restaurant-picture application/pdf 2000; expect 422 "a PDF as the picture of a restaurant"; check "$(code)" TYPE_NOT_ALLOWED "rule M2"; say "$(jq -r .detail <<<"$LAST")"
announce "$MINA" restaurant-picture image/png 6000000; expect 422 "a picture of six megabytes"; check "$(code)" SIZE_NOT_ALLOWED "rule M3"
announce "$MINA" holiday-video video/mp4 2000; expect 422 "a holiday video"; check "$(code)" PURPOSE_UNKNOWN "rule M1"
announce "$MINA" restaurant-picture image/png 10; expect 201 "mina announces ten bytes"
check "$(send "$UPLOAD_URL" image/png)" 200 "and sends $SIZE"
LANG_HEADER=tr api POST "$MEDIA/v1/media/$MEDIA_ID/confirm" "$MINA"; expect 422 "she confirms"; check "$(code)" NOT_WHAT_WAS_ANNOUNCED "rule M5"; say "$(jq -r .detail <<<"$LAST")"

api DELETE "$MEDIA/v1/media/$S11_PICTURE" "$MINA"; expect 200 "mina deletes her picture"
api GET "$MEDIA/v1/media/$S11_PICTURE" "$MINA"; expect 404 "and asks for it"
check "$(curl -s -m 20 -o /dev/null -w '%{http_code}' "$DOWNLOAD_URL")" 404 "the address sara was given, which has not expired yet: the store answers"
check "$(psql_in tiffin_media "select \"ActorUserName\" || ' in ' || \"TenantId\" from audit.entries where \"Action\" = 'file-deleted' order by \"Id\" desc limit 1")" "mina in tehran" "who deleted, and where"
api PUT "$RESTAURANTS/v1/restaurants/$TEHRAN_RESTAURANT/picture" "$MINA" '{"pictureId":null}'
fi

# ------------------------------------------------------------------------------------ S12
if wanted S12; then
section "S12 Where is my order: said by the courier, kept as a time series, shown to who waits"
TRACKING="http://localhost:$(rest_port_of tracking)"
free_courier "$OMID"
place "$SARA" "$TEHRAN_RESTAURANT" DIZI 1 450000 tok_ok; S12_ORDER="$ORDER_ID"
reaches "$S12_ORDER" "$SARA" Paid "sara's order"
wait_ticket "$MINA" "$S12_ORDER" Pending
api POST "$KITCHEN/v1/kitchen/tickets/$S12_ORDER/accept" "$MINA" '{"readyInMinutes":15}'
reaches "$S12_ORDER" "$SARA" OutForDelivery "omid carries it"
followed() { # the delivery has reached Tracking, from the event stream
  local deadline=$((SECONDS + WAIT_SECONDS))
  until api GET "$TRACKING/v1/tracking/deliveries/$S12_ORDER" "$SARA"; [ "$STATUS" = 200 ] || [ $SECONDS -ge $deadline ]; do sleep 0.5; done
  [ "$STATUS" = 200 ]
}
if followed; then ok "Tracking heard of the delivery from the event stream of Dispatch"; else bad "Tracking does not know the delivery"; fi
check "$(jq -r '.status + ", seen " + (.positionCount | tostring) + " times"' <<<"$LAST")" "UnderWay, seen 0 times" "before omid says anything"
for place in "35.7575 51.4100" "35.7601 51.4089" "35.7632 51.4071"; do
  api POST "$TRACKING/v1/tracking/deliveries/$S12_ORDER/positions" "$OMID" "$(jq -nc --argjson a "${place% *}" --argjson o "${place#* }" '{latitude:$a, longitude:$o}')"
  expect 204 "omid's app says where he is"
done
api GET "$TRACKING/v1/tracking/deliveries/$S12_ORDER" "$SARA"; expect 200 "sara asks where her order is"
check "$(jq -r '"\(.lastSeen.latitude) \(.lastSeen.longitude)"' <<<"$LAST")" "35.7632 51.4071" "where it was seen last"
check "$(jq -r '[.trail[].latitude] | map(tostring) | join(" ")' <<<"$LAST")" "35.7632 35.7601 35.7575" "the way it came, newest first"
api GET "$TRACKING/v1/tracking/deliveries/$S12_ORDER?points=1" "$OMID"; expect 200 "omid, who carries it, asks too"
check "$(jq -r '.trail | length' <<<"$LAST")" 1 "places he asked for"
api GET "$TRACKING/v1/tracking/deliveries/$S12_ORDER" "$REZA"; expect 404 "reza asks where sara's order is"
api GET "$TRACKING/v1/tracking/deliveries/$S12_ORDER" "$ELIF"; expect 404 "elif, of Istanbul"
api GET "$TRACKING/v1/tracking/deliveries/$S12_ORDER" "$MINA"; expect 403 "mina, who cooked it"
api POST "$TRACKING/v1/tracking/deliveries/$S12_ORDER/positions" "$SARA" '{"latitude":35.7,"longitude":51.4}'; expect 403 "sara says where the courier is"
LANG_HEADER=fa api POST "$TRACKING/v1/tracking/deliveries/$S12_ORDER/positions" "$OMID" '{"latitude":135.7,"longitude":51.4}'
expect 422 "omid's app says he is at latitude 135"; check "$(code)" POSITION_NOT_ON_EARTH "rule T3"
grpc "$DISPATCH" tiffin.dispatch.v1.Couriers/CompleteDelivery "$OMID" "{\"order_id\":\"$S12_ORDER\"}"; check "$GRPC_CODE" OK "omid hands the order over"
arrived() {
  local deadline=$((SECONDS + WAIT_SECONDS))
  until api GET "$TRACKING/v1/tracking/deliveries/$S12_ORDER" "$SARA"; [ "$(jq -r .status <<<"$LAST")" = Arrived ] || [ $SECONDS -ge $deadline ]; do sleep 0.5; done
  [ "$(jq -r .status <<<"$LAST")" = Arrived ]
}
if arrived; then ok "Tracking heard that it arrived"; else bad "Tracking still follows the delivery"; fi
api POST "$TRACKING/v1/tracking/deliveries/$S12_ORDER/positions" "$OMID" '{"latitude":35.77,"longitude":51.40}'
expect 422 "omid's app keeps sending"; check "$(code)" DELIVERY_HAS_ARRIVED "rule T2: where he goes now is his own"
check "$(psql_in tiffin_tracking "select count(*) from tracking.positions where order_id = '$S12_ORDER'")" 3 "rows of the time series"
check "$(psql_in tiffin_tracking "select count(*) from timescaledb_information.hypertables where hypertable_name = 'positions' and compression_enabled")" 1 "the table is a hypertable, compressed when it is old"
check "$(psql_in tiffin_tracking "select string_agg(proc_name, ' ' order by proc_name) from timescaledb_information.jobs where hypertable_name = 'positions'")" "policy_compression policy_retention" "what TimescaleDB does to it by itself"
fi

# ------------------------------------------------------------------------------------ S13
if wanted S13; then
section "S13 What happened, told in the reader's language: a service that only reads the stream"
NOTIFICATIONS="http://localhost:$(rest_port_of notifications)"
free_courier "$OMID"
place "$SARA" "$TEHRAN_RESTAURANT" DIZI 1 450000 tok_ok; S13_ORDER="$ORDER_ID"
reaches "$S13_ORDER" "$SARA" Paid "sara's order"
NUMBER=$(jq -r .orderNumber <<<"$LAST")
wait_ticket "$MINA" "$S13_ORDER" Pending
api POST "$KITCHEN/v1/kitchen/tickets/$S13_ORDER/accept" "$MINA" '{"readyInMinutes":15}'
reaches "$S13_ORDER" "$SARA" OutForDelivery "omid carries it"
grpc "$DISPATCH" tiffin.dispatch.v1.Couriers/CompleteDelivery "$OMID" "{\"order_id\":\"$S13_ORDER\"}"
reaches "$S13_ORDER" "$SARA" Delivered "and hands it over"
told() { # told TOKEN ORDER_ID COUNT  → the customer has this many notifications about the order
  local deadline=$((SECONDS + WAIT_SECONDS))
  while [ $SECONDS -lt $deadline ]; do
    api GET "$NOTIFICATIONS/v1/notifications?size=200" "$1"
    [ "$(jq -r --arg id "$2" '[.items[]? | select(.orderId == $id)] | length' <<<"$LAST")" = "$3" ] && return 0
    sleep 0.5
  done
  return 1
}
about() { jq -r --arg id "$S13_ORDER" --arg k "$1" '.items[] | select(.orderId == $id and .messageKey == $k) | .text' <<<"$LAST"; }
if told "$SARA" "$S13_ORDER" 3; then ok "sara was told three things about her order"; else bad "sara has $(jq -r --arg id "$S13_ORDER" '[.items[]? | select(.orderId == $id)] | length' <<<"$LAST") notification(s) about it, expected 3"; fi
check "$(about notifications.order_out_for_delivery)" "Omid Sadeghi is on the way with your order $NUMBER." "in English, which she did not ask for and is told by default"
LANG_HEADER=fa api GET "$NOTIFICATIONS/v1/notifications?size=200" "$SARA"
check "$(about notifications.order_out_for_delivery)" "Omid Sadeghi با سفارش $NUMBER شما در راه است." "the same notification, asked for in Persian"
LANG_HEADER="de, tr;q=0.8, en;q=0.5" api GET "$NOTIFICATIONS/v1/notifications?size=200" "$SARA"
check "$(about notifications.order_delivered)" "$NUMBER numaralı siparişiniz teslim edildi. Afiyet olsun." "asked for in German, then Turkish, then English: the first the service has"
FIRST_ID=$(jq -r --arg id "$S13_ORDER" '[.items[] | select(.orderId == $id)][0].notificationId' <<<"$LAST")
api GET "$NOTIFICATIONS/v1/notifications?unread=true&size=200" "$SARA"; UNREAD=$(jq -r .total <<<"$LAST")
api POST "$NOTIFICATIONS/v1/notifications/$FIRST_ID/read" "$REZA"; expect 404 "reza marks sara's notification as read"
api POST "$NOTIFICATIONS/v1/notifications/$FIRST_ID/read" "$SARA"; expect 200 "sara marks it as read"
api GET "$NOTIFICATIONS/v1/notifications?unread=true&size=200" "$SARA"
check "$(( UNREAD - $(jq -r .total <<<"$LAST") ))" 1 "unread notifications fewer than before"
api GET "$NOTIFICATIONS/v1/notifications?size=200" "$REZA"
check "$(jq -r --arg id "$S13_ORDER" '[.items[]? | select(.orderId == $id)] | length' <<<"$LAST")" 0 "what reza is told about sara's order"
api GET "$NOTIFICATIONS/v1/notifications"; expect 401 "somebody who is not signed in"

place "$SARA" "$TEHRAN_RESTAURANT" DIZI 1 450000 tok_insufficient_funds; S13_DECLINED="$ORDER_ID"
reaches "$S13_DECLINED" "$SARA" Cancelled "an order the bank refuses"
S13_ORDER="$S13_DECLINED"
if told "$SARA" "$S13_DECLINED" 2; then ok "sara was told that it was received, and that it was cancelled"; else bad "sara was not told of the cancellation"; fi
LANG_HEADER=fa api GET "$NOTIFICATIONS/v1/notifications?size=200" "$SARA"
say "$(about notifications.order_cancelled.payment-declined)"
check "$(about notifications.order_cancelled.payment-declined | grep -c 'مبلغی کسر نشده است')" 1 "the cancellation says why, and that nothing was charged"
check "$(psql_in tiffin_notifications "select count(*) from idempotency.processed_messages" | awk '{print ($1 > 0) ? "yes" : "no"}')" yes "the inbox remembers what was read from the stream"
fi

# ------------------------------------------------------------------------------------ S14
if wanted S14; then
section "S14 A service is down: who asks is told, who was asked later waits, and nothing is lost"
stop_service() { pkill -f "Tiffin\.$(pascal "$1")\.Api" 2>/dev/null; local deadline=$((SECONDS + 20)); while is_ready "$1" && [ $SECONDS -lt $deadline ]; do sleep 0.5; done; ! is_ready "$1"; }
start_service() { # started as scripts/run.sh starts it, without building again, with the TIFFIN_BIND and DOTNET_ARGS the services were started with
  mkdir -p "$REPO_ROOT/tmp/logs"
  TIFFIN_NO_BUILD=1 nohup "$REPO_ROOT/scripts/run.sh" "$1" >> "$REPO_ROOT/tmp/logs/$1.log" 2>&1 &
  local deadline=$((SECONDS + 90)); until is_ready "$1" || [ $SECONDS -ge $deadline ]; do sleep 1; done; is_ready "$1"
}
orders_in_tehran() { psql_in tiffin_ordering "select count(*) from ordering.orders where \"City\" = 'tehran'"; }
free_courier "$OMID"

say "Restaurants is asked while the customer waits. It is stopped."
if stop_service restaurants; then ok "Restaurants is down"; else bad "Restaurants could not be stopped"; fi
BEFORE="$(orders_in_tehran)"; PAYMENTS_BEFORE="$(psql_in tiffin_payments 'select count(*) from payments.payments')"
LANG_HEADER=fa place "$SARA" "$TEHRAN_RESTAURANT" DIZI 1 450000 tok_ok
expect 503 "sara orders"; check "$(code)" RESTAURANTS_UNAVAILABLE "she is told"; say "$(jq -r .detail <<<"$LAST")"
check "$(( $(orders_in_tehran) - BEFORE ))" 0 "orders that were stored"
check "$(( $(psql_in tiffin_payments 'select count(*) from payments.payments') - PAYMENTS_BEFORE ))" 0 "cards that were handed to Payments"
api GET "$ORDERING/v1/orders?size=1" "$SARA"; expect 200 "sara reads her orders meanwhile: Ordering needs nobody for that"
if start_service restaurants; then ok "Restaurants is up again"; else bad "Restaurants did not come back"; fi

say "The Kitchen is asked afterwards, by a message. It is stopped before sara orders."
stop_service kitchen && ok "the Kitchen is down" || bad "the Kitchen could not be stopped"
place "$SARA" "$TEHRAN_RESTAURANT" DIZI 1 450000 tok_ok; expect 202 "sara orders again: accepted, with Restaurants back and the Kitchen down"
S14_ORDER="$ORDER_ID"
reaches "$S14_ORDER" "$SARA" Paid "the order is paid all the same: Payments needs no Kitchen"
say "In an ordinary run the Kitchen has an order within a second of its payment. Now it is down for six."
sleep 6
api GET "$ORDERING/v1/orders/$S14_ORDER" "$SARA"
check "$(jq -r .status <<<"$LAST")" Paid "the order, meanwhile"
PAID_AT=$(jq -r '.history[] | select(.status == "Paid") | .occurredOnUtc' <<<"$LAST")
if start_service kitchen; then ok "the Kitchen is up again"; else bad "the Kitchen did not come back"; fi
if wait_ticket "$MINA" "$S14_ORDER" Pending; then ok "the Kitchen has the order now"; else bad "the ticket did not arrive"; fi
RECEIVED_AT=$(jq -r --arg id "$S14_ORDER" '.items[] | select(.orderId == $id) | .receivedOnUtc' <<<"$LAST")
# The request was written when the order was paid, and read when the Kitchen was back: it waited in between.
WAITED=$(jq -n --arg a "$PAID_AT" --arg b "$RECEIVED_AT" '[$a, $b] | map(sub("\\.[0-9]+"; "") | sub("\\+00:00$"; "Z") | fromdateiso8601) | .[1] - .[0]')
check "$([ "${WAITED:-0}" -ge 6 ] && echo "six seconds or more" || echo "${WAITED:-?} seconds")" "six seconds or more" "how long the request waited in its queue"
api POST "$KITCHEN/v1/kitchen/tickets/$S14_ORDER/accept" "$MINA" '{"readyInMinutes":20}'; expect 200 "mina accepts"

say "Notifications reads the stream. It is stopped before the order is delivered."
wait_for "$S14_ORDER" "$SARA" OutForDelivery
grpc "$DISPATCH" tiffin.dispatch.v1.Couriers/CompleteDelivery "$OMID" "{\"order_id\":\"$S14_ORDER\"}"; check "$GRPC_CODE" OK "omid hands the order over"
stop_service notifications && ok "Notifications is down" || bad "Notifications could not be stopped"
reaches "$S14_ORDER" "$SARA" Delivered "the order is delivered all the same: nobody waits for a reader of the stream"
if start_service notifications; then ok "Notifications is up again"; else bad "Notifications did not come back"; fi
NOTIFICATIONS="http://localhost:$(rest_port_of notifications)"
caught_up() {
  local deadline=$((SECONDS + WAIT_SECONDS))
  while [ $SECONDS -lt $deadline ]; do
    api GET "$NOTIFICATIONS/v1/notifications?size=200" "$SARA"
    [ "$(jq -r --arg id "$S14_ORDER" '[.items[]? | select(.orderId == $id and .messageKey == "notifications.order_delivered")] | length' <<<"$LAST")" = 1 ] && return 0
    sleep 0.5
  done
  return 1
}
if caught_up; then ok "it reads what it missed from where it had stopped, and sara is told"; else bad "sara was not told that her order was delivered"; fi
fi

# ------------------------------------------------------------------------------------ summary
section "Summary"
printf '   %s%d passed%s, %s%d failed%s, %d skipped\n' "$G" "$passed" "$N" "$([ $failed -gt 0 ] && echo "$R")" "$failed" "$N" "$skipped"
[ "$failed" -eq 0 ]
