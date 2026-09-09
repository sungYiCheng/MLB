# Project Context

## Absorbed Notes

This project comes from a prior Codex conversation about preparing an MLB AI daily website on another computer.

Important carry-over points:

- Codex account access and usage limits can be shared by logging into the same ChatGPT account, but local repo files, uncommitted changes, branches, environment variables, credentials, and MCP/app setup are machine-local.
- The safest way to move work between computers is still Git: push from the old computer, then clone or pull on the new computer.
- The intended app shape is Angular frontend plus .NET backend.
- For early Azure hosting, the preferred direction is low-cost services: Static Web Apps for frontend and Container Apps or Functions for backend. Specific pricing should be rechecked before committing to infrastructure.
- The backend should use a clean folder structure rather than placing multiple projects under a root API project folder.
- The frontend MVP has been generated with Angular and uses a fixed local dev port: `http://127.0.0.1:53180`.

## Backend Direction

Use this structure:

```text
backend/
  MlbAi.sln
  src/
    MlbAi.Api/
    MlbAi.Application/
    MlbAi.Infrastructure/
```

Dependency direction:

```text
MlbAi.Api -> MlbAi.Application
MlbAi.Api -> MlbAi.Infrastructure
MlbAi.Infrastructure -> MlbAi.Application
```

Initial contract:

- `IMlbService`
- `MlbGameDto`

The infrastructure implementation now calls MLB's public Stats API schedule endpoint for today's games.

## Frontend Direction

Use `frontend/` as a separate Angular workspace. During local development, run `npm start` from `frontend/`; it serves the app on `http://127.0.0.1:53180` and proxies `/api` requests to the backend on `http://localhost:5106`.
