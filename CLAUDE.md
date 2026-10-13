# Family Board

A shared task board, shopping list and points leaderboard for one family. It is also a learning project: system design and its trade-offs matter more than shipping speed.

## Start here: project status

Read the **Status** section of `README.md` before doing anything. It holds what is done, what is next, open questions for the owner, and follow-ups, including things that are not visible in the code (deploy setup, secrets, pending decisions).

- When you finish a step, resolve a question or a follow-up, update that section in the same commit.
- Keep it short: done, next, open questions, follow-ups. Reasoning belongs in the design doc, not there.
- If you cannot open the design doc from your environment, the Status section and the ADR numbers referenced in code and docs are your context. Ask the owner rather than guessing at a decision.

## The design doc is the source of truth for decisions

All architecture decisions live as numbered ADRs in the design doc:
https://claude.ai/code/artifact/d76d14ee-5270-4a19-b9ba-601eaaa58972

- Before making a decision that would be hard to reverse, or that a new developer would need explained, propose an ADR in that doc (Context, Decision, Alternatives, Trade-offs, Revisit when).
- Explain the trade-offs to the owner as you go. That is the point of the project.
- Never delete an ADR. A changed decision is a new ADR that marks the old one Superseded.

## Stack

- .NET 10, ASP.NET Core minimal APIs, PostgreSQL. EF Core (Npgsql provider) for queries and changes, one DbContext per module, but the schema comes from plain SQL migration scripts, not EF migrations (ADR-021).
- Sign-in: Google via the OpenID Connect handler, then our own 30-day cookie holding only our user id and name (ADR-022). Read the current user with `principal.TryGetUserId(...)` from SharedKernel.
- React, TypeScript and Vite in `web/`, TanStack Query for server state.
- One Docker image: the host serves both `/api` and the built React app (same origin, ADR-008, ADR-012).
- Hosting: Fly.io plus Neon Postgres (ADR-015, ADR-018).

## Layout

```
src/Host/            the one deployable; wiring only, no business logic
src/SharedKernel/    types every module may use: IModule, cross-module contracts, event records, DbSession
src/Modules/<Name>/  one project per module: Identity, Households, Tasks, Shopping, Points
tests/Architecture.Tests/  module boundary rules
tests/Integration.Tests/   real HTTP against a real Postgres (Testcontainers)
web/                 React app
```

## Rules

- Write requests (POST, PUT, PATCH, DELETE under `/api`) each run in one Postgres transaction on a per-request connection shared by every module (`DbSession`, ADR-030). Module DbContexts derive from `ModuleDbContext` and are registered with `AddModuleDbContext<T>()`; never open your own transaction in a module. Before raw SQL or `ExecuteUpdate` in a write, call `db.JoinRequestTransaction()`.
- Events: after a successful change, `DomainEvents.Raise(new SomethingHappened(...))`. Event records live in `SharedKernel/Events/Contracts.cs` and are a public contract: add fields, never rename or remove (the activity log keeps their JSON). React to another module's event with a `DomainEventHandler<TEvent>` registered as `IDomainEventHandler`. Handlers run before commit; if one throws, the whole request rolls back (ADR-031). Only write requests dispatch events; a GET must never raise one.
- Modules never reference each other. `ModuleBoundaryTests` enforces this. To read another module's data, depend on a read-only interface in SharedKernel that the owning module implements (`IHouseholdMembership`, `IUserDirectory`). To react to another module's changes, use domain events (ADR-004, ADR-025).
- Household-scoped endpoints call `.RequireHousehold()`, then read `CurrentHousehold.Id`. It resolves the `X-Household-Id` header, or the user's only household, and checks membership on every request (ADR-026). Module DbContexts for household-owned data apply a global query filter on `CurrentHousehold` (ADR-009).
- Household-owned entities implement `IHouseholdOwned`; the module DbContext stamps `HouseholdId` on insert and refuses changes to other households' rows. Tables reference related household data by composite key `(household_id, id)`, so the database rejects cross-household links too (ADR-028). See `TasksDbContext` as the pattern.
- State changes on an aggregate are separate command endpoints (`/start`, `/complete`), not a generic status PATCH. Edits send the `version` the client saw; a stale one gets 409 (ADR-014).
- JSON enums are lower-case strings (`"todo"`). Query-string enums must be parsed case-insensitively by hand; minimal APIs bind them case-sensitively.
- The points ledger is append-only: corrections are new entries, never edits or deletes. Points are only ever written by event handlers, never by an endpoint (ADR-032).
- Date ranges from the browser (`from`, `to`) are ISO timestamps computed in the viewer's time zone and must be URL-encoded (`+` in an offset otherwise becomes a space).
- Each module implements `IModule` and is listed explicitly in `Program.cs`. Its endpoints live under `/api/{module.Name}`.
- Every household-owned table has `household_id` and is filtered by it (ADR-009).
- Primary keys are UUIDv7 (`Guid.CreateVersion7()`, ADR-013).
- No secrets in the repo; it is public. Production configuration comes from Fly secrets.
- Platform probes use `/api/health/live`, which never touches the database (ADR-019).
- Schema changes: add a new numbered script, `src/Modules/<Name>/Migrations/NNNN_description.sql`. Never edit a script that has been deployed; write a new one. Each module owns its own Postgres schema; no foreign keys across modules. Keep the module's EF mapping in step with its scripts; integration tests catch mismatches.
- Migrations run on startup in Development and tests (`Database:MigrateOnStartup`), and in production only in Fly's release command (`dotnet FamilyApp.Host.dll migrate`).

## Commands

```
docker compose up -d                       # local Postgres
dotnet run --project src/Host              # API on http://localhost:5080
cd web && npm install && npm run dev       # React on http://localhost:5173, proxies /api
dotnet test FamilyApp.slnx                 # needs Docker for integration tests
```

CI (`.github/workflows/ci.yml`) builds and tests every push, and deploys green pushes to `main`. Keep it green. Test totals appear as notices on the run, which is handy where raw logs can't be downloaded.
