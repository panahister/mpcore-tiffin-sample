#!/usr/bin/env python3
"""Exercise Keycloak self-registration without inventing a second signup API.

The script follows the same public OIDC and HTML form flow as the browser. Demo
identity values come from environment variables so credentials never enter the
repository or command output. A successful run stops at the application callback;
the caller then verifies the durable event and Access projection independently.
"""

from __future__ import annotations

import base64
import hashlib
import http.cookiejar
import json
import os
import secrets
import urllib.error
import urllib.parse
import urllib.request
from html.parser import HTMLParser


class RegistrationPage(HTMLParser):
    def __init__(self) -> None:
        super().__init__()
        self.registration_url: str | None = None
        self.form_action: str | None = None

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        values = dict(attrs)
        href = values.get("href")
        if tag == "a" and href and "/login-actions/registration" in href:
            self.registration_url = href
        if tag == "form" and values.get("id") == "kc-register-form":
            self.form_action = values.get("action")


class StopAtCallback(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, file_pointer, code, message, headers, new_url):  # type: ignore[no-untyped-def]
        if new_url.startswith("http://localhost:4411/api/session/callback"):
            return None
        return super().redirect_request(request, file_pointer, code, message, headers, new_url)


def required(name: str) -> str:
    value = os.environ.get(name, "").strip()
    if not value:
        raise ValueError(f"{name} is required")
    return value


def page(opener: urllib.request.OpenerDirector, url: str) -> RegistrationPage:
    try:
        with opener.open(url, timeout=15) as response:
            document = response.read().decode("utf-8")
    except urllib.error.HTTPError as error:
        detail = error.read().decode("utf-8", "replace")[:3000]
        raise RuntimeError(f"registration page failed with HTTP {error.code}: {detail}") from None
    parser = RegistrationPage()
    parser.feed(document)
    return parser


def main() -> int:
    username = required("SIGNUP_USERNAME")
    password = required("SIGNUP_PASSWORD")
    email = required("SIGNUP_EMAIL")
    first_name = required("SIGNUP_FIRST_NAME")
    last_name = required("SIGNUP_LAST_NAME")
    if any(character.isspace() for character in username + password):
        raise ValueError("signup username and password cannot contain whitespace")

    verifier = secrets.token_urlsafe(64)
    challenge = base64.urlsafe_b64encode(hashlib.sha256(verifier.encode()).digest()).rstrip(b"=").decode()
    query = urllib.parse.urlencode(
        {
            "client_id": "tiffin-app",
            "response_type": "code",
            "scope": "openid profile",
            "redirect_uri": "http://localhost:4411/api/session/callback",
            "state": secrets.token_urlsafe(24),
            "nonce": secrets.token_urlsafe(24),
            "code_challenge": challenge,
            "code_challenge_method": "S256",
        }
    )
    issuer = os.environ.get("KEYCLOAK_ORIGIN", "http://localhost:38180").rstrip("/")
    cookie_jar = http.cookiejar.CookieJar()
    opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(cookie_jar), StopAtCallback())
    login = page(opener, f"{issuer}/realms/tiffin/protocol/openid-connect/auth?{query}")
    if not login.registration_url:
        raise RuntimeError("registration link is unavailable")
    # Browsers treat localhost as a secure context and return Keycloak's Secure
    # authentication cookies over the local HTTP development origin. Python's
    # RFC cookie jar does not have that localhost exception, so mirror it only
    # for this fixed loopback test origin.
    if urllib.parse.urlparse(issuer).hostname in {"localhost", "127.0.0.1"}:
        for cookie in cookie_jar:
            cookie.secure = False
    registration_url = urllib.parse.urljoin(issuer + "/", login.registration_url)
    registration = page(opener, registration_url)
    if not registration.form_action:
        raise RuntimeError("registration form action is unavailable")

    body = urllib.parse.urlencode(
        {
            "username": username,
            "password": password,
            "password-confirm": password,
            "email": email,
            "firstName": first_name,
            "lastName": last_name,
        }
    ).encode()
    request = urllib.request.Request(
        urllib.parse.urljoin(registration_url, registration.form_action),
        data=body,
        method="POST",
        headers={"Content-Type": "application/x-www-form-urlencoded"},
    )
    try:
        opener.open(request, timeout=15)
    except urllib.error.HTTPError as error:
        location = error.headers.get("Location", "")
        if error.code != 302 or not location.startswith("http://localhost:4411/api/session/callback"):
            detail = error.read().decode("utf-8", "replace")[:500]
            raise RuntimeError(f"registration failed with HTTP {error.code}: {detail}") from None
    else:
        raise RuntimeError("registration did not return the expected application callback")

    print(json.dumps({"registered": True, "username": username, "callbackIssued": True}, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
