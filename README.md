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

1. Create a free Neon project and copy its pooled connection string.
2. Rename `app` in `fly.toml` to a unique name, then `fly launch --no-deploy`.
3. `fly secrets set ConnectionStrings__Postgres="<Npgsql connection string>"`.
4. `fly deploy`.
5. Visit `https://<app>.fly.dev/api/health`.
