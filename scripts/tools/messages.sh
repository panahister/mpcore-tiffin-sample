#!/usr/bin/env bash
# Writes the message files of every service from their tables (Resources/messages.json).
#
#   scripts/tools/messages.sh            # writes them
#   scripts/tools/messages.sh --check    # fails when a file is not what its table says
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$root"
tables=$(find . -path ./tmp -prune -o -name messages.json -path '*/Resources/*' -not -path '*/bin/*' -not -path '*/obj/*' -print | sort)
before=$(find . -name '*.resx' -not -path '*/bin/*' -not -path '*/obj/*' -exec shasum {} + | sort)
for table in $tables; do python3 scripts/tools/resx.py "$table"; done
if [ "${1:-}" = "--check" ]; then
  after=$(find . -name '*.resx' -not -path '*/bin/*' -not -path '*/obj/*' -exec shasum {} + | sort)
  [ "$before" = "$after" ] || { echo "a message file is not what its table says: run scripts/tools/messages.sh and commit the result" >&2; exit 1; }
fi
