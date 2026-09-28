# Running Tiffin

## What you need

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

Tiffin needs `0.9.2`, which is on nuget.org. With older packages it builds up to the calls that are new:
`AddMPCoreServiceIdentity` and the tenant of a message in `0.9.1`, the tenant of a call in `0.9.2`.
