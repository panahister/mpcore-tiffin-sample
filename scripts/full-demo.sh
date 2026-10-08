#!/usr/bin/env bash
# One-command, source-built Tiffin demo. The host-debug workflow remains scripts/up.sh + scripts/run.sh.
set -euo pipefail
source "$(dirname "$0")/lib.sh"

FULL_COMPOSE="$INFRA_DIR/compose.full.yaml"
FRONTEND_DIR="${TIFFIN_FRONTEND_SOURCE_DIR:-$REPO_ROOT/../mpfrontend-tiffin-reference}"

usage() {
  cat <<'USAGE'
usage: scripts/full-demo.sh <up|up-backend|status|logs|down|reset> [--observability]

  up                 build, start, health-check and seed the complete product
  up-backend         do the same without web/BFF containers, for frontend source debugging
  status             show every container in the integrated stack
  logs               follow the product-runtime logs
  down               stop containers and preserve data volumes
  reset              stop containers and delete demo data volumes
  --observability    add OpenTelemetry Collector, Prometheus, Grafana, Jaeger and Kafka UI
USAGE
}

[ $# -ge 1 ] || { usage >&2; exit 2; }
action="$1"; shift
observability=false
if [ "${1:-}" = "--observability" ]; then observability=true; shift; fi
[ $# -eq 0 ] || { usage >&2; exit 2; }

ensure_env
export TIFFIN_FRONTEND_SOURCE_DIR="$FRONTEND_DIR"
export EDGE_AUTH="${EDGE_AUTH:-keycloak}"
export BACKEND_HOST=demo-runtime

store="$(env_value MEDIA_STORE rustfs)"
case "$store" in
  rustfs)
    export FULL_DEMO_STORE_HOST=rustfs FULL_DEMO_STORE_PORT=9000
    ;;
  seaweedfs)
    export FULL_DEMO_STORE_HOST=seaweedfs FULL_DEMO_STORE_PORT=8333
    ;;
  *) echo "MEDIA_STORE=$store: expected rustfs or seaweedfs" >&2; exit 2 ;;
esac

profiles=(--profile "media-$store")
if [ "$action" != "up-backend" ]; then profiles+=(--profile full-demo-frontend); fi
if $observability; then
  profiles+=(--profile observability --profile tools)
  export FULL_DEMO_OTEL_ENABLED=true
else
  export FULL_DEMO_OTEL_ENABLED=false
fi

full_compose() {
  compose -f "$FULL_COMPOSE" ${profiles[@]+"${profiles[@]}"} "$@"
}

build_demo_images() {
  # Several services intentionally share one source-built image. Building one representative for
  # each unique target avoids duplicate concurrent writes to the same BuildKit image/cache lease.
  full_compose build access
  full_compose build demo-seed
  full_compose build keycloak
  if [ "$action" = "up" ]; then
    full_compose build customer
  fi
}

case "$action" in
  up|up-backend)
    if [ "$action" = "up" ] && [ ! -f "$FRONTEND_DIR/Dockerfile" ]; then
      echo "Tiffin frontend source was not found at $FRONTEND_DIR" >&2
      echo "Clone https://github.com/panahister/mpfrontend-tiffin-reference next to this repository, or set TIFFIN_FRONTEND_SOURCE_DIR." >&2
      exit 2
    fi
    ensure_gateway_config
    if [ "$action" = "up" ]; then
      echo "== starting and checking the complete source-built demo"
    else
      echo "== starting and checking the containerized backend for frontend development"
    fi
    build_demo_images
    full_compose up --detach --remove-orphans --wait --wait-timeout 900
    if [ "$action" = "up-backend" ]; then
      cat <<INFO

The containerized Tiffin backend is ready for frontend source development.
  Keycloak     http://localhost:$(env_value KEYCLOAK_PORT 38180)
  APISIX       https://localhost:$(env_value GATEWAY_HTTPS_PORT 39443)
  Media store  http://localhost:$(env_value MEDIA_S3_PORT 39000)  ($store)

Stop: scripts/full-demo.sh down
INFO
      if [ -f "$FRONTEND_DIR/package.json" ]; then
        cat <<INFO

Run the frontend from source:
  cd "$FRONTEND_DIR"
  pnpm install --frozen-lockfile
  pnpm dev:product
INFO
      else
        cat <<'INFO'

Clone https://github.com/panahister/mpfrontend-tiffin-reference.git next to this repository,
then run `pnpm install --frozen-lockfile` and `pnpm dev:product` from that checkout.
INFO
      fi
      exit 0
    fi
    cat <<INFO

The complete Tiffin demo is ready.
  Customer     http://localhost:4411
  Operations   http://localhost:4412
  Keycloak     http://localhost:$(env_value KEYCLOAK_PORT 38180)
  APISIX       https://localhost:$(env_value GATEWAY_HTTPS_PORT 39443)
  Media store  http://localhost:$(env_value MEDIA_S3_PORT 39000)  ($store)

Status: scripts/full-demo.sh status
Logs:   scripts/full-demo.sh logs
Stop:   scripts/full-demo.sh down
INFO
    ;;
  status)
    full_compose ps --all
    ;;
  logs)
    full_compose logs --follow --tail 150 \
      access media restaurants ordering payments kitchen dispatch tracking notifications \
      customer customer-bff admin admin-bff
    ;;
  down)
    full_compose down --remove-orphans
    ;;
  reset)
    echo "Deleting the local Full Demo containers and named data volumes."
    full_compose down --volumes --remove-orphans
    ;;
  *) usage >&2; exit 2 ;;
esac
