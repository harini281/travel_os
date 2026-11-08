# API Conventions

- Prefix public APIs with `/api/v1`.
- Use JSON and camelCase property names.
- Use UUID identifiers.
- Return structured errors with `code`, `message`, and optional `details`.
- Validate requests at the API boundary and again for domain invariants.
- Use pagination for collection endpoints.
- Require authorization for trip-specific data.
- Use idempotency keys for future payment/booking actions.
- Version shared contracts; do not silently break existing clients.

Stage 1 health endpoints:
- Platform API: `GET /health`
- AI API: `GET /health`

The AI service's planning endpoint is explicitly demo-only until a model provider is configured and tested.
