param([Parameter(Mandatory=$true)][string]$FixtureRoot)
$ErrorActionPreference='Stop'
$tool=Split-Path $PSScriptRoot -Parent
$cli=Join-Path $tool 'PatchFontBridge.Cli.exe'
function Ok([string[]]$arguments){& $cli @arguments;if($LASTEXITCODE -ne 0){throw 'QoL command failed'}}
foreach($arch in @('x86','x64')){
 $root=Join-Path $FixtureRoot $arch;$game=Join-Path $root 'fixture.exe'
 $folder=Join-Path $root '引擎\DLL';New-Item -ItemType Directory -Force $folder|Out-Null
 $raw=Join-Path $folder 'winmm.dll';Copy-Item (Join-Path $root 'patch.dll') $raw -Force
 $preset=Join-Path $root 'preset.xml';[IO.File]::WriteAllText($preset,'<FontPreset Face="Microsoft YaHei" Height="110" Width="100" Spacing="0" Charset="-1" Bold="false" Vk="120" Modifiers="6"/>')
 $out=Join-Path $root '.WinmmProxyMaker\builds'
 Ok @('build',$game,$raw,'version.dll',$out,'--placement','patch','--preset',$preset)
 $package=(Get-ChildItem $out -Directory|Sort-Object Name -Descending|Select-Object -First 1).FullName
 Ok @('install',$package)
 & $cli apply $game $preset;if($LASTEXITCODE -eq 0){throw 'Unloaded installation accepted settings'}
 & $game --expect-font;if($LASTEXITCODE -ne 0){throw 'Nested runtime failed'}
 Ok @('status',$game);Ok @('apply',$game,$preset)
 $ini=[IO.File]::ReadAllText((Join-Path $root 'FontHook.ini'))
 if(!$ini.Contains('PickerHotkeyVK=120')-or !$ini.Contains('PickerHotkeyModifiers=6')-or !$ini.Contains('PickerShowOnStartup=0')){throw 'Hotkey preset missing'}
 & $game --expect-font;if($LASTEXITCODE -ne 0){throw 'Preset runtime failed'}
 $trace=[IO.File]::ReadAllText((Join-Path $root 'FontHook.trace.log'))
 if(!$trace.Contains('vk=120 modifiers=6')){throw 'Native SFH did not consume custom hotkey'}
 Add-Content -LiteralPath (Join-Path $root 'FontHook.ini') -Value 'FontCharSpacing=2' -Encoding UTF8
 Ok @('restore',(Join-Path $root '.WinmmProxyMaker\install.xml'))
 if(!(Test-Path $raw)){throw 'Raw patch not restored'}
 'PASS '+$arch+' nested directory, presets, successful-load gate, hotkey consumption and rollback'
}
Add-Type -AssemblyName System.Windows.Forms
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $tool 'PatchFontBridge.exe'))
$flags=[Reflection.BindingFlags]'NonPublic,Instance'
$form=[Activator]::CreateInstance($assembly.GetType('WinmmProxyMaker.MainForm'),$true)
try{
 $form.Opacity=0;$form.ShowInTaskbar=$false;$form.Show();[Windows.Forms.Application]::DoEvents()
 $field=$form.GetType().GetField('game',$flags).GetValue($form);$field.Text=Join-Path $FixtureRoot 'x86\fixture.exe'
 for($i=0;$i -lt 100;$i++){[Windows.Forms.Application]::DoEvents();Start-Sleep -Milliseconds 50;if($form.GetType().GetField('analysis',$flags).GetValue($form)){break}}
 if(!$form.GetType().GetField('analysis',$flags).GetValue($form)){throw 'EXE auto-analysis failed'}
 $form.SelectPage(1);[Windows.Forms.Application]::DoEvents()
 $views=$form.GetType().GetField('viewPanels',$flags).GetValue($form)
 if(!$views[1].Visible-or $views[0].Visible){throw 'Navigation failed'}
 $form.SelectPage(2);[Windows.Forms.Application]::DoEvents()
 if(!$views[2].Visible){throw 'Help page not visible'}
 $form.Close()
}finally{$form.Dispose()}
'QOL CHECKS PASSED'
