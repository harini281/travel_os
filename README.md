# TravelOS

**The Intelligent Operating System for Travel**

A greenfield, global travel platform built with React, ASP.NET Core, PostgreSQL, and Python/FastAPI.

## Stage 1 scope

This starter establishes the repository structure, service boundaries, initial data model, local PostgreSQL infrastructure, health endpoints, and versioned contracts. It does **not** claim that production authentication, live travel data, or a real AI model is implemented.

## Prerequisites

- Node.js 22+
- .NET 8 SDK
- Python 3.11+
- Docker Desktop / Docker Engine with Compose

## Repository layout

```text
apps/web                 React + TypeScript + Vite frontend
services/platform-api    ASP.NET Core platform API
services/ai-api          FastAPI AI service boundary
packages/contracts       Versioned JSON contracts
database                 Schema notes, migrations and seeds
infrastructure           Docker and deployment configuration
docs                     Product and architecture documentation
tests                    Cross-service integration test area
```

## Run locally

1. Copy `.env.example` to `.env` and adjust values if required.
2. Start PostgreSQL:

   ```bash
   docker compose up -d postgres
   ```

3. Start the platform API:

   ```bash
   cd services/platform-api
   dotnet restore
   dotnet run
   ```

4. Start the AI API in another terminal:

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
- Platform API OpenAPI: http://localhost:8080/openapi/v1.json
- AI API: http://localhost:8001
- AI API docs: http://localhost:8001/docs
- PostgreSQL: localhost:5432

## Important boundaries

- PostgreSQL is the source of truth for platform data.
- The platform API owns authorization and persistence.
- The AI API produces untrusted suggestions; it does not write to the platform database.
- Generated itinerary drafts remain separate from approved itinerary records.
- No booking, supplier availability, verification, or live price claims are made by this scaffold.
- Secrets belong in environment variables or a secrets manager, never in frontend code or source control.

See `docs/` for the architecture, schema, and API conventions.
