# MLB AI GO 目前架構總覽

最後更新：2026-10-20

## 一張圖看目前架構

```mermaid
flowchart LR
    User[使用者瀏覽器]

    subgraph Source[原始碼與 CI/CD]
        LocalGit[Local Git<br/>main branch]
        GitHub[GitHub<br/>sungYiCheng/MLB]
        AzureRepos[Azure Repos<br/>MLB_AI_GO]
        BackendPipeline[Backend Pipeline<br/>Test / Build / Deploy]
        FrontendPipeline[Frontend Pipeline<br/>Test / Build / Deploy]
    end

    subgraph AzureDev[Azure dev 環境]
        SWA[Static Web Apps<br/>Angular 前端]
        CA[Container Apps<br/>.NET Backend API]
        ACR[Container Registry<br/>Backend images]
        AOAI[Azure OpenAI<br/>gpt-5-mini-briefing]
        AppInsights[Application Insights]
        Logs[Log Analytics]
        Secret[Container Apps Secret<br/>Azure OpenAI API key]
    end

    MLB[MLB Stats API]

    User -->|HTTPS| SWA
    SWA -->|HTTPS + CORS| CA
    CA -->|取得比賽資料| MLB
    CA -->|grounded prompt| AOAI
    Secret -.安全注入.-> CA
    CA -->|requests / errors / dependencies| AppInsights
    AppInsights --> Logs

    LocalGit -->|push main| GitHub
    LocalGit -->|push main| AzureRepos
    AzureRepos --> BackendPipeline
    AzureRepos --> FrontendPipeline
    BackendPipeline -->|ACR cloud build| ACR
    ACR -->|image| CA
    FrontendPipeline -->|Angular build| SWA

    classDef person fill:#dbeafe,stroke:#2563eb,color:#172554
    classDef source fill:#f3f4f6,stroke:#4b5563,color:#111827
    classDef compute fill:#dcfce7,stroke:#16a34a,color:#052e16
    classDef data fill:#ffedd5,stroke:#ea580c,color:#431407
    classDef observe fill:#ede9fe,stroke:#7c3aed,color:#2e1065
    classDef secret fill:#fef3c7,stroke:#d97706,color:#451a03

    class User person
    class LocalGit,GitHub,AzureRepos,BackendPipeline,FrontendPipeline source
    class SWA,CA,ACR compute
    class MLB,AOAI data
    class AppInsights,Logs observe
    class Secret secret
```

## 詳細 Azure 資源關聯圖

```mermaid
flowchart TB
    Developer[Developer / Local Git]
    GitHub[GitHub remote]
    Repos[Azure Repos]
    BackendCI[Backend CI/CD]
    FrontendCI[Frontend CI/CD]
    AksCI[AKS Lab Pipeline<br/>manual trigger]
    User[Browser]
    MLB[MLB Stats API]

    subgraph DevRG[rg-mlb-ai-go-dev]
        SWA[Static Web Apps<br/>Angular]
        ACR[Container Registry<br/>Docker images]
        CAE[Container Apps Environment]
        CA[Container App<br/>.NET API]
        OAI[Azure OpenAI Account]
        Model[gpt-5-mini-briefing]
        AppSecret[Container Apps Secret<br/>OpenAI API key]
        Appi[Application Insights]
        LAW[Log Analytics Workspace]
        AcrIdentity[Managed Identity<br/>ACR pull]
    end

    subgraph AksRG[rg-mlb-ai-go-aks-lab]
        AKS[AKS Cluster<br/>Stopped]
        ControlID[Control plane identity]
        KubeletID[Kubelet identity]
    end

    subgraph ManagedRG[AKS managed MC resource group]
        VMSS[VM Scale Set<br/>2 x Standard_D2_v4]
        VNet[VNet / NSG]
        LB[Standard Load Balancer]
        PublicIP[Gateway Public IP]
        Disks[Managed OS disks]
    end

    subgraph K8s[Kubernetes resources, retained while stopped]
        GW[Gateway API + TLS]
        SVC[ClusterIP Service]
        DEP[Deployment / ReplicaSet]
        POD[API Pod]
        CFG[ConfigMap / Secret]
    end

    Developer --> GitHub
    Developer --> Repos
    Repos --> BackendCI
    Repos --> FrontendCI
    Repos --> AksCI

    BackendCI -->|test + ACR build| ACR
    BackendCI -->|deploy revision| CA
    FrontendCI -->|Angular build| SWA
    AksCI -.start / deploy / stop.-> AKS
    AksCI -.AKS image build.-> ACR

    User -->|active HTTPS path| SWA
    SWA -->|active API path| CA
    CA --> MLB
    CA --> OAI
    OAI --> Model
    AppSecret -.secret reference.-> CA
    AcrIdentity -.pull image.-> CA
    CA --> Appi
    Appi --> LAW

    ControlID --> AKS
    AKS --> VMSS
    VMSS --> VNet
    VMSS --> Disks
    KubeletID -.AcrPull.-> ACR
    LB --> PublicIP

    AKS -.controls.-> K8s
    PublicIP -.AKS Running 時.-> GW
    GW -.-> SVC
    SVC -.-> POD
    DEP -.creates.-> POD
    CFG -.env.-> POD
    POD -.-> MLB
    POD -.telemetry.-> Appi

    classDef person fill:#dbeafe,stroke:#2563eb,color:#172554
    classDef delivery fill:#f3f4f6,stroke:#4b5563,color:#111827
    classDef active fill:#dcfce7,stroke:#16a34a,color:#052e16
    classDef service fill:#ffedd5,stroke:#ea580c,color:#431407
    classDef observe fill:#ede9fe,stroke:#7c3aed,color:#2e1065
    classDef lab fill:#f3f4f6,stroke:#9ca3af,color:#374151

    class Developer,User person
    class GitHub,Repos,BackendCI,FrontendCI,AksCI delivery
    class SWA,ACR,CAE,CA active
    class OAI,Model,AppSecret,AcrIdentity service
    class Appi,LAW observe
    class AKS,ControlID,KubeletID,VMSS,VNet,LB,PublicIP,Disks,GW,SVC,DEP,POD,CFG lab
```

實線是目前正式環境正在使用的路徑；虛線是 AKS Lab 已建立但因 Cluster 停止而暫時不處理流量的路徑。

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

