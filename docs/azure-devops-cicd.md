# Azure DevOps CI/CD 筆記

這份筆記記錄目前 MLB AI Daily 的 Azure DevOps pipeline 練習。

## 目前目標

先完成後端 CI/CD，不急著進 AKS。

目前 pipeline 要做到：

```text
push 到 Azure DevOps main
  -> dotnet restore/build
  -> ACR cloud build backend image
  -> deploy image 到 Azure Container Apps
  -> smoke test Azure backend endpoint
```

## Pipeline 檔案

主要檔案：

```text
azure-pipelines.yml
```

目前 stages：

| Stage | 目的 |
| --- | --- |
| `Validate` | 還原、編譯並執行 .NET backend unit tests |
| `BuildImage` | 用 Azure Container Registry cloud build 建立 backend image |
| `DeployBackend` | 將新 image 部署到 Azure Container Apps |
| `SmokeTest` | 驗證 Azure backend `/` 與 `/api/games/today` 可以回應 |

目前 `DeployBackend` 先使用一般 job，不使用 Azure DevOps environment gate。等基本 CI/CD 跑順後，再加 environment approval 會比較適合練正式 release flow。

## Unit Test 位置

目前第一個測試專案：

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

1. `azure-pipelines.yml` 已經 push 到 Azure DevOps repo。
2. Azure DevOps repo 有建立 pipeline，來源指到 `azure-pipelines.yml`。
3. Service connection 名稱是 `sc-mlb-ai-go-azure`。
4. Service connection 有權限操作 `rg-mlb-ai-go-dev`。
5. Pipeline 第一次跑時，如果 Azure DevOps 要求授權使用 service connection，要按 approve。

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
