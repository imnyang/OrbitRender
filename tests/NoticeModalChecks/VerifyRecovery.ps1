param(
    [string]$Workspace = (Resolve-Path "$PSScriptRoot/../..").Path,
    [string]$GameDir = 'L:/SteamLibrary/steamapps/common/A Dance of Fire and Ice'
)
$ErrorActionPreference='Stop'
$managed=Join-Path $GameDir 'A Dance of Fire and Ice_Data/Managed'
$isolated=Join-Path $env:TEMP ('OrbitRender-recovery-check-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $isolated | Out-Null
Copy-Item "$workspace\packages\UnityModManager\lib\net35\UnityModManager.dll","$workspace\packages\0Harmony.dll","$workspace\OrbitRender\bin\Release\OrbitRender.dll" $isolated
[void][Reflection.Assembly]::LoadFrom("$managed\UnityEngine.CoreModule.dll")
[void][Reflection.Assembly]::LoadFrom("$managed\UnityEngine.dll")
[void][Reflection.Assembly]::LoadFrom("$managed\Assembly-CSharp.dll")
[void][Reflection.Assembly]::LoadFrom("$isolated\0Harmony.dll")
[void][Reflection.Assembly]::Load([IO.File]::ReadAllBytes("$managed\UnityModManager\dnlib.dll"))
[void][Reflection.Assembly]::LoadFrom("$isolated\UnityModManager.dll")
$assembly=[Reflection.Assembly]::LoadFrom("$isolated\OrbitRender.dll")
$info=New-Object 'UnityModManagerNet.UnityModManager+ModInfo'
$info.Id='OrbitRender.DiagnosticConflict'; $info.DisplayName='Test fixture'; $info.Version='0.0.1'; $info.ManagerVersion='0.30.0'
$mod=New-Object 'UnityModManagerNet.UnityModManager+ModEntry' ($info,$isolated)
$entries = [UnityModManagerNet.UnityModManager].GetField("modEntries").GetValue($null)
$entries.Add($mod)
$flags=[Reflection.BindingFlags]'Static,NonPublic'
$type=$assembly.GetType('OrbitRender.Renderer.ConflictRecovery',$true)
$disable=$type.GetMethod('DisableForNextLaunch',$flags)
[void]$disable.Invoke($null,([object[]]@($mod.PSObject.BaseObject)))
[xml]$saved=Get-Content "$isolated\Params.xml"
if($mod.Enabled -or $saved.Param.ModParams.Mod.Enabled -ne 'false') { throw 'Disabled setting not persisted' }
'Mod disabling PASS: actual UMM Params.xml contains Enabled=false in isolated test folder.'
$mod.Enabled=$true
Move-Item -LiteralPath "$isolated\Params.xml" -Destination "$isolated\Params.saved.xml"
New-Item -ItemType Directory -Path "$isolated\Params.xml" | Out-Null
$failed=$false
try { [void]$disable.Invoke($null,([object[]]@($mod.PSObject.BaseObject))) } catch { $failed=$true }
if(!$failed -or !$mod.Enabled) { throw 'Save failure did not preserve enabled state' }
'Save failure PASS: action fails and restores previous enabled state.'
$script=$type.GetMethod('BuildSteamRestartScript',$flags).Invoke($null,@([int]2147483647))
if (!$script.Contains("Start-Process -FilePath 'steam://rungameid/977950'") -or !$script.Contains('Wait-Process -Timeout 120') -or $script.Contains('.exe')) { throw 'Steam restart target or wait behavior incorrect' }
$marker=Join-Path $isolated 'steam-launch.txt'
$capture="function Start-Process { param([string]`$FilePath); [IO.File]::WriteAllText('" + $marker.Replace("'","''") + "',`$FilePath) }; "
& ([scriptblock]::Create($capture + $script))
if ((Get-Content $marker -Raw) -ne 'steam://rungameid/977950') { throw 'Steam protocol dispatch failed' }
'Steam restart PASS: helper waits for process exit and dispatches the ADOFAI Steam URI; launch intercepted, no game restart executed.'





