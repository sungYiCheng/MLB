# Kubernetes / AKS Prep

這個資料夾先放 AKS 前的 Kubernetes manifest 雛形，目標是學會 Container Apps 設定如何對應到 Kubernetes。

目前不建議直接拿去 production。這是一組 dev / learning base manifests。

## Files

```text
k8s/
  base/
    namespace.yaml
    backend-configmap.yaml
    backend-secret.example.yaml
    backend-deployment.yaml
    backend-service.yaml
  overlays/
    gateway/
      kustomization.yaml
      backend-gateway.yaml
      backend-http-route.yaml
    gateway-tls/
      kustomization.yaml
      letsencrypt-cluster-issuer.yaml
      backend-certificate.yaml
      gateway-tls-patch.yaml
      http-route-tls-patch.yaml
```

## Current Mapping

| Azure Container Apps | Kubernetes / AKS |
| --- | --- |
| Container App | Deployment |
| Container App revision | ReplicaSet / rollout revision |
| Container image | Deployment container image |
| Environment variables | ConfigMap / Secret |
| External ingress | Service + Gateway + HTTPRoute |
| Scale min/max replicas | Deployment replicas / HPA |
| `/health` smoke test | readinessProbe / livenessProbe |
| ACR image pull | AKS kubelet identity with AcrPull |
| Application Insights connection string | Secret |

## Apply Order

沒有 Ingress 的低成本預設部署：

```powershell
kubectl apply -k k8s/base
```

需要練習公開 Gateway 時才套用 overlay：

```powershell
kubectl apply -k k8s/overlays/gateway
```

目前 `backend-secret.example.yaml` 是範本。真的部署前，不要把真實 connection string commit 進 repo。可以用這種方式建立 secret：

```powershell
kubectl create secret generic mlb-ai-api-secrets `
  --namespace mlb-ai-go `
  --from-literal=APPLICATIONINSIGHTS_CONNECTION_STRING="<connection-string>" `
  --from-literal=ApplicationInsights__ConnectionString="<connection-string>"
```

`backend-secret.example.yaml` 不包含在 `kustomization.yaml` 裡，避免不小心把 placeholder secret 套進 cluster。真的需要 Application Insights 時，先用上面的 `kubectl create secret` 建立真實 Secret，再套用其他 manifests。

## Important Notes

- `k8s/base` 不包含 Gateway，避免一般練習一開始就公開 API；AKS 本身仍可能保留受控的 outbound Load Balancer 與 Public IP。
- `k8s/overlays/gateway` 使用 AKS 管理的 `approuting-istio` GatewayClass，套用前需啟用 Managed Gateway API 與 Application Routing Istio。
- 目前 HTTPRoute 沒有限制 hostname，方便學習時直接用 public IP 驗證。正式環境應再加上 DNS 與 TLS。
- `backend-deployment.yaml` 目前使用 `acrmlbaigo.azurecr.io/mlb-ai-api:dev-latest`。正式部署時更建議改成 commit SHA tag。
- AKS 要能拉 ACR image，需要讓 AKS kubelet identity 或 managed identity 有 ACR 的 `AcrPull` 權限。
## Free HTTPS overlay

`overlays/gateway-tls` 在 Gateway API 上加入免費的 HTTPS Lab 設定：

- `20-24-106-104.sslip.io` 將 hostname 解析到目前 Gateway Public IP。
- cert-manager 透過 Let’s Encrypt HTTP-01 取得並自動續期憑證。
- Gateway port `443` 負責 TLS termination，再把 HTTP 流量送到 ClusterIP Service。
- HTTP port `80` 仍保留，供 ACME HTTP-01 challenge 使用。

這個 hostname 與目前 Public IP 綁定。若刪除並重建 Gateway、Public IP 改變，必須同步更新 overlay 與 `infra/aks-lab/variables.ps1`。
