using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using Blinkmoon.GameSystem;
using Blinkmoon.GameSystem.UI;
using Blinkmoon.DebugModules;
using Cinemachine;
using NecromanticCommon;

namespace NecromanticTrainer
{
    [BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
    public class TrainerPlugin : BaseUnityPlugin
    {
        public const string PLUGIN_GUID = "com.necromantic.trainer";
        public const string PLUGIN_NAME = "Necromantic Advanced Trainer & QoL Suite";
        public const string PLUGIN_VERSION = "2.1.0";

        internal static ManualLogSource Log;
        private Harmony harmony;

        // --- Combat Settings ---
        public static bool GodMode = false;
        public static bool OneHitKill = false;
        public static bool RapidFire = false;
        public static float DamageMultiplier = 1.0f;
        public static float SpeedMultiplier = 1.0f;
        public static float AttackSpeedMultiplier = 1.0f;
        public static int ExtraProjectiles = 0;
        public static bool MobsFrozen = false;

        // --- Loot & Economy Settings ---
        public static bool SuperMagnet = false;
        public static float PickupMultiplier = 1.0f;
        public static bool FreeLibraryUpgrades = false;
        public static float LuckMultiplier = 1.0f;

        // --- Progression Settings ---
        public static float ExpMultiplier = 1.0f;
        public static bool EnableNativeDebugMenu = false;
        public static bool InfiniteRerolls = false;
        public static List<Inventory.AbilityType> BanishedAbilities = new List<Inventory.AbilityType>();

        // --- World & Camera Settings ---
        public static float GameSpeed = 1.0f;
        public static bool FreezeWaves = false;
        public static float CameraZoomMultiplier = 1.0f;

        // --- Camera Zoom Cache ---
        private static CinemachineVirtualCamera cachedVcam = null;
        private static float baseOrthoSize = -1f;
        private static float baseFov = -1f;
        private static bool lensModified = false;

        // --- Chest Coroutine State ---
        private static bool isOpeningChests = false;

        // --- Local Player Reference ---
        public static Character LocalCharacter = null;

        // --- Reflection Cache ---
        private static MethodInfo rpcOpenMethod = null;

        // --- GUI State ---
        private bool showGui = false;
        private Rect windowRect = new Rect(30, 30, 500, 650);
        private int selectedTab = 0;
        private string statusMessage = "Ready. Hotkeys can be changed in the Hotkeys tab.";
        private UpgradeModalView cachedModal = null;
        private float nextModalScanTime = 0f;
        private static float nextVcamScanTime = 0f;

        // --- Hotkeys (rebindable in the Hotkeys tab or com.necromantic.trainer.cfg) ---
        public static ConfigEntry<KeyCode> KeyMenu;
        public static ConfigEntry<KeyCode> KeyMenuAlt;
        public static ConfigEntry<KeyCode> KeySpeedNormal;
        public static ConfigEntry<KeyCode> KeySpeed2x;
        public static ConfigEntry<KeyCode> KeySpeed3x;
        public static ConfigEntry<KeyCode> KeyNuke;
        public static ConfigEntry<KeyCode> KeyVacuum;
        public static ConfigEntry<KeyCode> KeyGodMode;
        public static ConfigEntry<KeyCode> KeySuperMagnet;

        // --- Hotkeys tab rebind state ---
        private HotkeyBinding rebinding = null;
        private Vector2 hotkeyScroll = Vector2.zero;

        private void BindHotkeys()
        {
            // Not F1: the game binds F1 to its own developer debug menu (Rewired action ToggleDebugMenu).
            KeyMenu        = Config.Bind("Hotkeys", "ToggleMenuKey",    KeyCode.Insert, "Open/close the trainer window. (Avoid F1: the game uses it for its developer debug menu.)");
            KeyMenuAlt     = Config.Bind("Hotkeys", "ToggleMenuAltKey", KeyCode.Home,   "Alternate key to open/close the trainer window.");
            KeySpeedNormal = Config.Bind("Hotkeys", "SpeedNormalKey",   KeyCode.F2,     "Set game speed to 1x.");
            KeySpeed2x     = Config.Bind("Hotkeys", "Speed2xKey",       KeyCode.F3,     "Set game speed to 2x.");
            KeySpeed3x     = Config.Bind("Hotkeys", "Speed3xKey",       KeyCode.F4,     "Set game speed to 3x.");
            KeyNuke        = Config.Bind("Hotkeys", "NukeMobsKey",      KeyCode.F5,     "Kill every enemy on the map.");
            KeyVacuum      = Config.Bind("Hotkeys", "VacuumLootKey",    KeyCode.F6,     "Pull every collectible to the player.");
            KeyGodMode     = Config.Bind("Hotkeys", "GodModeKey",       KeyCode.F7,     "Toggle god mode.");
            KeySuperMagnet = Config.Bind("Hotkeys", "SuperMagnetKey",   KeyCode.F8,     "Toggle super magnet.");

            DevDebugMenu = Config.Bind("Developer", "EnableDevDebugMenu", false,
                "Enable the game's built-in developer debug menu. Read once at game start (the game decides " +
                "which debug modules to create during startup), so changes need a restart. Disabled in co-op.");
            EnableNativeDebugMenu = DevDebugMenu.Value;
        }

        // --- Developer debug menu ---
        public static ConfigEntry<bool> DevDebugMenu;
        /// <summary>Human-readable inputs bound to the game's ToggleDebugMenu action (from Rewired).</summary>
        public static string DebugMenuBindings = null;
        private const int RewiredActionToggleDebugMenu = 8; // RewiredConsts.Action.ToggleDebugMenu

        /// <summary>Reads which keys/buttons the game binds to its ToggleDebugMenu action.</summary>
        internal static void ReadDebugMenuBindings()
        {
            try
            {
                if (!Rewired.ReInput.isReady) return;
                List<string> found = new List<string>();
                foreach (Rewired.Player player in Rewired.ReInput.players.AllPlayers)
                {
                    foreach (Rewired.ActionElementMap map in player.controllers.maps.ElementMapsWithAction(RewiredActionToggleDebugMenu, false))
                    {
                        string name = map.controllerMap.controllerType + ": " + map.elementIdentifierName;
                        if (!found.Contains(name)) found.Add(name);
                    }
                }
                DebugMenuBindings = found.Count > 0 ? string.Join(", ", found.ToArray()) : "no input bound";
                if (Log != null) Log.LogInfo("[DebugMenu] ToggleDebugMenu action is bound to: " + DebugMenuBindings);
            }
            catch (Exception ex)
            {
                if (Log != null) Log.LogWarning("[DebugMenu] Could not read Rewired bindings: " + ex.Message);
            }
        }

        /// <summary>Activates any loaded DebugMenuView objects and reports what was found.</summary>
        private static string ShowDebugMenuViews()
        {
            int found = 0, activated = 0;
            foreach (Blinkmoon.DebugMenu.DebugMenuView view in Resources.FindObjectsOfTypeAll<Blinkmoon.DebugMenu.DebugMenuView>())
            {
                if (view == null || !view.gameObject.scene.IsValid()) continue; // skip prefab assets
                found++;
                if (!view.gameObject.activeSelf)
                {
                    view.gameObject.SetActive(true);
                    activated++;
                }
                if (Log != null) Log.LogInfo("[DebugMenu] DebugMenuView '" + view.name + "' in scene '" + view.gameObject.scene.name + "', activeInHierarchy=" + view.gameObject.activeInHierarchy);
            }
            if (found == 0) return "No debug menu is loaded in this scene. It may only exist in some scenes (try the hub or a match).";
            return "Debug menu objects found: " + found + ", activated: " + activated + ". Use the game's debug key to open/close it.";
        }

        private static string K(ConfigEntry<KeyCode> entry)
        {
            return entry != null ? HotkeyRegistry.KeyName(entry.Value) : "?";
        }

        private void Awake()
        {
            Log = Logger;
            ModHost.Protect(this, Log);
            Log.LogInfo(PLUGIN_NAME + " v" + PLUGIN_VERSION + " is loading...");
            BindHotkeys();

            try
            {
                Character.OnLocalPlayerSpawned += HandleLocalPlayerSpawned;
            }
            catch (Exception ex)
            {
                Log.LogWarning("Could not subscribe to OnLocalPlayerSpawned: " + ex.Message);
            }

            try
            {
                SceneManager.activeSceneChanged += HandleActiveSceneChanged;
            }
            catch (Exception ex)
            {
                Log.LogWarning("Could not subscribe to activeSceneChanged: " + ex.Message);
            }

            try
            {
                rpcOpenMethod = typeof(CaptureChestBase).GetMethod("Rpc_Open", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            }
            catch (Exception ex)
            {
                Log.LogWarning("Could not resolve Rpc_Open: " + ex.Message);
            }

            try
            {
                harmony = new Harmony(PLUGIN_GUID);
                harmony.PatchAll(Assembly.GetExecutingAssembly());
                Log.LogInfo("All Harmony patches registered successfully!");
            }
            catch (Exception ex)
            {
                Log.LogError("Failed to register Harmony patches: " + ex.ToString());
            }
        }

        private static void HandleLocalPlayerSpawned(Character c)
        {
            LocalCharacter = c;
            if (Log != null)
            {
                Log.LogInfo("Local player character tracked successfully: " + (c != null ? c.name : "null"));
            }
        }

        private static void HandleActiveSceneChanged(Scene oldScene, Scene newScene)
        {
            // If the camera survives the scene change, put it back to its original values
            // first; otherwise the next scan would capture the zoomed values as the new base.
            RestoreCameraZoom();

            // Rewired is initialised by now; log which input opens the game's debug menu.
            if (DebugMenuBindings == null) ReadDebugMenuBindings();

            // Invalidate camera cache across scene transitions
            cachedVcam = null;
            baseOrthoSize = -1f;
            baseFov = -1f;
            lensModified = false;
            isOpeningChests = false;
            if (Log != null)
            {
                Log.LogInfo("Scene changed to: " + newScene.name + ". Reset camera cache.");
            }
        }

        private void Update()
        {
            // Waiting for a key in the Hotkeys tab: block all suite hotkeys so the key being
            // assigned doesn't also run its old action. Cancel if the window was closed.
            if (rebinding != null)
            {
                if (!showGui) rebinding = null;
                else HotkeyHelper.SuppressFor(0.3f);
            }

            // Toggle GUI via HotkeyHelper
            if (HotkeyHelper.IsKeyDown(KeyMenu.Value, Log, "Trainer GUI") || HotkeyHelper.IsKeyDown(KeyMenuAlt.Value, Log, "Trainer GUI"))
            {
                showGui = !showGui;
                CanvasOverlay.Notify("Trainer GUI [" + K(KeyMenu) + "]: " + (showGui ? "OPEN" : "CLOSED"), 3.0f);
            }

            if (showGui)
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }

            // Co-op guard: cheats would affect other players' runs, so the trainer is
            // single-player only. Every Harmony patch is a no-op while its flag is off,
            // so resetting the flags here disables them all.
            if (IsCoopSession())
            {
                if (!coopLockActive)
                {
                    coopLockActive = true;
                    DisableAllCheats("Trainer disabled: co-op session.");
                    Log.LogInfo("Co-op session detected: trainer cheats disabled.");
                    CanvasOverlay.Notify("Trainer disabled in co-op sessions", 4.0f);
                }
                ConfigEntry<KeyCode>[] cheatKeys = { KeySpeedNormal, KeySpeed2x, KeySpeed3x, KeyNuke, KeyVacuum, KeyGodMode, KeySuperMagnet };
                foreach (ConfigEntry<KeyCode> k in cheatKeys)
                {
                    if (HotkeyHelper.IsKeyDown(k.Value, Log, "Blocked (co-op)"))
                    {
                        CanvasOverlay.Notify("Trainer disabled in co-op sessions", 2.5f);
                    }
                }
                ApplyCameraZoom();
                return;
            }
            coopLockActive = false;

            // Quick Hotkeys via HotkeyHelper
            if (HotkeyHelper.IsKeyDown(KeySpeedNormal.Value, Log, "Speed 1x"))
            {
                SetSpeed(1.0f);
                CanvasOverlay.Notify("Game Speed: 1.0x (Normal)", 2.5f);
            }
            if (HotkeyHelper.IsKeyDown(KeySpeed2x.Value, Log, "Speed 2x"))
            {
                SetSpeed(2.0f);
                CanvasOverlay.Notify("Game Speed: 2.0x (Fast)", 2.5f);
            }
            if (HotkeyHelper.IsKeyDown(KeySpeed3x.Value, Log, "Speed 3x"))
            {
                SetSpeed(3.0f);
                CanvasOverlay.Notify("Game Speed: 3.0x (Super Fast)", 2.5f);
            }
            if (HotkeyHelper.IsKeyDown(KeyNuke.Value, Log, "Nuke Mobs"))
            {
                NukeAllMobs();
                CanvasOverlay.Notify("Nuked all mobs on map!", 2.5f);
            }
            if (HotkeyHelper.IsKeyDown(KeyVacuum.Value, Log, "Vacuum Collectibles"))
            {
                VacuumAllCollectibles();
                CanvasOverlay.Notify("Vacuumed all collectibles to player!", 2.5f);
            }
            if (HotkeyHelper.IsKeyDown(KeyGodMode.Value, Log, "God Mode"))
            {
                GodMode = !GodMode;
                statusMessage = "God Mode: " + (GodMode ? "ENABLED" : "DISABLED");
                CanvasOverlay.Notify("God Mode: " + (GodMode ? "ENABLED" : "DISABLED"), 2.5f);
            }
            if (HotkeyHelper.IsKeyDown(KeySuperMagnet.Value, Log, "Super Magnet"))
            {
                SuperMagnet = !SuperMagnet;
                statusMessage = "Super Magnet: " + (SuperMagnet ? "ENABLED" : "DISABLED");
                CanvasOverlay.Notify("Super Magnet: " + (SuperMagnet ? "ENABLED" : "DISABLED"), 2.5f);
            }

            // Continuous Camera Zoom
            ApplyCameraZoom();
        }

        private static readonly FieldInfo appServiceRunnerField = AccessTools.Field(typeof(AppService), "_runner");
        private bool coopLockActive = false;

        /// <summary>
        /// True while a networked co-op session is running. AppService.IsSingleplayer can't be
        /// used here: it returns false whenever no session exists (menu, hub), which would
        /// disable the trainer there too.
        /// </summary>
        public static bool IsCoopSession()
        {
            try
            {
                AppService app = AppService.Instance;
                if (app == null || appServiceRunnerField == null) return false;
                Fusion.NetworkRunner runner = appServiceRunnerField.GetValue(app) as Fusion.NetworkRunner;
                if (runner == null) return false; // Unity null check also catches destroyed runners
                return runner.GameMode != Fusion.GameMode.Single;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Turns every trainer cheat off: toggles, multipliers, game speed, frozen mobs/waves,
        /// banished cards. Camera zoom (visual only) and hotkey bindings are kept. Used by the
        /// "Turn Off All Cheats" button and when a co-op session starts.
        /// </summary>
        private void DisableAllCheats(string status)
        {
            GodMode = false;
            OneHitKill = false;
            RapidFire = false;
            DamageMultiplier = 1.0f;
            SpeedMultiplier = 1.0f;
            AttackSpeedMultiplier = 1.0f;
            ExtraProjectiles = 0;
            SuperMagnet = false;
            PickupMultiplier = 1.0f;
            FreeLibraryUpgrades = false;
            LuckMultiplier = 1.0f;
            ExpMultiplier = 1.0f;
            InfiniteRerolls = false;
            BanishedAbilities.Clear();
            FreezeWaves = false;
            if (MobsFrozen)
            {
                MobsFrozen = false;
                FreezeAllMobs(false);
            }
            if (GameSpeed != 1.0f) SetSpeed(1.0f);
            statusMessage = status;
        }

        private void SetSpeed(float speed)
        {
            GameSpeed = speed;
            Time.timeScale = speed;
            statusMessage = "Game Speed set to " + speed + "x";
        }

        private void OnGUI()
        {
            if (!showGui) return;
            GUI.depth = -100;

            GUI.skin.window.fontSize = 14;
            GUI.skin.button.fontSize = 12;
            GUI.skin.label.fontSize = 12;
            GUI.skin.toggle.fontSize = 12;

            CaptureRebindKey();
            windowRect = GUI.Window(99902, windowRect, DrawWindow, "Necromantic Trainer & QoL Suite v" + PLUGIN_VERSION);
        }

        private void DrawWindow(int windowId)
        {
            GUILayout.BeginVertical();

            if (coopLockActive)
            {
                GUILayout.Label("<b>The trainer is disabled in co-op sessions.</b>");
                GUILayout.Label("Cheats would affect the other players' run. Play solo to use it.");
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Close Window (" + K(KeyMenu) + " / " + K(KeyMenuAlt) + ")", GUILayout.Height(28))) showGui = false;
                GUILayout.EndVertical();
                GUI.DragWindow();
                return;
            }

            string[] tabs = new string[] { "Combat", "Loot & Shop", "Progression", "World & Camera", "Hotkeys" };
            selectedTab = GUILayout.Toolbar(selectedTab, tabs);
            GUILayout.Space(8);

            switch (selectedTab)
            {
                case 0: DrawCombatTab(); break;
                case 1: DrawLootTab(); break;
                case 2: DrawProgressionTab(); break;
                case 3: DrawWorldTab(); break;
                case 4: DrawHotkeysTab(); break;
            }

            GUILayout.FlexibleSpace();
            GUILayout.Box("Status: " + statusMessage);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Turn Off All Cheats", GUILayout.Height(28)))
            {
                DisableAllCheats("All cheats turned off.");
                CanvasOverlay.Notify("All trainer cheats turned off", 2.5f);
                Log.LogInfo("All trainer cheats turned off from the menu.");
            }
            if (GUILayout.Button("Close Window (" + K(KeyMenu) + " / " + K(KeyMenuAlt) + ")", GUILayout.Height(28)))
            {
                showGui = false;
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        private void DrawHotkeysTab()
        {
            GUILayout.Label("<b>--- Hotkeys (all Necromantic plugins) ---</b>");
            GUILayout.Label("Click a key, then press the new one (Esc cancels). Changes apply immediately and are saved to each plugin's .cfg file.");
            GUILayout.Space(4);

            List<HotkeyBinding> bindings = HotkeyRegistry.GetAll();
            if (bindings.Count == 0)
            {
                GUILayout.Label("No hotkeys found.");
                return;
            }

            hotkeyScroll = GUILayout.BeginScrollView(hotkeyScroll, GUILayout.Height(380));
            string lastPlugin = null;
            foreach (HotkeyBinding b in bindings)
            {
                if (b.PluginName != lastPlugin)
                {
                    lastPlugin = b.PluginName;
                    GUILayout.Space(4);
                    GUILayout.Label("<b>" + b.PluginName + "</b>");
                }

                GUILayout.BeginHorizontal();
                GUILayout.Label("  " + b.Label, GUILayout.Width(190));
                string buttonText = (rebinding == b) ? "> Press a key... <" : b.KeyText;
                if (GUILayout.Button(buttonText, GUILayout.Width(150)))
                {
                    rebinding = (rebinding == b) ? null : b;
                    if (rebinding != null) statusMessage = "Press the new key for " + b.PluginName + ": " + b.Label + " (Esc cancels).";
                }
                if (b.Key != b.DefaultKey)
                {
                    if (GUILayout.Button("Reset", GUILayout.Width(60)))
                    {
                        b.Key = b.DefaultKey;
                        statusMessage = b.PluginName + ": " + b.Label + " reset to " + HotkeyRegistry.KeyName(b.DefaultKey) + ".";
                    }
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            GUILayout.Space(4);
            if (GUILayout.Button("Reset All Hotkeys to Defaults", GUILayout.Height(26)))
            {
                rebinding = null;
                foreach (HotkeyBinding b in bindings) b.Key = b.DefaultKey;
                statusMessage = "All hotkeys reset to defaults.";
            }
        }

        private static bool IsModifierKey(KeyCode k)
        {
            return k == KeyCode.LeftShift || k == KeyCode.RightShift || k == KeyCode.LeftControl || k == KeyCode.RightControl ||
                   k == KeyCode.LeftAlt || k == KeyCode.RightAlt || k == KeyCode.LeftCommand || k == KeyCode.RightCommand ||
                   k == KeyCode.LeftWindows || k == KeyCode.RightWindows || k == KeyCode.AltGr;
        }

        /// <summary>Called from OnGUI while a rebind is pending; consumes the next key press.</summary>
        private void CaptureRebindKey()
        {
            Event e = Event.current;
            if (rebinding == null || e == null || e.type != EventType.KeyDown || e.keyCode == KeyCode.None) return;
            KeyCode key = e.keyCode;
            e.Use();

            if (key == KeyCode.Escape)
            {
                statusMessage = "Rebind cancelled.";
                rebinding = null;
                return;
            }
            if (IsModifierKey(key)) return; // keep waiting for a real key

            HotkeyBinding conflict = HotkeyRegistry.FindConflict(key, rebinding);
            if (conflict != null)
            {
                statusMessage = HotkeyRegistry.KeyName(key) + " is already used by " + conflict.PluginName + ": " + conflict.Label + ". Press another key.";
                return;
            }

            KeyCode old = rebinding.Key;
            rebinding.Key = key;
            statusMessage = rebinding.PluginName + ": " + rebinding.Label + " " + HotkeyRegistry.KeyName(old) + " -> " + HotkeyRegistry.KeyName(key) + ".";
            if (HotkeyHelper.KeyCodeToVirtualKey(key) == 0)
            {
                statusMessage += " (Uncommon key: relies on Unity input only.)";
            }
            Log.LogInfo("Hotkey rebound: " + rebinding.PluginGuid + " " + rebinding.Entry.Definition.Key + " = " + key);
            rebinding = null;
        }

        private void DrawCombatTab()
        {
            GUILayout.Label("<b>--- Player & Combat Toggles ---</b>");
            GodMode = GUILayout.Toggle(GodMode, "  God Mode (Invincible & Zero Damage Taken) [" + K(KeyGodMode) + "]");
            OneHitKill = GUILayout.Toggle(OneHitKill, "  One-Hit Kill (999,999 Damage to All Mobs)");
            RapidFire = GUILayout.Toggle(RapidFire, "  Rapid Fire (Clamped to 0.05s Cooldown - Wwise Audio Safe)");

            GUILayout.Space(6);
            GUILayout.Label(string.Format("Damage Multiplier: <b>{0:F1}x</b>", DamageMultiplier));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("1x")) DamageMultiplier = 1.0f;
            if (GUILayout.Button("2x")) DamageMultiplier = 2.0f;
            if (GUILayout.Button("5x")) DamageMultiplier = 5.0f;
            if (GUILayout.Button("10x")) DamageMultiplier = 10.0f;
            if (GUILayout.Button("25x")) DamageMultiplier = 25.0f;
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label(string.Format("Movement Speed: <b>{0:F1}x</b>", SpeedMultiplier));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("1x")) SpeedMultiplier = 1.0f;
            if (GUILayout.Button("1.5x")) SpeedMultiplier = 1.5f;
            if (GUILayout.Button("2x")) SpeedMultiplier = 2.0f;
            if (GUILayout.Button("3x")) SpeedMultiplier = 3.0f;
            if (GUILayout.Button("5x")) SpeedMultiplier = 5.0f;
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label(string.Format("Attack Speed: <b>{0:F1}x</b> (divides your weapon cooldowns; min 0.05s{1})",
                AttackSpeedMultiplier, RapidFire ? "; Rapid Fire overrides this" : ""));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("1x")) AttackSpeedMultiplier = 1.0f;
            if (GUILayout.Button("1.5x")) AttackSpeedMultiplier = 1.5f;
            if (GUILayout.Button("2x")) AttackSpeedMultiplier = 2.0f;
            if (GUILayout.Button("3x")) AttackSpeedMultiplier = 3.0f;
            if (GUILayout.Button("5x")) AttackSpeedMultiplier = 5.0f;
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label(string.Format("Extra Projectile Quantity: <b>+{0}</b>", ExtraProjectiles));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+0")) ExtraProjectiles = 0;
            if (GUILayout.Button("+2")) ExtraProjectiles = 2;
            if (GUILayout.Button("+5")) ExtraProjectiles = 5;
            if (GUILayout.Button("+10")) ExtraProjectiles = 10;
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            GUILayout.Label("<b>--- Crowd & Mob Controls ---</b>");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Screen Wipe / Nuke All Mobs [" + K(KeyNuke) + "]", GUILayout.Height(30)))
            {
                NukeAllMobs();
            }
            bool newFreeze = GUILayout.Toggle(MobsFrozen, " Freeze Mobs", "Button", GUILayout.Height(30));
            if (newFreeze != MobsFrozen)
            {
                MobsFrozen = newFreeze;
                FreezeAllMobs(MobsFrozen);
                statusMessage = "Mobs " + (MobsFrozen ? "FROZEN" : "UNFROZEN");
            }
            GUILayout.EndHorizontal();
        }

        private void DrawLootTab()
        {
            GUILayout.Label("<b>--- Vacuum & Magnets ---</b>");
            SuperMagnet = GUILayout.Toggle(SuperMagnet, "  Super Magnet (~70m Drop Attraction Radius) [" + K(KeySuperMagnet) + "]");

            GUILayout.Space(6);
            if (GUILayout.Button("Vacuum All Collectibles to Player (Full Map) [" + K(KeyVacuum) + "]", GUILayout.Height(32)))
            {
                VacuumAllCollectibles();
            }

            if (GUILayout.Button(isOpeningChests ? "Opening Chests Sequentially..." : "Auto-Claim & Open All Chests (Sequential)", GUILayout.Height(28)))
            {
                if (!isOpeningChests)
                {
                    StartCoroutine(OpenChestsSequentially());
                }
            }

            GUILayout.Space(10);
            GUILayout.Label("<b>--- Academy Library & Scrolls Economy ---</b>");
            FreeLibraryUpgrades = GUILayout.Toggle(FreeLibraryUpgrades, "  Free Library Upgrades (0 Cost / Always Available)");

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+10,000 Scrolls", GUILayout.Height(28)))
            {
                AddScrolls(10000);
            }
            if (GUILayout.Button("+100,000 Scrolls", GUILayout.Height(28)))
            {
                AddScrolls(100000);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label(string.Format("Luck Multiplier: <b>{0:F1}x</b> (Better drops & rarity)", LuckMultiplier));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("1x")) LuckMultiplier = 1.0f;
            if (GUILayout.Button("2x")) LuckMultiplier = 2.0f;
            if (GUILayout.Button("5x")) LuckMultiplier = 5.0f;
            if (GUILayout.Button("10x")) LuckMultiplier = 10.0f;
            GUILayout.EndHorizontal();
        }

        private void DrawProgressionTab()
        {
            GUILayout.Label("<b>--- Level & Experience ---</b>");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Instant Level Up (+1 Level)", GUILayout.Height(28)))
            {
                InstantLevelUp(1);
            }
            if (GUILayout.Button("Add +5 Levels", GUILayout.Height(28)))
            {
                InstantLevelUp(5);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label(string.Format("Experience Multiplier: <b>{0:F1}x</b>", ExpMultiplier));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("1x")) ExpMultiplier = 1.0f;
            if (GUILayout.Button("2x")) ExpMultiplier = 2.0f;
            if (GUILayout.Button("5x")) ExpMultiplier = 5.0f;
            if (GUILayout.Button("10x")) ExpMultiplier = 10.0f;
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            GUILayout.Label("<b>--- Level-Up Rerolls & Banish (A.2) ---</b>");
            InfiniteRerolls = GUILayout.Toggle(InfiniteRerolls, "  Infinite In-Game Level-Up Rerolls (99 Rerolls Always Ready)");

            // OnGUI runs several times per frame; only rescan the scene a few times per second.
            if (Time.unscaledTime >= nextModalScanTime)
            {
                cachedModal = GameObject.FindObjectOfType<UpgradeModalView>();
                nextModalScanTime = Time.unscaledTime + 0.25f;
            }
            UpgradeModalView modal = cachedModal;
            if (modal != null && modal.gameObject.activeInHierarchy)
            {
                GUILayout.Space(6);
                GUILayout.Label("<color=yellow><b>Level-Up Card Selection Active:</b></color>");
                if (GUILayout.Button("Force Reroll Choices Now", GUILayout.Height(26)))
                {
                    try
                    {
                        Traverse.Create(modal).Method("ShowChoices", new object[] { true }).GetValue();
                        statusMessage = "Rerolled level-up choices!";
                    }
                    catch (Exception ex)
                    {
                        statusMessage = "Reroll failed: " + ex.Message;
                    }
                }

                // Show active choices and provide Banish buttons
                List<Inventory.AbilityType> choices = Traverse.Create(modal).Field("_choices").GetValue<List<Inventory.AbilityType>>();
                if (choices != null && choices.Count > 0)
                {
                    GUILayout.Label("Available Cards to Banish for Run:");
                    for (int i = 0; i < choices.Count; i++)
                    {
                        Inventory.AbilityType ab = choices[i];
                        GUILayout.BeginHorizontal();
                        GUILayout.Label("• " + ab.ToString(), GUILayout.Width(220));
                        if (GUILayout.Button("Banish", GUILayout.Width(70)))
                        {
                            if (!BanishedAbilities.Contains(ab))
                            {
                                BanishedAbilities.Add(ab);
                                statusMessage = "Banished " + ab.ToString() + " from run!";
                                Traverse.Create(modal).Method("ShowChoices", new object[] { true }).GetValue();
                            }
                        }
                        GUILayout.EndHorizontal();
                    }
                }
            }

            if (BanishedAbilities.Count > 0)
            {
                GUILayout.Space(6);
                GUILayout.Label(string.Format("Banished Abilities ({0}):", BanishedAbilities.Count));
                for (int b = 0; b < BanishedAbilities.Count; b++)
                {
                    Inventory.AbilityType banished = BanishedAbilities[b];
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("  [X] " + banished.ToString(), GUILayout.Width(220));
                    bool unbanish = GUILayout.Button("Unbanish", GUILayout.Width(80));
                    GUILayout.EndHorizontal();
                    if (unbanish)
                    {
                        // End the horizontal group before breaking, or IMGUI layout throws.
                        BanishedAbilities.RemoveAt(b);
                        break;
                    }
                }
                if (GUILayout.Button("Clear All Banished Abilities", GUILayout.Height(24)))
                {
                    BanishedAbilities.Clear();
                    statusMessage = "Cleared all banished abilities.";
                }
            }

            GUILayout.Space(10);
            GUILayout.Label("<b>--- Native Developer Unlocks ---</b>");
            if (GUILayout.Button("Unlock All Weapons, Evolutions & Abilities", GUILayout.Height(28)))
            {
                UnlockAllWeapons();
            }

            if (GUILayout.Button("Complete All Campaign Chapters", GUILayout.Height(28)))
            {
                CompleteChapters();
            }

            if (GUILayout.Button("Complete Current Bounty Assignments", GUILayout.Height(28)))
            {
                CompleteAssignments();
            }

            GUILayout.Space(6);
            GUILayout.Label("<b>--- Developer Debug Menu (built into the game) ---</b>");
            bool newDebug = GUILayout.Toggle(DevDebugMenu.Value, "  Enable developer debug menu (takes effect next game start)");
            if (newDebug != DevDebugMenu.Value)
            {
                DevDebugMenu.Value = newDebug;
                statusMessage = "Developer debug menu " + (newDebug ? "enabled" : "disabled") + ". Restart the game to apply.";
            }
            if (EnableNativeDebugMenu)
            {
                GUILayout.Label("Active this session. Open it with the game's own debug key" +
                    (string.IsNullOrEmpty(DebugMenuBindings) ? " (see LogOutput.log)." : ": " + DebugMenuBindings));
                if (DebugMenuBindings != null &&
                    (DebugMenuBindings.Contains("Keyboard: " + KeyMenu.Value) || DebugMenuBindings.Contains("Keyboard: " + KeyMenuAlt.Value)))
                {
                    GUILayout.Label("<color=yellow>That key also opens this trainer. Change the trainer's key in the Hotkeys tab.</color>");
                }
                if (GUILayout.Button("Show Debug Menu Now", GUILayout.Height(24)))
                {
                    statusMessage = ShowDebugMenuViews();
                }
            }
            else if (DevDebugMenu.Value)
            {
                GUILayout.Label("<color=yellow>Restart the game to activate it.</color>");
            }
        }

        private void DrawWorldTab()
        {
            GUILayout.Label("<b>--- Game Speed (Turbo Mode) ---</b>");
            GUILayout.Label(string.Format("Current Game Speed: <b>{0:F1}x</b> ({1}: 1x, {2}: 2x, {3}: 3x)", GameSpeed, K(KeySpeedNormal), K(KeySpeed2x), K(KeySpeed3x)));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("1.0x (Normal)")) SetSpeed(1.0f);
            if (GUILayout.Button("1.5x")) SetSpeed(1.5f);
            if (GUILayout.Button("2.0x (Turbo)")) SetSpeed(2.0f);
            if (GUILayout.Button("3.0x (Fast)")) SetSpeed(3.0f);
            if (GUILayout.Button("5.0x (Hyper)")) SetSpeed(5.0f);
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            GUILayout.Label("<b>--- Wave & Spawner Controls ---</b>");
            FreezeWaves = GUILayout.Toggle(FreezeWaves, "  Freeze Enemy Spawner (Paused via FixedUpdate prefix)");

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Skip Clock +1 Min")) SkipRunTime(60f);
            if (GUILayout.Button("Skip Clock +5 Min")) SkipRunTime(300f);
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            GUILayout.Label("<b>--- Tactical Camera Zoom ---</b>");
            string how = cachedVcam == null ? "applies once a match camera exists"
                       : zoomMode == ZoomMode.Fov ? "via field of view"
                       : "via camera distance";
            GUILayout.Label(string.Format("Camera Zoom: <b>{0:F1}x</b> ({1})", CameraZoomMultiplier, how));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("0.8x")) CameraZoomMultiplier = 0.8f;
            if (GUILayout.Button("1.0x (Default)")) CameraZoomMultiplier = 1.0f;
            if (GUILayout.Button("1.4x")) CameraZoomMultiplier = 1.4f;
            if (GUILayout.Button("1.8x")) CameraZoomMultiplier = 1.8f;
            if (GUILayout.Button("2.2x")) CameraZoomMultiplier = 2.2f;
            if (GUILayout.Button("3.0x")) CameraZoomMultiplier = 3.0f;
            GUILayout.EndHorizontal();
        }

        // --- Implementation Actions ---

        public static void NukeAllMobs()
        {
            try
            {
                DynamicAttributes[] allAttributes = GameObject.FindObjectsOfType<DynamicAttributes>();
                int count = 0;
                for (int i = 0; i < allAttributes.Length; i++)
                {
                    DynamicAttributes attr = allAttributes[i];
                    try
                    {
                        if (attr == null || attr.gameObject == null) continue;
                        if (attr.GetIsPlayer()) continue;
                        if (attr.isDead || !attr.IsAlive()) continue;

                        attr.ApplyDamage(
                            attr.transform.position,
                            999999f,
                            Fusion.PlayerRef.None,
                            null,
                            0f,
                            DynamicAttributes.DamageType.Regular,
                            Inventory.AbilityType.Invalid
                        );
                        count++;
                    }
                    catch (Exception ex)
                    {
                        if (Log != null)
                        {
                            Log.LogWarning("Nuke single mob skipped due to exception: " + ex.Message);
                        }
                    }
                }
                if (Log != null)
                {
                    Log.LogInfo("Screen Wipe executed: eliminated " + count + " mobs.");
                }
            }
            catch (Exception ex)
            {
                if (Log != null)
                {
                    Log.LogError("Nuke error: " + ex.Message);
                }
            }
        }

        public static void FreezeAllMobs(bool freeze)
        {
            try
            {
                MobBehaviour[] mobs = GameObject.FindObjectsOfType<MobBehaviour>();
                for (int i = 0; i < mobs.Length; i++)
                {
                    MobBehaviour mob = mobs[i];
                    try
                    {
                        if (mob != null)
                        {
                            mob.BlockMovement(freeze);
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                if (Log != null)
                {
                    Log.LogError("Freeze error: " + ex.Message);
                }
            }
        }

        public static void VacuumAllCollectibles()
        {
            try
            {
                GameObject playerObj = null;
                if (LocalCharacter != null)
                {
                    playerObj = LocalCharacter.gameObject;
                }
                else
                {
                    Character c = GameObject.FindObjectOfType<Character>();
                    if (c != null) playerObj = c.gameObject;
                }

                if (playerObj != null)
                {
                    DynamicAttributes attr = playerObj.GetComponent<DynamicAttributes>();
                    CollectibleBase[] collectibles = GameObject.FindObjectsOfType<CollectibleBase>();
                    int count = 0;
                    for (int i = 0; i < collectibles.Length; i++)
                    {
                        CollectibleBase col = collectibles[i];
                        try
                        {
                            if (col != null)
                            {
                                col.MoveToPlayer(attr);
                                count++;
                            }
                        }
                        catch { }
                    }
                    if (Log != null)
                    {
                        Log.LogInfo("Vacuum pulled " + count + " collectibles to player.");
                    }
                }
            }
            catch (Exception ex)
            {
                if (Log != null)
                {
                    Log.LogError("Vacuum error: " + ex.Message);
                }
            }
        }

        private IEnumerator OpenChestsSequentially()
        {
            isOpeningChests = true;
            statusMessage = "Opening chests sequentially...";

            CaptureChestBase[] chests = GameObject.FindObjectsOfType<CaptureChestBase>();
            int count = 0;

            for (int i = 0; i < chests.Length; i++)
            {
                CaptureChestBase chest = chests[i];
                if (chest != null && chest.gameObject != null && chest.gameObject.activeInHierarchy)
                {
                    try
                    {
                        if (rpcOpenMethod == null)
                        {
                            rpcOpenMethod = typeof(CaptureChestBase).GetMethod("Rpc_Open", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        }

                        if (rpcOpenMethod != null)
                        {
                            rpcOpenMethod.Invoke(chest, null);
                            count++;
                            statusMessage = string.Format("Opened chest {0}/{1}...", count, chests.Length);
                        }
                    }
                    catch (Exception ex)
                    {
                        if (Log != null) Log.LogWarning("Chest open error: " + ex.Message);
                    }

                    // Wait 1.5 seconds for chest animation to finish so overlays do not stack
                    yield return new WaitForSeconds(1.5f);
                }
            }

            statusMessage = string.Format("Sequential open complete: claimed {0} chests.", count);
            isOpeningChests = false;
        }

        // How the zoom is applied depends on the vcam's Body component (set in a prefab, so
        // detected at runtime). Moving the camera back keeps the FOV and scales the visible
        // area linearly; FOV is only a fallback because it distorts and must be capped.
        private enum ZoomMode { Fov, FramingDistance, FollowOffset, ThirdPersonDistance }
        private static ZoomMode zoomMode = ZoomMode.Fov;
        private static CinemachineComponentBase zoomBody = null;
        private static float baseDistance, lastDistance, baseFarClip;
        private static Vector3 baseOffset, lastOffset;

        private static CinemachineVirtualCamera FindGameplayVcam()
        {
            // The player camera is the vcam on the game's GameCamera; other vcams (trailer,
            // menus) may also exist.
            GameCamera gc = GameObject.FindObjectOfType<GameCamera>();
            if (gc != null)
            {
                CinemachineVirtualCamera v = gc.GetComponent<CinemachineVirtualCamera>();
                if (v != null) return v;
            }
            return GameObject.FindObjectOfType<CinemachineVirtualCamera>();
        }

        private static void CaptureZoomBase(CinemachineVirtualCamera vcam)
        {
            baseOrthoSize = vcam.m_Lens.OrthographicSize > 0f ? vcam.m_Lens.OrthographicSize : 12f;
            baseFov = vcam.m_Lens.FieldOfView > 0f ? vcam.m_Lens.FieldOfView : 60f;
            baseFarClip = vcam.m_Lens.FarClipPlane;
            zoomBody = vcam.GetCinemachineComponent(CinemachineCore.Stage.Body);

            CinemachineFramingTransposer framing = zoomBody as CinemachineFramingTransposer;
            CinemachineTransposer transposer = zoomBody as CinemachineTransposer;
            Cinemachine3rdPersonFollow thirdPerson = zoomBody as Cinemachine3rdPersonFollow;
            string detail;
            if (framing != null)
            {
                zoomMode = ZoomMode.FramingDistance;
                baseDistance = lastDistance = framing.m_CameraDistance;
                detail = "distance=" + baseDistance;
            }
            else if (transposer != null)
            {
                zoomMode = ZoomMode.FollowOffset;
                baseOffset = lastOffset = transposer.m_FollowOffset;
                detail = "followOffset=" + baseOffset + " (|" + baseOffset.magnitude.ToString("F1") + "|)";
            }
            else if (thirdPerson != null)
            {
                zoomMode = ZoomMode.ThirdPersonDistance;
                baseDistance = lastDistance = thirdPerson.CameraDistance;
                detail = "distance=" + baseDistance;
            }
            else
            {
                zoomMode = ZoomMode.Fov;
                detail = "no distance control, using FOV";
            }

            if (Log != null)
            {
                Log.LogInfo(string.Format("Camera zoom: vcam '{0}', body={1}, {2}, FOV={3}, ortho={4}, far={5}",
                    vcam.name, zoomBody != null ? zoomBody.GetType().Name : "none", detail,
                    baseFov, vcam.m_Lens.Orthographic, baseFarClip));
            }
        }

        private static void RestoreCameraZoom()
        {
            try
            {
                if (cachedVcam == null || !lensModified) return;
                switch (zoomMode)
                {
                    case ZoomMode.FramingDistance: ((CinemachineFramingTransposer)zoomBody).m_CameraDistance = baseDistance; break;
                    case ZoomMode.FollowOffset: ((CinemachineTransposer)zoomBody).m_FollowOffset = baseOffset; break;
                    case ZoomMode.ThirdPersonDistance: ((Cinemachine3rdPersonFollow)zoomBody).CameraDistance = baseDistance; break;
                    default:
                        cachedVcam.m_Lens.OrthographicSize = baseOrthoSize;
                        cachedVcam.m_Lens.FieldOfView = baseFov;
                        break;
                }
                cachedVcam.m_Lens.FarClipPlane = baseFarClip;
            }
            catch { }
        }

        public static void ApplyCameraZoom()
        {
            try
            {
                if (cachedVcam == null)
                {
                    // Nothing to do at default zoom; otherwise rescan at most once per second
                    // (FindObjectOfType every frame is expensive in menus with no vcam).
                    if (CameraZoomMultiplier == 1.0f && !lensModified) return;
                    if (Time.unscaledTime < nextVcamScanTime) return;
                    nextVcamScanTime = Time.unscaledTime + 1.0f;
                    cachedVcam = FindGameplayVcam();
                    if (cachedVcam == null) return;
                    CaptureZoomBase(cachedVcam);
                }

                float m = CameraZoomMultiplier;
                if (m == 1.0f && !lensModified) return;

                // If the game changed the distance/offset itself since our last write, adopt
                // its value as the new base instead of fighting it.
                switch (zoomMode)
                {
                    case ZoomMode.FramingDistance:
                    {
                        CinemachineFramingTransposer ft = (CinemachineFramingTransposer)zoomBody;
                        if (lensModified && Mathf.Abs(ft.m_CameraDistance - lastDistance) > 0.01f) baseDistance = ft.m_CameraDistance;
                        lastDistance = baseDistance * m;
                        ft.m_CameraDistance = lastDistance;
                        if (ft.m_MaximumDistance < lastDistance) ft.m_MaximumDistance = lastDistance;
                        break;
                    }
                    case ZoomMode.FollowOffset:
                    {
                        CinemachineTransposer t = (CinemachineTransposer)zoomBody;
                        if (lensModified && (t.m_FollowOffset - lastOffset).sqrMagnitude > 0.0001f) baseOffset = t.m_FollowOffset;
                        lastOffset = baseOffset * m; // same viewing angle, further away
                        t.m_FollowOffset = lastOffset;
                        break;
                    }
                    case ZoomMode.ThirdPersonDistance:
                    {
                        Cinemachine3rdPersonFollow tp = (Cinemachine3rdPersonFollow)zoomBody;
                        if (lensModified && Mathf.Abs(tp.CameraDistance - lastDistance) > 0.01f) baseDistance = tp.CameraDistance;
                        lastDistance = baseDistance * m;
                        tp.CameraDistance = lastDistance;
                        break;
                    }
                    default:
                    {
                        if (cachedVcam.m_Lens.Orthographic)
                        {
                            cachedVcam.m_Lens.OrthographicSize = baseOrthoSize * m;
                        }
                        else
                        {
                            // Visible width scales with tan(FOV/2), not FOV itself.
                            float half = baseFov * 0.5f * Mathf.Deg2Rad;
                            float fov = 2f * Mathf.Atan(Mathf.Tan(half) * m) * Mathf.Rad2Deg;
                            cachedVcam.m_Lens.FieldOfView = Mathf.Clamp(fov, 10f, 110f);
                        }
                        break;
                    }
                }

                // Moving the camera back must not push the ground past the far clip plane.
                if (zoomMode != ZoomMode.Fov)
                {
                    cachedVcam.m_Lens.FarClipPlane = baseFarClip * Mathf.Max(1f, m);
                }

                lensModified = (m != 1.0f);
            }
            catch (Exception ex)
            {
                if (Log != null)
                {
                    Log.LogError("Camera zoom error: " + ex.Message);
                }
            }
        }

        private void SkipRunTime(float secondsDelta)
        {
            try
            {
                int currentSeconds = 0;
                if (Map.Instance != null)
                {
                    currentSeconds = Map.Instance.GameTimerSeconds;
                }

                float targetTime = currentSeconds + secondsDelta;

                if (Map.Instance != null)
                {
                    Map.Instance.SetFakeTime((int)targetTime);
                }
                if (EventsSpawner.Instance != null)
                {
                    EventsSpawner.Instance.MoveToTimeSlot(targetTime);
                }
                if (WavesSpawner.Instance != null)
                {
                    WavesSpawner.Instance.MoveToTimeSlot(targetTime);
                }

                int min = (int)(targetTime / 60f);
                int sec = (int)(targetTime % 60f);
                statusMessage = string.Format("Advanced run time by +{0}s (Now at {1:D2}:{2:D2}).", secondsDelta, min, sec);
            }
            catch (Exception ex)
            {
                statusMessage = "Time skip failed: " + ex.Message;
            }
        }

        private void AddScrolls(int amount)
        {
            try
            {
                if (LibraryManager.Instance != null)
                {
                    LibraryManager.Instance.ReceiveScrolls(amount);
                    statusMessage = "Added " + amount + " Scrolls successfully!";
                }
                else
                {
                    statusMessage = "LibraryManager not ready. Please enter game first.";
                }
            }
            catch (Exception ex)
            {
                statusMessage = "Failed to add scrolls: " + ex.Message;
            }
        }

        private void InstantLevelUp(int count)
        {
            try
            {
                Level level = GameObject.FindObjectOfType<Level>();
                if (level != null)
                {
                    for (int i = 0; i < count; i++)
                    {
                        level.DebugLevelUp();
                    }
                    statusMessage = "Leveled up +" + count + " time(s)!";
                }
                else
                {
                    statusMessage = "Player Level controller not found in scene.";
                }
            }
            catch (Exception ex)
            {
                statusMessage = "Level up error: " + ex.Message;
            }
        }

        private void UnlockAllWeapons()
        {
            try
            {
                if (PlayerProgressionManager.Instance != null)
                {
                    PlayerProgressionManager.Instance.UnlockAllWeaponsAndAbilities();
                    statusMessage = "All weapons, abilities & evolutions unlocked!";
                }
                else
                {
                    statusMessage = "PlayerProgressionManager not ready.";
                }
            }
            catch (Exception ex)
            {
                statusMessage = "Unlock failed: " + ex.Message;
            }
        }

        private void CompleteChapters()
        {
            try
            {
                if (ChaptersManager.Instance != null)
                {
                    ChaptersManager.Instance.Cheat_CompleteAllChapters();
                    statusMessage = "All campaign chapters cleared!";
                }
                else
                {
                    statusMessage = "ChaptersManager not ready.";
                }
            }
            catch (Exception ex)
            {
                statusMessage = "Chapter clear failed: " + ex.Message;
            }
        }

        private void CompleteAssignments()
        {
            try
            {
                if (AssignmentsManager.Instance != null)
                {
                    AssignmentsManager.Instance.Cheat_CompleteCurrentAssignments();
                    statusMessage = "All bounty assignments completed!";
                }
                else
                {
                    statusMessage = "AssignmentsManager not ready.";
                }
            }
            catch (Exception ex)
            {
                statusMessage = "Assignment complete failed: " + ex.Message;
            }
        }
    }

    // ==========================================
    // HARMONY HOOKS
    // ==========================================

    [HarmonyPatch(typeof(DynamicAttributes), "ApplyDamage")]
    public static class Patch_DynamicAttributes_ApplyDamage
    {
        static bool Prefix(
            DynamicAttributes __instance,
            ref float damage,
            ref ICanTakeDamage.DamageDataPackage __result)
        {
            if (__instance == null) return true;

            if (__instance.GetIsPlayer())
            {
                if (TrainerPlugin.GodMode)
                {
                    damage = 0f;
                    __result = default(ICanTakeDamage.DamageDataPackage);
                    return false;
                }
            }
            else
            {
                if (TrainerPlugin.OneHitKill)
                {
                    damage = 999999f;
                }
                else if (TrainerPlugin.DamageMultiplier > 1.0f)
                {
                    damage *= TrainerPlugin.DamageMultiplier;
                }
            }
            return true;
        }
    }

    /// <summary>
    /// Rapid Fire and Attack Speed. The game computes
    ///   cooldown = stats.cooldown * attributes.GetNegativeValue(AttackSpeed), floored at minCooldown
    /// so the attack speed attribute is never read through GetCurrentValue; this is the only
    /// place to change it. Enemy abilities (A_Mino_Charge, A_Elite_Projectile) share this
    /// method, so only abilities owned by the player are changed. Never below 0.05s (20Hz):
    /// faster saturates the Wwise audio queue.
    /// </summary>
    [HarmonyPatch(typeof(GameplayAbilityBase), "GetAbilityCooldown")]
    public static class Patch_GameplayAbilityBase_GetAbilityCooldown
    {
        private const float MinCooldown = 0.05f;
        private static readonly MethodInfo getDynamicAttributes =
            AccessTools.PropertyGetter(typeof(GameplayAbilityBase), "dynamicAttributes");

        static void Postfix(GameplayAbilityBase __instance, ref float __result)
        {
            if (!TrainerPlugin.RapidFire && TrainerPlugin.AttackSpeedMultiplier <= 1.0f) return;
            try
            {
                DynamicAttributes owner = getDynamicAttributes != null
                    ? getDynamicAttributes.Invoke(__instance, null) as DynamicAttributes
                    : null;
                if (owner == null || !owner.GetIsPlayer()) return;
            }
            catch
            {
                return;
            }

            if (TrainerPlugin.RapidFire)
            {
                __result = MinCooldown;
            }
            else
            {
                __result = Mathf.Max(__result / TrainerPlugin.AttackSpeedMultiplier, MinCooldown);
            }
        }
    }

    [HarmonyPatch(typeof(AttributeBase), "GetCurrentValue")]
    public static class Patch_AttributeBase_GetCurrentValue
    {
        static void Postfix(AttributeBase __instance, AttributeBase.AttributeType type, ref float __result)
        {
            if (__instance == null) return;

            DynamicAttributes da = __instance as DynamicAttributes;
            bool isPlayer = (da != null && da.GetIsPlayer());

            if (type == AttributeBase.AttributeType.MovementSpeed && isPlayer)
            {
                if (TrainerPlugin.SpeedMultiplier > 1.0f)
                {
                    __result *= TrainerPlugin.SpeedMultiplier;
                }
            }
            else if (type == AttributeBase.AttributeType.ProjectileQuantity && isPlayer)
            {
                if (TrainerPlugin.ExtraProjectiles > 0)
                {
                    __result += TrainerPlugin.ExtraProjectiles;
                }
            }
            else if (type == AttributeBase.AttributeType.PickupRange && isPlayer)
            {
                // Sane cap: 35f multiplier (~70 world units radius) to prevent PhysX broadphase explosion
                if (TrainerPlugin.SuperMagnet)
                {
                    __result = 35.0f;
                }
                else if (TrainerPlugin.PickupMultiplier > 1.0f)
                {
                    __result = Mathf.Min(__result * TrainerPlugin.PickupMultiplier, 35.0f);
                }
            }
            else if (type == AttributeBase.AttributeType.Luck && isPlayer)
            {
                if (TrainerPlugin.LuckMultiplier > 1.0f)
                {
                    __result *= TrainerPlugin.LuckMultiplier;
                }
            }
        }
    }

    [HarmonyPatch(typeof(CollectibleBase), "GetPickupDistance")]
    public static class Patch_CollectibleBase_GetPickupDistance
    {
        static void Postfix(ref float __result)
        {
            // Sane cap at ~70 world units; full-map pulls handled safely via VacuumAllCollectibles (vacuum hotkey)
            if (TrainerPlugin.SuperMagnet)
            {
                __result = 70.0f;
            }
            else if (TrainerPlugin.PickupMultiplier > 1.0f)
            {
                __result = Mathf.Min(__result * TrainerPlugin.PickupMultiplier, 70.0f);
            }
        }
    }

    [HarmonyPatch(typeof(Level), "AddExperience")]
    public static class Patch_Level_AddExperience
    {
        static void Prefix(ref float amount)
        {
            if (TrainerPlugin.ExpMultiplier > 1.0f)
            {
                amount *= TrainerPlugin.ExpMultiplier;
            }
        }
    }

    [HarmonyPatch(typeof(LibraryManager), "PurchaseIsAvailable")]
    public static class Patch_LibraryManager_PurchaseIsAvailable
    {
        static bool Prefix(ref bool __result)
        {
            if (TrainerPlugin.FreeLibraryUpgrades)
            {
                __result = true;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(LibraryManager), "GetCurrentCost")]
    public static class Patch_LibraryManager_GetCurrentCost
    {
        static bool Prefix(ref int __result)
        {
            if (TrainerPlugin.FreeLibraryUpgrades)
            {
                __result = 0;
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Everything that reads DebugMenuEnabled (DebugMenuView.Start, trailer events, the lobby
    /// debug panel) does so once, in its own Awake/Start; DebugMenuView deactivates itself when
    /// it reads false. Forcing true from game start makes those objects keep the menu. In co-op
    /// it reports false so newly created debug objects stay off.
    /// </summary>
    [HarmonyPatch(typeof(DebugModulesEnabler), "get_DebugMenuEnabled")]
    public static class Patch_DebugModulesEnabler_DebugMenuEnabled
    {
        static void Postfix(ref bool __result)
        {
            if (TrainerPlugin.EnableNativeDebugMenu && !TrainerPlugin.IsCoopSession())
            {
                __result = true;
            }
        }
    }

    /// <summary>
    /// DebugModulesEnabler.Awake runs at game start: it sets DebugMenuEnabled from
    /// enableDebugMenuInReleaseCandidate and instantiates only the module prefabs flagged
    /// releaseCandidate. When the developer menu is enabled, also instantiate the modules meant
    /// for internal builds (editor-only modules are skipped: they may rely on editor APIs).
    /// Always logs the module list, which documents what the shipped build contains.
    /// </summary>
    [HarmonyPatch(typeof(DebugModulesEnabler), "Awake")]
    public static class Patch_DebugModulesEnabler_Awake
    {
        static void Prefix(DebugModulesEnabler __instance)
        {
            try
            {
                Traverse t = Traverse.Create(__instance);
                IList modules = t.Field("modules").GetValue() as IList;
                bool enable = TrainerPlugin.EnableNativeDebugMenu;
                if (enable) t.Field("enableDebugMenuInReleaseCandidate").SetValue(true);

                if (modules == null) return;
                for (int i = 0; i < modules.Count; i++)
                {
                    object entry = modules[i]; // boxed struct copy
                    Traverse e = Traverse.Create(entry);
                    bool editor = e.Field("editor").GetValue<bool>();
                    bool internalBuild = e.Field("internal_").GetValue<bool>();
                    bool rc = e.Field("releaseCandidate").GetValue<bool>();
                    GameObject prefab = e.Field("prefab").GetValue<GameObject>();
                    if (TrainerPlugin.Log != null)
                    {
                        TrainerPlugin.Log.LogInfo(string.Format("[DebugMenu] Module '{0}': editor={1}, internal={2}, release={3}",
                            prefab != null ? prefab.name : "null", editor, internalBuild, rc));
                    }
                    if (enable && internalBuild && !rc)
                    {
                        e.Field("releaseCandidate").SetValue(true);
                        modules[i] = entry; // write the modified struct back
                    }
                }
            }
            catch (Exception ex)
            {
                if (TrainerPlugin.Log != null) TrainerPlugin.Log.LogWarning("[DebugMenu] Awake prefix failed: " + ex.Message);
            }
        }

        static void Postfix()
        {
            if (TrainerPlugin.Log != null)
            {
                TrainerPlugin.Log.LogInfo("[DebugMenu] DebugModulesEnabler ready. Developer menu " +
                    (TrainerPlugin.EnableNativeDebugMenu ? "ENABLED (config)." : "disabled (enable it in the Trainer's Progression tab)."));
            }
        }
    }

    /// <summary>
    /// Spawner Freeze: Pure pause by skipping spawner ticks; does NOT reset wave or event indices.
    /// </summary>
    [HarmonyPatch(typeof(EventsSpawner), "UnPausedFixedUpdateNetwork")]
    public static class Patch_EventsSpawner_UnPausedFixedUpdateNetwork
    {
        static bool Prefix()
        {
            if (TrainerPlugin.FreezeWaves)
            {
                return false; // Skip spawn tick while frozen
            }
            return true;
        }
    }

    /// <summary>
    /// Spawner Freeze: Pure pause for WavesSpawner ticks.
    /// </summary>
    [HarmonyPatch(typeof(WavesSpawner), "UnPausedFixedUpdateNetwork")]
    public static class Patch_WavesSpawner_UnPausedFixedUpdateNetwork
    {
        static bool Prefix()
        {
            if (TrainerPlugin.FreezeWaves)
            {
                return false; // Skip spawn tick while frozen
            }
            return true;
        }
    }

    /// <summary>
    /// Infinite Native Level-Up Rerolls: Keeps rerollRemaining at 99 so native reroll button stays active.
    /// </summary>
    [HarmonyPatch(typeof(UpgradeModalView), "UpdateRerollButton")]
    public static class Patch_UpgradeModalView_UpdateRerollButton
    {
        static void Prefix(UpgradeModalView __instance)
        {
            if (TrainerPlugin.InfiniteRerolls && __instance != null)
            {
                Inventory inv = Traverse.Create(__instance).Field("_inventory").GetValue<Inventory>();
                if (inv != null && inv.rerollRemaining < 99)
                {
                    inv.rerollRemaining = 99;
                }
            }
        }
    }

    /// <summary>
    /// Infinite Native Level-Up Rerolls: Preserves 99 rerolls when reroll button is clicked.
    /// </summary>
    [HarmonyPatch(typeof(UpgradeModalView), "OnReroll")]
    public static class Patch_UpgradeModalView_OnReroll
    {
        static void Prefix(UpgradeModalView __instance)
        {
            if (TrainerPlugin.InfiniteRerolls && __instance != null)
            {
                Inventory inv = Traverse.Create(__instance).Field("_inventory").GetValue<Inventory>();
                if (inv != null)
                {
                    inv.rerollRemaining = 99;
                }
            }
        }
    }

    /// <summary>
    /// Banish Implementation: Filters banished abilities out of level-up choices.
    /// </summary>
    [HarmonyPatch(typeof(UpgradeController), "GetChoices")]
    public static class Patch_UpgradeController_GetChoices
    {
        static void Postfix(UpgradeController __instance, bool isReroll, ref List<Inventory.AbilityType> __result)
        {
            if (__result == null || TrainerPlugin.BanishedAbilities.Count == 0) return;

            List<Inventory.AbilityType> filtered = new List<Inventory.AbilityType>();
            for (int i = 0; i < __result.Count; i++)
            {
                if (!TrainerPlugin.BanishedAbilities.Contains(__result[i]))
                {
                    filtered.Add(__result[i]);
                }
            }

            // Only override if filtered list still contains valid choices
            if (filtered.Count > 0)
            {
                __result = filtered;
            }
        }
    }
}
