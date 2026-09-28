---
name: tiffin-apply-observability
description: "Add logs, metrics, traces or a health check to one of Tiffin's services, without leaking a secret. Finds the service the task belongs to (one of the nine) and hands over to that service's MP Core skill."
---

# Add logs

This repository holds nine services. Each carries MP Core's skill `mpcore-apply-observability`, written for that
service's own shape, transport and broker. This skill finds the right one. It has no procedure of its
own.

## Steps

1. **Decide which service the task belongs to.** `AGENTS.md` at the root of the repository has the
   table. A task that touches two services is two changes and a contract between them: say so, and ask
   which one to begin with.
2. **Read that service's manifest**, `<service>/.mpcore/template-manifest.json`. Its shape, transport
   and broker were decided when the service was generated.
3. **Read the body of the skill for that service, and follow it:**

   - Access (decisions about roles; the only client of Keycloak's administration): `access/.mpcore/skills/mpcore-apply-observability/SKILL.md`
   - Media (every file of the platform): `media/.mpcore/skills/mpcore-apply-observability/SKILL.md`
   - Restaurants (restaurants, menus, prices): `restaurants/.mpcore/skills/mpcore-apply-observability/SKILL.md`
   - Ordering (orders, and the saga that moves them): `ordering/.mpcore/skills/mpcore-apply-observability/SKILL.md`
   - Payments (the money of an order; called by services only): `payments/.mpcore/skills/mpcore-apply-observability/SKILL.md`
   - Kitchen (the restaurant's word on an order): `kitchen/.mpcore/skills/mpcore-apply-observability/SKILL.md`
   - Dispatch (couriers, and who carries what): `dispatch/.mpcore/skills/mpcore-apply-observability/SKILL.md`
   - Tracking (where a courier is): `tracking/.mpcore/skills/mpcore-apply-observability/SKILL.md`
   - Notifications (what a customer is told): `notifications/.mpcore/skills/mpcore-apply-observability/SKILL.md`

4. **Hold to the rules of this repository** (`AGENTS.md`): the nine services share no code; each writes
   only its own data, and tells another by a message; a defect of MP Core is reported, never worked
   around; a guarantee is proved by running it.
5. **Change `docs/business.md` with the code.** A rule that is not listed there does not exist.

## What the result looks like, in this repository

Every service has `Hosting/HostHealthChecks.cs` (`Live`/`Ready` tags, a database readiness check). A message that never prints its secret: `payments/src/Tiffin.Payments.Application/Commands/OpenPayment.cs` overrides `ToString()` to print `OrderId`, `City` and `Amount`, and to leave `PaymentToken` out.
