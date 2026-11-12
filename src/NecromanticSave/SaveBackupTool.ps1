<#
.SYNOPSIS
    Necromantic Offline Save Backup & Restore Tool
.DESCRIPTION
    Standalone CLI and interactive utility to list, create, and restore backups
    of the Necromantic save file (BlinkmoonSave.es3) without launching the game.
.PARAMETER Backup
    Creates a new timestamped backup immediately.
.PARAMETER List
    Lists all available backups with timestamps and file sizes.
.PARAMETER Restore
    Restores the specified backup by index (1-based) or filename.
.PARAMETER SaveDir
    Outputs the persistent data save directory path.
.PARAMETER GameDir
    Necromantic install folder (default: found via Steam). Backups go to
    <GameDir>\BepInEx\config\NecromanticSave\Backups (shared with the in-game plugin), or to
    the save folder's Backups\ if the game/BepInEx can't be found.
.EXAMPLE
    .\SaveBackupTool.ps1 -Backup
    .\SaveBackupTool.ps1 -List
    .\SaveBackupTool.ps1 -Restore 1
    .\SaveBackupTool.ps1
#>

[CmdletBinding()]
param (
    [switch]$Backup,
    [switch]$List,
    [string]$Restore,
    [switch]$SaveDir,
    [string]$GameDir
)

# --- Path Resolution ---
# Find the game folder via Steam (tools\GameDir.ps1) unless -GameDir / NECROMANTIC_GAME_DIR
# is given. Best-effort: without it, backups fall back to the save folder.
$resolver = Join-Path $PSScriptRoot "..\..\tools\GameDir.ps1"
if (Test-Path $resolver) {
    . $resolver
    try { $GameDir = Resolve-GameDir -GameDir $GameDir } catch { Write-Warning $_.Exception.Message }
}
if ([string]::IsNullOrWhiteSpace($GameDir)) { $GameDir = "." }

$SaveDirectory = Join-Path $env:USERPROFILE "AppData\LocalLow\Blinkmoon\Necromantic"
$SaveFile = Join-Path $SaveDirectory "BlinkmoonSave.es3"

# Primary backup directory: the same folder the in-game NecromanticSave plugin uses.
$PrimaryBackupDir = Join-Path $GameDir "BepInEx\config\NecromanticSave\Backups"
$FallbackBackupDir = Join-Path $SaveDirectory "Backups"

$BackupDirectory = if (Test-Path (Join-Path $GameDir "BepInEx\config")) { $PrimaryBackupDir } else { $FallbackBackupDir }
if (-not (Test-Path $BackupDirectory)) {
    New-Item -ItemType Directory -Path $BackupDirectory -Force | Out-Null
}

function Check-GameRunning {
    $proc = Get-Process -Name "Necromantic" -ErrorAction SilentlyContinue
    return ($null -ne $proc)
}

function Show-Header {
    Clear-Host
    Write-Host "================================================================" -ForegroundColor Cyan
    Write-Host "         Necromantic Save Backup & Restore Tool (Offline)       " -ForegroundColor Yellow
    Write-Host "================================================================" -ForegroundColor Cyan
    
    # Save file status
    if (Test-Path $SaveFile) {
        $item = Get-Item $SaveFile
        $kb = [math]::Round($item.Length / 1KB, 1)
        Write-Host " Active Save : $SaveFile" -ForegroundColor Gray
        Write-Host " Status      : " -NoNewline
        Write-Host "EXISTS" -ForegroundColor Green -NoNewline
        Write-Host " ($kb KB, Last Modified: $($item.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')))" -ForegroundColor Gray
    } else {
        Write-Host " Active Save : $SaveFile" -ForegroundColor Gray
        Write-Host " Status      : " -NoNewline
        Write-Host "NOT FOUND" -ForegroundColor Red
    }

    # Backup directory
    Write-Host " Backup Dir  : $BackupDirectory" -ForegroundColor Gray

    # Game status
    if (Check-GameRunning) {
        Write-Host " Game Status : " -NoNewline
        Write-Host "RUNNING (WARNING: Close game before restoring!)" -ForegroundColor Yellow
    } else {
        Write-Host " Game Status : " -NoNewline
        Write-Host "STOPPED (Safe to modify save files)" -ForegroundColor Green
    }
    Write-Host "----------------------------------------------------------------" -ForegroundColor Cyan
}

function Get-Backups {
    $dirs = @($BackupDirectory)
    if ((Test-Path $FallbackBackupDir) -and ($FallbackBackupDir -ne $BackupDirectory)) {
        $dirs += $FallbackBackupDir
    }

    $allFiles = @()
    foreach ($d in $dirs) {
        if (Test-Path $d) {
            $allFiles += Get-ChildItem -Path $d -Filter "*.es3" -File
        }
    }

    # Sort descending by LastWriteTime
    return ($allFiles | Sort-Object LastWriteTime -Descending)
}

function Show-BackupsList {
    $backups = Get-Backups
    if ($backups.Count -eq 0) {
        Write-Host "No backups found in $BackupDirectory" -ForegroundColor Yellow
        return $backups
    }

    Write-Host "`nAvailable Backups ($($backups.Count) found, newest first):" -ForegroundColor White
    Write-Host ("{0,-4} {1,-35} {2,-12} {3,-20}" -f "#", "Backup Filename", "Size", "Created / Modified") -ForegroundColor DarkCyan
    Write-Host ("-" * 75) -ForegroundColor DarkGray

    for ($i = 0; $i -lt $backups.Count; $i++) {
        $b = $backups[$i]
        $kb = [math]::Round($b.Length / 1KB, 1)
        $sizeStr = "$kb KB"
        $dateStr = $b.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")
        Write-Host ("[{0,2}] {1,-35} {2,-12} {3,-20}" -f ($i + 1), $b.Name, $sizeStr, $dateStr) -ForegroundColor White
    }
    return $backups
}

function New-SaveBackup([string]$Prefix = "BlinkmoonSave") {
    if (-not (Test-Path $SaveFile)) {
        Write-Host "ERROR: Active save file not found at: $SaveFile" -ForegroundColor Red
        return $null
    }

    $timestamp = (Get-Date).ToString("yyyyMMdd_HHmmss")
    $backupFileName = "${Prefix}_${timestamp}.es3"
    $destination = Join-Path $BackupDirectory $backupFileName

    try {
        Copy-Item -Path $SaveFile -Destination $destination -Force
        $kb = [math]::Round((Get-Item $destination).Length / 1KB, 1)
        Write-Host "SUCCESS: Backup created successfully!" -ForegroundColor Green
        Write-Host "File : $backupFileName ($kb KB)" -ForegroundColor Cyan
        Write-Host "Path : $destination" -ForegroundColor Gray
        return $destination
    } catch {
        Write-Host "ERROR: Failed to create backup: $_" -ForegroundColor Red
        return $null
    }
}

function Restore-SaveBackup($TargetIdentifier) {
    if (Check-GameRunning) {
        Write-Host "`nWARNING: Necromantic.exe is currently running!" -ForegroundColor Yellow
        Write-Host "Restoring a save while the game is running will cause sync issues or immediate overwrites." -ForegroundColor Yellow
        $confirm = Read-Host "Are you sure you want to proceed anyway? (y/N)"
        if ($confirm -ne 'y' -and $confirm -ne 'Y') {
            Write-Host "Restore cancelled." -ForegroundColor Yellow
            return $false
        }
    }

    $backups = Get-Backups
    if ($backups.Count -eq 0) {
        Write-Host "ERROR: No backups available to restore." -ForegroundColor Red
        return $false
    }

    $targetFile = $null

    # Check if TargetIdentifier is an integer index (1-based)
    $index = 0
    if ([int]::TryParse($TargetIdentifier, [ref]$index)) {
        if ($index -ge 1 -and $index -le $backups.Count) {
            $targetFile = $backups[$index - 1]
        } else {
            Write-Host "ERROR: Invalid backup index $index. Must be between 1 and $($backups.Count)." -ForegroundColor Red
            return $false
        }
    } else {
        # Search by filename
        $match = $backups | Where-Object { $_.Name -like "*$TargetIdentifier*" } | Select-Object -First 1
        if ($match) {
            $targetFile = $match
        } else {
            Write-Host "ERROR: Backup matching '$TargetIdentifier' was not found." -ForegroundColor Red
            return $false
        }
    }

    try {
        # 1. Safety backup of existing active save
        if (Test-Path $SaveFile) {
            $preRestoreTimestamp = (Get-Date).ToString("yyyyMMdd_HHmmss")
            $preRestoreFile = Join-Path $BackupDirectory "PreRestore_BlinkmoonSave_${preRestoreTimestamp}.es3"
            Copy-Item -Path $SaveFile -Destination $preRestoreFile -Force
            Write-Host "Safety pre-restore backup created: $(Split-Path $preRestoreFile -Leaf)" -ForegroundColor Gray
        }

        # 2. Copy selected backup over active save
        Copy-Item -Path $targetFile.FullName -Destination $SaveFile -Force
        Write-Host "SUCCESS: Save restored successfully!" -ForegroundColor Green
        Write-Host "Restored From: $($targetFile.Name)" -ForegroundColor Cyan
        Write-Host "Restored To  : $SaveFile" -ForegroundColor Gray
        return $true
    } catch {
        Write-Host "ERROR: Failed to restore backup: $_" -ForegroundColor Red
        return $false
    }
}

# --- Command-Line Switch Dispatch ---

if ($SaveDir) {
    Write-Host $SaveDirectory
    exit 0
}

if ($Backup) {
    $res = New-SaveBackup
    if ($res) { exit 0 } else { exit 1 }
}

if ($List) {
    $b = Show-BackupsList
    exit 0
}

if ($Restore) {
    $ok = Restore-SaveBackup $Restore
    if ($ok) { exit 0 } else { exit 1 }
}

# --- Interactive Mode ---

while ($true) {
    Show-Header
    Write-Host "`n[1] List Available Backups" -ForegroundColor White
    Write-Host "[2] Create Backup Now" -ForegroundColor White
    Write-Host "[3] Restore a Backup" -ForegroundColor White
    Write-Host "[4] Open Save Directory in File Explorer" -ForegroundColor White
    Write-Host "[5] Open Backups Directory in File Explorer" -ForegroundColor White
    Write-Host "[0] Exit" -ForegroundColor White
    Write-Host ""

    $choice = Read-Host "Select an option [0-5]"
    switch ($choice) {
        "1" {
            Show-BackupsList | Out-Null
            Write-Host "`nPress any key to return to menu..." -ForegroundColor Gray
            [Console]::ReadKey($true) | Out-Null
        }
        "2" {
            Write-Host ""
            New-SaveBackup | Out-Null
            Write-Host "`nPress any key to return to menu..." -ForegroundColor Gray
            [Console]::ReadKey($true) | Out-Null
        }
        "3" {
            $backups = Show-BackupsList
            if ($backups.Count -gt 0) {
                Write-Host ""
                $target = Read-Host "Enter backup # to restore (or filename, 'c' to cancel)"
                if ($target -ne 'c' -and $target -ne 'C' -and $target -ne '') {
                    Restore-SaveBackup $target | Out-Null
                }
            }
            Write-Host "`nPress any key to return to menu..." -ForegroundColor Gray
            [Console]::ReadKey($true) | Out-Null
        }
        "4" {
            if (Test-Path $SaveDirectory) {
                Invoke-Item $SaveDirectory
            } else {
                Write-Host "Save directory does not exist yet." -ForegroundColor Yellow
            }
        }
        "5" {
            if (Test-Path $BackupDirectory) {
                Invoke-Item $BackupDirectory
            }
        }
        "0" {
            Write-Host "Exiting SaveBackupTool." -ForegroundColor Cyan
            exit 0
        }
        default {
            Write-Host "Invalid choice. Please select 0 through 5." -ForegroundColor Yellow
            Start-Sleep -Seconds 1
        }
    }
}
