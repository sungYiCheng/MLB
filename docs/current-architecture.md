# MLB AI GO 目前架構總覽

最後更新：2026-10-21

## 圖例與閱讀規則

這份文件採用常見的多視角架構表達方式：每張圖只回答一種問題，不把部署、流量與資源所有權混在一起。

| 圖形表示 | 固定意義 |
| --- | --- |
| 實線箭頭 | 執行時的 HTTP 請求、回應或 Telemetry 資料流 |
| 虛線箭頭 | 部署、管理、設定注入、選取或所有權；不是使用者流量 |
| 有標題的外框 | Azure Subscription、Resource Group、Cluster、Namespace 等資源邊界 |
| 綠色節點 `[ACTIVE]` | 現在承接正式流量 |
| 灰色節點 `[STOPPED]` | 資源仍保留，但目前不提供運算或流量服務 |

外框只回答「這個資源放在哪裡或由誰管理」，箭頭才表示元件之間的關係。不要把框線當成網路流向。

## View 1：System Context（系統全貌）

這張圖只看使用者實際會經過哪些系統，不展開 Azure 與 Kubernetes 內部細節。

```mermaid
flowchart LR
    User([使用者])
    SWA[Azure Static Web Apps<br/>Angular 前端<br/>ACTIVE]
    CA[Azure Container Apps<br/>.NET API<br/>ACTIVE]
    MLB[MLB Stats API]
    AOAI[Azure OpenAI]
    Monitor[Application Insights]
    AKS[AKS Lab<br/>Kubernetes 練習環境<br/>STOPPED]

    User -->|1. HTTPS| SWA
    SWA -->|2. HTTPS API + CORS| CA
    CA -->|3a. 比賽資料| MLB
    CA -->|3b. AI 摘要| AOAI
    CA -->|3c. Telemetry| Monitor

    classDef actor fill:#dbeafe,stroke:#2563eb,color:#172554
    classDef active fill:#dcfce7,stroke:#15803d,color:#052e16,stroke-width:2px
    classDef external fill:#ffedd5,stroke:#c2410c,color:#431407
    classDef observe fill:#ede9fe,stroke:#7c3aed,color:#2e1065
    classDef stopped fill:#f3f4f6,stroke:#6b7280,color:#374151,stroke-width:2px

    class User actor
    class SWA,CA active
    class MLB,AOAI external
    class Monitor observe
    class AKS stopped
```

AKS 在圖上沒有接到實線，代表它目前不是正式網站的流量路徑；這比用一條含糊的虛線表示「可能會走」更精確。

## View 2：Azure Resource Topology（資源拓撲）

這張圖只回答「資源放在哪個邊界、由誰部署與管理」。所有箭頭都是管理關係，因此統一使用虛線，不代表 HTTP 流量。

```mermaid
flowchart TB
    Dev[Developer / Local Git]
    GitHub[GitHub<br/>sungYiCheng/MLB]

    subgraph DevOpsBoundary[Azure DevOps Organization: bbshare7788]
        Repos[Azure Repos<br/>MLB_AI_GO]
        FrontCI[Frontend Pipeline]
        BackCI[Backend Pipeline]
        AksCI[AKS Lab Pipeline]
    end

    subgraph Subscription[Azure Subscription Boundary: Azure subscription 1]
        subgraph DevRG[Resource Group Boundary: rg-mlb-ai-go-dev]
            SWA[Static Web Apps]
            ACR[Container Registry]
            CAE[Container Apps Environment]
            CA[Container App]
            AOAI[Azure OpenAI + Deployment]
            Appi[Application Insights]
            LAW[Log Analytics Workspace]
            PullID[Managed Identity<br/>ACR pull]
        end

        subgraph AksRG[Resource Group Boundary: rg-mlb-ai-go-aks-lab]
            AKS[AKS Cluster<br/>STOPPED]
            ControlID[Control plane identity]
            KubeletID[Kubelet identity]
        end

        subgraph ManagedRG[AKS Managed Resource Group Boundary: MC_rg-mlb-ai-go-aks-lab_aks-mlb-ai-go-lab_eastasia]
            VMSS[VM Scale Set<br/>2 x Standard_D2_v4]
            Net[VNet / NSG / Load Balancer]
            PIP[Gateway Public IP]
            Disks[Managed OS Disks]
        end
    end

    Dev -. push .-> GitHub
    Dev -. push .-> Repos
    Repos -. triggers .-> FrontCI
    Repos -. triggers .-> BackCI
    Repos -. manual trigger .-> AksCI
    FrontCI -. deploys .-> SWA
    BackCI -. builds image .-> ACR
    BackCI -. deploys revision .-> CA
    AksCI -. start / deploy / stop .-> AKS
    AksCI -. builds AKS image .-> ACR
    CAE -. hosts .-> CA
    PullID -. grants image pull .-> CA
    Appi -. stores logs in .-> LAW
    ControlID -. assigned to .-> AKS
    KubeletID -. AcrPull role .-> ACR
    AKS -. Azure creates and manages .-> VMSS
    AKS -. Azure creates and manages .-> Net
    AKS -. Azure creates and manages .-> PIP
    AKS -. Azure creates and manages .-> Disks

    classDef delivery fill:#e0f2fe,stroke:#0369a1,color:#082f49
    classDef active fill:#dcfce7,stroke:#15803d,color:#052e16
    classDef control fill:#ede9fe,stroke:#7c3aed,color:#2e1065
    classDef infra fill:#ffedd5,stroke:#c2410c,color:#431407
    classDef stopped fill:#f3f4f6,stroke:#6b7280,color:#374151

    class Dev,GitHub,Repos,FrontCI,BackCI,AksCI delivery
    class SWA,ACR,CAE,CA,AOAI,Appi,LAW,PullID active
    class ControlID,KubeletID control
    class VMSS,Net,PIP,Disks infra
    class AKS stopped
```

### 白話版：現在到底誰在做什麼

| 元件 | 最直白的工作 |
| --- | --- |
| Static Web Apps | 把 Angular 畫面送到使用者瀏覽器 |
| Container Apps | 目前正式執行 .NET API，不需要自己管理 VM |
| MLB Stats API | 提供比賽、球隊與球員的原始資料 |
| Azure OpenAI | 把後端整理好的 MLB 資料寫成每日摘要 |
| Memory Cache | 15 分鐘內重複使用摘要，減少等待與 Token 費用 |
| ACR | 存放 Backend 的 Docker Image |
| Azure DevOps | 自動測試、建置 Image、部署與驗證 |
| Application Insights | 記錄 API 是否正常、速度多快、哪裡失敗 |
| AKS | Kubernetes 練習環境，目前停止，不承接正式流量 |

## 使用者打開網站時

```mermaid
sequenceDiagram
    participant U as 使用者
    participant F as Angular / Static Web Apps
    participant B as .NET / Container Apps
    participant M as MLB Stats API
    participant A as Azure OpenAI

    U->>F: 開啟公開前端
    F->>B: GET /api/ai/daily-briefing
    B->>M: 取得賽程、戰績、外卡、排行榜
    M-->>B: MLB 真實資料
    alt 15 分鐘內已有快取
        B-->>F: 回傳快取摘要
    else 尚無快取
        B->>A: 傳送受限制的 MLB grounding data
        A-->>B: Structured JSON 摘要
        B->>B: 驗證格式並快取 15 分鐘
        B-->>F: 回傳 AI 摘要
    end
    F-->>U: 顯示每日摘要
```

如果 Azure OpenAI 逾時、限流或回傳不合法，Backend 會改用 deterministic preview，不讓整個頁面失效。

## CI/CD 部署流程

```mermaid
flowchart TD
    Commit[Commit / Push main]
    Repos[Azure Repos]

    Commit --> Repos
    Repos --> Backend[Backend Pipeline]
    Repos --> Frontend[Frontend Pipeline]

    Backend --> DotnetTest[dotnet restore / build / test]
    DotnetTest --> ACRBuild[ACR 雲端建置 Docker image]
    ACRBuild --> DeployCA[更新 Container Apps revision]
    DeployCA --> BackendSmoke[Health / CORS / MLB / AI smoke tests]

    Frontend --> NpmTest[npm ci / unit tests / production build]
    NpmTest --> DeploySWA[部署 Static Web Apps]
    DeploySWA --> FrontendSmoke[Frontend smoke / E2E tests]
```

GitHub 是另一份遠端備份與公開原始碼位置；目前 Azure DevOps Pipeline 由 Azure Repos 的 `main` 觸發。

## AKS Lab 的位置

```mermaid
flowchart LR
    ACR[Azure Container Registry]

    subgraph AKSLab[AKS Lab，目前停止以節省費用]
        Gateway[Gateway API / HTTPS]
        Service[ClusterIP Service]
        Deployment[Kubernetes Deployment]
        Pod1[API Pod]
        Node[Node VM]

        Gateway --> Service
        Service --> Pod1
        Deployment --> Pod1
        Pod1 --> Node
    end

    ACR -.重新啟動後拉取 image.-> Pod1
```

AKS 已完成建立、Pod 部署、Service、Gateway API、TLS、擴縮、rolling update 與 rollback 練習。目前 AKS 停止，所以正式前端不會打到 Pod，而是走 Container Apps。重新啟動 AKS 後，可以再將 production environment 的 API URL 切回 AKS 公開 HTTPS endpoint。

## 目前 Azure 資源分工

| 資源 | 目前用途 |
| --- | --- |
| Static Web Apps | 免費／低成本託管 Angular 靜態前端 |
| Container Apps | 目前正式執行 .NET Backend API |
| Container Registry | 保存 Pipeline 建好的 Backend Docker images |
| Azure OpenAI | 將 MLB 真實資料整理成每日 AI 摘要 |
| Application Insights | 記錄 API request、錯誤、延遲及外部 dependency |
| Log Analytics | 集中查詢與保存 Azure logs |
| Azure DevOps Pipelines | 自動測試、建置、部署及 smoke test |
| AKS | Kubernetes 學習環境，目前停止節省 Node VM 成本 |

## 目前最重要的兩條路

正式網站執行路徑：

```text
Browser -> Static Web Apps -> Container Apps -> MLB Stats API / Azure OpenAI
```

程式部署路徑：

```text
Local Git -> GitHub + Azure Repos -> Azure Pipelines -> ACR / Static Web Apps / Container Apps
```

