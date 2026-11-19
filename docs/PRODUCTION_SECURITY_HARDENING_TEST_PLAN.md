# TravelOS Stage 49 — Production security hardening

## Goal
Reduce avoidable production exposure before selecting a hosting provider. Stage 49 strengthens configuration validation, browser/API response headers and service-to-service network boundaries. It does not deploy publicly or replace a managed secret store, TLS ingress, or a security review.

## Production configuration guardrails
The Platform API now fails startup in Production unless:
- A JWT issuer and audience are explicitly configured.
- The JWT signing key is at least 32 bytes, is not a known placeholder/example value, and has sufficient character diversity to reject obvious sample secrets.
- At least one exact CORS origin is configured and every production origin uses HTTPS.
- CORS origins contain no wildcard, credentials, path, query or fragment.

Development and test environments retain the local Vite origin default. Do not use development defaults in production.

## Network segmentation
Compose now uses three network zones:
- `frontend`: web Nginx and Platform API.
- `backend`: Platform API and AI API only; isolated from external networks.
- `data`: Platform API, PostgreSQL, and migration runner only; isolated from external networks.

The web container cannot connect directly to the AI API or PostgreSQL. The Platform API is the only application bridge between the web, AI and database zones. PostgreSQL and application APIs remain unpublished to the host.

## Response hardening
The web proxy sets a restrictive Content Security Policy and additional browser security headers. Nginx also has request/connection timeouts and hides its version token. The Platform API adds API-appropriate content, framing, referrer, permissions, resource and no-store headers and disables the Kestrel Server header.

The CSP permits same-origin scripts, inline styles required by the current UI, HTTPS map/image connections, and data/blob images. Review and tighten it further if new external frontend integrations are added.

## CI validation
The container smoke job now checks:
1. Frontend CSP and nosniff headers.
2. API response security and no-store headers through the Nginx proxy.
3. No host-published ports on PostgreSQL, Platform API or AI API.
4. Web is on frontend but not backend.
5. AI API is on backend but not frontend.
6. Platform API bridges frontend and backend networks.
7. Production configuration validator unit tests run in the Platform API test job.

## Remaining production controls
Still required before a public launch:
- A managed secret store, secret rotation and least-privilege credentials.
- TLS ingress, DNS, firewall and trusted proxy configuration.
- Managed database, encrypted backups and tested restore drills.
- Container image scanning, SBOM, digest pinning and patch automation.
- Centralized logs, metrics, tracing, alerting and incident response.
- Distributed rate limiting and abuse protection across multiple API replicas.
- Email verification, password reset, refresh-token/session lifecycle and MFA.
- Privacy/data retention/data residency review and load testing.

Stage 49 is security hardening groundwork, not a security certification or production approval.
