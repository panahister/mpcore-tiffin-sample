#!/usr/bin/env python3
"""Idempotently seed the local US Tiffin POC through product APIs.

The images are reserved and confirmed by Media, uploaded directly to the active
S3-compatible store, then referenced by immutable ids from Restaurants. No
credential or object-store key is written to the repository or printed.
"""

from __future__ import annotations

import json
import os
import pathlib
import re
import urllib.error
import urllib.parse
import urllib.request
from typing import Any


ROOT = pathlib.Path(__file__).resolve().parents[1]
ASSETS = ROOT / "infrastructure" / "demo-assets" / "menu"
KEYCLOAK = os.environ.get("KEYCLOAK_ORIGIN", "http://localhost:38180").rstrip("/")
MEDIA = os.environ.get("MEDIA_ORIGIN", "http://localhost:6200").rstrip("/")
RESTAURANTS = os.environ.get("RESTAURANTS_ORIGIN", "http://localhost:6300").rstrip("/")


MENUS = [
    {
        "username": "madison",
        "city": "seattle",
        "restaurant": "Harbor & Pine",
        "items": [
            ("SALMON", "Cedar-Plank Salmon Bowl", 24.50, "cedar-plank-salmon-bowl.png"),
            ("CRAB-ROLL", "Dungeness Crab Roll", 21.00, "dungeness-crab-roll.png"),
            ("BLACKBERRY-LEMONADE", "Blackberry Sage Lemonade", 5.50, "blackberry-sage-lemonade.png"),
        ],
    },
    {
        "username": "madison",
        "city": "seattle",
        "restaurant": "Rain City Noodle House",
        "items": [
            ("MUSHROOM-RAMEN", "Pacific Mushroom Ramen", 18.50, "pacific-mushroom-ramen.png"),
            ("CHICKEN-GYOZA", "Ginger Chicken Gyoza", 10.00, "ginger-chicken-gyoza.png"),
            ("YUZU-SODA", "Yuzu Cucumber Soda", 5.00, "yuzu-cucumber-soda.png"),
        ],
    },
    {
        "username": "mason",
        "city": "austin",
        "restaurant": "Lone Star Smokehouse",
        "items": [
            ("BRISKET", "Oak-Smoked Brisket Plate", 22.00, "oak-smoked-brisket-plate.png"),
            ("CORN-MAC", "Street-Corn Mac & Cheese", 11.50, "street-corn-mac-and-cheese.png"),
            ("PEACH-COBBLER", "Texas Peach Cobbler Jar", 8.00, "texas-peach-cobbler-jar.png"),
        ],
    },
    {
        "username": "mason",
        "city": "austin",
        "restaurant": "Barton Springs Taqueria",
        "items": [
            ("CHICKEN-TACOS", "Smoked Chicken Street Tacos", 16.50, "smoked-chicken-street-tacos.png"),
            ("GREEN-CHILE-QUESO", "Green Chile Queso & Chips", 9.50, "green-chile-queso-and-chips.png"),
            ("PRICKLY-PEAR", "Prickly Pear Agua Fresca", 5.00, "prickly-pear-agua-fresca.png"),
        ],
    },
]

SCENARIO_RESTAURANTS = [
    ("madison", "seattle", re.compile(r"Harbor Bowl \d{6}")),
    ("mason", "austin", re.compile(r"Lone Star Kitchen \d{6}")),
]


class SeedError(RuntimeError):
    pass


def http(method: str, url: str, token: str | None = None, body: Any | None = None) -> tuple[int, Any]:
    data = None if body is None else json.dumps(body, separators=(",", ":")).encode()
    headers = {"Accept": "application/json"}
    if token:
        headers["Authorization"] = "Bearer " + token
    if data is not None:
        headers["Content-Type"] = "application/json"
    request = urllib.request.Request(url, data=data, method=method, headers=headers)
    try:
        with urllib.request.urlopen(request, timeout=20) as response:
            payload = response.read()
            return response.status, json.loads(payload) if payload else None
    except urllib.error.HTTPError as error:
        detail = error.read().decode("utf-8", "replace")[:500]
        raise SeedError(f"{method} {urllib.parse.urlsplit(url).path} failed with HTTP {error.code}: {detail}") from None


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
        with urllib.request.urlopen(request, timeout=20) as response:
            value = json.load(response)
    except urllib.error.HTTPError as error:
        raise SeedError(f"Keycloak refused the local demo identity {username} with HTTP {error.code}") from None
    token = value.get("access_token")
    if not isinstance(token, str) or not token:
        raise SeedError(f"Keycloak issued no token for {username}")
    return token


def upload(token: str, city: str, file_name: str) -> str:
    path = (ASSETS / file_name).resolve()
    if path.parent != ASSETS.resolve() or not path.is_file():
        raise SeedError(f"missing bounded demo asset: {file_name}")
    content = path.read_bytes()
    _, ticket = http(
        "POST",
        MEDIA + "/v1/media/uploads",
        token,
        {"purpose": "menu-picture", "fileName": file_name, "contentType": "image/png", "size": len(content)},
    )
    if not isinstance(ticket, dict) or not isinstance(ticket.get("uploadUrl"), str):
        raise SeedError("Media returned no upload ticket")
    upload_url = ticket["uploadUrl"]
    parsed = urllib.parse.urlsplit(upload_url)
    if parsed.scheme not in {"http", "https"} or parsed.hostname not in {"localhost", "127.0.0.1"}:
        raise SeedError("Media returned an upload URL outside the local object store")
    request = urllib.request.Request(upload_url, data=content, method="PUT", headers={"Content-Type": "image/png"})
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            if response.status not in {200, 201, 204}:
                raise SeedError(f"object upload returned HTTP {response.status}")
    except urllib.error.HTTPError as error:
        raise SeedError(f"object upload failed with HTTP {error.code}") from None
    media_id = ticket.get("mediaId")
    if not isinstance(media_id, str):
        raise SeedError("Media returned no immutable id")
    _, confirmed = http("POST", MEDIA + f"/v1/media/{urllib.parse.quote(media_id)}/confirm", token)
    if not isinstance(confirmed, dict) or confirmed.get("state") != "Available" or confirmed.get("city") != city:
        raise SeedError("Media did not confirm an available city-owned image")
    return media_id


def seed_menu(definition: dict[str, Any]) -> dict[str, Any]:
    username = definition["username"]
    city = definition["city"]
    token = token_for(username)
    _, page = http("GET", RESTAURANTS + "/v1/restaurants?" + urllib.parse.urlencode({"city": city, "size": 100}), token)
    existing = next((item for item in page.get("items", []) if item.get("name") == definition["restaurant"]), None)
    if existing is None:
        _, menu = http(
            "POST",
            RESTAURANTS + "/v1/restaurants/",
            token,
            {"name": definition["restaurant"], "currency": "USD"},
        )
    else:
        _, menu = http("GET", RESTAURANTS + f"/v1/restaurants/{existing['restaurantId']}/menu?city={city}", token)
    restaurant_id = menu["restaurantId"]
    current = {item["code"]: item for item in menu.get("items", [])}
    seeded: list[dict[str, str]] = []
    for code, name, price, file_name in definition["items"]:
        picture_id = current.get(code, {}).get("pictureId")
        if not picture_id:
            picture_id = upload(token, city, file_name)
        _, menu = http(
            "PUT",
            RESTAURANTS + f"/v1/restaurants/{restaurant_id}/menu/{urllib.parse.quote(code)}",
            token,
            {"name": name, "price": price, "isAvailable": True, "pictureId": picture_id},
        )
        current = {item["code"]: item for item in menu.get("items", [])}
        seeded.append({"code": code, "pictureId": picture_id})
    hero = seeded[0]["pictureId"]
    _, menu = http(
        "PUT", RESTAURANTS + f"/v1/restaurants/{restaurant_id}/picture", token, {"pictureId": hero}
    )
    if not menu.get("isOpen"):
        _, menu = http("POST", RESTAURANTS + f"/v1/restaurants/{restaurant_id}/open", token)
    return {
        "restaurantId": restaurant_id,
        "name": definition["restaurant"],
        "city": city,
        "currency": menu.get("currency"),
        "isOpen": menu.get("isOpen"),
        "items": seeded,
    }


def close_scenario_restaurants(username: str, city: str, name_pattern: re.Pattern[str]) -> list[dict[str, str]]:
    """Hide timestamped scenario fixtures from the human-facing demo catalog."""
    token = token_for(username)
    _, page = http(
        "GET",
        RESTAURANTS + "/v1/restaurants?" + urllib.parse.urlencode({"city": city, "size": 100}),
        token,
    )
    closed: list[dict[str, str]] = []
    for item in page.get("items", []):
        name = item.get("name")
        restaurant_id = item.get("restaurantId")
        if (
            item.get("isOpen") is True
            and isinstance(name, str)
            and name_pattern.fullmatch(name)
            and isinstance(restaurant_id, str)
        ):
            http("POST", RESTAURANTS + f"/v1/restaurants/{restaurant_id}/close", token)
            closed.append({"restaurantId": restaurant_id, "name": name, "city": city})
    return closed


def main() -> int:
    closed = [
        item
        for username, city, name_pattern in SCENARIO_RESTAURANTS
        for item in close_scenario_restaurants(username, city, name_pattern)
    ]
    result = [seed_menu(definition) for definition in MENUS]
    print(json.dumps({"closedScenarioFixtures": closed, "seeded": result}, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
