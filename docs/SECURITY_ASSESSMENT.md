# TravelOS — Stage 51 Security Assessment

**Assessment date:** 19 November 2026  
**Repository:** https://github.com/harini281/travel_os  
**Branch:** `stage51-security-testing`  
**Baseline:** `stage50-production-readiness`  
**Scope:** Source and configuration review, repository diff review, safe automated tests, and GitHub Actions evidence.

> **Overall result: FAIL / BLOCKED — not approved for public production.** This is a repository-level verification, not an independent security audit or penetration test. No production system or external provider was probed. Do not describe TravelOS as fully secure.

## 1. Executive result

The Stage 51 branch is based on Stage 50 with no base-branch divergence. The initial source comparison identified 31 changed paths; after adding the canonical assessment/response documents, the open Stage 51 draft PR lists 33 changed files. The reviewed changes include the AI service token boundary, receipt image normalization, authorization tests, GitHub security automation, Dependabot, deployment configuration and documentation. Remediation commits on the same branch include:

- `fa00fd3439e3e560d6dcfc023873c5ff4a4a81df` — removed the unavailable `SKException` catch that was breaking the .NET build.
- `c2fc271934d5e4e8e3d570a8bfb5838c80a64865` — raises the AI image's setuptools floor to the patched `78.1.1` version reported by Trivy.
- `0b31e6754bebc8d04e7f04fb50e718fde7204a08` — corrects the receipt image test's byte-sequence assertion so the test project can compile.
- `a7d4e9ce3803cd96b7c08a82f433c4409052b031` — aligns the deployment smoke test with the intentionally minimal public AI health response.
- `90600a0ca0c6a73a1c780463fb9786b719aea3a1` — attempts to remove the stale Debian `python3-setuptools` package from the AI image and asserts the active Python package is patched.

The code-build correction was verified by the Platform API build and C# CodeQL on commit `c2fc271`. That run then exposed a second issue: the new image unit test itself failed to compile with `CS1503` because an xUnit `DoesNotContain` overload was selected incorrectly; the assertion was changed to `Span.IndexOf` in `0b31e675`. The deployment smoke test also failed because it expected a `mode` field that was intentionally removed from the public health response; the workflow expectation was updated in `a7d4e9c`. The security run on `c2fc271` detected one remaining HIGH `setuptools 70.3.0` copy in the AI container despite the active pip-installed `setuptools 84.2.1`; `90600a0` attempts to remove the stale Debian package. The workflow runs for the current branch head `90600a0` were queued at the time this report was updated, so those latest fixes are **not yet verified**.

| Category | Result | Basis |
|---|---|---|
| AI API service authentication | **PASS — source and API tests** | All three internal POST routes require the header; absent/wrong token tests, fail-closed configuration test and public-health response test exist. AI API test job passed on `c2fc271`; latest branch-head rerun is queued. |
| Receipt image security | **BLOCKED** | The Platform API build passed on `c2fc271`, but the image unit-test project failed to compile due to the byte-array assertion (corrected in `0b31e675`). The current-head test run is queued. Animation and real resource-use behaviour are not fully tested. |
| Authorization / IDOR | **INCOMPLETE** | Cross-user tests cover trips, budgets/recommendations, expenses, repayments, invitations and receipt file/upload access. The PostgreSQL test project did not compile on `c2fc271` due to the test assertion error; the corrected current-head run is queued. Route-family coverage remains incomplete. |
| GitHub security automation | **PARTIAL** | On `c2fc271`, both CodeQL jobs, filesystem/secret/config scan and package audit passed. The container image job failed on one HIGH AI-image finding; the web image scan was skipped after that failure. Configuration scan is report-only. SARIF is stored as workflow artifacts, not uploaded to GitHub Code Scanning. Latest head run is queued. |
| Dependency vulnerability checks | **PASS WITH LIMITS** | npm reported 0 vulnerabilities; pip-audit reported no known vulnerabilities; NuGet high/critical gate passed on the previous head. This does not prove the container images are free of vulnerabilities. |
| Container image security | **FAIL / BLOCKED** | Trivy on `c2fc271` found one remaining HIGH finding (`setuptools 70.3.0`) in the AI image. Platform API image scan passed; web image scan was skipped. The latest Dockerfile attempts to remove the stale Debian package; a fresh scan is queued, not yet passed. |
| Runtime support | **FAIL — production blocker** | Platform API and tests target .NET 8, Docker uses .NET 8 SDK/runtime, and workflows install SDK 8. .NET 8 support ended 10 November 2026. No .NET 10 migration was performed. |
| Production readiness | **FAIL** | Unsupported runtime, build/test validation outstanding, image scan pending and independent staging security assessment not performed. |

## 2. Scope and evidence boundaries

Reviewed the complete Stage 50-to-Stage 51 file list returned by GitHub compare, including:

- AI service routes, request schemas, tests, requirements and Dockerfile.
- Platform API production-security configuration, middleware registration, AI client calls, receipt endpoints, image processor and project dependencies.
- JWT and production-configuration tests, PostgreSQL-backed authorization tests and receipt-image tests.
- CI and security workflows, Dependabot configuration, production Compose and example environment files.
- README, Stage 51 security plan and the previous Stage 51 report.

Tests use generated identities, fake AI providers and disposable CI PostgreSQL containers. The repository test plan explicitly scopes active testing to local application code and disposable resources. No production database, live customer data, third-party service, public internet host or production deployment was accessed.

## 3. Findings

### S51-01 — Build-breaking SkiaSharp exception reference

**Severity:** High (release blocker)  
**Evidence:** On `c86b427`, Platform API compilation failed at `ReceiptImageProcessor.cs:83` with `CS0246: SKException could not be found`, blocking downstream build/test/scan steps.  
**Remediation pushed:** `fa00fd3439e3e560d6dcfc023873c5ff4a4a81df` removes the unresolved catch type. The Platform API build and Platform API CodeQL jobs subsequently passed on `c2fc271`.  
**Status:** Source build issue corrected and verified on `c2fc271`; full unit/integration testing remains pending because the test project had a separate compile error, corrected in `0b31e675`.

### S51-02 — Three HIGH vulnerabilities in an earlier AI container scan

**Severity:** High  
**Evidence:** Trivy job logs for [Security Assessment run 44548475831](https://github.com/harini281/travel_os/actions/runs/44548475831) reported:
- `jaraco.context` 5.3.0 — CVE-2026-23949 — fixed in 6.1.0.
- `setuptools` 70.3.0 — CVE-2025-47273 — fixed in 78.1.1.
- `wheel` 0.45.1 — CVE-2026-24049 — fixed in 0.46.2.

**Remediation pushed:** AI image installs `jaraco.context>=6.1.0` and `wheel>=0.46.2`; commit `c2fc271` added `setuptools>=78.1.1`. Trivy then confirmed the active setuptools distribution was 84.2.1 but still detected a second 70.3.0 system package. Commit `90600a0` purges Debian `python3-setuptools` if installed and asserts the Python-imported version.  
**Status:** Two earlier findings no longer appeared in the `c2fc271` report; one HIGH remains. The `90600a0` image scan is queued. Do not mark resolved until Trivy confirms zero HIGH/CRITICAL findings for all built images.

### S51-03 — Unsupported .NET runtime

**Severity:** High (production blocker)  
**Evidence:** `services/platform-api/TravelOS.PlatformApi.csproj` and the test project target `net8.0`; Platform packages include ASP.NET Core / EF Core 8.0.11 and Npgsql EF Core 8.0.11. Platform Dockerfile uses `mcr.microsoft.com/dotnet/sdk:8.0` and `mcr.microsoft.com/dotnet/aspnet:8.0`. CI and security workflows set up `8.0.x`. README also describes .NET 8 prerequisites.  
**Lifecycle:** Microsoft lists .NET 8 LTS as ending 11 November 2026 (Microsoft lifecycle page; the date is shown in Pacific Time) and .NET 10 support through 15 November 2028 in Pacific Time. The user-facing equivalent dates are commonly expressed as 10 November 2026 and 14 November 2028. See [Microsoft .NET lifecycle](https://learn.microsoft.com/en-us/lifecycle/products/microsoft-net-and-net-core) and [release/support guidance](https://learn.microsoft.com/en-us/dotnet/core/releases-and-support).  
**Status:** Open. Current .NET 8 runtime is not an acceptable public-production runtime.

### S51-04 — IDOR / authorization coverage is improved but incomplete

**Severity:** Medium (coverage gap; not a confirmed exploit)  
**Evidence:** New PostgreSQL-backed tests exercise trip owner/member/outsider read and owner-only update; recommendation and budget boundaries; expense history/edit access and no unauthorized DB change; repayment recipient-only response; owner-managed invitation list and invitee-only acceptance; receipt member file access, outsider denial, uploader/owner deletion; receipt upload MIME mismatch, filename sanitization and oversized-file rejection. JWT validation tests cover missing/malformed bearer, wrong signing key, wrong issuer/audience, expired and future-`nbf` tokens.  
**Gap:** The test matrix does not independently exercise every endpoint and verb for itineraries, recommendations, receipts (including confirmation), financial resources and all group membership/preference/voting functions. The current test suite cannot be called comprehensive. The PostgreSQL test job on the earlier completed head failed before tests could execute due to the same C# compile error.  
**Status:** Incomplete; the tests are real PostgreSQL-backed integration tests, but on `c2fc271` the test assembly failed to compile at `ReceiptImageProcessorTests.cs:95` before the cases ran. The byte-array assertion was corrected in `0b31e675`; rerun against the disposable database is queued. Add confirmation/remaining group resource tests.

### S51-05 — Receipt image controls need more test evidence

**Severity:** Medium  
**Source implementation observed:** JPEG/PNG/WebP allow-list, Skia codec format detection, static-frame restriction, maximum edge 8,000 pixels, maximum 16 million pixels, 128 MiB output-info decoded-byte budget, re-encoding to JPEG, generated GUID storage filename, private storage root, request/form size limits and membership checks on retrieval. The endpoint stores and forwards normalized bytes, not the original file.  
**Tests observed:** Valid JPEG/PNG/WebP, malformed data, MIME mismatch, unsupported declared content type, excessive dimensions and pixel count, trailing bytes, path-shaped client filename and >8 MiB upload.  
**Gaps:** No explicit animated-image regression test is present in the inspected test file; no measurement/stress test demonstrates peak process memory or processing-time bounds; cleanup on all failure paths and unauthorized upload side effects need additional integration coverage. The receipt delete handler swallows `IOException` after deleting the DB row, so a failed disk deletion can leave an orphaned file. A decode/pixel ceiling is not a malware-scanning control.  
**Status:** Platform API build passed on `c2fc271`; unit-test compilation failed before running these tests and was corrected in `0b31e675`. Current-head execution is queued; residual test gaps remain after it passes.

### S51-06 — Code-scanning results are artifacts, not GitHub Code Scanning alerts

**Severity:** Medium (visibility/triage gap)  
**Evidence:** `.github/workflows/security.yml` configures `github/codeql-action/analyze@v4` with `upload: false` and the job uses `security-events: read`. On `c2fc271`, both CodeQL jobs passed and SARIF was retained as artifacts `14543037675` (C#) and `14542098628` (Web/AI) from [Security Assessment run 44551270049](https://github.com/harini281/travel_os/actions/runs/44551270049). These are not uploaded GitHub Code Scanning alerts.  
**Configuration scan:** Trivy configuration scanning is set to `exit-code: "0"`, intentionally preserving baseline findings for reviewer triage. Its job success is not a claim that configuration findings are absent or cleared.  
**Status:** Partial. Enable a reviewed upload/triage policy or clearly keep artifact-only reporting as an accepted limitation; do not report artifact storage as GitHub alert triage.

### S51-07 — Dependency reproducibility

**Severity:** Medium (supply-chain risk)  
**Evidence:** `apps/web/package.json` uses `latest` for React, Vite, TypeScript and several other direct dependencies; no `package-lock.json` exists and CI runs `npm install`. Python runtime requirements use ranges. Dependabot is configured weekly for Actions, npm, pip, NuGet and Docker.  
**Latest completed evidence:** On `c2fc271`, npm reported 0 vulnerabilities, pip-audit reported no known vulnerabilities and the NuGet high/critical check passed.  
**Status:** Partial. Audit output is a time-specific advisory check, not deterministic resolution or an SBOM. Commit a reproducible lockfile and use `npm ci` with a reviewed update process.

### S51-08 — Operational security residuals

**Severity:** Medium  
- Rate limiting is process-local; it is not coordinated across multiple API replicas.
- Receipt storage is a private local volume; there is no verified malware-scanning workflow, object-store lifecycle/retention or deletion automation.
- The receipt delete handler deletes the DB row before file deletion and catches `IOException` without surfacing the orphan; add cleanup/reconciliation monitoring.
- ASP.NET Core Data Protection logs that its key directory is container-local and may not persist across container destruction; configure a protected persistent key ring for production.
- Two CS8602 nullable-reference warnings remain in `Preferences/PreferenceEndpoints.cs` lines 104–105. Validate/normalize nullable request strings with a guard that the compiler understands.
- Branch protection was reported disabled on the Stage 51 branch. Protect the branch and require CI/review checks before production promotion.
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

### Completed workflow evidence on remediation commit `c2fc271934d5e4e8e3d570a8bfb5838c80a64865`

[TravelOS CI — run 44551269802](https://github.com/harini281/travel_os/actions/runs/44551269802) — **overall still in progress when inspected**:
- PASS: Platform API application build.
- PASS: AI API tests.
- PASS: Web typecheck/build.
- PASS: PostgreSQL migration validation.
- FAIL: Platform API unit tests — test-project compilation failed with `CS1503` at `ReceiptImageProcessorTests.cs:95` (`byte[]` passed to an incompatible xUnit async-enumerable overload).
- FAIL: Platform API PostgreSQL integration tests — same test-project compile error, so the integration cases did not execute.
- FAIL: Container deployment smoke — all images built and services started healthy, but the smoke assertion expected `data['mode']=='demo'` from the AI health response. The health response was intentionally reduced to `{"status":"ok"}`; workflow assertion corrected in `a7d4e9c`. Migration and port-isolation assertions were skipped after the earlier health assertion failed.
- Browser E2E job: isolated PostgreSQL, migrations, web build, browser installation, AI mock and Platform API startup passed; the browser journey step was still running at last inspection. Do not call it passed until completed.

[TravelOS Security Assessment — run 44551270049](https://github.com/harini281/travel_os/actions/runs/44551270049) — **FAIL**:
- PASS: CodeQL — Platform API.
- PASS: CodeQL — Web and AI.
- PASS: NuGet/npm/Python dependency audit.
- PASS: filesystem/secret/config scan job. Configuration scan is report-only (`exit-code: 0`), so findings still require human review.
- FAIL: container image vulnerability scans — Platform API image scan passed; AI image found one HIGH `setuptools 70.3.0` (`CVE-2025-47273`); web image build/scan were skipped after failure.
- SARIF artifacts retained: C# `14543037675`; Web/AI `14542098628`. Filesystem/config artifact: `14542652226`. No Code Scanning alert upload was performed.

The latest corrections on the current branch head `90600a0ca0c6a73a1c780463fb9786b719aea3a1` include the test assertion correction, health assertion correction and system setuptools purge. At the time of this update, its push-triggered workflows were queued:
- [TravelOS CI — run 44551871672](https://github.com/harini281/travel_os/actions/runs/44551871672) — queued.
- [TravelOS Security Assessment — run 44551871709](https://github.com/harini281/travel_os/actions/runs/44551871709) — queued.

### Earlier completed baseline-head evidence

[TravelOS CI — run 44549756999](https://github.com/harini281/travel_os/actions/runs/44549756999) on `c86b427` — **FAIL**: AI API tests, web build and migrations passed; Platform API build/tests failed on unresolved `SKException`; deployment image build failed; browser E2E could not start the Platform API.

[TravelOS Security Assessment — run 44549757071](https://github.com/harini281/travel_os/actions/runs/44549757071) on `c86b427` — **FAIL**: CodeQL Web/AI, filesystem/secret/config and dependency audit passed; Platform API CodeQL and container image builds failed at the unresolved type.

An earlier image scan in [run 44548475831](https://github.com/harini281/travel_os/actions/runs/44548475831) reported three HIGH AI-image findings:
- `jaraco.context` 5.3.0 — CVE-2026-23949 — fixed 6.1.0.
- `setuptools` 70.3.0 — CVE-2025-47273 — fixed 78.1.1.
- `wheel` 0.45.1 — CVE-2026-24049 — fixed 0.46.2.

### Failed / skipped / unavailable distinctions

- Platform API build on `c86b427` failed compilation; later application build and C# CodeQL succeeded on `c2fc271`.
- Unit and PostgreSQL integration tests on `c2fc271` failed at test-project compilation before execution. The test compile issue was corrected; latest head run is queued.
- The `c2fc271` deployment stack started, but the smoke test stopped on the obsolete AI health `mode` assertion. The assertion was aligned with the minimal health contract; latest head run is queued.
- The E2E run on `c2fc271` had not completed the browser journey at last inspection; no E2E pass is claimed.
- The AI container scan on `c2fc271` failed on one HIGH `setuptools` finding. Web image scan was skipped. The latest system-package purge is not yet validated.
- The Trivy configuration scan is configured report-only; job success is not a clean configuration result.
- CodeQL is artifact-only (`upload: false`); no GitHub Code Scanning alert upload is evidenced.
- No independent DAST, third-party penetration test, cloud configuration audit or external production scan was performed.

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

1. Current project runtime remains unsupported .NET 8.
2. Build and all dependent tests/scans must pass on the final commit after the compiler correction.
3. Fresh AI/Platform/Web Trivy image scans must complete; the historical AI-image HIGH findings must be shown resolved.
4. Complete cross-user IDOR regression coverage for the remaining itinerary, group and receipt-confirmation paths.
5. Decide whether CodeQL SARIF is uploaded for central alert triage and triage existing Trivy configuration findings.
6. Add reproducible dependency lockfiles and reviewed update controls.
7. Add staging security testing by an independent qualified tester; evaluate deployment IAM, secrets, ingress/TLS, backup restore, monitoring, rate limits and receipt lifecycle.
8. Enable branch protection and require passing CI/security checks and review.

## 8. Assessment conclusion

**FAIL / BLOCKED.** Stage 51 adds useful security controls and tests, but there are known build and historical image vulnerabilities, incomplete test evidence, an unsupported runtime, and incomplete automation/operational gates. The latest remediation-head workflows must be checked before the code fix or image updates can be called verified.

This report intentionally does not certify TravelOS as secure and does not describe the work as a penetration test.
