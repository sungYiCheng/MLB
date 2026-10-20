# AKS 目前完整架構

最後更新：2026-10-20

這份文件描述 AKS Lab 現在真正在 Azure 上的設定、停止前最後一次成功部署的 Kubernetes 資源，以及尚未補齊的部分。

## 先看目前狀態

### Azure 現在可確認的狀態

| 項目 | 目前值 |
| --- | --- |
| Resource Group | `rg-mlb-ai-go-aks-lab` |
| AKS Cluster | `aks-mlb-ai-go-lab` |
| Region | East Asia |
| Power state | `Stopped` |
| Provisioning state | `Succeeded` |
| Kubernetes version | `1.35` |
| AKS tier | Free |
| Node pool | `nodepool1`, System mode |
| Node count | 2 |
| Node VM | `Standard_D2_v4` |
| OS disk | 每個 Node 32 GiB |
| Network | Azure CNI Overlay |
| Outbound | Standard Load Balancer |
| Identity | User-assigned identities |
| OIDC issuer | Enabled |
| Workload Identity | Enabled |

AKS 停止後，Node VM 不執行，Pod、Service 與 Gateway 也不能處理流量。Kubernetes 設定仍保留，重新 Start 後 control plane 和 node pool 會恢復。

### 停止前最後一次成功部署

| Kubernetes 資源 | 最後已知狀態 |
| --- | --- |
| Namespace | `mlb-ai-go` |
| Deployment | `mlb-ai-api`, 1 replica |
| Container | `mlb-ai-api`, port 8080 |
| Service | `mlb-ai-api`, ClusterIP port 80 |
| GatewayClass | `approuting-istio` |
| Gateway | `mlb-ai-api-gateway` |
| HTTPRoute | `mlb-ai-api`, PathPrefix `/` |
| TLS hostname | `20-24-106-104.sslip.io` |
| Certificate | `mlb-ai-api-tls` |
| Certificate issuer | `letsencrypt-prod` |
| Backend image | ACR 中的 `mlb-ai-api:aks-lab-<BuildId>` |

Public IP 與 hostname 是停止前的最後已知值。Cluster 或 Gateway 重建後 IP 可能改變，必須重新核對，不能永久假設仍是 `20.24.106.104`。

## 完整 AKS 架構圖

```mermaid
flowchart TB
    Browser[Browser]
    SWA[Azure Static Web Apps<br/>Angular frontend]
    MLB[MLB Stats API]
    Insights[Application Insights]
    ACR[Azure Container Registry<br/>acrmlbaigo]
    DevOps[Azure DevOps<br/>AKS Lab Pipeline]

    subgraph LabRG[Resource Group: rg-mlb-ai-go-aks-lab]
        AKS[AKS managed control plane<br/>Free tier]
        CPID[Control plane identity]
        KubeletID[Kubelet identity<br/>AcrPull]
    end

    subgraph NodeRG[Managed Resource Group: MC_rg-mlb-ai-go-aks-lab_aks-mlb-ai-go-lab_eastasia]
        LB[Standard Load Balancer]
        PIP[Gateway Public IP]
        VNET[VNet / NSG<br/>Azure CNI Overlay]
        VMSS[VM Scale Set<br/>nodepool1]
        Node1[Node 1<br/>Standard_D2_v4]
        Node2[Node 2<br/>Standard_D2_v4]
        Disk1[32 GiB OS Disk]
        Disk2[32 GiB OS Disk]
    end

    subgraph SystemNS[System namespaces]
        Istiod[aks-istio-system<br/>istiod control plane]
        GatewayProxy[Managed Gateway proxy<br/>generated workload]
        CertManager[cert-manager<br/>controller / webhook]
        CoreDNS[kube-system<br/>CoreDNS / metrics / CSI / CNI]
    end

    subgraph AppNS[Namespace: mlb-ai-go]
        GatewayClass[GatewayClass<br/>approuting-istio]
        Gateway[Gateway<br/>HTTP 80 / HTTPS 443]
        HTTPRoute[HTTPRoute<br/>hostname + PathPrefix /]
        Certificate[Certificate<br/>mlb-ai-api-tls]
        TLSSecret[Secret<br/>mlb-ai-api-tls]
        ConfigMap[ConfigMap<br/>environment + CORS]
        AppSecret[Secret<br/>Application Insights]
        Deployment[Deployment<br/>replicas: 1]
        ReplicaSet[ReplicaSet]
        Pod[Pod]
        Container[.NET API container<br/>port 8080]
        Readiness[Readiness probe<br/>GET /health]
        Liveness[Liveness probe<br/>GET /health]
        Service[ClusterIP Service<br/>port 80 to 8080]
        Endpoint[EndpointSlice<br/>Ready Pod IP]
    end

    DevOps -->|create / start / stop / deploy| AKS
    DevOps -->|ACR cloud build| ACR
    CPID --> AKS
    KubeletID -->|pull image| ACR

    AKS --> VMSS
    VMSS --> Node1
    VMSS --> Node2
    VNET --> VMSS
    Disk1 --> Node1
    Disk2 --> Node2

    PIP --> LB
    LB --> GatewayProxy
    Istiod -->|programs| GatewayProxy
    GatewayClass --> Gateway
    Gateway --> GatewayProxy
    GatewayProxy --> HTTPRoute
    HTTPRoute --> Service
    Service --> Endpoint
    Endpoint --> Pod

    Deployment --> ReplicaSet
    ReplicaSet --> Pod
    Pod --> Container
    ConfigMap -.envFrom.-> Container
    AppSecret -.envFrom.-> Container
    Readiness --> Container
    Liveness --> Container
    Container --> MLB
    Container --> Insights

    CertManager --> Certificate
    Certificate --> TLSSecret
    TLSSecret -.TLS termination.-> Gateway

    Browser -.AKS Running 時 HTTPS.-> PIP
    SWA -.若切回 AKS endpoint.-> PIP
```

圖上的虛線代表「需要 AKS Running 或設定切換後才會生效」。目前正式前端走 Container Apps，不走 AKS。

## 流量如何進到 Pod

```mermaid
sequenceDiagram
    participant User as Browser
    participant DNS as sslip.io DNS
    participant LB as Azure Load Balancer
    participant GW as Managed Gateway proxy
    participant Route as HTTPRoute
    participant Svc as ClusterIP Service
    participant Pod as Ready API Pod
    participant MLB as MLB Stats API

    User->>DNS: 查詢 20-24-106-104.sslip.io
    DNS-->>User: Gateway Public IP
    User->>LB: HTTPS :443
    LB->>GW: 轉送連線
    GW->>GW: 使用 TLS Secret 解密
    GW->>Route: 比對 hostname 與 PathPrefix /
    Route->>Svc: backendRef mlb-ai-api:80
    Svc->>Pod: targetPort 8080
    Pod->>MLB: 取得比賽資料
    MLB-->>Pod: JSON
    Pod-->>User: API response
```

### 為什麼不是直接連 Pod

Pod 名稱與 IP 都可能因重建而改變。Service 使用 label selector 找到 Ready Pod，對內提供固定 DNS：

```text
mlb-ai-api.mlb-ai-go.svc.cluster.local
```

Gateway 和 HTTPRoute 只需要指向 Service，不需要知道 Pod 現在叫什麼或在哪台 Node。

## 應用程式部署關係

```mermaid
flowchart LR
    Deployment[Deployment<br/>期望 1 個 replica]
    RS[ReplicaSet<br/>維持 Pod 數量]
    Pod[Pod<br/>排程單位]
    Container[Container<br/>.NET API]

    Deployment --> RS
    RS --> Pod
    Pod --> Container

    Config[ConfigMap] -.非敏感設定.-> Container
    Secret[Secret] -.敏感設定.-> Container
    Kubelet[kubelet] -->|probes / restart| Container
    Scheduler[Scheduler] -->|選擇 Node| Pod
```

目前 Container 資源限制：

| 設定 | CPU | Memory |
| --- | ---: | ---: |
| Request | 100m | 128 MiB |
| Limit | 250m | 512 MiB |

- Request 是 Scheduler 安排 Pod 時保證預留的最低需求。
- Limit 是 Container 最多能使用的上限。
- Readiness 失敗時 Pod 不接 Service 流量。
- Liveness 連續失敗時 kubelet 重啟 Container。

目前應用 Deployment 固定為 1 replica，尚未建立應用自己的 HPA 或 PDB。Managed Gateway 產生的 proxy 資源可能有平台管理的 HPA/PDB，但那不是 MLB API 的自動擴縮。

## TLS 架構

```mermaid
flowchart LR
    LetsEncrypt[Let's Encrypt]
    Issuer[ClusterIssuer<br/>letsencrypt-prod]
    Cert[Certificate<br/>mlb-ai-api-tls]
    Secret[Secret<br/>mlb-ai-api-tls]
    Gateway[Gateway HTTPS listener<br/>:443]
    Service[Service<br/>HTTP :80]
    Pod[Pod<br/>HTTP :8080]

    Issuer --> LetsEncrypt
    Cert --> Issuer
    LetsEncrypt -->|HTTP-01 validation| Gateway
    Cert -->|stores certificate| Secret
    Secret --> Gateway
    Gateway -->|TLS termination| Service
    Service --> Pod
```

TLS termination 發生在 Gateway。瀏覽器到 Gateway 是 HTTPS；Gateway 解密後，在 Cluster 內以 HTTP 呼叫 Service。

## Identity 與權限

### Control plane identity

`id-mlb-ai-go-aks-control` 供 AKS control plane 操作需要的 Azure 資源。

### Kubelet identity

`id-mlb-ai-go-aks-kubelet` 供 Node/kubelet 使用，目前最重要的權限是從 `acrmlbaigo` 拉取 private image。

### Azure DevOps service connection

`sc-mlb-ai-go-azure` 讓 AKS Lab Pipeline 可以操作 Azure、取得 kubeconfig、建立 image、套用 manifests 與執行 smoke test。

### Workload Identity

Cluster 已啟用 OIDC issuer 與 Workload Identity，但 MLB API Pod 目前仍沒有 Kubernetes ServiceAccount + Federated Credential，所以應用程式尚未使用 Workload Identity 存取 Azure OpenAI 或 Key Vault。

## AKS Pipeline 做了什麼

AKS pipeline 是手動執行，`trigger: none`，支援：

| Operation | 用途 |
| --- | --- |
| Plan | 顯示預計使用的設定與成本相關資源 |
| Preflight | 驗證 quota、工具、manifest 與 Azure 權限 |
| Create | 建立 identities、AKS、node pool、Gateway API 能力 |
| Deploy | ACR build、Kubernetes deploy、rollout、smoke test |
| Stop | 停止 cluster 與 Node VM 計費主體 |
| Start | 恢復既有 cluster |
| Status | 查詢目前狀態 |
| Destroy | 刪除 AKS Lab 資源 |

```mermaid
sequenceDiagram
    participant Dev as Developer
    participant Pipeline as Azure DevOps
    participant ACR as ACR Tasks
    participant AKS as AKS API Server
    participant K8s as Kubernetes Controllers
    participant GW as Gateway

    Dev->>Pipeline: Run operation=Deploy
    Pipeline->>ACR: Build mlb-ai-api:aks-lab-BuildId
    ACR-->>Pipeline: Immutable image
    Pipeline->>AKS: Apply Namespace and Kustomize overlay
    Pipeline->>AKS: Create/update App Insights Secret
    Pipeline->>AKS: Set image and build metadata
    AKS->>K8s: Reconcile Deployment / ReplicaSet / Pod
    K8s-->>Pipeline: Rollout completed
    Pipeline->>AKS: Run temporary curl smoke Pod
    Pipeline->>GW: Verify Gateway / certificate / public health
    GW-->>Pipeline: HTTP 200
```

## Kustomize 三層設定

```text
k8s/base
  Namespace + ConfigMap + Deployment + ClusterIP Service

k8s/overlays/gateway
  base + Gateway + HTTPRoute

k8s/overlays/gateway-tls
  gateway + ClusterIssuer + Certificate + HTTPS patches
```

這讓同一套應用可以依練習階段選擇：

1. 只有 Cluster 內部 API。
2. 加上公開 HTTP Gateway。
3. 加上免費 HTTPS 與憑證。

## 已完成與尚未完成

### 已完成

- 兩台 Node 的 AKS cluster。
- Azure CNI Overlay 與 Standard Load Balancer。
- ACR private image pull。
- Namespace、Deployment、ReplicaSet、Pod、Service。
- ConfigMap 與 Kubernetes Secret。
- Readiness、liveness、內部 smoke test。
- Self-healing、scale、rolling update、rollback 實驗。
- Managed Gateway API、Istio gateway controller。
- sslip.io、Let's Encrypt、cert-manager 與 HTTPS。
- Start、Stop、Status、Destroy 自動化。

### 尚未補到 AKS

- Azure OpenAI endpoint、deployment 與 API key 尚未注入 AKS。
- AKS API 目前會對 AI briefing 使用 preview fallback。
- API Deployment 尚未建立 HPA。
- API Deployment 尚未建立 PDB。
- Pod 尚未使用 Workload Identity 存取 Azure 服務。
- 尚未將 Secret 改成 Key Vault + Secrets Store CSI Driver。
- Application Insights 已接入，但 Gateway access log 與更完整 AKS monitoring 還可加強。

## 重新啟動後應先檢查

```bash
az aks start \
  --resource-group rg-mlb-ai-go-aks-lab \
  --name aks-mlb-ai-go-lab

az aks get-credentials \
  --resource-group rg-mlb-ai-go-aks-lab \
  --name aks-mlb-ai-go-lab \
  --admin \
  --overwrite-existing

kubectl get nodes
kubectl get pods -A
kubectl get all -n mlb-ai-go
kubectl get gateway,httproute,certificate -n mlb-ai-go
```

不要先假設原本 Public IP、TLS hostname 與 certificate 狀態仍有效；Start 後應重新查詢並執行外部 `/health` 測試。

