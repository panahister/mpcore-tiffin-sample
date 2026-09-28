# Tiffin: instructions for Claude Code

This repository is the second sample for MP Core: nine services that work together to deliver food in
two cities. Read this file first, then the instructions of the service you are about to change.

## The layout

| Folder | What it owns | Its own instructions |
|---|---|---|
| `access/` | decisions about roles; the only client of Keycloak's administration | `access/AGENTS.md`, `access/CLAUDE.md` |
| `media/` | every file of the platform | `media/AGENTS.md`, `media/CLAUDE.md` |
| `restaurants/` | restaurants, menus, prices | `restaurants/AGENTS.md`, `restaurants/CLAUDE.md` |
| `ordering/` | orders, and the saga that moves them | `ordering/AGENTS.md`, `ordering/CLAUDE.md` |
| `payments/` | the money of an order; called by services only | `payments/AGENTS.md`, `payments/CLAUDE.md` |
| `kitchen/` | the restaurant's word on an order | `kitchen/AGENTS.md`, `kitchen/CLAUDE.md` |
| `dispatch/` | couriers, and who carries what | `dispatch/AGENTS.md`, `dispatch/CLAUDE.md` |
| `tracking/` | where a courier is | `tracking/AGENTS.md`, `tracking/CLAUDE.md` |
| `notifications/` | what a customer is told | `notifications/AGENTS.md`, `notifications/CLAUDE.md` |
| `tests/Tiffin.Contracts.Tests/` | what holds the messages between the nine together | |
| `scripts/`, `infrastructure/` | the dependencies, and the scenarios that run against the services | |
| `docs/` | the business, the architecture, what MP Core contributes, what was learned | |

Each service is a repository of its own in everything but location. It has its own
`.mpcore/template-manifest.json`, its own skills in `.mpcore/skills`, and its own guides in `docs/`.

## Skills

| Where you were started | The skills you find | What they are |
|---|---|---|
| At the root of this repository | ten, named `tiffin-...`, in `.claude/skills` | Each finds the service a task belongs to and hands over to that service's skill |
| In a service's folder | ten, named `mpcore-...`, in `<service>/.claude/skills` | MP Core's skills, written for that service's shape, transport and broker |

Either way the procedure is in one place: `<service>/.mpcore/skills/<name>/SKILL.md`. Read it before you
act. `scripts/agents/sync-skills.py` writes the root skills; edit that file, not what it writes.

## Start every task this way

1. **Decide which service the task belongs to**, and read that service's instructions and manifest. A
   task that touches two services is two changes and a contract between them.
2. **Read the rule before the code.** [docs/business.md](docs/business.md) lists every business rule by
   its code and the service that owns it. A rule that is not there does not exist: ask, do not invent.
3. **Select the one skill that fits**, and read its body in the service's `.mpcore/skills`.
4. **Change one thing**, in the service that owns it.
5. **Prove it.** Run the service's tests, and the scenarios that touch what you changed
   (`scripts/scenarios.sh S1 S8`). Report the real output, failures included.

## Rules of this repository

- **The nine services share no code.** Never add a project reference from one service to another, and
  never a shared contracts library. A reader declares its own copy of a message, and
  `tests/Tiffin.Contracts.Tests` holds the copies together. That test project is the only one that may
  reference more than one service.
- **A service writes only its own data.** Another service is told by a message.
- **A defect of MP Core is not worked around here.** Say what you found, with the smallest case that
  shows it, and stop. MP Core is fixed in its own repository.
- **A handler never calls `SaveChangesAsync`.** MP Core saves, and commits the messages with the change.
- **A query only reads.** It declares no unit of work, publishes nothing, and is the only thing a `GET`
  sends.
- **A message that carries something personal or secret overrides `ToString`.** A failed message is
  printed into the log.
- **A guarantee is proved by a test that was seen failing, or by an experiment against the running
  system.** Do not write that something is guaranteed because a document says so.
- **Documentation is in English**, and a convention names its source.
- **Nothing is committed, pushed or published without the owner's word.**

## Commands

| To | Run |
|---|---|
| start the dependencies | `scripts/up.sh` (`--observability` adds tracing, metrics, dashboards and the Kafka browser) |
| write local settings, once | `scripts/setup.sh` |
| run every service | `scripts/run.sh all` |
| run one service | `scripts/run.sh ordering` (or any of the nine) |
| stop the services | `scripts/run.sh stop` |
| test everything | `scripts/test.sh` |
| run the scenarios | `scripts/scenarios.sh`, or some: `scripts/scenarios.sh S1 S8` |
| stop the dependencies | `scripts/down.sh` (`--volumes` deletes their data) |
| regenerate the message files | `scripts/tools/messages.sh` |
