# 第三方元件聲明

## SimpleFontHook

- 專案：https://github.com/SuQiandYing/SimpleFontHook
- 上游維護者：SuQiandYing
- 用途：提供遊戲進程內的字型 Hook、選單與設定持久化；本工具將 x86／x64 DLL 嵌入建置產物。
- 授權：MIT；完整文字見 [licenses/SimpleFontHook-LICENSE.txt](licenses/SimpleFontHook-LICENSE.txt)。
- 授權來源：https://github.com/SuQiandYing/SimpleFontHook/blob/main/LICENSE.txt
- 授權文字查閱日期：2026-10-10。

授權副本保留上游現有的 `Copyright (c) 2026 <YOUR NAME>` 原文，沒有自行替上游修改權利人名稱。此副本不是對實際 SFH 來源版本的固定；本儲存庫包含 SFH 本地修改版原始碼与 DLL；未記錄修改版的上游 commit，建置輸入雜湊記錄於 BUILD_INPUTS.json。

本專案內建的 SFH 有快捷鍵、選單啟動與字型建立入口等修改，詳見 README。這些修改不應歸為上游現有版本的保證功能。本專案與上游獨立維護。

### 含 SFH 的發行包

發行者應保留實際使用之 SFH 版本附帶的著作權與 MIT 授權文字，記錄來源 commit／版本、DLL 雜湊與本地修改。建議隨包提供對應的修改版原始碼或可取得的來源，讓使用者可以重建。

此處的本專案 MIT 授權不取代第三方授權，也不涵蓋使用者提供的遊戲、字型或漢化補丁。

## 傳遞依賴與壓縮工具

SimpleFontHook 的建置使用 Microsoft Detours；若發行包包含該程式庫或相關程式碼，需保留實際所用版本的授權聲明。來源：https://github.com/microsoft/Detours。

先前整合產物使用 UPX 壓縮。原始碼儲存庫包含 UPX 工具與對應的 LICENSE、COPYING；使用者下載包不包含 UPX 執行檔。壓縮產物與 UPX 工具本身應分別依其適用條款處理。來源：https://github.com/upx/upx。
本發行包內含 SFH DLL，使用 Microsoft Detours 4.0.1；授權見 licenses/Detours-LICENSE.txt。原始碼套件包含 UPX 工具與其原始 LICENSE、COPYING；使用者執行包不包含 UPX 執行檔。
