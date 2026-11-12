<#
.SYNOPSIS
    Builds NecromanticCommon and all 5 plugins into .\bin using the .NET Framework csc.exe.
.DESCRIPTION
    References are taken from the game install, so the game and BepInEx 5 must be
    installed. The game folder is found automatically via Steam (see tools\GameDir.ps1);
    override with -GameDir or the NECROMANTIC_GAME_DIR environment variable.
    Nothing is written to the game folder unless -Deploy is passed, in which case
    deploy-to-game.ps1 runs after a successful build. -Package writes a release zip to
    .\dist laid out to be extracted straight into the game folder (for GitHub Releases).
.EXAMPLE
    .\build-all.ps1
    .\build-all.ps1 -Deploy
    .\build-all.ps1 -Package
    .\build-all.ps1 -GameDir "E:\Games\Necromantic" -Deploy
#>
[CmdletBinding()]
param(
    [string]$GameDir,
    [switch]$Deploy,
    [switch]$Package
)

$ErrorActionPreference = "Stop"
$RepoRoot = $PSScriptRoot
. (Join-Path $RepoRoot "tools\GameDir.ps1")
$GameDir  = Resolve-GameDir -GameDir $GameDir
Write-Host "Game folder: $GameDir" -ForegroundColor Cyan
$BinDir   = Join-Path $RepoRoot "bin"
$Managed  = Join-Path $GameDir "Necromantic_Data\Managed"
$Core     = Join-Path $GameDir "BepInEx\core"

if (-not (Test-Path $Managed)) { throw "Game assemblies not found at '$Managed'. Pass -GameDir." }
if (-not (Test-Path (Join-Path $Core "BepInEx.dll"))) { throw "BepInEx 5 not found at '$Core'. Run .\install-bepinex.ps1 first." }

$Csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $Csc)) { $Csc = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe" }
if (-not (Test-Path $Csc)) { throw "csc.exe (.NET Framework 4.x) not found." }

New-Item -ItemType Directory -Path $BinDir -Force | Out-Null

# One shared reference set; unused references are harmless.
$CommonRefs = @(
    "$Managed\mscorlib.dll", "$Managed\netstandard.dll", "$Managed\System.dll", "$Managed\System.Core.dll",
    "$Core\BepInEx.dll", "$Core\0Harmony.dll",
    "$Managed\UnityEngine.dll", "$Managed\UnityEngine.CoreModule.dll", "$Managed\UnityEngine.IMGUIModule.dll",
    "$Managed\UnityEngine.TextRenderingModule.dll", "$Managed\UnityEngine.InputLegacyModule.dll",
    "$Managed\UnityEngine.PhysicsModule.dll", "$Managed\UnityEngine.UI.dll", "$Managed\UnityEngine.UIModule.dll"
)
$GameRefs = @(
    "$Managed\Assembly-CSharp.dll", "$Managed\Assembly-CSharp-firstpass.dll", "$Managed\Rewired_Core.dll",
    "$Managed\Fusion.Runtime.dll", "$Managed\Fusion.Common.dll", "$Managed\Fusion.Sockets.dll", "$Managed\Cinemachine.dll",
    "$Managed\BGDatabase.dll", "$Managed\Newtonsoft.Json.dll",
    "$BinDir\NecromanticCommon.dll"
)

# Order matters: Common first, every plugin references it.
$Projects = @(
    @{ Name = "NecromanticCommon";  Sources = @("src\Common\*.cs");                               Refs = $CommonRefs },
    @{ Name = "NecromanticTrainer"; Sources = @("src\NecromanticTrainer\TrainerPlugin.cs");      Refs = $CommonRefs + $GameRefs },
    @{ Name = "NecromanticQoL";     Sources = @("src\NecromanticQoL\*.cs");                      Refs = $CommonRefs + $GameRefs },
    @{ Name = "NecromanticEndless"; Sources = @("src\NecromanticEndless\EndlessPlugin.cs");      Refs = $CommonRefs + $GameRefs },
    @{ Name = "NecromanticData";    Sources = @("src\NecromanticData\NecromanticDataPlugin.cs"); Refs = $CommonRefs + $GameRefs },
    @{ Name = "NecromanticSave";    Sources = @("src\NecromanticSave\SavePlugin.cs");            Refs = $CommonRefs + $GameRefs }
)

$failed = @()
foreach ($p in $Projects) {
    $out = Join-Path $BinDir "$($p.Name).dll"
    $sources = $p.Sources | ForEach-Object { Get-ChildItem (Join-Path $RepoRoot $_) } | ForEach-Object { "`"$($_.FullName)`"" }
    $refArgs = $p.Refs | ForEach-Object { "/r:`"$_`"" }

    Write-Host ">> $($p.Name)" -ForegroundColor Yellow
    & $Csc /nologo /nowarn:1701 /noconfig /nostdlib /optimize+ /target:library /out:"$out" $refArgs $sources
    if ($LASTEXITCODE -ne 0) {
        Write-Host "   FAILED (exit $LASTEXITCODE)" -ForegroundColor Red
        $failed += $p.Name
        if ($p.Name -eq "NecromanticCommon") { break }  # everything else depends on it
    } else {
        Write-Host "   OK -> bin\$($p.Name).dll ($((Get-Item $out).Length) bytes)" -ForegroundColor Green
    }
}

if ($failed.Count -gt 0) {
    Write-Host "`nBuild failed: $($failed -join ', ')" -ForegroundColor Red
    exit 1
}
Write-Host "`nAll $($Projects.Count) assemblies built into $BinDir" -ForegroundColor Cyan

if ($Package) {
    # Layout mirrors the game folder so users can extract the zip into it directly.
    $distDir = Join-Path $RepoRoot "dist"
    $stage   = Join-Path $distDir "stage"
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    $pluginsOut = Join-Path $stage "BepInEx\plugins"
    $presetsOut = Join-Path $stage "BepInEx\config\NecromanticData\presets"
    New-Item -ItemType Directory -Path $pluginsOut, $presetsOut -Force | Out-Null
    Copy-Item (Join-Path $BinDir "*.dll") $pluginsOut
    Copy-Item (Join-Path $RepoRoot "config\presets\*.json") $presetsOut
    Copy-Item (Join-Path $RepoRoot "LICENSE"), (Join-Path $RepoRoot "README.md") $stage

    $zipPath = Join-Path $distDir ("NecromanticModSuite_" + (Get-Date -Format "yyyy-MM-dd") + ".zip")
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    # Write entries by hand with "/" separators: on Windows PowerShell 5 both Compress-Archive
    # and ZipFile.CreateFromDirectory emit "\", which non-Windows unzip tools mishandle.
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        $stageFull = (Resolve-Path $stage).Path.TrimEnd('\') + '\'
        foreach ($file in Get-ChildItem $stage -Recurse -File) {
            $entryName = $file.FullName.Substring($stageFull.Length).Replace('\', '/')
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entryName) | Out-Null
        }
    } finally {
        $archive.Dispose()
    }
    Remove-Item $stage -Recurse -Force
    Write-Host "Release package: $zipPath" -ForegroundColor Cyan
}

if ($Deploy) {
    & (Join-Path $RepoRoot "deploy-to-game.ps1") -GameDir $GameDir
    if (-not $?) { exit 1 }
}
