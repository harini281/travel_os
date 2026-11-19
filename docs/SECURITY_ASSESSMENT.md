# TravelOS — Stage 51 Security Assessment

**Assessment date:** 19 November 2026  
**Repository:** https://github.com/harini281/travel_os  
**Branch:** `stage51-security-testing`  
**Baseline:** `stage50-production-readiness`  
**Scope:** Source and configuration review, repository diff review, safe automated tests, and GitHub Actions evidence.

> **Overall result: FAIL / BLOCKED — not approved for public production.** This is a repository-level verification, not an independent security audit or penetration test. No production system or external provider was probed. Do not describe TravelOS as fully secure.

## 1. Executive result

The Stage 51 branch is based on Stage 50 and is ahead without divergence. The compare endpoint reported 31 changed paths against the base branch. The reviewed changes include the AI service token boundary, receipt image normalization, authorization tests, GitHub security automation, Dependabot, deployment configuration and documentation. Two narrow remediation commits were subsequently pushed to the same branch:

- `fa00fd3439e3e560d6dcfc023873c5ff4a4a81df` — removed the unavailable `SKException` catch that was breaking the .NET build.
- `c2fc271934d5e4e8e3d570a8bfb5838c80a64865` — raises the AI image's setuptools floor to the patched `78.1.1` version reported by Trivy.

The new CI and security workflows on the second commit were still running at the time of this report. The two changes are **not verified fixes** until those jobs pass. Earlier completed runs on `c86b4272924d54c5cd36cdb227a74e008c8473a9` conclusively failed due to the compile error. The logs showed the same `CS0246: SKException could not be found` error in the API build, unit tests, PostgreSQL integration tests, deployment image build and Platform API CodeQL build.

| Category | Result | Basis |
|---|---|---|
| AI API service authentication | **PASS — source and API tests** | All three internal POST routes require the header; absent/wrong token tests, fail-closed configuration test and public-health response test exist and the AI API test job passed on the pre-fix head. Current-head rerun is pending. |
| Receipt image security | **BLOCKED** | Format detection, decode, dimension/pixel limits and JPEG re-encoding are implemented with unit tests; the Platform API did not compile on the previous head, and the remediation is awaiting current-head CI. Animation and real resource-use behaviour are not fully tested. |
| Authorization / IDOR | **INCOMPLETE** | New PostgreSQL cross-user tests cover several key resources, but route-family coverage is not exhaustive; Platform API integration test execution failed on the compile error. |
| GitHub security automation | **FAIL / PARTIAL** | Web/AI CodeQL, file/secret scan and package audit completed successfully on the previous head. Platform API CodeQL and image scans failed before completing. Configuration scan is report-only. SARIF is stored as workflow artifacts, not uploaded to GitHub Code Scanning. |
| Dependency vulnerability checks | **PASS WITH LIMITS** | npm reported 0 vulnerabilities; pip-audit reported no known vulnerabilities; NuGet high/critical gate passed on the previous head. This does not prove the container images are free of vulnerabilities. |
| Container image security | **FAIL / BLOCKED** | Historical Trivy run found three HIGH findings in the AI image. Current Dockerfile updates include the corresponding patched versions; latest scan is pending. A later scan on the original head could not run because the Platform API image build failed. |
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
**Evidence:** On commit `c86b4272924d54c5cd36cdb227a74e008c8473a9`, Platform API compilation failed at `ReceiptImageProcessor.cs:83` with `CS0246: SKException could not be found`. This blocked API build, unit tests, PostgreSQL tests, image builds, deployment smoke tests and the C# CodeQL build.  
**Remediation pushed:** `fa00fd3439e3e560d6dcfc023873c5ff4a4a81df` removes the unresolved catch type.  
**Status:** Pending revalidation on the latest branch head. Do not mark fixed until new .NET build, tests and Docker image build pass.

### S51-02 — Three HIGH vulnerabilities in an earlier AI container scan

**Severity:** High  
**Evidence:** Trivy job logs for [Security Assessment run 44548475831](https://github.com/harini281/travel_os/actions/runs/44548475831) reported:
- `jaraco.context` 5.3.0 — CVE-2026-23949 — fixed in 6.1.0.
- `setuptools` 70.3.0 — CVE-2025-47273 — fixed in 78.1.1.
- `wheel` 0.45.1 — CVE-2026-24049 — fixed in 0.46.2.

**Remediation pushed:** Existing AI image commands raised `jaraco.context` and `wheel`; commit `c2fc271934d5e4e8e3d570a8bfb5838c80a64865` adds `setuptools>=78.1.1`.  
**Status:** Pending a successful fresh AI image build and Trivy scan. Previous versions and scan findings remain part of the evidence; no claim of remediation is made yet.

### S51-03 — Unsupported .NET runtime

**Severity:** High (production blocker)  
**Evidence:** `services/platform-api/TravelOS.PlatformApi.csproj` and the test project target `net8.0`; Platform packages include ASP.NET Core / EF Core 8.0.11 and Npgsql EF Core 8.0.11. Platform Dockerfile uses `mcr.microsoft.com/dotnet/sdk:8.0` and `mcr.microsoft.com/dotnet/aspnet:8.0`. CI and security workflows set up `8.0.x`. README also describes .NET 8 prerequisites.  
**Lifecycle:** Microsoft lists .NET 8 LTS as ending 11 November 2026 (Microsoft lifecycle page; the date is shown in Pacific Time) and .NET 10 support through 15 November 2028 in Pacific Time. The user-facing equivalent dates are commonly expressed as 10 November 2026 and 14 November 2028. See [Microsoft .NET lifecycle](https://learn.microsoft.com/en-us/lifecycle/products/microsoft-net-and-net-core) and [release/support guidance](https://learn.microsoft.com/en-us/dotnet/core/releases-and-support).  
**Status:** Open. Current .NET 8 runtime is not an acceptable public-production runtime.

### S51-04 — IDOR / authorization coverage is improved but incomplete

**Severity:** Medium (coverage gap; not a confirmed exploit)  
**Evidence:** New PostgreSQL-backed tests exercise trip owner/member/outsider read and owner-only update; recommendation and budget boundaries; expense history/edit access and no unauthorized DB change; repayment recipient-only response; owner-managed invitation list and invitee-only acceptance; receipt member file access, outsider denial, uploader/owner deletion; receipt upload MIME mismatch, filename sanitization and oversized-file rejection. JWT validation tests cover missing/malformed bearer, wrong signing key, wrong issuer/audience, expired and future-`nbf` tokens.  
**Gap:** The test matrix does not independently exercise every endpoint and verb for itineraries, recommendations, receipts (including confirmation), financial resources and all group membership/preference/voting functions. The current test suite cannot be called comprehensive. The PostgreSQL test job on the earlier completed head failed before tests could execute due to the same C# compile error.  
**Status:** Incomplete; expand negative access tests and rerun against a disposable database after the build fix.

### S51-05 — Receipt image controls need more test evidence

**Severity:** Medium  
**Source implementation observed:** JPEG/PNG/WebP allow-list, Skia codec format detection, static-frame restriction, maximum edge 8,000 pixels, maximum 16 million pixels, 128 MiB output-info decoded-byte budget, re-encoding to JPEG, generated GUID storage filename, private storage root, request/form size limits and membership checks on retrieval. The endpoint stores and forwards normalized bytes, not the original file.  
**Tests observed:** Valid JPEG/PNG/WebP, malformed data, MIME mismatch, unsupported declared content type, excessive dimensions and pixel count, trailing bytes, path-shaped client filename and >8 MiB upload.  
**Gaps:** No explicit animated-image regression test is present in the inspected test file; no measurement/stress test demonstrates peak process memory or processing-time bounds; cleanup on all failure paths and unauthorized upload side effects need additional integration coverage. A decode/pixel ceiling is not a malware-scanning control.  
**Status:** Blocked on current build/test; residual test gaps remain after it passes.

### S51-06 — Code-scanning results are artifacts, not GitHub Code Scanning alerts

**Severity:** Medium (visibility/triage gap)  
**Evidence:** `.github/workflows/security.yml` configures `github/codeql-action/analyze@v4` with `upload: false` and the job uses `security-events: read`. SARIF output is retained as Actions artifacts. The successful web/AI analysis artifact on run 44549757071 is artifact `14542142200`; it is not an uploaded alert set in GitHub Code Scanning.  
**Configuration scan:** Trivy configuration scanning is set to `exit-code: "0"`, intentionally preserving baseline findings for reviewer triage. Its job success is not a claim that configuration findings are absent or cleared.  
**Status:** Partial. Enable a reviewed upload/triage policy or clearly keep artifact-only reporting as an accepted limitation; do not report artifact storage as GitHub alert triage.

### S51-07 — Dependency reproducibility

**Severity:** Medium (supply-chain risk)  
**Evidence:** npm dependency audit runs `npm install`, and the inspected branch does not include a committed npm lockfile. Python runtime requirements are ranges rather than a full hash-locked environment. Dependabot is configured weekly for Actions, npm, pip, NuGet and Docker.  
**Latest completed evidence on pre-fix head:** npm reported 0 vulnerabilities; pip-audit reported no known vulnerabilities; the NuGet high/critical check completed successfully.  
**Status:** Partial. Audit output is a time-specific advisory check, not deterministic resolution or an SBOM. Commit reproducible lockfiles with a deliberate update process.

### S51-08 — Operational security residuals

**Severity:** Medium  
- Rate limiting is process-local; it is not coordinated across multiple API replicas.
- Receipt storage is a private local volume; there is no verified malware-scanning workflow, object-store lifecycle/retention or deletion automation.
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

### Latest completed baseline-head evidence

[TravelOS CI — run 44549756999](https://github.com/harini281/travel_os/actions/runs/44549756999) on `c86b427` — **FAIL**:
- PASS: AI API tests.
- PASS: Web typecheck/build.
- PASS: PostgreSQL migration validation.
- FAIL: Platform API build — `SKException` CS0246.
- FAIL: Platform API unit tests — compilation blocked by CS0246.
- FAIL: Platform API PostgreSQL tests — compilation blocked by CS0246.
- FAIL: container deployment job — application image build blocked by CS0246; runtime/smoke assertions were skipped.
- FAIL: browser E2E job — Platform API did not start; browser journey step was skipped. This is not an E2E test failure from browser assertions, nor a pass.

[TravelOS Security Assessment — run 44549757071](https://github.com/harini281/travel_os/actions/runs/44549757071) on `c86b427` — **FAIL**:
- PASS: CodeQL Web/AI.
- PASS: Trivy filesystem/secret and configuration job; configuration findings are report-only (exit code 0), so review artifact findings before accepting.
- PASS: npm, Python and NuGet dependency audit job.
- FAIL: CodeQL Platform API build — CS0246; no source analysis SARIF was generated for the .NET job.
- FAIL: container image security job — Platform API build failed; Platform, AI and Web image scans did not all run. Mark downstream image scans skipped, not passed.

Artifacts from that security run:
- [Web/AI CodeQL SARIF artifact 14542142200](https://github.com/harini281/travel_os/actions/runs/44549757071)
- [Trivy filesystem/config artifact 14542790988](https://github.com/harini281/travel_os/actions/runs/44549757071)

### New remediation-head runs — not yet validated at report time

- [TravelOS CI — run 44551269802](https://github.com/harini281/travel_os/actions/runs/44551269802) on `c2fc271` — in progress/awaiting result.
- [TravelOS Security Assessment — run 44551270049](https://github.com/harini281/travel_os/actions/runs/44551270049) on `c2fc271` — queued/in progress/awaiting result.

Do not infer completion from run creation. Update this report with final job-level outcomes before changing any FAIL/BLOCKED status to PASS.

### Failed / skipped / unavailable distinctions

- The Platform API CodeQL scan on `c86b427` failed at compilation; its analyzer stage was not executed.
- The container scanner job on `c86b427` failed during Platform API image build; subsequent image scans were skipped.
- Browser journey tests were skipped because the API failed startup; no browser pass is evidenced on that head.
- Trivy configuration scan is configured report-only; a successful step is not a clean configuration result.
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
