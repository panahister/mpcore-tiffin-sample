#!/usr/bin/env bash
# Starts everything the services depend on, in Docker. The services themselves are NOT containerised:
# run them from your IDE or with scripts/run.sh.
#
#   scripts/up.sh                  # dependencies only: what the scenarios need
#   scripts/up.sh --observability  # and tracing, metrics, dashboards and the Kafka browser
set -euo pipefail
source "$(dirname "$0")/lib.sh"

ensure_env
ensure_gateway_config
store="$(env_value MEDIA_STORE rustfs)"
case "$store" in rustfs|seaweedfs) ;; *) echo "MEDIA_STORE=$store: rustfs or seaweedfs" >&2; exit 2 ;; esac
# Both stores listen on the same port: the one that was not chosen is stopped first.
for other in rustfs seaweedfs; do [ "$other" = "$store" ] || compose --profile "media-$other" stop "$other" >/dev/null 2>&1 || true; done
profiles=(--profile "media-$store")
[ "${1:-}" = "--observability" ] && profiles+=(--profile observability --profile tools)
echo "the store of Media: MEDIA_STORE=$store"
echo "== starting the dependencies"
compose ${profiles[@]+"${profiles[@]}"} up -d --wait --wait-timeout 300

cat <<INFO

The dependencies are up.
  PostgreSQL   localhost:$(env_value POSTGRES_PORT 35432)   one database per service
  TimescaleDB  localhost:$(env_value TIMESCALE_PORT 35433)   database tiffin_tracking
  Kafka        localhost:$(env_value KAFKA_PORT 39092)
  RabbitMQ     localhost:$(env_value RABBITMQ_PORT 35672)   management http://localhost:$(env_value RABBITMQ_UI_PORT 35673)
  Redis        localhost:$(env_value REDIS_PORT 36379)
  Edge         https://localhost:$(env_value GATEWAY_HTTPS_PORT 39443)   Apache APISIX, over TLS
  Keycloak     http://localhost:$(env_value KEYCLOAK_PORT 38180)   realm "tiffin"
  Media store  http://localhost:$(env_value MEDIA_S3_PORT 39000)   $store, the S3 API
  PayLane      http://localhost:$(env_value WIREMOCK_PORT 38081)/__admin/requests   (WireMock)

Next: scripts/setup.sh (once), then scripts/run.sh all, then scripts/scenarios.sh
INFO
