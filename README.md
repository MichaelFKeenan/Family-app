# Family Board

A family to-do board, permanent shopping list and points leaderboard. Built as a learning project in system design.

Design doc and decision log: https://claude.ai/code/artifact/d76d14ee-5270-4a19-b9ba-601eaaa58972

## Status

_Last updated: 13 Oct 2026. Update this section in the same commit that finishes a step._

**Live:** https://mfk-family-board.fly.dev (Fly.io, London, one machine; Neon Postgres, London). Every green push to `main` deploys automatically.

**Done**

- Phase 1, step 1: walking skeleton. Modular .NET host with five empty modules, health endpoints, React status page, CI, automatic deploy.
- Phase 1, step 2: Identity. Google sign-in, users and login identities, 30-day cookie session, `GET /api/identity/me`. Plain SQL migrations run by Fly's release command. Cookie-encryption keys stored in Postgres.
- Phase 1, step 3: Households. Create a household, single-use hashed invite links, members list, `.RequireHousehold()` resolving the current household per request, isolation tests.
- Phase 1, step 4: Tasks. Board (to do, doing, done) with priority, points, category, assignee and due date; start, complete, reopen, edit, delete; categories with defaults. Household isolation by EF global query filter plus a composite foreign key; optimistic concurrency on a `version` column (ADR-028).
- Phase 1, step 5: domain events. Every write request is one transaction shared by all modules; events raised during it are logged to `platform.activity_events` and dispatched to handlers before commit. Default categories come from a `HouseholdCreated` handler. Recent-activity feed (ADR-030, ADR-031).
- Phase 1, step 6: Points. Append-only `points.ledger` fed only by task events: completion pays the assignee (or completer), reopen and delete reverse whatever each person holds for that task. Leaderboard for this week, this month and all time; history endpoint; one-off backfill from the activity log (ADR-032, ADR-033). **Built and tested, not yet deployed** (see open questions).

**Open questions**

- Fly's free trial has ended and deploys fail until a card is added at https://fly.io/trial. The live site may also be stopped. Once added, re-run the latest CI run to deploy step 6.

**Next**

- Deploy step 6, then confirm on the live site: sign-in, household, invite, board, activity feed, leaderboard.
- Phase 1, step 7: Shopping list. Permanent list, add and tick off items, ticked items kept for history (ADR-010).

**Follow-ups**

- [ ] Reset the Neon database password (it was shared in a chat), then update the `NEON_CONNECTION_STRING` GitHub secret.
- [ ] Replace the organization-wide `FLY_API_TOKEN` with an app-scoped deploy token (ADR-020).
- [ ] Commit `web/package-lock.json` after the first local `npm install`, so builds are reproducible.

**Decisions not visible in the code**

- Points go straight to whoever completes a task; no approval step (ADR-016).
- Children don't sign in yet: v1 is Google sign-in for adults only. Child support is deferred, likely as a parent-managed login.
- Hosting stays on a container, not serverless, because SignalR needs long-lived connections (ADR-018).
- Modules stay strictly separated (confirmed 12 Oct 2026, ADR-001). Cross-module reads use SharedKernel interfaces; changes will use events (ADR-025).
- Who may sign in is controlled by Google, not our code: the OAuth consent screen stays in Testing mode, and only its listed test users can sign in (ADR-024). To add a family member, add their Google email there.

## Run it locally

You need the .NET 10 SDK, Node 22 and Docker.

```
docker compose up -d
dotnet run --project src/Host
```

Migrations run automatically on startup in Development.

To sign in locally, give the host your Google OAuth client (stored outside the repo):

```
dotnet user-secrets --project src/Host set "Authentication:Google:ClientId" "<client id>"
dotnet user-secrets --project src/Host set "Authentication:Google:ClientSecret" "<client secret>"
```

In a second terminal:

```
cd web
npm install
npm run dev
```

Open http://localhost:5173. Google must list `http://localhost:5173/signin-google` as a redirect URI.

## Test

```
dotnet test FamilyApp.slnx
```

Integration tests start their own throwaway Postgres in Docker.

## Deploy (Fly.io + Neon)

Deployment is automatic: every green push to `main` deploys (ADR-020). Migrations run first, in Fly's release command (ADR-021). One-off setup:

1. Create a Neon project (Postgres only, no Neon Auth). Copy the pooled connection string in .NET format.
2. Create a Fly.io account and an organization access token.
3. In Google Cloud, create an OAuth client (Web application) with redirect URI `https://mfk-family-board.fly.dev/signin-google`. Keep the consent screen in Testing mode and add each family member as a test user.
4. In GitHub, Settings → Secrets and variables → Actions, add:
   - `FLY_API_TOKEN`: the Fly token
   - `NEON_CONNECTION_STRING`: the Neon string
   - `GOOGLE_CLIENT_ID` and `GOOGLE_CLIENT_SECRET`: the OAuth client
5. Push to `main`, or re-run the latest CI run. The deploy job creates the Fly app on first run.
6. Visit https://mfk-family-board.fly.dev/api/health.

Without the Fly and Neon secrets, the deploy job skips itself and CI stays green. Without the Google secrets, the app runs but sign-in answers 503.
