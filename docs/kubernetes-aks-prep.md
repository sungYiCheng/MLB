# Kubernetes / AKS Prep Notes

這份筆記用來銜接目前 Azure Container Apps 版本與未來 AKS 版本。

## Why This Step

目前後端跑在 Azure Container Apps。Container Apps 幫我們包掉很多 Kubernetes 細節，例如 revision、ingress、scale、container runtime。

進 AKS 後，這些東西要拆開理解：

```text
Deployment -> 管 Pod 怎麼跑、跑幾份、用哪個 image
Pod        -> 真正跑 container 的最小單位
Service    -> 在 cluster 裡給 Pod 一個穩定入口
Ingress    -> 讓外部 HTTP/HTTPS 流量進到 Service
ConfigMap  -> 非敏感設定
Secret     -> connection string、token 這類敏感設定
Probe      -> Kubernetes 判斷 container 是否 ready / alive
```

## Container Apps To AKS Mapping

| 目前 Container Apps | 未來 AKS |
| --- | --- |
| `ca-mlb-ai-api` | `Deployment/mlb-ai-api` |
| Container image | `spec.template.spec.containers[].image` |
| `Cors__AllowedOrigins__0` | `ConfigMap/mlb-ai-api-config` |
| `ApplicationInsights__ConnectionString` | `Secret/mlb-ai-api-secrets` |
| External ingress | `Service` + `Ingress` |
| `/health` pipeline smoke test | `readinessProbe` + `livenessProbe` |
| Container App revision | Deployment rollout revision |

## Manifest Files

```text
k8s/base/namespace.yaml
```

建立 `mlb-ai-go` namespace，把這個練習專案的 Kubernetes resources 集中在一起。

```text
k8s/base/backend-configmap.yaml
```

放非敏感設定，例如：

```text
ASPNETCORE_ENVIRONMENT
Cors__AllowedOrigins__0
```

```text
k8s/base/backend-secret.example.yaml
```

敏感設定範本。真實 connection string 不應該 commit。

```text
k8s/base/backend-deployment.yaml
```

描述 backend container 怎麼跑。包含：

- image
- port 8080
- ConfigMap / Secret env
- readinessProbe
- livenessProbe
- CPU / memory requests and limits

```text
k8s/base/backend-service.yaml
```

在 cluster 內建立穩定入口。Service 會把 port 80 轉到 backend container 的 8080。

```text
k8s/base/backend-ingress.yaml
```

描述外部 HTTP request 要怎麼進到 service。目前 host 是 placeholder：

```text
api.mlb-ai-go.local
```

## Why Service Is Needed

Pod 會被重建，IP 會變，所以不能直接依賴 Pod IP。

Service 提供穩定名稱：

```text
mlb-ai-api.mlb-ai-go.svc.cluster.local
```

Ingress 再把外部流量導到這個 Service。

## Why Probes Matter

目前 backend 有：

```text
GET /health
```

在 Kubernetes 裡可以用它做：

| Probe | 用途 |
| --- | --- |
| readinessProbe | 判斷 Pod 能不能接流量 |
| livenessProbe | 判斷 Pod 是否卡死，需要重啟 |

這跟 CI/CD 的 smoke test 類似，但 probes 是 Kubernetes 在 runtime 持續做的健康判斷。

## Before Real AKS Deployment

真的部署前還需要：

1. 建立 AKS cluster。
2. 讓 AKS 有權限拉 ACR image。
3. 安裝 ingress controller。
4. 決定 domain / DNS。
5. 建立真實 Secret。
6. 把 image tag 從 `dev-latest` 改成 commit SHA 或 pipeline build tag。
