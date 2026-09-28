#!/usr/bin/env python3
"""Draws the diagrams of the README into docs/images. Run from the repository root:

    python3 scripts/diagrams/tiffin.py

The kit (draw.py and the icons) is the one Storefront draws its pictures with, so the two samples look alike.
Every number and every line of code in a picture is taken from this repository; the README says how.
"""
import os, sys
sys.path.insert(0, os.path.dirname(__file__))
from draw import write_both

OUT = "docs/images"
os.makedirs(OUT, exist_ok=True)


# ------------------------------------------------------------------------------------------------ system
SERVICES = [
    # colour, name, what it owns, ports, icons of what it keeps and speaks
    ("purple", "Access", "Roles, in front of Keycloak", "REST :6100", ["postgresql", "apachekafka"]),
    ("purple", "Media", "Every file of the platform", "REST :6200", ["postgresql", "s3", "apachekafka"]),
    ("green", "Restaurants", "Restaurants, menus, prices", "REST :6300", ["postgresql", "redis", "apachekafka"]),
    ("blue", "Ordering", "Orders, and the saga", "REST :6400 · gRPC :6401", ["postgresql", "rabbitmq", "apachekafka"]),
    ("teal", "Payments", "Money; called by services only", "gRPC :6501 · not at the edge", ["postgresql", "rabbitmq"]),
    ("amber", "Kitchen", "The restaurant's word", "REST :6600", ["postgresql", "rabbitmq", "apachekafka"]),
    ("rose", "Dispatch", "Couriers, who carries what", "gRPC :6701", ["postgresql", "rabbitmq", "apachekafka"]),
    ("rose", "Tracking", "Where a courier is", "REST :6800", ["timescale", "apachekafka"]),
    ("slate", "Notifications", "What a customer is told", "REST :6900", ["postgresql", "apachekafka"]),
]


def system(d):
    d.heading(32, 44, "Tiffin: nine services, two cities",
              "Everything in this picture runs on your machine with four commands, and on GitHub on every change.")
    L, LW, R, RW = 32, 676, 728, 240

    callers = ["Customers", "Restaurants", "Couriers", "City admins"]
    for i, name in enumerate(callers):
        x = L + i * 171
        d.rect(x, 84, 163, 42, d.fill("slate"), d.stroke("slate"), r=21, shadow=True)
        d.icon("people", x + 12, 94, 22)
        d.text(x + 42, 110, name, size=12.3, weight=600, fill=d.ink("slate"))
    d.arrow([(L + LW / 2, 128), (L + LW / 2, 156)], sw=1.8)
    d.text(L + LW / 2 + 10, 147, "HTTPS  ·  gRPC over TLS  ·  Tehran and Istanbul", size=10.5, fill=d.t["muted"])

    d.group(L, 168, LW, 84, "rose", "EDGE")
    d.tool(L + 16, 188, LW - 32, 52, "apisix", "Apache APISIX",
           ["Ends TLS and routes REST and gRPC to eight services; by a switch, verifies tokens with Keycloak"], "run")
    d.arrow([(L + LW / 2, 254), (L + LW / 2, 280)], sw=1.8)

    gy, w, h, gx = 292, 212, 122, 10
    d.group(L, gy, LW, 3 * h + 2 * gx + 36, "green", "NINE SERVICES  ·  A DATABASE EACH  ·  NO SHARED ASSEMBLY")
    for i, (color, name, owns, ports, icons) in enumerate(SERVICES):
        x, y = L + 16 + (i % 3) * (w + gx), gy + 20 + (i // 3) * (h + gx)
        d.card(x, y, w, h, color, name, [owns, f"`{ports}`"], kicker="service", title_size=14, line_size=11.2)
        for k, ic in enumerate(icons):
            d.icon(ic, x + w - 22 - k * 22, y + h - 26, 16)
        d.chip(x + 12, y + h - 29, "MP Core", "blue", size=9.5, pad=7)

    by = gy + 3 * h + 2 * gx + 50
    d.group(L, by, LW, 92, "amber", "MESSAGES  ·  EACH SIDE STATES ITS OWN COPY OF A CONTRACT")
    d.tool(L + 16, by + 20, 316, 58, "rabbitmq", "RabbitMQ", ["5 requests and 7 answers: the order's saga,", "one queue per contract, one reader each"], "run", size=24)
    d.tool(L + 344, by + 20, 316, 58, "apachekafka", "Apache Kafka", ["10 topics: what happened, for whoever", "reads it, from the beginning if it is new"], "run", size=24)

    # ---- the right column
    d.group(R, 168, RW, 110, "purple", "IDENTITY")
    d.tool(R + 12, 188, RW - 24, 76, "keycloak", "Keycloak", ["Every service validates", "every token itself; only", "Access may administer it"], "run")
    d.group(R, 300, RW, 84, "teal", "PAYMENT PROVIDER")
    d.tool(R + 12, 320, RW - 24, 52, "wiremock", "PayLane", ["Fictional, played by WireMock"], "run")
    d.group(R, 406, RW, 128, "slate", "FILE STORE  ·  S3 API")
    d.tool(R + 12, 426, RW - 24, 44, "rustfs", "RustFS 1.0.0", ["the default"], "run", size=22)
    d.tool(R + 12, 478, RW - 24, 44, "seaweedfs", "SeaweedFS 4.47", ["MEDIA_STORE=seaweedfs"], "run", size=22)
    d.group(R, 556, RW, 172, "blue", "DATA")
    d.tool(R + 12, 576, RW - 24, 44, "postgresql", "PostgreSQL", ["eight databases, one server"], "run", size=22)
    d.tool(R + 12, 628, RW - 24, 44, "timescale", "TimescaleDB", ["Tracking's hypertable"], "run", size=22)
    d.tool(R + 12, 680, RW - 24, 36, "redis", "Redis", [], "run", size=20)

    d.status(L + 8, by + 112, "run")
    d.text(L + 20, by + 116, "Run end to end by the 16 scenarios of scripts/scenarios.sh, with each file store, on every change", size=11.5, fill=d.t["muted"])


# ------------------------------------------------------------------------------------------------ journey
def journey(d):
    d.heading(32, 44, "One order, through six services",
              "Every box is one transaction in one service. The blue line is what MP Core guarantees there.")
    steps = [
        ("blue", "Ordering", "The order is placed", ["The price is asked of Restaurants,", "the card handed to Payments"], "Idempotency-Key; calls as itself"),
        ("teal", "Payments", "The card is charged", ["PayLane may be slow, or down;", "the token is erased once used"], "Retry under one key"),
        ("amber", "Kitchen", "The restaurant decides", ["mina accepts, and promises", "a time; or refuses"], "A queue: the request waits"),
        ("rose", "Dispatch", "A courier is chosen", ["The one who waited longest.", "Six orders may want him"], "One save wins; five go back"),
        ("rose", "Tracking", "The courier is followed", ["Positions in a hypertable,", "compressed after seven days"], "TimescaleDB, by policy"),
        ("rose", "Dispatch", "The order is delivered", ["The courier says so, and the", "stream tells whoever reads"], "Outbox: sent if committed"),
        ("blue", "Ordering", "The saga ends", ["Delivered; or cancelled, with", "every step taken back"], "The city on every message"),
        ("slate", "Notifications", "The customer is told", ["From the stream, in English,", "Turkish or Chinese"], "Inbox: handled once"),
    ]
    w, h, gx, gy, x0, y0 = 224, 168, 10, 22, 32, 88
    for i, (color, service, title, lines, promise) in enumerate(steps):
        col, row = i % 4, i // 4
        x, y = x0 + col * (w + gx), y0 + row * (h + gy)
        d.card(x, y, w, h, color, title, lines, kicker=service)
        d.badge(x + w - 20, y + 26, str(i + 1), color)
        d.line(x + 14, y + h - 40, x + w - 14, y + h - 40, color=d.stroke(color), sw=1, dash="3 4")
        d.icon("mpcore", x + 14, y + h - 30, 18)
        d.text(x + 38, y + h - 17, promise, size=10.6, weight=600, fill=d.ink("blue"))
        if col:
            d.arrow([(x - gx + 1, y + h / 2), (x - 1, y + h / 2)], sw=1.6)
    d.arrow([(x0 + 4 * w + 3 * gx + 8, y0 + h / 2), (x0 + 4 * w + 3 * gx + 18, y0 + h / 2), (x0 + 4 * w + 3 * gx + 18, y0 + h + gy / 2),
             (x0 - 12, y0 + h + gy / 2), (x0 - 12, y0 + h + gy + h / 2), (x0 - 2, y0 + h + gy + h / 2)], sw=1.6)

    # ---- what is taken back when a step fails
    ty = y0 + 2 * h + gy + 24
    d.text(x0, ty + 4, "WHEN A STEP FAILS, THE STEPS BEFORE IT ARE TAKEN BACK", size=10, weight=700, fill=d.accent("rose"), spacing="0.8")
    backs = [("The bank refuses", "cancelled; nothing else was done", "S4"),
             ("The restaurant refuses", "Payments gives the money back", "S2"),
             ("No courier is free", "the Kitchen stops, the money goes back", "S3"),
             ("PayLane gone for good", "cancelled; the request is dead-lettered", "S9"),
             ("The restaurant is silent", "after ten minutes: stop, money back", "S15"),
             ("The customer cancels", "until the Kitchen cooks: stop, money back", "S7")]
    bw = (4 * w + 3 * gx - 5 * 8) / 6
    for k, (what, back, proof) in enumerate(backs):
        x = x0 + k * (bw + 8)
        d.rect(x, ty + 14, bw, 76, d.fill("rose"), d.stroke("rose"), r=9, sw=1)
        d.text(x + 10, ty + 34, what, size=11.2, weight=700, fill=d.ink("rose"))
        words, lines, cur = back.split(" "), [], ""
        for word in words:
            if len(cur) + len(word) + 1 > 24:
                lines.append(cur); cur = word
            else:
                cur = (cur + " " + word).strip()
        lines.append(cur)
        for j, line in enumerate(lines[:2]):
            d.text(x + 10, ty + 51 + j * 14, line, size=10.2, fill=d.t["muted"])
        d.chip(x + bw - 40, ty + 66, proof, "blue", size=9, pad=6)


# ------------------------------------------------------------------------------------------------ city
def city(d):
    d.heading(32, 44, "The city travels with the work",
              "The city is the tenant. MP Core carries it from the token to the audit trail; the sample's rows name it themselves.")
    stops = [
        ("purple", "The token", ["Keycloak writes the", "group's city into", "tenant_id"], "never a user's header", ""),
        ("blue", "The request", ["ITenantContext answers", "it for the handler and", "for the audit trail"], "one port for all code", ""),
        ("amber", "A message", ["x-tenant-id, written by", "the publisher, opened", "for the handler"], "the save included", "0.9.1"),
        ("teal", "A call", ["x-tenant-id on a service's", "call; believed only from", "a listed service"], "listed services only", "0.9.2"),
        ("green", "A row", ["Every table has a City;", "every repository asks", "for it"], "the sample's own rule", ""),
        ("slate", "The audit trail", ["Every record names", "its city and its", "actor"], "in the change's commit", ""),
    ]
    w, gx, x0, y0, h = 148, 10, 32, 92, 170
    for i, (color, name, lines, how, since) in enumerate(stops):
        x = x0 + i * (w + gx)
        d.card(x, y0, w, h, color, name, lines, kicker="step " + str(i + 1), line_size=11)
        d.line(x + 12, y0 + h - 44, x + w - 12, y0 + h - 44, color=d.stroke(color), sw=1, dash="3 4")
        d.text(x + 12, y0 + h - 24, how, size=10.4, weight=600, fill=d.ink("blue") if i in (1, 2, 3) else d.t["muted"])
        if since:
            d.chip(x + w - 84, y0 + 12, "MP Core " + since, "blue", size=9, pad=6)
        if i:
            d.arrow([(x - gx + 1, y0 + h / 2), (x - 1, y0 + h / 2)], sw=1.6)

    by = y0 + h + 24
    d.rect(x0, by, 6 * w + 5 * gx, 104, d.fill("green"), d.stroke("green"), r=10, shadow=True)
    d.text(x0 + 16, by + 26, "What belongs to one city does not exist for the other", size=13.5, weight=700, fill=d.ink("green"))
    d.text(x0 + 16, by + 46, "An order, a restaurant, a file or a person of Tehran is “not found” for somebody of Istanbul, never “forbidden”,", size=11.2, fill=d.t["muted"])
    d.text(x0 + 16, by + 62, "because “forbidden” would say that it exists. Scenario S6 asks it of five services.", size=11.2, fill=d.t["muted"])
    d.chip(x0 + 16, by + 74, "S6", "blue", size=9.5, pad=7)
    d.chip(x0 + 56, by + 74, "Payments' audit trail: 20 of 20 payments of the scenarios name their city", "blue", size=9.5, pad=7)


# ------------------------------------------------------------------------------------------------ failure
def failure(d):
    d.heading(32, 44, "When something is down, or two arrive at once, nothing is lost",
              "Each case is a scenario that breaks something in the middle of an order, and checks what happened.")
    cases = [
        ("rose", "While one waits", "Restaurants is stopped",
         ["The customer is told 503,", "`RESTAURANTS_UNAVAILABLE`", "No order is stored, and", "no card is handed over"], "A call times out, the failure is a value", "S14"),
        ("amber", "By a message", "The Kitchen is stopped",
         ["The order is paid all the", "same. The request waits", "in its queue, six seconds", "and more, until it is back"], "Durable queue; sent only if committed", "S14"),
        ("teal", "From the stream", "Notifications stops",
         ["Nobody waits for it. Back,", "it reads what it missed:", "the customer is told the", "order was delivered"], "Kafka from its offset; inbox", "S14"),
        ("purple", "Outside", "PayLane does not answer",
         ["Once: tried again under", "one key. For good: the", "order is cancelled, the", "request dead-lettered"], "Resilient client; a given-up message answered", "S9"),
        ("blue", "Two at once", "Six orders, one courier",
         ["One save wins; five find", "nobody free, are taken", "back and paid back. No", "guard: six to one courier"], "Row version; the failed try's messages dropped", "S8"),
    ]
    w, gx, x0, y0, h = 180, 9, 32, 88, 240
    for i, (color, kicker, title, lines, how, proof) in enumerate(cases):
        x = x0 + i * (w + gx)
        d.card(x, y0, w, h, color, title, lines, kicker=kicker, title_size=13, line_size=11)
        d.line(x + 12, y0 + h - 58, x + w - 12, y0 + h - 58, color=d.stroke(color), sw=1, dash="3 4")
        d.icon("mpcore", x + 12, y0 + h - 48, 16)
        words, lines2, cur = how.split(" "), [], ""
        for word in words:
            if len(cur) + len(word) + 1 > 25:
                lines2.append(cur); cur = word
            else:
                cur = (cur + " " + word).strip()
        lines2.append(cur)
        for j, line in enumerate(lines2[:2]):
            d.text(x + 34, y0 + h - 36 + j * 14, line, size=10.2, weight=600, fill=d.ink("blue"))
        d.chip(x + w - 44, y0 + 12, proof, "blue", size=9.5, pad=7)


# ------------------------------------------------------------------------------------------------ why
def why(d):
    d.heading(32, 44, "What nine services need, without MP Core and with it",
              "Every row is a guarantee the scenarios prove against the running system. Without MP Core, each of nine teams builds it.")
    rows = [
        ("The city of a message", "A header on every publish, read before every handler", "nothing", "S1 S6"),
        ("The city of a call", "A header on every call, and a rule for whom to believe", ".AddMPCoreTenantPropagation()", "S1"),
        ("A service calls as itself", "Client credentials, a token cache, refused tokens forgotten", ".AddMPCoreServiceIdentity(...)", "S0 S1"),
        ("A saga step and its message", "An outbox per service, and proof a crash loses neither", "no handler calls SaveChangesAsync", "S1 to S4"),
        ("An order sent twice", "A key store in the same transaction, and the replay", ".RequireIdempotencyKey()", "S5"),
        ("A message delivered twice", "An inbox in six services", "options.UseMPCoreInbox();", "S5 S13"),
        ("Six orders, one courier", "Retries with pauses; a failed try's messages dropped", "OnException<DbUpdateConcurrencyException>()", "S8"),
        ("A rule, in three languages", "An error model on REST and gRPC, texts per language", "CheckRule(new ...)  (36 in 8 services)", "S2 S13"),
        ("A step never answered", "A sweeper in each service, with its own lock", "DeliverAfter = deadlines.RestaurantAnswer", "S15"),
        ("A time series", "Hypertable, compression and retention, as SQL", "migrationBuilder.CreateHypertable(...)", "S12"),
    ]
    x0, cw = 32, [196, 344, 306, 76]
    hy = 92
    heads = [("slate", "THE PLATFORM NEEDS"), ("rose", "WITHOUT MP CORE, EACH TEAM BUILDS"), ("green", "WITH MP CORE, TIFFIN WROTE"), ("blue", "PROVED BY")]
    x = x0
    for (color, label), w in zip(heads, cw):
        d.text(x + 4, hy, label, size=10, weight=700, fill=d.accent(color), spacing="0.8")
        x += w + 6
    rh, y = 42, hy + 12
    for need, without, code, proof in rows:
        x = x0
        d.rect(x, y, cw[0], rh - 6, d.fill("slate"), d.stroke("slate"), r=8, sw=1)
        d.text(x + 12, y + 23, need, size=12, weight=700, fill=d.ink("slate"))
        x += cw[0] + 6
        d.rect(x, y, cw[1], rh - 6, d.fill("rose"), d.stroke("rose"), r=8, sw=1)
        d.text(x + 12, y + 23, without, size=11, fill=d.ink("rose"))
        x += cw[1] + 6
        d.rect(x, y, cw[2], rh - 6, d.fill("green"), d.stroke("green"), r=8, sw=1)
        d.text(x + 12, y + 23, code, size=11, fill=d.ink("green"), mono=code != "nothing", italic=code == "nothing")
        x += cw[2] + 6
        d.chip(x, y + 6, proof, "blue", size=10.5, pad=8)
        y += rh
    y += 14
    total = sum(cw) + 18
    d.rect(x0, y, total, 88, d.t["canvas"], d.t["frame"], r=10, sw=1)
    d.text(x0 + 16, y + 26, "The code, counted", size=13, weight=700)
    d.text(x0 + 16, y + 44, "lines of C#, without comments and braces", size=10.5, fill=d.t["muted"])
    scale = (total - 260) / 5021
    bx = x0 + 240
    for k, (color, label, n) in enumerate([("green", "Tiffin's business: Domain and Application, nine services", 3353),
                                           ("blue", "MP Core 0.9.3: 28 packages, with 485 tests of their own", 5021)]):
        by = y + 18 + k * 32
        d.rect(bx, by, n * scale, 24, d.fill(color), d.stroke(color), r=6, sw=1)
        d.text(bx + 10, by + 16.5, f"{n:,}  ·  {label}", size=11, weight=600, fill=d.ink(color))


write_both(f"{OUT}/system", 1000, 850, "Tiffin: nine services behind a gateway, two brokers, identity, a payment provider, a file store and their data", system)
write_both(f"{OUT}/order-journey", 1000, 596, "One order through six services, what MP Core guarantees at each step, and what is taken back when a step fails", journey)
write_both(f"{OUT}/city", 1000, 412, "The city is the tenant: from the token to the request, a message, a call, a row and the audit trail", city)
write_both(f"{OUT}/failure", 1000, 350, "When a service, a provider or a courier is not there: five scenarios and what each shows", failure)
write_both(f"{OUT}/why-mpcore", 1000, 652, "What nine services need without MP Core and with it: the guarantees, the code Tiffin wrote, and the scenario that proves each", why)
print("drawn:", ", ".join(sorted(os.listdir(OUT))))
