# Running Tiffin

## Choose a local workflow

Tiffin supports three explicit local workflows. They use the same public source boundaries and product
contracts; the difference is which runtimes stay editable on the host.

| Workflow | Docker owns | Host owns | Required developer toolchain | Choose it when |
|---|---|---|---|---|
| **Backend Developer** | PostgreSQL, TimescaleDB, Kafka, RabbitMQ, Redis, Keycloak, APISIX, WireMock, and the selected S3 store | All nine .NET services | Docker, .NET `10.0.400`, `jq`, `curl`, `grpcurl`; protobuf tooling on Apple Silicon | You need breakpoints, service logs, migrations, or business-scenario work |
| **Frontend Developer** | The complete seeded backend above, including all nine services | Customer, Operations, both BFFs, and presentation runtimes | Docker, Node.js `24.19.0`, pnpm `11.25.0` | You need frontend hot reload and debugging without running .NET locally |
| **Full Demo** | Every backend and frontend runtime | Nothing | Git and Docker with at least 10 GB available | You want to evaluate the integrated product with the fewest steps |

The containerized modes build from the checked-out source. They do not download a prebuilt Tiffin
application image. RustFS is the default media store; `MEDIA_STORE=seaweedfs` selects the retained
alternative. The first image build can take several minutes and later runs reuse Docker layers.

### Clone the product boundaries

Keep these four repositories as siblings:

```text
workspace/
├── mpcore-tiffin-sample/
├── mpfrontend-tiffin-reference/
├── tiffin-keycloak/
└── tiffin-apisix/
```

```bash
git clone https://github.com/panahister/mpcore-tiffin-sample.git
git clone https://github.com/panahister/mpfrontend-tiffin-reference.git
git clone https://github.com/panahister/tiffin-keycloak.git
git clone https://github.com/panahister/tiffin-apisix.git
```

Advanced layouts may set `TIFFIN_FRONTEND_SOURCE_DIR`, `TIFFIN_KEYCLOAK_SOURCE_DIR`,
`TIFFIN_KEYCLOAK_PRODUCT_DIR`, `TIFFIN_KEYCLOAK_THEME_DIR`, and `TIFFIN_APISIX_PRODUCT_DIR`.

### Workflow A: Backend Developer

This is the most transparent path for a backend engineer. Infrastructure remains reproducible in
Docker, while every .NET process runs under the host debugger or the repository launcher.

```bash
cd mpcore-tiffin-sample
scripts/up.sh
scripts/setup.sh
scripts/run.sh all
scripts/scenarios.sh
```

`scripts/setup.sh` writes local addresses and development values into .NET user secrets outside the
repository. Open a service solution such as `ordering/Tiffin.Ordering.Backend.sln` to replace the
launcher with IDE debugging. Use `scripts/run.sh stop` before an IDE starts the same ports.

Stop without deleting infrastructure data:

```bash
scripts/run.sh stop
scripts/down.sh
```

Delete the local dependency data only when a clean scenario baseline is intentional:

```bash
scripts/run.sh stop
scripts/down.sh --volumes
```

### Workflow B: Frontend Developer

This path builds and starts identity, gateway, storage, messaging, the nine services, and the US demo
dataset in Docker. Ports `4411` and `4412` remain free for the editable frontend source.

```bash
cd mpcore-tiffin-sample
scripts/full-demo.sh up-backend

cd ../mpfrontend-tiffin-reference
pnpm install --frozen-lockfile
pnpm dev:product
```

`pnpm dev:product` starts Customer, Operations, and their two Security BFF processes. It uses Next.js
development mode, a shared local Redis validation profile, the generated local APISIX certificate, and
the containerized backend at the same browser-visible origins used by Full Demo. Press `Ctrl+C` to stop
the frontend processes; the backend and seeded data remain available.

```bash
cd ../mpcore-tiffin-sample
scripts/full-demo.sh down
```

### Workflow C: Full Demo

This is the evaluator path. One command builds every checked-out source boundary, waits for health,
runs the idempotent US seed through product APIs, and starts both web applications.

```bash
cd mpcore-tiffin-sample
scripts/full-demo.sh up
```

Open:

- Customer: `http://localhost:4411`
- Operations: `http://localhost:4412`
- Keycloak: `http://localhost:38180`
- APISIX: `https://localhost:39443`
- RustFS S3 API: `http://localhost:39000`

Inspect or stop the stack with:

```bash
scripts/full-demo.sh status
scripts/full-demo.sh logs
scripts/full-demo.sh down
```

`down` preserves databases, queues, identity, and media. `reset` removes the containers and named demo
volumes so the next `up` starts from a clean local state:

```bash
scripts/full-demo.sh reset
```

Add `--observability` to `up` or `up-backend` to include OpenTelemetry Collector, Prometheus, Grafana,
Jaeger, and Kafka UI. Use the same flag with `status`, `logs`, `down`, or `reset` when addressing that
profile. To run either containerized workflow with SeaweedFS:

```bash
MEDIA_STORE=seaweedfs scripts/full-demo.sh up
```

Do not run Backend Developer services and a containerized workflow at the same time: they intentionally
use the same service ports. Similarly, stop Full Demo before starting the host frontend on `4411` and
`4412`.

## Frontend authorization-code client

The imported public `tiffin-app` client supports authorization code with S256 PKCE. Its local
callbacks are exactly `http://localhost:4411/api/session/callback` and
`http://localhost:4412/api/session/callback`; origins are the corresponding two app roots.
`bash scripts/verify-frontend-client.sh` checks that contract locally and in CI. It does not alter a
live realm, grant roles or change token audiences. Existing password-grant scenario behavior is retained.
For an existing realm, inspect its client settings rather than assuming a modified import is applied.
Production must configure its own exact HTTPS callbacks/origins; this is a local reference profile.

## Product message languages

Each of the nine services has English, Simplified Chinese, Turkish and additive Arabic resources.
Request `Accept-Language: ar` or `ar-SA` on REST, or the corresponding language metadata on gRPC.
Regional Arabic falls back to the actual Arabic satellite; published Chinese/Turkish remain unchanged.
The canonical `messages.json` tables contain 79 product texts in every language with matching placeholders.
Notifications render their saved key/arguments at read time in the requested language, excluding
zero-quality preferences as specified in [RFC 9110 section 12.4.2](https://www.rfc-editor.org/rfc/rfc9110.html#section-12.4.2).
These additions do not translate generic messages owned by MP Core or change stored business records.

`bash scripts/tools/messages.sh --check` verifies the committed/generated resource relationship.
`bash scripts/test.sh --configuration Release -p:MPCoreSource=NuGet` verifies the independent package
consumer. The 2026-10-08 publication run passes all 313 tests: Access 38, Media 39, Restaurants 27,
Ordering 50, Payments 20, Kitchen 21, Dispatch 17, Tracking 21, Notifications 37 and Contracts 43.
Each missing Arabic satellite regression was first observed failing, as was the notification language defect.
Remote CI and full live transport/journey acceptance are separate gates.

## Backend Developer prerequisites

| | |
|---|---|
| .NET SDK | `10.0.400` (`global.json`) |
| Docker | with 10 GB of memory: twelve containers and nine services |
| `jq`, `curl`, `grpcurl` | for the scenarios |
| On Apple Silicon | `brew install protobuf grpc`, or Rosetta: the gRPC code generator that ships with .NET is built for Intel |

## The scripts

| Script | What it does |
|---|---|
| `scripts/up.sh` | starts the dependencies in Docker and waits until each is healthy. `--observability` adds the collector, Jaeger, Prometheus, Grafana and a browser for Kafka |
| `scripts/full-demo.sh up-backend` | builds, starts, health-checks, and seeds the complete backend in Docker while leaving frontend ports free |
| `scripts/full-demo.sh up` | builds, starts, health-checks, and seeds the complete backend and frontend in Docker |
| `scripts/full-demo.sh status` / `logs` | inspects the integrated containerized runtime |
| `scripts/full-demo.sh down` / `reset` | stops while preserving data / stops and deletes named demo volumes |
| `scripts/setup.sh` | writes addresses and development secrets into each service's user secrets, outside the repository. Once, and again after a port changed |
| `scripts/run.sh all` | builds the nine services, one after the other, starts them in the background and waits until each says it is ready. Logs in `tmp/logs` |
| `scripts/run.sh ordering` | one service, in the foreground |
| `scripts/run.sh stop` | stops what `all` started |
| `scripts/scenarios.sh` | every scenario; `scripts/scenarios.sh S1 S8` for some |
| `scripts/test.sh` | builds and tests every service and the contracts between them |
| `scripts/down.sh` | stops the dependencies and keeps their data; `--volumes` deletes it |
| `scripts/tools/messages.sh` | writes the message files of every service from their tables |

## Where everything listens

| | Address |
|---|---|
| The edge | `https://localhost:39443`, with `--cacert infrastructure/apisix/generated/localhost.crt` |
| Keycloak | `http://localhost:38180`, realm `tiffin`, administration `admin` / `admin` |
| The services | `6100` to `6900`, see the README |
| PostgreSQL | `localhost:35432`, one database per service, `tiffin` / `tiffin` |
| TimescaleDB | `localhost:35433` |
| Kafka | `localhost:39092` |
| RabbitMQ | `localhost:35672`; its management at `http://localhost:35673`, `tiffin` / `tiffin` |
| Redis | `localhost:36379` |
| The store of Media | `http://localhost:39000` |
| PayLane | `http://localhost:38081/__admin/requests` shows what it was asked |

The ports stay clear of Storefront's, so both samples run on one machine.

## A service, from an IDE

Open the service's solution, `ordering/Tiffin.Ordering.Backend.sln`. It builds alone. Run the `Api`
project with `ASPNETCORE_ENVIRONMENT=Development`; `appsettings.Development.json` and the user secrets
that `scripts/setup.sh` wrote are all it needs. The REST services describe themselves at
`/openapi-ui/` in Development, and the gRPC services answer reflection.

## Things that will happen to you

| What you see | Why | What to do |
|---|---|---|
| Every request is answered 401 after Keycloak was recreated | its keys are new, and a service asks for the keys again at most once in five minutes | restart the services |
| The first request to a service, right after `scripts/run.sh all` on a fresh `scripts/down.sh --volumes`, is answered 401 | its first check of a token raced Keycloak's own keys becoming current (`SecurityTokenSignatureKeyNotFoundException`); MP Core's `RefreshOnIssuerKeyNotFound` forces a fresh fetch on that failure | nothing: the very next call to the same service succeeds (T-15) |
| Media cannot upload after `MEDIA_STORE` was changed | Media remembers that its bucket exists; the other store has none yet | restart Media |
| A service starts and its tables are missing | it was started without being built after a migration was added | `scripts/run.sh` builds before it starts; `dotnet run --no-build` does not |
| `scripts/run.sh all` says a service is not ready | its log says why | `tmp/logs/<service>.log` |
| The cards that are refused | PayLane answers by the token: `tok_insufficient_funds`, `tok_card_expired`, `tok_psp_down` (always 503), `tok_flaky` (503 once, then approved) | |

## Where MP Core comes from

`Directory.Build.targets` decides, for every project of the repository:

| | |
|---|---|
| A clone of MP Core lies next to this repository | MP Core is built from its source. A fix in MP Core is picked up by the next build |
| There is none | the packages are restored from nuget.org |
| `-p:MPCoreSource=NuGet` or `-p:MPCoreSource=Local` | says which, for one build |

Tiffin needs `0.9.3`, which is on nuget.org. With older packages it builds up to the calls that are new:
`AddMPCoreServiceIdentity` and the tenant of a message in `0.9.1`, the tenant of a call in `0.9.2`, a
delay set by the publisher in `0.9.3`.
