# Stage 55 — Receipt storage lifecycle reliability

**Branch:** `stage55-receipt-reliability`  
**Base:** `stage54-security-remediation`  
**Status:** Partial implementation; CI and security workflows pass for commit `56a30c2f3a8ac378604a1bc4093c8931f35e027f`. This is not a production-readiness declaration.

## Why this stage exists

The current receipt endpoints store image bytes on the filesystem and receipt metadata in PostgreSQL. The current delete flow commits removal of the database row before attempting to delete the file; an `IOException` is swallowed. A failed filesystem deletion therefore leaves an untracked private file. Upload has a related compensation path: if metadata persistence fails, a best-effort file delete is attempted and an `IOException` is swallowed.

The current release checklist also identifies the absence of a malware scanning control and the absence of an operational restore/reconciliation process for receipt storage. Image normalization is a useful content-validation boundary, but it is not malware scanning and it does not eliminate decoder resource-exhaustion risk.

## Scope

### 55.1 Durable receipt deletion

Implement a database-backed deletion queue/state transition so that a user delete request is not treated as complete merely because metadata was removed. The design must:

- Preserve authorization: only the uploader or trip owner may request deletion.
- Record a deletion request durably before returning success.
- Retry transient filesystem failures with capped exponential backoff and observable attempt/error metadata.
- Be idempotent across process restarts and concurrent workers.
- Prevent receipt download once deletion has been requested.
- Avoid exposing storage paths or internal exception details to API clients.
- Provide an operator-visible count of pending/failed cleanup operations without exposing receipt contents.
- Define retention and terminal-failure handling; never silently discard a failed cleanup record.

Do not introduce a distributed queue dependency for the single-host Compose deployment. Keep the database as the durable source of truth. A PostgreSQL transaction-scoped advisory lock serializes queue processing across API replicas; the lock is released automatically if a process or database connection fails.

### 55.2 Upload compensation and reconciliation

- Make upload failure compensation explicit and testable.
- Add a safe reconciliation command/service to identify files with no corresponding live metadata and metadata whose file is missing.
- Reconciliation must be dry-run by default; destructive cleanup requires an explicit operator action and age threshold.
- Never delete files based only on an untrusted filename. Resolve and validate every path under the configured storage root.
- Record counts and outcomes, not sensitive receipt bytes or full local paths.

### 55.3 Malware and decoder resilience

- Define an interface for an optional malware scanner; fail closed for public production uploads when the configured required scanner is unavailable.
- Keep local development behavior explicit and documented.
- Add bounded decoder concurrency and time/resource tests around the existing size, dimensions, pixel and decoded-byte limits.
- Include animated/multi-frame, malformed, truncated, polyglot/trailing-payload and oversized fixtures.
- Do not claim malware protection unless an actual scanner is configured and its result is enforced.

### 55.4 Verification

Required automated coverage:

- Authorized uploader and trip-owner deletion; unrelated trip member denied.
- Delete request is durable before response; worker retries after simulated I/O failure.
- Repeated requests and concurrent workers do not double-delete or corrupt state.
- Receipt bytes are inaccessible after deletion is requested.
- Successful cleanup reaches a terminal state and removes the file.
- Worker restart resumes pending work.
- Upload metadata failure does not leave an untracked file without a reconciliation record or detectable orphan.
- Reconciliation dry-run is non-destructive and path traversal is rejected.
- Scanner unavailable/rejects/accepts behavior is deterministic.
- Decoder stress tests are bounded and do not make unsupported performance claims.

CI must validate migration ordering on a clean PostgreSQL database, API tests, integration tests, web build/E2E, container smoke, dependency audits, CodeQL and Trivy filesystem/config/container scans.

## Out of scope

- Public deployment, DNS/TLS provisioning, managed secrets, cloud IAM or production credentials.
- Declaring the application production-ready.
- Replacing the existing receipt storage provider with cloud object storage.
- A claim of independent penetration testing or security certification.

## Exit criteria

- [ ] Database migration is forward-only and validated on clean PostgreSQL.
- [x] Durable delete lifecycle, transaction-scoped worker lock and capped retry scheduling are implemented (automated tests and CI verification still pending).
- [ ] Upload compensation and automated reconciliation verification are implemented. The first dry-run reconciliation worker is present; it still needs tests and operational controls.
- [ ] Scanner integration contract and fail-closed production behavior are tested.
- [ ] Decoder resource limits and stress tests are evidenced.
- [ ] Authorization and restart/idempotency tests pass.
- [x] CI and security workflows pass on commit `56a30c2f3a8ac378604a1bc4093c8931f35e027f` (GitHub Actions run `44994871999` and `44994871985`). Re-run after any subsequent source changes.
- [ ] Release checklist is updated with repository evidence and remaining target-host blockers.

A passing CI run proves only the tested repository behavior. Target-host storage durability, scanner service availability, backup/restore, IAM, TLS and independent security review remain separate release gates.
