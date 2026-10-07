# 專案筆記

這個資料夾用來放 MLB AI Daily side project 的學習紀錄、部署紀錄和架構筆記。

## 筆記

- [練習紀錄](./learning-record.md)：目前完整的練習過程，包含本機設定、Git 流程、Azure 部署、成本筆記和架構圖。
- [目前 Azure 狀態](./azure-current-state.md)：目前 Azure 上已建立的資源、前後端部署方式、CI/CD 流程與 Ingress 概念。
- [Azure DevOps CI/CD 筆記](./azure-devops-cicd.md)：目前前後端分離 pipelines、service connection、部署驗證與未來 AKS 對應觀念。
- [設定管理筆記](./configuration-management.md)：local/dev/prod 設定方式、CORS allowed origins、Container Apps env vars 與未來 AKS ConfigMap/Secret 對應。
- [觀測筆記](./observability.md)：Application Insights、Log Analytics、KQL 查詢範本與 Azure Monitor alert rule 規劃。
- [Kubernetes / AKS 準備筆記](./kubernetes-aks-prep.md)：Container Apps 到 AKS 的概念對應、Deployment、Service、Ingress、ConfigMap、Secret 與 probes。
- [AKS Lab 操作手冊與實作紀錄](./aks-lab-runbook.md)：純 Azure、無本機 Docker 的操作流程，以及前置資源、RBAC、Preflight 與故障排除紀錄。
- [Kubernetes Runtime 實作筆記](./kubernetes-runtime-lab.md)：目前 AKS runtime 架構、部署流程、資源關係、網路、probes、self-healing 與常用指令。
- [AKS Gateway API 實作筆記](./gateway-api-lab.md)：Managed Gateway API、Istio application routing、容量問題、Gateway/HTTPRoute、Public IP 與外部 smoke test。
- [AKS 免費 HTTPS 實作筆記](./aks-https-tls.md)：sslip.io、Let’s Encrypt、cert-manager、Gateway TLS termination 與驗證流程。
