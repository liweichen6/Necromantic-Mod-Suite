# Necromantic Mod Suite — Modder & Developer Guide

**Version**: Suite v2.1.0 / v1.0.0  
**Target Game**: *Necromantic* (Steam AppID 2615010 | Unity 2022.3.31f1 Mono x64)  
**Frameworks**: BepInEx 5.4.x (installed by `install-bepinex.ps1`), HarmonyX, BGDatabase (`BansheeGz.BGDatabase`), Easy Save 3 (ES3)  
**Game Directory**: found automatically via Steam; see `tools\GameDir.ps1`

---

## 1. Architecture Overview: Necromantic & BGDatabase

*Necromantic* is powered by Unity 2022.3.31f1 and uses **BGDatabase** (`BansheeGz.BGDatabase.BGRepo` in `BGDatabase.dll`) for its data repository.

The game's data repository resides in binary format within the game assets and is instantiated at runtime as a singleton repository: `BGRepo.I`. Under the namespace `BGDatabase.Generated`, the game defines **36 typed tables** (prefixed with `DT_` or referenced canonically without `DT_`).

### Key Gameplay Tables

| Table Name | Class Name | Contents & Purpose |
| :--- | :--- | :--- |
| **`CORE_AbilityDescription`** | `DT_CORE_AbilityDescription` | Localization keys, weapon icons, user-facing descriptions, and upgrade text. |
| **`CORE_AbilityEffects`** | `DT_CORE_AbilityEffects` | Core weapon balancing: base damage (`Value`), attack cadence (`Cooldown`), hitbox scale (`Size`), projectile speed, pierce count, and duration. |
| **`CORE_Mobs`** | `DT_CORE_Mobs` | Monster archetypes: baseline `Health`, rush `Speed`, contact `Damage`, prefab references, mob category, and drop weights. |
| **`CORE_Heroes`** | `DT_CORE_Heroes` | Hero baseline stats: base `Movement` speed, starting weapons, base health, armor, and perk allocations. |
| **`CORE_LevelSettings`** | `DT_CORE_LevelSettings` | Per-map multipliers: minute-by-minute `MobHealthModifier`, experience modifiers, and level size. |
| **`CORE_PhaseDuration`** | `DT_CORE_PhaseDuration` | Stage pacing: duration of `FirstPhase`, `SecondPhase`, and `TrirdPhase` waves in seconds. |
| **`CORE_WaveConfig`** | `DT_CORE_WaveConfig` | Wave definitions, monster composition groups, spawn density, and interval timers. |
| **`FEAT_GlobalUpgrades`** | `DT_FEAT_GlobalUpgrades` | Academy Library meta-progression perks: tier costs, stat bonuses per rank, and prerequisites. |

---

## 2. Startup Cache Neutralization & Hook Timing

### The Architecture Problem
A common pitfall in modding *Necromantic* is modifying `BGDatabase` too late.
Analysis of `Assembly-CSharp.dll` reveals that `Blinkmoon.GameSystem.DataTableHookup::InitService()` implements `IInitService` and executes during `ServiceManager.ProcessGameStartup`.

During `InitService()`, the game executes the following caching routines:
- `GatherAbilitiesInformation()` → Caches into internal arrays in `AbilitiesInfo` and `AbilitiesMobInfo`
- `GatherMobsInformation()` & `GatherBossesInformation()` → Caches into `MobsInfo`
- `GatherHeroesInformation()` → Caches into `HeroesInfo`
- `GatherChoiceProgressionInformation()` → Caches into `ChoiceProgressionInfo`
- `GatherDataTablePhases()`, `GatherDataTableWeights()`, `GatherDataTableWaves()` → Caches into `WavesInfo`
- `GatherLevelInformation()` → Caches into `LevelsInfo`

> [!WARNING]
> If a mod modifies `BGDatabase` after `DataTableHookup.InitService()` has executed, gameplay systems reading from `DataTableHookup.Instance` will consume **stale, unmodded values**.

### The Solution: Dual-Layer Timing
`NecromanticData` ensures overrides apply strictly before caching via a two-layer guarantee:
1. **`BGRepo.OnLoad` Event**: Subscribed in `Plugin.Awake()`. Triggers immediately whenever `BGRepo.Load()` completes.
2. **Harmony Prefix on `DataTableHookup.InitService`**:
   ```csharp
   [HarmonyPatch(typeof(DataTableHookup), "InitService")]
   public static class Patch_DataTableHookup_InitService
   {
       [HarmonyPrefix]
       public static void Prefix()
       {
           // Guarantees overrides are applied to BGRepo.I BEFORE DataTableHookup reads database
           NecromanticDataPlugin.EnsureOverridesApplied("DataTableHookup.InitService Prefix");
       }
   }
   ```
This ensures that every override and balance preset is committed to `BGRepo.I` in RAM before a single row is read into the game's cache.

---

## 3. BGDatabase JSON Schema & Export System

### Automated Reference Export
When `ExportOnStartup = true` (default), `NecromanticData` exports all 36 tables (vanilla values, before overrides) on first launch, and again on every launch if `ForceReExport = true`, to:  
`BepInEx\config\NecromanticData\export\<Table>.json`

Each file is formatted as indented, human-readable JSON:
```json
{
  "Undead_T0_Unarmed": {
    "_Id": "4392817294819284",
    "Health": 10.0,
    "Speed": 1.85,
    "Damage": 1.0,
    "MobType": "Melee",
    "Tags": ["Undead", "Basic"]
  },
  "Undead_T0_BigShield": {
    "_Id": "5819284719284918",
    "Health": 30.0,
    "Speed": 1.5,
    "Damage": 2.0,
    "MobType": "Melee",
    "Tags": ["Undead", "Armored"]
  }
}
```

### Schema Rules & Field Conventions
1. **Root Keys**: Named by row `entity.Name` (e.g. `Undead_T0_Unarmed`, `Wpn_Necro_L1`). If names collide, the exporter appends `_<Id>`.
2. **Metadata Fields**: Any field starting with an underscore (`_`) like `"_Id"` is treated as read-only user metadata. The override parser ignores these fields during import.
3. **Field Names**: Case-insensitive (e.g., `"Health"`, `"health"`).
4. **Data Types**:
   - `float` / `double`: Floating-point numbers (`18.0`, `4.5`).
   - `int`: Signed integer numbers (`5`, `100`).
   - `bool`: Boolean `true` or `false`.
   - `string`: Text strings (`"Farmland"`, `"SwordSlash"`).
   - `Enum`: Accepts enum string names (`"Melee"`) or integer ordinals (`0`).
   - `List<string>`: JSON string arrays (`["TagA", "TagB"]`).
   - `List<float>`: JSON float arrays (`[1.0, 2.5, 5.0]`).

---

## 4. Writing Custom Delta Overrides

Place your custom JSON override files in:  
`BepInEx\config\NecromanticData\overrides\*.json`

Files ending in `.example` are ignored. Overrides are non-destructive and only need to specify the exact rows and fields you wish to alter.

### Format A: Multi-Table JSON (Recommended for Mod Packs)
Root properties match database table names:
```json
{
  "CORE_AbilityEffects": {
    "Wpn_Necro_L1": {
      "Value": 25.0,
      "Cooldown": 3.0
    }
  },
  "CORE_Mobs": {
    "Undead_T0_Unarmed": {
      "Health": 15.0,
      "Speed": 2.0
    }
  }
}
```

### Format B: Single-Table JSON (Clean for Targeted Edits)
The filename defines the table (e.g. `BepInEx\config\NecromanticData\overrides\CORE_Mobs.json`):
```json
{
  "Undead_T0_Unarmed": {
    "Health": 20.0,
    "Speed": 2.2
  },
  "Undead_T0_1h": {
    "Damage": 4.0
  }
}
```

---

## 5. Easy Save 3 & Save Progression Architecture

*Necromantic* manages persistent progression through **Easy Save 3** (`ES3`), serializing account state to:  
`%USERPROFILE%\AppData\LocalLow\Blinkmoon\Necromantic\BlinkmoonSave.es3`  
(Resolved natively via `Path.Combine(Application.persistentDataPath, "BlinkmoonSave.es3")`).

### Key Engine Classes & Singletons

1. **`Blinkmoon.GameSystem.PlayerProgressionManager.Instance`**:
   - Primary singleton managing persistent game state across hub and matches.
   - `PersistentData`: Property returning active `PlayerData` instance.
   - `LoadOrStartData()`: Reads `BlinkmoonSave.es3` via ES3, populates in-memory fields, or creates default profile.
   - `SaveData()`: Serializes `PersistentData` into `BlinkmoonSave.es3`.
   - `InvokeDataChanged()`: Notifies UI controllers, Academy Library, and telemetry listeners of state updates.
   - `UnlockAllWeaponsAndAbilities()`: Cheat method that populates weapon collections with all weapons and evolutions.
   - `ResetPlayerData()`: Clears progression and resets to initial fresh game state.
2. **`Blinkmoon.GameSystem.PlayerData`**:
   - Data transfer object holding all player progression fields:
     - `Scrolls`: Academy Library upgrade currency (`int`).
     - `Cheese`: Secondary meta-currency (`int`).
     - `LibrarySpent`: Total scrolls expended in the Academy (`int`).
     - `TotalBookCount`: Unlocked tomes (`int`).
     - `Weapons`: Collection of unlocked weapon keys (`List<string>`).
     - `Abilities`: Collection of unlocked character abilities (`List<string>`).
     - `Achievements`: Completed achievement identifiers (`List<string>`).
     - `telemetryData.cumulativeGameplayData`:
       - `totalMatchesPlayed` (`int`)
       - `totalMatchesWon` (`int`)
       - `totalEnemiesKilled` (`int`)
3. **`Blinkmoon.GameSystem.PlayerMainData`**:
   - Internal manager component referenced via private field `_playerMainData` on `PlayerProgressionManager`.
   - Contains property `PersistedPlayerData` that references active `PlayerData`.

### Safe In-Memory Editing Pattern (Why Raw File Writes Corrupt)
Easy Save 3 files utilize binary dictionary tags, type hashes, and payload length headers. Direct byte modification or text editing of `BlinkmoonSave.es3` from external scripts frequently breaks header offsets, resulting in ES3 parse exceptions and save resets.

`NecromanticSave` implements the canonical **Safe In-Memory Mutation Pattern**:
```text
[External JSON / UI Input]
           │
           ▼
[Modify PlayerProgressionManager.Instance.PersistentData in RAM]
           │
           ▼
[Invoke PlayerProgressionManager.Instance.SaveData()]
           │
           ▼
[Native Easy Save 3 Engine Serializes Valid .es3 File]
```
This guarantees 100% format validity, header consistency, and zero risk of corrupting player save files.

---

## 6. Step-by-Step Modding Tutorials

### Tutorial 1: Creating a Custom Weapon Balance Override
**Goal**: Transform the starter Necromancer weapon (`Wpn_Necro_L1`) into a rapid-firing scythe and double its damage.

1. Inspect `BepInEx\config\NecromanticData\export\CORE_AbilityEffects.json`.
2. Locate the row `"Wpn_Necro_L1"`. Note its default values (`Value: 8.0`, `Cooldown: 7.0`, `Size: 1.0`).
3. Create a new file: `BepInEx\config\NecromanticData\overrides\SuperNecro.json`.
4. Add the following JSON content:
   ```json
   {
     "CORE_AbilityEffects": {
       "Wpn_Necro_L1": {
         "Value": 35.0,
         "Cooldown": 2.0,
         "Size": 1.5
       }
     }
   }
   ```
5. Launch the game. Inspect `BepInEx\LogOutput.log` to confirm the modification:
   ```text
   [Info :Necromantic Data Framework] [SuperNecro.json] CORE_AbilityEffects.Wpn_Necro_L1.Value = 35
   [Info :Necromantic Data Framework] [SuperNecro.json] CORE_AbilityEffects.Wpn_Necro_L1.Cooldown = 2
   [Info :Necromantic Data Framework] [SuperNecro.json] CORE_AbilityEffects.Wpn_Necro_L1.Size = 1.5
   ```

---

### Tutorial 2: Creating a High-Speed "Chaos Mode" Mob Override
**Goal**: Make all early undead move 50% faster and cut wave phase times in half.

1. Create a new file: `BepInEx\config\NecromanticData\overrides\ChaosPacing.json`.
2. Add the following content:
   ```json
   {
     "CORE_Mobs": {
       "Undead_T0_Unarmed": {
         "Speed": 3.0
       },
       "Undead_T0_1h": {
         "Speed": 2.8
       },
       "Undead_T0_BigShield": {
         "Speed": 2.4
       }
     },
     "CORE_PhaseDuration": {
       "FirstPhase": {
         "Duration": 10.0
       },
       "SecondPhase": {
         "Duration": 10.0
       },
       "TrirdPhase": {
         "Duration": 10.0
       }
     }
   }
   ```
3. Launch the game and start a run. Waves will cycle every 10 seconds with sprinting mobs.

---

### Tutorial 3: Customizing Hero Movement & Baseline Stats
**Goal**: Boost Robbie and Milo with increased movement speed and custom base tuning.

1. Create a new file: `BepInEx\config\NecromanticData\overrides\FastHeroes.json`.
2. Add the following content:
   ```json
   {
     "CORE_Heroes": {
       "Robbie": {
         "Movement": 7.5
       },
       "Milo": {
         "Movement": 7.0
       }
     }
   }
   ```
3. Save and launch. Robbie and Milo will immediately start with enhanced mobility.

---

### Tutorial 4: Programmatic Save Editing & JSON Automation
**Goal**: Export progression to JSON, modify Academy Scrolls and unlock status, and import changes safely.

1. In game, press **`F11`** to open the Save Manager & Editor.
2. Navigate to **JSON Export / Import** tab and click **Export PlayerData to JSON**.
3. Open `BepInEx\config\NecromanticSave\PlayerData_Export.json` in your editor.
4. Modify `Scrolls` or `telemetryData`:
   ```json
   {
     "Scrolls": 500000,
     "Cheese": 1500,
     "telemetryData": {
       "cumulativeGameplayData": {
         "totalMatchesPlayed": 42,
         "totalMatchesWon": 30,
         "totalEnemiesKilled": 125000
       }
     }
   }
   ```
5. Save the file. In the F11 menu, click **Import PlayerData from JSON**.
6. `NecromanticSave` automatically creates a safety backup (`PreJsonImport_BlinkmoonSave.es3`), validates all fields, updates `PersistentData`, and writes to `BlinkmoonSave.es3` using the native ES3 engine.

---

## 7. Building from Source

Everything compiles with the .NET Framework `csc.exe` that ships with Windows
(`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`); no Visual Studio needed.
All sources are C# 5.

```powershell
# Build NecromanticCommon + all 5 plugins into .\bin
powershell -ExecutionPolicy Bypass -File .\build-all.ps1

# Build and copy into the game (game must be closed)
powershell -ExecutionPolicy Bypass -File .\build-all.ps1 -Deploy

# Override the Steam auto-detection (tools\GameDir.ps1); NECROMANTIC_GAME_DIR also works
powershell -ExecutionPolicy Bypass -File .\build-all.ps1 -GameDir "E:\Games\Necromantic" -Deploy
```

`build-all.ps1` references assemblies straight from the game install
(`<GameDir>\Necromantic_Data\Managed` and `<GameDir>\BepInEx\core`), builds
`NecromanticCommon.dll` first, then each plugin against it, and exits non-zero if
anything fails. Flags: `/noconfig /nostdlib /optimize+ /target:library` so the output binds
only to the game's own Mono runtime libraries.

### Shared library: `NecromanticCommon`

`src/Common/` builds `NecromanticCommon.dll`, a plain library (not a BepInEx plugin) that
Trainer, QoL, Endless and Save reference. It must be deployed alongside them.

| File | Purpose |
| :--- | :--- |
| `ModHost.cs` | `ModHost.Protect(this, Log)`: first call in every plugin `Awake()`. Marks the BepInEx manager object `HideAndDontSave` so `Update`/`OnGUI` keep running (see below), and logs `Plugin update loop is running` once the first frame ticks. |
| `HotkeyHelper.cs` | `IsKeyDown` / `IsComboDown`: Win32 `GetAsyncKeyState` (focus-checked) plus Unity `Input` fallback, edge-triggered and debounced so one press fires exactly once. |
| `CanvasOverlay.cs` | `CanvasOverlay.Notify(msg, seconds)`: top-centre uGUI notification banner at sorting order 32700. |

### Why plugins need `ModHost.Protect`

On this game the BepInEx chainloader runs before the first scene exists. With BepInEx's
default `HideManagerGameObject = false`, the `BepInEx_Manager` object that hosts every
plugin component is torn down during startup: `Awake()` runs (so the log looks healthy and
Harmony patches work) but `Start`/`Update`/`OnGUI` never do, so no hotkeys, HUD or windows.
`ModHost.Protect` applies the same `HideAndDontSave` flag the config option would, and
`deploy-to-game.ps1` also sets `HideManagerGameObject = true` in `BepInEx.cfg`.

If you add a new plugin with an `Update`/`OnGUI`, call `ModHost.Protect(this, Logger)`
first thing in `Awake()`.

---

## 8. Developer Best Practices & Engine Safety Guidelines

### 1. Wwise Audio Buffer Safety (Rapid Fire Cooldown Clamps)
- *Necromantic* uses the **Audiokinetic Wwise** sound engine for sound events and voice queues.
- **Rule**: When modding weapon cooldowns or attack speed, **never clamp cooldowns to 0 or match 60Hz tick rates**.
- Saturating the Wwise event queue with 60 audio events per second causes buffer starvation, audio crackling, and driver crashes.
- Always enforce a minimum cooldown clamp of **`0.05s` (20Hz max)**:
  ```csharp
  [HarmonyPatch(typeof(GameplayAbilityBase), "GetAbilityCooldown")]
  public static class Patch_Cooldown
  {
      static void Postfix(ref float __result)
      {
          if (TrainerPlugin.RapidFire)
          {
              __result = 0.05f; // Safe 20Hz clamp preserves Wwise audio buffers
          }
      }
  }
  ```

### 2. PhysX Broadphase Limits (Magnet Radii)
- In Unity PhysX, inflating a sphere collider or pickup range query to astronomical values (e.g. 200 kilometers) triggers extensive broadphase bounding-box overlaps across thousands of inactive scene colliders.
- **Rule**: Keep continuous magnetic pickup radii capped at **35x (~70 world units)**. For full-map sweeps, use discrete procedural sweeps (`FindObjectsOfType<CollectibleBase>()` with `.MoveToPlayer()`) triggered on demand rather than continuous PhysX triggers.

### 3. Safe Save Data Management (`BlinkmoonSave.es3`)
- **Rule**: Never perform raw file writes, byte alterations, or regex text replacements on `BlinkmoonSave.es3`.
- Always read or modify progression through `PlayerProgressionManager.Instance.PersistentData` in memory and commit via `PlayerProgressionManager.Instance.SaveData()`.
- Always generate a timestamped safety backup before restoring or importing save data.

### 4. UI Modal Stacking Prevention
- When automating in-game actions that trigger fullscreen or modal UI (such as chest openings or upgrade cards), never fire events simultaneously in a single frame.
- **Rule**: Always pace modal-triggering calls using a Unity Coroutine with a delay (e.g., `yield return new WaitForSeconds(1.5f);`) so animations and UI transitions can finish cleanly.

### 5. Multiplayer & Co-Op Safety
- *Necromantic* runs every session, solo included, on Photon Fusion (the game's wrapper lives in `Blinkmoon.GameSystem.BadFusion`).
  Solo play is started by `AppService.StartSinglePlayerSession` with `GameMode.Single`; hosting
  uses `GameMode.Host` and joining `Client` / `AutoHostOrClient`.
- **Gotcha**: `AppService.Instance.IsSingleplayer` returns **false whenever no session exists**
  (main menu, between runs), not only in co-op. It is fine for guarding in-run actions such as
  instant restart, but to detect co-op anywhere use the runner's mode instead:
  ```csharp
  var runner = AccessTools.Field(typeof(AppService), "_runner").GetValue(AppService.Instance) as Fusion.NetworkRunner;
  bool inCoop = runner != null && runner.GameMode != Fusion.GameMode.Single;
  ```
  (This is what `TrainerPlugin.IsCoopSession()` does; the Trainer disables itself in co-op.)
- **Rule**: Gate anything that changes the shared simulation (damage, spawns, timers, restarts)
  on single-player, and check `__instance.GetIsPlayer()` in `ApplyDamage` hooks so you only
  change the intended side.

### 6. Pitfalls Found During Development
These all shipped in an early prototype and were fixed; they are easy to reintroduce.
- **`EventsSpawner.MoveToTimeSlot(float time)` takes an absolute run time**, not a delta.
  Passing `60` jumps the clock *to* 1:00. Compute `Map.Instance.GameTimerSeconds + delta` first.
- **`EventsSpawner.StartSystem()` resets `eventIndex` / `subtierIndex` to 0.** Don't call it
  to "unfreeze" spawning; skip the tick with a `UnPausedFixedUpdateNetwork` prefix instead.
- **Forcing `GameplayAbilityBase.CanActivateAbility()` to `true` bypasses the cooldown clamp**
  and fires every tick (60Hz). Clamp `GetAbilityCooldown()` instead (see Wwise safety above).
- **One try/catch around a whole loop** (e.g. damaging every mob) lets one bad object abort
  the rest. Catch per item.
- **`FindObjectOfType` in `Update`/`OnGUI` is expensive**, and `OnGUI` runs several times per
  frame. Cache the result and rescan on a timer or on `SceneManager.activeSceneChanged`.
- **Uncapped perspective FOV** looks like a fisheye past ~90°; clamp it.
- **`MobBehaviour.InitializeAttributes()` assigns a fresh `attributesOverrides` list** before
  calling `ModifyAttributesByType()`, so editing overrides in a postfix there doesn't compound
  on pooled mobs. Percent-type overrides (`percent == true`) hold a bonus percentage, not a
  multiplier, so scale them as `((1 + p/100) * mult - 1) * 100`.
- **`Resources.GetBuiltinResource<Font>("Arial.ttf")` fails on Unity 2022.2+**; use
  `"LegacyRuntime.ttf"` for uGUI text.
- **The BepInEx manager object is destroyed at startup** unless hidden; see
  [Why plugins need `ModHost.Protect`](#why-plugins-need-modhostprotect).
- **IMGUI window IDs must be unique across plugins.** In use: Trainer `99902`, Save `99903`,
  QoL HUD `8899`, Endless `987654`.

---

## 9. Game Internals & Hook Points

### Runtime facts
| Component | Detail | What it means for modding |
| :--- | :--- | :--- |
| Engine | Unity 2022.3.31f1, Windows x64 | Standard BepInEx 5 (Mono) setup. |
| Scripting backend | Mono (`MonoBleedingEdge`), **not IL2CPP** | Game code is plain .NET IL: decompile with ILSpy/dnSpy and patch with Harmony. |
| Debug symbols | `Assembly-CSharp.pdb` ships with the game | Real method/parameter names in decompilers and stack traces. |
| Obfuscation / anti-cheat | None | No integrity checks to work around. |
| Burst | `lib_burst_generated.dll`, used for pathfinding and GPU instancing only | All gameplay logic is Harmony-patchable. |
| Networking | Photon Fusion 1.1.9 | See co-op safety above. |
| Audio | Audiokinetic Wwise | Don't flood it with events (cooldown clamp). |
| Data | BGDatabase (`BGRepo.I`, 36 tables) | See §1–4. |
| Saves | Easy Save 3, `BlinkmoonSave.es3` | See §5. |

### Key classes and hooks (all in `Blinkmoon.GameSystem` unless noted)
- **Local player**: subscribe to the static event `Character.OnLocalPlayerSpawned`
  (`Action<Character>`), or read `PlayerManager.LocalPlayer`.
- **Damage**: `DynamicAttributes.ApplyDamage(Vector3 location, float damage, PlayerRef attacker,
  GameObject causer, float knockback, DamageType damageType, Inventory.AbilityType ability)`
  returns `ICanTakeDamage.DamageDataPackage` (`damage` = actual non-overkill damage, `killed`).
  God mode / damage multipliers are prefixes here; the DPS meter is a postfix.
- **Stats**: `AttributeBase.GetCurrentValue(AttributeType)`. `AttributeType` values: Health=1,
  Damage=2, PercentDamage=3, XP=4, HealthRegen=5, HealthPickup=6, MovementSpeed=7,
  ProjectileSpeed=8, ProjectileQuantity=9, Luck=10, DamageElBs=11, AttackSpeed=12,
  PickupRange=13, EnemyEffectDur=14, OnPickupHealth=15, OnGoldHealth=16, FireStandDamage=17,
  Knockback=18, Invincible=19, ScrollsGain=20, DamageTaken=21.
- **Cooldowns**: `GameplayAbilityBase.GetAbilityCooldown()` (protected virtual) returns
  `currentStats.cooldown * dynamicAttributes.GetNegativeValue(AttackSpeed)`, floored at
  `minCooldown`. Attack speed is read through **`GetNegativeValue`**, so patching
  `GetCurrentValue(AttackSpeed)` does nothing to fire rate. Enemy abilities
  (`A_Mino_Charge`, `A_Elite_Projectile`) and `A_Sword` override it but call the base
  version, so a base postfix sees all of them; check the owner
  (`dynamicAttributes.GetIsPlayer()`, via the protected getter) to only affect the player.
- **Run clock and spawners**: `Map.Instance.GameTimerSeconds`, `Map.SetFakeTime(int)`,
  `WavesSpawner` / `EventsSpawner` (`UnPausedFixedUpdateNetwork`, `MoveToTimeSlot`).
- **Run end**: `GameEndManager.StartGameEndFlow(bool win)`, `GameEndManager.RestartGame()`.

### Developer cheats left in the game
Callable directly from a plugin; the Trainer uses them.
| Call | Effect |
| :--- | :--- |
| `PlayerProgressionManager.Instance.UnlockAllWeaponsAndAbilities()` | Unlock all weapons, abilities and evolutions |
| `ChaptersManager.Instance.Cheat_CompleteAllChapters()` | Mark every campaign chapter complete |
| `AssignmentsManager.Instance.Cheat_CompleteCurrentAssignments()` | Complete the current bounties |
| `Level.DebugLevelUp()` (on the `Level` component) | Instant level-up |
| `LibraryManager.Instance.ReceiveScrolls(int)` | Grant Scrolls |
| `Blinkmoon.DebugModules.DebugModulesEnabler.DebugMenuEnabled` | The developers' debug menu; see below |

### The developer debug menu
- `DebugModulesEnabler.Awake()` runs once at startup: it sets `DebugMenuEnabled` from its
  `enableDebugMenuInReleaseCandidate` field and instantiates only the `modules` entries
  (`DebugModuleEntry { editor, internal_, releaseCandidate, prefab }`) flagged
  `releaseCandidate`.
- Readers check the flag **once** in their own `Awake`/`Start`: `DebugMenuView.Start`
  deactivates the menu when false; `HubTrailerEvents`, `TrailerEvents` and
  `LobbyPanelDebug` read it in `Awake`. A getter patch that is switched on later therefore
  changes nothing; it must be active before those run.
- The menu is a uGUI tree (`DebugMenuView` → `DebugMenuButton`s) built from
  `[DebugAction]` / `[DebugActionList]` members of `DebugActions`, `HubDebugActions` and
  `GameplayDebugActions`. It is opened by the Rewired action `ToggleDebugMenu` (id 8); the
  key binding lives in the Rewired input asset, not in code.
- The Trainer's `Patch_DebugModulesEnabler_Awake` logs every module and, when
  `EnableDevDebugMenu` is set, forces the flag on and also instantiates `internal_` modules.
  `TrainerPlugin.ReadDebugMenuBindings()` logs the bound input from Rewired.
