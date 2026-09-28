#!/usr/bin/env bash
# Builds and tests every service and the contracts between them. Arguments are passed on to dotnet.
#
#   scripts/test.sh                              # everything, Debug
#   scripts/test.sh --configuration Release      # as CI does
#
# A service is built and tested alone: it is a solution of its own, and nothing of another service is
# needed to build it. tests/Tiffin.Contracts.Tests is the one project that sees them all.
set -euo pipefail
source "$(dirname "$0")/lib.sh"

total=0

# The logger of the test run and the configuration are for both steps; a build does not know a logger.
build_args=(); test_args=()
while [ $# -gt 0 ]; do
  case "$1" in
    --logger) test_args+=("$1" "$2"); shift 2 ;;
    *) build_args+=("$1"); test_args+=("$1"); shift ;;
  esac
done

run_both() { # run_both NAME PATH
  local name="$1" path="$2" out
  dotnet build "$path" --nologo -v quiet ${build_args[@]+"${build_args[@]}"} >/dev/null \
    || { dotnet build "$path" --nologo ${build_args[@]+"${build_args[@]}"} | grep -E ' error ' | sort -u; echo "$name: the build failed" >&2; exit 1; }
  out="$(dotnet test "$path" --nologo --no-build ${test_args[@]+"${test_args[@]}"} 2>&1)" \
    || { printf '%s\n' "$out" | grep -E 'Failed |error|Assert|Expected|Actual' | head -40; echo "$name: tests failed" >&2; exit 1; }
  local passed; passed="$(printf '%s\n' "$out" | sed -n 's/.*Passed: *\([0-9][0-9]*\).*/\1/p' | paste -sd+ - | bc)"
  printf '   %-14s %s passed\n' "$name" "${passed:-0}"
  total=$((total + ${passed:-0}))
}

for service in "${SERVICES[@]}"; do run_both "$service" "$(solution_of "$service")"; done
run_both contracts "$REPO_ROOT/tests/Tiffin.Contracts.Tests"
printf '   %-14s %s passed\n' "together" "$total"
