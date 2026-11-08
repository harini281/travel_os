# Services

- `platform-api`: authoritative business API and persistence boundary.
- `ai-api`: isolated AI planning boundary; outputs are untrusted and demo-only in Stage 1.

The AI service must not connect directly to the platform database.
