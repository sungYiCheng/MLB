# MLB AI Daily 練習紀錄

最後更新：2026-10-06

## 目標

這是一個用來練習全端開發與 Azure 部署的 MLB dashboard side project。

這個專案不只是把功能做出來，也要把整個練習路線留下來，方便之後回頭複習：

- 建立前後端分離的專案
- 串接 MLB 即時比賽資料
- 一步一步加強前端 UI 和資料呈現
- 將原始碼同步到 GitHub 與 Azure DevOps
- 將前端與後端部署到 Azure
- 為後續 CI/CD 與 AKS 練習做準備

## 目前專案結構

```text
.
+-- backend/
|   +-- Dockerfile
|   +-- MlbAi.sln
|   +-- src/
|       +-- MlbAi.Api/
|       +-- MlbAi.Application/
|       +-- MlbAi.Infrastructure/
+-- frontend/
|   +-- angular.json
|   +-- package.json
|   +-- src/
+-- azure-pipelines.yml
+-- azure-pipelines-infra.yml
+-- azure-pipelines-backend.yml
+-- azure-pipelines-frontend.yml
+-- infra/
+-- k8s/
+-- docs/
+-- PROJECT_CONTEXT.md
+-- README.md
```

## 架構圖

```mermaid
flowchart LR
    User[使用者瀏覽器] --> SWA[Azure Static Web Apps<br/>Angular 前端]
    SWA --> API[Azure Container Apps<br/>.NET Web API]
    API --> MLB[MLB Stats API]

    Dev[本機開發] --> FE[Angular Dev Server<br/>127.0.0.1:53180]
    FE --> Proxy[本機 /api Proxy]
    Proxy --> LocalAPI[.NET API<br/>localhost:5106]
    LocalAPI --> MLB

    GitHub[GitHub Repo] --> Source[Source Code]
    AzureDevOps[Azure DevOps Repo] --> Source
    Source --> ACR[Azure Container Registry<br/>後端 Image]
    ACR --> API
    API --> AppInsights[Application Insights<br/>requests / errors / latency]
    API --> LogAnalytics[Log Analytics<br/>container logs]
```

## 執行流程

```mermaid
sequenceDiagram
    participant Browser as Browser
    participant Frontend as Static Web Apps / Angular 前端
    participant Backend as Container Apps / .NET API 後端
    participant MLB as MLB Stats API

    Browser->>Frontend: 開啟 dashboard
    Frontend->>Backend: 請求 MLB dashboard 資料
    Backend->>MLB: 抓取賽程、比分、球員與球隊資料
    MLB-->>Backend: 回傳官方 MLB 資料
    Backend-->>Frontend: 回傳整理後的 JSON
    Frontend-->>Browser: 顯示比賽、戰績、領先者、球員資訊
```

## 部署流程

```mermaid
flowchart TD
    Code[本機程式碼] --> Commit[Git Commit]
    Commit --> GitHub[Push 到 GitHub]
    Commit --> AzureRepo[Push 到 Azure DevOps Repo]
    AzureRepo --> BackendPipeline[Backend Pipeline]
    AzureRepo --> FrontendPipeline[Frontend Pipeline]
    BackendPipeline --> Build[ACR Task 雲端 Build]
    Build --> Image[acrmlbaigo.azurecr.io/mlb-ai-api]
    Image --> ContainerApp[Azure Container Apps 後端]
    FrontendPipeline --> FrontendBuild[Angular production build]
    FrontendBuild --> StaticWebApp[Azure Static Web Apps 前端]
```

## 目前 Azure DevOps Pipeline 流程

```mermaid
flowchart TD
    Push[Push 到 Azure DevOps main] --> Validate[Validate<br/>dotnet restore/build]
    Validate --> BuildImage[BuildImage<br/>ACR cloud build]
    BuildImage --> DeployBackend[DeployBackend<br/>更新 image 與 CORS env var]
    DeployBackend --> SmokeTest[SmokeTest<br/>檢查 /health、/ 與 CORS]
    SmokeTest --> IntegrationCheck[IntegrationCheck<br/>選擇性檢查 /api/games/today]
    Push --> FrontendValidate[ValidateFrontend<br/>npm ci/build]
    FrontendValidate --> FrontendDeploy[DeployFrontend<br/>Static Web Apps]
    FrontendDeploy --> FrontendSmoke[FrontendSmokeTest<br/>檢查前端網址]
```

目前 Azure DevOps YAML 已拆開：

```text
azure-pipelines.yml           # 停用的總入口
azure-pipelines-infra.yml     # 手動觸發的 infra provisioning pipeline
azure-pipelines-backend.yml   # 後端 pipeline
azure-pipelines-frontend.yml  # 前端 pipeline
```

目前 pipeline 使用 Azure DevOps service connection 名稱：

```text
sc-mlb-ai-go-azure
```

這個 service connection 需要在 Azure DevOps 專案中建立，並授權它能操作目前的 Azure resource group 與 Container Apps。

## Backend Image Tag 策略

後端 image 現在不再只依賴單一 `latest` 概念，而是同一次 ACR build 會產生三種 tag：

```text
mlb-ai-api:<commit-sha>
mlb-ai-api:build-<azure-devops-build-id>
mlb-ai-api:dev-latest
```

其中實際部署到 Azure Container Apps 的是 `<commit-sha>`，這樣每個 Container Apps revision 都能追到明確程式版本。

部署時 backend pipeline 也會寫入：

```text
AppVersion
AppBuildId
AppImageTag
```

所以 `/health` 會多回傳 deployment metadata，方便確認目前 Azure 上跑的是哪次 commit / build。

## Observability 觀測

目前後端已開始接 Azure 觀測資源：

```text
Application Insights
Log Analytics Workspace
```

Application Insights 偏向看應用程式層級：

- request 次數
- response time
- failed requests
- dependency calls
- exception / failure

Log Analytics Workspace 偏向收集與查詢 logs：

- Container Apps console logs
- 平台 logs
- KQL 查詢

後端透過這個環境變數連到 Application Insights：

```text
APPLICATIONINSIGHTS_CONNECTION_STRING
```

這個值由 infra script 或 backend pipeline 從 Azure resource 查出來後注入 Container App，不會寫死在 repo。

`/health` 現在也會有：

```text
application-insights
```

用來確認目前 backend process 是否已經拿到 Application Insights connection string。

觀測操作筆記放在：

```text
docs/observability.md
```

常用 KQL 查詢範本放在：

```text
infra/azure/queries
```

目前 infra script 也有 Azure Monitor alert rule 骨架，但預設不建立。可以先用：

```powershell
.\infra\azure\provision-dev.ps1 -PlanOnly -EnableAlertRules
```

看它會建立哪些 alert rules。

## Kubernetes / AKS 準備

目前已先建立 AKS 前的 Kubernetes manifest 雛形：

```text
k8s/base/
  namespace.yaml
  backend-configmap.yaml
  backend-secret.example.yaml
  backend-deployment.yaml
  backend-service.yaml
  backend-ingress.yaml
  kustomization.yaml
```

這一層先不真的建立 AKS，而是練習把 Container Apps 的設定拆成 Kubernetes resources：

| Container Apps | Kubernetes |
| --- | --- |
| Container App | Deployment |
| Environment variables | ConfigMap / Secret |
| External ingress | Service + Ingress |
| `/health` smoke test | readinessProbe / livenessProbe |
| Container App revision | Deployment rollout revision |

詳細筆記放在：

```text
docs/kubernetes-aks-prep.md
```

## AKS Lab 建立進度

目前已新增獨立、手動觸發的 AKS Lab Pipeline：

```text
azure-pipelines-aks-lab.yml
```

Pipeline 支援：

```text
Plan -> Preflight -> Create -> Deploy -> Stop / Start -> Status -> Destroy
```

2026-10-06 已完成 `Plan`、`Preflight`、`Create` 與 `Deploy`。AKS cluster 與 system node 已建立並開始計費，MLB API 已在 cluster 內運行；application-facing Ingress 尚未安裝。

已建立並保留的免費前置資源：

```text
rg-mlb-ai-go-aks-lab
  id-mlb-ai-go-aks-control
  id-mlb-ai-go-aks-kubelet
```

已建立的 AKS 資源：

```text
aks-mlb-ai-go-lab
  Kubernetes 1.35
  nodepool1: 2 x Standard_D2_v4, 32 GiB OS disk each

MC_rg-mlb-ai-go-aks-lab_aks-mlb-ai-go-lab_eastasia
  VM Scale Set / VNet / NSG / managed network resources
```

Create run `#27` 成功，cluster 為 `Succeeded / Running`，Kubernetes node 為 `Ready`。

Gateway Deploy run `#30` 成功，目前 runtime 狀態：

```text
Namespace:  mlb-ai-go
Deployment: mlb-ai-api 1/1 available
Pod:        1/1 Running, restarts 0
Image:      acrmlbaigo.azurecr.io/mlb-ai-api:aks-lab-30
Service:    ClusterIP 10.0.105.212:80
Endpoint:   Pod IP is dynamic; current verification used port 8080
```

已完成第一次 self-healing 實驗：手動刪除 MLB API Pod 後，ReplicaSet 自動建立替代 Pod；Service IP 不變，並重新指向新 Pod endpoint。

已完成的權限：

- Azure DevOps Service Principal 對 Lab RG 有最小範圍 `Contributor`。
- Control plane identity 對 kubelet identity 有 `Managed Identity Operator`。
- Kubelet identity 對共用 ACR 有 `AcrPull`。

Preflight 包含 Azure providers、ACR、node VM SKU、East Asia regional/family vCPU quota、Resource Group、identities，以及 base/ingress Kustomize manifests。最初規劃的 `Standard_B2s` 在正式 Create 時被此訂閱的 East Asia AKS policy 拒絕，因此後續改用 allowed list 內、且 Dv4-family quota 為 `0/10` 的 `Standard_D2_v4`。

這次最重要的故障排除經驗：

- 個人帳號能看見 Azure 資源，不代表 Azure DevOps Service Principal 也有權限。
- AKS managed node Resource Group 必須讓 AKS 自動建立，不能先建立同名 Resource Group。
- 本機與 Pipeline 使用不同 identity，所以兩邊都要驗證。

完整操作、架構、RBAC 關係與錯誤排除請看：

```text
docs/aks-lab-runbook.md
```

Kubernetes runtime 架構、資源關係、probes、self-healing 與常用指令請看：

```text
docs/kubernetes-runtime-lab.md
```

## Azure Infra Provisioning

目前已開始把 Azure dev 資源整理成可重跑的基礎設施腳本：

```text
infra/azure/
  README.md
  variables.dev.ps1
  provision-dev.ps1
```

這一層的目的不是取代前後端 pipeline，而是補上「雲端資源本身怎麼建立」這塊。

目前 `provision-dev.ps1` 會描述並建立：

- Resource group
- Log Analytics Workspace
- Application Insights
- Azure Container Registry
- User-assigned managed identity
- Container Apps environment
- Backend Container App
- Static Web App shell

預設可以先用 dry run 看它會執行哪些 Azure CLI 指令：

```powershell
.\infra\azure\provision-dev.ps1 -PlanOnly
```

Azure DevOps 也新增手動觸發的：

```text
azure-pipelines-infra.yml
```

它預設 `planOnly=true`，所以第一次跑只會印出計畫，不會真的動 Azure 資源。

## 重要本機 Port

| Service | URL |
| --- | --- |
| Frontend dev server | `http://127.0.0.1:53180` |
| Backend API | `http://localhost:5106` |

前端刻意避開 `4200`，因為這個 port 常被其他 Angular 專案使用。

## Azure 開發資源

| Resource | 目前設定 |
| --- | --- |
| Frontend | Azure Static Web Apps Free |
| Backend | Azure Container Apps Consumption |
| Backend scale | `minReplicas=0`, `maxReplicas=1` |
| Backend size | `0.25 CPU`, `0.5Gi memory` |
| Container registry | Azure Container Registry Basic |
| Logs | `log-mlb-ai-go-dev` Log Analytics Workspace |
| App telemetry | `appi-mlb-ai-api-dev` Application Insights |

目前開發環境網址：

- Frontend: `https://yellow-forest-04081e300.5.azurestaticapps.net`
- Backend: `https://ca-mlb-ai-api.wonderfulpond-0bfd6efa.eastasia.azurecontainerapps.io`

## 為什麼目前不是 VM

目前這套部署沒有使用 Azure Virtual Machine。

前端是放在 Azure Static Web Apps。後端是包成 container，跑在 Azure Container Apps。Azure 會管理底層 compute、擴縮和 runtime 環境。因為後端設定是 `minReplicas=0`，所以沒有流量時可以縮到 0。

這跟 VM 不同。VM 是建立一台完整主機，通常只要機器存在或開著，就會持續產生成本。

## 目前成本預估

以目前學習用途來看，粗估月費：

- 輕量測試：約 US$5 到 US$8/月
- 每天偶爾使用：約 US$5 到 US$10/月
- 後端幾乎整天都有流量：約 US$20 到 US$30/月

主要固定成本是 Azure Container Registry Basic，大約 US$5/月。Static Web Apps 目前是 Free tier。Container Apps 大多是用量計費，而且目前後端可以 scale to zero。

## 後端筆記

後端是 .NET Web API，分成三個專案：

- `MlbAi.Api`：HTTP endpoints 與應用程式啟動設定
- `MlbAi.Application`：contracts 與 DTOs
- `MlbAi.Infrastructure`：MLB Stats API 串接

主要 API endpoints：

- `GET /`
- `GET /health`
- `GET /api/games/today`

後端會先把 MLB 官方 API 的資料整理成前端比較好用的 JSON。Dashboard 目前包含比賽卡片、分區戰績、外卡排名、球員大頭照、打者/投手數據、各項數據領先者。

## 前端筆記

前端是 Angular app。

本機開發：

```powershell
cd frontend
npm start
```

本機 dev server 會透過 `proxy.conf.json`，將 `/api` request 轉到 `http://localhost:5106`。

Production build 會使用這個檔案中的 Azure 後端 URL：

```text
frontend/src/environments/environment.production.ts
```

前端目前已加入 Vitest unit tests：

```text
frontend/src/app/mlb-display.ts
frontend/src/app/mlb-display.spec.ts
```

`mlb-display.ts` 放前端畫面顯示用的純邏輯，例如比賽狀態、比分、打者/投手 stat line、球隊 logo 與球員大頭照 URL。這些邏輯不需要啟動瀏覽器或 Angular component 就能測，因此很適合放進 CI/CD 的第一層檢查。

常用測試指令：

```powershell
cd frontend
npm test
npm run test:ci
```

前端也已加入 Playwright E2E tests：

```text
frontend/playwright.config.ts
frontend/e2e/dashboard.spec.ts
```

E2E tests 會用真正的 Chromium 瀏覽器打開頁面，檢查使用者實際會碰到的流程。目前先測：

- dashboard shell 是否載入
- 主要標題與 summary 是否存在
- Daily Board 是否能收合與展開
- 部署後的資料載入是否會結束
- 是否有前端 runtime page error

本機測 deployed site：

```powershell
cd frontend
$env:E2E_BASE_URL = 'https://yellow-forest-04081e300.5.azurestaticapps.net'
npm run e2e:ci
```

## Git 與 Remote 筆記

目前 remotes：

```text
origin -> GitHub
azure  -> Azure DevOps
```

目前工作規則：

- 不自動 push，除非明確要求
- 想先本機檢查時，只做 local commit
- 明確要求 push 時，才推到遠端

## 這次練習中的重要決策

- 前端和後端分成 `frontend/` 與 `backend/`。
- 前端使用 Angular。
- 後端使用 .NET Web API。
- 後端採用 Clean Architecture 風格拆成 Api、Application、Infrastructure。
- 避開本機前端 port `4200`，改用 `53180`。
- GitHub 作為主要原始碼 remote。
- 同步到 Azure DevOps，之後練 Azure Pipeline。
- 因為本機 Docker Desktop 被 WSL2/virtualization 問題卡住，所以先不依賴本機 Docker。
- 使用 Azure ACR cloud build，在 Azure 上 build Docker image。
- 前端部署到 Azure Static Web Apps。
- 後端部署到 Azure Container Apps，不先使用 VM 或 AKS。
- Azure dev 資源先用 Azure CLI + PowerShell 腳本整理，之後可再轉成 Bicep 或 Terraform。
- 後端接 Application Insights，開始練習部署後觀測。
- 先建立 Kubernetes manifest 雛形，讓 Container Apps 的設定能對應到 AKS 概念。
- AKS 保留成後續進階練習。

## 目前下一步

2026-10-07 已完成：

1. Deployment 在 1 與 2 replicas 之間擴縮，並觀察 Service EndpointSlice。
2. 使用環境變數觸發 Rolling Update，觀察新舊 ReplicaSet 替換。
3. 使用 `kubectl rollout undo` 回復到 `aks-lab-28`。
4. 故意將 readiness path 改錯，驗證壞 Pod 不會進入 Service 流量，且舊 Pod 會留下繼續服務。
5. 啟用 AKS Managed Gateway API 與 Application Routing Istio。
6. 因第二個 `istiod` 出現 `Insufficient cpu`，將 node pool 從 1 擴成 2。
7. 建立 Gateway 與 HTTPRoute，並從外部通過 `http://20.24.106.104/health` 取得 HTTP 200。
8. 使用 sslip.io、Let’s Encrypt 與 cert-manager 建立免費 HTTPS，Pipeline run `#31` 驗證 Certificate `Ready=True` 且公開 `/health` 回傳 HTTP 200。
9. 將 frontend production API URL 改為 `https://20-24-106-104.sslip.io`，讓 Static Web Apps 經 Gateway 與 Service 呼叫 AKS Pod。
10. Frontend automatic CI run `#32` 與手動重複驗證 run `#33` 都成功；公開 bundle、API、CORS 與 E2E 均已驗證。

下一階段可加入 Gateway access logs、API HPA 與 NetworkPolicy。當天結束時應執行 `Stop` 或 `Destroy`，因為現在有兩個 node 與公開 Load Balancer。

更細的 Azure DevOps CI/CD 操作筆記放在：

```text
docs/azure-devops-cicd.md
```

目前 Azure 實際狀態與 Ingress 概念整理放在：

```text
docs/azure-current-state.md
```

設定管理與 CORS 筆記放在：

```text
docs/configuration-management.md
```

## 常用指令

Build 後端：

```powershell
dotnet build backend\MlbAi.sln
```

啟動後端：

```powershell
dotnet run --project backend\src\MlbAi.Api\MlbAi.Api.csproj
```

啟動前端：

```powershell
cd frontend
npm start
```

檢查 Git 狀態：

```powershell
git status --short
```

準備好後推到 GitHub：

```powershell
git push origin main
```

準備好後推到 Azure DevOps：

```powershell
git push azure main
```
