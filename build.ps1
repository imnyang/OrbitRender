param(
    [string]$GameDir = 'L:\SteamLibrary\steamapps\common\A Dance of Fire and Ice',
    [string]$MSBuildPath,
    [string]$FfmpegPath = 'ffmpeg',
    [switch]$Test,
    [switch]$Copy
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
Set-StrictMode -Version Latest
Set-Location $PSScriptRoot

# Build dependencies remain local and ignored. No game DLL or FFmpeg binary
# is redistributed; FFmpeg is installed by the mod after first-launch consent.
function Get-Package([string]$Id, [string]$Version, [string]$Destination, [string]$Expected) {
    if (Test-Path -LiteralPath (Join-Path $Destination $Expected)) { return }

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null

    $archive = Join-Path $Destination 'package.zip'
    Invoke-WebRequest `
        "https://api.nuget.org/v3-flatcontainer/$Id/$Version/$Id.$Version.nupkg" `
        -OutFile $archive

    Expand-Archive -LiteralPath $archive -DestinationPath $Destination -Force
    Remove-Item -LiteralPath $archive

    if (!(Test-Path -LiteralPath (Join-Path $Destination $Expected))) {
        throw "Package $Id is incomplete."
    }
}

Get-Package `
    'unitymodmanager' `
    '0.32.4' `
    'packages/UnityModManager' `
    'lib/net35/UnityModManager.dll'

Get-Package `
    'microsoft.netframework.referenceassemblies.net48' `
    '1.0.3' `
    'packages/net48' `
    'build/.NETFramework/v4.8/mscorlib.dll'

function Get-LoaderArchive([string]$Url, [string]$Destination, [string]$Expected) {
    if (Test-Path -LiteralPath (Join-Path $Destination $Expected)) { return }
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    $archive = Join-Path $Destination 'loader.zip'
    Invoke-WebRequest $Url -OutFile $archive
    Expand-Archive -LiteralPath $archive -DestinationPath $Destination -Force
    Remove-Item -LiteralPath $archive
    if (!(Test-Path -LiteralPath (Join-Path $Destination $Expected))) {
        throw "Loader archive is incomplete: $Url"
    }
}

# Reference each loader's own Harmony ABI; loader binaries stay in packages.
Get-LoaderArchive `
    'https://github.com/LavaGang/MelonLoader/releases/download/v0.6.6/MelonLoader.x64.zip' `
    'packages/MelonLoader' 'MelonLoader/net35/MelonLoader.dll'
Get-LoaderArchive `
    'https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.2/BepInEx_win_x64_5.4.23.2.zip' `
    'packages/BepInEx' 'BepInEx/core/BepInEx.dll'

if (!(Test-Path 'packages/0Harmony.dll')) {
    Get-Package `
        'lib.harmony' `
        '2.2.2' `
        'packages/Harmony' `
        'lib/net48/0Harmony.dll'

    Copy-Item `
        -LiteralPath 'packages/Harmony/lib/net48/0Harmony.dll' `
        -Destination 'packages/0Harmony.dll'
}

if (!$MSBuildPath) {
    $command = Get-Command MSBuild.exe -ErrorAction SilentlyContinue

    if ($command) {
        $MSBuildPath = $command.Source
    }
    else {
        $rider = Get-ChildItem `
            "${env:ProgramFiles}/JetBrains" `
            -Filter 'JetBrains Rider *' `
            -Directory `
            -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending |
            Select-Object -First 1

        if ($rider) {
            $MSBuildPath = Join-Path `
                $rider.FullName `
                'tools/MSBuild/Current/Bin/MSBuild.exe'
        }
    }
}

if (!$MSBuildPath -or !(Test-Path -LiteralPath $MSBuildPath)) {
    throw 'Pass -MSBuildPath with Visual Studio or Rider MSBuild.exe.'
}

if (!(Test-Path -LiteralPath "$GameDir/A Dance of Fire and Ice_Data/Managed/Assembly-CSharp.dll")) {
    throw 'Pass -GameDir with your ADOFAI installation.'
}

& $MSBuildPath `
    OrbitRender.sln `
    /restore `
    /t:Rebuild `
    /p:Configuration=Release `
    "/p:GameDir=$GameDir" `
    /v:minimal `
    /nologo

if ($LASTEXITCODE -ne 0) {
    throw 'Mod build failed.'
}

foreach ($loader in @('MelonLoader', 'BepInEx')) {
    & $MSBuildPath OrbitRender/OrbitRender.csproj /t:Rebuild /p:Configuration=Release `
        "/p:GameDir=$GameDir" "/p:ModLoader=$loader" /v:minimal /nologo
    if ($LASTEXITCODE -ne 0) { throw "$loader build failed." }
}

# Remove FFmpeg artifacts produced by older versions of this build script.
# The exact Release/FFmpeg directory is generated output, not user data.
$releaseDirectory = Join-Path $PSScriptRoot 'OrbitRender/bin/Release'
$oldFfmpegDirectory = Join-Path $releaseDirectory 'FFmpeg'

if (Test-Path -LiteralPath $oldFfmpegDirectory -PathType Container) {
    Remove-Item -LiteralPath $oldFfmpegDirectory -Recurse -Force
}

foreach ($staleName in @(
    'ffmpeg.exe',
    'ffprobe.exe',
    'ffmpeg',
    'ffprobe',
    'FFmpeg-LICENSE.txt',
    'FFmpeg-README.txt'
)) {
    $stalePath = Join-Path $releaseDirectory $staleName

    if (Test-Path -LiteralPath $stalePath -PathType Leaf) {
        Remove-Item -LiteralPath $stalePath -Force
    }
}

if ($Test) {
    & $MSBuildPath `
        Tests/RendererTests.csproj `
        /t:Rebuild `
        /v:minimal `
        /nologo

    if ($LASTEXITCODE -ne 0) {
        throw 'Test build failed.'
    }

    foreach ($relativePath in @('OrbitRender.dll', 'MelonLoader/OrbitRender.dll', 'BepInEx/OrbitRender.dll')) {
        & ./Tests/bin/Release/RendererTests.exe --user-presets `
            (Join-Path $releaseDirectory $relativePath) `
            (Join-Path $GameDir 'A Dance of Fire and Ice_Data/Managed')
        if ($LASTEXITCODE -ne 0) { throw "User preset/settings tests failed: $relativePath" }
    }

    $testOutput = Join-Path `
        $env:TEMP `
        ('orbit-render-tests-' + [guid]::NewGuid().ToString('N'))

    & ./Tests/bin/Release/RendererTests.exe `
        $FfmpegPath `
        $testOutput

    if ($LASTEXITCODE -ne 0) {
        throw 'Renderer tests failed.'
    }

    Write-Host "Test videos: $testOutput"
}

Write-Host "Mod output: $releaseDirectory"

# Package only mod-owned files, never game/loader DLLs or nested build outputs.
$buildsDirectory = Join-Path $PSScriptRoot 'Builds'
New-Item -ItemType Directory -Force -Path $buildsDirectory | Out-Null
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$packageErrors = @()
foreach ($loader in @('UMM', 'MelonLoader', 'BepInEx')) {
    $source = if ($loader -eq 'UMM') { $releaseDirectory } else { Join-Path $releaseDirectory $loader }
    $zipName = if ($loader -eq 'UMM') { 'OrbitRender.zip' } else { "OrbitRender-$loader.zip" }
    $zipPath = Join-Path $buildsDirectory $zipName
    if (Test-Path -LiteralPath ($zipPath + '.tmp')) { Remove-Item -LiteralPath ($zipPath + '.tmp') -Force }
    $zip = [System.IO.Compression.ZipFile]::Open($zipPath + '.tmp', [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($name in @('OrbitRender.dll', 'LICENSE.md', 'Info.json', 'Localization/en.ftl', 'Localization/ko.ftl')) {
            if ($name -eq 'Info.json' -and $loader -ne 'UMM') { continue }
            $file = Join-Path $source $name
            if (!(Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing package file: $file" }
            $entryName = switch ($loader) {
                'UMM' { "OrbitRender/$name" }
                'BepInEx' { "BepInEx/plugins/OrbitRender/$name" }
                'MelonLoader' {
                    if ($name -eq 'OrbitRender.dll') { 'Mods/OrbitRender.dll' }
                    else { "Mods/OrbitRender/$name" }
                }
            }
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file, $entryName) | Out-Null
        }
    }
    finally { $zip.Dispose() }
    try { [System.IO.File]::Copy($zipPath + '.tmp', $zipPath, $true) }
    catch [System.IO.IOException] {
        $packageErrors += "Could not replace $zipPath. Close it in your archive viewer and build again. The new package remains at $zipPath.tmp. $($_.Exception.Message)"
        continue
    }
    Remove-Item -LiteralPath ($zipPath + '.tmp') -Force
    Write-Host "Package: $zipPath"
}
if ($packageErrors.Count -gt 0) { throw ($packageErrors -join [Environment]::NewLine) }

if ($Copy) {
    $modDirectory = Join-Path $GameDir 'Mods/OrbitRender'

    # Stop ADOFAI before replacing mod files.
    Write-Host 'Stopping ADOFAI...'

    $adofaiProcesses = Get-Process -ErrorAction SilentlyContinue |
        Where-Object {
            $_.ProcessName -eq 'A Dance of Fire and Ice'
        }

    if ($adofaiProcesses) {
        $adofaiProcesses | Stop-Process -Force

        # Wait until the process is fully terminated so DLLs are unlocked.
        $adofaiProcesses | ForEach-Object {
            try {
                $_.WaitForExit()
            }
            catch {
                # Process may already be gone.
            }
        }

        Write-Host 'ADOFAI stopped.'
    }
    else {
        Write-Host 'ADOFAI is not running.'
    }

    Write-Host "Copying OrbitRender to: $modDirectory"

    New-Item `
        -ItemType Directory `
        -Force `
        -Path $modDirectory |
        Out-Null

    foreach ($name in @('OrbitRender.dll', 'Info.json', 'LICENSE.md', 'Localization')) {
        Copy-Item -LiteralPath (Join-Path $releaseDirectory $name) -Destination $modDirectory -Recurse -Force
    }

    Write-Host 'OrbitRender copied successfully.'

    Write-Host 'Launching ADOFAI...'
    Start-Process 'steam://rungameid/977950'
}
