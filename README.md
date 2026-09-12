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

## Container Build

The backend includes a Dockerfile for cloud builds:

```text
backend/Dockerfile
```

The container listens on port `8080`.

This repo is prepared to build the backend image in Azure, so local Docker Desktop is optional for the CI/CD path.
The Azure pipeline uses ACR Tasks to build and push the image from the Dockerfile without requiring Docker on this computer.

The current Azure development backend is:

```text
https://ca-mlb-ai-api.wonderfulpond-0bfd6efa.eastasia.azurecontainerapps.io
```

The current Azure development frontend is:

```text
https://yellow-forest-04081e300.5.azurestaticapps.net
```

Before running the pipeline, update these variables in `azure-pipelines.yml`:

```yaml
azureServiceConnection: 'TODO-AZURE-SERVICE-CONNECTION'
acrName: 'acrmlbaigo'
imageRepository: 'mlb-ai-api'
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

1. Create an Azure DevOps Azure Resource Manager service connection.
2. Replace the `azureServiceConnection` placeholder in `azure-pipelines.yml`.
3. Wire the Angular production API URL to the Azure Container Apps backend.
4. Deploy the frontend to Azure Static Web Apps.
5. Move the same image flow to AKS when ready.
