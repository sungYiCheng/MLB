# Azure DevOps CI/CD 筆記

這份筆記記錄目前 MLB AI Daily 的 Azure DevOps pipeline 練習。

## 目前目標

先完成前後端分離 CI/CD，不急著進 AKS。

目前 pipeline 要做到：

```text
push 到 Azure DevOps main
  -> backend pipeline 驗證 .NET、build image、deploy Container Apps
  -> frontend pipeline 驗證 Angular、deploy Static Web Apps
  -> smoke test public frontend/backend endpoints
```

## Pipeline 檔案

主要檔案已拆成三支：

```text
azure-pipelines.yml           # 停用的總入口，只保留說明
azure-pipelines-infra.yml     # 手動觸發的 Azure dev 資源建置流程
azure-pipelines-backend.yml   # 後端 CI/CD
azure-pipelines-frontend.yml  # 前端 CI/CD
```

`azure-pipelines.yml` 設定：

```yaml
trigger: none
pr: none
```

這樣可以避免 Azure DevOps 預設抓根目錄 YAML 時，不小心把前後端一起部署。真正會跑的是另外兩支 pipeline YAML。

## Infra Pipeline

基礎設施 pipeline 檔案：

```text
azure-pipelines-infra.yml
```

這條 pipeline 預設：

```yaml
trigger: none
pr: none
```

也就是不會因為 push 自動執行，需要從 Azure DevOps 手動啟動。它會呼叫：

```text
infra/azure/provision-dev.ps1
```

目前有一個參數：

| Parameter | Default | 目的 |
| --- | --- | --- |
| `planOnly` | `true` | 只印出 Azure CLI 指令，不真的建立或更新 Azure 資源 |

這樣可以先練習 infra provisioning 的流程，又不會不小心重建或修改雲端資源。

## Backend Pipeline

後端 pipeline 檔案：

```text
azure-pipelines-backend.yml
```

目前 stages：

| Stage | 目的 |
| --- | --- |
| `Validate` | 還原、編譯並執行 .NET backend unit tests |
| `BuildImage` | 用 Azure Container Registry cloud build 建立 backend image |
| `DeployBackend` | 將新 image 部署到 Azure Container Apps，並設定 CORS allowed origin |
| `SmokeTest` | 驗證 Azure backend `/health`、`/` 與 CORS header 可以回應 |
| `IntegrationCheck` | 選擇性驗證 `/api/games/today` 能打到 MLB 資料 |

目前 `DeployBackend` 先使用一般 job，不使用 Azure DevOps environment gate。等基本 CI/CD 跑順後，再加 environment approval 會比較適合練正式 release flow。

後端 pipeline 只有在這些路徑變更時自動觸發：

```text
backend/**
azure-pipelines-backend.yml
```

### Backend image tag strategy

後端 pipeline 目前會用同一份 build 產生三個 ACR image tag：

| Tag | 範例 | 用途 |
| --- | --- | --- |
| Commit SHA | `6a07cad...` | 精準知道部署的是哪一版程式碼 |
| Build ID | `build-15` | 對回 Azure DevOps pipeline run |
| Dev latest | `dev-latest` | 代表 dev 環境最近一次成功 build 的 backend image |

實際部署到 Container Apps 時，使用的是 commit SHA tag：

```text
acrmlbaigo.azurecr.io/mlb-ai-api:<commit-sha>
```

這樣做的好處是 Azure 上每個 revision 都可以追到明確 commit，不會只看到模糊的 `latest`。

部署時也會寫入這些環境變數：

```text
AppVersion=<commit-sha>
AppBuildId=<azure-devops-build-id>
AppImageTag=<commit-sha>
APPLICATIONINSIGHTS_CONNECTION_STRING=<azure-managed-connection-string>
```

後端 `/health` 會回報：

```json
{
  "deployment": {
    "version": "<commit-sha>",
    "buildId": "<azure-devops-build-id>",
    "imageTag": "<image-tag>"
  }
}
```

之後進 AKS 時，同樣可以把這個策略套到 Kubernetes Deployment image tag 和 rollout/rollback 流程。

### Backend observability

後端現在有接 Application Insights SDK：

```text
Microsoft.ApplicationInsights.AspNetCore
```

Backend pipeline 在部署前會用 Azure CLI 查詢 Application Insights resource：

```bash
az resource show \
  --resource-group "$(resourceGroupName)" \
  --name "$(applicationInsightsName)" \
  --resource-type "Microsoft.Insights/components" \
  --query "properties.ConnectionString" \
  --output tsv
```

查到的 connection string 會用 Container Apps environment variable 注入：

```text
APPLICATIONINSIGHTS_CONNECTION_STRING
```

這個值不寫進 repo，也不放在 YAML 裡。Pipeline 每次部署時從 Azure resource 查出來，再塞進正在部署的 Container App revision。

完成後可以在 Azure Portal 的 Application Insights 裡看：

- requests
- failures
- performance
- dependencies
- live metrics

也可以在 Log Analytics Workspace 裡用 KQL 查 Container Apps logs。

## Frontend Pipeline

前端 pipeline 檔案：

```text
azure-pipelines-frontend.yml
```

目前 stages：

| Stage | 目的 |
| --- | --- |
| `ValidateFrontend` | 安裝 Node.js、執行 `npm ci`、跑 Vitest unit tests、發布測試結果、build Angular production bundle，並發布 build artifact |
| `DeployFrontend` | 使用 Azure Static Web Apps deployment token 將前端靜態檔部署到 Azure |
| `FrontendSmokeTest` | 驗證公開前端網址可以回應，且 HTML 裡有 Angular root element，接著用 Playwright 跑 deployed-site E2E tests |

前端 pipeline 只有在這些路徑變更時自動觸發：

```text
frontend/**
azure-pipelines-frontend.yml
```

前端 pipeline 需要 Azure DevOps secret variable：

```text
AZURE_STATIC_WEB_APPS_API_TOKEN
```

這個 token 是 Azure Static Web Apps 的 deployment token。它是敏感資料，所以不放進 repo，也不寫進 YAML。

前端目前使用 Vitest 做 unit tests：

```text
frontend/src/app/mlb-display.spec.ts
```

這批測試先針對前端畫面格式化邏輯：

- 比賽狀態是否為 live
- 台灣時間顯示
- 比分、戰績、落後場次 fallback
- 局數、好壞球、出局數格式
- 打者與投手數據文字
- 球隊 logo 與球員大頭照 URL

Pipeline 會跑：

```text
npm run test:ci
```

並把這個 JUnit 測試報表發布到 Azure DevOps：

```text
frontend/test-results/junit.xml
```

前端目前也使用 Playwright 做 E2E tests：

```text
frontend/e2e/dashboard.spec.ts
frontend/playwright.config.ts
```

這批測試會在前端部署到 Azure Static Web Apps 之後執行，直接打公開網址：

```text
E2E_BASE_URL=https://yellow-forest-04081e300.5.azurestaticapps.net
npm run e2e:ci
```

目前 E2E tests 先檢查：

- dashboard shell 可以載入
- 主要標題與 summary 欄位存在
- Daily Board 區塊可以收合與展開
- 初始資料載入流程會結束
- 沒有前端 runtime page error

Playwright 也會輸出 JUnit 報表：

```text
frontend/test-results/e2e-junit.xml
```

## Unit Test 位置

目前後端第一個測試專案：

```text
backend/tests/MlbAi.Application.Tests
```

這批測試先針對不依賴外部 MLB API 的純邏輯：

- 戰績格式化
- MLB game type code 轉顯示文字
- double-header code 轉顯示文字
- title case 顯示格式
- 英文單複數 suffix

Pipeline 裡的 `Validate` stage 會先跑 `dotnet test`。如果 unit test 失敗，就不會繼續 build image 或 deploy。

Docker image build 只 restore/publish `MlbAi.Api` 專案，不把 test project 放進 runtime image。Unit tests 是 CI 品質關卡，不是正式 container 需要執行的內容。

## Health Check 與 Smoke Test

目前後端提供：

```text
GET /health
```

`/health` 是部署後的穩定健康檢查，不會打外部 MLB API。它會回傳：

- service name
- overall status
- ASP.NET Core environment
- UTC timestamp
- checks 清單

目前 checks：

| Check | 目的 |
| --- | --- |
| `api-process` | 確認 API process 活著，而且 HTTP pipeline 可以回應 |
| `mlb-stats-api-client` | 確認 MLB Stats API client 有設定 base URL，但不真的呼叫外部服務 |
| `cors-allowed-origins` | 確認後端有載入 CORS allowed origins 設定 |
| `application-insights` | 確認後端 process 有載入 Application Insights connection string |

`/health` 也會回傳目前部署版本：

| Field | 目的 |
| --- | --- |
| `deployment.version` | 目前執行中的 commit SHA |
| `deployment.buildId` | 對應的 Azure DevOps build id |
| `deployment.imageTag` | Container App 目前使用的 image tag |

Pipeline 的 `SmokeTest` stage 會檢查：

```text
GET /health
GET /
CORS response header
```

其中 `/health` 還會用 `grep` 確認 response 裡有：

```text
"status":"healthy"
"api-process"
"mlb-stats-api-client"
"cors-allowed-origins"
"deployment"
"version"
"buildId"
"imageTag"
"application-insights"
```

CORS header 檢查會帶上 Azure Static Web Apps 的 origin，確認後端回傳相同的 `access-control-allow-origin`。

原本的 `/api/games/today` 現在放到 `IntegrationCheck` stage，並設定為 optional。這樣 MLB 外部 API 如果暫時慢或失敗，不會把一次成功部署誤判成後端本身壞掉。

## 必要 Azure DevOps Service Connection

Pipeline 目前預期 Azure DevOps 裡有這個 service connection：

```text
sc-mlb-ai-go-azure
```

目前狀態：已建立，並已授權給 pipelines 使用。

它的用途是讓 Azure Pipeline 可以操作 Azure 資源，例如：

- ACR build
- 更新 Container Apps image
- 查詢 Container Apps revision

建議 scope 先限縮在目前練習用 resource group：

```text
rg-mlb-ai-go-dev
```

## 建立 Service Connection 的方向

在 Azure DevOps 專案裡：

```text
Project settings
  -> Service connections
  -> New service connection
  -> Azure Resource Manager
  -> 選目前 Azure subscription
  -> scope 選 resource group
  -> service connection name 填 sc-mlb-ai-go-azure
```

建立後，確認允許 pipeline 使用這個 service connection。

## 第一次執行 Pipeline 前檢查

1. `azure-pipelines-backend.yml` 和 `azure-pipelines-frontend.yml` 已經 push 到 Azure DevOps repo。
2. Azure DevOps repo 有建立兩條 pipeline，分別指到 backend/frontend YAML。
3. Service connection 名稱是 `sc-mlb-ai-go-azure`。
4. Service connection 有權限操作 `rg-mlb-ai-go-dev`。
5. Frontend pipeline 有設定 secret variable `AZURE_STATIC_WEB_APPS_API_TOKEN`。
6. Pipeline 第一次跑時，如果 Azure DevOps 要求授權使用 service connection，要按 approve。

## 之後進 AKS 的對應概念

目前 Container Apps pipeline 會練到這條主線：

```text
Code -> Build -> Image -> Registry -> Deploy -> Smoke Test
```

之後換成 AKS 時，主線一樣，只是 deploy 動作會從：

```text
az containerapp update
```

改成：

```text
kubectl apply
helm upgrade
```

或其他 Kubernetes deployment 工具。
