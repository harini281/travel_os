# TravelOS

**The Intelligent Operating System for Travel**

A greenfield, global travel platform built with React, ASP.NET Core, PostgreSQL, and Python/FastAPI.

## Stage 2 scope

This stage adds a PostgreSQL bootstrap schema, EF Core entity relationships, database configuration, PostgreSQL health checks, and fixes discovered by the first CI run. It remains a foundation, not a production-ready application.

## Prerequisites

- Node.js 22+
- .NET 8 SDK
- Python 3.11+
- Docker Desktop / Docker Engine with Compose

## Repository layout

```text
apps/web                 React + TypeScript + Vite frontend
services/platform-api    ASP.NET Core platform API and EF Core model
services/ai-api          FastAPI AI service boundary
packages/contracts       Versioned JSON contracts
database/migrations      PostgreSQL bootstrap schema
infrastructure           Docker and deployment configuration
docs                     Product and architecture documentation
tests                    Cross-service integration test area
```

## Run locally

1. Copy `.env.example` to `.env`. The checked-in values are for local development only.
2. Start PostgreSQL:

   ```bash
   docker compose up -d postgres
   docker compose ps
   ```

   The SQL bootstrap script creates the initial tables when PostgreSQL initializes a **new empty data volume**.

3. Start the platform API in another terminal:

   ```bash
   cd services/platform-api
   dotnet restore
   dotnet run
   ```

   The API reads its connection string from `ConnectionStrings__TravelOS`. The Docker initialization also applies the ASP.NET Core Identity tables from `002_identity_schema.sql` when using a new empty volume.

   Configure the JWT issuer, audience, and a unique signing key before starting the API. The signing key must be at least 32 bytes. For local development, set these environment variables in your shell (the example below is a placeholder only; use a fresh random secret):

   ```powershell
   $env:Jwt__Issuer = "TravelOS"
   $env:Jwt__Audience = "TravelOS.Client"
   $env:Jwt__SigningKey = "replace-with-a-unique-random-secret-at-least-32-bytes"
   ```

   On macOS/Linux, use `export Jwt__Issuer=TravelOS`, `export Jwt__Audience=TravelOS.Client`, and `export Jwt__SigningKey='replace-with-a-unique-random-secret-at-least-32-bytes'`.

   The API reads its connection string from `ConnectionStrings__TravelOS`. For local development, set it in your shell or user secrets, for example:

   ```text
   Host=localhost;Port=5432;Database=travelos;Username=travelos;Password=change-me-local-only
   ```

4. Start the AI API:

   ```bash
   cd services/ai-api
   python -m venv .venv
   # Windows: .venv\Scripts\activate
   # macOS/Linux: source .venv/bin/activate
   pip install -r requirements.txt
   uvicorn app.main:app --reload --port 8001
   ```

5. Start the web app:

   ```bash
   cd apps/web
   npm install
   npm run dev
   ```

## Local endpoints

- Web: http://localhost:5173
- Platform API: http://localhost:8080
- Platform API health: http://localhost:8080/health
- AI API: http://localhost:8001
- AI API docs: http://localhost:8001/docs
- PostgreSQL: localhost:5432

## Authentication

- `POST /api/v1/auth/register` creates a traveller account and returns a short-lived JWT access token.
- `POST /api/v1/auth/login` validates credentials and returns a JWT access token.
- `GET /api/v1/auth/me` requires a valid bearer token and returns the current account.
- ASP.NET Core Identity manages password hashing, normalized email, role membership, failed-login lockout and password policy.
- The default `Traveller` role is assigned during registration. Never expose a public role-selection field.
- Access tokens expire after 15 minutes. Refresh tokens, email verification, password reset, MFA and social login are not implemented yet.
- Use HTTPS outside local development. Keep the JWT signing key in a secrets manager and rotate it deliberately.

## Database notes

- Bootstrap schema: `database/migrations/001_initial_schema.sql`.
- Identity schema extension: `database/migrations/002_identity_schema.sql`.
- Docker executes it only when initializing a new empty PostgreSQL volume.
- If you already have a volume from an earlier run, the script is not replayed automatically. Back up data before resetting volumes; do not run `docker compose down -v` if you need to keep existing data.
- The EF Core model is configured for PostgreSQL through Npgsql. A generated, versioned EF Core migration and migration deployment step are still outstanding.
- The API health endpoint includes PostgreSQL connectivity when a connection string is configured.

## Important boundaries

- PostgreSQL is the source of truth for platform data.
- The platform API owns authorization and persistence.
- The AI API produces untrusted suggestions; it does not write to the platform database.
- Generated itinerary drafts remain separate from approved itinerary records.
- No booking, supplier availability, verification, or live price claims are made by this scaffold.
- Secrets belong in environment variables or a secrets manager, never in frontend code or source control.

See `docs/` for the architecture, schema, and API conventions.
