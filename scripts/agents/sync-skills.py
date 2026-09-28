#!/usr/bin/env python3
"""Writes the skills an AI coding agent finds at the root of this repository.

Each service carries MP Core's ten skills, written for that service's own shape, transport and
broker. An agent that is started in a service's folder finds them. An agent that is started at the
root of this repository finds none: Codex looks in .agents/skills from its working directory up to
the root of the repository, and Claude Code looks into a folder below only once it works there.

So the root has ten skills of its own. Each does one thing: it finds the service a task belongs to,
and hands over to that service's skill. It carries no procedure of its own, so there is one body to
keep. Modelled on Storefront's own scripts/agents/sync-skills.py, the first sample to carry this.

    python3 scripts/agents/sync-skills.py

writes .agents/skills (Codex) and .claude/skills (Claude Code). Edit this file, not what it writes.
"""
import os
import shutil

SERVICES = [
    ("access", "Access", "decisions about roles; the only client of Keycloak's administration"),
    ("media", "Media", "every file of the platform"),
    ("restaurants", "Restaurants", "restaurants, menus, prices"),
    ("ordering", "Ordering", "orders, and the saga that moves them"),
    ("payments", "Payments", "the money of an order; called by services only"),
    ("kitchen", "Kitchen", "the restaurant's word on an order"),
    ("dispatch", "Dispatch", "couriers, and who carries what"),
    ("tracking", "Tracking", "where a courier is"),
    ("notifications", "Notifications", "what a customer is told"),
]

# name of MP Core's skill, what it is used for here, and where this repository shows the result
SKILLS = [
    ("plan-bounded-context",
     "Plan a bounded context or a capability of Tiffin before any code, with its aggregates, rules, commands and queries, and what is still unclear",
     "The rules and their proof, one row each: `docs/business.md`'s \"The rules\" table (e.g. `O7 | A customer cancels until the restaurant has started to cook`) and \"The scenarios\" table (`S7 | The customer changes their mind`)."),
    ("implement-ddd-module",
     "Create or reshape the Domain, Application and Infrastructure skeleton of one of Tiffin's services",
     "`ordering/src/Tiffin.Ordering.Domain/` (Rules/, Events/), `Tiffin.Ordering.Application/` (Commands/, Queries/, Ports/, Contracts/), `Tiffin.Ordering.Infrastructure/` (Audit/, Persistence/, Protos/), composed by `AddInfrastructure(...)` in that project's `DependencyInjection.cs`."),
    ("implement-vertical-slice",
     "Implement one business capability of Tiffin end to end, from the rule and the handler to the endpoint, its authorization and its tests",
     "Rule O7: `CancelOrder`/`CancelOrderHandler` in `ordering/src/Tiffin.Ordering.Application/Commands/CancelOrder.cs`, exposed at `orders.MapPost(\"/{orderId:guid}/cancel\", ...)` in `ordering/src/Tiffin.Ordering.Api/Rest/Endpoints/OrderEndpoints.cs`, proved by scenario S7."),
    ("design-transport-contract",
     "Design or change a REST or gRPC contract of one of Tiffin's services",
     "Ordering exposes both: REST in `ordering/src/Tiffin.Ordering.Api/Rest/Endpoints/OrderEndpoints.cs`, gRPC in `ordering/src/Tiffin.Ordering.Api/Protos/tiffin_ordering.proto`. Payments is gRPC-only, called by services alone: `payments/src/Tiffin.Payments.Api/` has `Grpc/` and `Protos/`, no `Rest/`."),
    ("apply-security",
     "Restrict a capability of Tiffin to a role, or to the actor's own record, from the validated token only",
     "`ordering/src/Tiffin.Ordering.Api/Hosting/OrderingPolicies.cs` declares `public const string Customer = \"tiffin.customer\"`; `CancelOrderHandler` (`CancelOrder.cs`) then checks the actor owns the order and belongs to its city before cancelling it."),
    ("apply-business-audit",
     "Record who did what in one of Tiffin's services, and decide what is never recorded",
     "`ordering/src/Tiffin.Ordering.Infrastructure/Audit/AuditPolicyConfiguration.cs`: `policy.Entity<Order>(\"ordering\").Include(o => o.Status).Include(o => o.CancellationReason)...` (default-deny; the delivery address is deliberately excluded). `CancelOrderHandler` calls `audit.RecordAsync(\"ordering\", \"order-cancelled\", ...)`."),
    ("apply-observability",
     "Add logs, metrics, traces or a health check to one of Tiffin's services, without leaking a secret",
     "Every service has `Hosting/HostHealthChecks.cs` (`Live`/`Ready` tags, a database readiness check). A message that never prints its secret: `payments/src/Tiffin.Payments.Application/Commands/OpenPayment.cs` overrides `ToString()` to print `OrderId`, `City` and `Amount`, and to leave `PaymentToken` out."),
    ("configure-messaging",
     "Declare a topic, a queue, a retry rule, or what happens when a message is given up",
     "`ordering/src/Tiffin.Ordering.Api/Hosting/GivenUpMessages.cs` maps a dead-lettered answer (`PaymentAuthorized`, `KitchenRejected`, `CourierUnavailable`, ...) back to the order it belongs to and says so; wired into `Program.cs` with Wolverine's `.And(GivenUpMessages.SayWhichOrderWaitsAsync, ...)`."),
    ("integrate-contexts",
     "Make two of Tiffin's services work together without sharing code or a transaction",
     "`tests/Tiffin.Contracts.Tests/ContractTests.cs` holds two independently-declared copies of one message together: Ordering's `Contracts.PaymentRequested` and Payments' own `Contracts.PaymentRequested`, checked in the same `Messages()` table."),
    ("verify-business-behavior",
     "Prove a capability of Tiffin: a rule by its code, a guarantee by running it",
     "`scripts/scenarios.sh`: every step states what it expects. S15 sets `Ordering:RestaurantAnswerDeadline` to eight seconds and shows a silent restaurant's order cancelled, refunded and the kitchen told to stop, while one answered in time is untouched."),
]

BODY = """---
name: tiffin-{short}
description: "{description}. Finds the service the task belongs to (one of the nine) and hands over to that service's MP Core skill."
---

# {title}

This repository holds nine services. Each carries MP Core's skill `mpcore-{short}`, written for that
service's own shape, transport and broker. This skill finds the right one. It has no procedure of its
own.

## Steps

1. **Decide which service the task belongs to.** `AGENTS.md` at the root of the repository has the
   table. A task that touches two services is two changes and a contract between them: say so, and ask
   which one to begin with.
2. **Read that service's manifest**, `<service>/.mpcore/template-manifest.json`. Its shape, transport
   and broker were decided when the service was generated.
3. **Read the body of the skill for that service, and follow it:**

{bodies}
4. **Hold to the rules of this repository** (`AGENTS.md`): the nine services share no code; each writes
   only its own data, and tells another by a message; a defect of MP Core is reported, never worked
   around; a guarantee is proved by running it.
5. **Change `docs/business.md` with the code.** A rule that is not listed there does not exist.

## What the result looks like, in this repository

{example}
"""

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))


def main():
    for target in (".agents/skills", ".claude/skills"):
        folder = os.path.join(ROOT, target)
        if os.path.isdir(folder):
            for name in os.listdir(folder):
                if name.startswith("tiffin-"):
                    shutil.rmtree(os.path.join(folder, name))
        for short, description, example in SKILLS:
            bodies = "".join(
                f"   - {name} ({what}): `{folder_}/.mpcore/skills/mpcore-{short}/SKILL.md`\n"
                for folder_, name, what in SERVICES)
            text = BODY.format(short=short, description=description, example=example, bodies=bodies,
                               title=description.split(",")[0])
            path = os.path.join(folder, f"tiffin-{short}", "SKILL.md")
            os.makedirs(os.path.dirname(path), exist_ok=True)
            with open(path, "w", encoding="utf-8") as f:
                f.write(text)
    print(f"wrote {len(SKILLS)} skills into .agents/skills and .claude/skills")


if __name__ == "__main__":
    main()
