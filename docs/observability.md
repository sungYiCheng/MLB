# Observability Notes

這份筆記記錄 MLB AI Daily 目前的 Azure 觀測方式。目標是先學會三件事：

- 知道服務有沒有流量
- 知道 API 是否變慢或失敗
- 知道之後要怎麼設定 alert

## Resources

目前 dev 環境使用：

| Resource | Name | 用途 |
| --- | --- | --- |
| Application Insights | `appi-mlb-ai-api-dev` | 看 API requests、failures、duration、dependencies |
| Log Analytics Workspace | `log-mlb-ai-go-dev` | 用 KQL 查詢 telemetry 與 Container Apps logs |
| Container App | `ca-mlb-ai-api` | 後端 .NET API |

## Where To Look

Azure Portal 裡可以從這幾個入口看：

- Application Insights -> Performance
- Application Insights -> Failures
- Application Insights -> Logs
- Log Analytics Workspace -> Logs
- Container App -> Log stream
- Container App -> Revisions

## Common KQL

查最近一小時 requests：

```kql
AppRequests
| where TimeGenerated > ago(1h)
| summarize Count=count(), AvgDurationMs=avg(DurationMs) by Name, Success
| order by Count desc
```

查失敗 request：

```kql
AppRequests
| where TimeGenerated > ago(1h)
| where Success == false or ResultCode startswith "5"
| project TimeGenerated, Name, ResultCode, DurationMs, Url
| order by TimeGenerated desc
```

查慢 request：

```kql
AppRequests
| where TimeGenerated > ago(1h)
| where DurationMs > 3000
| project TimeGenerated, Name, ResultCode, DurationMs, Url
| order by DurationMs desc
```

查 backend health check：

```kql
AppRequests
| where TimeGenerated > ago(1h)
| where Name == "GET /health"
| summarize Count=count(), LastSeen=max(TimeGenerated), AvgDurationMs=avg(DurationMs)
```

查 Container Apps console logs：

```kql
ContainerAppConsoleLogs_CL
| where TimeGenerated > ago(1h)
| where ContainerAppName_s == "ca-mlb-ai-api"
| project TimeGenerated, Log_s
| order by TimeGenerated desc
```

## Alert Rule Plan

目前 infra script 已放入 alert rule 骨架，但預設不建立：

```powershell
.\infra\azure\provision-dev.ps1 -PlanOnly -EnableAlertRules
```

真的建立 alert rules：

```powershell
.\infra\azure\provision-dev.ps1 -EnableAlertRules
```

目前規劃三個學習用 alert rules：

| Alert | 條件 | 目的 |
| --- | --- | --- |
| `alert-mlb-ai-api-5xx-dev` | 最近 5 分鐘 5xx request count > 0 | API 有 server error |
| `alert-mlb-ai-api-slow-requests-dev` | 平均 request duration > 3000 ms | API 變慢 |
| `alert-mlb-ai-api-health-missing-dev` | 最近 10 分鐘沒有 `/health` request | health probe 或流量可能中斷 |

目前沒有設定 action group，所以 alert rule 可以被建立與學習，但不會發 email。等查詢和規則都熟了，再補 email action group。

## Learning Notes

Application Insights 比較像「應用程式視角」：

- 哪個 endpoint 被打
- 是否成功
- 花多久
- 有沒有 exception

Log Analytics 比較像「查詢引擎」：

- 用 KQL 查歷史 telemetry
- 查 Container App logs
- 讓 alert rules 可以用查詢條件觸發

之後進 AKS 時會延伸到：

- Pod logs
- Container restart
- Ingress request
- Service latency
- Kubernetes events
