# AKS Lab Runbook

這份操作手冊描述低成本、純 Azure 的 AKS 練習流程。本機不需要 Docker Desktop；Docker image 由 ACR Tasks 在 Azure 建置，AKS 操作由 Azure DevOps hosted agent 執行。

## Resource Boundary

現有環境與練習環境刻意分開：

```text
rg-mlb-ai-go-dev                  長期保留
  ACR                             共用 image registry
  Application Insights           共用 API telemetry
  Static Web App                  現有 frontend
  Container App                   現有 backend

rg-mlb-ai-go-aks-lab              免費 bootstrap 容器，保留
  User Assigned control identity  保留
  User Assigned kubelet identity  保留並持有 AcrPull
  AKS Free tier cluster           每日可拋棄

MC_rg-mlb-ai-go-aks-lab_*         AKS 自動建立，每日可拋棄
  VM scale set
  32 GiB managed OS disk
  Network resources
  Managed outbound networking     AKS 預設可包含 Standard Load Balancer / Public IP
  Public application endpoint     只有啟用 Ingress 時才建立
```

空的 Lab Resource Group、User Assigned Identities 與 RBAC assignments 本身不產生運算費用，因此會保留，讓權限可以限制在 Lab 範圍，不必把 Azure DevOps Service Principal 升成整個訂閱的 Owner。AKS node Resource Group 不能預先存在，必須由 AKS Resource Provider 自動建立。

`Destroy` 只允許刪除 AKS cluster 與 AKS 自動建立的 node Resource Group，不會刪除 bootstrap group、identities 或 `rg-mlb-ai-go-dev`。

## Pipeline

Pipeline YAML：

```text
azure-pipelines-aks-lab.yml
```

管理腳本與參數：

```text
infra/aks-lab/bootstrap-aks-lab.ps1
infra/aks-lab/manage-aks-lab.ps1
infra/aks-lab/variables.ps1
```

`bootstrap-aks-lab.ps1` 由有訂閱權限的管理者執行一次，建立一個空的 Lab Resource Group、兩個 identities 與最小範圍的 RBAC。日常 Pipeline 不需要訂閱層級 Owner 權限。

第一次使用時，在 Azure DevOps 建立一條新 pipeline，選擇 repository 內現有 YAML，路徑設為：

```text
/azure-pipelines-aks-lab.yml
```

Pipeline 沿用 Azure Resource Manager service connection：

```text
sc-mlb-ai-go-azure
```

## Operations

### Plan

只列出預定的資源與設定，不修改 Azure。每次調整 IaC 後先跑這個操作。

### Preflight

執行 Azure 與 repository 的唯讀檢查：

- 顯示 Azure DevOps service connection 實際登入的帳號與訂閱。
- 確認 `Microsoft.Compute`、`Microsoft.ContainerService` 與 `Microsoft.Network` providers 已註冊。
- 確認共用 ACR 存在。
- 確認 `Standard_B2s` 可在 East Asia 列出。
- 顯示 East Asia regional vCPU 與 B-series quota。
- 確認 bootstrap Resource Group 與兩個 identities 都存在。
- 確認 Lab 與共享 Resource Group 名稱不同。
- 使用 `kubectl kustomize` 渲染 base 與 ingress overlay。

Preflight 不建立或修改 AKS 資源。

### Create

建立：

- AKS Free tier cluster `aks-mlb-ai-go-lab`
- 一台 `Standard_B2s` system node
- 32 GiB managed OS disk
- Azure CNI Overlay 網路
- OIDC issuer 與 workload identity
- 使用預先授權的 kubelet identity 從既有 ACR 拉 image

Create 不會部署應用程式，也不會安裝 NGINX Ingress。

### Deploy

預設會：

1. 使用 ACR Tasks 在 Azure 建置 backend image。
2. 從 AKS 動態取得 admin kubeconfig；憑證只存在 hosted agent 的暫存環境。
3. 從現有 Application Insights 讀取 connection string。
4. 在 AKS 建立 Kubernetes Secret，不把 connection string 寫入 Git。
5. 套用 Namespace、ConfigMap、Deployment 和 ClusterIP Service。
6. 使用本次 pipeline build tag 更新 Deployment image。
7. 等待 rollout 完成。
8. 在 cluster 內建立一次性 curl Pod，呼叫 `/health` smoke test。

`includeIngress` 預設為 `false`。因此一般 Deploy 不會替 API 建立公開入口，但 AKS 本身仍可能保留受控的 Standard Load Balancer 與 Public IP，供 cluster outbound traffic 使用。

需要練習 Ingress 時才把 `includeIngress` 設為 `true`。Pipeline 會額外安裝：

- NGINX Ingress Controller
- 對外的 `LoadBalancer` Service 與 public frontend
- Ingress 使用的 Public IP
- `k8s/overlays/ingress` 內的 Ingress resource

### Stop

執行 `az aks stop`，停止 control plane 和 node compute。適合當天中途暫停、稍後還要繼續時使用。

### Start

執行 `az aks start`，恢復前一次保存的 cluster state。啟動完成後才能再次 Deploy 或使用 `kubectl`。

### Status

顯示 cluster 是否存在，以及目前的 power state、provisioning state、Kubernetes version 和 node resource group。

### Destroy

刪除 AKS cluster 以及它管理的 VM、磁碟、Load Balancer、Public IP 等計費資源。必須同時把 `confirmDestroy` 設為 `true`，否則 pipeline 會拒絕執行。

Destroy 的順序：

1. 刪除 `aks-mlb-ai-go-lab` cluster。
2. AKS 清除並刪除它自動建立的 node Resource Group，包括 VM、磁碟和網路資源。
3. 保留空的 Lab bootstrap group、兩個 identities 和最小權限 RBAC。
4. 保留共用的 ACR、Application Insights、前端與 Container App。

## Recommended Daily Flow

```text
Plan
  -> Preflight
  -> Create
  -> Deploy (includeIngress=false)
  -> Kubernetes practice
  -> Stop / Start during long breaks
  -> Deploy (includeIngress=true) only for the Ingress lesson
  -> Destroy with confirmDestroy=true at the end of the day
```

每天 Destroy 後，下次 Create 通常需要重新等待 AKS provisioning。程式碼、image、telemetry、manifests、bootstrap identities 與權限都保留在 AKS 之外。

## Why The Frontend Is Not Switched Yet

目前 Static Web App 仍然呼叫既有 Container App。AKS Lab 是獨立驗證環境，刪除 AKS 不會讓目前網站中斷。

等 Ingress、DNS 與憑證流程穩定後，再新增一個 AKS 專用 frontend environment 或動態 API URL；不要先把目前 frontend 寫死到每天會改變的 Public IP。

## Local Plan Check

不登入 Azure也可以先確認參數：

```powershell
.\infra\aks-lab\manage-aks-lab.ps1 -Operation Plan
```

這個命令不建立任何 Azure 資源。
