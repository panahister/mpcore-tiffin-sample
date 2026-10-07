#!/usr/bin/env bash
# Shared settings for the scripts. Sourced, never run.
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
INFRA_DIR="$REPO_ROOT/infrastructure"
APISIX_PRODUCT_DIR="${TIFFIN_APISIX_PRODUCT_DIR:-$REPO_ROOT/../tiffin-apisix/product/tiffin-local}"
ENV_FILE="$INFRA_DIR/.env"
ENV_EXAMPLE="$INFRA_DIR/.env.example"

# The services: name, REST port, gRPC port ("-" when it has none), the server and the name of its
# database, and its broker. One line each; everything else in the scripts reads this table.
SERVICES=(access media restaurants ordering payments kitchen dispatch tracking notifications)
service_row() {
  case "$1" in
    access)        echo "6100 -    postgres  tiffin_access        kafka" ;;
    media)         echo "6200 -    postgres  tiffin_media         kafka" ;;
    restaurants)   echo "6300 -    postgres  tiffin_restaurants   kafka" ;;
    ordering)      echo "6400 6401 postgres  tiffin_ordering      kafka" ;;
    payments)      echo "-    6501 postgres  tiffin_payments      rabbitmq" ;;
    kitchen)       echo "6600 -    postgres  tiffin_kitchen       rabbitmq" ;;
    dispatch)      echo "-    6701 postgres  tiffin_dispatch      rabbitmq" ;;
    tracking)      echo "6800 -    timescale tiffin_tracking      kafka" ;;
    notifications) echo "6900 -    postgres  tiffin_notifications kafka" ;;
    *) echo "unknown service: $1 (one of: ${SERVICES[*]})" >&2; return 2 ;;
  esac
}
field() { service_row "$1" | awk -v n="$2" '{print $n}'; }   # field SERVICE 1..5
rest_port_of() { field "$1" 1; }
grpc_port_of() { field "$1" 2; }
pascal() { printf '%s' "$1" | awk '{print toupper(substr($0,1,1)) substr($0,2)}'; }
project_of() { service_row "$1" >/dev/null || return 2; echo "$REPO_ROOT/$1/src/Tiffin.$(pascal "$1").Api"; }
solution_of() { echo "$REPO_ROOT/$1/Tiffin.$(pascal "$1").Backend.sln"; }

# Is the service ready? REST answers /health/ready; a service with gRPC only answers the health service.
is_ready() {
  local rest grpc; rest="$(rest_port_of "$1")"; grpc="$(grpc_port_of "$1")"
  if [ "$rest" != "-" ]; then curl -sf "http://localhost:$rest/health/ready" >/dev/null
  else grpcurl -plaintext "localhost:$grpc" grpc.health.v1.Health/Check 2>/dev/null | grep -q SERVING; fi
}

ensure_env() {
  if [ ! -f "$ENV_FILE" ]; then
    cp "$ENV_EXAMPLE" "$ENV_FILE"
    echo "created infrastructure/.env from .env.example"
  fi
}

# Reads a variable from infrastructure/.env, falling back to .env.example, then to a default.
env_value() {
  local key="$1" default="$2" value=""
  # What the caller's environment says wins: EDGE_AUTH=keycloak scripts/up.sh
  if [ -n "${!key:-}" ]; then printf '%s' "${!key}"; return; fi
  for f in "$ENV_FILE" "$ENV_EXAMPLE"; do
    [ -f "$f" ] || continue
    value=$(grep -E "^${key}=" "$f" | tail -1 | cut -d= -f2-)
    [ -n "$value" ] && { printf '%s' "$value"; return; }
  done
  printf '%s' "$default"
}

compose() { docker compose --env-file "$ENV_FILE" -f "$INFRA_DIR/compose.yaml" "$@"; }

# The edge needs a certificate. One is made for this machine, for the name "localhost", and kept outside
# the repository's history (infrastructure/apisix/generated is ignored). It is trusted by nobody: a caller
# names it explicitly (curl --cacert), which is what the scenarios do.
GATEWAY_DIR="$INFRA_DIR/apisix/generated"
GATEWAY_CA="$GATEWAY_DIR/localhost.crt"
ensure_gateway_config() {
  mkdir -p "$GATEWAY_DIR"
  [ -f "$APISIX_PRODUCT_DIR/apisix.template.yaml" ] || {
    echo "Tiffin APISIX source was not found at $APISIX_PRODUCT_DIR" >&2
    echo "Clone https://github.com/panahister/tiffin-apisix next to this repository, or set TIFFIN_APISIX_PRODUCT_DIR." >&2
    return 2
  }
  if [ ! -s "$GATEWAY_CA" ] || [ ! -s "$GATEWAY_DIR/localhost.key" ]; then
    openssl req -x509 -newkey rsa:2048 -nodes -days 825 -subj "/CN=localhost" \
      -addext "subjectAltName=DNS:localhost,IP:127.0.0.1" \
      -keyout "$GATEWAY_DIR/localhost.key" -out "$GATEWAY_CA" >/dev/null 2>&1
    echo "made a certificate for the edge: infrastructure/apisix/generated/localhost.crt"
  fi
  local auth; auth="$(env_value EDGE_AUTH off)"
  [ -f "$APISIX_PRODUCT_DIR/edge-auth.$auth.yaml" ] || { echo "EDGE_AUTH=$auth: there is no product/tiffin-local/edge-auth.$auth.yaml (off, keycloak)" >&2; return 2; }
  awk -v cert="$GATEWAY_CA" -v key="$GATEWAY_DIR/localhost.key" -v auth="$APISIX_PRODUCT_DIR/edge-auth.$auth.yaml" '
    function paste(file, margin,   line) { while ((getline line < file) > 0) if (line !~ /^#/) print margin line; close(file) }
    /^__CERTIFICATE__$/ { paste(cert, "      "); next }
    /^__KEY__$/         { paste(key, "      "); next }
    /^__EDGE_AUTH__$/   { paste(auth, "      "); next }
    { print }' "$APISIX_PRODUCT_DIR/apisix.template.yaml" > "$GATEWAY_DIR/apisix.yaml"
  echo "the edge: EDGE_AUTH=$auth"
}
