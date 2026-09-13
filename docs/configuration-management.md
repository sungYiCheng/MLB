# 設定管理筆記

這份筆記記錄目前 MLB AI Daily 的環境設定方式。目標是保持簡單，只分成：

- `local`
- `dev`
- `prod`

## 為什麼要做設定管理

不同環境會有不同網址、權限、金鑰或外部服務設定。這些值如果全部寫死在程式碼裡，之後切換環境會很麻煩，也比較容易把開發設定帶到正式環境。

目前先從 CORS 開始練習，因為它剛好會碰到本機前端、Azure 前端、Azure 後端之間的跨來源 request。

## CORS 設定

後端現在會讀取：

```text
Cors:AllowedOrigins
```

如果沒有設定任何 allowed origin，後端不會回傳允許跨來源呼叫的 CORS header。

## Local

本機開發設定在：

```text
backend/src/MlbAi.Api/appsettings.Development.json
```

目前允許：

```text
http://127.0.0.1:53180
http://localhost:53180
```

這讓本機 Angular dev server 可以呼叫本機 backend API。

## Dev

Azure dev 環境目前使用 Container Apps environment variable：

```text
Cors__AllowedOrigins__0=https://yellow-forest-04081e300.5.azurestaticapps.net
```

這個設定會由 Azure DevOps pipeline 部署時寫入 Container Apps。

## Prod

目前還沒有正式 production domain。

未來如果有正式前端網址，可以使用同樣方式設定：

```text
Cors__AllowedOrigins__0=https://正式前端網址
```

如果有多個網址，可以繼續往下加：

```text
Cors__AllowedOrigins__1=https://第二個允許網址
Cors__AllowedOrigins__2=https://第三個允許網址
```

## 與 AKS 的對應

目前 Container Apps 用 environment variables。

之後進 AKS 時，概念會對應到：

```text
ConfigMap -> 非敏感設定
Secret    -> 密碼、token、connection string
Deployment env -> 將 ConfigMap/Secret 注入 container
```

這次 CORS 屬於非敏感設定，未來放 AKS 時比較像 ConfigMap。

## Pipeline 新增的檢查

目前 Azure Pipeline 的 `SmokeTest` stage 會檢查：

```text
GET /health
GET /
CORS header
```

CORS header 檢查會帶上 Azure Static Web Apps 的 origin：

```text
Origin: https://yellow-forest-04081e300.5.azurestaticapps.net
```

並確認後端回傳：

```text
access-control-allow-origin: https://yellow-forest-04081e300.5.azurestaticapps.net
```

這代表 Azure 前端可以從瀏覽器呼叫 Azure 後端。
