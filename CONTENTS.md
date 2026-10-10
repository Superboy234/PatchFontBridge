# PatchFontBridge 1.4

- `README.md`：使用者快速開始與開發者建置。
- `LICENSE`、`THIRD_PARTY_NOTICES.md`、`licenses/`：本專案與第三方授權。
- `src/`、`assets/`：GUI／CLI、代理範本與介面素材。
- `SimpleFontHook/`：內嵌的 x86／x64 SFH DLL。
- `vendor/SimpleFontHook/`：對應的本地修改版 SFH 原始碼與建置依賴。
- `prebuilt/`：預先編譯的 x86／x64 代理。
- `BUILD_INPUTS.json`：建置输入與雜湊。
- `tools/prepare-proxies.ps1`：開發者重建代理範本。
- `build.ps1`、`build-font-hooks.ps1`：開發者建置腳本。
- `tests/`：隔離測試来源與腳本。

使用者下載發行包即可使用，不需建置工具。內部 WinmmProxyMaker 命名保留，以相容既有安裝。