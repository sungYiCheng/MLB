# Kubernetes 白話觀念區

最後更新：2026-10-07

這份筆記不以指令為主，而是用生活化方式理解 Kubernetes。先知道每一層為什麼存在，再回頭看 YAML 和 `kubectl`，會容易很多。

## 一張圖先看懂

```text
Azure Subscription
└─ Resource Group
   └─ AKS Cluster
      ├─ Control Plane（管理大腦，Azure 代管）
      └─ Node Pool
         ├─ Node 1（Azure VM）
         │  ├─ Pod A
         │  │  └─ Container
         │  └─ Pod B
         │     ├─ Main Container
         │     └─ Sidecar Container
         └─ Node 2（Azure VM）
            └─ Pod C
               └─ Container
```

最短的記法：

```text
Cluster   = 整套 Kubernetes 環境
Control Plane = 管理與決策的大腦
Node      = 真正提供 CPU、記憶體的機器
Pod       = Kubernetes 排程與生命週期單位
Container = 真正執行程式的 Process
```

## 為什麼需要 Kubernetes

如果只有一個 Container，我們確實可以找一台 VM 直接執行：

```text
VM
└─ MLB API Container
```

但系統開始變大後，會出現很多管理問題：

- Container 掛掉，誰負責重啟？
- 想執行三份 API，誰負責建立？
- 某台 VM 掛掉，應用程式能不能搬家？
- 更新新版時，怎麼避免所有服務一起中斷？
- 哪個 Container 健康，可以接收流量？
- CPU 和記憶體應該分給誰？

Kubernetes 的工作就是持續維持「我們宣告的期望狀態」。

```text
期望：API 應該有 3 個 Pod
實際：目前只有 2 個 Pod
Kubernetes：自動補第 3 個 Pod
```

這種持續比較並修正的過程叫做 `Reconciliation`，白話就是「持續對帳」。

## Image、Container、Pod、Node 的差別

### Image：尚未執行的標準包

Image 裡包含編譯完成的程式、Runtime、檔案與啟動方式。

```text
acrmlbaigo.azurecr.io/mlb-ai-api:aks-lab-30
```

可以把 Image 想成冷凍料理包。內容已經準備好，但尚未加熱，也沒有在提供服務。

### Container：真正執行中的程式

Image 啟動後才變成 Container：

```text
Image
  -> 啟動
  -> Container
```

Container 像料理包加熱後，正在廚房出餐。同一個 Image 可以同時啟動很多份 Container。

### Pod：Kubernetes 的完整工作站

Kubernetes 不直接排程單獨 Container，而是排程 Pod。

我們目前最簡單的情況是：

```text
MLB API Pod
└─ MLB API Container
```

Pod 除了包住 Container，還描述：

- 使用哪個 Image。
- 使用哪些環境變數。
- CPU 與記憶體需求。
- Readiness／Liveness probes。
- Volume。
- Pod IP。

因此 Pod 是「Container 加上 Kubernetes 執行設定」。

### Node：真正提供運算能力的機器

Node 在 AKS 裡基本上是 Azure VM。它提供：

- CPU。
- Memory。
- Disk。
- Container Runtime。
- kubelet。

同一台 Node 可以執行很多 Pod：

```text
Node 1
├─ MLB API Pod
├─ CoreDNS Pod
├─ Gateway Pod
├─ metrics-server Pod
└─ Azure network Pods
```

所以不是「一個 Pod 對應一台 Node」。比較像一棟飯店裡可以有很多房間。

## 為什麼 Pod 和 Container 還要分開

因為有些 Container 必須一起工作。

例如：

```text
API Pod
├─ MLB API Container
└─ Telemetry Sidecar Container
```

兩個 Container 放同一個 Pod 時：

- 一定會被排到同一台 Node。
- 可以使用 `localhost` 溝通。
- 可以共用 Volume。
- 一起建立與刪除。
- 一起擴縮。

生活上像一台救護車：

```text
救護車（Pod）
├─ 駕駛（Container）
└─ 救護人員（Container）
```

兩人工作不同，但必須一起到現場，適合放在同一個 Pod。

### 什麼時候不該放同一個 Pod

假設 API 需要五份，背景 Worker 只需要一份：

```text
API Deployment    -> 5 Pods
Worker Deployment -> 1 Pod
```

如果 API 和 Worker 放在同一個 Pod，API 擴成五份時，Worker 也會變成五份，可能重複抓五次 MLB 資料。

判斷原則：

```text
必須一起執行、一起擴縮
  -> 可以放同一個 Pod

可以獨立更新、獨立擴縮
  -> 分成不同 Pod / Deployment
```

## 稍微複雜的 Kubernetes 系統

假設 MLB 專案成長後有 API、Worker、Admin：

```text
AKS Cluster
├─ Node 1
│  ├─ API Pod 1
│  ├─ Worker Pod 1
│  └─ Kubernetes system Pods
├─ Node 2
│  ├─ API Pod 2
│  ├─ Admin Pod 1
│  └─ Gateway Pod
└─ Node 3
   ├─ API Pod 3
   ├─ Worker Pod 2
   └─ Monitoring Pods
```

這時關係不是 1 對 1：

- 一台 Node 可以執行很多 Pod。
- 一套應用可以有很多相同 Pod。
- 一個 Pod 可以包含很多 Container。
- 一個 Service 可以把流量分給很多 Ready Pod。

## Deployment、ReplicaSet、Pod 怎麼合作

假設我們宣告：

```text
mlb-ai-api replicas = 3
```

關係會是：

```text
Deployment
└─ ReplicaSet
   ├─ API Pod 1
   ├─ API Pod 2
   └─ API Pod 3
```

- Deployment 管理版本、更新策略與期望狀態。
- ReplicaSet 維持指定數量的 Pod。
- Pod 真正執行 Container。

若 Pod 2 被刪除：

```text
期望 replicas = 3
實際 replicas = 2
ReplicaSet -> 自動建立新的 Pod 2
```

這就是 self-healing 的其中一種形式。

## Service 為什麼重要

Pod 名稱與 IP 都可能改變，因此使用者不能直接記住 Pod IP。

Service 像銀行總機：

```text
Service: mlb-ai-api
├─ Ready API Pod 1
├─ Ready API Pod 2
└─ Ready API Pod 3
```

外部只要找 Service，Service 再把 request 交給可用 Pod。

如果 Pod 2 Readiness 失敗：

```text
Pod 1 Ready      -> 可接流量
Pod 2 Not Ready  -> 暫停接流量
Pod 3 Ready      -> 可接流量
```

Service 網址不需要改，只會更新後面的 EndpointSlice。

## Cluster、Control Plane、Node Pool

### Cluster 是不是最上層

在 Kubernetes 範圍內，Cluster 是整套環境的最大邊界：

```text
Cluster
├─ Control Plane
├─ Node Pool
│  ├─ Node 1
│  └─ Node 2
└─ Kubernetes Resources
```

但真正做管理決策的是 Cluster 裡的 Control Plane。

```text
Cluster       = 整套環境
Control Plane = 管理大腦
Node          = 執行工作的機器
```

在 Azure 更高層還有：

```text
Azure Subscription
└─ Resource Group
   └─ AKS Cluster
```

### Node Pool 是什麼

Node Pool 是一組用途與規格相近的 Node。

大型環境可能有：

```text
AKS Cluster
├─ System Node Pool
│  └─ CoreDNS、Gateway、系統 Pods
├─ Application Node Pool
│  └─ API、Worker Pods
└─ GPU Node Pool
   └─ AI 推論 Pods
```

生活上像不同用途的廚房：一般廚房不需要放昂貴 GPU 設備。

## Scheduler 如何選擇 Node

Scheduler 會考慮：

- Pod 要求多少 CPU 和記憶體。
- Node 還有多少可分配資源。
- Pod 是否要求特定類型 Node。
- 是否希望相同 replicas 分散到不同 Node。
- Node 是否健康或正在維護。

例如：

```text
Node 1
├─ API Pod 1
└─ Worker Pod 1

Node 2
├─ API Pod 2
└─ Admin Pod 1

Node 3
└─ API Pod 3
```

如果三個 API Pod 全放同一台 Node，Node 掛掉時三份都會消失。可以透過 topology spread 或 anti-affinity，要求 replicas 儘量分散。

## CPU 與記憶體怎麼分配

每個 Container 可以宣告：

```yaml
resources:
  requests:
    cpu: 100m
    memory: 128Mi
  limits:
    cpu: 250m
    memory: 512Mi
```

生活上可以想成飯店訂房：

```text
requests = 最低保證的訂位
limits   = 最高允許使用量
```

`1000m CPU` 等於一個 CPU core，因此 `100m` 約為 `0.1 CPU`。

### Pod 有多個 Container 時

```text
API Container
requests: 300m CPU / 256Mi

Sidecar Container
requests: 100m CPU / 128Mi
```

Pod 的一般 Container requests 會相加：

```text
Pod requests
CPU:    400m
Memory: 384Mi
```

Scheduler 要找一台至少還能承諾這些資源的 Node。因為整個 Pod 是同一個排程單位，不能把兩個 Container 拆到不同 Node。

## Capacity、Allocatable 與實際使用量

假設 Node 規格是：

```text
Capacity
CPU:    2 vCPU
Memory: 8 GiB
```

作業系統與 Kubernetes 元件也需要資源，因此應用可用的 `Allocatable` 會稍微少一些：

```text
Allocatable
CPU:    約 1.8 vCPU
Memory: 約 6～7 GiB
```

數字只是概念範例，實際值要用 `kubectl describe node` 查看。

Scheduler 主要根據 requests 判斷能否放入，而不是只看 `kubectl top` 的即時使用率。

## Node 有剩餘資源會不會浪費

從 Kubernetes 角度，未必浪費：

- 可以再排入其他 Pod。
- 現有 Container 可以暫時使用超過 request 的 CPU。
- 未使用記憶體可供應用或作業系統快取使用。

但從 Azure 費用角度，如果 Node 沒有其他工作，仍然支付完整 VM 費用。

像租四房公寓但目前只使用一間：

- 其他房間未來仍能使用。
- 當下確實處於閒置。
- 房租仍以整間計算。

所以它不一定是技術上的浪費，但可能是成本上的閒置。

## CPU 和 Memory 超過 Limit 的差別

### CPU 超過 Limit

通常會被限制速度：

```text
CPU throttling
```

程式還活著，但處理速度變慢，像水龍頭被限制流量。

### Memory 超過 Limit

記憶體無法像 CPU 一樣延後使用，Container 可能被終止：

```text
OOMKilled
```

Kubernetes 可能重新啟動它，但若程式持續超過 limit，就會重複被終止。

## Requests 設太高或太低

### 設太高

程式實際只需要 `100m`，卻宣告 `1000m`：

- Scheduler 預留太多容量。
- 新 Pod 可能 Pending。
- 即時 CPU 使用率看起來仍很低。
- 可能被迫增加 Node，造成成本浪費。

### 設太低

程式平常需要 `800m`，卻只宣告 `50m`：

- Scheduler 可能在同一台 Node 塞太多 Pod。
- 多個 Pod 同時搶 CPU。
- API response 變慢。
- Memory 壓力可能造成 OOMKilled。

比較理想的做法是觀察一段時間的實際使用量，再調整 requests 與 limits。

## Pod Autoscaling 與 Node Autoscaling

兩者管理的對象不同：

```text
HPA
  -> 根據負載增加或減少 Pod

Cluster Autoscaler
  -> Pod 放不下時增加 Node
  -> 工作可集中時減少 Node
```

例如：

```text
流量增加
  -> HPA 將 API 從 2 Pods 擴到 8 Pods
  -> 現有 Nodes 放不下
  -> Cluster Autoscaler 增加 Node
```

目前 MLB Lab 還沒有啟用這兩項；API Pod 固定一份，Node 固定兩台。

## 為什麼目前有兩個 Node

最初只有一台 `Standard_D2_v4` Node。啟用 Managed Gateway API 與 Istio 後，第二個 `istiod` Pod 一直 Pending：

```text
0/1 nodes are available: Insufficient cpu
```

原因不是即時 CPU 已用滿，而是所有 Pod 的 CPU requests 加總後，剩餘可承諾容量不足。

因此 Node Pool 從一台擴成兩台：

```text
nodepool1
├─ Node 1: Standard_D2_v4
└─ Node 2: Standard_D2_v4
```

第二台讓 system Pods、Gateway 元件與 MLB API 有更多排程空間，也提高 Node 維護時的可用性；代價是 VM 成本大約增加一倍。

## istiod 到底是什麼

`istiod` 是 Istio 的控制中心，不是 MLB API，也不直接處理 MLB 資料。

可以把 Gateway 系統想成交通系統：

```text
istiod        = 交通控制中心
Gateway       = 高速公路入口
HTTPRoute     = 導航與交流道規則
Gateway Proxy = 現場執行交通管制的人員
Service       = 目的地大樓總機
Pod           = 真正提供服務的辦公室
```

### Control Plane 與 Data Plane

```text
istiod
  -> Control Plane
  -> 讀取規則、產生設定、下達設定

Gateway Proxy
  -> Data Plane
  -> 真正接收、判斷與轉送 request
```

`istiod` 會監看：

- Gateway。
- HTTPRoute。
- Service。
- EndpointSlice。
- Pod。
- 網路與憑證設定。

接著把規則轉換成 Gateway Proxy 能執行的設定。

## 套用 Gateway YAML 後發生什麼

```text
1. kubectl 將 Gateway / HTTPRoute 送到 Kubernetes API Server
2. API Server 保存期望狀態
3. istiod 發現設定變更
4. istiod 將規則翻譯成 Proxy 設定
5. istiod 把設定送給 Gateway Proxy
6. Gateway Proxy 監聽 80 / 443
7. 外部 request 被送到 mlb-ai-api Service
8. Service 選擇 Ready MLB API Pod
```

一般 request 不會流經 `istiod`：

```text
Browser
  -> Gateway Proxy
  -> Service
  -> MLB API Pod
```

不是：

```text
Browser
  -> istiod
  -> MLB API Pod
```

所以 `istiod` 是大腦，Gateway Proxy 才是實際處理流量的手腳。

## 為什麼需要兩個 istiod Pod

AKS Managed Gateway API 讓 `istiod` 保持多個 replicas，目的是高可用性：

```text
istiod Pod 1 故障
  -> istiod Pod 2 繼續提供控制功能
```

既有 Gateway Proxy 通常能繼續使用已經收到的設定，但如果所有 `istiod` 都不可用：

- 新設定可能無法下發。
- 新 Proxy 可能拿不到設定。
- Service endpoints 變化可能無法即時同步。

因此它不在每個 request 的路徑上，仍然是重要系統元件。

## 我們有啟用完整 Service Mesh 嗎

沒有。目前狀態是：

```text
serviceMeshProfile.mode = Disabled
```

因此 MLB API Pod 裡沒有 Envoy sidecar。我們主要使用 Istio 的 Managed Gateway API／Application Routing 能力，讓它管理對外入口。

完整 Service Mesh 還可以提供：

- Service-to-service mTLS。
- Retry、timeout、circuit breaking。
- 流量比例與 canary routing。
- 每個服務之間的 telemetry。

這些屬於後續進階主題。

## 目前專案的完整流量

```text
Browser
  -> Azure Static Web Apps
  -> HTTPS 20-24-106-104.sslip.io
  -> Azure Public IP / Load Balancer
  -> Gateway Proxy
  -> HTTPRoute
  -> Service mlb-ai-api:80
  -> Ready MLB API Pod:8080
  -> MLB API Container
```

控制流則是：

```text
Gateway / HTTPRoute YAML
  -> Kubernetes API Server
  -> istiod
  -> Gateway Proxy 設定
```

「使用者流量」和「管理設定流」是兩條不同路徑，這是理解 Istio 最重要的地方之一。

## 常見誤解

### Running 不等於 Ready

`Running` 只表示 Container process 正在執行。`Ready` 才表示它通過 readiness probe，可以接收 Service 流量。

### Pod 不是 VM

Pod 是 Kubernetes 邏輯與排程單位，真正的 CPU／Memory 來自 Node VM。

### Requests 不是實際使用量

Requests 是排程時的資源承諾。`kubectl top` 顯示的是當下實際使用量。

### AKS Free 不代表整套免費

Free tier 主要指 AKS control plane 定價層；Node VM、disk、Load Balancer、Public IP 與資料量仍可能計費。

### istiod 不是 Ingress 流量本身

`istiod` 管理 Proxy 設定，Gateway Proxy 才真正接收外部 request。

## 複習問題

1. 為什麼 Kubernetes 不直接管理 Container，而要再包一層 Pod？
2. 什麼程式適合放在同一個 Pod？
3. 為什麼 Pod IP 改變後，前端網址不需要修改？
4. Node 有剩餘資源時，技術上與成本上有什麼不同？
5. CPU limit 與 memory limit 超過時，結果有什麼差別？
6. 為什麼 `kubectl top` 很低，Scheduler 還是可能回報 `Insufficient cpu`？
7. HPA 和 Cluster Autoscaler 各自管理什麼？
8. Cluster 與 Control Plane 的差別是什麼？
9. `istiod` 和 Gateway Proxy 分別負責什麼？
10. 為什麼一般使用者 request 不會經過 `istiod`？
