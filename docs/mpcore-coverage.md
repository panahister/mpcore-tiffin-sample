# What MP Core does here

Which of MP Core's capabilities Tiffin uses, and what proves each. The list follows MP Core's own
catalogue, [docs/guide/capabilities.md](https://github.com/panahister/mpcore/blob/main/docs/guide/capabilities.md),
area by area. "Proved by" names a scenario of `scripts/scenarios.sh` or a test of this repository; a line
that is used and not proved here says so.

## The packages

Counted from the project files. Of MP Core's 28 runtime packages, the nine services reference 25.

| Package | access | media | restaurants | ordering | payments | kitchen | dispatch | tracking | notifications |
|---|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|
| `MPCore.Domain`, `MPCore.Application` | x | x | x | x | x | x | x | x | x |
| `MPCore.Hosting`, and through it `MPCore.Observability` | x | x | x | x | x | x | x | x | x |
| `MPCore.Persistence.Abstractions`, `...EntityFrameworkCore.PostgreSql` | x | x | x | x | x | x | x | x | x |
| `MPCore.Messaging.Abstractions`, `MPCore.Messaging.Wolverine` | x | x | x | x | x | x | x | x | x |
| `MPCore.Security.Abstractions`, `MPCore.Security.AspNetCore` | x | x | x | x | x | x | x | x | x |
| `MPCore.Tenancy.Abstractions` | x | x | x | x | x | x | x | x | x |
| `MPCore.Localization` | x | x | x | x | x | x | x | x | x |
| `MPCore.Validation.FluentValidation` | x | x | x | x | x | x | x | x | x |
| `MPCore.Resilience.Http` | x | x | x | x | x | x | x | x | x |
| `MPCore.Messaging.Wolverine.Kafka` | x | x | x | x | | x | x | x | x |
| `MPCore.Messaging.Wolverine.RabbitMQ` | | | | x | x | x | x | | |
| `MPCore.Transport.Http` | x | x | x | x | | x | | x | x |
| `MPCore.Transport.Grpc` | | | | x | x | | x | | |
| `MPCore.Observability.Prometheus` | x | x | x | x | | x | | x | x |
| `MPCore.Audit.Abstractions`, `...EntityFrameworkCore.PostgreSql` | x | x | x | x | x | x | | | |
| `MPCore.Idempotency.EntityFrameworkCore.PostgreSql` | | | | x | x | x | x | x | x |
| `MPCore.Caching.Abstractions`, `MPCore.Caching.Redis` | | | x | | | | | | |
| `MPCore.Caching.Memory` | x | | | | | | | x | |
| `MPCore.Persistence.Timescale` | | | | | | | | x | |

**Not referenced by Tiffin:** `MPCore.Caching.Hybrid` and `MPCore.Localization.EntityFrameworkCore.PostgreSql`.
Storefront runs both.

## Area by area

| Area of MP Core | What Tiffin does with it | Proved by |
|---|---|---|
| **1. Domain model** | Ten aggregates, child entities, value objects, 33 named rules, integration events raised by the aggregate | the unit tests of every service; S2, S7, S11 for rules as a caller sees them |
| **2. Use cases** | Commands and queries as separate messages; handlers as static methods; failures as values; validators before the handler | the unit tests; architecture tests of every service |
| **3. One commit** | The order and the request for its charge commit together; an answer leaves with the change it reports | S1; `PlaceOrderTests` |
| | A decision about a role and the step that tells the identity provider commit together; the provider is told afterwards | S10; `AccessTests` |
| **4. Twice is once** | `Idempotency-Key` on placing an order: a repeat receives the first answer | S5 |
| | The inbox on every consumer of six services | S13 looks into it; **a second delivery of one message was not provoked** |
| **5. Messaging** | RabbitMQ for requests and answers, Kafka for what happened; three services speak to both brokers | S1 to S4, S12, S13 |
| | Retry with a cooldown, then the dead-letter queue, then an answer to whoever waits | S9 |
| | A message for a service that is down waits in its queue; a reader of the stream that was down reads what it missed | S14 |
| | **The tenant of a message** (new in `0.9.1`): written by the publisher, opened for the handler | S1, S6: the audit trail of every consumer names the city |
| **6. Transport** | REST in seven services, gRPC in three, both in one; Problem Details and rich gRPC status | every scenario |
| **7. Security** | Bearer tokens on every request; deny by default; roles from Keycloak; an audience per service | S0 |
| | **A service's own identity** (new in `0.9.1`): Ordering calls Restaurants and Payments, Access calls Keycloak's administration | S0, S1, S10 |
| | **The tenant of a call** (new in `0.9.2`): Ordering names the city of the order in `x-tenant-id`; Payments and Restaurants believe it from Ordering only | S1 and every scenario that orders: Payments' audit trail names the city |
| | Behind a gateway | S0 asks through the edge; **the scenarios call the services directly** |
| **8. Business audit** | Business actions and entity changes, with the actor, the city and the request | S1, S6, S10, S11 |
| **9. Language** | 78 texts in English, Persian and Turkish; failures over REST and gRPC, and stored notifications, in the caller's language | S2, S6, S8, S11, S13; `NotificationTests` |
| | Translations edited while the service runs | not used here |
| **10. Data and cache** | One database per service; a hypertable with retention and compression; Redis in front of the menus | S12 for the hypertable; `RestaurantTests` for the cache, with a fake: **Redis itself is asked by the running service and not looked into by a scenario** |
| **11. Operations** | Health over REST and gRPC; calls to other systems with timeouts, retries and a circuit breaker | S0 for health; S9 for a provider that is down |
| | Logs, traces and metrics | configured, and off by default: **not looked at** |
| **12. Tooling** | Nine backends generated by `mpcore new backend`, in four combinations of transport and messaging | the generator built each of them: 0 warnings, 0 errors |
| | Multi-tenancy | **run for the first time in a sample**: S6, and every other scenario in passing |

## What MP Core deliberately does not do, and Tiffin did itself

| Not MP Core's | What Tiffin does |
|---|---|
| Data divided per tenant | every table has a `City`, every repository asks for it in its signature, every read names it |
| Rate limits | the edge limits how often an address may read the menus without a token |
| An endpoint for the audit trail | none; the scenarios read the table |
| A store of files | Media's adapter for the S3 API lives in Media. It moves to MP Core when a second service needs it |
| A delay set by the publisher | solved in MP Core `0.9.3`: `MessageDeliveryContext.DeliverAfter`; Ordering's deadline for a restaurant, S15 |
