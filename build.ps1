$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
Push-Location $PSScriptRoot
try {
    foreach ($resource in @('SimpleFontHook\x32\winmm.dll', 'SimpleFontHook\x64\winmm.dll')) {
        if (!(Test-Path -LiteralPath $resource)) {
            throw "Missing SFH resource: $resource. See README.md for the required integration build."
        }
    }
    if (!(Test-Path -LiteralPath 'assets\WinmmProxyMaker.ico')) { & (Join-Path $PSScriptRoot 'make-icon.ps1') }
    $common = @('/nologo','/platform:anycpu','/optimize+','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Xml.Linq.dll','/resource:src\proxy.cpp.in,proxy.cpp','/resource:SimpleFontHook\x32\winmm.dll,sfh.x86','/resource:SimpleFontHook\x64\winmm.dll,sfh.x64','/resource:assets\WinmmProxyMaker.ico,app.icon','/resource:assets\icon-preview.png,app.logo','/win32manifest:src\app.manifest','/win32icon:assets\WinmmProxyMaker.ico','src\Program.cs','src\Integration.cs','src\Profiles.cs','src\Gui.cs')
    foreach ($arch in @('x86','x64')) {
        foreach ($proxy in Get-ChildItem -LiteralPath (Join-Path 'prebuilt' $arch) -Filter '*.dll' -ErrorAction SilentlyContinue) {
            $common += '/resource:'+$proxy.FullName+',proxy.'+$arch+'.'+$proxy.Name
        }
    }
    & $compiler /target:winexe /out:PatchFontBridge.exe @common
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    & $compiler /target:exe /out:PatchFontBridge.Cli.exe @common
    if ($LASTEXITCODE -ne 0) { throw 'CLI build failed.' }
    Copy-Item -LiteralPath src\app.config -Destination PatchFontBridge.exe.config -Force
    Copy-Item -LiteralPath src\app.config -Destination PatchFontBridge.Cli.exe.config -Force
} finally { Pop-Location }
