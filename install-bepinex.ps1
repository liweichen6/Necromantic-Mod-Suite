<#
.SYNOPSIS
    Installs or upgrades BepInEx 5 (x64) in the Necromantic folder.
.DESCRIPTION
    1. Finds the game folder (tools\GameDir.ps1: -GameDir, NECROMANTIC_GAME_DIR, or Steam).
    2. Asks the GitHub API for the latest stable BepInEx 5.x release (or -Version).
    3. Downloads BepInEx_win_x64_<ver>.zip and verifies its SHA-256 against GitHub's digest.
    4. Backs up BepInEx\config and BepInEx\plugins to <GameDir>\BepInEx_backup_<time>.zip.
    5. Replaces BepInEx\core (stale files from older versions are removed), winhttp.dll and
       doorstop_config.ini, and clears BepInEx\cache. Plugins and configs are left alone.
    6. Ensures HideManagerGameObject = true in BepInEx.cfg (required by this game).
.EXAMPLE
    .\install-bepinex.ps1 -DryRun          # show what would happen, change nothing
    .\install-bepinex.ps1                  # install / upgrade to latest stable 5.x
    .\install-bepinex.ps1 -Version 5.4.23.6
#>
[CmdletBinding()]
param(
    [string]$GameDir,
    [string]$Version,
    [switch]$Force,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"   # Invoke-WebRequest is very slow with the progress bar
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$RepoRoot = $PSScriptRoot
. (Join-Path $RepoRoot "tools\GameDir.ps1")
$GameDir = Resolve-GameDir -GameDir $GameDir
Write-Host "Game folder: $GameDir" -ForegroundColor Cyan

# --- Installed version ---
$coreDll = Join-Path $GameDir "BepInEx\core\BepInEx.dll"
$installed = $null
if (Test-Path $coreDll) { $installed = (Get-Item $coreDll).VersionInfo.FileVersion }
Write-Host ("Installed BepInEx: " + $(if ($installed) { $installed } else { "none" }))

# --- Pick release ---
$headers = @{ "User-Agent" = "Necromantic-Mod-Suite-installer"; "Accept" = "application/vnd.github+json" }
$releases = Invoke-RestMethod -Uri "https://api.github.com/repos/BepInEx/BepInEx/releases?per_page=50" -Headers $headers
$release = $releases |
    Where-Object { -not $_.prerelease -and -not $_.draft -and $_.tag_name -match '^v5\.' } |
    Where-Object { -not $Version -or $_.tag_name -eq "v$($Version.TrimStart('v'))" } |
    Select-Object -First 1
if (-not $release) { throw "No stable BepInEx 5 release found" + $(if ($Version) { " matching '$Version'" } else { "" }) + "." }

# 5.4.23+ names it BepInEx_win_x64_<ver>.zip; older releases used BepInEx_x64_<ver>.zip.
$asset = $release.assets | Where-Object { $_.name -match '^BepInEx_(win_)?x64_[\d\.]+\.zip$' } | Select-Object -First 1
if (-not $asset) { throw "Release $($release.tag_name) has no Windows x64 zip." }
$target = $release.tag_name.TrimStart('v')
Write-Host "Target BepInEx:    $target ($($asset.name), $([math]::Round($asset.size / 1KB)) KB, published $($release.published_at.ToString().Substring(0,10)))"

# FileVersion of 5.4.23.6 is "5.4.23.6"; older builds report e.g. "5.4.22.0" for 5.4.22.
function Normalize([string]$v) { if (-not $v) { return "" }; $p = $v.Split('.'); while ($p.Count -lt 4) { $p += "0" }; return ($p[0..3] -join '.') }
if (-not $Force -and (Normalize $installed) -eq (Normalize $target)) {
    Write-Host "Already up to date. Use -Force to reinstall." -ForegroundColor Green
    exit 0
}

if ($DryRun) {
    Write-Host "`n[DryRun] Would download $($asset.browser_download_url)" -ForegroundColor Yellow
    Write-Host "[DryRun] Would verify $($asset.digest)" -ForegroundColor Yellow
    Write-Host "[DryRun] Would back up BepInEx\config + BepInEx\plugins, replace BepInEx\core, winhttp.dll, doorstop_config.ini" -ForegroundColor Yellow
    exit 0
}

if (Get-Process -Name "Necromantic" -ErrorAction SilentlyContinue) {
    throw "Necromantic is running; close the game first."
}
if (-not $asset.digest -or $asset.digest -notmatch '^sha256:([0-9a-f]{64})$') {
    throw "GitHub has no SHA-256 digest for $($asset.name) (older releases lack one); refusing to install an unverified download. Use a newer -Version."
}
$expectedHash = $Matches[1]

$work = Join-Path ([System.IO.Path]::GetTempPath()) ("bepinex_" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $work | Out-Null
try {
    # --- Download + verify ---
    $zip = Join-Path $work $asset.name
    Write-Host ">> Downloading $($asset.name)" -ForegroundColor Yellow
    Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zip -UseBasicParsing -Headers @{ "User-Agent" = $headers["User-Agent"] }
    $actual = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $expectedHash) { throw "SHA-256 mismatch for $($asset.name): expected $expectedHash, got $actual." }
    Write-Host "   SHA-256 verified" -ForegroundColor Green

    $extract = Join-Path $work "extract"
    Expand-Archive -Path $zip -DestinationPath $extract
    if (-not (Test-Path (Join-Path $extract "BepInEx\core\BepInEx.dll"))) { throw "Unexpected archive layout (no BepInEx\core\BepInEx.dll)." }

    # --- Backup user data ---
    $bepDir = Join-Path $GameDir "BepInEx"
    $toBackup = @("config", "plugins") | ForEach-Object { Join-Path $bepDir $_ } | Where-Object { Test-Path $_ }
    if ($toBackup) {
        $backup = Join-Path $GameDir ("BepInEx_backup_" + (Get-Date -Format "yyyyMMdd_HHmmss") + ".zip")
        Compress-Archive -Path $toBackup -DestinationPath $backup
        Write-Host "   Backed up config + plugins to $backup" -ForegroundColor Green
    }

    # --- Install ---
    Write-Host ">> Installing BepInEx $target" -ForegroundColor Yellow
    foreach ($stale in @("core", "cache")) {
        $p = Join-Path $bepDir $stale
        if (Test-Path $p) { Remove-Item $p -Recurse -Force }
    }
    Get-ChildItem $extract -Force | ForEach-Object {
        Copy-Item $_.FullName -Destination $GameDir -Recurse -Force
    }
    foreach ($d in @("plugins", "config", "patchers")) {
        New-Item -ItemType Directory -Path (Join-Path $bepDir $d) -Force | Out-Null
    }

    # --- Required config ---
    $cfg = Join-Path $bepDir "config\BepInEx.cfg"
    if (-not (Test-Path $cfg)) {
        # Ship the suite's defaults; BepInEx fills in any missing keys on first launch.
        Copy-Item (Join-Path $RepoRoot "config\BepInEx.cfg") $cfg
    } else {
        $text = Get-Content $cfg -Raw
        if ($text -match '(?m)^HideManagerGameObject\s*=\s*false') {
            [System.IO.File]::WriteAllText($cfg, ($text -replace '(?m)^HideManagerGameObject\s*=\s*false', 'HideManagerGameObject = true'))
        }
    }

    $now = (Get-Item (Join-Path $bepDir "core\BepInEx.dll")).VersionInfo.FileVersion
    Write-Host "`nBepInEx $now installed in $GameDir" -ForegroundColor Cyan
    if (-not (Test-Path (Join-Path $bepDir "plugins\NecromanticCommon.dll"))) {
        Write-Host "Next: .\deploy-to-game.ps1 to install the mod suite." -ForegroundColor Cyan
    }
}
finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
