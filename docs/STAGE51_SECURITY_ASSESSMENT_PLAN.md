# TravelOS Stage 51 — Security Assessment Plan

**Stage:** 51 — Security Testing, Vulnerability Assessment and Penetration-Test Preparation  
**Base branch:** `stage50-production-readiness`  
**Base commit:** `071e737db16ac5dce2f23180bf981b818f4f2d03`  
**Working branch:** `stage51-security-testing`  
**Scope:** repository-owned source, disposable CI/test services and local test fixtures only.

## 1. Purpose and safety boundaries

Stage 51 assesses security controls in the existing TravelOS application, adds safe regression coverage and remediates confirmed code-level gaps. It is not an authorization to test public production systems, external providers, third-party infrastructure or accounts that TravelOS does not own.

All active tests will use local application code, fake providers, generated test identities and disposable PostgreSQL containers. Inputs will be bounded and non-destructive. No denial-of-service, destructive SQL, credential guessing against real accounts, filesystem traversal against real hosts, cloud metadata probing, external scanning or production traffic will be performed.

Automated static analysis, dependency scanning and regression tests are evidence of code-level checks only. They do not constitute a professional penetration test, independent security audit, certification or production approval.

## 2. Components and trust boundaries to review

| Area | In-scope review |
|---|---|
| React/Vite web client | Output encoding, untrusted text rendering, browser storage/token handling, external links, CSP compatibility and dependency supply chain |
| ASP.NET Core Platform API | JWT validation, route authorization, ownership/member checks, request validation, error disclosure, rate limits and security headers |
| FastAPI AI API | Request schema enforcement, internal service boundary, receipt payload validation, provider error handling and untrusted AI output |
| PostgreSQL | EF Core query parameterization, migration SQL, runtime-role grants, ownership relations and CI test database isolation |
| Receipts | Multipart limits, type/signature mismatch, full image decoding, image dimensions, generated storage names, retrieval permissions and AI forwarding |
| External integrations | Fixed provider hosts, URL construction/encoding, timeouts, response validation and secret handling |
| Docker/Compose | Network exposure, service credentials, non-root containers, build inputs, mutable image references and configuration |
| GitHub Actions | Least-privilege permissions, code analysis, dependency audits, secret scanning and container/configuration scanning |

## 3. Stage 49/50 controls already present — do not duplicate

- Stage 49 validates production JWT settings and exact HTTPS CORS origins, sets API/browser security headers, and separates frontend/backend/internal data networks.
- Stage 50 separates the PostgreSQL migration administrator from the non-superuser application database account, validates production database and live AI provider settings, provides encrypted backup/integrity tools, rotates local container logs and documents release gates.
- Existing per-process rate-limit policies cover authentication, receipts, routing, opening-hours, flight refresh, weather and AI generation.
- API routes reviewed use ASP.NET Core authorization plus trip/member/owner/creator/recipient checks; EF Core queries generally scope records by trip and ID.
- The AI service is on the backend Compose network and not published by the production Compose stack.
- Receipt storage is outside the web root and uses generated server-side filenames.

Stage 51 will add test depth and missing defense-in-depth; it will not reimplement Stage 49/50 controls.

## 4. Initial findings and risk ranking

These are static-review observations, not proof of exploitability. Severity may change after regression testing.

| ID | Area | Initial observation | Risk | Planned action |
|---|---|---|---|---|
| S51-01 | Receipt upload | MIME allowlist and magic-byte prefixes exist, but the uploaded payload is not fully decoded as an image or checked against a pixel/dimension budget before storage and AI forwarding. Multipart parsing also occurs before handler-level size validation. | Medium | Enforce request/form limits, identify and decode allowed image formats with a maximum pixel count, verify the detected format, and test malformed/oversized inputs do not persist files or call AI. |
| S51-02 | AI service boundary | Planner and receipt extraction HTTP routes do not authenticate the calling Platform API. Compose network separation reduces reachability, but any workload admitted to the backend network can invoke them. | Medium (defence-in-depth) | Add a server-only shared service credential, constant-time verification, fail-closed configuration and unauthorized/authorized tests. Keep health checks public. |
| S51-03 | Security automation | The workflow builds/tests application components but does not run dedicated CodeQL, dependency-vulnerability, secret or container/configuration scans. | Medium | Add a separate least-privilege security workflow and Dependabot update configuration. |
| S51-04 | Dependency reproducibility | Web dependencies use floating `latest` declarations and no committed lockfile was found; Python dependencies use version ranges. | Medium (supply-chain exposure) | Add automated audits and update policy. Do not claim that an audit scan pins dependencies; lockfile adoption remains an explicit follow-up if generated reproducibly. |
| S51-05 | Authentication regression coverage | Production JWT validation is configured for signature, issuer, audience and lifetime, but the reviewed automated test set does not comprehensively exercise malformed, wrong-signature, wrong-issuer/audience, expired and not-yet-valid tokens. | Low (coverage gap) | Add a token validation matrix using test-only signed tokens. Preserve existing policy behavior. |
| S51-06 | Resource access regression coverage | Endpoint review shows owner/member/creator/recipient checks, but the test set is concentrated in itinerary tests and does not provide a broad cross-user/IDOR regression matrix. | Low (coverage gap) | Add targeted PostgreSQL integration tests for trip, expense, repayment and receipt access using multiple generated test identities. |
| S51-07 | Multi-instance rate limiting | Fixed-window limits are process-local, as already documented in Stage 50. | Medium operational residual | Add to findings/limitations; do not pretend a code-level single-node test proves distributed enforcement. Distributed limiter requires an infrastructure decision. |
| S51-08 | Runtime support lifecycle | The Platform API targets .NET 8, which reached end of support on 10 November 2026. Unsupported runtime versions no longer receive Microsoft security servicing. | High release blocker | Record as a release blocker; recommend a separately tested migration to supported .NET 10 LTS. |

No confirmed SQL injection, request-controlled external URL/SSRF path, or authorization bypass has been established during the initial static review. These remain test targets, not asserted vulnerabilities.

## 5. Assessment and regression test matrix

### JWT and authentication

- Missing Authorization header and malformed bearer scheme.
- Empty, malformed and tampered JWT.
- Correct signature with wrong signing key.
- Wrong issuer and wrong audience.
- Expired token and future `nbf` token.
- Valid token with correct test issuer/audience/key.
- Verify the configured clock-skew boundary.
- Verify a token without a valid user subject cannot access user-owned resources.

### Authorization and IDOR

For each endpoint family under test, create owner, current member, invited-but-not-accepted user and unrelated user identities in disposable PostgreSQL:

- Trip read/update/delete; owner-only mutation.
- Itinerary read versus owner-only mutation and approval.
- Expense list/create; creator/owner edit and delete; non-member denied.
- Repayment creation and recipient-only response; sender/other member cannot confirm.
- Receipt list/file/confirm/delete; member visibility, uploader/owner mutation rights and unrelated user denial.
- Budget owner-write/member-read.
- Invitation acceptance bound to the invited account.
- Preference read/write scoped to the current member.
- Notification reads scoped to the authenticated user.

Tests must assert both HTTP response and absence of unauthorized database/file side effects. Do not change a permission rule unless an actual counterexample is proven and regression tests establish the intended contract.

### Input and API handling

- Empty, over-limit and structurally invalid request fields.
- Unexpected JSON properties where schemas are strict.
- SQL metacharacters as ordinary text; verify they remain data and never change query structure.
- HTML/script-looking values as persisted text; verify the React view renders text, not executable markup.
- Malformed upstream JSON, timeouts and non-success provider responses through fake clients.
- Verify external provider hostnames remain fixed and user-supplied search text is URI-encoded.
- Verify auth/rate-limit status behavior without generating load.

### Receipt upload

- Valid small JPEG, PNG and WebP fixtures.
- Mismatch between declared MIME and actual encoded format.
- Truncated header, malformed image body and unsupported format.
- Zero-byte and above-limit body/file inputs.
- Oversized dimensions/pixel count in a small compressed file.
- Filename containing path separators, control characters or HTML-looking text.
- Unauthenticated and non-member upload/read attempts.
- File/DB cleanup behavior when persistence fails.
- Verify rejected files are not written and are not forwarded to the AI extraction service.

### Infrastructure and automation

- Production Compose config still exposes only its intended web ingress.
- Internal AI routes reject missing/incorrect service credentials.
- Health checks continue working without application service credentials.
- Dependency audits cover npm, Python and NuGet dependencies.
- Static CodeQL covers C#, JavaScript/TypeScript and Python.
- Secret scanning and Docker/configuration vulnerability scans run with read-only repository permissions except code-scanning result upload.

## 6. Implementation sequence

1. Create this plan on `stage51-security-testing` based exactly on the Stage 50 commit.
2. Implement full receipt image validation and internal AI API authentication.
3. Add JWT, IDOR and upload security regression tests using generated identities, local files and fake upstream clients.
4. Add dedicated security automation (CodeQL, dependency review/audits, secret and container/configuration scanning) with narrow workflow permissions.
5. Write a final security assessment report documenting findings, fixes, test evidence, limitations and outstanding risks.
6. Run the full existing CI suite plus new security tests. Fix only demonstrated regressions.
7. Open a draft Stage 51 PR targeting `stage50-production-readiness`.

## 7. Out of scope / required independent work

- Production or internet-facing penetration testing.
- Cloud IAM, TLS ingress, DNS, firewall and managed-secret configuration.
- Distributed rate limiting, centralized monitoring and operational controls.
- Account lifecycle features (email verification, password reset, refresh/session management and MFA).
- Malware scanning service, object storage, receipt retention/deletion automation and privacy/legal approval.
- Formal compliance certification, accessibility certification or security sign-off.

These require separate authorization, infrastructure decisions or independent specialist review.
