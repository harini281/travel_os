# TravelOS — Production Release Readiness Checklist

**Status:** Stage 52 .NET 10 migration passes repository CI/security validation; no public deployment is authorized by this checklist.

Use `PASS`, `FAIL`, `NOT RUN` or `NOT APPLICABLE` for each gate. A documented feature is not a passed operational control unless it has been verified in the target hosting environment.

## Release blockers — must be closed before public production

| Gate | Current repository evidence | Status before hosting review |
|---|---|---|
| Production database settings fail fast | Stage 50 validates PostgreSQL connection presence, syntax, host, database, username, password and application-role identity | Implemented; CI verification pending |
| Runtime database least privilege | Migration administrator is separated from the API login; CI checks the runtime role is not superuser/CREATEDB/CREATEROLE/replication/BYPASSRLS and cannot write migration history | Implemented; target permissions pending review |
| JWT issuer/audience/signing key and exact HTTPS CORS | Stage 49 validation + unit tests | Implemented; target values not verified |
| Platform-to-AI service authentication | Stage 51 shared token required for planner/receipt endpoints; health endpoint remains public; production requires a unique 32+ character token | Implemented; target secret injection and rotation pending |
| Supported .NET runtime | Platform API/tests, Docker images and CI target .NET 10 LTS; CI/security pass on source commit `09655a1` and runtime smoke reported .NET 10.0.15 | Repository verification PASS; target-host runtime/configuration review still pending |
| No committed production environment files | `deploy/production.env` added to Git ignore rules | Implemented; repository scan pending |
| Managed secret injection and rotation | Not provided by Compose | Blocker |
| TLS ingress, DNS, firewall and trusted proxy | Must be supplied by hosting operator | Blocker |
| Database hosting, encryption at rest, access controls and capacity | Compose currently uses a local PostgreSQL volume | Blocker |
| Database and private receipt backup | Encrypted backup utility added; no off-host automation | Implemented groundwork; target backup store pending |
| Full restore drill and measured RPO/RTO | Integrity checker validates archives but is not a full restore | Blocker |
| Receipt storage security and recovery | Named private volume; no object storage or malware scanning | Blocker |
| Centralized logs, metrics, tracing and alerting | Compose health checks and local log rotation only | Blocker |
| Image provenance, vulnerability scanning and SBOM | No registry policy, scanner, SBOM or digest lock | Blocker |
| Production load/capacity and multi-instance rate limits | Rate limits are process-local | Blocker |
| Privacy, data retention, residency and AI provider approval | Manual legal/operational review required | Blocker |
| Authentication lifecycle | Email verification, password reset, refresh/session management and MFA remain absent | Product/security decision required |
| Independent security/accessibility review | CI tests are not certification | Blocker |

## Repository-level checks

- [ ] CI passes on the final Stage 50 commit.
- [ ] The eight CI jobs pass, including PostgreSQL integration, migrations, container smoke and cross-browser/mobile/accessibility E2E.
- [ ] `bash -n deploy/backup.sh deploy/verify-backup.sh` passes.
- [ ] Platform API production configuration tests pass.
- [ ] AI API provider configuration tests pass.
- [ ] Production environment files are ignored and no actual secrets are present in Git history.
- [ ] No production credentials or deployment infrastructure were changed as part of Stage 50.
- [ ] Compose network isolation, no-published-service-port checks and security-header checks still pass.
- [ ] Migrations remain forward-only and the migration history count/contents match the expected release.

## Target-environment checks

- [ ] Production environment file or managed secret bindings validated without printing values.
- [ ] Database account is least-privilege and connection uses the target host's required TLS policy.
- [ ] TLS certificate, HTTPS redirects, HSTS policy and trusted proxy header behavior are verified.
- [ ] Only the approved ingress is reachable from outside.
- [ ] Web, Platform API, AI API and PostgreSQL health checks pass.
- [ ] Synthetic authentication, trip creation/read and receipt upload/retrieval smoke tests pass.
- [ ] Backup is copied off-host, checksums and decryption are verified, and the backup key is recoverable by authorized custodians.
- [ ] Database **and receipt files** restore successfully to an isolated environment.
- [ ] Measured RPO/RTO meet approved business requirements.
- [ ] Alerts reach the on-call owner and an incident response exercise is recorded.
- [ ] Vulnerability scan, SBOM, image digests and patch policy are recorded.
- [ ] AI provider privacy/data processing, cost ceilings, quota and fallback behavior are approved.
- [ ] Receipt data retention, access audit, deletion and user-consent policies are approved.

## Release sign-off

| Role | Name | Decision | Date/evidence |
|---|---|---|---|
| Engineering owner |  | Pending |  |
| Security reviewer |  | Pending |  |
| Data/DB owner |  | Pending |  |
| Operations/on-call owner |  | Pending |  |
| Product/privacy owner |  | Pending |  |

A release must not be described as production-ready while any required release blocker remains open. Stage 50 prepares the repository and its operators for review; the hosting/provider controls and sign-offs are intentionally outside this code change.
