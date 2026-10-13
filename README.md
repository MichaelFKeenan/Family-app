# Family Board

A family to-do board, permanent shopping list and points leaderboard. Built as a learning project in system design.

Design doc and decision log: https://claude.ai/code/artifact/d76d14ee-5270-4a19-b9ba-601eaaa58972

## Status

_Last updated: 13 Oct 2026. Update this section in the same commit that finishes a step._

**Live:** https://65-109-172-182.sslip.io on a fixed-price Hetzner server (ADR-034), first deployed 13 Oct 2026. Every green push to `main` deploys automatically.

**Done**

- Phase 1, step 1: walking skeleton. Modular .NET host with five empty modules, health endpoints, React status page, CI, automatic deploy.
- Phase 1, step 2: Identity. Google sign-in, users and login identities, 30-day cookie session, `GET /api/identity/me`. Plain SQL migrations run by Fly's release command. Cookie-encryption keys stored in Postgres.
- Phase 1, step 3: Households. Create a household, single-use hashed invite links, members list, `.RequireHousehold()` resolving the current household per request, isolation tests.
- Phase 1, step 4: Tasks. Board (to do, doing, done) with priority, points, category, assignee and due date; start, complete, reopen, edit, delete; categories with defaults. Household isolation by EF global query filter plus a composite foreign key; optimistic concurrency on a `version` column (ADR-028).
- Phase 1, step 5: domain events. Every write request is one transaction shared by all modules; events raised during it are logged to `platform.activity_events` and dispatched to handlers before commit. Default categories come from a `HouseholdCreated` handler. Recent-activity feed (ADR-030, ADR-031).
- Phase 1, step 6: Points. Append-only `points.ledger` fed only by task events: completion pays the assignee (or completer), reopen and delete reverse whatever each person holds for that task. Leaderboard for this week, this month and all time; history endpoint; one-off backfill from the activity log (ADR-032, ADR-033).
- Phase 1, step 7: Shopping list. Add (once per name, case-insensitive), tick and untick (idempotent), remove mistakes, recently ticked with undo; ticked items stay as history. Shopping tab at `/#shopping` (ADR-010, ADR-035).
- Hosting moved to Hetzner (ADR-034): `infra/` holds the server's first-boot config, Docker Compose file and Caddy config; CI pushes images to GitHub's registry and deploys over SSH.

**Open questions**

- None.

**Next**

- Owner: confirm on the live site: sign-in, household, invite, board, activity feed, leaderboard, shopping list.
- Phase 1, step 8: Polish. Mobile layout, error states, Neon backups/restore check. Then phase 1 is done.

**Follow-ups**

- [ ] Reset the Neon database password (it was shared in a chat), then update the `NEON_CONNECTION_STRING` GitHub secret.
- [ ] Delete the Fly app and the `FLY_API_TOKEN` GitHub secret (no longer used).
- [ ] Pin the server's SSH host key as a secret instead of trusting it on each deploy (ADR-034).
- [ ] Rate limiting, if availability ever matters (the owner prioritises cost, and an attack can't raise the bill). `/api/health` is public and queries Postgres on every call, so a flood could use up Neon's free compute hours and pause the database until next month. Options: cache or rate-limit `/api/health`, ASP.NET Core's rate limiter per IP for anonymous requests, Cloudflare's free plan in front.
- [ ] Buy a real domain and move off sslip.io (then update the `DOMAIN` secret and the Google redirect URI).
- [ ] Commit `web/package-lock.json` after the first local `npm install`, so builds are reproducible.

**Decisions not visible in the code**

- Points go straight to whoever completes a task; no approval step (ADR-016).
- Children don't sign in yet: v1 is Google sign-in for adults only. Child support is deferred, likely as a parent-managed login.
- Hosting stays on a container, not serverless, because SignalR needs long-lived connections (ADR-018). It runs on a fixed-price server so there can be no surprise bill (ADR-034).
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

## Deploy (Hetzner + Neon)

Every green push to `main` builds an image, pushes it to `ghcr.io/michaelfkeenan/family-app`, then deploys it over SSH: write `.env`, run migrations, restart (ADR-020, ADR-021, ADR-034). One-off setup:

1. **Neon:** a project (Postgres only, no Neon Auth). Copy the pooled connection string in .NET format.
2. **Hetzner server:** Ubuntu 24.04, cheapest x86 shared-vCPU type with 2 GB RAM, a German or Finnish location, public IPv4 and IPv6. Under *Cloud config*, paste [`infra/cloud-init.yaml`](infra/cloud-init.yaml). Attach a firewall allowing inbound TCP 22, 80, 443 and UDP 443.
3. **Domain:** an `A` record (and `AAAA` for IPv6) pointing at the server. Without a domain, `<ip-with-dashes>.sslip.io` works.
4. **Google:** add redirect URI `https://<domain>/signin-google`; keep the consent screen in Testing with family members as test users.
5. **GitHub secrets** (Settings → Secrets and variables → Actions):
   - `DEPLOY_SSH_KEY`: private half of the key whose public half is in `cloud-init.yaml`
   - `DEPLOY_HOST`: the server's IPv4 address
   - `DOMAIN`: e.g. `family.example.com`
   - `NEON_CONNECTION_STRING`, `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET`
6. Re-run the latest CI run. Caddy fetches the HTTPS certificate on first start.

Without the deploy secrets the deploy job skips itself and CI stays green. To rotate the deploy key: new key pair, new public key in `cloud-init.yaml` and in `/home/deploy/.ssh/authorized_keys` on the server, new `DEPLOY_SSH_KEY` secret.
