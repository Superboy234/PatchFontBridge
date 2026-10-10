param([switch]$KeepArtifacts,[string]$WorkDirectory=[IO.Path]::GetTempPath())
$ErrorActionPreference='Stop'
$toolRoot=Split-Path $PSScriptRoot -Parent
$cli=Join-Path $toolRoot 'PatchFontBridge.Cli.exe'
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vsPath=(& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath).Trim()
$devCmd=Join-Path $vsPath 'Common7\Tools\VsDevCmd.bat'
$testRoot=Join-Path ([IO.Path]::GetFullPath($WorkDirectory)) ('wpm-tests-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
function Ok([string[]]$Arguments){& $cli @Arguments;if($LASTEXITCODE -ne 0){throw ('Expected success: '+($Arguments -join ' '))}}
function Reject([string[]]$Arguments){& $cli @Arguments;if($LASTEXITCODE -eq 0){throw ('Expected rejection: '+($Arguments -join ' '))}}
try {
 foreach($arch in @('x86','x64')) {
  $root=Join-Path $testRoot $arch;New-Item -ItemType Directory -Path $root | Out-Null
  Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'fixture.cpp'),(Join-Path $PSScriptRoot 'payload.cpp') -Destination $root
  $commands=@"
@echo off
cd /d "%~dp0"
call "$devCmd" -arch=$arch -host_arch=x64 >nul
if errorlevel 1 exit /b 1
cl /nologo /O2 /MT /EHsc fixture.cpp /Fe:fixture.exe /link version.lib user32.lib
if errorlevel 1 exit /b 1
cl /nologo /LD /O2 /MT payload.cpp /Fe:patch.dll
exit /b %errorlevel%
"@
  [IO.File]::WriteAllText((Join-Path $root 'build.cmd'),$commands,[Text.Encoding]::Default)
  & $env:COMSPEC /d /c (Join-Path $root 'build.cmd');if($LASTEXITCODE -ne 0){throw 'Fixture compilation failed'}
  $exe=Join-Path $root 'fixture.exe';$patch=Join-Path $root 'patch.dll';$patchHash=(Get-FileHash $patch).Hash
  $ini=Join-Path $root 'FontHook.ini';$initialIni="[FontHook]`r`nFontNameW=Microsoft YaHei`r`nPickerShowOnStartup=1`r`nEnableFontHook=1`r`n[Other]`r`nPickerShowOnStartup=1`r`n"
  [IO.File]::WriteAllText($ini,$initialIni,(New-Object Text.UTF8Encoding($false)));$iniHash=(Get-FileHash $ini).Hash
  foreach($scenario in @('root-bak','dll-delete','dll-bak-early')) {
   $placement=if($scenario.StartsWith('dll')){'dll'}else{'root'};$mode=if($scenario.Contains('delete')){'delete'}else{'bak'}
   $output=Join-Path $testRoot "$arch-$scenario";$arguments=@('build',$exe,$patch,'version.dll',$output,'--placement',$placement,'--original',$mode);if($scenario.Contains('early')){$arguments+='--early'}
   Ok $arguments
   $package=(Get-ChildItem -LiteralPath $output -Directory | Select-Object -First 1).FullName
   & $exe (Join-Path $package 'version.dll');if($LASTEXITCODE -ne 0){throw 'Proxy forwarding failed'}
   $fontTarget=Join-Path $root 'winmm.dll';[IO.File]::WriteAllText($fontTarget,'old winmm sentinel');$fontHash=(Get-FileHash $fontTarget).Hash
   Ok @('install',$package)
   if(Test-Path $patch){throw 'Original patch not retired'}
   if($mode -eq 'bak' -and !(Test-Path ($patch+'.bak'))){throw 'Original .bak missing'}
   if($mode -eq 'delete' -and (Test-Path ($patch+'.bak'))){throw 'Delete mode left .bak'}
   $lines=[IO.File]::ReadAllText($ini);if(!$lines.Contains('PickerShowOnStartup=0')-or $lines.Contains('PickerShowOnStartup=1')-or !$lines.Contains('FontNameW=Microsoft YaHei')){throw 'INI incorrectly changed'}
   if($placement -eq 'dll' -and !(Test-Path (Join-Path $root 'dll\version.dll'))){throw 'Child proxy missing'}
   if((Get-FileHash $fontTarget).Hash -ne (Get-FileHash (Join-Path $package 'winmm.dll')).Hash){throw 'Wrong font DLL installed'}
   & $exe --expect-font;if($LASTEXITCODE -ne 0){throw "Integrated runtime failed with code $LASTEXITCODE"}
   $marker=Join-Path $root '.WinmmProxyMaker\install.xml'
   if($scenario -eq 'root-bak'){
    $hostTarget=Join-Path $root 'version.dll';[IO.File]::WriteAllText($hostTarget,'changed by user');Reject @('restore',$marker)
    if([IO.File]::ReadAllText($hostTarget) -ne 'changed by user'){throw 'Modified file overwritten'}
    Copy-Item (Join-Path $package 'version.dll') $hostTarget -Force
   }
   Ok @('restore',$marker)
   if((Get-FileHash $patch).Hash -ne $patchHash-or (Get-FileHash $ini).Hash -ne $iniHash-or (Get-FileHash $fontTarget).Hash -ne $fontHash){throw 'Restore bytes differ'}
   if(Test-Path (Join-Path $root 'version.dll')){throw 'Root bridge not removed'}
   if(Test-Path ($patch+'.bak')){throw '.bak not removed on restore'}
   if($placement -eq 'dll' -and (Test-Path (Join-Path $root 'dll\version.dll'))){throw 'Child proxy not removed'}
   Remove-Item -LiteralPath $fontTarget
   "PASS $arch ${scenario}: font, config, bridge, original DLL, rollback"
  }
  if($arch -eq 'x86') {
   foreach($relative in @('winmm.dll','dll\version.dll')) {
    $raw=Join-Path $root $relative;Copy-Item -LiteralPath $patch -Destination $raw -Force;$rawHash=(Get-FileHash $raw).Hash
    $collisionOutput=Join-Path $testRoot ('collision-'+[Guid]::NewGuid().ToString('N'))
    Ok @('build',$exe,$raw,'version.dll',$collisionOutput,'--placement','dll','--original','bak')
    $collisionPackage=(Get-ChildItem -LiteralPath $collisionOutput -Directory | Select-Object -First 1).FullName
    Ok @('install',$collisionPackage);& $exe --expect-font;if($LASTEXITCODE -ne 0){throw 'Same-path runtime failed'}
    Ok @('restore',(Join-Path $root '.WinmmProxyMaker\install.xml'))
    if((Get-FileHash $raw).Hash -ne $rawHash){throw 'Same-path restore failed'}
    Remove-Item -LiteralPath $raw
    "PASS input matches installation target: $relative"
   }
  }
 }
 Reject @('analyze',(Join-Path $testRoot 'x86\fixture.exe'),(Join-Path $testRoot 'x64\patch.dll'))
 Reject @('build',(Join-Path $testRoot 'x86\fixture.exe'),(Join-Path $testRoot 'x86\patch.dll'),'kernel32.dll',$testRoot)
 Reject @('build',(Join-Path $testRoot 'x86\fixture.exe'),(Join-Path $toolRoot 'SimpleFontHook\x32\winmm.dll'),'version.dll',$testRoot)
 [IO.File]::WriteAllBytes((Join-Path $testRoot 'invalid.dll'),[byte[]]@(1,2,3));Reject @('analyze',(Join-Path $testRoot 'x86\fixture.exe'),(Join-Path $testRoot 'invalid.dll'))
 Add-Type -AssemblyName System.Windows.Forms
 $assembly=[Reflection.Assembly]::LoadFrom((Join-Path $toolRoot 'PatchFontBridge.exe'));$form=[Activator]::CreateInstance($assembly.GetType('WinmmProxyMaker.MainForm'),$true)
 try{$form.Opacity=0;$form.ShowInTaskbar=$false;$form.Show();[Windows.Forms.Application]::DoEvents();if(!$form.IsHandleCreated){throw 'GUI failed'};$form.Close();if(!$form.IsDisposed){throw 'GUI close failed'}}finally{$form.Dispose()}
 $utf16=[Text.Encoding]::Unicode;$iniBytes=[byte[]]($utf16.GetPreamble()+$utf16.GetBytes("[FontHook]`r`nFontNameW=思源黑體`r`nPickerShowOnStartup=1`r`n"))
 $result=[byte[]]($assembly.GetType('WinmmProxyMaker.Integration').GetMethod('Configure').Invoke($null,[object[]]@(,$iniBytes)))
 $decoded=[Text.Encoding]::UTF8.GetString($result);if(!$decoded.Contains('思源黑體')-or $decoded.Contains('PickerShowOnStartup=1')){throw 'UTF-16 conversion failed'}
 'ALL CHECKS PASSED'
}finally{
 if($KeepArtifacts){"Artifacts retained: $testRoot"}else{
  $resolved=[IO.Path]::GetFullPath($testRoot);$prefix=[IO.Path]::GetFullPath($WorkDirectory).TrimEnd('\')+'\'
  if(!$resolved.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)-or !(Split-Path $resolved -Leaf).StartsWith('wpm-tests-')){throw 'Refusing to delete unverified test directory'}
  Remove-Item -LiteralPath $resolved -Recurse -Force
 }
}
