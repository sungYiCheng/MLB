# AI Daily Briefing 學習筆記

## 這一階段的目標

這一階段先建立一條完整、可測試的 AI 功能管線，但暫時不呼叫付費模型：

```text
Angular Daily Briefing 區塊
  -> GET /api/ai/daily-briefing
  -> .NET API 同時取得賽程、戰績與數據領先者
  -> DailyBriefingPreviewService 組成固定格式
  -> 前端顯示重點賽事、關注球員與資料說明
```

目前回應中的 `mode` 是 `preview`，`isAiGenerated` 是 `false`。這代表內容由可預測的程式規則產生，不是假裝成 AI 的文字。

## 為什麼先做 Preview

正式串接模型之前，先把 API 契約、畫面、錯誤處理、單元測試與 E2E 做好，可以將問題分開：

- 畫面壞掉時，先查前端，不必懷疑模型。
- JSON 格式錯誤時，先查 API 契約。
- Azure OpenAI 尚未建立時，功能仍可在本機測試。
- 未來更換模型時，前端不必跟著重寫。

這種作法稱為先建立 integration boundary。模型是後端的一個實作細節，不應直接控制整個前端結構。

## 新增的後端元件

`DailyBriefingDto.cs`

- 定義前後端共同依賴的 JSON 形狀。
- 包含 headline、summary、keyGames、playersToWatch、dataNotes 與 generatedAtUtc。
- `isAiGenerated` 讓畫面可以誠實區分 preview 與真正模型結果。

`DailyBriefingPreviewService.cs`

- 輸入真實 MLB 賽程、戰績與排行榜資料。
- 以固定規則挑選最多三場比賽及四位領先球員。
- 不呼叫外部生成式 AI，因此容易測試，也不產生 token 費用。

`GET /api/ai/daily-briefing`

- 平行取得三類 MLB 資料，縮短等待時間。
- MLB API 無法使用時回傳 HTTP 503。
- MLB API 逾時時回傳 HTTP 504。
- 成功時回傳固定的 `DailyBriefingDto`。

## 測試

後端新增兩個單元測試：

1. 有賽事時，確認比賽、先發投手與領先球員會進入固定格式。
2. 沒有賽事時，確認仍能回傳合法且容易理解的空狀態。

前端新增 E2E 測試，確認 Daily Briefing 可以收合與重新展開。整體驗證結果：

- 後端單元測試：25 個通過。
- 前端單元測試：7 個通過。
- Playwright E2E：4 個通過。
- Production build：成功。
- 桌面與 390px 手機寬度：沒有文字重疊或水平溢位。

## 下一階段如何換成真正 AI

下一步不需要改前端契約，而是在後端加入模型提供者：

```text
DailyBriefing endpoint
  -> 收集可信 MLB 資料
  -> 組成受限制的 prompt
  -> Azure OpenAI structured output
  -> 驗證輸出符合 DailyBriefingDto
  -> 成功：isAiGenerated = true
  -> 失敗：回退 preview，網站仍可使用
```

正式串接時還需要：

- Azure OpenAI 或 Microsoft Foundry 模型部署。
- Endpoint、deployment name 與 API key／Managed Identity 設定。
- Timeout、retry、每日快取與 token 上限。
- Application Insights 記錄延遲、token、模型版本與失敗原因。
- 不把金鑰、完整 prompt 或敏感資料寫進 Git 與 log。

這個階段的重點不是讓模型寫一段漂亮文字，而是建立一個即使模型失敗，產品仍能正常運作的 AI 功能。
