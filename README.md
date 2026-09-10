# VideoDownloader

![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)
![Avalonia](https://img.shields.io/badge/Avalonia-12.1.2-8B5CF6.svg)
![Platform](https://img.shields.io/badge/Platform-Windows-0078D4.svg)
![Version](https://img.shields.io/badge/Version-1.0.0-blue.svg)
![Status](https://img.shields.io/badge/Status-Stable-10B981.svg)

使用 C#、.NET 10 與 Avalonia 12.1.2 製作的 Windows 桌面影音下載工具，以 yt-dlp 取得來源資訊與下載媒體，並使用 FFmpeg 合併影片或轉換音訊。

目前為 Windows 1.0.0 首個正式版本，採亮色雙欄介面，已具備下載佇列、下載紀錄、取消下載、使用者設定保存與下載工具自動準備。主視窗預設高度為 920，內容在較小螢幕或較高縮放比例時可垂直捲動，不會裁切底部操作按鈕。

文件更新：2026-09-10。最新封裝已包含頁首 Logo、About 關於視窗、完成提示與紀錄操作修正、Threads 分享連結解析、Instagram 輪播選取及 X 純音訊辨識修正，版本維持 `1.0.0`。

一般使用者可安裝 `VideoDownloader-v1.0.0-Setup-x64.exe`，不需另裝 .NET 或 Inno Setup。開發者的建置方式與交付路徑見下方「建置與發布」。

## 已完成功能

- 貼上網址、檢查 HTTP／HTTPS 格式及辨識來源平台。
- 自動取得影片標題、作者、時長與縮圖；網址變更時取消舊查詢。
- Threads 分享短網址、完整貼文網址及 `/media` 網址可自動解析公開影片；依貼文編號核對內容，避免抓到推薦影片。
- 多項貼文（例如 Instagram 輪播）會略過無可用影音的項目，選取第一個可下載影音並標示項目編號；目前不提供圖片下載或整則貼文批次下載。
- 影片輸出 MP4、MKV，依來源格式列出可用解析度，預設選擇最高畫質。
- 有 4K／8K 格式時提供對應選項；不會為只有 720p 的來源建立更高畫質選項。直式影片以較短邊標示解析度。
- 影片網址可只下載音訊，輸出 MP3 或 WAV。
- 音訊辨識同時檢查編碼及明確的獨立音軌資訊，支援 X 未提供音訊編碼名稱的 HLS 音軌，避免誤停用 MP3／WAV 品質選項；明確無音訊的格式仍排除。
- 串流下載顯示百分比、速度、下載量與剩餘時間；FFmpeg 合併、擷取、封裝及轉檔顯示各階段進度。
- 多筆下載佇列：逐筆執行、顯示等待項目、移除等待項目及取消目前下載。
- 啟動時自動檢查、安裝或更新 yt-dlp，顯示核心狀態。
- 自動檢查 FFmpeg／ffprobe，缺少或無法執行時下載經 SHA256 驗證的 Windows 建置。
- 工具準備失敗可直接按右上角「重試」，重新檢查 yt-dlp 與 FFmpeg。
- 保存最近 100 筆下載紀錄：標題、平台、格式／品質、結束時間、輸出路徑與成功／失敗／取消狀態。
- 啟動時自動選取最近紀錄，下載結束後自動選取最新紀錄；可開啟檔案或所在資料夾，雙擊成功紀錄可開啟檔案。
- 可移除選取紀錄或清除全部紀錄，會保存變更且不刪除下載檔案。
- 下載完成提示固定顯示於頁首下方，與內容區保留間距；完成時內容回到頂端，捲動內容不會蓋住提示。可直接開啟輸出檔案或檔案所在資料夾。
- 下載中可按「取消目前下載」，停止下載／轉檔並清理本次工作的暫存目錄。
- 記住儲存資料夾、格式及 MP3／WAV 品質。影片畫質仍依新來源預設最高。
- 同名下載自動加上編號，不覆寫既有影片或音訊。
- 右上角圓框 i 資訊按鈕開啟關於視窗：產品與作者資訊、動態版本、下載工具更新檢查及複製系統資訊。

平台名稱辨識包含 YouTube、Facebook、Instagram、Threads、X／Twitter；實際能否下載由 yt-dlp、來源提供的格式及存取條件決定。辨識出平台不代表該網址一定可下載。

## 音訊品質

| 格式 | 可選品質 |
| --- | --- |
| MP3 | 最佳音質（可變位元率）、320／256／192／128／96 kbps |
| WAV | 16-bit／來源取樣率、24-bit／來源取樣率、16-bit／44.1 kHz、24-bit／48 kHz |

這些是輸出設定。提高位元率、位元深度或取樣率不會恢復來源已失去的音質。MP3 選項維持現有設定，尚未顯示來源音訊位元率。

選擇 MP3／WAV 後，程式會依來源音軌資訊啟用音訊品質選單。X 的部分 HLS 音軌未填入編碼名稱，現在也能透過明確的獨立音軌資訊辨識；只有資訊缺漏而沒有音軌證據時，不會直接判定有音訊。

## 開發與執行

目前核心執行檔路徑以 Windows `.exe` 為準；尚未完成 macOS／Linux 支援。

需要：

- .NET 10 SDK。
- 可使用的網路連線；第一次沒有 yt-dlp 時需連線至 GitHub。
- FFmpeg 與 ffprobe 可由程式自動準備；也可預先放在同一個 PATH 目錄或程式 `Tools` 資料夾供離線使用。
- 可寫入的程式 `Tools` 目錄與下載儲存目錄。

在專案根目錄執行：

```powershell
dotnet restore
dotnet run --project VideoDownloader.csproj
```

若使用 PATH 安裝 FFmpeg，可先確認：

```powershell
ffmpeg -version
ffprobe -version
```

程式會先檢查既有 FFmpeg／ffprobe；找不到可用工具時，從 [Gyan Windows 建置](https://github.com/GyanD/codexffmpeg/releases) 下載 essentials ZIP、驗證 GitHub 發行資產 SHA256，解壓並測試兩個工具後安裝。此建置來源列於 [FFmpeg 官方下載頁](https://ffmpeg.org/download.html)。

安裝位置是 `%LOCALAPPDATA%\VideoDownloader\Tools\ffmpeg`，不需要改動系統 PATH。首次準備可能需數分鐘與額外磁碟空間。FFmpeg 目前只在缺少／不可用時安裝，不會每次強制更新；失敗時顯示錯誤，排除問題後可按右上角「重試」。

## 使用方式

網址尚未辨識時顯示 `?`；辨識出 YouTube、Facebook、Instagram、Threads 或 X／Twitter 後切換平台圖示。

工作列與安裝捷徑使用一致的程式識別碼，桌面及開始功能表捷徑明確指定隨安裝包附帶的 Logo。若舊的工作列釘選仍顯示通用圖示，請安裝新版後取消舊釘選，從開始功能表開啟 VideoDownloader 再重新釘選。

1. 啟動程式，等待工具準備完成；若顯示失敗，查看錯誤並排除問題後按「重試」。
2. 貼上單一影片網址，等待資訊及品質選單載入。
3. 選擇影片或音訊格式，再選擇畫質／音訊品質。
4. 選擇儲存資料夾，按影片資訊下方的「開始下載／加入佇列」。
5. 等待頁首下方顯示「下載完成」，可直接按「開啟檔案」或「開啟下載資料夾」。下載串流到 100% 後，可能仍需合併或轉檔。

下載期間主按鈕顯示「加入下載佇列」，另提供「取消目前下載」按鈕。取消及正常關閉視窗都會停止子程序，並嘗試刪除這次工作的 `.videodownloader-*` 暫存目錄；已完成的檔案不受影響。強制結束程序、斷電或檔案被其他程式鎖住時，仍可能留下暫存目錄。

要下載多筆影片，可在目前工作執行時貼上下一個網址、選擇格式與品質，再加入佇列。等待中的項目可移除；佇列逐筆執行，目前工作取消或失敗後會繼續處理下一筆。關閉程式後不會保存等待佇列。

下載紀錄會自動選取最新一筆，也可手動選取舊紀錄開啟檔案或所在資料夾。檔案已被移動或刪除時，無法透過原路徑開啟。移除／清除紀錄只處理紀錄，不會刪除實際下載檔案。

首次儲存位置使用 Windows 已知資料夾 API 取得實際「下載」位置（包括使用者重新導向的路徑），再加上 `VideoDownloader` 子目錄。設定存放在 `%LOCALAPPDATA%\VideoDownloader\settings.json`，紀錄存放在同目錄的 `history.json`。紀錄上限為 100 筆，超出只移除舊紀錄，不刪除下載檔案。

## 關於視窗

點擊右上角的向量圓框 i，開啟緊湊的關於視窗（預設 460 × 520）。Logo 與名稱並排，顯示 Bruce Yang、Copyright、版本、Stable、發布日期及平台資訊；版本與作者等發布資料統一取自 `VideoDownloader.csproj`。

.NET、Avalonia、yt-dlp、FFmpeg 版本動態讀取。長版本字串以省略號呈現，滑鼠提示與「複製資訊」保留完整內容。第三方授權說明濃縮為一行，不提供 GitHub 連結。

「檢查更新」更新 yt-dlp 並確認 FFmpeg 可用，下載或工具準備中需稍後再試；主程式仍需安裝新版。「複製資訊」包含產品、版本與作業系統資訊，不含下載網址或紀錄。

## 核心更新與工具位置

`Tools` 是相對於 `AppContext.BaseDirectory`，不是固定指向原始碼資料夾。例如開發執行時：

```text
bin/Debug/net10.0/
├─ VideoDownloader.exe
└─ Tools/
   ├─ yt-dlp.exe
   ├─ yt-dlp.exe.bak     # 成功替換既有核心時保留的備份
   ├─ ffmpeg.exe         # 若已加入 PATH，可不放在此處
   ├─ ffprobe.exe
   └─ yt-dlp-plugins/    # 隨程式發布的 Threads 解析器
```

更新流程會比較本機與 GitHub 最新穩定版本；相同或較新的本機版本不重新下載。新版先下載至暫存檔，驗證同一發行版本的 SHA256 及執行檔版本，再原子替換舊檔。

更新失敗時會沿用已確認可執行的本機核心；沒有可用核心時停用下載。滑鼠移到右上角狀態可查看錯誤，排除問題後按「重試」重新準備工具。

原始碼中的 `Tools/yt-dlp.exe` 是可選的離線初始檔，有放置時會複製至建置／發布目錄。第三方執行檔已由 `.gitignore` 排除。更多細節見 [Tools/README.md](Tools/README.md)。

Threads 使用隨程式附帶的 `Tools/yt-dlp-plugins/` 解析器，由 yt-dlp.exe 載入，使用者不需另外安裝 Python。分享連結會自動確認目標貼文，從公開連結預覽資料取得影片、音軌與實際畫質；多項貼文先下載第一個可用影片。若貼文只有文字／圖片，或網站未提供公開影片資料，會顯示原因；目前不支援登入 Cookie。此解析方式依賴 Threads 公開頁面的資料結構，網站變更時可能需要更新程式。

## 建置與發布

### Windows 安裝包

專案提供已使用 Inno Setup 7 編譯的安裝腳本，會建立 per-user Windows x64 安裝包，預設安裝至 `%LOCALAPPDATA%\Programs\VideoDownloader`。安裝精靈一開始會顯示產品名稱、版本、作者、支援平台及下載核心說明；接著建立開始功能表捷徑，並預設勾選「建立桌面捷徑（建議）」，使用者可自行取消。安裝包是 self-contained，不要求目標電腦預先安裝 .NET；第一次啟動仍會自動準備 yt-dlp 與 FFmpeg。安裝精靈其餘系統頁面使用標準英文介面，主程式為繁體中文。

需要先安裝 [Inno Setup 7](https://jrsoftware.org/isinfo.php)，再在專案根目錄執行：

```powershell
dotnet restore -r win-x64
.\installer\build-installer.ps1
```

封裝前可先清理中繼目錄：

```powershell
git clean -fdx
```

腳本會先執行 `dotnet publish -c Release -r win-x64 --self-contained true --no-restore`（同時略過可能受權限限制的 Avalonia 遙測建置工作），再呼叫 Inno Setup 產生安裝包。封裝輸出先寫到 `artifacts/installer/`，並建議複製到 `release-backup/` 供最終交付與保留紀錄：

```text
release-backup/VideoDownloader-v1.0.0-Setup-x64.exe
```

最新封裝流程在 `installer/build-installer.ps1` 已確認可重建，`artifacts/` 與 `artifacts/installer-publish/` 僅為中繼輸出，建議保留 `release-backup/VideoDownloader-v1.0.0-Setup-x64.exe` 作為可交付成果。若要快速驗證：
`SHA256: BBDD7A2E1B421FA451D21B12CA6E9C87FC06700FBCF8DBD7719BDECC9C952EA1`

`artifacts/`、`release-backup/` 是否提交由你決定，但建議採「release-backup 留存、發佈前上傳 release」流程。

版本號統一設定於 `VideoDownloader.csproj` 的 `<Version>`；封裝腳本會讀取此值，套用到安裝包版本與檔名。`1.0.0` 定位為首個正式版，安裝包與主程式均使用 `Assets/VideoDownloader-logo.ico`。

`artifacts/installer-publish/` 為可重建的 self-contained 發布輸出，重建指令請見下方。若要避免遺失 DLL 相依，請以完整資料夾一起交付，或改用 `installer` 安裝包。封裝腳本會更新安裝流程輸出；程式碼修改後需重建發佈輸出。

建議發布到 GitHub Releases 時使用以下檔名與版本標籤：

```text
VideoDownloader v1.0.0
└── VideoDownloader-v1.0.0-Setup-x64.exe
```

安裝包完成後，使用者只需下載並安裝 Setup；開始功能表會出現 VideoDownloader，第一次啟動會依序準備 yt-dlp 與 FFmpeg。安裝包目前尚未簽署，Windows SmartScreen 可能顯示未識別發行者提示。

建置完成後會產生 `bin/`、`obj/`、`.artifacts/`、`artifacts/` 等可重建輸出；交付可直接使用單一安裝包 `.exe`。下載中斷可能留下的 `.part`、`.m4s` 與 `.videodownloader-*` 暫存檔可在程式關閉後刪除。

2026-09-10 目前專案文件已更新為「封裝結果優先保留在 `release-backup/`」；如需交付，請先重建 `artifacts/installer-publish/` 與安裝包，再以 `release-backup/VideoDownloader-v1.0.0-Setup-x64.exe` 進行 Release 上傳。

封裝前建議清理 `bin/`、`obj/`、`Tests/*/bin`、`Tests/*/obj`、`artifacts`、`.artifacts/` 等暫存與封裝紀錄。建議每次重包前先刪除並重建，避免保留舊中繼檔。正式程式碼、測試專案、圖示、Threads 解析器與腳本保持不變，IDE 可能會在執行時重建少量快取。

建置與測試會重新產生所需目錄；清理後要重新封裝，先執行 `dotnet restore -r win-x64`。後續介面修改已重新封裝，安裝包校驗值以上方發布資訊為準。更新核心會自動取得校驗資料，不需要手動保存 `Tools/SHA2-256SUMS`。

若 `iscc.exe` 不在 PATH，可指定路徑：

```powershell
.\installer\build-installer.ps1 -Iscc 'C:\Program Files\Inno Setup 7\ISCC.exe'
```

安裝器不內含 FFmpeg；程式第一次啟動會從已設定的來源下載、驗證並安裝。yt-dlp 若有放在原始碼 `Tools/yt-dlp.exe`，會隨發布檔打包；沒有時則在第一次啟動自動下載。

```powershell
dotnet build VideoDownloader.csproj
dotnet publish VideoDownloader.csproj -c Release -r win-x64 --self-contained true -o artifacts/installer-publish
```

發布時會複製可選的 yt-dlp 執行檔及內附的 Threads 解析器。首次啟動會自動準備缺少的工具；若無法存取工具下載站，請預先放置 yt-dlp、FFmpeg 與 ffprobe，其中 FFmpeg／ffprobe 需自行加入執行目錄的 `Tools`。下載來源影片仍需要網路連線。

若執行中的程式鎖住建置檔案，先關閉程式再建置，或使用獨立輸出目錄：

```powershell
dotnet build VideoDownloader.csproj -o .artifacts/check-build
```

在受限環境若遇到 `AvaloniaStatsTask` 無法寫入遙測紀錄，可使用以下參數驗證建置；這不會修改專案設定：

```powershell
dotnet build VideoDownloader.csproj -p:UsedAvaloniaProducts=
```

### Git 忽略規則

`.gitignore` 排除 .NET 建置目錄、發布與安裝包、測試結果、Python 快取與 `.venv/`、第三方工具執行檔及更新備份，以及下載片段和暫存目錄。`Tools/yt-dlp-plugins/` 的 Python 原始碼、`Tests/` 測試原始碼、`Assets/` 圖示及安裝腳本應納入版本控制。

安裝包請上傳至 GitHub Releases；完整發布資料夾中的相依檔案仍需保留。忽略規則不會刪除本機檔案，也不會自動停止追蹤已經提交到 Git 的檔案。

## 測試

About 與主視窗版面測試（使用 Avalonia Headless，不啟動主視窗的網路更新）：

```powershell
dotnet run --project Tests/AboutSmoke -p:UsedAvaloniaProducts= -- Tools/yt-dlp.exe
```

驗證組件版本與作者、工具缺少與取消狀態、真實工具版本、預設／最小視窗尺寸下的按鈕可見性、剪貼簿內容，以及更新按鈕呼叫檢查流程後重新讀取版本。更新流程在此測試使用模擬回傳；工具更新本身由 UpdaterSmoke／WorkflowSmoke 驗證。最後的 yt-dlp 路徑可省略，省略時跳過指定工具的版本實測。另驗證長檔名、已有捲動位置時顯示完成提示，以及 1000 × 720／850 × 650 視窗下的固定間距、回到頂端與內容裁切。

Threads 解析器測試（開發環境需 Python 與 `yt-dlp` 套件；安裝包使用者不需要）：

```powershell
python -m unittest discover -s Tests/ThreadsExtractor -v
```

涵蓋分享網址、完整網址、目標貼文核對、不誤抓推薦內容、圖片貼文、輪播與 DASH 畫質／音軌。

版本更新測試使用模擬 HTTP 回應及真實 yt-dlp 版本探測：

```powershell
dotnet run --project Tests/UpdaterSmoke -- Tools/yt-dlp.exe
```

最後一個參數需指向實際存在的 yt-dlp；若由程式首次下載，可改用 `bin/Debug/net10.0/Tools/yt-dlp.exe`。

測試涵蓋首次安裝、相同／較新版本略過、離線保留本機版本、缺少核心、SHA256 失敗、版本不符、損壞核心修復、備份、取消與暫存檔清理。

影音、設定、紀錄及 FFmpeg 安裝測試：

```powershell
dotnet run --project Tests/WorkflowSmoke -- Tools/yt-dlp.exe C:/ffmpeg/bin
```

兩個參數分別為實際 yt-dlp 路徑及 FFmpeg／ffprobe 所在目錄。測試會生成本機影片與 HTTP 來源，驗證 MP4／MKV、MP3 位元率、WAV 位元深度／取樣率、來源畫質選項、設定與紀錄序列化、重複下載不覆寫、下載中取消及暫存清理。FFmpeg 安裝使用模擬 HTTP 發行回應和真實工具 ZIP，驗證安裝、離線沿用與 SHA256 拒絕；不會修改使用者的正式工具安裝。

`AudioDetectionTests.cs` 涵蓋 X HLS 缺少編碼名稱、獨立音軌、明確無音訊、資訊不足、圖片及 DRM 格式，已接入上述工作流程測試。

`CarouselTests.cs` 涵蓋失敗／圖片項目略過、保留原項目編號、純圖片貼文與 MP4／MKV／MP3／WAV 選取參數。

### 真實網址驗證

以下為開發期間已完成的樣本實測，與本機自動化測試分開執行；臨時測試程式、下載樣本與紀錄已清理，`Tests/` 內的正式回歸測試仍保留：

| 來源 | 已驗證內容 |
| --- | --- |
| Instagram 輪播貼文 | 資訊查詢、選取第一個可用影片及實際 MP4 下載。 |
| Threads 分享連結 | 解析目標貼文、取得畫質與音軌、MP4 下載及 MP3 轉換。 |
| X／Twitter 三個測試網址 | MP4 下載；修正後可辨識音訊並輸出 MP3／WAV，以 ffprobe 確認只有音軌且編碼正確。 |

樣本成功不代表同平台所有貼文都可下載，網站內容與存取條件也可能改變。其他來源樣本、長影片、原生檔案開啟操作及不同 DPI 下的視窗操作仍需擴充驗證。

## 目前限制

- 目前提供 Windows x64 版本，尚未提供 macOS／Linux 發布包。
- 還沒有加入 Cookie／瀏覽器登入整合；需要登入、私訊、私人或受限權限的內容仍可能無法下載。
- 多項貼文仍以第一個可用影音為主，未提供逐項勾選、整則貼文批次下載或圖片下載。
- 下載佇列為本次執行生命週期保存，程式重啟後不保留等待工作；未支援暫停繼續與平行下載。
- 進度目前為分段階段指標（下載/FFmpeg/轉碼），尚未整合成「整體百分比」或跨任務 ETA 模型。
- MP4／MKV 採來源封裝優先，未提供完整的「通用相容編碼」預設轉碼策略。
- 首次啟動依賴網路更新 `yt-dlp` 與 `FFmpeg`；離線環境若未預先放工具檔仍需手動處理。
- 安裝程式未簽章，目標機可能仍有 SmartScreen 安全提示。

## 後續改善

建議下一版先聚焦以下四大方向（非版本承諾）：

| 類型 | 待補內容 |
| --- | --- |
| 穩定性 | 佇列持久化（重啟可恢復）、失敗重試策略、可中斷/續傳下載。 |
| 相容性 | 提供轉碼預設包（例如偏向通用播放）、並加入更完整的平台限制與失敗因應處理。 |
| 訊息體驗 | 擴充錯誤分類（網路/平台/轉碼/檔案系統），加入可複製診斷摘要與重試按鈕。 |
| 發行與驗證 | 補強自動打包上傳流程、簽章規劃與跨 OS 發行需求釐清，並補齊失敗案例與 DPI 迴歸測試。 |

## 專案結構

完整檔案樹與各檔案用途請見根目錄的 [project_structure.txt](project_structure.txt)，可直接用記事本開啟。

```text
VideoDownloader/
├── README.md                 使用、建置、測試與後續改善
├── project_structure.txt     完整純文字檔案樹
├── VideoDownloader.csproj    專案設定
├── Program.cs               程式進入點
├── App.axaml / App.axaml.cs  主題與主視窗初始化
├── app.manifest             Windows 應用程式資訊
├── ViewLocator.cs           ViewModel 與 View 對應
├── Assets/                  圖示資源
├── Views/                   介面及操作事件
├── ViewModels/              ViewModel 基礎
├── Services/                下載、更新、工具安裝及使用者資料
├── installer/               Inno Setup 安裝包腳本與建置腳本
├── Tests/
│   ├── AboutSmoke/          關於視窗、版本與剪貼簿測試
│   ├── UpdaterSmoke/        核心更新測試
│   ├── ThreadsExtractor/    Threads 解析器測試
│   └── WorkflowSmoke/       影音、音軌辨識、取消、紀錄及 FFmpeg 安裝測試
└── Tools/                   工具說明、Threads 解析器及可選離線核心
```

使用者設定與下載紀錄保存在 `%LOCALAPPDATA%/VideoDownloader`；建置產物與執行時資料的路徑也列於檔案樹說明。

目前 README 以「可重建」模式維護，發布包在發佈前由封裝流程輸出後再上傳；本機保留版本建議存放在 `release-backup/`，請以 GitHub Release 的附件版本為最終下載來源，`.gitignore` 已排除 `artifacts` 目錄。


