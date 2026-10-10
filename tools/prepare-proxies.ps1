$ErrorActionPreference='Stop'
$previousPrepare=$env:PFB_PREPARE_PROXIES
try {
$env:PFB_PREPARE_PROXIES='1'
$root=Split-Path $PSScriptRoot -Parent
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $root 'PatchFontBridge.Cli.exe'))
$core=$assembly.GetType('WinmmProxyMaker.Core')
$candidateType=$assembly.GetType('WinmmProxyMaker.Candidate')
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vs=(& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath).Trim()
$dev=Join-Path $vs 'Common7\Tools\VsDevCmd.bat'
$temp=Join-Path ([IO.Path]::GetTempPath()) ('pfb-prepare-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $temp | Out-Null
[IO.File]::WriteAllText((Join-Path $temp 'sample.cpp'),'int main(){return 0;}')
[IO.File]::WriteAllText((Join-Path $temp 'payload.cpp'),'#include <windows.h>'+"`r`n"+'BOOL WINAPI DllMain(HINSTANCE,DWORD,LPVOID){return TRUE;}')
$names=@('version.dll','dinput8.dll','dinput.dll','dsound.dll','ddraw.dll','d3d8.dll','d3d9.dll','d3d11.dll','dxgi.dll','xinput1_3.dll','xinput1_4.dll','wininet.dll','winhttp.dll')
foreach($arch in @('x86','x64')){
 $dir=Join-Path $temp $arch;New-Item -ItemType Directory $dir | Out-Null
 $command="@echo off`r`ncall `"$dev`" -arch=$arch -host_arch=x64 >nul`r`nif errorlevel 1 exit /b 1`r`ncl /nologo /O2 /MT `"$temp\sample.cpp`" /Fe:sample.exe`r`nif errorlevel 1 exit /b 1`r`ncl /nologo /LD /O2 /MT `"$temp\payload.cpp`" /Fe:payload.dll`r`nexit /b %errorlevel%`r`n"
 [IO.File]::WriteAllText((Join-Path $dir 'build.cmd'),$command)
 Push-Location $dir;try{& $env:COMSPEC /d /c build.cmd;if($LASTEXITCODE -ne 0){throw 'Bootstrap compilation failed'}}finally{Pop-Location}
 $analysis=$core.GetMethod('Analyze').Invoke($null,@([string](Join-Path $dir 'sample.exe'),[string](Join-Path $dir 'payload.dll')))
 $out=Join-Path $root ('prebuilt\'+$arch);New-Item -ItemType Directory -Force $out | Out-Null
 foreach($name in $names){
  $system=Join-Path $env:WINDIR $(if($arch -eq 'x86'){'SysWOW64\'+$name}else{'System32\'+$name})
  if(!(Test-Path -LiteralPath $system)){continue}
  $candidate=[Activator]::CreateInstance($candidateType,$true)
  $candidateType.GetField('Name').SetValue($candidate,$name)
  $candidateType.GetField('SystemPath').SetValue($candidate,$system)
  $candidateType.GetField('Evidence').SetValue($candidate,'Release preparation')
  $log=[Action[string]]{param($message) Write-Host $message}
  $package=$core.GetMethod('Build').Invoke($null,@($analysis,$candidate,[string](Join-Path $temp 'packages'),$false,$log,'root','bak',$null))
  Copy-Item -LiteralPath (Join-Path $package $name) -Destination (Join-Path $out $name) -Force
  Write-Host "Prepared $arch/$name"
 }
}
Write-Host "Build intermediates: $temp"
} finally { $env:PFB_PREPARE_PROXIES=$previousPrepare }
