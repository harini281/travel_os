# TravelOS — Stage 51 Security Assessment

**Assessment date:** 20 November 2026  
**Repository:** https://github.com/harini281/travel_os  
**Assessment branch:** `stage51-security-testing`  
**Verified Stage 51 commit:** `193e1ac351a4f4cc0e911affe42f1d86f02f3474`  
**Baseline:** `stage50-production-readiness`  
**Stage 51 PR:** [#53 — merged](https://github.com/harini281/travel_os/pull/53)

> **Repository verification: PASS WITH LIMITATIONS. Public production approval: BLOCKED.** Stage 51 code and automated workflows passed the recorded CI/security runs. This is not an independent penetration test, security certification, exhaustive route audit or production sign-off.

## 1. Executive result

GitHub state was checked directly on 20 November 2026:
- Branch `stage51-security-testing` exists at `193e1ac351a4f4cc0e911affe42f1d86f02f3474`.
- PR #53 was **merged**, not open or draft. Its merge commit is `d85affc134e23a8ddc314013a43f51afa2a9a02f`.
- Comparing `stage50-production-readiness` to `stage51-security-testing` now reports the base one merge commit ahead and zero commits ahead, because that PR has already been merged. The PR's original recorded base SHA was `071e737db16ac5dce2f23180bf981b818f4f2d03`.
- PR #54 (Stage 52) and #55 (Stage 53) are open drafts stacked on top of the Stage 51 branch history. They were not changed here.
- No force-push or history rewrite was performed.
- The requested "update the existing Stage 51 draft PR" could not be carried out because PR #53 is already merged. Do not reopen or revert it merely to reproduce the earlier requested state.

## 2. Exact GitHub Actions evidence

Both current workflow runs are tied to Stage 51 commit `193e1ac351a4f4cc0e911affe42f1d86f02f3474` and completed successfully on 20 November 2026:

- [TravelOS CI — run 44678346752](https://github.com/harini281/travel_os/actions/runs/44678346752): **8/8 jobs succeeded**.
- [TravelOS Security Assessment — run 44678346761](https://github.com/harini281/travel_os/actions/runs/44678346761): **5/5 jobs succeeded**.

### CI jobs — 8/8

1. Platform API build — success.
2. Platform API unit tests — success; the workflow job passed. Prior exact-head documentation records 49 passed, 0 failed and 27 PostgreSQL-dependent skips in the unit-only run.
3. PostgreSQL API integration tests — success.
4. Container deployment build and smoke test — success.
5. Cross-browser, mobile and accessibility E2E tests — success.
6. PostgreSQL migration validation — success.
7. AI API tests — success.
8. Web typecheck and build — success.

The previous detailed validation record reports 80 PostgreSQL-backed tests passed and 16 browser E2E journeys passed on the same Stage 51 commit. Job summaries establish successful completion; exact numerical test totals are retained as the recorded test output rather than inferred from job status alone. The AI suite had a previously reported Starlette/httpx deprecation warning.

### Security jobs — 5/5

1. CodeQL — Platform API — success; analyze/upload step succeeded.
2. CodeQL — Web and AI — success; analyze/upload step succeeded.
3. NuGet, npm and Python dependency audit — success.
4. Filesystem, secret and configuration scan — success.
5. Container image vulnerability scans — success for Platform API, AI API and web images.

**Important configuration-scan qualification:** `.github/workflows/security.yml` sets the filesystem/secret scan to `exit-code: "1"`, but the Trivy configuration scan is explicitly configured as `exit-code: "0"`. Therefore its job passing proves the scan step completed, not that every HIGH/CRITICAL configuration finding would fail CI. Treat this as a policy-enforcement gap until reviewed findings and a blocking gate are added. Do not describe this configuration scan as a clean blocking gate without inspecting its SARIF.

GitHub run jobs and step summaries were retrieved for both runs. No jobs in these two latest runs were failed or skipped. The 27 unit-only skips are a separate test-run detail, not skipped workflow jobs; PostgreSQL tests have their own successful integration job.

## 3. Security category results

| Category | Result | Evidence / qualification |
|---|---|---|
| Internal AI API authentication | **PASS — source and test coverage** | All three internal POST endpoints use the shared dependency. Missing, wrong and malformed-present token cases are represented in AI API tests. |
| Constant-time comparison | **PASS** | Uses Python `hmac.compare_digest` on UTF-8 bytes. |
| Platform-to-AI token delivery | **PASS — source inspection** | The Platform API client helper sets `X-TravelOS-Service-Token`; draft, recommendation and receipt clients call it. |
| Fail-closed service configuration | **PASS — source/test coverage** | AI service returns 503 when server token is missing; production Platform API validation requires a high-entropy 32+ character token distinct from JWT signing key. |
| Secret/log exposure | **PARTIAL** | Generic auth errors and no direct token logging observed in reviewed code. This is not a complete runtime log/telemetry assurance review. |
| Public AI health endpoint | **PASS** | `GET /health` returns minimal `{"status":"ok"}`; workflow verifies exact response shape. |
| Receipt image validation | **PASS for implemented bounds; PARTIAL for adversarial/performance coverage** | JPEG/PNG/WebP allow-list, decoded-format match, full decode, dimension/pixel/decoded-byte limits, multi-frame rejection and JPEG normalization are implemented. |
| Receipt metadata stripping | **PASS — regression test present** | JPEG re-encoding test checks EXIF APP1 marker removal; normalized output is JPEG. |
| Receipt storage/path safety | **PASS — source and tested cases** | GUID-generated storage name is used; client filename is reduced to a safe display name. |
| Receipt authorization | **PASS for tested cases** | Trip membership gates file access; upload/confirmation/deletion rights enforce membership and uploader/owner checks. |
| Trip/itinerary/expense/group IDOR coverage | **PARTIAL** | Multi-user PostgreSQL tests exercise important owner/member/invitee/outsider boundaries. Not every endpoint, nested resource ID mismatch or HTTP verb is exhaustively covered. |
| CodeQL | **PASS** | Two CodeQL jobs succeeded on the current Stage 51 commit. |
| Dependency scanning | **PASS at scan time** | NuGet/npm/Python audit job succeeded. Existing lack of npm lockfile and ranged Python requirements remains a reproducibility risk. |
| Secret/filesystem scanning | **PASS at scan time** | Trivy filesystem and secret scan job succeeded. |
| Trivy configuration policy | **PARTIAL** | Scan runs, but `exit-code: 0` means findings do not fail the job. Review retained report before release. |
| Trivy image scans | **PASS at scan time** | All three image build/scan steps succeeded with HIGH/CRITICAL gate. This is not a guarantee about later rebuilds or deployment digests. |
| .NET runtime support | **FAIL — production blocker** | Stage 51 project/tests, Docker images and workflows target .NET 8. .NET 8 ended support 10 November 2026. |
| Independent penetration testing | **NOT PERFORMED** | No independent DAST or authorized specialist staging assessment evidence. |
| Production readiness | **BLOCKED** | Unsupported runtime, branch-protection state unavailable to this connector, incomplete operational controls and no independent assessment. |

## 4. Findings and remediation

### S51-01 — Internal AI API was not authenticated
**Severity:** Medium defence-in-depth finding.  
**Remediation:** Shared server-only `AI_SERVICE_TOKEN`, dependency on all internal POST routes, constant-time comparison and fail-closed behavior implemented. Platform client adds the service header.  
**Residual:** No complete runtime log assurance; no production ingress review.

### S51-02 — Receipt payload was not fully decoded before storage
**Severity:** Medium.  
**Remediation:** Receipt processor checks actual encoded format, decodes supported single-frame JPEG/PNG/WebP, bounds dimensions to 8,000 per edge, pixels to 16 million and decoded bytes to 128 MiB, then re-encodes to JPEG quality 85. Upload limit is 8 MiB and route request size is bounded. Re-encoding removes source EXIF and trailing payload.  
**Residual:** No measured peak-memory/processing-time stress run or malware scanner. A dedicated animated-image test fixture is not evidenced, although the implementation rejects codecs reporting additional frames. Receipt deletion saves the DB deletion before attempting file deletion and swallows `IOException`; failed file deletion can leave an orphaned object. Add retryable reconciliation/retention control.

### S51-03 — Cross-user resource access
**Severity:** Medium coverage limitation; no confirmed exploitable IDOR established by this review.  
**Remediation/evidence:** PostgreSQL integration tests cover trip owner/member/outsider access, expense editing/history, recipient-only repayment response, owner-managed invitations/invitee-only acceptance, preferences, polls/votes and receipt file/confirm/delete access. Unauthorized mutation checks are present for selected cases.  
**Residual:** Not an exhaustive verb-by-verb or parent/child ID mismatch matrix for every trip, itinerary, recommendation, receipt, expense and group endpoint. Broaden route coverage before production.

### S51-04 — Security automation
**Severity:** Medium policy gap.  
**Remediation/evidence:** CodeQL, NuGet/npm/pip audit, Trivy filesystem/secret/configuration and three image scans are configured. Dependabot is weekly for GitHub Actions, npm, pip, NuGet and Docker.  
**Residual:** Trivy configuration scan uses `exit-code: 0`; make it blocking after triage. Dependency audit success is time-specific and is not an SBOM or reproducible lockfile.

### S51-05 — Dependency reproducibility
**Severity:** Medium.  
**Evidence:** The web project has used floating `latest` dependency declarations, CI uses `npm install`, and a committed lockfile was not evidenced in the reviewed Stage 51 files. Python dependency ranges remain.  
**Residual/remediation:** Commit and review a package lockfile, change CI to `npm ci`, pin Python runtime dependencies with hashes/lock tooling, and generate an SBOM for release artifacts.

### S51-06 — Runtime support lifecycle
**Severity:** High, production blocker.  
**Evidence:** Platform and test projects target `net8.0`; the Platform Dockerfile uses .NET 8 SDK/runtime images; CI/security workflows install SDK 8.0.x. Microsoft lists .NET 8 support ending 10 November 2026 (local user-facing date conversion).  
**Remediation:** Stage 52 branch `stage52-dotnet10-migration` and draft PR #54 represent a separate migration path. Do not treat Stage 51 as upgraded. Validate all supported .NET 10 packages, tests, images and deployment on that follow-up branch before promotion.

### S51-07 — Operational residuals
**Severity:** Medium to High depending on deployment topology and data policy.
- Persist and protect ASP.NET Core Data Protection keys across container replacement/replicas.
- Establish receipt retention/deletion and storage/database reconciliation.
- Decide on malware scanning under the threat model.
- Use distributed rate limiting when API replicas exceed one.
- Validate secrets manager, TLS ingress, cloud IAM/network policy, monitoring/alerts and backups/restore in the target host.
- Enable required branch protection/reviews/checks. This connection could not read the protected branch settings because GitHub returned 403; current branch-protection state is therefore **unverified here**, not independently confirmed disabled.

## 5. Image security details

Source review of `ReceiptImageProcessor` found:
- Signature and declared content type are checked by the endpoint.
- Skia codec format must match the declared JPEG/PNG/WebP type.
- `FrameCount > 0` rejects animations/multi-frame inputs.
- Width and height must be positive and each no greater than 8,000.
- Product of dimensions cannot exceed 16,000,000 pixels.
- BGRA decoded allocation must be positive and <=128 MiB.
- Re-encoding creates new JPEG bytes; stored and AI-forwarded content is the normalized output.
- Stored filename uses a generated GUID and fixed extension.
- The supplied filename is only stored as sanitized display metadata.

Test source covers valid JPEG/PNG/WebP normalization, malformed payloads, MIME mismatch, unsupported declared types, over-dimension and over-pixel fixtures, trailing payload discard and EXIF APP1 marker removal.

Not independently measured: worst-case decoder CPU/memory under concurrency, garbage-collection behavior, temp-file cleanup for every failure path, scanner-based malware detection.

## 6. .NET 10 assessment and migration plan

Stage 51 itself remains on .NET 8. The repository's separate Stage 52 draft migration is an appropriate isolated workstream; PR #54 targets Stage 51 and is still draft.

Migration checklist:
1. Update Platform API and all test projects to `net10.0`.
2. Move ASP.NET Core/JWT/Identity/EF Core/TestHost dependencies to compatible serviced 10.x versions.
3. Use an Npgsql EF Core provider version compatible with EF Core 10; verify all transitive dependencies and migrations.
4. Recheck SkiaSharp native assets and receipt codec behavior on the selected Linux runtime image.
5. Move Docker build/runtime images to .NET 10 SDK/ASP.NET images.
6. Move setup-dotnet in CI and security workflows to SDK 10; check CodeQL manual build and dependency-audit behavior.
7. Search workflow scripts, deployment docs, local setup and release automation for stale .NET 8 references.
8. Run build, unit, disposable PostgreSQL integration, migrations, Docker smoke, browser E2E, CodeQL, NuGet, Trivy image/config and SBOM generation on one exact commit.
9. Deploy only to isolated staging; rehearse backup/restore, secrets, Data Protection, TLS and IAM; obtain independent review.

**Stage 51 compatibility test result:** NOT RUN. Do not infer .NET 10 compatibility from Stage 51's .NET 8 CI.

## 7. GitHub automation and history

- Current Stage 51 CI: [run 44678346752](https://github.com/harini281/travel_os/actions/runs/44678346752), 8/8 jobs successful.
- Current Stage 51 Security: [run 44678346761](https://github.com/harini281/travel_os/actions/runs/44678346761), 5/5 jobs successful.
- Historical Stage 51 exact-head run references are retained in the merged PR description; the above newer run IDs were verified directly and are the latest evidence retrieved for the Stage 51 SHA.
- No failed/skipped jobs were found in the two current workflow job lists.
- Unit-only test run included 27 database-dependent skips; PostgreSQL integration job succeeded separately.
- A previous AI test run recorded one Starlette/httpx deprecation warning.
- Earlier reported scanner/build failures were corrected in prior commits, including SkiaSharp compile failure, vulnerable AI image packages, web base image findings and smoke-test expectations.
- Branch protection endpoint returned HTTP 403 for this connector. It is unavailable to this assessment and requires an owner/admin check in GitHub settings.
- PR #53 is already merged. The merge must not be undone as part of this verification. PR #54 and #55 remain separate drafts; do not merge them here.

## 8. Production blockers

1. Complete/approve .NET 10 LTS migration and verify the resulting release candidate.
2. Confirm and enforce branch protection, required reviewers and CI/security checks in GitHub settings.
3. Make Trivy configuration findings blocking after SARIF triage.
4. Resolve npm lockfile/reproducibility and Python dependency pinning; produce SBOMs.
5. Configure persistent protected Data Protection keys.
6. Implement receipt retention/deletion and orphan-file reconciliation; decide on malware scanning.
7. Configure and verify target-host TLS, ingress, cloud IAM, network boundaries, secrets manager, logging/alerts, backup retention and isolated restore drill.
8. Use distributed rate limiting if horizontally scaled.
9. Complete an independently scoped staging security assessment/penetration test with written authorization and findings closure.

## 9. Conclusion

**Stage 51 repository verification: PASS WITH LIMITATIONS.** The two current Stage 51 workflow runs succeeded: 8 CI jobs and 5 security jobs. Source review supports the internal AI authentication implementation and the image processing controls described above. Authorization integration coverage is meaningful but not exhaustive.

**Public production approval: BLOCKED.** The .NET 8 target is out of support, branch protection could not be verified through this connection, Trivy configuration findings are not blocking, reproducible dependency resolution is incomplete, operational storage/key-management controls require deployment decisions, and independent staging testing has not been performed.

This assessment does not state that TravelOS is fully secure and does not claim an independent penetration test.
