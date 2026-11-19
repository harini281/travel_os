# TravelOS Stage 48 — Container deployment foundation

## Goal
Provide a repeatable, portable container deployment for the existing web client, Platform API, AI API and PostgreSQL database. This stage builds and smoke-tests the stack; it does not deploy to a public host or claim production readiness.

## Container layout
| Service | Image/build | Network access | Persistent data |
|---|---|---|---|
| Web | Unprivileged Nginx serving the Vite build | App network; only published service | None |
| Platform API | .NET 8 ASP.NET Core runtime | App + isolated data network | Private receipt volume |
| AI API | Python 3.11 + Uvicorn | App network only | None |
| PostgreSQL | PostgreSQL 16 Alpine | Isolated data network only | Database volume |
| Migration runner | PostgreSQL client image | Isolated data network only | Migration history in database |

The web container proxies /api/* and /health to the Platform API. The browser uses a same-origin relative API base in the container build. Provider keys remain server-side and are not copied into the frontend image.

## Configuration
1. Copy deploy/production.env.example to deploy/production.env.
2. Replace the migration-administrator password, the separate application-role password and the JWT signing key with unique, randomly generated values. Use URL-safe database passwords. The API uses the non-superuser application role; the migration runner uses the privileged bootstrap administrator. For existing PostgreSQL volumes, keep the original bootstrap role name/password until a separately reviewed role transition is complete; see the Stage 50 operations runbook.
3. Set CORS_ALLOWED_ORIGIN to the exact HTTPS origin users will visit.
4. Keep AI_PROVIDER=demo unless live AI has been separately reviewed, budgeted and configured.
5. Keep FlightAware unset unless its account, pricing and usage limits have been reviewed.
6. Protect the env file with restrictive filesystem permissions and inject secrets from a managed secret store in hosted environments. Never commit it.

The example file contains placeholders only. It is suitable for CI smoke tests, not a real deployment.

## Start the stack
From the repository root:
```bash
cp deploy/production.env.example deploy/production.env
# Edit deploy/production.env before starting.
docker compose --env-file deploy/production.env -f docker-compose.prod.yml config
docker compose --env-file deploy/production.env -f docker-compose.prod.yml up --build -d --wait
docker compose --env-file deploy/production.env -f docker-compose.prod.yml ps
```

The web container binds to loopback by default at http://127.0.0.1:8088. Put a maintained reverse proxy or managed ingress in front of it to terminate TLS, enforce HTTPS and provide the public hostname. Do not expose plain HTTP directly to the public internet.

The database is not published to the host. The AI API and Platform API are not directly published either; the browser reaches the Platform API through Nginx.

## Database migrations
The one-shot migrate service waits for PostgreSQL health and applies migrations 001–017 in the repository's documented order. It records each applied filename in travelos_migration_history and skips previously recorded migrations on later starts. Each migration and its history insert run in one transaction.

Do not edit an already-applied migration in place. Add a new forward-only migration file and extend the ordered list in deploy/migrate.sh. Back up existing data before upgrades. Validate every future migration against a disposable database before release.

## Health and persistence
- PostgreSQL uses pg_isready.
- The Platform API health endpoint checks PostgreSQL when a connection string is configured.
- The AI API and web containers have HTTP health checks.
- Compose waits for migration completion and API health before starting the web service.
- PostgreSQL data and private receipt files use named volumes. Backups, restore drills, retention and encryption-at-rest must be configured by the hosting operator.

## CI smoke validation
The container-deployment-smoke job validates Compose configuration, builds all images, starts the complete stack with placeholder CI-only values, checks the web and API health endpoints, and verifies all 18 migration records exist in the disposable database. It does not push images to a registry or use live provider credentials.

## Verification checklist
1. Compose configuration validates with the example env file.
2. All three application images build successfully.
3. PostgreSQL becomes healthy and all 18 migrations are recorded.
4. Platform API health becomes healthy after migrations.
5. AI API health reports its configured demo mode.
6. Web root serves the built single-page application.
7. Nginx proxies /health to the Platform API.
8. The browser can call the API through the same-origin proxy.
9. PostgreSQL has no published host port; receipt files use a named volume.
10. The stack stops cleanly with docker compose down.

## Explicit limitations
This stage is a deployment foundation, not a production launch. Still required before public production use:
- A selected hosting provider, DNS, TLS termination, firewall and ingress configuration.
- Managed PostgreSQL or hardened database hosting, encrypted backups and tested restore procedures.
- Managed secret injection/rotation; the sample uses environment variables and is not a secret manager.
- Centralized logs, metrics, tracing, alerting, uptime checks and incident response.
- Container/image vulnerability scanning, SBOM generation, patch cadence and image pinning by digest.
- Production capacity/load tests, rate-limit strategy across multiple API instances, and external provider contracts/quotas.
- Privacy, retention, data residency and legal review for receipt uploads and AI provider use.
- Email verification, password reset, session/refresh-token management and MFA remain separate authentication work.
- Manual screen-reader and physical-device accessibility testing remains outside CI.

Do not describe the stack as production-ready until the relevant hosting, security, operational and recovery controls have been implemented and verified.
