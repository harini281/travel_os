# TravelOS — Security Response Guide

**Applies to:** TravelOS repository and deployments  
**Owner:** Project maintainers / designated incident lead  
**Status:** Operational draft — tailor contacts and hosting-provider procedures before production.

This guide supports triage and containment. It is not a substitute for an organization's approved incident-response policy, legal advice or regulatory advice. Never conduct incident investigation against systems or accounts you do not own or have permission to access.

## 1. Response priorities

1. Protect travellers and their personal, itinerary and financial data.
2. Contain exposed credentials and unauthorized access before pursuing root cause.
3. Preserve audit evidence without copying secrets or unnecessary personal data into issue comments.
4. Restore service from known-good, patched versions and verified backups.
5. Record decisions, owners, timestamps and follow-up actions.

Assign an incident lead, technical lead and communications/privacy contact. For any suspected personal-data exposure, involve the organization's responsible privacy/legal contact and follow applicable legal and contractual notification requirements.

## 2. Severity and escalation

| Severity | Examples | Initial handling |
|---|---|---|
| Critical | Confirmed active account takeover at scale, exposed production signing key/service token/database credentials, confirmed large-scale data exfiltration, malicious production deployment | Declare incident immediately; isolate affected services/credentials; preserve evidence; notify accountable leadership |
| High | Confirmed cross-user access to trips/receipts/expenses, exploitable remote code execution, internet-exposed internal AI endpoint, active HIGH/CRITICAL exploitable container dependency | Escalate to incident lead immediately; contain affected route/service; patch and validate |
| Medium | Unauthenticated information disclosure with limited impact, image-processing crash affecting availability, a high-risk dependency without evidence of exploitation, misconfigured scanning/branch controls | Triage promptly, identify exposure window, schedule controlled remediation and regression tests |
| Low | Test/documentation gap with no known exploitable path, low-risk scanner warning | Track, assign an owner and due date; reassess on scope change |

Severity should be adjusted based on data sensitivity, reachability, exploit preconditions, impact and evidence. A scanner severity alone is not proof of exploitability.

## 3. First 30–60 minutes: intake and containment

1. Record when and how the report arrived, affected component, affected environment, first known/last known occurrence, reporter contact and evidence location.
2. Confirm that the issue concerns TravelOS-owned source or infrastructure. Do not probe unrelated public hosts or third-party providers.
3. Preserve relevant GitHub Actions run IDs, job logs, deployment version, application audit events and infrastructure events. Restrict access to the evidence. Redact tokens, passwords, session cookies, personal information and receipt content before sharing.
4. If a secret is exposed or suspected compromised, revoke/rotate it using the actual secret manager/provider. Do not paste the replacement into GitHub, chat, logs or this guide.
5. If an internal API boundary is compromised, restrict AI API network reachability to the Platform API, rotate `AI_SERVICE_TOKEN` in both services and restart/verify both services. Keep the health endpoint minimal and free of configuration values.
6. If JWT signing material is compromised, rotate the signing key using a planned token invalidation/re-authentication procedure. Expect existing tokens to become invalid; communicate the user impact.
7. If receipt access is suspected, restrict affected routes/storage permissions, preserve access logs and database metadata, identify impacted receipt IDs/users through authorized records, and avoid distributing receipt images during triage.
8. If an actively exploitable application path is confirmed, disable the affected feature or ingress path only through an approved change and document service impact. Do not delete forensic evidence.

## 4. AI service-token response

The internal token header is `X-TravelOS-Service-Token`; the server-side setting is `AI_SERVICE_TOKEN`.

- Confirm the token is not set in browser-facing `VITE_*` variables, frontend bundles, source control, screenshots or logs.
- Generate a new, high-entropy random value with an approved secrets manager or secure local generator.
- Ensure the new value is unique and different from `Jwt:SigningKey`.
- Update the secret for both the AI API and Platform API atomically or in a coordinated maintenance window.
- Restart/redeploy both services and verify authenticated Platform API calls succeed.
- Verify direct internal AI POST calls without the token and with a wrong token return 401; missing server configuration must fail closed (503 at the AI route and production-startup rejection by the Platform API).
- Confirm `GET /health` still returns only its minimal health response and does not expose provider configuration, token state or secret values.
- Review access logs and deployment records for the suspected exposure interval. Avoid logging header values.

Do not weaken authentication to recover service availability. If rotation fails, keep the affected internal route unavailable rather than exposing it unauthenticated.

## 5. Receipt and image-processing response

- Restrict upload/retrieval if there is evidence of unauthorized receipt access or parser exploitation.
- Preserve relevant request metadata and API audit records. Do not retain attacker-provided file data longer than required by the investigation and approved retention policy.
- Identify receipts by server-generated receipt ID and storage metadata; never use a client filename as a filesystem path.
- Review declared content type, decoded format, pixel dimensions, frame count, normalized JPEG metadata, storage path and upload actor.
- Confirm the file was re-encoded and only normalized bytes were forwarded to AI extraction.
- Check that the upload was bounded by both multipart/request and file limits, that failed operations did not leave orphaned files or database records, and that outsiders cannot read the receipt through a different trip/receipt ID.
- Before restoring upload service, test malformed bodies, MIME mismatch, unsupported formats, dimensions/pixel thresholds, animation, >8 MB files, path-like filenames and cleanup behaviour using generated test files in a disposable environment.
- Image re-encoding reduces metadata/trailing-content risks; it is not antivirus. Do not claim malware scanning unless an approved scanner actually ran and its result is retained.

## 6. Authorization / IDOR response

For a suspected cross-user access issue:

1. Record the exact route, HTTP method, resource ID, trip ID, caller identity class (owner, member, invitee, outsider) and response. Redact bearer tokens.
2. Preserve relevant audit events and database state before changing data.
3. Reproduce using generated accounts and a disposable PostgreSQL database only.
4. Test the full intended permission matrix, including read, create, update, delete, confirm/respond and file-download operations. Vary both parent and child resource IDs to test path mismatch.
5. Assert there are no unauthorized database mutations, file reads/writes or downstream AI calls.
6. Patch the narrow authorization check, add regression tests, run the entire API and browser test suites, then request independent code review.
7. Search for the same ownership pattern in adjacent endpoints; do not infer that fixing one route fixes the whole resource family.

Return 404 where the existing API intentionally hides resource existence; preserve a consistent authorization contract.

## 7. Dependency and container vulnerability response

1. Record the scanner, database timestamp, image digest, package name/version, CVE, severity, fixed version, affected image and whether the package is reachable at runtime.
2. Verify findings against the package/vendor advisory. Distinguish direct, transitive, build-time and runtime dependencies.
3. Upgrade to a supported patched version, rebuild from a clean base and pin or lock the chosen dependency according to the repository's dependency-management policy.
4. Re-run npm audit, pip-audit, NuGet vulnerability checks and Trivy filesystem/config/image scans.
5. Retain the full reports and compare image digests. Do not mark a vulnerability fixed merely because a Dockerfile constraint changed.
6. If no fix exists, record the exposure, mitigations, owner, expiry date and risk acceptance by an authorized decision-maker. Do not silently suppress the finding.

For the Stage 51 assessment, a historical AI image scan reported three HIGH CVEs in `jaraco.context`, `setuptools` and `wheel`. The branch raised minimum versions; a fresh image build and scan are required to close the finding.

## 8. GitHub Actions / repository compromise

- Preserve the workflow run, job logs, artifact IDs, commit SHA and workflow file revision.
- Inspect changed files, branch refs, PR metadata, actor identity, permissions, third-party actions and artifact provenance.
- Revoke compromised repository credentials through GitHub/secret manager procedures; do not expose replacement credentials in commits.
- Require branch protection, review and passing required checks before accepting security changes.
- Treat a skipped scan as unavailable, never as passed. Treat SARIF artifacts as retained reports, not Code Scanning alerts unless upload is configured and confirmed.
- Do not force-push or rewrite shared history during incident handling. Preserve commits and PR history unless the incident commander approves a specific containment action with an evidence-preserving plan.

## 9. Recovery and verification

Before restoring the affected component:

- Deploy a reviewed commit on a supported runtime.
- Confirm CI build, unit tests, disposable PostgreSQL integration tests, migrations, deployment smoke tests and browser E2E results.
- Confirm CodeQL, dependency, secret/configuration and all container image scans completed for the exact deployed SHA.
- Confirm the service token, JWT, database role, TLS ingress, network boundaries and private receipt storage settings are correct.
- Restore from a verified backup only when required; conduct restore validation in isolation first.
- Monitor auth failures, unexpected 4xx/5xx rates, upload rejection rates, unusual file access, privilege changes and outbound AI/provider calls.
- Record residual risk, evidence links, approvals and recovery time.

A passing pipeline does not establish the absence of vulnerabilities or replace independent security testing.

## 10. Communications and post-incident record

Communications must be approved by the incident lead and privacy/legal contact. State only what is known, what is not yet known, which users/services may be affected, what action users should take and when the next update will be provided. Do not speculate about data exposure.

After closure, document:

- Timeline and incident owner.
- Affected systems, commits, images and data categories.
- Confirmed facts versus hypotheses.
- Root cause and attack path, if established.
- Containment, eradication and recovery actions.
- User/partner/regulator notification decisions and rationale.
- Tests and independent review performed.
- Remaining risks, owners and deadlines.

## 11. Contacts and deployment-specific details to complete before production

Maintain these outside public source control and keep them current:

- Primary incident lead and 24/7 escalation route.
- Hosting/cloud provider emergency contact and account owner.
- Secrets manager and credential-rotation procedure.
- Database and receipt-storage backup locations, retention and restore owner.
- Privacy/legal contact and approved communications channel.
- Production ingress/DNS/TLS owner.
- Approved independent penetration-testing provider and scope authorization.

Never commit live contact details, credentials, private endpoints or secret recovery codes into this document.
