# Azure Infra Notes

這個資料夾保存 MLB AI Daily 的 Azure dev 環境建置腳本。目標是把目前用 Azure Portal / Azure CLI 建出來的資源，整理成可以審閱、重跑、學習的形式。

目前先使用 Azure CLI + PowerShell，而不是直接上 Terraform 或 Bicep。原因是這個階段比較適合看懂每個 Azure 指令實際建立了什麼；之後進 AKS 前，可以再把這一層改成 Bicep 或 Terraform。

## 目前 dev 架構

```text
GitHub / Azure Repos
        |
        v
Azure DevOps Pipelines
        |
        +--> Backend pipeline
        |       |
        |       +--> ACR Tasks build image
        |       +--> Azure Container Registry
        |       +--> Azure Container Apps
        |       +--> Application Insights / Log Analytics
        |       +--> Smoke test /health, /, CORS
        |
        +--> Frontend pipeline
                |
                +--> Angular build
                +--> Azure Static Web Apps
                +--> Smoke test + Playwright E2E

Browser
   |
   +--> Azure Static Web Apps frontend
              |
              +--> Azure Container Apps backend API
                         |
                         +--> Application Insights
                         |
                         +--> Log Analytics Workspace
                         |
                         +--> MLB Stats API
```

## Azure resources

| Resource | Current dev value | Purpose |
| --- | --- | --- |
| Resource group | `rg-mlb-ai-go-dev` | 把這次練習相關 Azure 資源集中管理 |
| Log Analytics Workspace | `log-mlb-ai-go-dev` | 收集 Container Apps logs 與查詢資料 |
| Application Insights | `appi-mlb-ai-api-dev` | 觀察 backend request、error、latency 與 telemetry |
| Azure Container Registry | `acrmlbaigo` | 存放 backend container image |
| Container Apps environment | `cae-mlb-ai-go-dev` | Container Apps 的執行環境 |
| Container App | `ca-mlb-ai-api` | 執行 .NET backend API |
| Static Web App | `swa-mlb-ai-go-dev` | 部署 Angular frontend |
| User-assigned identity | `id-mlb-ai-go-acr-pull` | 讓 backend app 可以用受控身分拉 ACR image |

## Files

```text
infra/azure/
  README.md              # 這份說明
  queries/               # Log Analytics / Application Insights KQL 查詢範本
  variables.dev.ps1      # dev 環境參數
  provision-dev.ps1      # 建立或更新 dev Azure 資源
```

根目錄另有：

```text
azure-pipelines-infra.yml
```

這條 pipeline 預設不會自動觸發，需要手動執行。先用來練習 infra provisioning flow。

## Dry run

先在本機看腳本會做哪些事：

```powershell
.\infra\azure\provision-dev.ps1 -PlanOnly
```

指定 subscription：

```powershell
.\infra\azure\provision-dev.ps1 -SubscriptionId "<subscription-id>" -PlanOnly
```

## Apply

真的建立或更新 Azure dev 資源：

```powershell
.\infra\azure\provision-dev.ps1 -SubscriptionId "<subscription-id>"
```

## What this script does

`provision-dev.ps1` 會按照順序處理：

1. 選擇 Azure subscription。
2. 註冊需要的 Azure resource providers。
3. 建立 resource group。
4. 建立 ACR。
5. 建立 user-assigned managed identity。
6. 建立 Log Analytics Workspace。
7. 建立 workspace-based Application Insights。
8. 指派 `AcrPull` 權限給 managed identity。
9. 建立 Azure Container Apps environment，並接到指定 Log Analytics Workspace。
10. 用 ACR Tasks 從 `backend/Dockerfile` build backend image。
11. 建立或更新 backend Container App，並注入 Application Insights connection string。
12. 建立 Static Web App shell。
13. 如果傳入 `-EnableAlertRules`，建立學習用 Azure Monitor alert rules。

## Observability

目前 dev 環境會建立兩個觀測用資源：

| Resource | 用途 |
| --- | --- |
| Log Analytics Workspace | 查 Container Apps console logs、平台 logs、KQL 查詢 |
| Application Insights | 看 backend HTTP requests、failures、performance、dependencies |

Backend 會透過這個環境變數接上 Application Insights：

```text
APPLICATIONINSIGHTS_CONNECTION_STRING
ApplicationInsights__ConnectionString
```

這個值由 infra script 或 backend pipeline 從 Azure 查出後注入 Container App，不會寫進 repo。

後端 `/health` 會顯示：

```json
{
  "name": "application-insights",
  "status": "configured"
}
```

這只表示 backend process 有拿到 Application Insights connection string，不代表外部 request 一定已經進入 Azure Monitor。要確認實際 telemetry，可以到 Azure Portal 的 Application Insights resource 查看 requests、failures 或 live metrics。

KQL 查詢範本放在：

```text
infra/azure/queries
```

Alert rules 預設不建立。先看 dry run：

```powershell
.\infra\azure\provision-dev.ps1 -PlanOnly -EnableAlertRules
```

真的建立：

```powershell
.\infra\azure\provision-dev.ps1 -EnableAlertRules
```

目前 alert rules 不綁定 action group，所以會建立規則但不會寄信。

## Important notes

- Static Web Apps 的 deployment token 不會寫進 repo。
- 這份腳本不會設定 Azure DevOps secret variable；`AZURE_STATIC_WEB_APPS_API_TOKEN` 仍然要放在 Azure DevOps pipeline variables。
- 這份腳本目前針對 `dev` 環境。未來可以新增 `variables.prod.ps1` 或改成 Bicep/Terraform parameters。
- 如果要停止 backend 成本，可以把 Container App min replicas 保持 `0`。ACR 和 Log Analytics 仍可能有少量儲存或資料擷取費用。
