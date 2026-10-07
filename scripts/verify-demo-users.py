#!/usr/bin/env python3
"""Verify every human Tiffin demo identity without printing credentials or tokens.

The check proves three separate things: Keycloak accepts the documented local
password convention, the issued token contains the expected city and product
role, and a read-only endpoint protected by that role can be reached.  It never
changes roles, duty state, restaurants, orders or any other product data.
"""

from __future__ import annotations

import base64
import json
import os
import subprocess
import sys
import urllib.error
import urllib.parse
import urllib.request
from typing import Any


KEYCLOAK = os.environ.get("KEYCLOAK_ORIGIN", "http://localhost:38180").rstrip("/")
SERVICES = {
    "access": os.environ.get("ACCESS_ORIGIN", "http://localhost:6100").rstrip("/"),
    "kitchen": os.environ.get("KITCHEN_ORIGIN", "http://localhost:6600").rstrip("/"),
    "ordering": os.environ.get("ORDERING_ORIGIN", "http://localhost:6400").rstrip("/"),
}

# Username, city/tenant, product role, and the safe read used to prove access.
USERS = [
    ("olivia", "seattle", "customer", "orders"),
    ("ethan", "seattle", "customer", "orders"),
    ("madison", "seattle", "restaurant-manager", "kitchen"),
    ("noah", "seattle", "courier", "courier"),
    ("ava", "seattle", "city-admin", "access"),
    ("emma", "austin", "customer", "orders"),
    ("mason", "austin", "restaurant-manager", "kitchen"),
    ("logan", "austin", "city-admin", "access"),
    ("grace", "platform", "platform-admin", "access"),
]


class VerificationError(RuntimeError):
    pass


def token_for(username: str) -> str:
    form = urllib.parse.urlencode(
        {
            "grant_type": "password",
            "client_id": "tiffin-app",
            "username": username,
            "password": username + "-lab",
        }
    ).encode()
    request = urllib.request.Request(
        KEYCLOAK + "/realms/tiffin/protocol/openid-connect/token",
        data=form,
        method="POST",
        headers={"Content-Type": "application/x-www-form-urlencoded", "Accept": "application/json"},
    )
    try:
        with urllib.request.urlopen(request, timeout=10) as response:
            body = json.load(response)
    except (urllib.error.URLError, json.JSONDecodeError) as error:
        raise VerificationError(f"{username}: Keycloak login failed") from error
    token = body.get("access_token")
    if not isinstance(token, str) or not token:
        raise VerificationError(f"{username}: Keycloak issued no access token")
    return token


def claims_of(token: str) -> dict[str, Any]:
    try:
        encoded = token.split(".")[1]
        encoded += "=" * (-len(encoded) % 4)
        return json.loads(base64.urlsafe_b64decode(encoded))
    except (IndexError, ValueError, json.JSONDecodeError) as error:
        raise VerificationError("Keycloak issued an unreadable access token") from error


def get(url: str, token: str) -> int:
    request = urllib.request.Request(url, headers={"Authorization": "Bearer " + token, "Accept": "application/json"})
    try:
        with urllib.request.urlopen(request, timeout=10) as response:
            response.read()
            return response.status
    except urllib.error.HTTPError as error:
        error.read()
        return error.code
    except urllib.error.URLError as error:
        raise VerificationError(f"protected read is unreachable: {urllib.parse.urlsplit(url).path}") from error


def protected_read(kind: str, token: str) -> str:
    if kind == "orders":
        status = get(SERVICES["ordering"] + "/v1/orders/?page=1&size=1", token)
        if status != 200:
            raise VerificationError(f"Orders read returned HTTP {status}")
        return "Orders 200"
    if kind == "kitchen":
        status = get(SERVICES["kitchen"] + "/v1/kitchen/tickets/?status=Pending&page=1&size=1", token)
        if status != 200:
            raise VerificationError(f"Kitchen read returned HTTP {status}")
        return "Kitchen 200"
    if kind == "access":
        status = get(SERVICES["access"] + "/v1/access/roles", token)
        if status != 200:
            raise VerificationError(f"Access read returned HTTP {status}")
        return "Access 200"
    if kind == "courier":
        # An idle courier correctly receives NotFound. PermissionDenied would mean
        # that the role does not work and is deliberately not accepted here.
        result = subprocess.run(
            [
                "grpcurl",
                "-max-time",
                "5",
                "-plaintext",
                "-H",
                "authorization: Bearer " + token,
                "-d",
                "{}",
                "localhost:6701",
                "tiffin.dispatch.v1.Couriers/GetMyDelivery",
            ],
            capture_output=True,
            text=True,
            timeout=8,
            check=False,
        )
        combined = result.stdout + result.stderr
        if result.returncode == 0:
            return "Dispatch OK"
        if "NotFound" in combined or "Not Found" in combined:
            return "Dispatch authorized (idle)"
        raise VerificationError("Dispatch did not authorize the courier")
    raise VerificationError(f"unknown protected read: {kind}")


def main() -> int:
    report: list[dict[str, str]] = []
    for username, city, role, probe in USERS:
        token = token_for(username)
        claims = claims_of(token)
        roles = claims.get("realm_access", {}).get("roles", [])
        if claims.get("preferred_username") != username:
            raise VerificationError(f"{username}: token names another identity")
        if claims.get("tenant_id") != city:
            raise VerificationError(f"{username}: expected tenant {city}")
        if role not in roles:
            raise VerificationError(f"{username}: expected role {role}")
        report.append(
            {
                "username": username,
                "tenant": city,
                "role": role,
                "proof": protected_read(probe, token),
                "result": "PASS",
            }
        )
    print(json.dumps({"verified": len(report), "users": report}, indent=2))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (VerificationError, FileNotFoundError, subprocess.TimeoutExpired) as error:
        print(str(error), file=sys.stderr)
        raise SystemExit(1)
