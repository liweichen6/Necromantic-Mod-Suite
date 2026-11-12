# Necromantic Data Framework (v1.0.0)

**Plugin GUID**: `com.necromantic.data`  
**Assembly**: `BepInEx\plugins\NecromanticData.dll`  
**Target Game**: Necromantic (Unity 2022.3.31f1, Mono x64, BepInEx 5.4.x)  

---

## 1. Overview & Architecture

`NecromanticData` is a BepInEx 5 data modding framework for the Unity game *Necromantic*.
The game stores its core gameplay data in a **BGDatabase** (`BansheeGz.BGDatabase.BGRepo` in `BGDatabase.dll`), comprising 36 generated tables under `BGDatabase.Generated` (e.g. `DT_CORE_AbilityDescription`, `DT_CORE_AbilityEffects`, `DT_CORE_Mobs`, `DT_CORE_WaveConfig`, `DT_CORE_PhaseDuration`, `DT_CORE_LevelSettings`, `DT_CORE_Heroes`, `DT_FEAT_GlobalUpgrades`, etc.).

### Key Capabilities:
1. **Automated Reference Export**: On first launch (or every launch with `ForceReExport`), the plugin exports all database tables to `BepInEx\config\NecromanticData\export\<Table>.json` formatted as clean, indented, human-readable JSON. Modders can inspect every row, field name, data type, and default value.
2. **Delta Overrides System**: On launch, the plugin scans `BepInEx\config\NecromanticData\overrides\*.json` and applies overrides in memory. Mod files only need to specify the rows and fields they want to change.
3. **Safe In-Memory Modification**: Changes are applied strictly to the in-memory `BGRepo.I` instance via `BGField.SetValue`. The mod never writes to the game's binary asset files or save files.
4. **Validation & Crash Prevention**: Field names and types are validated against the database schema. Malformed entries, missing rows, and type conversion failures are logged as warnings and skipped without crashing the game.
5. **Configurable Balance Presets**: Ships with 3 curated balance preset packs in `BepInEx\config\NecromanticData\presets\`, disabled by default and toggleable via `BepInEx\config\com.necromantic.data.cfg`.

---

## 2. Hook Timing & Startup Cache Neutralization

### The Challenge:
Analysis of `Assembly-CSharp.dll` revealed that `Blinkmoon.GameSystem.DataTableHookup::InitService()` is an `IInitService` executed during `ServiceManager.ProcessGameStartup`.
During `InitService()`, the game calls:
- `GatherAbilitiesInformation()` (caches into `AbilitiesInfo` and `AbilitiesMobInfo`)
- `GatherMobsInformation()` & `GatherBossesInformation()` (caches into `MobsInfo`)
- `GatherHeroesInformation()` (caches into `HeroesInfo`)
- `GatherChoiceProgressionInformation()` (caches into `ChoiceProgressionInfo`)
- `GatherDataTablePhases()`, `GatherDataTableWeights()`, `GatherDataTableWaves()` (caches into `WavesInfo`)
- `GatherLevelInformation()` (caches into `LevelsInfo`)

If data modifications were applied after `DataTableHookup.InitService()` runs, all gameplay systems reading from `DataTableHookup.Instance` would use stale, unmodded values!

### The Solution:
`NecromanticData` employs a dual-layer timing strategy:
1. **`BGRepo.OnLoad` Event**: Subscribed in `Plugin.Awake()`. Triggers immediately when `BGRepo.Load()` completes.
2. **Harmony Prefix on `DataTableHookup.InitService`**: A `[HarmonyPrefix]` patch on `Blinkmoon.GameSystem.DataTableHookup.InitService` invokes `EnsureOverridesApplied("DataTableHookup.InitService Prefix")`.
This guarantees that **before `DataTableHookup` reads a single row from `BGDatabase`**, all presets and overrides have already been written to `BGRepo.I` in memory.

---

## 3. Configuration & Directory Structure

```text
<GameDir>\
├── BepInEx\
│   ├── config\
│   │   ├── com.necromantic.data.cfg          # BepInEx configuration file
│   │   └── NecromanticData\
│   │       ├── export\                       # Generated on launch (all 36 tables)
│   │       │   ├── CORE_AbilityDescription.json
│   │       │   ├── CORE_AbilityEffects.json
│   │       │   ├── CORE_Mobs.json
│   │       │   ├── CORE_PhaseDuration.json
│   │       │   └── ...
│   │       ├── overrides\                    # Place custom mod overrides here
│   │       │   └── example_custom_mobs.json.example
│   │       └── presets\                      # Shipped balance packs
│   │           ├── weapon_buffs.json
│   │           ├── hard_mode.json
│   │           └── fast_waves.json
│   └── plugins\
│       └── NecromanticData.dll
```

Source lives in `src/NecromanticData/` in the repo. The canonical preset files are
`config/presets/*.json`; the same presets are embedded in the plugin and written out if missing.

### Configuration Options (`com.necromantic.data.cfg`):
- `[Framework] ExportOnStartup` (default: `true`): Exports all tables on launch if not already exported.
- `[Framework] ForceReExport` (default: `false`): Force re-exporting all tables on every launch.
- `[Framework] EnableOverrides` (default: `true`): Apply files from `overrides/*.json`.
- `[Framework] LogDetailedChanges` (default: `true`): Log every field override to `LogOutput.log`.
- `[Presets] EnableWeaponBuffs` (default: `false`): Enables the weapon buff preset.
- `[Presets] EnableHardMode` (default: `false`): Enables the hard mode preset.
- `[Presets] EnableFastWaves` (default: `false`): Enables the fast wave pacing preset.

---

## 4. Writing Overrides

The framework supports two JSON formats:

### Format A: Multi-Table JSON (e.g. `overrides/MyMod.json`)
The root object contains table names (`CORE_Mobs`, `CORE_AbilityEffects`, etc.):
```json
{
  "CORE_Mobs": {
    "Undead_T0_Unarmed": {
      "Health": 25.0,
      "Speed": 2.2
    }
  },
  "CORE_AbilityEffects": {
    "Wpn_Necro_L1": {
      "Value": 20.0,
      "Cooldown": 4.0
    }
  }
}
```

### Format B: Single-Table JSON (e.g. `overrides/CORE_Mobs.json`)
The filename defines the table, and the root object contains row names:
```json
{
  "Undead_T0_Unarmed": {
    "Health": 25.0,
    "Speed": 2.2
  },
  "Undead_T0_BigShield": {
    "Health": 60.0
  }
}
```

### Row & Field Resolution Rules:
- **Table Names**: Supports canonical BGDatabase names (`CORE_Mobs`) or generated names with `DT_` prefix (`DT_CORE_Mobs`), case-insensitive.
- **Row Matching**: Matches by row `name` (e.g. `Undead_T0_Unarmed`), `BGId` (16-byte base64/hex ID), or compound `Name_ID`.
- **Field Matching**: Matches field names case-insensitively (`Health`, `health`). Fields starting with `_` (e.g. `_Id`, `_comment`) are ignored.
- **Enum Fields**: Accepts enum member names as strings (e.g. `"MobType": "Melee"`) or integer ordinals (`"MobType": 0`).
- **Array/List Fields**: Accepts JSON arrays (`"Tags": ["Skeleton", "Elite"]`).

---

## 5. Shipped Balance Presets & Design Rationale

All preset numbers are calibrated against the authentic data extracted from the game's `bansheegz_database`:

### 1. `weapon_buffs.json` (`Presets.EnableWeaponBuffs`)
**Design Objective**: Bring underperforming starter weapons into parity with top-tier weapons (e.g., Sorcerer's Fire Shard at 40 damage / 2.2s cooldown and Rogue's Daggers at 30 damage / 1.0s cooldown).
- **Necromancer (`Wpn_Necro_L1`)**:
  - *Vanilla*: Value = `8.0`, Cooldown = `7.0s` (extremely sluggish DPS: 1.14 DPS).
  - *Tuned*: Value = `18.0`, Cooldown = `4.5s` (4.0 DPS, viable starter clear).
- **Shadow Knight (`Wpn_ShdKnight_L1`)**:
  - *Vanilla*: Value = `20.0`, Cooldown = `3.0s`, Size = `0.6` (tiny hitbox and low damage output).
  - *Tuned*: Value = `32.0`, Cooldown = `2.2s`, Size = `1.2` (doubled cleave area, +60% damage, -27% cooldown).
- **Ranger (`Wpn_Ranger_L1`)**:
  - *Vanilla*: Value = `20.0`, Cooldown = `2.0s` (10.0 DPS).
  - *Tuned*: Value = `30.0`, Cooldown = `1.6s` (18.75 DPS, responsive ranged poke).
- **Fighter (`Wpn_Fighter_L1`)**:
  - *Vanilla*: Value = `25.0`, Cooldown = `1.2s`.
  - *Tuned*: Value = `35.0`, Cooldown = `1.0s` (+40% single-hit damage, 1.0s attack rhythm).
- **Bard (`Wpn_Bard_L1`)**:
  - *Vanilla*: Value = `4.0`, Size = `1.4`.
  - *Tuned*: Value = `6.0`, Size = `1.8` (+50% note damage, larger aura coverage).

### 2. `hard_mode.json` (`Presets.EnableHardMode`)
**Design Objective**: Significantly heighten survival pressure without bullet-sponge tedium by increasing enemy lethality, movement speed, and accelerating wave phase pacing.
- **`CORE_Mobs`**:
  - `Undead_T0_Unarmed`: Health `10.0` → `18.0`, Speed `1.85` → `2.30` (+24% rush speed).
  - `Undead_T0_1h`: Health `20.0` → `35.0`, Speed `1.75` → `2.20`, Damage `2.0` → `3.0`.
  - `Undead_T0_BigShield`: Health `30.0` → `55.0`, Speed `1.50` → `1.90`, Damage `2.0` → `3.0`.
  - `Undead_T1_Archer`: Health `20.0` → `38.0`, Speed `2.30` → `2.70`, Damage `2.0` → `4.0` (doubled ranged damage).
  - `Undead_T1_2h`: Health `20.0` → `45.0`, Speed `2.00` → `2.40`, Damage `5.0` → `8.0` (punishing heavy strikes).
- **`CORE_LevelSettings`**:
  - `Farmland`, `Nobleland`, `Churchland`: `MobHealthModifier` `1.065` → `1.15` (steeper minute-by-minute scaling).
- **`CORE_PhaseDuration`**:
  - `FirstPhase`, `SecondPhase`, `TrirdPhase`: Duration `20.0s` → `15.0s` (-25% duration; forces faster wave transitions and denser enemy packs).

### 3. `fast_waves.json` (`Presets.EnableFastWaves`)
**Design Objective**: Action-packed "turbo mode" for quick sessions.
- **`CORE_PhaseDuration`**:
  - All 3 phases compressed from `20.0s` to `10.0s` (waves cycle twice as fast).
- **`CORE_Heroes`**:
  - Base Movement for all 6 heroes (`Robbie`, `Milo`, `Danica`, `Morgan`, `Gorian`, `Gwen`) boosted from `5.0` → `6.75` (+35% speed) to compensate for aggressive wave compression.

---

## 6. Build Instructions

From the repository root:
```powershell
powershell -ExecutionPolicy Bypass -File .\build-all.ps1
```
This compiles every assembly (including `NecromanticData.dll`) into `bin\` against the game's own assemblies. Add `-Deploy` to copy them into the game; see the Modder Guide for details.
