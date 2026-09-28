#!/usr/bin/env bash
# Runs one service on this machine, against the dependencies in Docker.
#
#   scripts/run.sh ordering        one service, in the foreground
#   scripts/run.sh all             every service, in the background; logs in tmp/logs, then waits until
#                                  each of them says it is ready
#   scripts/run.sh stop            stops what "all" started
#
# MP Core comes from a clone of its repository next to this one (../mpcore) when there is one, and from
# nuget.org otherwise; see Directory.Build.targets. DOTNET_ARGS passes extra arguments to dotnet.
# TIFFIN_BIND=0.0.0.0 is for Linux, where the edge cannot reach the loopback address.
set -euo pipefail
source "$(dirname "$0")/lib.sh"
[ $# -ge 1 ] || { echo "usage: $0 <${SERVICES[*]} | all | stop>"; exit 2; }
LOGS="$REPO_ROOT/tmp/logs"

if [ "$1" = "stop" ]; then
  for pidfile in "$LOGS"/*.pid; do
    [ -f "$pidfile" ] || continue
    pid="$(cat "$pidfile")"; kill "$pid" 2>/dev/null && echo "stopped $(basename "$pidfile" .pid)"; rm -f "$pidfile"
  done
  exit 0
fi

if [ "$1" = "all" ]; then
  mkdir -p "$LOGS"
  # Services share MP Core's projects when it is built from source, and two builds at the same moment
  # write the same files. So everything is built first, in turn, and then started without building.
  for service in "${SERVICES[@]}"; do
    echo "== building $service"
    dotnet build "$(project_of "$service")" ${DOTNET_ARGS:-} --nologo -v quiet
  done
  for service in "${SERVICES[@]}"; do
    TIFFIN_NO_BUILD=1 nohup "$0" "$service" > "$LOGS/$service.log" 2>&1 &
    echo $! > "$LOGS/$service.pid"
  done
  for attempt in $(seq 1 60); do
    waiting=()
    for service in "${SERVICES[@]}"; do is_ready "$service" || waiting+=("$service"); done
    [ ${#waiting[@]} -eq 0 ] && { echo "every service is ready"; exit 0; }
    sleep 3
  done
  echo "not ready after three minutes: ${waiting[*]} (logs in tmp/logs)" >&2
  exit 1
fi

project="$(project_of "$1")"
if [ "$(uname -s)" = "Darwin" ] && [ "$(uname -m)" = "arm64" ] && ! /usr/bin/arch -x86_64 /usr/bin/true 2>/dev/null \
   && ! command -v grpc_csharp_plugin >/dev/null && [ -z "${DOTNET_ARGS:-}" ]; then
  cat >&2 <<'MSG'
This Mac has neither Rosetta nor a native gRPC code generator, so the .proto files cannot be compiled.
Install one of them once, then run this script again:
    softwareupdate --install-rosetta --agree-to-license
or
    brew install protobuf grpc
MSG
  exit 1
fi

export ASPNETCORE_ENVIRONMENT=Development

# The services listen on the loopback address. The edge runs in a container: Docker Desktop lets it
# reach that address, Docker on Linux does not. There, TIFFIN_BIND=0.0.0.0 makes a service listen where
# the container can reach it.
if [ -n "${TIFFIN_BIND:-}" ]; then
  rest="$(rest_port_of "$1")"; grpc="$(grpc_port_of "$1")"
  [ "$rest" != "-" ] && export Kestrel__Endpoints__Rest__Url="http://$TIFFIN_BIND:$rest"
  [ "$grpc" != "-" ] && export Kestrel__Endpoints__Grpc__Url="http://$TIFFIN_BIND:$grpc"
fi

if [ -z "${TIFFIN_NO_BUILD:-}" ]; then
  # Builds take turns; running does not. A turn that was never given back is taken over after 15 minutes.
  turn="${TMPDIR:-/tmp}/tiffin-build.turn"
  until mkdir "$turn" 2>/dev/null; do
    [ -n "$(find "$turn" -maxdepth 0 -mmin +15 2>/dev/null)" ] && rmdir "$turn" 2>/dev/null
    sleep 1
  done
  trap 'rmdir "$turn" 2>/dev/null' EXIT
  dotnet build "$project" ${DOTNET_ARGS:-}
  rmdir "$turn" 2>/dev/null; trap - EXIT
fi

exec dotnet run --project "$project" --no-build ${DOTNET_ARGS:-}
