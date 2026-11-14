param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/6000.3.25f1/Editor/Unity.exe'
)
$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path "$PSScriptRoot/../..").Path
$fixture = Join-Path $workspace 'packages/NoticeModalChecks'
New-Item -ItemType Directory -Force -Path "$fixture/Assets/UI", "$fixture/Assets/Editor", "$fixture/Packages", "$fixture/ProjectSettings" | Out-Null
Copy-Item -LiteralPath "$workspace/OrbitRender/UI/NoticeModal.cs", "$workspace/OrbitRender/UI/UguiFactory.cs", "$workspace/OrbitRender/UI/UiLayout.cs" -Destination "$fixture/Assets/UI"
Copy-Item -LiteralPath "$PSScriptRoot/Stubs.cs" -Destination "$fixture/Assets"
Copy-Item -LiteralPath "$PSScriptRoot/VerifyNotice.cs" -Destination "$fixture/Assets/Editor"
Copy-Item -LiteralPath "$workspace/OrbitRender/Localization/ko.ftl", "$workspace/OrbitRender/Localization/en.ftl", "$workspace/OrbitRender/Localization/es.ftl" -Destination "$fixture/Assets"
'{"dependencies":{"com.unity.ugui":"2.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.ui":"1.0.0"}}' | Set-Content "$fixture/Packages/manifest.json"
'm_EditorVersion: 6000.3.25f1' | Set-Content "$fixture/ProjectSettings/ProjectVersion.txt"
$process = Start-Process -FilePath $Unity -ArgumentList @('-batchmode', '-projectPath', "`"$fixture`"", '-executeMethod', 'VerifyNotice.Run', '-logFile', "`"$fixture/check.log`"") -WindowStyle Hidden -PassThru
$process.WaitForExit()
if ($process.ExitCode -ne 0) { throw "Unity modal checks failed. See $fixture/check.log" }
Get-Content "$fixture/check.log" | Select-String 'NOTICE CHECK PASS'
