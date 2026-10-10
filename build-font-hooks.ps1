$ErrorActionPreference='Stop'
$base=Join-Path $PSScriptRoot 'vendor\SimpleFontHook'
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vsPath=(& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath).Trim()
$version=(& $vswhere -latest -products '*' -property installationVersion).Trim()
$toolset=if([int]($version.Split('.')[0]) -ge 18){'v145'}else{'v143'}
$devCmd=Join-Path $vsPath 'Common7\Tools\VsDevCmd.bat'
foreach($arch in @('Win32','x64')){
 $target=if($arch -eq 'Win32'){'x86'}else{'x64'}
 Push-Location $base
 try { & $env:COMSPEC /d /c "call `"$devCmd`" -arch=$target -host_arch=x64 >nul && set SIMPLEFONTHOOK_SKIP_PACK=1&& msbuild SimpleFontHook.sln /p:Configuration=Release /p:Platform=$arch /p:PlatformToolset=$toolset /t:Rebuild /v:minimal";if($LASTEXITCODE -ne 0){throw "Native build failed: $arch"} }
 finally {Pop-Location}
 $built=Join-Path $base $(if($arch -eq 'Win32'){'Release\winmm.dll'}else{'x64\Release\winmm.dll'})
 $out=Join-Path $PSScriptRoot $(if($arch -eq 'Win32'){'SimpleFontHook\x32'}else{'SimpleFontHook\x64'})
 New-Item -ItemType Directory -Force (Join-Path $out 'uncompressed') | Out-Null
 Copy-Item -LiteralPath $built -Destination (Join-Path $out 'uncompressed\winmm.dll') -Force
 & (Join-Path $base 'tools\upx\upx.exe') --best --lzma $built
 if($LASTEXITCODE -ne 0){throw "Compression failed: $arch"}
 Copy-Item -LiteralPath $built -Destination (Join-Path $out 'winmm.dll') -Force
}
& (Join-Path $PSScriptRoot 'build.ps1')
