#!/usr/bin/env bash
# Source-only integration gate: never prints identities, credentials or realm contents.
set -euo pipefail
repo="$(cd "$(dirname "$0")/.." && pwd)"
realm="${1:-$repo/infrastructure/keycloak/tiffin-realm.json}"
jq -e '
  [.clients[] | select(.clientId == "tiffin-app")] as $matches |
  ($matches | length) == 1 and
  ($matches[0] | .publicClient == true and .standardFlowEnabled == true and
    .serviceAccountsEnabled == false and .attributes["pkce.code.challenge.method"] == "S256" and
    .redirectUris == ["http://localhost:4411/api/session/callback", "http://localhost:4412/api/session/callback"] and
    .webOrigins == ["http://localhost:4411", "http://localhost:4412"])
' "$realm" >/dev/null
echo 'Tiffin frontend client: exact callbacks and S256 verified'
