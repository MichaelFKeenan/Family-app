# Family Board

A family to-do board, permanent shopping list and points leaderboard. Built as a learning project in system design.

Design doc and decision log: https://claude.ai/code/artifact/d76d14ee-5270-4a19-b9ba-601eaaa58972

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
