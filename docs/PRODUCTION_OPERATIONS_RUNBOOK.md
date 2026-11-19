# TravelOS — Production Operations Runbook

## Scope and safety

This runbook describes how an operator should prepare and verify a deployment using the Stage 48/49 Compose configuration and Stage 50 backup utilities. It does **not** authorize a public deployment, create infrastructure, provision a hosting provider, or certify production readiness.

Do not expose the web container's plain HTTP listener directly to the internet. Do not run the production Compose file against a personal database or a database containing data that is not backed up.

## Supported deployment shape

The current production Compose file runs:
- React static web assets behind unprivileged Nginx.
- ASP.NET Core 8 Platform API.
- Python/FastAPI AI API.
- PostgreSQL 16.
- One-shot migration runner.
- A named private receipt-storage volume.

Only the web service publishes a host port. It defaults to loopback on `127.0.0.1:8088`. PostgreSQL, Platform API and AI API are not published to the host. Compose network zones remain `frontend`, `backend` and internal-only `data`.

A public deployment still needs a selected host, DNS, firewall, TLS termination, trusted ingress, secure secret injection, database hosting approval and operational ownership. None of those are created by this stage.

## Configuration inventory

Copy the template, then keep the resulting file local and access-controlled:

```bash
cp deploy/production.env.example deploy/production.env
chmod 600 deploy/production.env
```

The file is ignored by Git. Never commit it, paste it into issues, print it in CI or share it in logs.

| Setting | Required | Purpose / rule |
|---|---:|---|
| `POSTGRES_DB` | Yes | Database created by PostgreSQL |
| `POSTGRES_USER` | Yes | Bootstrap/migration administrator created by the official PostgreSQL image; privileged and never used by the API |
| `POSTGRES_PASSWORD` | Yes | Unique migration-administrator password |
| `TRAVELOS_APP_DB_USER` | Yes | Separate non-superuser role used by the API |
| `TRAVELOS_APP_DB_PASSWORD` | Yes | Unique runtime application password, different from the migration administrator password |
| `TRAVELOS_CONNECTION_STRING` | Yes | Must use the same database and the `TRAVELOS_APP_DB_USER` / `TRAVELOS_APP_DB_PASSWORD` pair |
| `JWT_ISSUER` | Yes | Stable issuer identifier |
| `JWT_AUDIENCE` | Yes | Stable application audience |
| `JWT_SIGNING_KEY` | Yes | Unique random secret of at least 32 bytes; store and rotate through a managed secret mechanism where available |
| `CORS_ALLOWED_ORIGIN` | Yes | Exact HTTPS origin only; no wildcard, path, query, fragment or embedded credentials |
| `WEB_BIND_ADDRESS` | Yes | Keep at `127.0.0.1` unless a reviewed network boundary requires otherwise |
| `WEB_PORT` | Yes | Host-local port for the TLS ingress to reach |
| `AI_PROVIDER` | Yes | `demo` by default; use `openai` only after model, cost, privacy and data-transfer review |
| `AI_RECEIPT_PROVIDER` | Yes | `demo` by default; receipt images leave the platform when live extraction is enabled |
| `OPENAI_API_KEY` | Conditional | Required when either AI provider mode is `openai`; server-side only |
| `OPENAI_MODEL` | Conditional | Non-empty model name when a live provider is enabled |
| `OPENAI_BASE_URL` | Optional | Defaults to the OpenAI API; live mode requires an HTTPS URL without embedded credentials/query/fragment |
| `FLIGHTAWARE_API_KEY` | Optional | Server-side only; confirm account access, pricing and provider terms before use |

**Existing PostgreSQL volume upgrade warning:** the official PostgreSQL image only applies `POSTGRES_USER` and `POSTGRES_PASSWORD` while initializing an empty data directory. Do not rename the bootstrap account in `deploy/production.env` on an existing volume and assume that the container will create the new role. Keep `POSTGRES_USER`/`POSTGRES_PASSWORD` aligned with the account that already exists in that cluster, then apply the new separate `TRAVELOS_APP_DB_USER`/`TRAVELOS_APP_DB_PASSWORD` through the migration runner. Review ownership and grants for legacy schemas before using a restricted migration role; do not drop, recreate or reset an existing database volume to apply this change.

The migration runner uses `POSTGRES_USER` only for ordered schema changes. It creates or updates `TRAVELOS_APP_DB_USER` as a separate NOINHERIT, non-superuser login and grants runtime table DML/sequence access while keeping migration-history writes with the administrator. The Platform API connection string must use that runtime role. The API now fails at startup in Production if the PostgreSQL connection string is missing, malformed, incomplete or does not use `TRAVELOS_APP_DB_USER`. JWT and HTTPS CORS checks from Stage 49 remain in force. The AI API fails at startup for unsupported mode values or when a live provider lacks its key or a valid HTTPS endpoint.

The root `.env.example` is for local development. `apps/web/.env.example` contains a public browser API URL only. Never place AI, FlightAware, database or JWT secrets in a `VITE_*` variable.

## Pre-release configuration checks

From the repository root:

```bash
docker compose --env-file deploy/production.env -f docker-compose.prod.yml config --quiet
docker compose --env-file deploy/production.env -f docker-compose.prod.yml config --services
```

Before continuing, verify manually:
1. The environment file permissions are restricted and it is excluded from backups intended for developers.
2. The connection string and PostgreSQL service variables identify the same database account.
3. JWT signing material is unique to this environment and is retrievable from the approved secret store.
4. The CORS origin is the actual externally visible HTTPS origin.
5. AI modes are explicitly approved; demo mode remains the default until the provider/data review is complete.
6. The host firewall allows only the approved ingress path. Do not publish PostgreSQL, API or AI ports.
7. The trusted ingress terminates TLS, overwrites forwarding headers and forwards only to the loopback-bound web port.
8. The operator has a tested recovery copy before any schema change or release.

## Backup and verification

### One-time encryption key setup

Install the approved `age` utility on the operator workstation or backup host. Generate the recipient/identity pair in a protected location. Do not save the private identity in the repository or on the same failure domain as the backups.

```bash
mkdir -p "$HOME/.config/travelos"
chmod 700 "$HOME/.config/travelos"
age-keygen -o "$HOME/.config/travelos/backup-identity.txt"
chmod 600 "$HOME/.config/travelos/backup-identity.txt"
age-keygen -y "$HOME/.config/travelos/backup-identity.txt"
```

Store the displayed public recipient in the approved backup job configuration. Store the private identity in a separate secret store or offline recovery location. Make sure at least two authorized custodians can recover it under the organization's recovery policy.

### Create a coordinated backup

The backup script requires Bash 4+, Docker Compose, Docker, `age`, `sha256sum`, `flock` and a running Platform API container with its receipt volume mounted. It validates Compose configuration and PostgreSQL readiness, pauses the web/API writers, captures PostgreSQL in custom archive format, archives the private receipt volume, checks both archive formats and encrypts both files before publishing the backup directory.

Use a destination outside the repository, ideally on an encrypted backup disk or a staging mount that is subsequently copied to approved off-host storage:

```bash
mkdir -p /secure-backup-staging/travelos
chmod 700 /secure-backup-staging/travelos
AGE_RECIPIENT='age1_REPLACE_WITH_APPROVED_PUBLIC_RECIPIENT' \
  TRAVELOS_ENV_FILE="$PWD/deploy/production.env" \
  bash ./deploy/backup.sh /secure-backup-staging/travelos
```

The script pauses web/API writes during the capture and attempts to bring the previously running services back to healthy status. It fails rather than create an unencrypted final backup if the dump/archive, encryption or validation fails. Keep the destination outside the repository. Copy the **entire timestamped directory** to a separate, access-controlled off-host location.

The utility does not upload backups, enforce retention, set an RPO/RTO, verify remote-storage durability, or replace a full recovery drill. Those remain operator responsibilities.

### Verify an encrypted backup

```bash
AGE_IDENTITY_FILE="$HOME/.config/travelos/backup-identity.txt" \
  bash ./deploy/verify-backup.sh /secure-backup-staging/travelos/travelos-backup-YYYYMMDDTHHMMSSZ
```

This checks the SHA-256 manifest, decrypts both archives into a private temporary directory and validates the PostgreSQL custom archive and receipt tar archive. The plaintext temporary directory is removed on exit. A successful integrity check does **not** prove that the application can be restored correctly.

## Restore drill — disposable target only

Perform this drill on a separate disposable PostgreSQL instance and isolated receipt directory. Never point a restore command at the live volume as a test.

1. Select a backup that has passed `verify-backup.sh`.
2. Decrypt `database.dump.age` and `receipts.tar.gz.age` into a private temporary directory with mode 700.
3. Create a new empty PostgreSQL database using the same supported PostgreSQL major version.
4. Restore the custom archive into that database using `pg_restore --exit-on-error --no-owner --no-acl`. Do not use `--clean` against a database containing data to preserve.
5. Extract the receipt archive into a new empty staging directory, retaining file ownership/permissions according to the deployment's receipt-storage user.
6. Verify the restored migration history, account/trip/expense/receipt row counts, and representative API reads in the isolated environment.
7. Verify original receipt image retrieval via an authenticated trip-member request and confirm access is denied to a non-member.
8. Start the Platform API and run its health check against the restored database and staging receipt mount.
9. Record restore duration, data age, missing objects, operator, evidence and remediation. Delete the disposable environment securely after review.

Do not overwrite a live PostgreSQL volume or receipt volume until a separately approved recovery plan, maintenance window and rollback path exist. This repository does not yet provide a one-command production restore utility.

## Release procedure

1. Review the full Stage 50 diff and CI result; ensure the Stage 49 base branch/PR status is understood.
2. Confirm the release commit and deployment artifact/image digests are approved and recorded.
3. Confirm the last good encrypted database **and receipt** backup is off-host and verified.
4. Confirm migration changes are forward-only. Never edit an already-applied migration.
5. Validate the Compose environment with `config --quiet`; do not publish rendered configuration to logs.
6. Build/retrieve only approved images and record immutable image digests before production use.
7. Deploy during the approved maintenance window using the hosting provider's documented rollout process.
8. Verify web/API/AI health, PostgreSQL readiness, migration history, security headers, TLS, published ports and service-network boundaries.
9. Run a small authenticated smoke test and receipt read/write check with a synthetic account.
10. Record the release SHA, image digests, migration names, backup identifier, operator and outcome.

This repository's Compose workflow is a single-host deployment foundation. It does not implement a rolling deployment controller or automated rollback orchestration.

## Rollback and incident response

- **API/web regression:** stop further rollout, preserve redacted logs and deployment metadata, then redeploy the last approved image/commit using the hosting provider's rollback process.
- **Migration failure:** stop the rollout. Do not blindly rerun or edit the migration. Determine whether the migration transaction committed, compare the migration history table with the migration runner output, and restore only under an approved database recovery plan.
- **Suspected credential leak:** revoke/rotate the affected key in its provider/secret store, update the deployment environment, restart affected services, invalidate affected sessions/tokens where applicable, preserve sanitized evidence and record the incident.
- **Database corruption/data loss:** stop application writes, preserve the affected volume for investigation, choose a verified recovery point and use the approved restore drill procedure. Do not overwrite the only copy.
- **AI provider outage or cost anomaly:** disable the live provider via approved configuration change and return to demo/manual-review mode. Never put fallback secrets in the browser.

Collect container logs through the approved host logging mechanism. Redact access tokens, connection strings, passwords, user-supplied receipt contents and provider responses before sharing diagnostic bundles.

## Monitoring and ownership gaps

The repository currently has Compose health checks and bounded local container log rotation, but no centralized log service, metrics backend, tracing collector, on-call alerting, uptime monitor, incident ticket workflow or SLOs. Assign owners and select those services before public production use.

Set and approve:
- Recovery Point Objective (RPO) and Recovery Time Objective (RTO).
- Backup frequency, off-host destination, retention, legal hold and deletion schedule.
- Database/receipt encryption key custody and tested key recovery.
- Alert thresholds for API health, PostgreSQL capacity, disk/receipt volume use, repeated restarts, error rates and provider usage/cost.
- Security patch cadence, vulnerability response SLA and image provenance policy.

## Known product/platform limitations

- Rate limiting is currently process-local; limits are not coordinated across multiple API replicas.
- Receipt images are stored on the local named volume; object storage integration, malware scanning, retention automation and deletion workflows are not implemented.
- Email verification, password reset, refresh-token/session management and MFA are not implemented.
- Public AI, map, route, weather, opening-hours and flight providers require separate privacy, availability, pricing, usage-policy and contractual review.
- Automated browser tests and a successful container smoke test do not constitute an independent penetration test, accessibility certification, disaster-recovery certification or production approval.
