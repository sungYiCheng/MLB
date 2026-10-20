# AI Daily Briefing 學習筆記

最後更新：2026-10-20

## 這一階段完成了什麼

Daily Briefing 已從固定規則的 preview 升級成真正的 Azure OpenAI 功能：

```text
Angular Daily Briefing
  -> GET /api/ai/daily-briefing
  -> .NET API 平行取得賽程、戰績、外卡與數據領先者
  -> 整理成受限制的 grounding data
  -> Azure OpenAI gpt-5-mini structured output
  -> 驗證成 DailyBriefingDto
  -> 快取 15 分鐘
  -> 前端顯示摘要
```

正常成功時：

```text
mode = azure-openai
isAiGenerated = true
```

Azure OpenAI 未設定、逾時、限流或回傳格式不合法時，API 會回到原本的 preview。這叫做 graceful degradation。AI 是加值功能，不應因為模型暫時不可用而讓整個 MLB 網站一起失效。

## Azure 上新增的資源

| 項目 | 設定 |
| --- | --- |
| Resource group | `rg-mlb-ai-go-dev` |
| Azure OpenAI account | `oai-mlb-ai-go-dev-7788` |
| Region | Japan East |
| Model deployment | `gpt-5-mini-briefing` |
| Model/version | `gpt-5-mini` / `2025-08-07` |
| Deployment type | GlobalStandard |
| TPM capacity | 50K tokens/minute |
| REST API version | `2024-10-21` |

TPM capacity 是每分鐘能處理多少 token 的速率上限，不是預購一台固定收月租的 AI 主機。費用仍主要依實際輸入與輸出 token 計算。15 分鐘快取會避免每位訪客刷新頁面都重複付一次模型費用。

## Container Apps 設定

正式後端 `ca-mlb-ai-api` 使用以下環境變數：

```text
AzureOpenAI__Endpoint
AzureOpenAI__DeploymentName
AzureOpenAI__ApiVersion
AzureOpenAI__ApiKey=secretref:azure-openai-api-key
```

雙底線 `__` 是 .NET 環境變數表示巢狀設定的方式。例如 `AzureOpenAI__DeploymentName` 等同於設定檔中的 `AzureOpenAI:DeploymentName`。

API key 沒有寫進 Git、YAML 或一般環境變數值，而是存在 Container Apps secret `azure-openai-api-key`，程式只透過 secret reference 讀取。

## 程式如何限制 AI

`AzureOpenAiDailyBriefingService` 不會讓模型自行上網或自由猜測。它只接收後端已經從 MLB Stats API 取得的資料：

- 最多 10 場當日比賽。
- 各分區第一名。
- 美聯、國聯外卡前五名。
- 各主要數據前三名。

System prompt 明確要求模型只能使用提供的資料，不得捏造比分、球員、傷勢、預測或其他事實。輸出使用 JSON Schema structured output，欄位固定為 `headline`、`summary`、`keyGames`、`playersToWatch`。

這種「先找可信資料，再讓模型整理」的方式就是 grounded generation。它比單純問模型「今天 MLB 發生什麼」可靠得多。

## 快取與成本

第一次呼叫會取得 MLB 資料並請模型生成，實測約 6 秒。成功結果會放入後端記憶體快取 15 分鐘；第二次呼叫實測約 0.9 秒，且 `generatedAtUtc` 相同，證明沒有再次呼叫模型。

目前是單一 Container App instance，所以 memory cache 很適合練習。如果未來擴成多個 instance，每台都有自己的快取，屆時可改用 Azure Cache for Redis 或資料庫共用結果。

## Health Check 與 CI/CD

`GET /health` 新增 `azure-openai` 檢查：

- `configured`：必要設定都有提供。
- `preview-fallback`：未設定模型，功能會使用 preview。

Backend pipeline 也會確認 `/health` 包含 `azure-openai`，並呼叫 `/api/ai/daily-briefing` 檢查 `mode` 與 `headline`。Health check 本身只驗證設定是否存在，不會每次都花 token 呼叫模型。

## 這次遇到的問題

### 模型版本與配額

原先嘗試的舊模型正在淘汰，新模型又不一定在每個區域都有可用 quota。最後先查 Japan East 的 subscription quota，再選擇可部署的 `gpt-5-mini`。

### 新模型參數不同

`gpt-5-mini` 使用 `max_completion_tokens`，不接受舊的 `max_tokens`；也不接受自訂 `temperature=0.2`。因此改用預設 temperature，並設定 `reasoning_effort=minimal`。

### 429 限流

10K TPM 對完整 MLB grounding request 太緊，Azure 回傳 HTTP 429。部署容量提高到 50K TPM 後可正常生成。程式仍保留 preview fallback，避免限流時整個 endpoint 失敗。

### 環境變數名稱不一致

程式讀取的是 `AzureOpenAI:DeploymentName`，最初測試時卻使用 `AzureOpenAI__Deployment`。少了 `Name` 會讓程式判定設定不完整。修正成 `AzureOpenAI__DeploymentName` 後，health 顯示 `configured`，API 正常回傳 `azure-openai`。

## 本機測試方式

本機若不提供 Azure OpenAI 設定，網站仍可正常使用 preview。需要測真實模型時，提供以下環境變數後啟動 API：

```text
AzureOpenAI__Endpoint
AzureOpenAI__DeploymentName
AzureOpenAI__ApiVersion
AzureOpenAI__ApiKey
```

不要把 key 寫進 `appsettings.json`、commit、截圖或筆記。可由 Azure CLI 臨時取得並只放在目前 shell process 的環境變數中。

## 目前學到的 AI 工程觀念

- Model deployment：應用程式呼叫自己的部署名稱，不直接呼叫抽象模型名稱。
- Grounding：模型只根據可信來源資料產生文字。
- Structured output：用 schema 限制 JSON 形狀，降低前後端整合失敗。
- Token/TPM：token 是使用量，TPM 是每分鐘吞吐上限。
- Cache：減少延遲、重複 token 費用與限流機率。
- Fallback：模型失效時回到可預測規則，維持產品可用性。
- Secret management：敏感金鑰留在 Azure secret，不進入原始碼。
