# PatchFontBridge

**讓使用 `winmm.dll` 的漢化補丁與 SimpleFontHook 共存的 Windows 代理 DLL 整合工具。**

PatchFontBridge（原名 WinmmProxyMaker）分析遊戲的 DLL 匯入入口，產生另一個名稱的轉送代理來載入原漢化 DLL，將 `winmm.dll` 入口留給 [SimpleFontHook](https://github.com/SuQiandYing/SimpleFontHook)。工具提供圖形介面、備份與還原、字型預設及快捷鍵設定。

本專案獨立維護，並非 SimpleFontHook 官方產品。字型替換能力來自 SimpleFontHook；本專案主要負責代理建置與安裝整合。

## 下載與快速開始

使用者請下載 **`PatchFontBridge-1.4-win.zip` 發行包**，不要下載 GitHub 的 Source code ZIP。

1. 解壓縮，開啟 `PatchFontBridge.exe`。
2. 選擇遊戲 EXE 與原漢化補丁 DLL。
3. 確認位置後按「安裝到遊戲目錄」，再啟動遊戲。

**已內建 32／64 位元 SimpleFontHook 與代理 DLL，不必另外準備 DLL、安裝 Visual Studio 或 C++ Build Tools。** 使用者只需要 Windows 10／11 與 .NET Framework 4.8。

發行包提供於 [GitHub Releases](https://github.com/Superboy234/PatchFontBridge/releases)。目前版本為 **1.4 預覽版**。新代理流程已完成編譯，尚未在真實遊戲與不同 Windows 版本上驗證。

還原時先關閉遊戲，再開啟工具按「還原原 DLL」。

## 功能

- 分析遊戲 EXE 的靜態匯入，選擇可用的代理入口，例如 `version.dll`。
- 產生 x86／x64 轉送代理，核對匯出名稱與序號。
- 將漢化主代理放在遊戲根目錄、原漢化 DLL 所在資料夾，或 `漢化補丁` 資料夾。
- 使用子資料夾時建立根目錄橋接，供遊戲載入主代理。
- 將原 DLL 改為 `.bak`，或移除原件並保留工具的還原備份。
- 掃描補丁內可辨識的字型檔名，將找到的 TTF／OTF／TTC 與補丁副本一起部署。
- 提供字型預覽、排版、字集與快捷鍵預設。
- 提供載入狀態檢查、設定分發、更新整合及還原。
- 提供暗色／淺色介面、Mini 模式及高 DPI 支援。

## 工作原理

兩份不同的 `winmm.dll` 無法同時放在同一目錄，以相同名稱提供載入入口。單純改名也不會讓遊戲自動載入它。

本工具根據遊戲實際匯入的其他系統 DLL，產生具有對應匯出的代理，將系統 API 呼叫轉送到原系統 DLL，再載入漢化補丁副本與 SimpleFontHook。

以 `version.dll` 為例：

```text
遊戲 EXE
├─ version.dll → 本工具的代理 → 系統 version.dll
│                            └─ 原漢化補丁副本
└─ winmm.dll   → SimpleFontHook → 系統 winmm.dll
```

這是載入入口的橋接，不會改寫原漢化 DLL 的內容。入口是否可用取決於遊戲的匯入與載入方式，並非任意 DLL 名稱都能使用。

## 環境需求

| 用途 | 需求 |
| --- | --- |
| 執行工具 | Windows 10／11、.NET Framework 4.8 |
| 安裝代理 | 發行包已內建所需元件，不需要編譯器 |
| 開發者重建 | .NET Framework C# 編譯器；重建原生元件另需 MSVC、MASM 與 Windows SDK |
| 目標程式 | 可分析的 x86／x64 Windows EXE；補丁須與遊戲架構相符 |

代理使用預先編譯的範本，安裝時填入補丁路徑並核對 API 匯出。若此 Windows 沒有匹配範本，該入口不會列為可用選项；請改選其他入口或取得更新版本。

## 使用方式

下載並解壓縮發行包後：

1. 關閉遊戲及其啟動器，備份遊戲目錄。
2. 開啟 `PatchFontBridge.exe`，選擇遊戲 EXE；程式會自動分析一次載入入口。
3. 選擇**原漢化補丁 DLL**，不要選成 SimpleFontHook 的 `winmm.dll`。
4. 選擇代理放置位置。「與原 DLL 同資料夾」要求該資料夾位於遊戲目錄內。
5. 視需要設定字型與快捷鍵。預設保留原字型；勾選「明確啟用字型替換」才寫入替換預設。
6. 按「安裝到遊戲目錄」；已有安裝紀錄時，使用「更新整合」。
7. 啟動遊戲，確認文字顯示並查看載入狀態。

安裝會設定 `PickerShowOnStartup=0`。採用本專案修改版 SFH 時，預設快捷鍵為 **Ctrl+Alt+F10**；實際註冊結果以 SFH 選單與日誌為準。

還原時先關閉遊戲，再按「還原原 DLL」。若 DLL 被其他工具修改，工具可能拒絕覆寫，需先檢查檔案。

### 安裝檔案

| 名稱或路徑 | 用途 |
| --- | --- |
| `winmm.dll` | 與遊戲架構相符的 SimpleFontHook |
| `version.dll` 等入口 DLL | 產生的代理或子資料夾橋接，名稱依分析結果決定 |
| `translation_patch_<編號>.dll` | 原漢化 DLL 的位元組副本，編號取自雜湊 |
| `FontHook.ini` | SFH 字型及選單設定 |
| `.WinmmProxyMaker/` | 建置紀錄、安裝清單及還原備份 |
| `<入口 DLL>.proxy.log` | 代理載入結果 |

為相容既有安裝，內部命名空間、安裝資料夾與舊清單名稱仍保留 `WinmmProxyMaker`。不要自行刪除補丁副本或改動清單。

## SimpleFontHook 整合

上游專案：[SuQiandYing/SimpleFontHook](https://github.com/SuQiandYing/SimpleFontHook)。

內建 SFH 以本專案的 1.3.1 整合修改為基礎，包括：

- 可設定的選單快捷鍵。
- 等待本進程有可見遊戲視窗後建立選單。
- 字型探測與明確替換時使用原始 `CreateFontIndirectExW`，減少補丁強制指定字型的干擾。
- 接受不含 `FontNameW` 的設定檔；啟動隱藏不依賴快捷鍵註冊成功。

修改版原始碼位於 endor/SimpleFontHook/，32／64 位元 DLL 已嵌入工具。SimpleFontHook/ 保存建置輸入，prebuilt/ 保存原生代理範本。這些元件亦隨來源儲存庫提供，使用者不必自行編譯。

若遊戲沒有另一份 `winmm.dll` 衝突，可直接依 [SimpleFontHook 上游說明](https://github.com/SuQiandYing/SimpleFontHook#readme)使用該工具。

## 開發者建置

一般使用者不需要以下步驟。儲存庫已包含 SFH DLL 與代理範本；只重建 GUI／CLI 時執行：

```powershell
.\build.ps1
```

輸出 `PatchFontBridge.exe`、`PatchFontBridge.Cli.exe` 及各自的 `.config`。

重建 SFH 需要 Visual Studio C++ Build Tools，執行 `build-font-hooks.ps1`。重新產生代理範本需要相同 C++ 環境，先執行 `build.ps1` 取得工具，再執行 `tools/prepare-proxies.ps1`，最後再次執行 `build.ps1` 嵌入範本。

代理支援表由建置電腦的系統 DLL 匯出產生。不同 Windows 版本的匯出可能不同，工具會比對後才提供入口。完整跨 Windows 版本相容性仍需確認。

發行時保留 EXE 旁的 `.config`、授權與第三方聲明。建置輸入的雜湊及系統資訊記錄在 `BUILD_INPUTS.json`；SFH 來源是保留的本地修改快照，未固定上游 commit。

### CLI

```powershell
.\PatchFontBridge.Cli.exe analyze "D:\Game\game.exe"
.\PatchFontBridge.Cli.exe build "D:\Game\game.exe" "D:\Game\patch\winmm.dll" version.dll "D:\Game\.WinmmProxyMaker\builds" --placement translation --original bak
.\PatchFontBridge.Cli.exe install "<產生的套件目錄>"
.\PatchFontBridge.Cli.exe status "D:\Game\game.exe"
.\PatchFontBridge.Cli.exe restore "D:\Game\.WinmmProxyMaker\install.xml"
```

`build` 的入口名稱須來自分析結果；`--placement` 支援 `root`、`patch`、`translation`，另保留 `dll` 相容選項。建置與安裝為兩個不同步驟。

## 相容性與限制

- 載入成功表示 DLL 已載入，不代表字型替換或文字編碼必然正確。
- 本機字型預覽不能重現每種遊戲引擎的渲染方式。
- 自訂字元映射的漢化字型換成一般系統字型後，可能出現錯字或缺字。
- 動態載入、加殼、多進程啟動器及補丁的 Hook 順序可能影響結果。
- 資源掃描只處理可辨識的靜態字型檔名；其他資料與動態路徑需按補丁要求保留。
- 預設保留字型不會清除既有 INI 中的替換設定。

## 回報問題

請提供工具版本、遊戲與補丁版本、x86／x64 架構、入口與放置位置、重現步驟，以及去除個人資訊後的日誌與相關設定。請勿提交遊戲檔案或未獲授權的漢化補丁。

`tests/` 提供隔離測試來源與腳本，不能代替實際遊戲相容性確認。

## 授權與致謝

PatchFontBridge 原創程式碼採用 [MIT License](LICENSE)。

感謝 SuQiandYing 維護 SimpleFontHook。該專案採用 MIT 授權，其授權獨立保留於 [第三方授權副本](licenses/SimpleFontHook-LICENSE.txt)。其他第三方元件與發行注意事項見 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。