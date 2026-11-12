# Necromantic Mod Suite — Complete User Guide

**Version**: Suite v2.1.0 / v1.0.0  
**Target Game**: *Necromantic* (Steam AppID 2615010 | Unity 2022.3.31f1 Mono x64)  
**Modding Engine**: BepInEx 5.4.x (x64) with HarmonyX  
**Game Directory**: found automatically through Steam (App ID 2615010). Override with `-GameDir "<path>"` or the `NECROMANTIC_GAME_DIR` environment variable.

---

## 1. Suite Overview & Architecture

The **Necromantic Mod Suite** is a unified, high-performance modular plugin collection engineered specifically for the rogue-lite horde-survival game *Necromantic*. Built on top of BepInEx 5 and HarmonyX, the suite delivers game enhancements, training and debugging utilities, combat telemetry, limitless late-game scaling, safe save management & progression editing, and full data-driven balance modding.

The suite consists of five complementary plugins:

| Plugin Name | Assembly | GUID | Version | Primary Purpose |
| :--- | :--- | :--- | :--- | :--- |
| **NecromanticTrainer** | `NecromanticTrainer.dll` | `com.necromantic.trainer` | `2.1.0` | Comprehensive Insert-key trainer overlay, godmode, speed, full vacuum, screen nuke, spawner freeze, relative clock skip, infinite rerolls, card banishing, and native debug module enabler. |
| **NecromanticQoL** | `NecromanticQoL.dll` | `com.necromantic.qol` | `1.0.0` | F9 live rolling DPS & per-weapon damage meter HUD, solo-guarded instant restart (Ctrl+R), and intro splash screen bypass. |
| **NecromanticEndless** | `NecromanticEndless.dll` | `com.necromantic.endless` | `1.0.0` | F10 Endless & Nightmare modes, boss chest flow bypass, dynamic match timer extension, spawner wave/event looping, and exponential overtime mob scaling. |
| **NecromanticData** | `NecromanticData.dll` | `com.necromantic.data` | `1.0.0` | BGDatabase 36-table JSON reference exporter, startup cache-neutralized in-memory delta overrides system, and 3 curated balance presets. |
| **NecromanticSave** | `NecromanticSave.dll` | `com.necromantic.save` | `1.0.0` | F11 Save Manager & Editor, timestamped ES3 backups & instant in-memory restore, live progression editing (Scrolls, Cheese, lifetime telemetry stats), JSON export/import, and offline CLI backup tool (`SaveBackupTool.ps1`). |

---

## 2. Installation & Directory Layout

### System Requirements
- **Operating System**: Windows 10/11 (64-bit)
- **Game Version**: Steam retail build (Unity 2022.3.31f1 Mono x64)
- **Framework**: BepInEx 5.4.x (64-bit Mono); tested with 5.4.22, latest is 5.4.23.x

### Installation Steps
1. Close the game. From the repository root, install or upgrade BepInEx:
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\install-bepinex.ps1 -DryRun   # preview
   powershell -ExecutionPolicy Bypass -File .\install-bepinex.ps1
   ```
   It fetches the latest stable BepInEx 5 x64 release from GitHub, checks its SHA-256,
   backs up `BepInEx\config` and `BepInEx\plugins` to `BepInEx_backup_<time>.zip` in the game
   folder, and replaces only BepInEx's own files. Pin a version with `-Version 5.4.23.6`.
2. Build and install the suite:
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\build-all.ps1 -Deploy
   ```
   (All scripts find the game through Steam; override with `-GameDir "<path>"`.)
3. Launch the game. A banner at the top of the screen lists the hotkeys, and the DPS HUD
   appears in the top-left. `BepInEx\LogOutput.log` should contain
   `[ModHost] Plugin update loop is running (hotkeys active).`

**From a release zip** (no build tools needed): install BepInEx 5 x64, then extract
`NecromanticModSuite_<date>.zip` into the game folder. It contains **all six** DLLs,
including `NecromanticCommon.dll` (the shared library the Trainer, QoL, Endless and Save
plugins depend on). `HideManagerGameObject = true` under `[Chainloader]` in
`BepInEx\config\BepInEx.cfg` is recommended; the plugins also apply the equivalent fix
themselves.

> **Single-player only:** the Trainer disables itself and resets every cheat as soon as a
> co-op session starts, and its window only shows a notice until you're back in solo play.

### File Layout

```text
Game Directory (runtime files only)
├── Necromantic.exe
├── steam_appid.txt
├── winhttp.dll                                   # BepInEx Doorstop hook
├── doorstop_config.ini
└── BepInEx\
    ├── core\                                     # BepInEx & Harmony runtimes
    ├── plugins\                                  # Active plugin binaries
    │   ├── NecromanticCommon.dll                 # Shared hotkey/overlay library (required)
    │   ├── NecromanticTrainer.dll                # (v2.1.0)
    │   ├── NecromanticQoL.dll                    # (v1.0.0)
    │   ├── NecromanticEndless.dll                # (v1.0.0)
    │   ├── NecromanticData.dll                   # (v1.0.0)
    │   └── NecromanticSave.dll                   # (v1.0.0)
    ├── config\                                   # Generated configs & data
    │   ├── BepInEx.cfg                           # Console enabled, HideManagerGameObject = true
    │   ├── com.necromantic.qol.cfg               # QoL configuration
    │   ├── com.necromantic.endless.cfg           # Endless mode configuration
    │   ├── com.necromantic.data.cfg              # Data framework configuration
    │   ├── com.necromantic.save.cfg              # Save Manager configuration
    │   ├── NecromanticData\
    │   │   ├── export\                           # 36 BGDatabase tables exported as JSON
    │   │   ├── overrides\                        # User custom delta JSON overrides
    │   │   └── presets\                          # weapon_buffs, hard_mode, fast_waves
    │   └── NecromanticSave\
    │       ├── Backups\                          # Timestamped BlinkmoonSave_*.es3 backups
    │       └── PlayerData_Export.json            # Human-readable progression JSON
    └── LogOutput.log                             # BepInEx diagnostic log

```

See the [README](../README.md) for the repository layout.

---

## 3. Master Hotkey Reference Table

Every hotkey in the suite is designed for zero latency and conflicts are eliminated through dedicated keybind mappings:

| Hotkey | Mod Origin | Action | Scope / Context | Detailed Behavior |
| :--- | :--- | :--- | :--- | :--- |
| **`Insert`** / **`Home`** | NecromanticTrainer | **Toggle Trainer GUI** | Universal | Opens/closes the 5-tab IMGUI trainer window (Combat, Loot & Shop, Progression, World & Camera, Hotkeys). All keys in this table are defaults and can be changed in the Hotkeys tab. |
| **`F2`** | NecromanticTrainer | **1.0x Normal Speed** | In-Game / Hub | Resets `Time.timeScale` to standard 1.0x pacing. |
| **`F3`** | NecromanticTrainer | **2.0x Turbo Speed** | In-Game / Hub | Doubles simulation speed (`Time.timeScale = 2.0f`). |
| **`F4`** | NecromanticTrainer | **3.0x Fast Pacing** | In-Game / Hub | Sets simulation speed to triple (`Time.timeScale = 3.0f`). |
| **`F5`** | NecromanticTrainer | **Screen Wipe / Nuke** | Active Match | Applies 999,999 pure damage to all live enemy `DynamicAttributes` in scene. Player and dead entities are skipped safely. |
| **`F6`** | NecromanticTrainer | **Vacuum All Drops** | Active Match | Immediately sweeps every collectible scroll, gem, and drop on the entire map directly to the player. |
| **`F7`** | NecromanticTrainer | **Toggle GodMode** | Active Match | Toggles total invincibility. Blocks damage before damage calculations take place (`damage = 0`, returns empty damage package). |
| **`F8`** | NecromanticTrainer | **Toggle Super Magnet** | Active Match | Toggles expanded pickup radius clamped at 35x (~70 world units) to prevent physics engine overhead. |
| **`F9`** | NecromanticQoL | **Toggle Combat DPS HUD** | Active Match | Shows/hides the rolling DPS, total damage, match timer, kill count, and per-ability damage meter. |
| **`F10`** | NecromanticEndless | **Endless Status Window** | Active Match | Shows live match overtime status, current HP/damage multipliers, wave/event spawner state, and mode toggles. |
| **`F11`** | NecromanticSave | **Toggle Save Manager & Editor** | Universal | Opens/closes the 3-tab IMGUI Save Manager & Editor window (Backups & Restore, Save Editor, JSON Export/Import). |
| **`Ctrl + R`** | NecromanticQoL | **Instant Run Restart** | Active Match / GameOver | Cleanly restarts current match into 00:00 without returning to Main Menu. Guarded: Solo play only (blocked in co-op). |

---

## 4. NecromanticTrainer (v2.1.0) Feature Guide

Toggle the Trainer overlay at any time using **`Insert`** or **`Home`**. (F1 is deliberately not used: the game binds F1 to its own developer debug menu.) The window is movable by dragging its title bar.

```
+-------------------------------------------------------------+
| Necromantic Trainer & QoL Suite v2.1.0                      |
+-------------------------------------------------------------+
| [Combat] [Loot & Shop] [Progression] [World] [Hotkeys]      |
|  ...                                                        |
| [Turn Off All Cheats]            [Close Window (Insert/Home)]|
+-------------------------------------------------------------+
```

**Turn Off All Cheats** (bottom of every tab) resets every cheat in one click: god mode,
one-hit kill, rapid fire, all multipliers (damage, movement, attack speed, luck, XP, pickup),
extra projectiles, super magnet, free library upgrades, infinite rerolls, banished cards,
frozen mobs/waves and game speed. Camera zoom and hotkey bindings are kept. The same reset
runs automatically when a co-op session starts.

### Tab 1: Combat
- **God Mode [F7]**:
  - Hooked via Harmony Prefix on `DynamicAttributes.ApplyDamage`.
  - When enabled, player damage is clamped to `0f` and the method returns `false` immediately.
  - Zero health loss, zero stagger, and zero vulnerability to stage hazards or boss slam attacks.
- **One-Hit Kill**:
  - When enabled, any damage dealt by the player to enemy mobs is overridden to `999,999f`.
- **Rapid Fire (Wwise Audio Safe)**:
  - Overrides `GameplayAbilityBase.GetAbilityCooldown` via Harmony Postfix, for **your**
    abilities only (enemy abilities such as the minotaur charge use the same method and are
    left alone).
  - **Correction**: Cooldown is clamped to **`0.05s` (20Hz maximum)** rather than running at an unrestricted 60Hz tick rate. This prevents Wwise sound buffer starvation, distortion, audio engine dropouts, and fatal audio thread crashes while still delivering blazing machine-gun fire rates.
- **Attack Speed** (`1x`, `1.5x`, `2x`, `3x`, `5x`):
  - Divides your weapon cooldowns by the multiplier (2x = twice as often), never below `0.05s`.
  - The game computes a cooldown as `base cooldown × attack-speed attribute`, floored at the
    weapon's own minimum; this setting applies on top of that. Rapid Fire overrides it.
- **Damage Multiplier**:
  - Multiplies player attack damage dealt by `1.0x`, `2.0x`, `5.0x`, `10.0x`, or `25.0x`.
- **Movement Speed Multiplier**:
  - Scales `AttributeBase.AttributeType.MovementSpeed` on the player by `1.0x`, `1.5x`, `2.0x`, `3.0x`, or `5.0x`.
- **Extra Projectiles**:
  - Appends bonus projectiles (`+0`, `+2`, `+5`, `+10`) to `AttributeType.ProjectileQuantity` without requiring duplicate card upgrades.
- **Screen Wipe / Nuke All Mobs [F5]**:
  - Iterates all scene entities with `DynamicAttributes`. Validates alive state, ignores the player character, and inflicts `999,999` damage.
  - Isolated try-catch wrappers ensure that single null or mid-destruction references never abort the nuke loop.
- **Freeze Mobs**:
  - Invokes `MobBehaviour.BlockMovement(true/false)` on all active enemies. Enemies halt in place without losing target data.

---

### Tab 2: Loot & Shop
- **Super Magnet [F8]**:
  - Clamps player pickup range to **35.0f** (~70 world units radius) via `AttributeBase.GetCurrentValue` and `CollectibleBase.GetPickupDistance`.
  - **Correction**: Sane ~70m cap prevents broadphase PhysX collision explosions that cause severe frame stutter with unconstrained 200km colliders.
- **Vacuum All Collectibles (Full Map) [F6]**:
  - Instantly locates every `CollectibleBase` in the scene and calls `col.MoveToPlayer(playerAttr)`, pulling all experience scrolls and drops from the entire map to your hero in one pulse.
- **Auto-Claim & Open All Chests (Sequential)**:
  - Locates all `CaptureChestBase` chests in the level and triggers `Rpc_Open` sequentially via a coroutine with a **1.5-second pacing delay**.
  - Pacing prevents chest reward UI overlays from stacking on top of each other and locking the screen.
- **Free Library Upgrades**:
  - Harmony Prefixes on `LibraryManager.PurchaseIsAvailable` (returns `true`) and `LibraryManager.GetCurrentCost` (returns `0`).
  - Allows purchasing Academy permanent meta-progression upgrades at zero scroll cost.
- **Add Scrolls**:
  - Instant buttons to add `+10,000` or `+100,000` Academy Scrolls directly into `LibraryManager.Instance.ReceiveScrolls()`.
- **Luck Multiplier**:
  - Multiplies `AttributeType.Luck` by `1.0x`, `2.0x`, `5.0x`, or `10.0x` for higher rarity card offerings and drop rates.

---

### Tab 3: Progression & Cards
- **Instant Level Up**:
  - Calls `Level.DebugLevelUp()` to instantly grant `+1` or `+5` hero levels and trigger upgrade cards.
- **Experience Multiplier**:
  - Hooks `Level.AddExperience` to scale incoming experience by `1.0x`, `2.0x`, `5.0x`, or `10.0x`.
- **Infinite Level-Up Rerolls (Track A.2)**:
  - Hooks `UpgradeModalView.UpdateRerollButton` and `UpgradeModalView.OnReroll` to keep `inv.rerollRemaining` pinned to **99**.
  - The native in-game Reroll button never disables or runs out.
- **Force Reroll Choices Now**:
  - Direct button in the GUI that calls `UpgradeModalView.ShowChoices(true)` to refresh the active card selection immediately.
- **Run Card Banish System**:
  - Displays the active cards currently offered on level-up.
  - Clicking **Banish** adds the ability to `TrainerPlugin.BanishedAbilities` and immediately rerolls choices.
  - Banished abilities are intercepted in `UpgradeController.GetChoices` postfix and completely filtered out of future level-up card pools for the remainder of the run.
  - Includes buttons to **Unbanish** specific abilities or **Clear All Banished Abilities**.
- **Native Developer Cheats**:
  - **Unlock All Weapons, Evolutions & Abilities**: Invokes `PlayerProgressionManager.Instance.UnlockAllWeaponsAndAbilities()`.
  - **Complete All Campaign Chapters**: Invokes `ChaptersManager.Instance.Cheat_CompleteAllChapters()`.
  - **Complete Current Bounty Assignments**: Invokes `AssignmentsManager.Instance.Cheat_CompleteCurrentAssignments()`.
- **Developer Debug Menu** (the game's own, left in the release build):
  - **How the game does it:** at startup `DebugModulesEnabler` decides once whether the debug
    menu is enabled (off in release builds) and creates only the debug modules flagged for
    release. The menu object (`DebugMenuView`) switches itself off at start when the flag is
    off. It is opened with the game's own Rewired input action `ToggleDebugMenu`. Its
    contents come from the game's debug-action lists:
    - **Meta** (`DebugActions`): complete chapters, unlock/lock heroes, complete or pick
      assignments, unlock all weapons and abilities, reset or **delete all save data**,
      info/FPS panels, error console.
    - **Hub** (`HubDebugActions`): unlock maps, quick-play dialog, lobby panel, save-data
      viewer, currencies, presents, relations.
    - **Match** (`GameplayDebugActions`): level up, toggle invincible, unlock max slots, hide
      HUD, game over / win, time slots, give or upgrade any weapon/active/passive, spawn
      NPCs, single mobs, waves, minibosses, events, XP, health, scrolls, chests, potions,
      shields.
  - **How to use it:** tick **Enable developer debug menu** and restart the game (the choice
    is read at startup, so a restart is needed). The mod then forces the flag on and also
    creates the internal-build debug modules. The tab shows the key the game binds to the
    menu (also logged as `[DebugMenu] ToggleDebugMenu action is bound to: ...`), and
    **Show Debug Menu Now** activates any debug menu objects in the current scene.
  - `LogOutput.log` lists every debug module in the build (`[DebugMenu] Module '...'`).
  - Disabled in co-op. **Careful:** the menu includes destructive actions such as deleting
    all save data; NecromanticSave keeps automatic backups, but treat it as a developer tool.

---

### Tab 4: World & Camera
- **Game Speed (Turbo Mode)**:
  - Adjusts global timescale: `1.0x` (F2), `1.5x`, `2.0x` (F3), `3.0x` (F4), `5.0x`.
- **Freeze Enemy Spawner**:
  - Hooks Harmony Prefixes on `EventsSpawner.UnPausedFixedUpdateNetwork` and `WavesSpawner.UnPausedFixedUpdateNetwork` returning `false`.
  - **Correction**: This is a **pure pause** on the spawner's tick logic. It **does NOT reset wave or event indices** when unfreezing. When turned off, spawning resumes seamlessly from the exact second and wave it was paused.
- **Skip Clock (+1 Min / +5 Min)**:
  - **Correction**: Adds a relative delta (+60s or +300s) to `Map.Instance.GameTimerSeconds` and syncs `EventsSpawner` and `WavesSpawner` forward via `MoveToTimeSlot(targetTime)`. It **does NOT perform an absolute rewind**.
- **Tactical Camera Zoom** (`0.8x`, `1.0x`, `1.4x`, `1.8x`, `2.2x`, `3.0x`):
  - Moves the gameplay camera further from (or closer to) the player while keeping its field of
    view, so the visible area grows with the multiplier and nothing is distorted. The far clip
    plane is pushed back to match so the ground isn't cut off.
  - Works with whichever Cinemachine follow component the camera uses (framing transposer
    distance, transposer follow offset, or third-person distance). Only if none is present
    does it fall back to widening the field of view (capped at 110°). The label shows which
    method is active, and `LogOutput.log` logs a `Camera zoom:` line with the details.
  - Original values are restored when you pick `1.0x` or change scenes. If the game moves the
    camera itself, the zoom follows the new position.

### Tab 5: Hotkeys
- Lists **every hotkey of every suite plugin** (Trainer, QoL, Endless, Save), grouped by plugin.
- Click a key, then press the new key (Esc cancels). It applies immediately and is saved to
  that plugin's `.cfg` file. While waiting, all suite hotkeys are paused so the key you press
  doesn't also trigger its old action.
- A key already used by another action is rejected with a message naming that action.
  Modifier keys alone (Ctrl/Shift/Alt) are ignored.
- **Reset** returns one key to its default; **Reset All Hotkeys to Defaults** resets everything.
- The QoL restart key shows as `Ctrl+R` while `RequireCtrlForRestart = true`; rebinding changes
  the key, and the Ctrl requirement stays a `.cfg` setting.

---

## 5. NecromanticQoL (v1.0.0) Feature Guide

### 1. Live DPS & Damage Meter HUD (`F9`)
Press **`F9`** to toggle the combat telemetry HUD overlay.

```text
+---------------------------------------+
| Combat & DPS Meter             [Drag] |
+---------------------------------------+
| DPS: 1,482.5             Time: 08:34  |
| Total Dmg: 724,190       Kills: 2,410 |
| ── Damage by Ability ──────────────── |
| Fire Shard             421,050 (58.1%)|
| Daggers                185,200 (25.6%)|
| Lightning Staff        117,940 (16.3%)|
|                                       |
| [F9] Toggle HUD  |  [Ctrl+R] Restart  |
+---------------------------------------+
```

- **Rolling DPS Calculation**:
  - Accurately tracks non-overkill damage dealt over a rolling window (default 5.0 seconds).
  - Formula: `CurrentDPS = RollingWindowSum / WindowDuration`.
- **True Damage Accounting**:
  - Hooks Harmony Postfix on `DynamicAttributes.ApplyDamage`. Reads `__result.damage`, ensuring only actual damage applied to enemy health is counted (overkill on low-health mobs is ignored).
  - Skips player self-damage and zombie decoy hits.
- **Kill Tracker**:
  - Tracks unique killed enemy instance IDs upon fatal hit (`__result.killed == true`) and harmonizes with the native `KillsCounter` component.
- **Per-Ability Breakdown**:
  - Displays each weapon/spell, the absolute damage inflicted, and its percentage share of total run damage.
- **Zero-Lag Architecture**:
  - Entirely event-driven via Harmony postfix. Zero `FindObjectsOfType` calls in `Update()`.

---

### 2. Instant Run Restart (`Ctrl + R`)
- Press **`Ctrl + R`** during any active run or on the Game Over screen to instantly start a fresh run.
- **Solo Guarded**:
  - Strictly inspects `AppService.Instance.IsSingleplayer`. If triggered in an active co-op session, restart is safely blocked and an on-screen warning banner appears:  
    `"Instant restart is disabled in co-op sessions!"`
- **Complete Run Reset**:
  - Triggers `GameEndManager.Instance.RestartGame()`.
  - Resets map clock: `Map.Instance.SetFakeTime(0)` and `Map.Instance.GameTimerSeconds = 0`.
  - Resets session countdown: `Session.GameTimer = PausableTickTimer.CreateFromSeconds(...)`.
  - Dismisses Game Over dialog if active (`GameEndManager.Instance.Revived()`).
  - Flushes damage telemetry and kill stats clean to zero.

---

### 3. Splash Screen Skip
- Skips publisher and studio intro splash videos on game startup (`SplashScreenRunner.Init` prefix).
- Automatically invokes the engine's `OnSplashScreensCompleted` callback, landing on the Main Menu in seconds without black screens or freezes.

### 4. Minimap (`M`)
The game has no minimap; this adds one in the top-right corner during matches.
- **Orientation:** rotates with the camera, so up on the map is up on screen. You are the
  white square in the centre.
- **Markers:**
  | Marker | Meaning |
  | :--- | :--- |
  | small red | regular enemy |
  | orange | elite |
  | purple (large) | miniboss |
  | pink (largest) | boss |
  | yellow | chest |
  | cyan | other player |
  | tiny green | XP gems, scrolls and other pickups |
- Bosses, minibosses, chests and other players that are out of range stay pinned to the map
  edge in their direction, so you always know where to head.
- **`M`** toggles it (rebindable in the Trainer's Hotkeys tab). `[Minimap]` settings in
  `com.necromantic.qol.cfg`: `EnableMinimap`, `SizePixels` (at 1080p, scales with
  resolution), `RangeUnits` (world units from you to the edge), `ShowPickups`.
- **Performance:** the scene is scanned 4 times per second (up to 400 pickups drawn); only
  positions are read each frame. Dead and pooled (inactive) enemies are skipped.
- Information only, so it also works in co-op.

---

## 6. NecromanticEndless (v1.0.0) Feature Guide

Press **`F10`** to toggle the Endless Mode configuration and status HUD.

```text
+-------------------------------------------------------------+
| Endless / Nightmare Mode Status (F10)                       |
+-------------------------------------------------------------+
| [X] Endless Mode Enabled                                    |
| [ ] Nightmare Mode Enabled (Base 1.5x)                      |
| [X] Show On-Screen HUD Line                                 |
|                                                             |
| Match Time: 18:45 (Normal Cap: 15:00)                       |
| Extra Time: +03m 45s (+3 minutes)                           |
| Mob Health Multiplier: x1.52 (+15.0%/min)                   |
| Mob Damage Multiplier: x1.33 (+10.0%/min)                   |
| Waves Spawner: Wave 14 / 16                                 |
| Events Spawner: Event 5 / 7                                 |
+-------------------------------------------------------------+
```

### Core Mechanics

1. **Boss Chest Flow Bypass**:
   - In vanilla, opening the final level boss chest flags `isFinalChest = true` and triggers a victory scene that ends the match.
   - NecromanticEndless intercepts `BookObtainModalView.CloseModal` via Harmony Prefix.
   - When the final chest is opened, it awards complete victory credit to your account (`PlayerProgressionManager.Instance.UpdateGameplayData`), sets `isFinalChest = false`, and unpauses gameplay cleanly.
2. **Victory Flow Suppression**:
   - Intercepts `GameEndManager.StartGameEndFlow(bool win)`.
   - When `win == true`, the victory screen is suppressed, allowing the run to continue forever.
   - When `win == false` (hero dies), the defeat flow is allowed through normally so players can conclude their run whenever they choose.
3. **Continuous Overtime Clock**:
   - Vanilla map clocks halt when the level timer hits the session duration limit.
   - `Map.FixedUpdateNetwork` postfix monitors session expiration. When expired, it automatically increments `fakeTime` by `RealDuration` and creates a fresh timer chunk.
   - Match time continues ticking upward indefinitely (`15:01`, `20:00`, `35:00+`).
4. **Infinite Wave & Event Looping**:
   - `WavesSpawner.UnPausedFixedUpdateNetwork` postfix: when reaching the end of the wave script, it resets the wave index to the late-game wave block (last 3 waves) and invokes `StartNextWave()`.
   - `EventsSpawner.UnPausedFixedUpdateNetwork` postfix: continuously cycles late-game elite minibosses (last 3 events) and subtier swarm events (last 4 subevents).
5. **Exponential Overtime Scaling**:
   - Overtime minutes calculate dynamically: `extraMinutes = max(0, (currentSeconds - normalDuration) / 60)`.
   - Health formula: `HealthMultiplier = (1 + HealthScalePercentPerMinute / 100) ^ extraMinutes`.
   - Damage formula: `DamageMultiplier = (1 + DamageScalePercentPerMinute / 100) ^ extraMinutes`.
   - Applied via Harmony Postfix on `MobBehaviour.ModifyAttributesByType` for standard mobs, elites, minibosses, and bosses.
6. **Nightmare Mode**:
   - Applies an immediate base difficulty multiplier (default `1.5x`) to all mob health and damage from minute 0, stacking with per-minute overtime scaling.
7. **HUD Line**:
   - Displays an unobtrusive top-center notification:  
     `ENDLESS +3m, mobs x1.52` or `NIGHTMARE +5m, mobs x3.02`.

---

## 7. NecromanticData (v1.0.0) Feature Guide

`NecromanticData` provides a data modding framework that interfaces with the game's underlying **BGDatabase** (`BansheeGz.BGDatabase.BGRepo`).

### 1. Automated Reference Export
- On the first launch (or every launch with `ForceReExport = true`), the framework exports all **36 database tables** (vanilla values, before overrides) to:  
  `BepInEx\config\NecromanticData\export\<Table>.json`
- Each JSON file contains formatted, human-readable entries with row names, field names, data types, and current values.
- Modders can inspect every weapon, character, mob, wave phase, and upgrade value directly.

### 2. Startup Cache Neutralization
- The game's `DataTableHookup.InitService()` caches database tables into memory arrays during early startup.
- `NecromanticData` hooks both `BGRepo.OnLoad` and applies a Harmony Prefix on `DataTableHookup.InitService()`.
- **Result**: All user overrides and balance presets are applied to in-memory tables **before** `DataTableHookup` reads them, ensuring full effect with zero stale data.

### 3. Shipped Balance Presets
All presets are disabled by default and can be toggled in `BepInEx\config\com.necromantic.data.cfg`:

- **Weapon Buffs (`Presets.EnableWeaponBuffs`)**:
  - Rebalances underpowered starting weapons to achieve parity with top weapons like Fire Shard and Daggers.
  - *Necromancer (`Wpn_Necro_L1`)*: Value `8.0` → `18.0`, Cooldown `7.0s` → `4.5s`.
  - *Shadow Knight (`Wpn_ShdKnight_L1`)*: Value `20.0` → `32.0`, Cooldown `3.0s` → `2.2s`, Size `0.6` → `1.2`.
  - *Ranger (`Wpn_Ranger_L1`)*: Value `20.0` → `30.0`, Cooldown `2.0s` → `1.6s`.
  - *Fighter (`Wpn_Fighter_L1`)*: Value `25.0` → `35.0`, Cooldown `1.2s` → `1.0s`.
  - *Bard (`Wpn_Bard_L1`)*: Value `4.0` → `6.0`, Size `1.4` → `1.8`.
- **Hard Mode (`Presets.EnableHardMode`)**:
  - Increases enemy lethality, movement speed, and tightens phase pacing.
  - *Undead T0 Unarmed*: Health `10.0` → `18.0`, Speed `1.85` → `2.30`.
  - *Undead T0 1H*: Health `20.0` → `35.0`, Speed `1.75` → `2.20`, Damage `2.0` → `3.0`.
  - *Undead T0 Shield*: Health `30.0` → `55.0`, Speed `1.50` → `1.90`, Damage `2.0` → `3.0`.
  - *Undead T1 Archer*: Health `20.0` → `38.0`, Speed `2.30` → `2.70`, Damage `2.0` → `4.0`.
  - *Undead T1 2H*: Health `20.0` → `45.0`, Speed `2.00` → `2.40`, Damage `5.0` → `8.0`.
  - *Phase Durations*: Compressed from `20.0s` to `15.0s`.
- **Fast Waves (`Presets.EnableFastWaves`)**:
  - Turbo pacing mode. Wave phase durations compressed from `20.0s` to `10.0s`.
  - Hero base movement speeds boosted by +35% (`5.0` → `6.75`) for all 6 characters to compensate.

---

## 8. NecromanticSave (v1.0.0) Feature Guide

Toggle the Save Manager & Editor window at any time using **`F11`**. The window (ID `99903`) is movable by dragging its title bar and operates independently from the Trainer (`F1`) and Endless (`F10`) menus.

```text
+-------------------------------------------------------------+
| Necromantic Save Manager & Editor (v1.0.0)           [F11]  |
+-------------------------------------------------------------+
| [Backups & Restore] | [Save Editor] | [JSON Export/Import]  |
+-------------------------------------------------------------+
```

### Tab 1: Backups & Restore
- **Active Save File Telemetry**:
  - Displays the live path to `BlinkmoonSave.es3` in `%USERPROFILE%\AppData\LocalLow\Blinkmoon\Necromantic\`.
  - Shows file status (`Present & Healthy` or `Not Found`), exact size in KB, and last modification timestamp.
- **Manual Backups**:
  - **Create Backup Now**: Copies `BlinkmoonSave.es3` to a timestamped backup:  
    `BepInEx\config\NecromanticSave\Backups\BlinkmoonSave_yyyyMMdd_HHmmss.es3`.
  - **Pruning**: Automatically enforces `MaxBackups = 50` by deleting the oldest backups first when the limit is exceeded.
- **Backup Browser & Instant Restore**:
  - Lists all existing backups sorted newest first with index numbers, filenames, file sizes, and creation timestamps.
  - **Restore Selected Backup**: Automatically creates a safety backup (`PreRestore_BlinkmoonSave`), overwrites `BlinkmoonSave.es3`, and calls `PlayerProgressionManager.Instance.LoadOrStartData()` + `InvokeDataChanged()` to reload the restored state into the active game memory instantly.
  - **Delete Selected**: Safely removes unwanted backups from disk.
  - **Open Backups Folder**: Opens the Windows Explorer directory containing your backup `.es3` files.
- **Automation Options**:
  - **Auto-backup on game launch**: Generates a backup (`BlinkmoonSave_Launch_<timestamp>.es3`) every time the game starts.
  - **Auto-backup on match start**: Generates a backup (`BlinkmoonSave_MatchStart_<timestamp>.es3`) whenever a player spawns into a match.

---

### Tab 2: Save Editor
The Save Editor operates directly on the game's in-memory `PlayerProgressionManager.Instance.PersistentData` (`PlayerData`), ensuring complete safety without raw file manipulation.

- **Scrolls Economy (Academy Upgrades)**:
  - Displays current in-memory Academy Scrolls.
  - Input field to set exact amount and click **Apply**.
  - Quick adjustment buttons: **`+1,000`**, **`+10,000`**, **`+100,000`**, and **`Max (999,999)`**.
- **Cheese Currency**:
  - Displays current in-memory Cheese.
  - Input field to set exact amount and click **Apply**.
  - Quick adjustment buttons: **`+10`**, **`+100`**, and **`+1,000`**.
- **Lifetime Statistics (Telemetry)**:
  - **Matches Played**: Live counter with manual text edit and `+1` / `+5` increment buttons.
  - **Matches Won**: Live counter with manual text edit and `+1` / `+5` increment buttons.
  - **Enemies Killed**: Live counter with manual text edit and `+1k` / `+10k` increment buttons.
- **Progression Cheats & Shortcuts**:
  - **Unlock All Weapons & Hero Abilities**: Calls native `PlayerProgressionManager.Instance.UnlockAllWeaponsAndAbilities()`.
  - **Reset Progression to New Game Defaults**: Guarded by a **Confirm Reset** toggle to prevent accidental wipe; takes a `PreReset_BlinkmoonSave_*.es3` safety backup first, then calls `ResetPlayerData()`.
- **Persistence & Disk Commit**:
  - **Commit & Save Changes to Disk**: Commits edited values to `PersistentData` and calls official `PlayerProgressionManager.Instance.SaveData()`. This lets the native Easy Save 3 engine serialize cleanly with **zero risk of file corruption**.
  - **Reload from Disk (Discard Edits)**: Calls `LoadOrStartData()` and `InvokeDataChanged()` to discard any uncommitted in-memory edits.

---

### Tab 3: JSON Export & Import
- **Export PlayerData to JSON**:
  - Serializes the active `PlayerData` object into formatted, readable JSON saved at:  
    `BepInEx\config\NecromanticSave\PlayerData_Export.json`.
  - Exports all scalar properties (`Scrolls`, `Cheese`, `LibrarySpent`), weapon lists, character unlocks, achievements, and cumulative telemetry data.
- **Import PlayerData from JSON**:
  - Automatically captures a safety backup (`PreJsonImport_BlinkmoonSave`).
  - Reads `PlayerData_Export.json`, validates deserialized fields, updates `PersistentData` and backing `PlayerMainData` fields, and calls `SaveData()`.
  - Allows power users to edit progression using any external text editor (VS Code, Notepad++, etc.) and import it cleanly.

---

### Standalone Offline Utility: `SaveBackupTool.ps1`
Located at: `src\NecromanticSave\SaveBackupTool.ps1` in the repository.

A standalone PowerShell script that allows backing up, listing, and restoring saves from the CLI without launching the game. It finds the game through Steam and uses the same `Backups\` folder as the in-game plugin (override with `-GameDir "<path>"`).

```powershell
# Create a timestamped backup
powershell -ExecutionPolicy Bypass -File .\src\NecromanticSave\SaveBackupTool.ps1 -Backup

# List all existing backups
powershell -ExecutionPolicy Bypass -File .\src\NecromanticSave\SaveBackupTool.ps1 -List

# Restore backup #1
powershell -ExecutionPolicy Bypass -File .\src\NecromanticSave\SaveBackupTool.ps1 -Restore 1

# Launch interactive menu
powershell -ExecutionPolicy Bypass -File .\src\NecromanticSave\SaveBackupTool.ps1
```

> [!IMPORTANT]
> `SaveBackupTool.ps1` actively checks if `Necromantic.exe` is running and warns the user before restoring to prevent file lock contention.

---

## 9. Configuration Files Reference

### 0. `com.necromantic.trainer.cfg`
Created on first launch. Holds hotkeys and the debug-menu switch (cheat toggles themselves
reset every session).
```ini
[Developer]
## Enable the game's built-in developer debug menu (read at game start; restart to apply).
EnableDevDebugMenu = false

[Hotkeys]
ToggleMenuKey = Insert
ToggleMenuAltKey = Home
SpeedNormalKey = F2
Speed2xKey = F3
Speed3xKey = F4
NukeMobsKey = F5
VacuumLootKey = F6
GodModeKey = F7
SuperMagnetKey = F8
```

### 1. `com.necromantic.save.cfg`
```ini
[General]
## Hotkey to toggle the Save Manager & Editor window.
ToggleKey = F11

[Backups]
## Automatically backup BlinkmoonSave.es3 when the game launches.
AutoBackupOnLaunch = true

## Automatically backup BlinkmoonSave.es3 when a match starts.
AutoBackupOnMatchStart = true

## Maximum number of backups to keep (oldest pruned first). 0 for unlimited.
MaxBackups = 50

## Subfolder under BepInEx/config/NecromanticSave/ to store backups.
BackupSubFolder = Backups
```

### 2. `com.necromantic.qol.cfg`
```ini
[General]
## Skip studio logo and intro splash screens on game startup.
SkipIntro = true

[Hotkeys]
## Key to restart the current run.
RestartKey = R
## Require Ctrl modifier with RestartKey (Ctrl+R).
RequireCtrlForRestart = true

[HUD]
## Show live DPS and damage meter HUD overlay.
EnableHud = true
## Hotkey to toggle HUD visibility.
ToggleHudKey = F9
## Rolling window duration in seconds for live DPS calculation.
DpsWindowSeconds = 5.0
## Initial X / Y position of HUD window.
WindowX = 10.0
WindowY = 10.0

[Minimap]
## Show the minimap during matches.
EnableMinimap = true
## Show/hide the minimap.
ToggleMinimapKey = M
## Minimap width/height in pixels at 1080p (scaled with resolution).
SizePixels = 220
## World units from the player to the map edge.
RangeUnits = 60
## Show XP gems, scrolls and other pickups.
ShowPickups = true
```

### 3. `com.necromantic.endless.cfg`
```ini
[General]
## Enable Endless Mode (continues past level timer and final boss).
EndlessModeEnabled = false
## Enable Nightmare Mode (base difficulty multiplier and aggressive scaling).
NightmareModeEnabled = false

[Scaling]
## Percentage mob health increases per extra minute past normal duration.
HealthScalePercentPerMinute = 15.0
## Percentage mob damage increases per extra minute past normal duration.
DamageScalePercentPerMinute = 10.0

[Nightmare]
## Base health and damage multiplier applied when Nightmare Mode is enabled.
NightmareBaseMultiplier = 1.5

[UI]
## Keyboard shortcut to toggle Endless Mode status window.
StatusHotkey = F10
## Show small HUD line 'ENDLESS +Nm, mobs xN.NN' on screen while active.
ShowHudLine = true
```

### 4. `com.necromantic.data.cfg`
```ini
[Framework]
## Automatically export database tables to export/ on launch if not already exported.
ExportOnStartup = true
## Force re-exporting all database tables on every launch.
ForceReExport = false
## Load and apply override JSON files from overrides/.
EnableOverrides = true
## Log every modified database field value to LogOutput.log.
LogDetailedChanges = true

[Presets]
## Enable the 'weapon_buffs' balance preset.
EnableWeaponBuffs = false
## Enable the 'hard_mode' preset.
EnableHardMode = false
## Enable the 'fast_waves' preset.
EnableFastWaves = false
```

---

## 10. Troubleshooting & FAQ

### Q: Why does pressing Insert or F11 not show the menu?
Open `BepInEx\LogOutput.log` and check, in order:
1. **`[ModHost] Plugin update loop is running (hotkeys active).`** If this line is missing,
   the plugins loaded but their `Update`/`OnGUI` never ran (on this game the BepInEx manager
   object is destroyed at startup unless it is hidden). Make sure
   `HideManagerGameObject = true` under `[Chainloader]` in `BepInEx\config\BepInEx.cfg`
   (`deploy-to-game.ps1` sets this) and that you are running the current DLLs.
   A `[ModHost] BepInEx manager object was destroyed!` error means the same thing.
2. **`NecromanticCommon.dll`** is in `BepInEx\plugins\`. Without it the Trainer, QoL,
   Endless and Save plugins fail to load.
3. **`[HotkeyHelper] Hotkey pressed: ...`** appears when you press the key. If not, check that
   the game window has focus and that an overlay (Steam, Discord, GeForce Experience) isn't
   capturing function keys. **`Insert`** is an alternate key for the Trainer, and every
   hotkey can be rebound in the Trainer's **Hotkeys** tab (or its plugin's `.cfg` file).

### Q: Can using the Save Editor corrupt my save file?
- **No.** Raw disk writes are never performed directly by `NecromanticSave`. Modifications are applied to `PlayerProgressionManager.Instance.PersistentData` in system RAM. Disk writes route through the official `PlayerProgressionManager.Instance.SaveData()` call, allowing Easy Save 3 to serialize natively with valid headers and checksums. Additionally, every restore or JSON import automatically captures a safety backup first.

### Q: Why does Ctrl+R show "Instant restart is disabled in co-op sessions!"?
- Instant restart modifies local simulation clocks and state authority which desynchronizes peer clients in BadFusion multiplayer sessions. To prevent desyncs and broken lobbies, restart is strictly guarded for solo play.

### Q: Where are backups stored?
- Backups are stored in:  
  `<GameDir>\BepInEx\config\NecromanticSave\Backups\`.
- You can also open this folder directly by clicking **Open Backups Folder** in the F11 menu.

### Q: I noticed Rapid Fire is no longer 60 shots per second. Why?
- Earlier versions ran at an uncapped 60Hz tick rate, which saturated the Wwise sound engine queue and caused missing sound effects, crackling, and crashes. Rapid Fire is now clamped to **20Hz (`0.05s`)**, which is completely inaudible in difference during gameplay while preserving 100% audio engine stability.

---

## 11. In-Game Testing Checklist

Follow this checklist to verify full functionality of the entire 5-plugin suite:

- [ ] **Startup & Splash Skip**:
  - Launch `Necromantic.exe`.
  - Confirm the studio intro splash screen is skipped and the game lands on the Main Menu in seconds.
- [ ] **Save Manager Verification (`F11`)**:
  - In Main Menu or Hub, press **`F11`** to open the Save Manager & Editor window.
  - Verify active save file status shows `Present & Healthy` with correct size and timestamp.
  - Click **Create Backup Now** and confirm a new entry appears in the backups list.
  - Switch to **Save Editor** tab. Click **`+10,000`** on Scrolls and **`+100`** on Cheese.
  - Click **Commit & Save Changes to Disk**. Verify Academy Scrolls in the game UI reflect the new total.
  - Switch to **JSON Export / Import** tab. Click **Export PlayerData to JSON**. Confirm `PlayerData_Export.json` is generated.
- [ ] **Hub Verification (`Insert`)**:
  - Press **`Insert`** in the Hub to verify the Trainer GUI opens.
  - Toggle **Free Library Upgrades** and verify Academy upgrades can be purchased at 0 scroll cost.
- [ ] **Match Telemetry & HUD (`F9`)**:
  - Enter any stage (e.g., Farmland).
  - Verify the **Combat & DPS Meter** is visible in the top-left corner.
  - Attack enemies and verify rolling DPS and per-ability damage values update in real time.
  - Press **`F9`** to toggle HUD visibility off and on.
- [ ] **Combat Cheats (`F5`, `F6`, `F7`, `F8`)**:
  - Press **`F7`** to toggle God Mode. Stand in enemies and confirm zero damage taken.
  - Press **`F8`** to toggle Super Magnet. Notice drops gravitating to hero within ~70m.
  - Press **`F6`** to vacuum all drops across the entire map instantly.
  - Press **`F5`** to trigger Screen Wipe / Nuke. Confirm all active mobs are eliminated.
- [ ] **Progression & Banishing**:
  - In Trainer **Progression & Cards** tab, check **Infinite In-Game Level-Up Rerolls**.
  - Level up and verify the native Reroll button shows `99` remaining and can be pressed continuously.
  - In the Trainer window, click **Banish** on a card. Verify the card is removed and excluded from subsequent card offerings.
- [ ] **Endless Mode (`F10`)**:
  - Press **`F10`** and enable **Endless Mode**.
  - In Trainer **World** tab, use **Skip Clock (+5 Min)** to advance game time near the 15:00 mark.
  - Defeat the stage boss and open the final chest. Confirm the chest awards rewards, closes cleanly, and gameplay continues without triggering a victory screen.
  - Confirm the top HUD indicator reads `ENDLESS +Nm, mobs xN.NN`.
- [ ] **Instant Restart (`Ctrl + R`)**:
  - In solo match, press **`Ctrl + R`**.
  - Verify match clock resets to `00:00`, player respawns at origin, and DPS meter resets cleanly.
- [ ] **Offline Save Backup Tool**:
  - Exit the game. Open PowerShell in the root directory.
  - Run: `powershell -ExecutionPolicy Bypass -File .\src\NecromanticSave\SaveBackupTool.ps1 -List`.
  - Confirm all backups generated during testing are displayed with timestamps and file sizes.
