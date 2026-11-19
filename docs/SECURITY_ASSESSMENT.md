# TravelOS — Stage 51 Security Assessment

**Assessment date:** 19 November 2026  
**Repository:** https://github.com/harini281/travel_os  
**Branch:** `stage51-security-testing`  
**Baseline:** `stage50-production-readiness`  
**Completed application validation baseline:** `2ceec4dbb85ef10d8d2144f8d5bcf37ab6e2de9f`  
**Trivy policy-coverage upgrade:** `324929de999809ec834494adbd3adf225b9fd0aa`  
**Draft PR:** [#53 — Stage 51: Security verification and hardening](https://github.com/harini281/travel_os/pull/53)

> **Overall result: FAIL / BLOCKED for public production.** The completed Stage 51 validation baseline passes its CI and security gates, but the current head still has a fresh Trivy-policy-coverage rerun queued, .NET 8 is out of support, branch protection is disabled, and independent staging security testing has not been performed. This is a repository-level assessment, not an independent audit, certification or penetration test.

## 1. Executive result

The Stage 51 branch is based on Stage 50 with no base-branch divergence. The draft PR currently targets `stage50-production-readiness`, remains open and unmerged, and preserves the existing branch/commit history. PRs #51 and #52 were historical Stage 49/50 PRs; they were not changed or merged as part of this work.

The completed validation baseline is commit `2ceec4dbb85ef10d8d2144f8d5bcf37ab6e2de9f`:

- [CI run 44558532457](https://github.com/harini281/travel_os/actions/runs/44558532457) — **PASS** across all eight jobs.
- [Security Assessment run 44558532472](https://github.com/harini281/travel_os/actions/runs/44558532472) — **PASS** across all five jobs.

Exact completed CI evidence:
- Platform API build — passed.
- Platform API unit tests — **48 passed, 0 failed, 27 skipped**. The skipped cases are PostgreSQL-backed tests and are run by the separate database job.
- PostgreSQL-backed integration suite — **79 passed, 0 failed**.
- AI API tests — **48 passed, 1 warning**.
- Web typecheck/build — passed.
- PostgreSQL migrations — passed.
- Container deployment build and smoke test — passed, including web/API/AI health, migrations recorded and internal port/network isolation checks.
- Browser E2E — **16 passed, 0 failed**, including Chromium, Firefox, WebKit, mobile Chromium, mobile WebKit, tablet Chromium and the receipt upload/confirmation/duplicate flow.

Exact completed security evidence on the same commit:
- CodeQL Platform API — passed and SARIF uploaded successfully to GitHub Code Scanning.
- CodeQL Web and AI — passed and SARIF uploaded successfully to GitHub Code Scanning.
- NuGet/npm/Python dependency audit — passed; npm reported 0 vulnerabilities, pip-audit reported no known vulnerabilities, and the NuGet high/critical gate passed.
- Filesystem and secret scan — passed.
- Trivy configuration scan — the job passed its HIGH/CRITICAL gate, but Trivy 0.70 warned that one AWS CloudFront logging policy requiring Trivy 0.72+ was skipped. This is a policy-coverage limitation, not a clean result for that skipped rule.
- Platform API, AI API and web container image scans — all passed the HIGH/CRITICAL gate. The AI image was rebuilt as a multi-stage image so builder/package-manager layers do not enter the final runtime; the web runtime uses the refreshed NGINX unprivileged/Alpine 3.24 base.

Because of the skipped Trivy policy, commit `324929de999809ec834494adbd3adf225b9fd0aa` pins Trivy `v0.76.2` for all five Trivy action uses. A follow-up image build on that commit failed because `ldconfig` ran after the Dockerfile switched to UID 10001; the error was `Can't create temporary cache file /etc/ld.so.cache~: Permission denied`. Commit `aadca97e81a72984e48cae12641d129c4123d41f` moves the linker-cache/import validation before the privilege drop while keeping the final runtime process non-root. The exact-head validation workflows are now queued:
- [TravelOS CI — run 44559967580](https://github.com/harini281/travel_os/actions/runs/44559967580).
- [TravelOS Security Assessment — run 44559968042](https://github.com/harini281/travel_os/actions/runs/44559968042).

Do not call Trivy 0.76.2 configuration coverage final until that exact-head scan passes and the CloudFront rule is no longer skipped.

The final source also received null-safe normalization in `Preferences/PreferenceEndpoints.cs` (commit `a40c73ef36b6693a7a9ce05a789bd5e2e4604155`). The latest CI rerun will validate that change together with the corrected AI image Dockerfile.

| Category | Result | Evidence and limits |
|---|---|---|
| AI API service authentication | **PASS** | All three internal POST routes require the service token; missing, wrong and malformed-present tokens are rejected; missing server configuration fails closed; public health returns only `{"status":"ok"}`. AI API tests passed. |
| Receipt image security | **PASS for tested controls; residual limits** | Format/signature checks, full decode, dimension/pixel/decoded-byte ceilings, animation rejection, JPEG re-encoding, generated storage names and access checks are implemented. Unit/integration/E2E receipt journeys passed. No peak-memory/performance stress test or malware scan is evidenced. |
| Authorization / IDOR | **PASS for exercised matrix; not exhaustive** | 79 PostgreSQL integration tests passed, including multi-user trips, expenses, repayments, invitations, preferences, polls/votes, receipt file/confirmation/delete and outsider upload side-effect cases. Not every route and HTTP verb has its own negative-access test. |
| CodeQL / dependency / secret automation | **PASS on baseline; current Trivy policy rerun pending** | Both CodeQL analyses uploaded SARIF; dependency, filesystem/secret and Trivy image gates passed. The earlier Trivy config scan skipped one policy due to outdated scanner; Trivy 0.76.2 is now pinned and the current-head rerun is pending. |
| Container image security | **PASS on baseline** | All three images passed Trivy HIGH/CRITICAL gates after moving to current image bases and a multi-stage AI runtime. This is a time-specific scan, not proof that no vulnerabilities exist. |
| Runtime support | **FAIL — production blocker** | Platform API/tests, Docker and CI still target .NET 8, which ended support on 10 November 2026. Migration to .NET 10 LTS has not been performed or compatibility-tested. |
| Production readiness | **FAIL / BLOCKED** | Unsupported .NET 8, branch protection disabled, independent staging assessment not performed, plus operational residuals listed below. |

## 2. Scope and evidence boundaries

Reviewed the Stage 50-to-Stage 51 changed files, including:
- AI service routes, request schemas, tests, requirements and Dockerfiles.
- Platform API production-security configuration, middleware registration, AI client calls, receipt endpoints/image processor and project dependencies.
- JWT/production-configuration tests, PostgreSQL-backed cross-user authorization tests and receipt-image tests.
- CI/security workflows, Dependabot, production Compose and environment templates.
- README, Stage 51 security plan, security assessment and response guide.

All active API/IDOR testing used generated identities, fake AI providers and a disposable PostgreSQL test database. No production database, real customer data, public deployment, third-party provider or external host was actively probed. No destructive testing or load/DoS testing was performed.

The GitHub Actions runs cited below are the evidence record. Skipped, queued and unavailable steps are not counted as passes.

## 3. Findings

### S51-01 — Build-breaking SkiaSharp exception reference

**Severity:** High (release blocker when present)  
**Evidence:** On the original `c86b427` head, Platform API compilation failed with `CS0246: SKException could not be found`, blocking dependent tests, image builds and C# CodeQL.  
**Remediation:** Commit `fa00fd3439e3e560d6dcfc023873c5ff4a4a81df` removed the unresolved catch type. The Platform API build and CodeQL subsequently passed, and the final validation baseline CI passed the API build, unit tests, PostgreSQL integration tests and deployment smoke.  
**Status:** Fixed and verified on the completed validation baseline. A small later null-safe preference normalization edit has a separate current-head validation run queued.

### S51-02 — Vulnerabilities in earlier AI and web container images

**Severity:** High / Critical (historical findings; remediated on the completed validation baseline)  
**Evidence:** Earlier Trivy scans reported three HIGH Python package findings in the AI image: `jaraco.context` CVE-2026-23949, `setuptools` CVE-2025-47273 and `wheel` CVE-2026-24049. An old web image based on NGINX 1.27 / Alpine 3.21.3 reported 68 findings (65 HIGH, 3 CRITICAL), including Alpine/OpenSSL and other OS packages.  
**Remediation:** Raised vulnerable Python package floors, then replaced the AI image with a multi-stage build: dependency installation occurs in a builder, packaging tools are removed, and only the cleaned CPython runtime tree is copied into a fresh Debian Trixie final image. The web image moved to an unprivileged NGINX 1.31.9 / Alpine 3.24 base with explicit `USER 101`. The web image scan and all three image scans passed on the completed validation baseline.  
**Evidence:** [Security Assessment run 44558532472](https://github.com/harini281/travel_os/actions/runs/44558532472), container image job passed.  
**Status:** PASS on the validation baseline. Current head has a fresh Trivy 0.76.2 run pending; continue to treat container findings as time-sensitive and rescan release images.

### S51-03 — Unsupported .NET runtime

**Severity:** High (production blocker)  
**Evidence:** `services/platform-api/TravelOS.PlatformApi.csproj` and the test project target `net8.0`; Platform packages include ASP.NET Core / EF Core 8.0.11 and Npgsql EF Core 8.0.11. Platform Dockerfile uses `mcr.microsoft.com/dotnet/sdk:8.0` and `mcr.microsoft.com/dotnet/aspnet:8.0`. CI and security workflows set up `8.0.x`. README also describes .NET 8 prerequisites.  
**Lifecycle:** Microsoft lists .NET 8 LTS as ending 11 November 2026 (Microsoft lifecycle page; the date is shown in Pacific Time) and .NET 10 support through 15 November 2028 in Pacific Time. The user-facing equivalent dates are commonly expressed as 10 November 2026 and 14 November 2028. See [Microsoft .NET lifecycle](https://learn.microsoft.com/en-us/lifecycle/products/microsoft-net-and-net-core) and [release/support guidance](https://learn.microsoft.com/en-us/dotnet/core/releases-and-support).  
**Status:** Open. Current .NET 8 runtime is not an acceptable public-production runtime.

### S51-04 — Authorization / IDOR regression coverage

**Severity:** Medium (coverage limitation; no confirmed exploitable IDOR established in this review)  
**Evidence:** The 79-test PostgreSQL integration suite passed. It exercises trip owner/member/outsider read and owner-only update; recommendation/budget membership boundaries; expense history/edit access and no unauthorized DB mutation; recipient-only repayment response; owner-managed invitations and invitee-only acceptance; receipt member file access; uploader/owner confirmation/deletion; member-scoped preferences and group preference visibility; poll listing/voting/closure permissions; and outsider receipt-upload denial with no file or DB side effects. JWT tests cover missing/malformed bearer tokens, wrong signing key, issuer/audience, expiry and future `nbf`.  
**Gap:** This is targeted regression coverage, not a full exhaustive method/verb matrix over every itinerary, notification, group membership, poll and finance route. No independently conducted penetration test has been performed.  
**Status:** PASS for the routes and permission cases exercised by the 79 passing database tests; broader route-family coverage remains a follow-up.

### S51-05 — Receipt image processing and storage controls

**Severity:** Medium (residual resource/operational test gaps)  
**Source implementation observed:** JPEG/PNG/WebP allow-list, Skia codec format detection, single-frame restriction, maximum edge 8,000 pixels, maximum 16 million pixels, 128 MiB decoded-byte budget, 8 MiB upload limit, request/form limits, JPEG re-encoding, generated GUID storage filename, private storage root and trip membership checks. The Platform API stores and forwards normalized JPEG bytes rather than the original upload.  
**Tests observed:** Supported JPEG/PNG/WebP, malformed image data, MIME mismatch, unsupported formats, excessive dimension/pixel count, trailing-content discard, >8 MiB rejection, path-shaped filename handling, outsider upload with no file/DB side effect, member file read, and uploader/owner receipt confirmation/deletion. The browser journey confirmed receipt upload, manual confirmation and duplicate-image detection.  
**Gaps:** No dedicated animated-file regression fixture (the implementation rejects additional frames); no measured peak memory or processing-time stress test; no malware-scanning service is configured. Receipt deletion currently removes the database row before attempting file deletion and swallows `IOException`, which can leave an orphaned stored file.  
**Status:** PASS for the tested image-validation/authorization cases. Performance bounds, dedicated animation test, malware scanning and deletion reconciliation remain open.

### S51-06 — CodeQL, Trivy and GitHub security automation

**Severity:** Medium (coverage and maintenance residuals)  
**Evidence:** On [Security Assessment run 44558532472](https://github.com/harini281/travel_os/actions/runs/44558532472), both CodeQL jobs passed and logs confirmed “Successfully uploaded results” and “Analysis upload status is complete.” SARIF artifacts were also retained: C# `14545139248`, Web/AI `14547585066`. The filesystem/secret/config job passed; Trivy configuration now has an exit code of 1 for HIGH/CRITICAL findings.  
**Trivy version limitation:** The successful baseline run used Trivy 0.70 and logged that the AWS CloudFront logging Rego policy was skipped because it required Trivy 0.72 or newer. The current branch pins all Trivy actions to `v0.76.2`, released 5 November 2026, to ensure policy compatibility. The post-pin run is queued/in progress and must pass before claiming complete configuration-policy coverage.  
**Status:** PASS for CodeQL uploads, dependency audit, filesystem/secret scan and image scans on the validation baseline; configuration scan coverage rerun pending with Trivy 0.76.2.

### S51-07 — Dependency reproducibility

**Severity:** Medium (supply-chain risk)  
**Evidence:** `apps/web/package.json` uses `latest` for React, Vite, TypeScript and several other direct dependencies; no `package-lock.json` exists and CI runs `npm install`. Python runtime requirements use ranges. Dependabot is configured weekly for Actions, npm, pip, NuGet and Docker.  
**Latest completed evidence:** On `c2fc271`, npm reported 0 vulnerabilities, pip-audit reported no known vulnerabilities and the NuGet high/critical check passed.  
**Status:** Partial. Audit output is a time-specific advisory check, not deterministic resolution or an SBOM. Commit a reproducible lockfile and use `npm ci` with a reviewed update process.

### S51-08 — Operational security residuals

**Severity:** Medium  
- Rate limiting is process-local and is not coordinated across multiple API replicas.
- Receipt storage is a private local volume; there is no verified malware scanner, object-store lifecycle/retention or automated deletion/reconciliation.
- Receipt deletion can leave an orphaned file if filesystem deletion fails after the DB row is removed.
- ASP.NET Core Data Protection keys are container-local unless a persistent, protected key directory is configured; configure a persistent key ring for production.
- The final preference normalization source now guards nullable `Pace`/`TravelStyle` values; verify the current-head compile is warning-free in the latest CI run.
- Branch protection was reported disabled on `stage51-security-testing`. Configure required reviews and CI/security checks before production promotion.
- CI emits runner deprecation warnings because actions/checkout@v4, setup-dotnet@v4, setup-node@v4 and setup-python@v5 are still identified as Node 20 actions being forced to Node 24. Update action major versions as a separate maintenance change.
- No independent staging penetration test, external specialist review or cloud IAM/ingress assessment was performed.

## 4. AI API authentication verification

| Check | Source/test evidence | Result |
|---|---|---|
| Planner draft route protected | `Depends(require_platform_service_auth)` | PASS |
| Recommendations route protected | `Depends(require_platform_service_auth)` | PASS |
| Receipt extraction route protected | `Depends(require_platform_service_auth)` | PASS |
| Missing header rejected | Parameterized API tests for all three POST routes expect 401 | PASS |
| Incorrect token rejected | Parameterized API tests for all three POST routes expect 401 | PASS |
| Missing server token fails closed | Test expects 503 | PASS |
| Constant-time comparison | Python `hmac.compare_digest` over UTF-8 byte strings | PASS |
| Platform API sends header | Draft, recommendation and receipt clients call `AddAiServiceAuthentication` | PASS by source inspection |
| Platform fails closed on missing/short token | Client guard and production configuration validation | PASS by source/test inspection |
| Health route public/minimal | Tests assert exact `{"status":"ok"}` shape | PASS |
| Malformed-present token cases | Wrong token is tested; no distinct malformed-token test | PARTIAL |
| Runtime secret/log exposure review | Generic auth errors and no token logging observed in reviewed paths; no complete runtime log assurance | PARTIAL |

Never expose `AI_SERVICE_TOKEN` to the browser or place it in a `VITE_*` variable. Keep it distinct from the JWT signing key and rotate it if suspected compromised.

## 5. GitHub Actions verification record

### Completed validation baseline — commit `2ceec4dbb85ef10d8d2144f8d5bcf37ab6e2de9f`

[TravelOS CI — run 44558532457](https://github.com/harini281/travel_os/actions/runs/44558532457) — **PASS**:
- PASS: Platform API build.
- PASS: Platform API unit tests — 48 passed, 0 failed, 27 skipped (PostgreSQL-only cases are exercised by the separate integration job).
- PASS: PostgreSQL integration tests — 79 passed, 0 failed.
- PASS: AI API tests — 48 passed, 1 warning.
- PASS: Web typecheck/build.
- PASS: PostgreSQL migrations.
- PASS: Container deployment build and smoke test — web/API/AI health, migrations and internal network/port isolation assertions passed.
- PASS: Browser E2E — 16 passed, 0 failed across Chromium, Firefox, WebKit, mobile Chromium, mobile WebKit and tablet Chromium. Receipt upload/confirmation/duplicate detection and accessibility journeys passed.

[TravelOS Security Assessment — run 44558532472](https://github.com/harini281/travel_os/actions/runs/44558532472) — **PASS**:
- PASS: CodeQL — Platform API; SARIF upload completed successfully.
- PASS: CodeQL — Web and AI; SARIF upload completed successfully.
- PASS: NuGet/npm/Python dependency audit. npm reported 0 vulnerabilities; pip-audit reported no known vulnerabilities; NuGet high/critical gate passed.
- PASS: filesystem/secret scan.
- PASS: Trivy configuration scan HIGH/CRITICAL gate, with the Trivy 0.70 policy-skip limitation noted below.
- PASS: Trivy Platform API, AI API and web container images at the HIGH/CRITICAL threshold.
- SARIF artifacts: C# `14545139248`; Web/AI `14547585066`. Trivy filesystem/config artifact: `14546990802`.

### Latest Trivy policy compatibility update

Commit `324929de999809ec834494adbd3adf225b9fd0aa` pins each Trivy action to `v0.76.2` (the 5 November 2026 release). This addresses the Trivy 0.70 warning that the AWS CloudFront logging policy required Trivy 0.72+. The new branch-head workflow runs were queued when this report was written:
- [TravelOS Security Assessment — run 44559376151](https://github.com/harini281/travel_os/actions/runs/44559376151) — queued.
- [TravelOS CI — run 44559376142](https://github.com/harini281/travel_os/actions/runs/44559376142) — queued.

The branch subsequently received a null-safe preference normalization change at `a40c73ef36b6693a7a9ce05a789bd5e2e4604155`; its exact-head build/security workflows are also pending. Do not transfer the prior baseline PASS status to this new head until its runs complete.

### Earlier failed runs and regressions corrected

- [CI run 44549756999](https://github.com/harini281/travel_os/actions/runs/44549756999) on `c86b427` failed due to the unavailable `SKException` symbol; downstream API tests and image build were blocked.
- [Security run 44549757071](https://github.com/harini281/travel_os/actions/runs/44549757071) on `c86b427` failed during Platform API CodeQL/image build due to the same compile error.
- [Security run 44551270049](https://github.com/harini281/travel_os/actions/runs/44551270049) identified one remaining HIGH `setuptools 70.3.0` CVE in the AI image after a pip upgrade, and the image scans were incomplete.
- Later runs exposed and corrected a test assertion compile error, a deployment smoke test expecting the old public health `mode` field, a stale PNG expectation after JPEG normalization, and an invalid E2E PNG fixture.
- Historical web image Trivy runs reported 68 HIGH/CRITICAL findings (65 HIGH, 3 CRITICAL) in the old Alpine 3.21.3 base. The refreshed Alpine 3.24.4 web image passed Trivy on the completed validation baseline.

### Failed / skipped / unavailable distinctions

- No tests or scans are reported as passing solely because a job was created or queued.
- The 27 skips in the Platform API unit-only job are PostgreSQL-dependent cases; the separate disposable PostgreSQL job ran 79 tests and all passed.
- The browser E2E suite on the completed validation baseline ran 16 tests, all passed. Earlier E2E failures were test expectation/fixture regressions and were corrected.
- The baseline Trivy 0.70 configuration scan skipped one AWS CloudFront policy because the policy required 0.72+. Trivy 0.76.2 is pinned on the current branch; exact-head rerun pending.
- No independent DAST, third-party penetration test, cloud IAM review or production deployment assessment was performed.
- The Node 20 action deprecation warning is recorded; no CI action major-version migration was included in this stage.

## 6. .NET 10 LTS migration plan (not executed)

The repository still targets .NET 8. A migration must be a separately reviewed change, not a blind string replacement:

1. Create a runtime-upgrade branch or follow-up stage based on the reviewed security branch; keep Stage 51 reviewable.
2. Change Platform API and xUnit test target frameworks to `net10.0`.
3. Move Microsoft ASP.NET Core, Identity, EF Core and test-host packages to compatible serviced 10.x releases. Select a compatible Npgsql EF Core provider version and verify provider/EF Core major-version alignment; review any breaking APIs and migrations.
4. Review SkiaSharp and native Linux assets on the chosen .NET 10 runtime image.
5. Change Platform Docker build/runtime images to supported `mcr.microsoft.com/dotnet/sdk:10.0` and `mcr.microsoft.com/dotnet/aspnet:10.0`, rebuild and inspect the final image.
6. Update `actions/setup-dotnet` in CI and security workflows to the selected .NET 10 SDK; verify the CodeQL C# manual build and dependency audit.
7. Check all .NET test projects, build scripts, developer setup docs, container smoke tests and release automation for references to 8.0.
8. Run full unit, PostgreSQL integration, migrations, deployment smoke, browser E2E and CodeQL/dependency/image scans against the same commit; inspect all failures.
9. Deploy only to isolated staging, run migration/backup/restore checks, observe runtime/health/performance, and obtain independent security review before production.

**Compatibility test result:** Not performed. Current project packages and tests remain .NET 8; no .NET 10 build or migration test is claimed.

## 7. Production blockers

1. **Unsupported .NET 8 runtime.** Platform API/tests, Docker images and workflow SDKs still target .NET 8, which ended support on 10 November 2026. Migrate to .NET 10 LTS in a separately reviewed change and run the full suite on it.
2. **Current-head validation.** The completed green CI/security baseline is commit `2ceec4d`; Trivy 0.76.2 and null-safe preference changes are later commits. Their exact-head workflows must pass before the branch is called fully validated.
3. **Branch protection is disabled.** Enable required pull-request review and required CI/security checks before production promotion.
4. **No independent security test.** Obtain an authorized independent staging assessment/penetration test; this repository review and automated test suite are not a penetration test.
5. **Operational security controls.** Configure a persistent Data Protection key ring, private receipt retention/deletion reconciliation, malware scanning if required by the threat model, distributed rate limiting if running multiple replicas, and reviewed cloud IAM/ingress/backup settings.
6. **Dependency reproducibility.** The web project uses `latest` for several direct dependencies, has no committed `package-lock.json`, and CI uses `npm install`. Commit a lockfile and use `npm ci`; retain a deliberate update process for Python and NuGet ranges.
7. **Scanner maintenance.** Validate the pinned Trivy 0.76.2 config scan and update deprecated Node 20-targeting GitHub Actions.

## 8. Assessment conclusion

**FAIL / BLOCKED for public production.** The completed Stage 51 validation baseline passes CI and the configured security gates, including CodeQL uploads, dependency audits and all three image scans. The baseline also passes 79 PostgreSQL integration tests and 16 browser E2E journeys. The Trivy 0.76.2 policy-coverage rerun is still in progress/queued; the repository still targets unsupported .NET 8. Branch protection, independent testing and several operational controls remain outstanding.

This report intentionally does not certify TravelOS as secure and does not describe the work as a penetration test.
