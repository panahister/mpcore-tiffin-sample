# Findings

What building Tiffin found, in the order it was found. A defect of MP Core is fixed in MP Core, never
worked around here; a finding that is a decision waits for the owner.

| # | Found | Where | State |
|---|---|---|---|
| T-01 | A message carried no tenant, and a handler that received one worked for nobody: the audit trail of a consumer recorded no city | MP Core, `MPCore.Messaging.Wolverine` | fixed in MP Core, for 0.9.1: `HandlerTenantMiddleware`, `WolverineTenantMessagePublisher`; test `TenantOverMessagesTests`, seen failing first |
| T-02 | A service had no way to call another service as itself: MP Core validated tokens and could not obtain one | MP Core, `MPCore.Resilience.Http` | added in MP Core, for 0.9.1: `AddMPCoreServiceIdentity` (OAuth 2.0 client credentials); `ServiceIdentityTests`, checked by breaking the code |
| T-03 | A generated backend has no test project, no user-secrets identifier and no development settings; nine of them were given each by a script | MP Core, the template | fixed in MP Core (mpcore#23): a fresh GUID as `UserSecretsId`, `appsettings.Development.json`, and `tests/MPCore.Backend.Tests` with a health-check test for every shape and Clean-Architecture layering tests for shape `service` |
| T-04 | A generated gRPC client cannot be the dependency of a handler: the client factory registers it with a lambda and its own implementation is not public, and Wolverine refuses both | Tiffin, Ordering | solved here: the client is built over `IHttpClientFactory`; a note for MP Core's documentation |
| T-05 | A service that calls another on behalf of a customer has no tenant in its own token; the called service records the change without a city | MP Core, a decision | decided by the owner (the tenant in `x-tenant-id`, believed only from listed services) and added to MP Core in `0.9.2`: `AddMPCoreTenantPropagation`, `TrustedServiceClients`; ADR-014, addendum. Payments' audit trail now names the city of all 20 payments of the scenarios, and names none when Ordering is taken off its list |
| T-06 | The binary of a host was older than its first migration, and the host started with no table | Tiffin, the scripts | solved here: `scripts/run.sh` builds before it starts |
| T-07 | `AddCompression` takes the name of a column to order by and refuses `recorded_on_utc DESC`. The first reading of it was wrong: MP Core always compresses newest first, and TimescaleDB's own settings for `tracking.positions` say so (`orderby_asc` false). What was missing is that its documentation did not say which direction | MP Core, `MPCore.Persistence.Timescale` | documented in MP Core, for 0.9.1. A choice of direction waits until somebody needs oldest first |
| T-08 | An order waits for ever for a restaurant that never answers: a step of the process needs a deadline, and MP Core's publisher port cannot delay a message | MP Core, a decision | decided by the owner (a delay set by the publisher) and added to MP Core for `0.9.3`: `MessageDeliveryContext.DeliverAfter`, ADR-015. Ordering gives a restaurant ten minutes; scenario S15 proves it with eight seconds, and that an order answered in time is left alone |
| T-09 | After the identity provider was made anew its keys were new, and every service refused every token for minutes: ASP.NET Core asks for the keys again at most once in five minutes | Tiffin, operations | noted in `docs/running.md`: restart the services after Keycloak was recreated. Production rotates keys with an overlap |
| T-10 | With a second constructor on MP Core's publisher, Wolverine used the one without the tenant context, and every message left without its city. MP Core's own tests did not see it: their host knew its tenant from the ambient scope only. Tiffin's scenarios did, 20 checks at once | MP Core, `MPCore.Messaging.Wolverine` | fixed in MP Core, for 0.9.1: one constructor, on a publisher of its own; a test with a host that knows its tenant by itself, seen failing |
| T-11 | The first form of that fix changed the constructor of a published type. MP Core's API baseline refused the package | MP Core, the release gate | the gate did what it is for; the fix was made an addition |
| T-12 | Three checks of the scenarios were wrong, not the services: a variable of one scenario overwrote the address of a service for the next; a count was read from RabbitMQ's statistics, which lag; a service was stopped after the message it should have missed had arrived | Tiffin, the scenarios | corrected. A wait is now proved by two timestamps, not by a count of the broker |
| T-13 | The first run on GitHub failed twelve checks, all in S14, with both stores. S14 stops three services and starts them again with `scripts/run.sh`; the workflow gave `TIFFIN_BIND` and `DOTNET_ARGS` to the step that starts the services only, so a service started again ran a Debug build that CI never made (`bin/Debug/net10.0/Tiffin.Restaurants.Api: No such file or directory`) | Tiffin, the workflow | solved here: the scenarios job sets both for every step. The next run passed 266 of 266 checks with each store |
| T-14 | Payments recorded Ordering as a user, not a service. Keycloak 26 writes `client_id` only for a client with the `service_account` scope, and the realm file gave its five service clients no scopes; MP Core counts a caller as a service only with that claim. So the documents said something the audit trail did not, and T-05's header would have been refused | Tiffin, the realm | solved here: every service client lists the scopes Storefront's does, `service_account` included. MP Core's summary of the rule was wrong too, and says it now |
| T-15 | On a fresh `scripts/down.sh --volumes` and `scripts/up.sh`, three of the nine services — always the third, sixth and ninth that `scripts/run.sh all` builds and starts (Restaurants, Kitchen, Notifications) — answer a valid token with 401 the first time a scenario asks them, though Keycloak's own health check and its JWKS endpoint already answer correctly by then. Reproduces the same way with an unrelated change fully reverted, so it is not that change. A direct call to the same service, made by hand moments later, succeeds | Tiffin, startup | open: looks like T-09's family (a service's own OIDC metadata fetched too early), but not explained — why always the third, sixth and ninth, and why a hand-made call recovers at once. Reproduce with `scripts/down.sh --volumes; scripts/up.sh; scripts/setup.sh; scripts/run.sh all; scripts/scenarios.sh S0` |

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
