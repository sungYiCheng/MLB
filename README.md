# MLB AI Daily

MLB AI Daily is a side-project workspace for a small full-stack web app:

- Angular frontend
- .NET Web API backend
- Clean Architecture-style backend split into Api, Application, and Infrastructure
- Azure deployment with low-cost hosting in mind

## Learning Notes

Practice notes, architecture diagrams, Azure deployment records, and current next steps are kept in:

- [docs/learning-record.md](docs/learning-record.md)
- [docs/azure-current-state.md](docs/azure-current-state.md)
- [docs/configuration-management.md](docs/configuration-management.md)
- [docs/observability.md](docs/observability.md)
- [docs/kubernetes-aks-prep.md](docs/kubernetes-aks-prep.md)
- [infra/azure/README.md](infra/azure/README.md)

## Current Status

This workspace has been prepared as a full-stack MLB dashboard repo. The backend API, frontend dashboard, and first Azure deployment are ready.

```text
backend/
  MlbAi.sln
  src/
    MlbAi.Api/
    MlbAi.Application/
    MlbAi.Infrastructure/
frontend/
  src/
k8s/
  base/
```

## Backend Commands

```powershell
dotnet build backend\MlbAi.sln
dotnet test backend\MlbAi.sln
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

Before running the backend pipeline, create an Azure DevOps Azure Resource Manager service connection named `sc-mlb-ai-go-azure`.

The CI/CD YAML files are split by app boundary:

```text
azure-pipelines.yml           # disabled index file
azure-pipelines-infra.yml     # manual infra provisioning pipeline
azure-pipelines-backend.yml   # backend build/image/deploy/smoke test
azure-pipelines-frontend.yml  # frontend build/deploy/smoke test
```

The current Azure dev resources are documented as reusable provisioning scripts under:

```text
infra/azure
```

AKS preparation manifests are under:

```text
k8s/base
```

The backend pipeline uses these deployment variables in `azure-pipelines-backend.yml`:

```yaml
azureServiceConnection: 'sc-mlb-ai-go-azure'
resourceGroupName: 'rg-mlb-ai-go-dev'
containerAppName: 'ca-mlb-ai-api'
frontendDevUrl: 'https://yellow-forest-04081e300.5.azurestaticapps.net'
acrName: 'acrmlbaigo'
applicationInsightsName: 'appi-mlb-ai-api-dev'
imageRepository: 'mlb-ai-api'
imageCommitTag: '$(Build.SourceVersion)'
imageBuildTag: 'build-$(Build.BuildId)'
imageDevLatestTag: 'dev-latest'
```

The backend CI/CD flow currently validates the .NET solution, runs backend unit tests, builds the backend image in ACR, tags the image with commit/build/dev tags, deploys the commit-tagged image to Azure Container Apps, configures the allowed frontend origin, injects the Application Insights connection string when the resource exists, runs smoke tests against `/health`, `/`, and the CORS response header, and then runs an optional integration check against `/api/games/today`.

Backend image tags:

| Tag | Purpose |
| --- | --- |
| `$(Build.SourceVersion)` | Exact commit deployed to Azure |
| `build-$(Build.BuildId)` | Azure DevOps pipeline run lookup |
| `dev-latest` | Latest successful dev backend image |

The backend `/health` response includes deployment metadata for the running revision:

```json
{
  "deployment": {
    "version": "<commit-sha>",
    "buildId": "<azure-devops-build-id>",
    "imageTag": "<image-tag>"
  }
}
```

The backend also reports whether Application Insights telemetry is configured:

```json
{
  "name": "application-insights",
  "status": "configured"
}
```

The frontend CI/CD flow installs Angular dependencies, runs Vitest unit tests, publishes JUnit test results, builds the production frontend, deploys the built files to Azure Static Web Apps, smoke-tests the public frontend URL, and runs Playwright E2E tests against the deployed site. The frontend pipeline needs a secret variable named `AZURE_STATIC_WEB_APPS_API_TOKEN`.

## Frontend Commands

```powershell
cd frontend
npm start
```

Frontend tests:

```powershell
cd frontend
npm test
npm run test:ci
npm run e2e
```

`npm run test:ci` writes a JUnit report to `frontend/test-results/junit.xml` for Azure DevOps.
`npm run e2e` runs Playwright tests. Set `E2E_BASE_URL` to test a deployed site instead of the local dev server.

The Angular dev server runs at `http://127.0.0.1:53180` and proxies `/api` requests to `http://localhost:5106`.

The API currently exposes:

- `GET /`
- `GET /health`
- `GET /api/games/today`

`/api/games/today` calls MLB's public Stats API schedule endpoint:

```text
https://statsapi.mlb.com/api/v1/schedule?sportId=1&date=YYYY-MM-DD
```

The response includes game id, official game date, UTC game time, teams, status, scores, records, probable pitchers, venue, series details, inning/count data, and line-score totals when MLB provides them.

## Next Steps

1. Create an Azure DevOps Azure Resource Manager service connection.
2. Push split pipeline YAML files to Azure DevOps.
3. Run the backend and frontend pipelines.
4. Add stricter deployment gates.
5. Move the same image flow to AKS when ready.
