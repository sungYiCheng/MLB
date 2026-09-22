# Azure Current State

這份筆記用 step by step 的方式整理目前 MLB AI Daily 在 Azure 上已經建立與部署的內容，以及 CI/CD 怎麼把程式部署上去。

## Big Picture

目前架構是：

```text
GitHub / Azure DevOps Repo
  -> Azure DevOps Pipelines
      -> Backend CI/CD
          -> ACR build backend image
          -> Azure Container Apps backend
          -> Smoke test
      -> Frontend CI/CD
          -> Angular build
          -> Azure Static Web Apps frontend
          -> Smoke + E2E test

Browser
  -> Azure Static Web Apps
      -> Azure Container Apps backend
          -> MLB Stats API
          -> Application Insights / Log Analytics
```

目前還沒有真正建立 AKS。AKS 相關的 `k8s/base` manifests 是下一階段的準備。

## Azure Resource Group

目前主要資源集中在：

```text
rg-mlb-ai-go-dev
```

這個 resource group 裡目前有：

| Resource | Name | 用途 |
| --- | --- | --- |
| Azure Container Registry | `acrmlbaigo` | 存放 backend Docker image |
| Container Apps Environment | `cae-mlb-ai-go-dev` | Container Apps 的執行環境 |
| Container App | `ca-mlb-ai-api` | 執行 .NET backend API |
| Static Web App | `swa-mlb-ai-go-dev` | 部署 Angular frontend |
| User-assigned identity | `id-mlb-ai-go-acr-pull` | 讓 backend / Azure 資源能拉 ACR image |
| Log Analytics Workspace | `log-mlb-ai-go-dev` | 查 logs、KQL、觀測資料 |
| Application Insights | `appi-mlb-ai-api-dev` | 看 requests、failures、duration、dependencies |
| Managed workspace | `workspace-rgmlbaigodev3ZWi` | Container Apps 先前自動建立的 workspace |
| Smart Detection | `Application Insights Smart Detection` | Application Insights 自動偵測相關資源 |

## Backend Hosting

Backend 目前跑在 Azure Container Apps：

```text
ca-mlb-ai-api
```

目前公開網址：

```text
https://ca-mlb-ai-api.wonderfulpond-0bfd6efa.eastasia.azurecontainerapps.io
```

目前 backend image 來自 ACR：

```text
acrmlbaigo.azurecr.io/mlb-ai-api:<commit-sha>
```

目前已部署過的 image tag 會對應到 Azure DevOps commit，例如：

```text
305cc22d4a4daf3e34b4f0949598616cf28c6e25
```

Backend Container App 目前有這些重要環境變數：

```text
ASPNETCORE_ENVIRONMENT
Cors__AllowedOrigins__0
AppVersion
AppBuildId
AppImageTag
APPLICATIONINSIGHTS_CONNECTION_STRING
ApplicationInsights__ConnectionString
```

其中：

| Env var | 目的 |
| --- | --- |
| `Cors__AllowedOrigins__0` | 允許 Azure Static Web Apps 前端呼叫 backend |
| `AppVersion` | 目前部署的 commit SHA |
| `AppBuildId` | Azure DevOps build id |
| `AppImageTag` | backend 使用的 image tag |
| `ApplicationInsights__ConnectionString` | .NET 讀取 Application Insights connection string |

## Frontend Hosting

Frontend 目前跑在 Azure Static Web Apps：

```text
swa-mlb-ai-go-dev
```

公開網址：

```text
https://yellow-forest-04081e300.5.azurestaticapps.net
```

Static Web Apps 適合目前前端，因為 Angular build 完就是靜態檔，不需要 VM 或 container 常駐。

## CI/CD Flow

目前 Azure DevOps 有兩條主要 pipeline：

```text
MLB_AI_GO-Backend-CI
MLB_AI_GO-Frontend-CI
```

Repo 裡的 YAML：

```text
azure-pipelines-backend.yml
azure-pipelines-frontend.yml
azure-pipelines-infra.yml
```

### Backend Pipeline

Backend pipeline 目前流程：

```text
Validate
  -> dotnet restore
  -> dotnet build
  -> dotnet test

BuildImage
  -> az acr build
  -> push image tags:
      <commit-sha>
      build-<BuildId>
      dev-latest

DeployBackend
  -> az containerapp update
  -> 設定 CORS
  -> 設定 AppVersion / AppBuildId / AppImageTag
  -> 如果 App Insights 存在，注入 connection string

SmokeTest
  -> GET /health
  -> GET /
  -> CORS preflight check

IntegrationCheck
  -> GET /api/games/today
  -> optional，不讓外部 MLB API 短暫失敗擋住部署
```

目前最新 backend pipeline 已成功部署 `305cc22`。

### Frontend Pipeline

Frontend pipeline 目前流程：

```text
ValidateFrontend
  -> npm ci
  -> Vitest unit tests
  -> Angular production build
  -> publish build artifact

DeployFrontend
  -> Azure Static Web Apps deploy

FrontendSmokeTest
  -> 確認前端網址回應
  -> Playwright E2E test
```

Frontend pipeline 使用 secret variable：

```text
AZURE_STATIC_WEB_APPS_API_TOKEN
```

這個 token 不會放進 repo。

## Observability

目前 backend 已接上：

```text
Application Insights
Log Analytics
```

你可以在 Application Insights 裡看：

- requests
- failures
- performance
- dependencies
- live metrics

也可以在 Log Analytics 用 KQL 查：

```kql
AppRequests
| where TimeGenerated > ago(1h)
| summarize Count=count(), AvgDurationMs=avg(DurationMs) by Name, Success
| order by Count desc
```

我們也已經放了 KQL 範本：

```text
infra/azure/queries
```

## Ingress Concept

Ingress 的核心概念是：

```text
外部 HTTP/HTTPS 流量要怎麼進入你的服務
```

### 在目前 Container Apps 裡

現在 backend 是 Azure Container Apps，Ingress 是 Azure 幫你管理的。

你只要設定：

```text
ingress: external
target port: 8080
```

Azure 就會給你一個公開網址：

```text
https://ca-mlb-ai-api.wonderfulpond-0bfd6efa.eastasia.azurecontainerapps.io
```

所以目前流量是：

```text
Browser / Frontend
  -> Container Apps external ingress
      -> backend container port 8080
```

你不需要自己建 Service、Ingress Controller 或 Load Balancer。

### 在未來 AKS 裡

AKS 不會像 Container Apps 一樣全部幫你包好。你要自己準備：

```text
Ingress Controller
Ingress
Service
Deployment / Pod
```

未來流量會變成：

```text
Browser
  -> Public IP / Load Balancer
      -> Ingress Controller
          -> Ingress rule
              -> Service
                  -> Pod
```

我們目前在 repo 裡先準備了：

```text
k8s/base/backend-ingress.yaml
k8s/base/backend-service.yaml
k8s/base/backend-deployment.yaml
```

意思是先把 Container Apps 幫你包起來的東西拆開學。

## Container Apps vs AKS

| 目前 Container Apps | 未來 AKS |
| --- | --- |
| Container App | Deployment |
| Revision | ReplicaSet / rollout revision |
| External ingress | Ingress Controller + Ingress |
| Built-in public URL | Public IP / DNS / Ingress host |
| Env vars | ConfigMap / Secret |
| Scale settings | replicas / HPA |
| Health check | readinessProbe / livenessProbe |

## What We Have Not Done Yet

目前還沒有：

- 建立 AKS cluster
- 建立 AKS node pool
- 安裝 NGINX Ingress Controller
- 把 backend 真正部署到 AKS
- 修改 frontend production API URL 指向 AKS
- 把 AKS deploy 接進 backend pipeline

這些會是下一階段。

## Suggested Next Step

下一步建議先做：

```text
AKS low-cost infra plan
```

也就是先決定：

- AKS cluster name
- node size
- node count
- 是否只測試時 start，用完 stop
- ingress controller 選 NGINX 或 Application Gateway
- backend pipeline 要新增 AKS deploy stage，還是先手動部署
