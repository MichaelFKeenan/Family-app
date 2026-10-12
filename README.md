# Family Board

A family to-do board, permanent shopping list and points leaderboard. Built as a learning project in system design.

Design doc and decision log: https://claude.ai/code/artifact/d76d14ee-5270-4a19-b9ba-601eaaa58972

## Status

_Last updated: 12 Oct 2026. Update this section in the same commit that finishes a step._

**Live:** https://mfk-family-board.fly.dev (Fly.io, London, one machine; Neon Postgres, London). Every green push to `main` deploys automatically.

**Done**

- Phase 1, step 1: walking skeleton. Modular .NET host with five empty modules, health endpoints, React status page, CI, automatic deploy.

**Next**

- Phase 1, step 2: Identity. Google sign-in, users and login identities, cookie session, `GET /api/me` (ADR-007, ADR-008).

**Follow-ups**

- [ ] Reset the Neon database password (it was shared in a chat), then update the `NEON_CONNECTION_STRING` GitHub secret.
- [ ] Replace the organization-wide `FLY_API_TOKEN` with an app-scoped deploy token (ADR-020).
- [ ] Commit `web/package-lock.json` after the first local `npm install`, so builds are reproducible.

**Decisions not visible in the code**

- Points go straight to whoever completes a task; no approval step (ADR-016).
- Children don't sign in yet: v1 is Google sign-in for adults only. Child support is deferred, likely as a parent-managed login.
- Hosting stays on a container, not serverless, because SignalR needs long-lived connections (ADR-018).

## Run it locally

You need the .NET 10 SDK, Node 22 and Docker.

```
docker compose up -d
dotnet run --project src/Host
```

In a second terminal:

```
cd web
npm install
npm run dev
```

Open http://localhost:5173. The status card should show the API as reachable and Postgres as Healthy.

## Test

```
dotnet test FamilyApp.slnx
```

Integration tests start their own throwaway Postgres in Docker.

## Deploy (Fly.io + Neon)

Deployment is automatic: every green push to `main` deploys (ADR-020). One-off setup:

1. Create a Neon project (Postgres only, no Neon Auth). Copy the pooled connection string in .NET format.
2. Create a Fly.io account and an organization access token.
3. In GitHub, Settings → Secrets and variables → Actions, add:
   - `FLY_API_TOKEN`: the Fly token
   - `NEON_CONNECTION_STRING`: the Neon string
4. Push to `main`, or re-run the latest CI run. The deploy job creates the Fly app on first run.
5. Visit https://mfk-family-board.fly.dev/api/health.

Without those secrets, the deploy job skips itself and CI stays green.
