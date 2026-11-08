# Integration tests

Cross-service tests will be added here as the API contracts stabilize.

Stage 1 includes unit/API tests in `services/ai-api/tests`. The ASP.NET Core service currently exposes a health endpoint and EF Core model; run `dotnet build` before adding migration and persistence tests.
