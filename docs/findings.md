# Findings

What building Tiffin found, in the order it was found. A defect of MP Core is fixed in MP Core, never
worked around here; a finding that is a decision waits for the owner.

| # | Found | Where | State |
|---|---|---|---|
| T-01 | A message carried no tenant, and a handler that received one worked for nobody: the audit trail of a consumer recorded no city | MP Core, `MPCore.Messaging.Wolverine` | fixed in MP Core, for 0.9.1: `HandlerTenantMiddleware`, `WolverineTenantMessagePublisher`; test `TenantOverMessagesTests`, seen failing first |
| T-02 | A service had no way to call another service as itself: MP Core validated tokens and could not obtain one | MP Core, `MPCore.Resilience.Http` | added in MP Core, for 0.9.1: `AddMPCoreServiceIdentity` (OAuth 2.0 client credentials); `ServiceIdentityTests`, checked by breaking the code |
| T-03 | A generated backend has no test project, no user-secrets identifier and no development settings; nine of them were given each by a script | MP Core, the template | open: a proposal for the template |
| T-04 | A generated gRPC client cannot be the dependency of a handler: the client factory registers it with a lambda and its own implementation is not public, and Wolverine refuses both | Tiffin, Ordering | solved here: the client is built over `IHttpClientFactory`; a note for MP Core's documentation |
| T-05 | A service that calls another on behalf of a customer has no tenant in its own token; the called service records the change without a city | MP Core, a decision | **open, for the owner**: whether a tenant named by a trusted service is believed, and how |
| T-06 | The binary of a host was older than its first migration, and the host started with no table | Tiffin, the scripts | solved here: `scripts/run.sh` builds before it starts |
| T-07 | `AddCompression` takes the name of a column to order by and refuses `recorded_on_utc DESC`. The first reading of it was wrong: MP Core always compresses newest first, and TimescaleDB's own settings for `tracking.positions` say so (`orderby_asc` false). What was missing is that its documentation did not say which direction | MP Core, `MPCore.Persistence.Timescale` | documented in MP Core, for 0.9.1. A choice of direction waits until somebody needs oldest first |
| T-08 | An order waits for ever for a restaurant that never answers: a step of the process needs a deadline, and MP Core's publisher port cannot delay a message | MP Core, a decision | **open, for the owner**: MP Core lists "a delay set by the publisher" among what it deliberately does not do. A process with deadlines needs it, or a scheduler of its own |
| T-09 | After the identity provider was made anew its keys were new, and every service refused every token for minutes: ASP.NET Core asks for the keys again at most once in five minutes | Tiffin, operations | noted in `docs/running.md`: restart the services after Keycloak was recreated. Production rotates keys with an overlap |
| T-10 | With a second constructor on MP Core's publisher, Wolverine used the one without the tenant context, and every message left without its city. MP Core's own tests did not see it: their host knew its tenant from the ambient scope only. Tiffin's scenarios did, 20 checks at once | MP Core, `MPCore.Messaging.Wolverine` | fixed in MP Core, for 0.9.1: one constructor, on a publisher of its own; a test with a host that knows its tenant by itself, seen failing |
| T-11 | The first form of that fix changed the constructor of a published type. MP Core's API baseline refused the package | MP Core, the release gate | the gate did what it is for; the fix was made an addition |
| T-12 | Three checks of the scenarios were wrong, not the services: a variable of one scenario overwrote the address of a service for the next; a count was read from RabbitMQ's statistics, which lag; a service was stopped after the message it should have missed had arrived | Tiffin, the scenarios | corrected. A wait is now proved by two timestamps, not by a count of the broker |

## What was seen failing

A check that passes the first time proves that the check runs, not that it guards anything. For each
guarantee below the code was broken on purpose, the check was seen to fail, and the code was restored.

| Guarantee | How the code was broken | What failed |
|---|---|---|
| The tenant travels with a message (MP Core) | before the fix | `TenantOverMessagesTests`: the handler saw no tenant |
| A host that knows its tenant from a token publishes for it (MP Core) | a second constructor on the publisher | the third test of `TenantOverMessagesTests`; and in Tiffin, 20 checks of S1, S6 and S10 |
| A service's token is asked for once and kept (MP Core) | the cache always answered "none" | 2 of 15 tests of `ServiceIdentityTests` |
| A refused token is forgotten; a token is never sent in cleartext (MP Core) | both guards removed | 2 of 15 tests |
| A courier carries one order | the row version of the courier removed | scenario S8: one courier was given **six** orders at once, and none was cancelled |
| The restaurant's refusal gives the money back | the request for the refund removed | `OrderProcessTests`, 1 of 36 |
| A restaurant of another city does not exist for a customer | the comparison of cities removed | `PlaceOrderTests`, 1 of 36 |
| A reader declares only what its writer sends | a field added to a reader | contract tests, 1 of 43 |
| A writer and its reader name the same queue | a queue renamed on one side | contract tests, 1 of 43 |
| A copy of a contract file says what the original says | a field number changed in the copy | contract tests, 1 of 43 |
| The scenarios fail when a service is down | Dispatch stopped | scenario S0: 2 of 47 checks |
