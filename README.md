# Necromantic Mod Suite

BepInEx 5 plugins for **[Necromantic](https://store.steampowered.com/app/2615010/Necromantic/)**
(Unity 2022.3.31f1, Mono x64): a trainer, a DPS meter and QoL tweaks, endless/nightmare
modes, a data-override framework and a save manager.
The game is pretty much abandoned though, last update on 2024.07.24, Patch Update 10, Release Candidate 0.1.40.

**Built and tested against:**

| | |
| :--- | :--- |
| Game version | **0.1.40** (ReleaseCandidate), Patch Update 10, 2024-07-24 |
| Steam App ID / build ID | 2615010 / **14941003** |
| Engine | Unity 2022.3.31f1, Mono x64 |
| Mod loader | BepInEx 5.4.22 (5.4.23.x expected to work) |

To check yours: the game's `Player.log` (`%USERPROFILE%\AppData\LocalLow\Blinkmoon\Necromantic\`)
prints `THIS BUILD VERSION: #0.1.40`, and Steam's `steamapps\appmanifest_2615010.acf` has the
`buildid`. If the game ever updates and a hooked method changes, that plugin logs
`Failed to apply Harmony patches` (or similar) in `BepInEx\LogOutput.log`.

| Plugin | GUID | Version | What it does |
| :--- | :--- | :--- | :--- |
| **NecromanticTrainer** | `com.necromantic.trainer` | 2.1.0 | Trainer window (Insert): god mode, one-hit kill, damage/move/attack-speed/projectile multipliers, nuke, vacuum, chest opener, spawner freeze, clock skip, infinite rerolls, card banishing, unlocks, the game's own developer debug menu, camera zoom, game speed, one-click "turn off all cheats", in-game hotkey rebinding. |
| **NecromanticQoL** | `com.necromantic.qol` | 1.0.0 | Live DPS / per-ability damage HUD (F9), minimap (M), solo instant restart (Ctrl+R), intro splash skip. |
| **NecromanticEndless** | `com.necromantic.endless` | 1.0.0 | Endless & Nightmare modes (F10): no forced victory, timer extension, wave/event looping, per-minute mob scaling. |
| **NecromanticData** | `com.necromantic.data` | 1.0.0 | Exports all 36 BGDatabase tables to JSON and applies JSON delta overrides / balance presets in memory. |
| **NecromanticSave** | `com.necromantic.save` | 1.0.0 | Save manager & editor (F11): automatic timestamped backups, restore, progression editing, JSON export/import. Plus an offline backup CLI. |

All of them except Data depend on **`NecromanticCommon.dll`**, a shared library (not a
plugin) with the hotkey reader, the on-screen notification banner, and the `ModHost`
startup fix described below.

> **Single-player only.** The Trainer switches itself off (and resets every cheat) as soon
> as a co-op session starts, and instant restart is blocked in co-op. Endless/Nightmare
> modes and data presets change the run for everyone in the lobby when you host, so only
> use them with players who agreed to it.

## Hotkeys

| Key | Plugin | Action |
| :--- | :--- | :--- |
| `Insert` / `Home` | Trainer | Toggle trainer window (not F1: the game uses F1 for its developer debug menu) |
| `F2` / `F3` / `F4` | Trainer | Game speed 1x / 2x / 3x |
| `F5` | Trainer | Kill all enemies on screen |
| `F6` | Trainer | Pull every collectible to the player |
| `F7` | Trainer | Toggle god mode |
| `F8` | Trainer | Toggle super magnet |
| `F9` | QoL | Toggle DPS HUD |
| `M` | QoL | Toggle minimap |
| `F10` | Endless | Toggle Endless / Nightmare window |
| `F11` | Save | Toggle save manager |
| `Ctrl+R` | QoL | Restart the current run (solo only) |

These are the defaults. Every hotkey of every plugin can be changed in-game in the Trainer's
**Hotkeys** tab (click a key, press the new one); changes are saved to the plugins'
`BepInEx\config\com.necromantic.*.cfg` files.

## Install

### From a release (players)

1. Install **BepInEx 5 (x64)** in the game folder, either from the
   [BepInEx releases](https://github.com/BepInEx/BepInEx/releases) or with
   `install-bepinex.ps1` from this repo (below).
2. Download `NecromanticModSuite_<date>.zip` from this repo's Releases page and extract it
   into the game folder (it contains `BepInEx\plugins\*.dll` and the data presets).
3. Start the game. A banner listing the hotkeys appears at the top of the screen.

### From source (with the scripts)

Close the game, then from the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File .\install-bepinex.ps1    # install/upgrade BepInEx 5
powershell -ExecutionPolicy Bypass -File .\build-all.ps1 -Deploy  # build and install the suite
```

- `install-bepinex.ps1` downloads the latest stable BepInEx 5 release from GitHub (or
  `-Version 5.4.23.6`), verifies its SHA-256 against GitHub's published digest, backs up
  `BepInEx\config` and `BepInEx\plugins` to `BepInEx_backup_<time>.zip`, then replaces
  BepInEx's own files. Plugins and configs are kept. Use `-DryRun` to only show what it
  would do; it does nothing if you're already on the target version.
- `deploy-to-game.ps1` (run by `-Deploy`) copies `bin\*.dll` to `BepInEx\plugins\`, default
  configs (existing ones are kept) and the data presets, and sets
  `HideManagerGameObject = true` in `BepInEx.cfg`.

**Finding the game folder.** All scripts locate the install on their own, in this order:
1. `-GameDir "<path>"`
2. the `NECROMANTIC_GAME_DIR` environment variable
3. Steam: the install path from the registry, then every library listed in
   `steamapps\libraryfolders.vdf`, then that library's `appmanifest_2615010.acf`
   (`installdir`) to get `steamapps\common\Necromantic`.

The logic is in `tools\GameDir.ps1`.

## Build

Requires the game + BepInEx installed (assemblies are referenced from there) and the
.NET Framework 4.x `csc.exe` that ships with Windows. Built DLLs are not committed.

```powershell
powershell -ExecutionPolicy Bypass -File .\build-all.ps1            # build into .\bin
powershell -ExecutionPolicy Bypass -File .\build-all.ps1 -Deploy    # build, then deploy
powershell -ExecutionPolicy Bypass -File .\build-all.ps1 -Package   # build + dist\*.zip for a GitHub Release
```

## Troubleshooting: hotkeys do nothing

On this game the BepInEx chainloader runs before the first scene loads, and with BepInEx's
default settings the `BepInEx_Manager` object that hosts all plugins is destroyed during
startup. Plugins still log that they loaded and Harmony patches still work, but no
`Update`/`OnGUI` ever runs, so no hotkeys, HUD or windows.

Every plugin now calls `ModHost.Protect` in `Awake()` to prevent this, and the deploy script
sets `HideManagerGameObject = true`. To confirm, `BepInEx\LogOutput.log` should contain:

```
[ModHost] Plugin update loop is running (hotkeys active).
```

and pressing a hotkey logs `[HotkeyHelper] Hotkey pressed: ...`. See the
[User Guide](docs/USER_GUIDE.md#10-troubleshooting--faq) for more.

## Saves

The game stores progress in `%USERPROFILE%\AppData\LocalLow\Blinkmoon\Necromantic\BlinkmoonSave.es3`
(Easy Save 3). NecromanticSave edits the in-memory `PlayerProgressionManager` data and
writes through the game's own `SaveData()`, never the raw file. Backups go to
`BepInEx\config\NecromanticSave\Backups\`, automatically on launch, on match start, and
before every restore, JSON import and progression reset.

Offline backup tool (game closed):

```powershell
powershell -ExecutionPolicy Bypass -File .\src\NecromanticSave\SaveBackupTool.ps1 -List
powershell -ExecutionPolicy Bypass -File .\src\NecromanticSave\SaveBackupTool.ps1 -Backup
powershell -ExecutionPolicy Bypass -File .\src\NecromanticSave\SaveBackupTool.ps1 -Restore 1
```

## Repository layout

```text
config/                   Default BepInEx + plugin configs, presets/*.json for NecromanticData
src/Common/               NecromanticCommon: ModHost, HotkeyHelper, CanvasOverlay
src/Necromantic*/         One folder per plugin
src/NecromanticSave/SaveBackupTool.ps1   Offline backup/restore CLI
build-all.ps1             Build everything into bin/ (-Deploy to install, -Package for a release zip)
deploy-to-game.ps1        Install bin/ + configs into the game folder
install-bepinex.ps1       Install / upgrade BepInEx 5 from GitHub (SHA-256 verified)
tools/GameDir.ps1         Finds the game folder (-GameDir, env var, or Steam lookup)
docs/USER_GUIDE.md        Every feature, config option and a test checklist
docs/MODDER_GUIDE.md      BGDatabase tables, JSON overrides, ES3 saves, build details, pitfalls, game hook points
```

## License

[MIT](LICENSE). This is an unofficial fan project, not affiliated with or endorsed by
Blinkmoon. No game files are included; you need your own copy of Necromantic.
