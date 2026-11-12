using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using NecromanticCommon;
using Blinkmoon.GameSystem;
using Blinkmoon.GameSystem.GameState;
using Blinkmoon.GameSystem.BadFusion;
using Fusion;

namespace NecromanticQoL
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class QoLPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.necromantic.qol";
        public const string PluginName = "Necromantic QoL";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log;
        internal static QoLPlugin Instance;

        // BepInEx Configuration Entries
        public static ConfigEntry<bool> EnableHud;
        public static ConfigEntry<KeyCode> ToggleHudKey;
        public static ConfigEntry<KeyCode> RestartKey;
        public static ConfigEntry<bool> RequireCtrlForRestart;
        public static ConfigEntry<bool> SkipIntro;
        public static ConfigEntry<float> DpsWindowSeconds;
        public static ConfigEntry<float> WindowX;
        public static ConfigEntry<float> WindowY;

        // HUD Window & Layout State
        private Rect _windowRect;
        private Vector2 _scrollPos = Vector2.zero;
        private bool _stylesInitialized = false;
        private GUIStyle _windowStyle;
        private GUIStyle _headerLabelStyle;
        private GUIStyle _statLabelStyle;
        private GUIStyle _abilityNameStyle;
        private GUIStyle _abilityValueStyle;
        private GUIStyle _footerStyle;
        private Texture2D _windowBgTex;

        private static bool _startupBannerShown = false;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            ModHost.Protect(this, Log);

            // Bind Configuration
            EnableHud = Config.Bind<bool>("HUD", "EnableHud", true, "Show live DPS and damage meter HUD overlay.");
            ToggleHudKey = Config.Bind<KeyCode>("HUD", "ToggleHudKey", KeyCode.F9, "Hotkey to toggle HUD visibility.");
            DpsWindowSeconds = Config.Bind<float>("HUD", "DpsWindowSeconds", 5.0f, "Rolling window duration in seconds for live DPS calculation.");
            WindowX = Config.Bind<float>("HUD", "WindowX", 10.0f, "Initial X position of HUD window.");
            WindowY = Config.Bind<float>("HUD", "WindowY", 10.0f, "Initial Y position of HUD window.");

            RestartKey = Config.Bind<KeyCode>("Hotkeys", "RestartKey", KeyCode.R, "Key to restart the current run.");
            RequireCtrlForRestart = Config.Bind<bool>("Hotkeys", "RequireCtrlForRestart", true, "Require Ctrl modifier with RestartKey (Ctrl+R).");

            SkipIntro = Config.Bind<bool>("General", "SkipIntro", true, "Skip studio logo and intro splash screens on game startup.");

            _windowRect = new Rect(WindowX.Value, WindowY.Value, 290f, 320f);

            // Minimap lives on the same (protected) host object as the plugin.
            Minimap.BindConfig(Config);
            gameObject.AddComponent<Minimap>();

            // Apply Harmony Patches
            try
            {
                Harmony harmony = new Harmony(PluginGuid);
                harmony.PatchAll(typeof(QoLPlugin).Assembly);
                Log.LogInfo("Necromantic QoL v" + PluginVersion + " loaded and patches applied successfully.");
            }
            catch (Exception ex)
            {
                Log.LogError("Failed to apply Harmony patches: " + ex.ToString());
            }
        }

        private void Update()
        {
            // Initial startup notification
            if (!_startupBannerShown && Time.unscaledTime > 0.5f)
            {
                _startupBannerShown = true;
                // Keys come from the live config so rebinds show up here.
                ShowNotification(string.Format("Necromantic Mod Suite Active  |  [{0}] Trainer  [{1}] HUD  [{2}] Endless  [{3}] Save",
                    HotkeyRegistry.KeyText("com.necromantic.trainer", "ToggleMenuKey", "Insert"),
                    HotkeyRegistry.KeyText(PluginGuid, "ToggleHudKey", "F9"),
                    HotkeyRegistry.KeyText("com.necromantic.endless", "StatusHotkey", "F10"),
                    HotkeyRegistry.KeyText("com.necromantic.save", "ToggleKey", "F11")), 9.0f);
            }

            // Toggle HUD key check via HotkeyHelper
            if (HotkeyHelper.IsKeyDown(ToggleHudKey.Value, Log, "Toggle DPS Meter HUD"))
            {
                EnableHud.Value = !EnableHud.Value;
                Log.LogInfo("HUD visibility toggled: " + EnableHud.Value.ToString());
                ShowNotification("DPS Meter HUD: " + (EnableHud.Value ? "ENABLED" : "DISABLED"), 3.0f);
            }

            // Instant restart hotkey check (Ctrl+R by default via HotkeyHelper)
            bool triggerRestart = RequireCtrlForRestart.Value
                ? (HotkeyHelper.IsComboDown(RestartKey.Value, KeyCode.LeftControl, Log, "Instant Restart") || HotkeyHelper.IsComboDown(RestartKey.Value, KeyCode.RightControl, Log, "Instant Restart"))
                : HotkeyHelper.IsKeyDown(RestartKey.Value, Log, "Instant Restart");
            if (triggerRestart)
            {
                PerformInstantRestart();
            }

            // Prune expired DPS window samples periodically in Update
            DamageMeter.PruneExpiredSamples(DpsWindowSeconds.Value);
        }

        private void OnGUI()
        {
            GUI.depth = -100;
            InitStyles();

            // Render DPS & Damage meter HUD if enabled
            if (EnableHud.Value)
            {
                _windowRect = GUI.Window(8899, _windowRect, DrawMeterWindow, "Combat & DPS Meter", _windowStyle);
            }
        }

        private void InitStyles()
        {
            if (_stylesInitialized) return;

            // Semi-transparent dark background texture for window
            _windowBgTex = new Texture2D(1, 1);
            _windowBgTex.SetPixel(0, 0, new Color(0.08f, 0.08f, 0.12f, 0.90f));
            _windowBgTex.Apply();

            _windowStyle = new GUIStyle(GUI.skin.window);
            _windowStyle.normal.background = _windowBgTex;
            _windowStyle.onNormal.background = _windowBgTex;
            _windowStyle.fontStyle = FontStyle.Bold;
            _windowStyle.fontSize = 12;

            _headerLabelStyle = new GUIStyle(GUI.skin.label);
            _headerLabelStyle.fontStyle = FontStyle.Bold;
            _headerLabelStyle.fontSize = 13;
            _headerLabelStyle.normal.textColor = new Color(0.95f, 0.85f, 0.40f);

            _statLabelStyle = new GUIStyle(GUI.skin.label);
            _statLabelStyle.fontSize = 12;
            _statLabelStyle.normal.textColor = Color.white;

            _abilityNameStyle = new GUIStyle(GUI.skin.label);
            _abilityNameStyle.fontSize = 11;
            _abilityNameStyle.normal.textColor = new Color(0.85f, 0.85f, 0.95f);

            _abilityValueStyle = new GUIStyle(GUI.skin.label);
            _abilityValueStyle.fontSize = 11;
            _abilityValueStyle.alignment = TextAnchor.MiddleRight;
            _abilityValueStyle.normal.textColor = new Color(0.40f, 0.95f, 0.60f);

            _footerStyle = new GUIStyle(GUI.skin.label);
            _footerStyle.fontSize = 10;
            _footerStyle.alignment = TextAnchor.MiddleCenter;
            _footerStyle.normal.textColor = new Color(0.70f, 0.70f, 0.70f);

            _stylesInitialized = true;
        }

        private void DrawMeterWindow(int windowId)
        {
            // Title bar draggable
            GUI.DragWindow(new Rect(0, 0, 10000, 22));

            GUILayout.BeginVertical();

            // Run stats header
            float dps = DamageMeter.GetCurrentDps(DpsWindowSeconds.Value);
            float totalDmg = DamageMeter.GetTotalDamage();
            int kills = DamageMeter.GetKillCount();

            string timeStr = "--:--";
            try
            {
                if (Map.Instance != null)
                {
                    int totalSec = Map.Instance.GameTimerSeconds;
                    if (totalSec >= 0)
                    {
                        int minutes = totalSec / 60;
                        int seconds = totalSec % 60;
                        timeStr = string.Format("{0:00}:{1:00}", minutes, seconds);
                    }
                }
            }
            catch { }

            // Summary grid
            GUILayout.BeginHorizontal();
            GUILayout.Label(string.Format("DPS: <color=#66FF66>{0:N1}</color>", dps), _statLabelStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label(string.Format("Time: <color=#FFFF66>{0}</color>", timeStr), _statLabelStyle);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(string.Format("Total Dmg: <color=#66FFFF>{0:N0}</color>", totalDmg), _statLabelStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label(string.Format("Kills: <color=#FF8866>{0:N0}</color>", kills), _statLabelStyle);
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.Label("── Damage by Ability ────────────────────", _footerStyle);

            // Per-ability damage list
            _scrollPos = GUILayout.BeginScrollView(_scrollPos, GUILayout.Height(170));
            List<KeyValuePair<Inventory.AbilityType, float>> sortedAbilities = DamageMeter.GetSortedTotals();

            if (sortedAbilities == null || sortedAbilities.Count == 0)
            {
                GUILayout.Space(20);
                GUILayout.Label("No damage recorded yet.", _footerStyle);
            }
            else
            {
                for (int i = 0; i < sortedAbilities.Count; i++)
                {
                    KeyValuePair<Inventory.AbilityType, float> entry = sortedAbilities[i];
                    float pct = totalDmg > 0.001f ? (entry.Value / totalDmg * 100f) : 0f;

                    GUILayout.BeginHorizontal();
                    GUILayout.Label(entry.Key.ToString(), _abilityNameStyle, GUILayout.Width(130));
                    GUILayout.Label(string.Format("{0:N0} ({1:0.0}%)", entry.Value, pct), _abilityValueStyle);
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.EndScrollView();

            GUILayout.Space(4);
            string restartLabel = (RequireCtrlForRestart.Value ? "Ctrl+" : "") + RestartKey.Value.ToString();
            GUILayout.Label(string.Format("[{0}] Toggle HUD  |  [{1}] Restart Run", ToggleHudKey.Value.ToString(), restartLabel), _footerStyle);

            GUILayout.EndVertical();
        }

        public static void ShowNotification(string message, float durationSeconds)
        {
            CanvasOverlay.Notify(message, durationSeconds);
        }

        private void PerformInstantRestart()
        {
            // Verify in active gameplay run or gameover
            bool inRun = false;
            try
            {
                if (GameStateManager.Instance != null && GameStateManager.Instance.CurState != null)
                {
                    GameState cur = GameStateManager.Instance.CurState;
                    if (cur is GameplayGameState || cur is GameoverState)
                    {
                        inRun = true;
                    }
                }
                if (PlayerManager.LocalPlayer != null)
                {
                    inRun = true;
                }
            }
            catch (Exception ex)
            {
                Log.LogWarning("Error checking run status: " + ex.Message);
            }

            if (!inRun)
            {
                ShowNotification("Instant restart only available during a match.", 2.5f);
                return;
            }

            // Check solo vs co-op: disable in co-op sessions with a visible message
            try
            {
                if (AppService.Instance != null && !AppService.Instance.IsSingleplayer)
                {
                    ShowNotification("Instant restart is disabled in co-op sessions!", 3.5f);
                    Log.LogWarning("Instant restart requested in co-op session - rejected.");
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.LogWarning("Error checking multiplayer status: " + ex.Message);
            }

            // Check GameEndManager existence
            if (GameEndManager.Instance == null)
            {
                ShowNotification("GameEndManager not available to restart.", 2.5f);
                return;
            }

            try
            {
                Log.LogInfo("Triggering instant run restart via GameEndManager.RestartGame()...");

                // Execute the game's official restart flow
                GameEndManager.Instance.RestartGame();

                // Reset match clock and fake time so run starts clean at 0:00
                if (Map.Instance != null)
                {
                    Map.Instance.SetFakeTime(0);
                    Map.Instance.GameTimerSeconds = 0;
                }

                // Reset session countdown timer
                if (AppService.Instance != null && AppService.Instance.Session != null)
                {
                    Session s = AppService.Instance.Session;
                    s.GameTimer = PausableTickTimer.CreateFromSeconds(s, (float)s.RealDuration, false, "");
                }

                // Dismiss game-over screen if it was active
                if (GameEndManager.Instance != null)
                {
                    GameEndManager.Instance.Revived();
                }

                // Reset DPS meter and kill stats
                DamageMeter.ResetRun("InstantRestart");

                ShowNotification("Run restarted!", 2.5f);
                Log.LogInfo("Instant run restart completed.");
            }
            catch (Exception ex)
            {
                Log.LogError("Exception during instant restart: " + ex.ToString());
                ShowNotification("Restart error: " + ex.Message, 3.0f);
            }
        }
    }

    /// <summary>
    /// Live damage and DPS measurement engine.
    /// Tracks rolling ~5s window DPS, ability damage totals, and kill count.
    /// Zero per-frame FindObjectsOfType cost.
    /// </summary>
    public static class DamageMeter
    {
        private struct DamageSample
        {
            public float Time;
            public float Damage;
        }

        private static readonly Queue<DamageSample> _recentDamage = new Queue<DamageSample>();
        private static float _rollingDamageSum = 0f;
        private static float _totalRunDamage = 0f;

        private static readonly Dictionary<Inventory.AbilityType, float> _abilityTotals = new Dictionary<Inventory.AbilityType, float>();
        private static readonly List<KeyValuePair<Inventory.AbilityType, float>> _sortedTotals = new List<KeyValuePair<Inventory.AbilityType, float>>();
        private static bool _totalsDirty = true;
        private static float _lastSortTime = 0f;

        private static int _runKills = 0;
        private static readonly HashSet<int> _killedMobInstanceIds = new HashSet<int>();

        public static void RecordDamage(Inventory.AbilityType ability, float actualDamage)
        {
            if (actualDamage <= 0.0001f) return;

            float now = Time.time;
            _recentDamage.Enqueue(new DamageSample { Time = now, Damage = actualDamage });
            _rollingDamageSum += actualDamage;
            _totalRunDamage += actualDamage;

            float cur;
            if (_abilityTotals.TryGetValue(ability, out cur))
            {
                _abilityTotals[ability] = cur + actualDamage;
            }
            else
            {
                _abilityTotals[ability] = actualDamage;
            }

            _totalsDirty = true;
        }

        public static bool RegisterKill(int mobInstanceId)
        {
            if (_killedMobInstanceIds.Add(mobInstanceId))
            {
                _runKills++;
                return true;
            }
            return false;
        }

        public static void OnMobKilledMessageEmitted()
        {
            // Secondary kill signal if any mob was defeated without direct ApplyDamage instance ID
            _runKills++;
        }

        public static void PruneExpiredSamples(float windowSeconds)
        {
            if (windowSeconds <= 0f) windowSeconds = 5.0f;
            float now = Time.time;
            while (_recentDamage.Count > 0 && (now - _recentDamage.Peek().Time) > windowSeconds)
            {
                DamageSample expired = _recentDamage.Dequeue();
                _rollingDamageSum -= expired.Damage;
            }
            if (_rollingDamageSum < 0f)
            {
                _rollingDamageSum = 0f;
            }
        }

        public static float GetCurrentDps(float windowSeconds)
        {
            PruneExpiredSamples(windowSeconds);
            if (windowSeconds <= 0f) windowSeconds = 5.0f;
            return _rollingDamageSum / windowSeconds;
        }

        public static float GetTotalDamage()
        {
            return _totalRunDamage;
        }

        public static int GetKillCount()
        {
            int count = _runKills;

            // Harmonize with player's native KillsCounter component if available
            try
            {
                if (PlayerManager.LocalPlayer != null)
                {
                    KillsCounter kc = PlayerManager.LocalPlayer.GetComponent<KillsCounter>();
                    if (kc != null)
                    {
                        int nativeKills = kc.GetKilled();
                        if (nativeKills > count)
                        {
                            count = nativeKills;
                            _runKills = nativeKills;
                        }
                    }
                }
            }
            catch { }

            return count;
        }

        public static List<KeyValuePair<Inventory.AbilityType, float>> GetSortedTotals()
        {
            float now = Time.time;
            if (_totalsDirty || (now - _lastSortTime) > 0.5f)
            {
                _sortedTotals.Clear();
                foreach (KeyValuePair<Inventory.AbilityType, float> kvp in _abilityTotals)
                {
                    _sortedTotals.Add(kvp);
                }
                _sortedTotals.Sort(CompareDamageDescending);
                _totalsDirty = false;
                _lastSortTime = now;
            }
            return _sortedTotals;
        }

        private static int CompareDamageDescending(KeyValuePair<Inventory.AbilityType, float> a, KeyValuePair<Inventory.AbilityType, float> b)
        {
            return b.Value.CompareTo(a.Value);
        }

        public static void ResetRun(string reason)
        {
            QoLPlugin.Log.LogInfo("Resetting DamageMeter run stats (reason: " + reason + ")");
            _recentDamage.Clear();
            _rollingDamageSum = 0f;
            _totalRunDamage = 0f;
            _abilityTotals.Clear();
            _sortedTotals.Clear();
            _killedMobInstanceIds.Clear();
            _runKills = 0;
            _totalsDirty = true;
            _lastSortTime = 0f;
        }
    }

    /// <summary>
    /// Harmony Patches for Necromantic QoL
    /// </summary>

    // 1. Skip Intro / Splash Screens Patch
    [HarmonyPatch(typeof(SplashScreenRunner), "Init")]
    public static class Patch_SplashScreenRunner_Init
    {
        [HarmonyPrefix]
        public static bool Prefix(SplashScreenRunner __instance)
        {
            try
            {
                if (!QoLPlugin.SkipIntro.Value)
                {
                    return true;
                }

                QoLPlugin.Log.LogInfo("Skipping intro splash screens via native OnSplashScreensCompleted callback.");

                Traverse.Create(__instance).Field("_initialized").SetValue(true);
                Traverse.Create(__instance).Field("_splashScreensRunning").SetValue(false);

                // Invoke the game's own splash completion callback registered by ServiceManager
                Action completedCallback = Traverse.Create(typeof(SplashScreenRunner)).Field("OnSplashScreensCompleted").GetValue<Action>();
                if (completedCallback != null)
                {
                    completedCallback.Invoke();
                }

                // Return false to skip showing "pBlinkMoonSplash" and launching RunSplashScreens coroutine
                return false;
            }
            catch (Exception ex)
            {
                QoLPlugin.Log.LogError("Exception in Patch_SplashScreenRunner_Init Prefix: " + ex.ToString());
                return true;
            }
        }
    }

    // 2. Real Damage & Fatal Kill Hook
    [HarmonyPatch(typeof(DynamicAttributes), "ApplyDamage")]
    public static class Patch_DynamicAttributes_ApplyDamage
    {
        private static readonly System.Reflection.FieldInfo FiZombieDecoy = typeof(DynamicAttributes).GetField("isZombieDecoy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        [HarmonyPostfix]
        public static void Postfix(
            DynamicAttributes __instance,
            ref ICanTakeDamage.DamageDataPackage __result,
            Vector3 location,
            float damage,
            PlayerRef attacker,
            GameObject causer,
            float knockback,
            DynamicAttributes.DamageType damageType,
            Inventory.AbilityType ability)
        {
            try
            {
                // Only process player damage dealt to enemy mobs
                if (__instance == null || __instance.GetIsPlayer())
                {
                    return;
                }

                if (FiZombieDecoy != null)
                {
                    object isDecoyObj = FiZombieDecoy.GetValue(__instance);
                    if (isDecoyObj is bool && (bool)isDecoyObj)
                    {
                        return;
                    }
                }

                Character localPlayer = PlayerManager.LocalPlayer;
                if (localPlayer == null)
                {
                    return;
                }

                // Validate that the local player was the attacker
                if (attacker.PlayerId != localPlayer.playerID)
                {
                    return;
                }

                // __result.damage contains the exact non-overkill damage applied to target health
                float actualDealt = __result.damage;
                if (actualDealt > 0f)
                {
                    DamageMeter.RecordDamage(ability, actualDealt);
                }

                // Register kill if this hit was fatal
                if (__result.killed)
                {
                    DamageMeter.RegisterKill(__instance.GetInstanceID());
                }
            }
            catch (Exception ex)
            {
                QoLPlugin.Log.LogError("Exception in Patch_DynamicAttributes_ApplyDamage Postfix: " + ex.ToString());
            }
        }
    }

    // 3. MessageSystem Hooks for Run Lifecycle & Kill Tracking
    [HarmonyPatch(typeof(MessageSystem), "EmitMessage")]
    public static class Patch_MessageSystem_EmitMessage
    {
        [HarmonyPostfix]
        public static void Postfix(MessageAction action, IMessageData data)
        {
            try
            {
                if (action == MessageAction.RunStarted)
                {
                    DamageMeter.ResetRun("MessageSystem.RunStarted");
                }
            }
            catch (Exception ex)
            {
                QoLPlugin.Log.LogError("Exception in Patch_MessageSystem_EmitMessage Postfix: " + ex.ToString());
            }
        }
    }

    // 4. GameEndManager RestartGame Hook to Reset Meter
    [HarmonyPatch(typeof(GameEndManager), "RestartGame")]
    public static class Patch_GameEndManager_RestartGame
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {
                DamageMeter.ResetRun("GameEndManager.RestartGame");
            }
            catch (Exception ex)
            {
                QoLPlugin.Log.LogError("Exception in Patch_GameEndManager_RestartGame Postfix: " + ex.ToString());
            }
        }
    }

    // 5. Map Spawned Hook to Reset Meter on Fresh Map Entry
    [HarmonyPatch(typeof(Map), "Spawned")]
    public static class Patch_Map_Spawned
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {
                DamageMeter.ResetRun("Map.Spawned");
            }
            catch (Exception ex)
            {
                QoLPlugin.Log.LogError("Exception in Patch_Map_Spawned Postfix: " + ex.ToString());
            }
        }
    }
}
