<#
.SYNOPSIS
    Locates the Necromantic install folder. Dot-source this file, then call Resolve-GameDir.
.DESCRIPTION
    Resolution order:
      1. An explicit path (the scripts' -GameDir parameter)
      2. The NECROMANTIC_GAME_DIR environment variable
      3. Steam: registry -> <Steam>\steamapps\libraryfolders.vdf -> every library's
         appmanifest_2615010.acf -> steamapps\common\<installdir>
    A candidate is accepted only if it contains Necromantic.exe.
#>

$script:NecromanticAppId = "2615010"

function Get-SteamLibraryPaths {
    $roots = @()
    foreach ($key in @("HKCU:\Software\Valve\Steam", "HKLM:\SOFTWARE\WOW6432Node\Valve\Steam", "HKLM:\SOFTWARE\Valve\Steam")) {
        $props = Get-ItemProperty -Path $key -ErrorAction SilentlyContinue
        if ($props) {
            foreach ($name in @("SteamPath", "InstallPath")) {
                if ($props.$name) { $roots += [System.IO.Path]::GetFullPath($props.$name) }
            }
        }
    }

    $libraries = @()
    foreach ($root in ($roots | Select-Object -Unique)) {
        $libraries += $root
        $vdf = Join-Path $root "steamapps\libraryfolders.vdf"
        if (Test-Path $vdf) {
            # Lines look like:   "path"		"D:\\SteamLibrary"
            foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) {
                $libraries += $m.Groups[1].Value -replace '\\\\', '\'
            }
        }
    }
    # Registry paths differ only in case/slashes from the VDF ones; dedupe case-insensitively.
    $seen = @{}
    foreach ($lib in $libraries) {
        $full = [System.IO.Path]::GetFullPath($lib).TrimEnd('\')
        if (-not $seen.ContainsKey($full.ToLowerInvariant()) -and (Test-Path $full)) {
            $seen[$full.ToLowerInvariant()] = $true
            $full
        }
    }
}

function Find-NecromanticSteamDir {
    $libraries = @(Get-SteamLibraryPaths)
    foreach ($lib in $libraries) {
        $manifest = Join-Path $lib "steamapps\appmanifest_$($script:NecromanticAppId).acf"
        if (Test-Path $manifest) {
            $m = [regex]::Match((Get-Content $manifest -Raw), '"installdir"\s+"([^"]+)"')
            if ($m.Success) {
                $dir = Join-Path $lib ("steamapps\common\" + $m.Groups[1].Value)
                if (Test-Path (Join-Path $dir "Necromantic.exe")) { return $dir }
            }
        }
    }
    # Manifest missing (e.g. copied install): try the conventional folder name.
    foreach ($lib in $libraries) {
        $dir = Join-Path $lib "steamapps\common\Necromantic"
        if (Test-Path (Join-Path $dir "Necromantic.exe")) { return $dir }
    }
    return $null
}

function Resolve-GameDir {
    param([string]$GameDir)

    $candidates = @(
        @{ Source = "-GameDir";              Path = $GameDir },
        @{ Source = "NECROMANTIC_GAME_DIR";  Path = $env:NECROMANTIC_GAME_DIR }
    )
    foreach ($c in $candidates) {
        if ([string]::IsNullOrWhiteSpace($c.Path)) { continue }
        if (Test-Path (Join-Path $c.Path "Necromantic.exe")) { return (Resolve-Path $c.Path).Path }
        throw "Necromantic.exe not found in '$($c.Path)' (from $($c.Source))."
    }

    $found = Find-NecromanticSteamDir
    if ($found) { return $found }

    throw "Could not find Necromantic via Steam (App ID $($script:NecromanticAppId)). Pass -GameDir or set NECROMANTIC_GAME_DIR."
}
