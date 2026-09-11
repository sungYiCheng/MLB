# MLB AI Daily

MLB AI Daily is a side-project workspace for a small full-stack web app:

- Angular frontend
- .NET Web API backend
- Clean Architecture-style backend split into Api, Application, and Infrastructure
- Future Azure deployment with low-cost hosting in mind

## Current Status

This workspace has been prepared as a fresh repo on this computer. The backend API and frontend MVP are ready.

```text
backend/
  MlbAi.sln
  src/
    MlbAi.Api/
    MlbAi.Application/
    MlbAi.Infrastructure/
frontend/
  src/
```

## Backend Commands

```powershell
dotnet build backend\MlbAi.sln
dotnet run --project backend\src\MlbAi.Api\MlbAi.Api.csproj
```

## Frontend Commands

```powershell
cd frontend
npm start
```

The Angular dev server runs at `http://127.0.0.1:53180` and proxies `/api` requests to `http://localhost:5106`.

The API currently exposes:

- `GET /`
- `GET /api/games/today`

`/api/games/today` calls MLB's public Stats API schedule endpoint:

```text
https://statsapi.mlb.com/api/v1/schedule?sportId=1&date=YYYY-MM-DD
```

The response includes game id, official game date, UTC game time, teams, status, scores, records, probable pitchers, venue, series details, inning/count data, and line-score totals when MLB provides them.

## Next Steps

1. Add richer game details and daily analysis.
2. Improve loading/error states after the API shape settles.
3. Add deployment files for Azure Static Web Apps and Azure Container Apps when the app shape is clearer.
