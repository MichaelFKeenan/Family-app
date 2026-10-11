# Family Board

A shared task board, shopping list and points leaderboard for one family. It is also a learning project: system design and its trade-offs matter more than shipping speed.

## The design doc is the source of truth

All architecture decisions live as numbered ADRs in the design doc:
https://claude.ai/code/artifact/d76d14ee-5270-4a19-b9ba-601eaaa58972

- Before making a decision that would be hard to reverse, or that a new developer would need explained, propose an ADR in that doc (Context, Decision, Alternatives, Trade-offs, Revisit when).
- Explain the trade-offs to the owner as you go. That is the point of the project.
- Never delete an ADR. A changed decision is a new ADR that marks the old one Superseded.

## Stack

- .NET 10, ASP.NET Core minimal APIs, PostgreSQL via Npgsql (EF Core arrives with the first real module).
- React, TypeScript and Vite in `web/`, TanStack Query for server state.
- One Docker image: the host serves both `/api` and the built React app (same origin, ADR-008, ADR-012).
- Hosting: Fly.io plus Neon Postgres (ADR-015, ADR-018).

## Layout

```
src/Host/            the one deployable; wiring only, no business logic
src/SharedKernel/    types every module may use (IModule, later domain events)
src/Modules/<Name>/  one project per module: Identity, Households, Tasks, Shopping, Points
tests/Architecture.Tests/  module boundary rules
tests/Integration.Tests/   real HTTP against a real Postgres (Testcontainers)
web/                 React app
```

## Rules

- Modules never reference each other. They communicate through domain events (ADR-004). `ModuleBoundaryTests` enforces this.
- Each module implements `IModule` and is listed explicitly in `Program.cs`. Its endpoints live under `/api/{module.Name}`.
- Every household-owned table has `household_id` and is filtered by it (ADR-009).
- Primary keys are UUIDv7 (`Guid.CreateVersion7()`, ADR-013).
- No secrets in the repo; it is public. Production configuration comes from Fly secrets.
- Platform probes use `/api/health/live`, which never touches the database (ADR-019).

## Commands

```
docker compose up -d                       # local Postgres
dotnet run --project src/Host              # API on http://localhost:5080
cd web && npm install && npm run dev       # React on http://localhost:5173, proxies /api
dotnet test FamilyApp.slnx                 # needs Docker for integration tests
```

CI (`.github/workflows/ci.yml`) builds and tests every push. Keep it green.
