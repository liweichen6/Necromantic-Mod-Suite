<#
.SYNOPSIS
    Copies the built DLLs, default configs and data presets into a Necromantic install.
.DESCRIPTION
    - bin\*.dll            -> BepInEx\plugins\   (always overwritten)
    - config\*.cfg         -> BepInEx\config\    (only if missing; user settings are kept)
    - config\presets\*.json-> BepInEx\config\NecromanticData\presets\ (always overwritten)
    - Ensures [Chainloader] HideManagerGameObject = true in BepInEx.cfg. Without it the
      BepInEx manager object is destroyed at startup on this game and no plugin
      hotkeys/windows work.
    The game folder is found automatically via Steam; override with -GameDir or the
    NECROMANTIC_GAME_DIR environment variable.
#>
[CmdletBinding()]
param(
    [string]$GameDir
)

$ErrorActionPreference = "Stop"
$RepoRoot = $PSScriptRoot
. (Join-Path $RepoRoot "tools\GameDir.ps1")
$GameDir = Resolve-GameDir -GameDir $GameDir

if (-not (Test-Path (Join-Path $GameDir "BepInEx\core\BepInEx.dll"))) {
    throw "BepInEx is not installed in '$GameDir'. Run .\install-bepinex.ps1 first."
}

if (Get-Process -Name "Necromantic" -ErrorAction SilentlyContinue) {
    throw "Necromantic is running; close the game first (plugin DLLs are locked while it runs)."
}

$pluginsTarget = Join-Path $GameDir "BepInEx\plugins"
$configTarget  = Join-Path $GameDir "BepInEx\config"
$presetsTarget = Join-Path $configTarget "NecromanticData\presets"
New-Item -ItemType Directory -Path $pluginsTarget, $configTarget, $presetsTarget -Force | Out-Null

Write-Host ">> Plugin DLLs" -ForegroundColor Yellow
$dlls = Get-ChildItem -Path (Join-Path $RepoRoot "bin") -Filter "*.dll"
if ($dlls.Count -eq 0) { throw "No DLLs in bin\. Run build-all.ps1 first." }
foreach ($dll in $dlls) {
    Copy-Item $dll.FullName $pluginsTarget -Force
    Write-Host "   $($dll.Name) ($($dll.Length) bytes)" -ForegroundColor Green
}

Write-Host ">> Config files" -ForegroundColor Yellow
foreach ($cfg in Get-ChildItem -Path (Join-Path $RepoRoot "config") -Filter "*.cfg") {
    $dest = Join-Path $configTarget $cfg.Name
    if (-not (Test-Path $dest)) {
        Copy-Item $cfg.FullName $dest
        Write-Host "   Created $($cfg.Name)" -ForegroundColor Green
    } else {
        Write-Host "   Kept existing $($cfg.Name)" -ForegroundColor Gray
    }
}

$bepCfg = Join-Path $configTarget "BepInEx.cfg"
$text = Get-Content $bepCfg -Raw
if ($text -match '(?m)^HideManagerGameObject\s*=\s*false') {
    $text = $text -replace '(?m)^HideManagerGameObject\s*=\s*false', 'HideManagerGameObject = true'
    [System.IO.File]::WriteAllText($bepCfg, $text)
    Write-Host "   Set HideManagerGameObject = true in BepInEx.cfg" -ForegroundColor Green
}

Write-Host ">> Data presets" -ForegroundColor Yellow
foreach ($p in Get-ChildItem -Path (Join-Path $RepoRoot "config\presets") -Filter "*.json") {
    Copy-Item $p.FullName $presetsTarget -Force
    Write-Host "   $($p.Name)" -ForegroundColor Green
}

Write-Host "`nDeployed to $GameDir" -ForegroundColor Cyan
