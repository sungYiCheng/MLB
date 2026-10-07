# Azure Current State

這份筆記用 step by step 的方式整理目前 MLB AI Daily 在 Azure 上已經建立與部署的內容，以及 CI/CD 怎麼把程式部署上去。

最後更新：2026-10-07

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
      -> AKS Lab Pipeline
          -> Plan / Preflight / Create
          -> ACR build
          -> Deploy to AKS
          -> Internal + public HTTPS smoke tests

Browser
  -> Azure Static Web Apps
      -> AKS Gateway API over HTTPS
          -> ClusterIP Service
          -> MLB API Pod
          -> MLB Stats API
          -> Application Insights / Log Analytics

AKS Lab
  -> ClusterIP Service
      -> MLB API Pod
          -> MLB Stats API
          -> Application Insights
```

AKS backend 已透過 Managed Gateway API、sslip.io、Let’s Encrypt 與 cert-manager 提供 `https://20-24-106-104.sslip.io`。Frontend production 設定已改為這個 AKS endpoint，等待 frontend pipeline 完成後，公開網站就會直接呼叫 AKS Pod。原本的 Container Apps backend 暫時保留，作為比較與回復路徑。

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

AKS 練習資源放在另一個 Resource Group：

```text
rg-mlb-ai-go-aks-lab
```

| Resource | Name | 用途 |
| --- | --- | --- |
| AKS cluster | `aks-mlb-ai-go-lab` | Kubernetes 練習環境 |
| Control plane identity | `id-mlb-ai-go-aks-control` | AKS control plane 使用的 Azure identity |
| Kubelet identity | `id-mlb-ai-go-aks-kubelet` | Node 從 ACR 拉取 image |

AKS 自動建立並管理：

```text
MC_rg-mlb-ai-go-aks-lab_aks-mlb-ai-go-lab_eastasia
```

其中包含 VM Scale Set、VNet、NSG、managed disk 與 AKS 管理的網路資源。

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

目前 Azure DevOps 的主要 pipelines 包含：

```text
MLB_AI_GO-Backend-CI
MLB_AI_GO-Frontend-CI
MLB_AI_GO-AKS-Lab
```

Repo 裡的 YAML：

```text
azure-pipelines-backend.yml
azure-pipelines-frontend.yml
azure-pipelines-infra.yml
azure-pipelines-aks-lab.yml
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

### AKS Lab Pipeline

AKS Lab Pipeline 是手動觸發，不會因一般 push 自動建立或刪除 cluster：

```text
Plan -> Preflight -> Create -> Deploy -> Stop / Start -> Status -> Destroy
```

2026-10-06 執行結果：

- Preflight run `#26` 成功。
- Create run `#27` 成功。
- Gateway Deploy run `#30` 成功。
- Free HTTPS Deploy run `#31` 成功。
- Image 為 `acrmlbaigo.azurecr.io/mlb-ai-api:aks-lab-30`。
- Cluster 內部 `/health` smoke test 成功。

## AKS Runtime

目前 cluster：

```text
aks-mlb-ai-go-lab
  Kubernetes 1.35.8
  nodepool1: 2 x Standard_D2_v4
  nodes: 2 x Ready
```

目前應用程式：

```text
Namespace:  mlb-ai-go
Deployment: mlb-ai-api 1/1 available
Pod:        mlb-ai-api-59dbdd9457-pvcgw 1/1 Running
Image:      acrmlbaigo.azurecr.io/mlb-ai-api:aks-lab-30
Service:    ClusterIP 10.0.105.212:80
Endpoint:   10.244.1.28:8080 (Pod IP may change)
ConfigMap:  mlb-ai-api-config
Secret:     mlb-ai-api-secrets
Gateway:    mlb-ai-api-gateway Programmed=True
HTTPRoute:  mlb-ai-api Accepted=True / ResolvedRefs=True
Public URL: https://20-24-106-104.sslip.io/health
TLS:        Let's Encrypt Certificate Ready=True
```

Deployment 使用 RollingUpdate，resource settings 為：

```text
requests: 100m CPU / 128Mi memory
limits:   250m CPU / 512Mi memory
```

Readiness 與 liveness 都呼叫 `/health`。Pod 已持續運行約 22 小時、`RESTARTS=0`，Deployment conditions 為 `Available=True` 與 `Progressing=True`。

已完成 self-healing 實驗：手動刪除舊 Pod 後，ReplicaSet 自動建立替代 Pod；Service ClusterIP 保持不變並改指向新 Pod IP。

完整 runtime 架構請看：

```text
docs/kubernetes-runtime-lab.md
```

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

### 在目前 AKS 裡

AKS 不會像 Container Apps 一樣全部幫你包好。你要自己準備：

```text
Ingress Controller
Ingress
Service
Deployment / Pod
```

啟用 Gateway API 後，流量變成：

```text
Browser
  -> Public IP / Load Balancer
      -> Managed Gateway proxy
          -> HTTPRoute
              -> Service
                  -> Pod
```

目前 repo 已準備：

```text
k8s/base/backend-service.yaml
k8s/base/backend-deployment.yaml
k8s/overlays/gateway/backend-gateway.yaml
k8s/overlays/gateway/backend-http-route.yaml
```

目前 base 與 Gateway overlay 都已部署。Deployment、Pod 與 ClusterIP Service 負責應用執行；Gateway、HTTPRoute、LoadBalancer Service 與 Public IP 負責對外流量。

## Container Apps vs AKS

| 目前 Container Apps | 未來 AKS |
| --- | --- |
| Container App | Deployment |
| Revision | ReplicaSet / rollout revision |
| External ingress | Gateway + HTTPRoute |
| Built-in public URL | Public IP / DNS / Gateway listener |
| Env vars | ConfigMap / Secret |
| Scale settings | replicas / HPA |
| Health check | readinessProbe / livenessProbe |

## Intentionally Deferred Items

目前基礎 AKS 與 backend runtime 已完成。以下是刻意留到下一階段的項目，不是故障：

- 購買正式自有網域並取代 Lab 用的 sslip.io（非必要）
- 啟用 MLB API workload 的 HPA 自動擴縮（Gateway proxy 已有 managed HPA）
- 啟用 Container Insights / Managed Prometheus
- 將 Kubernetes Secret 進一步改成 Azure Key Vault + Workload Identity
- 加入 production 等級的 Entra/Kubernetes RBAC 與 network policy

目前 Container Insights 尚未啟用，所以 Portal 的 Monitoring > Insights 不會有完整 Container logs、CPU 與 memory dashboard。這是為了先控制學習複雜度與 Log Analytics 成本。

## Suggested Next Step

已完成 Kubernetes runtime 練習：

```text
Scale -> Rolling Update -> Rollback -> Readiness failure -> Gateway API
```

依序學習：

免費 DNS/TLS 與前端切換已完成設定；下一階段是 Gateway access logs 與 API HPA。
