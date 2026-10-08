# Tiffin backend conventions

This is the day-to-day coding contract for Tiffin's nine services. It applies the reusable
[MP Core backend conventions](https://github.com/panahister/mpcore/blob/main/docs/BACKEND-CONVENTIONS.md)
to this repository's service boundaries, files, contracts, and scenario evidence.

Read it before adding an endpoint, command, query, rule, integration, event, migration, or shared
technical behavior.

## Choose the owner before writing code

Each Tiffin service is one bounded context and owns its database. Start with the business decision, not
the endpoint that happens to expose it.

| Question | Owner |
|---|---|
| Who may receive a product role in a city? | Access |
| What file is valid and available? | Media |
| What is on a menu and what does it cost now? | Restaurants |
| What is the state of an order and its saga? | Ordering |
| Was money accepted or refunded? | Payments |
| Will the restaurant prepare the order? | Kitchen |
| Which courier carries which delivery? | Dispatch |
| What is the latest accepted courier position? | Tracking |
| What should the customer be told? | Notifications |

If a feature needs more than one row, keep each decision with its owner and integrate through a versioned
call or message. The services share no domain or contract assembly and never read another service's
database.

```mermaid
flowchart LR
  STORY[Approved behavior] --> SERVICE[Owning service]
  SERVICE --> DOMAIN[Domain invariant and transition]
  DOMAIN --> APP[Application command or query]
  APP --> PORTS[Application ports]
  PORTS --> INFRA[Persistence and external adapters]
  INFRA --> API[REST or gRPC composition]
  API --> CONTRACT[Contract and architecture tests]
  CONTRACT --> SCENARIO[Focused then complete scenario]
```

## The service-internal boundary

Every service uses the same project roles:

```text
<service>/src/
├── Tiffin.<Service>.Domain/           aggregates, values, rules, domain events
├── Tiffin.<Service>.Application/      commands, queries, handlers, ports, views, failures
├── Tiffin.<Service>.Infrastructure/   EF mappings, migrations, remote clients, provider adapters
└── Tiffin.<Service>.Api/              REST/gRPC endpoints, policies, channels, host composition
```

| Decision | Correct location |
|---|---|
| An order cannot move from this state to that state | `Domain` aggregate/rule |
| A use case loads an order, checks the current actor, calls ports, and publishes work | `Application` handler |
| Restaurants or Payments is called through HTTP/gRPC | `Application` port plus `Infrastructure` adapter |
| An aggregate is mapped to PostgreSQL | `Infrastructure` persistence configuration |
| A REST path returns `202` or a gRPC method maps a failure | `Api` transport adapter |
| A queue/topic, retry, dead-letter rule, or authorization policy is composed | `Api/Hosting` |
| A technical guarantee is missing in every service | Fix and prove it in MP Core; do not copy a workaround into nine services |

Validators reject malformed input. Aggregates protect invariants. Handlers coordinate use cases and
resource-level authority. Endpoint policy protects the operation. These responsibilities complement one
another and are not interchangeable.

## Standard Tiffin slice path

1. Add or update the rule in `docs/business.md` with a concrete success and refusal case when behavior
   changes.
2. Name the owning service, actor, city behavior, aggregate, and observable outcome.
3. Define the REST/gRPC or versioned message contract and compatibility impact.
4. Implement domain transition first, then application command/query and ports, then infrastructure, then
   endpoint/host composition.
5. Keep actor and city sourced from validated context. Caller-controlled input never chooses authority.
6. Keep one command transaction: state, outbox work, audit intent, and idempotency record commit together
   where required.
7. Add domain/handler tests, endpoint contract tests, message-copy contract tests, and architecture tests
   for every boundary touched.
8. Build and test the service in Release, then run the focused scenario and finally the complete S0-S15
   suite when the change crosses services.

Typical focused verification is:

```bash
dotnet build ordering/Tiffin.Ordering.Backend.sln --configuration Release
dotnet test ordering/Tiffin.Ordering.Backend.sln --configuration Release --no-build
scripts/scenarios.sh S1 S5 S7
```

Replace `ordering` and the scenario set with the owner of the change. Before repository review:

```bash
scripts/test.sh --configuration Release -p:MPCoreSource=NuGet
scripts/scenarios.sh
```

Use [Hybrid Mode](running.md#mode-1-hybrid-mode) for host breakpoints against the real identity, gateway,
brokers, databases, storage, and frontend. Do not replace a failing integration with a mock and call it
connected evidence.

## Real flow 1: place an order

| Boundary | Tiffin implementation |
|---|---|
| Request shape | `ordering/src/Tiffin.Ordering.Application/Commands/PlaceOrder.cs` |
| Input validation | `ordering/src/Tiffin.Ordering.Application/Validators/PlaceOrderValidator.cs` |
| Domain policy | `ordering/src/Tiffin.Ordering.Domain/Order.cs`, `DeliveryAddress.cs`, and `Rules/OrderingRule.cs` |
| Application orchestration | `PlaceOrderHandler` obtains actor/city from validated context, asks Restaurants for the current quote, asks Payments for a token reference, stores the order, and publishes `PaymentRequested` in one transaction |
| Integration ports/adapters | `Application/Ports/Ports.cs`; `Infrastructure/Services/RestaurantQuotesClient.cs` and `PaymentIntentsClient.cs` |
| Transport | `ordering/src/Tiffin.Ordering.Api/Rest/Endpoints/OrderEndpoints.cs`, success `202` and idempotent execution |
| Focused tests | `ordering/tests/Tiffin.Ordering.Tests/PlaceOrderTests.cs`, `OrderTests.cs`, and `EndpointContractTests.cs` |
| Connected evidence | S1 success; S2-S4/S7 compensation and refusal; S5 idempotency; S6 city isolation; S14 outages |

The endpoint does not price the order, mutate the aggregate, or call a provider. The handler does not
build an HTTP response. The aggregate does not know Restaurants, Payments, Wolverine, or ASP.NET Core.

## Real flow 2: reserve, upload, and confirm media

| Boundary | Tiffin implementation |
|---|---|
| Commands | `media/src/Tiffin.Media.Application/Commands/MediaCommands.cs`: `ReserveUpload` and `ConfirmUpload` |
| Validation and rules | `Application/Validators/ReserveUploadValidator.cs`; `Domain/MediaFile.cs`, `Purposes.cs`, and `Rules/MediaRule.cs` |
| Storage port | `media/src/Tiffin.Media.Application/Ports/Ports.cs` owns `IObjectStore` |
| S3 adapter | `media/src/Tiffin.Media.Infrastructure/Store/S3ObjectStore.cs` implements the port for the selected S3-compatible store |
| REST contract | `media/src/Tiffin.Media.Api/Rest/Endpoints/MediaEndpoints.cs`: reserve `201`, bodyless confirm `200` |
| Focused tests | `media/tests/Tiffin.Media.Tests/MediaTests.cs` and `EndpointContractTests.cs` |
| Connected evidence | S11 plus application-facing RustFS/SeaweedFS compatibility and origin controls |

Media decides the object key, purpose, content type, size, expiry, ownership, and confirmation state. The
browser moves bytes directly to the signed store address. Neither Restaurants nor the frontend receives
S3 credentials, and no other service becomes a file authority.

## Cross-service change checklist

- [ ] Each service still owns and declares its side of the contract.
- [ ] The contract has an explicit version and route/channel owner.
- [ ] A synchronous call is required by current truth; otherwise an event-carried copy was considered.
- [ ] An async consumer is idempotent and has explicit retry/dead-letter behavior.
- [ ] Timeout, cancellation, compensation, and late/duplicate delivery behavior are specified.
- [ ] Tenant propagation is trusted only from the approved service identity.
- [ ] `tests/Tiffin.Contracts.Tests` holds independently declared copies together.
- [ ] The focused outage/refusal scenario and complete suite report real counts.

## Developer and agent review checklist

- [ ] The business rule, owner, actor, city, and acceptance examples are explicit.
- [ ] Domain protects state; Application orchestrates; Infrastructure adapts; API composes.
- [ ] No service assembly or database is shared.
- [ ] No provider type leaks through an application port.
- [ ] Expected failures use stable product failure identities; exceptions represent unexpected states.
- [ ] Sensitive request, message, and provider values have safe logging behavior.
- [ ] Authorization and resource ownership are tested, not inferred from frontend visibility.
- [ ] Unit, endpoint, contract, architecture, Release, and connected evidence match the changed boundary.
- [ ] A framework defect was not hidden with a Tiffin workaround.

