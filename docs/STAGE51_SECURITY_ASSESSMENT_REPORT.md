# TravelOS Stage 51 — Security Assessment Report

**Assessment branch:** `stage51-security-testing`  
**Base:** `stage50-production-readiness` at `071e737db16ac5dce2f23180bf981b818f4f2d03`  
**Assessment type:** Repository source review, safe regression tests and automated security checks  
**Status:** Implementation committed; latest corrected-head CI and scanner results still pending final validation

## 1. Scope and limitations

The review covered the React/Vite client, ASP.NET Core Platform API, FastAPI AI service, PostgreSQL access patterns and migrations, receipt storage, Docker/Compose files, external provider calls, and GitHub Actions workflows.

All active security tests are designed for generated test users and disposable CI databases. No production environment, external provider account, public host, third-party system or real user data was scanned or probed.

This report is not a professional penetration test, independent security audit, security certification or approval to deploy publicly. Code review and automated checks cannot replace an authorized independent penetration test.

## 2. Findings and remediation status

| ID | Severity | Finding | Evidence / remediation | Status |
|---|---|---|---|---|
| S51-01 | Medium | Receipt upload originally checked declared MIME type and file signature prefixes but did not fully decode the uploaded image before storage or OCR forwarding. | Added ImageSharp format detection, decode, single-frame check, 8,000-pixel edge limit, 16-million-pixel cap and 128 MiB decoded-memory ceiling. The image is re-encoded as JPEG before storage/OCR forwarding to strip embedded metadata and trailing payloads. Multipart request body and form parsing are bounded. | Fixed in code; tests pending CI |
| S51-02 | Medium | Planner, recommendation and receipt-extraction routes in the internal AI API accepted requests without a service identity. Compose network segmentation reduced reachability but did not authenticate workloads on that network. | Added `X-TravelOS-Service-Token` authentication with constant-time comparison to all three AI routes. The public health route remains available for health checks. Platform API calls now attach the header. Production startup requires a unique random 32+ character token distinct from the JWT key; Compose requires the same value for both services. | Fixed in code; tests pending CI |
| S51-03 | Low / coverage gap | JWT rejection behavior lacked a comprehensive integration regression matrix. | Added PostgreSQL-backed tests for missing/malformed tokens, wrong signing key, wrong issuer, wrong audience, expired and future-`nbf` tokens, plus a valid token case. | Added; tests pending CI |
| S51-04 | Low / coverage gap | Trip owner/member/outsider access had limited cross-user test coverage. | Added PostgreSQL-backed tests for trip access, owner-only trip update, cross-user read denial and verification that rejected edits do not change stored trip data. Further endpoint-family coverage remains desirable. | Added; tests pending CI |
| S51-05 | Medium / supply-chain gap | Dedicated static analysis, dependency audit, secret scan, configuration scan and container-image scanning were absent. | Added a least-privilege security workflow with CodeQL for C#, TypeScript/JavaScript and Python; npm, pip-audit and NuGet vulnerability checks; Trivy filesystem/secret/config scans; and scans of built Platform API, AI API and web images. SARIF scan reports are stored as workflow artifacts. Configuration-scan findings are preserved for reviewer triage rather than automatically blocking on baseline repository configuration findings. Added weekly Dependabot update configuration. | Implemented; scan results pending |
| S51-06 | Medium | Web package manifests use floating `latest` direct dependency versions and no committed npm lockfile was present in the inspected tree. Python dependencies are expressed as ranges. | Automated audits and Dependabot were added. No npm lockfile was generated in this stage, so reproducible dependency resolution remains an outstanding supply-chain task. | Partially mitigated; remains open |
| S51-07 | High — release blocker | Platform API targets .NET 8, which reached end of support on **10 November 2026**. Microsoft no longer services .NET 8 with security fixes after that date. | Recorded as a production release blocker. Do not deploy the current runtime publicly. A separately tested migration to supported .NET 10 LTS is recommended; this major framework upgrade is not silently mixed into the targeted security patch. | Open |
| S51-08 | Medium — operational residual | Rate limiting is process-local; it is not coordinated across multiple API replicas. | Already documented by Stage 50. No distributed limiter has been introduced without an infrastructure decision. | Open |
| S51-09 | Medium — operational residual | Receipt files are stored on a private local volume; no malware scanning, object storage, retention automation or deletion lifecycle is implemented. | Image decoding/re-encoding reduces malformed/polyglot upload risk but is not antivirus or malware scanning. | Open |

## 3. Security controls verified by source inspection

- JWT validation explicitly requires signature, issuer, audience and lifetime validation with a 30-second clock skew.
- API endpoint groups require authentication. Reviewed trip, itinerary, budget, expense, repayment, receipt, invitation, notification, flight, weather and assistant handlers scope data by trip membership, ownership, creator or recipient as appropriate.
- EF Core data access uses parameterized LINQ. Reviewed handwritten SQL uses EF Core interpolated SQL APIs rather than raw string concatenation.
- External provider hosts are configured server-side; user-supplied geocoding text and flight identifiers are URI-encoded before being used in provider paths/queries.
- React uses JSX rendering and the inspected application stores the access token in `sessionStorage`, not `localStorage`. Persistent browser `localStorage` uses observed in the reviewed UI are for timezone preferences.
- Stage 49 CSP, CORS, security headers and Docker network segmentation remain in place. Stage 50 PostgreSQL runtime least privilege and production configuration validation remain in place.

These are source-review observations, not proof that every endpoint or runtime path is free from vulnerabilities.

## 4. Remaining assessment coverage

- Extend cross-user regression tests to expenses, repayments, invitations, receipts, notifications and group data.
- Review full API route inventory and verify each mutation’s ownership/member/creator/recipient policy with negative tests.
- Generate and commit a tested npm lockfile; consider lock files for Python as part of a reproducible-build follow-up.
- Perform a full, independently authorized security test against a staging deployment after the .NET runtime is upgraded.
- Add distributed rate limiting, receipt malware scanning and formal secret rotation as separate operational/product work.

## 5. Verification record

The existing Stage 50 CI result is historical baseline evidence only. Stage 51's final API build, unit tests, PostgreSQL integration tests, browser E2E suite, CodeQL results, package audits, Trivy scans and container-image scans must all finish on the final Stage 51 commit before closing this assessment.

Record the final branch SHA, CI run URLs, scan-artifact links, all unresolved vulnerabilities and accepted exceptions here after CI completes. A green automated pipeline does not by itself constitute a clean penetration-test report or a production release sign-off.
