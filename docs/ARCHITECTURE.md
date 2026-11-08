# Architecture

## Stage 1 topology

```text
React + TypeScript (apps/web)
          |
          v
ASP.NET Core Platform API (services/platform-api)
          |                         |
          v                         v
      PostgreSQL              FastAPI AI API
                              (services/ai-api)
                                     |
                                     v
                             Provider adapter boundary
                             (demo-only in Stage 1)
```

## Service responsibilities

### Web application
- Presentation and user interaction.
- Calls the platform API.
- Never stores provider secrets.
- Does not make authorization decisions.

### Platform API
- Owns users, trips, memberships, budgets, approved itineraries, and audit history.
- Enforces authorization and business rules.
- Owns persistence and database migrations.
- Calls the AI API through a server-side client in a later stage.

### AI API
- Accepts validated planning requests.
- Returns structured, untrusted suggestions.
- Does not connect directly to the platform database.
- Uses a demo provider in Stage 1; a real model is not configured.

### PostgreSQL
- Platform source of truth.
- UUID primary keys.
- UTC timestamps.
- Numeric monetary values with ISO 4217 currency codes.

## Architectural choice

Start with a modular monolith for the core platform API and a separately deployable AI service. Do not split every domain into microservices prematurely.

## Trust boundary

AI output is untrusted input. The platform API must validate it, associate it with the requesting user/trip, and require explicit approval before creating an approved itinerary.

## Future integrations

Maps, geocoding, routing, weather, accommodation, transport, payments, and booking providers should be integrated behind provider interfaces. Provider data must retain source and retrieval timestamps where relevant.
