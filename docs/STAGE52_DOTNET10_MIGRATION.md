# Stage 52 — .NET 10 LTS migration

**Branch:** `stage52-dotnet10-migration`  
**Base:** `stage51-security-testing`  
**Purpose:** Move the Platform API and its test/build/deployment pipeline from unsupported .NET 8 to supported .NET 10 LTS without altering the completed Stage 51 security branch.

## Scope

- Target the Platform API and test project at `net10.0`.
- Align ASP.NET Core/JWT/Identity and EF Core packages on the .NET 10 servicing line.
- Use the Npgsql EF Core 10 provider compatible with EF Core 10.
- Move the Platform API container to .NET 10 SDK/runtime images.
- Move CI and security workflows to the .NET 10 SDK.
- Move maintained GitHub Actions to Node 24-capable major versions.
- Update developer prerequisites, stack information and deployment/release documentation.
- Preserve PostgreSQL schema and migration history; this upgrade does not rewrite applied SQL migrations.

## Selected versions

- .NET SDK/runtime line: `10.0.x`.
- ASP.NET Core JWT bearer and Identity EF Core: `10.0.15`.
- EF Core Design: `10.0.15`.
- Npgsql EF Core provider: `10.0.3` (targets .NET 10; requires EF Core 10.0.4 or later and below 11).
- ASP.NET Core test host: `10.0.15`.
- Microsoft.NET.Test.Sdk: `18.12.0`.
- Npgsql health check package: `9.0.0`.
- SkiaSharp and Linux native assets remain at `3.119.4`; API unit/integration, Docker smoke and browser receipt journeys passed on the .NET 10 runtime.
- Actions: checkout v7, setup-dotnet v6, setup-node v7, setup-python v7 and upload-artifact v6.

Versions were selected from current NuGet and GitHub release metadata as of 20 November 2026. See the NuGet pages for [JWT bearer 10.0.15](https://www.nuget.org/packages/Microsoft.AspNetCore.Authentication.JwtBearer/10.0.15), [Identity EF Core 10.0.15](https://www.nuget.org/packages/Microsoft.AspNetCore.Identity.EntityFrameworkCore/10.0.15), [EF Core Design 10.0.15](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.Design/10.0.15), [Npgsql provider 10.0.3](https://www.nuget.org/packages/Npgsql.EntityFrameworkCore.PostgreSQL/10.0.3), [MVC Testing 10.0.15](https://www.nuget.org/packages/Microsoft.AspNetCore.Mvc.Testing/10.0.15) and [Test SDK 18.12.0](https://www.nuget.org/packages/Microsoft.NET.Test.Sdk/18.12.0).

## Validation result — PASS on source commit `09655a1c06175b012996bc3a29554fced9b701e8`

[TravelOS CI run 44678743033](https://github.com/harini281/travel_os/actions/runs/44678743033) — **PASS, all 8 jobs**:
- Platform API build — succeeded on .NET 10.
- Unit tests — **49 passed, 0 failed, 27 skipped** (PostgreSQL-only tests run in the separate integration job).
- PostgreSQL integration tests — **80 passed, 0 failed**.
- AI API tests — **48 passed, 1 existing Starlette/httpx deprecation warning**.
- Web typecheck/build — passed.
- PostgreSQL migrations — passed.
- Docker deployment smoke — passed: all images built, stack started, web/API/AI health checks passed, all 18 migrations were recorded, and API/database network isolation passed. The API container reports `Microsoft.NETCore.App 10.0.15`.
- Cross-browser/mobile/tablet E2E — **16 passed, 0 failed** across Chromium, Firefox, WebKit, mobile Chromium, mobile WebKit and tablet Chromium. Receipt upload/confirmation/duplicate checks passed with the .NET 10 API.

[TravelOS Security Assessment run 44678743097](https://github.com/harini281/travel_os/actions/runs/44678743097) — **PASS, all 5 jobs**:
- CodeQL Platform API — passed; SARIF uploaded to GitHub Code Scanning.
- CodeQL Web and AI — passed; SARIF uploaded to GitHub Code Scanning.
- NuGet/npm/Python dependency audit — passed; npm reported 0 vulnerabilities, pip-audit reported no known vulnerabilities, NuGet high/critical gate passed.
- Filesystem/secret/config scan — passed.
- Trivy `v0.76.2` image scans — Platform API, AI API and web images all passed the HIGH/CRITICAL gate. The scanned runtime packages included .NET 10.0.15; no HIGH/CRITICAL image findings were reported.

**Warnings that remain:** EF Core reports multiple-collection-include/query-splitting warnings on selected queries; AI tests report a Starlette/httpx deprecation; web build reports a chunk-size warning; Docker build logs show pip running as root during build stages. These did not fail CI/security gates and should be considered follow-up cleanup.

## Production and target-host status

The repository-level .NET 10 migration is verified, but that is not public-production approval. Still outstanding:
- Run the app in the selected target host/environment and verify its .NET 10 runtime, configuration, TLS ingress, secret injection, scaling and observability.
- Enable branch protection and require reviewed changes plus CI/security checks.
- Conduct independent authorized staging security testing.
- Configure persistent ASP.NET Core Data Protection keys, backup/restore, receipt retention/deletion reconciliation and malware scanning according to the deployment threat model.
- Review distributed rate limiting before scaling to multiple API replicas.

No SQL migration was added or edited by this runtime upgrade; the 18 existing migrations applied successfully in disposable CI databases.

## Compatibility risks

- ASP.NET Core hosting, authentication and JWT behavior.
- EF Core 10 query translation and Npgsql provider alignment, particularly PostgreSQL JSON/array types, query splitting and transaction semantics.
- Native SkiaSharp Linux libraries in the .NET 10 runtime image.
- Database migration history must remain forward-only; this runtime migration must not mutate applied SQL migrations.
- Data Protection persistence, key rotation and external deployment settings remain operational requirements.

## Status

**CODE MIGRATION AND REPOSITORY VALIDATION: PASS** on source commit `09655a1c06175b012996bc3a29554fced9b701e8`. All 8 CI jobs and all 5 security jobs passed, including 80 PostgreSQL tests, 16 E2E journeys, runtime smoke on .NET 10.0.15, CodeQL uploads and all three Trivy image scans. Target-host runtime verification, branch protection, independent staging assessment and the remaining production gates are still required.
