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
- SkiaSharp and Linux native assets remain at `3.119.4`, pending native runtime verification.
- Actions: checkout v7, setup-dotnet v6, setup-node v7, setup-python v7 and upload-artifact v6.

Versions were selected from current NuGet and GitHub release metadata as of 20 November 2026. See the NuGet pages for [JWT bearer 10.0.15](https://www.nuget.org/packages/Microsoft.AspNetCore.Authentication.JwtBearer/10.0.15), [Identity EF Core 10.0.15](https://www.nuget.org/packages/Microsoft.AspNetCore.Identity.EntityFrameworkCore/10.0.15), [EF Core Design 10.0.15](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.Design/10.0.15), [Npgsql provider 10.0.3](https://www.nuget.org/packages/Npgsql.EntityFrameworkCore.PostgreSQL/10.0.3), [MVC Testing 10.0.15](https://www.nuget.org/packages/Microsoft.AspNetCore.Mvc.Testing/10.0.15) and [Test SDK 18.12.0](https://www.nuget.org/packages/Microsoft.NET.Test.Sdk/18.12.0).

## Required validation

The migration is not complete until the same branch head passes:

1. .NET 10 restore and Platform API build.
2. Platform API unit tests.
3. Full PostgreSQL-backed integration suite and all existing migrations.
4. AI API tests and web typecheck/build.
5. Cross-browser, mobile and tablet E2E suite.
6. Docker build and deployment smoke test, including receipt image processing with native SkiaSharp libraries.
7. CodeQL, NuGet/npm/Python dependency audit, filesystem/secret/config scan and Trivy image scans.
8. Review compiler/runtime warnings and verify final container runtime/base image versions.

## Compatibility risks

- ASP.NET Core hosting, authentication and JWT behavior.
- EF Core 10 query translation and Npgsql provider alignment, particularly PostgreSQL JSON/array types, query splitting and transaction semantics.
- Native SkiaSharp Linux libraries in the .NET 10 runtime image.
- Database migration history must remain forward-only; this runtime migration must not mutate applied SQL migrations.
- Data Protection persistence, key rotation and external deployment settings remain operational requirements.

## Status

**IN PROGRESS — implementation pushed; CI/security validation pending.** Do not close the .NET 8 release blocker until the exact Stage 52 branch head passes the full matrix and target-host runtime verification is complete. Independent staging assessment, branch protection and the remaining production gates are still required.
