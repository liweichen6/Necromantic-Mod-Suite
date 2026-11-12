using System;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using Blinkmoon.GameSystem;
using Blinkmoon.GameSystem.BadFusion;
using Blinkmoon.GameSystem.UI;
using NecromanticCommon;

namespace NecromanticEndless
{
    [BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
    public class EndlessPlugin : BaseUnityPlugin
    {
        public const string PLUGIN_GUID = "com.necromantic.endless";
        public const string PLUGIN_NAME = "Necromantic Endless & Nightmare Mode";
        public const string PLUGIN_VERSION = "1.0.0";

        internal static ManualLogSource Log;
        private Harmony harmony;

        // Configuration Entries
        public static ConfigEntry<bool> EndlessModeEnabled;
        public static ConfigEntry<bool> NightmareModeEnabled;
        public static ConfigEntry<float> HealthScalePercentPerMinute;
        public static ConfigEntry<float> DamageScalePercentPerMinute;
        public static ConfigEntry<float> NightmareBaseMultiplier;
        public static ConfigEntry<KeyCode> StatusHotkey;
        public static ConfigEntry<bool> ShowHudLine;

        // GUI and HUD State
        private bool showStatusWindow = false;
        private Rect windowRect = new Rect(40f, 40f, 420f, 320f);
        private GUIStyle hudStyle;
        private GUIStyle shadowStyle;

        // Reflection Caches for High-Performance Ticking
        internal static FieldInfo field_Book_isFinalChest;
        internal static FieldInfo field_Map_fakeTime;
        internal static FieldInfo field_Waves_waves;
        internal static FieldInfo field_Waves_waveIndex;
        internal static FieldInfo field_Waves_additionalWaveIndex;
        internal static FieldInfo field_Waves_maxWaves;
        internal static FieldInfo field_Waves_spawnDelay;
        internal static FieldInfo field_Waves_onWaveChanged;
        internal static MethodInfo method_Waves_StartNextWave;
        internal static MethodInfo method_Waves_GetWaveIndex;

        internal static FieldInfo field_Events_events;
        internal static FieldInfo field_Events_subevents;
        internal static FieldInfo field_Events_eventIndex;
        internal static FieldInfo field_Events_subtierIndex;
        internal static MethodInfo method_Events_get_spawnCooldown;
        internal static MethodInfo method_Events_get_spawnSubtierCooldown;
        internal static MethodInfo method_Events_StartNextDelay;
        internal static MethodInfo method_Events_StartNextSubtierDelay;

        internal static FieldInfo field_Mob_attributes;

        private void Awake()
        {
            Log = Logger;
            ModHost.Protect(this, Log);
            Log.LogInfo(PLUGIN_NAME + " v" + PLUGIN_VERSION + " initializing...");

            // Bind configuration settings
            EndlessModeEnabled = Config.Bind<bool>(
                "General",
                "EndlessModeEnabled",
                false,
                "Enable Endless Mode: prevents forced level end upon timer completion or final boss defeat, continuing indefinitely."
            );

            NightmareModeEnabled = Config.Bind<bool>(
                "General",
                "NightmareModeEnabled",
                false,
                "Enable Nightmare Mode: applies base difficulty multiplier and aggressive scaling."
            );

            HealthScalePercentPerMinute = Config.Bind<float>(
                "Scaling",
                "HealthScalePercentPerMinute",
                15.0f,
                "Percentage mob and elite/miniboss health increases per extra minute past normal level duration."
            );

            DamageScalePercentPerMinute = Config.Bind<float>(
                "Scaling",
                "DamageScalePercentPerMinute",
                10.0f,
                "Percentage mob and elite/miniboss damage increases per extra minute past normal level duration."
            );

            NightmareBaseMultiplier = Config.Bind<float>(
                "Nightmare",
                "NightmareBaseMultiplier",
                1.5f,
                "Base health and damage multiplier applied when Nightmare Mode is enabled."
            );

            StatusHotkey = Config.Bind<KeyCode>(
                "UI",
                "StatusHotkey",
                KeyCode.F10,
                "Keyboard shortcut to toggle Endless Mode status and config window."
            );

            ShowHudLine = Config.Bind<bool>(
                "UI",
                "ShowHudLine",
                true,
                "Show small HUD line 'ENDLESS +Nm, mobs xN.NN' on screen while active."
            );

            // Initialize Reflection Cache
            InitReflection();

            // Apply Harmony patches
            try
            {
                harmony = new Harmony(PLUGIN_GUID);
                harmony.PatchAll(Assembly.GetExecutingAssembly());
                Log.LogInfo("Endless & Nightmare Mode Harmony patches applied successfully!");
            }
            catch (Exception ex)
            {
                Log.LogError("Failed to apply Harmony patches: " + ex);
            }
        }

        private static void InitReflection()
        {
            try
            {
                field_Book_isFinalChest = AccessTools.Field(typeof(BookObtainModalView), "isFinalChest");
                field_Map_fakeTime = AccessTools.Field(typeof(Map), "fakeTime");

                field_Waves_waves = AccessTools.Field(typeof(WavesSpawner), "waves");
                field_Waves_waveIndex = AccessTools.Field(typeof(WavesSpawner), "waveIndex");
                field_Waves_additionalWaveIndex = AccessTools.Field(typeof(WavesSpawner), "additionalWaveIndex");
                field_Waves_maxWaves = AccessTools.Field(typeof(WavesSpawner), "maxWaves");
                field_Waves_spawnDelay = AccessTools.Field(typeof(WavesSpawner), "__spawnDelay");
                field_Waves_onWaveChanged = AccessTools.Field(typeof(WavesSpawner), "onWaveChanged");
                method_Waves_StartNextWave = AccessTools.Method(typeof(WavesSpawner), "StartNextWave");
                method_Waves_GetWaveIndex = AccessTools.Method(typeof(WavesSpawner), "GetWaveIndex");

                field_Events_events = AccessTools.Field(typeof(EventsSpawner), "events");
                field_Events_subevents = AccessTools.Field(typeof(EventsSpawner), "subevents");
                field_Events_eventIndex = AccessTools.Field(typeof(EventsSpawner), "eventIndex");
                field_Events_subtierIndex = AccessTools.Field(typeof(EventsSpawner), "subtierIndex");
                method_Events_get_spawnCooldown = AccessTools.Method(typeof(EventsSpawner), "get__spawnCooldown");
                method_Events_get_spawnSubtierCooldown = AccessTools.Method(typeof(EventsSpawner), "get__spawnSubtierCooldown");
                method_Events_StartNextDelay = AccessTools.Method(typeof(EventsSpawner), "StartNextDelay");
                method_Events_StartNextSubtierDelay = AccessTools.Method(typeof(EventsSpawner), "StartNextSubtierDelay");

                field_Mob_attributes = AccessTools.Field(typeof(MobBehaviour), "attributes");
            }
            catch (Exception ex)
            {
                Log.LogError("Error initializing reflection cache: " + ex);
            }
        }

        private void Update()
        {
            if (HotkeyHelper.IsKeyDown(StatusHotkey.Value, Log, "Toggle Endless Menu"))
            {
                showStatusWindow = !showStatusWindow;
                CanvasOverlay.Notify("Endless / Nightmare Menu: " + (showStatusWindow ? "OPEN [" + StatusHotkey.Value + "]" : "CLOSED"), 3.0f);
            }

            if (showStatusWindow)
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }
        }

        private void OnGUI()
        {
            GUI.depth = -100;

            bool isEndless = EndlessModeEnabled.Value;
            bool isNightmare = NightmareModeEnabled.Value;

            // Draw HUD indicator when in an active game map and at least one mode is active
            if ((isEndless || isNightmare) && ShowHudLine.Value && Map.Instance != null)
            {
                DrawEndlessHud();
            }

            // Draw status window if toggled (allows enabling/disabling modes via F10)
            if (showStatusWindow)
            {
                windowRect = GUI.Window(987654, windowRect, DrawStatusWindow, "Endless / Nightmare Mode Status (" + StatusHotkey.Value + ")");
            }
        }

        private void DrawEndlessHud()
        {
            int normalDuration = GetNormalDuration();
            int currentSeconds = Map.Instance.GameTimerSeconds;
            int extraSeconds = currentSeconds - normalDuration;
            int extraMinutes = extraSeconds > 0 ? (extraSeconds / 60) : 0;

            float healthMult = Mathf.Pow(1.0f + (HealthScalePercentPerMinute.Value / 100.0f), (float)extraMinutes);
            if (NightmareModeEnabled.Value)
            {
                healthMult *= NightmareBaseMultiplier.Value;
            }

            if (hudStyle == null)
            {
                hudStyle = new GUIStyle(GUI.skin.label);
                hudStyle.fontSize = 15;
                hudStyle.fontStyle = FontStyle.Bold;
                hudStyle.alignment = TextAnchor.MiddleCenter;
                hudStyle.normal.textColor = new Color(1.0f, 0.45f, 0.45f, 1.0f);

                shadowStyle = new GUIStyle(hudStyle);
                shadowStyle.normal.textColor = Color.black;
            }

            string modePrefix = NightmareModeEnabled.Value ? "NIGHTMARE" : "ENDLESS";
            string text = string.Format("{0} +{1}m, mobs x{2:0.00}", modePrefix, extraMinutes, healthMult);

            float w = 340f;
            float h = 26f;
            float x = (Screen.width - w) / 2f;
            float y = 10f;

            // Text shadow for crisp visibility
            GUI.Label(new Rect(x + 1f, y + 1f, w, h), text, shadowStyle);
            GUI.Label(new Rect(x, y, w, h), text, hudStyle);
        }

        private void DrawStatusWindow(int windowId)
        {
            GUILayout.BeginVertical();

            int normalDuration = GetNormalDuration();
            int currentSeconds = (Map.Instance != null) ? Map.Instance.GameTimerSeconds : 0;
            int extraSeconds = currentSeconds - normalDuration;
            int extraMinutes = extraSeconds > 0 ? (extraSeconds / 60) : 0;

            float healthMult = Mathf.Pow(1.0f + (HealthScalePercentPerMinute.Value / 100.0f), (float)extraMinutes);
            float damageMult = Mathf.Pow(1.0f + (DamageScalePercentPerMinute.Value / 100.0f), (float)extraMinutes);

            if (NightmareModeEnabled.Value)
            {
                healthMult *= NightmareBaseMultiplier.Value;
                damageMult *= NightmareBaseMultiplier.Value;
            }

            GUILayout.Label("<b>--- Mode Toggles ---</b>");
            bool newEndless = GUILayout.Toggle(EndlessModeEnabled.Value, " Endless Mode Enabled");
            if (newEndless != EndlessModeEnabled.Value)
            {
                EndlessModeEnabled.Value = newEndless;
            }

            bool newNightmare = GUILayout.Toggle(NightmareModeEnabled.Value, " Nightmare Mode Enabled (Base " + NightmareBaseMultiplier.Value.ToString("0.0") + "x)");
            if (newNightmare != NightmareModeEnabled.Value)
            {
                NightmareModeEnabled.Value = newNightmare;
            }

            bool newShowHud = GUILayout.Toggle(ShowHudLine.Value, " Show On-Screen HUD Line");
            if (newShowHud != ShowHudLine.Value)
            {
                ShowHudLine.Value = newShowHud;
            }

            GUILayout.Space(8f);
            GUILayout.Label("<b>--- Match & Scaling Status ---</b>");

            int curMin = currentSeconds / 60;
            int curSec = currentSeconds % 60;
            int normMin = normalDuration / 60;
            int normSec = normalDuration % 60;

            GUILayout.Label(string.Format("Match Time: {0:D2}:{1:D2} (Normal Cap: {2:D2}:{3:D2})", curMin, curSec, normMin, normSec));
            GUILayout.Label(string.Format("Extra Time: +{0}m {1}s (+{2} minutes)", (extraSeconds > 0 ? extraSeconds / 60 : 0), (extraSeconds > 0 ? extraSeconds % 60 : 0), extraMinutes));
            GUILayout.Label(string.Format("Mob Health Multiplier: x{0:0.00} (+{1:0.0}%/min)", healthMult, HealthScalePercentPerMinute.Value));
            GUILayout.Label(string.Format("Mob Damage Multiplier: x{0:0.00} (+{1:0.0}%/min)", damageMult, DamageScalePercentPerMinute.Value));

            // Reflection reads are guarded: an exception inside a GUI.Window callback
            // would leave IMGUI's layout groups unbalanced and break the window.
            string waveLine = null;
            string eventLine = null;
            try
            {
                if (WavesSpawner.Instance != null && method_Waves_GetWaveIndex != null && field_Waves_waves != null)
                {
                    int waveIdx = (int)method_Waves_GetWaveIndex.Invoke(WavesSpawner.Instance, null);
                    IList waveList = field_Waves_waves.GetValue(WavesSpawner.Instance) as IList;
                    int waveTotal = (waveList != null) ? waveList.Count : 0;
                    waveLine = string.Format("Waves Spawner: Wave {0} / {1}", waveIdx + 1, waveTotal);
                }
                if (EventsSpawner.Instance != null && field_Events_eventIndex != null && field_Events_events != null)
                {
                    int evIdx = (int)field_Events_eventIndex.GetValue(EventsSpawner.Instance);
                    IList evList = field_Events_events.GetValue(EventsSpawner.Instance) as IList;
                    int evTotal = (evList != null) ? evList.Count : 0;
                    eventLine = string.Format("Events Spawner: Event {0} / {1}", evIdx, evTotal);
                }
            }
            catch { }
            if (waveLine != null) GUILayout.Label(waveLine);
            if (eventLine != null) GUILayout.Label(eventLine);

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close (" + StatusHotkey.Value + ")"))
            {
                showStatusWindow = false;
            }

            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        public static int GetNormalDuration()
        {
            int normalDuration = 900; // 15 minutes default
            try
            {
                if (AppService.Instance != null && AppService.Instance.Session != null)
                {
                    int dur = AppService.Instance.Session.RealDuration;
                    if (dur > 0)
                    {
                        normalDuration = dur;
                    }
                }
            }
            catch
            {
                // Fallback to default
            }
            return normalDuration;
        }

        public static bool IsEndlessActive()
        {
            return EndlessModeEnabled.Value || NightmareModeEnabled.Value;
        }
    }

    // =========================================================================
    // Harmony Patches
    // =========================================================================

    /// <summary>
    /// Prevents victory transition when opening the final boss chest, awarding rewards cleanly and unpausing.
    /// </summary>
    [HarmonyPatch(typeof(BookObtainModalView), "CloseModal")]
    public static class Patch_BookObtainModalView_CloseModal
    {
        [HarmonyPrefix]
        public static void Prefix(BookObtainModalView __instance)
        {
            if (!EndlessPlugin.IsEndlessActive())
            {
                return;
            }

            try
            {
                if (__instance != null && EndlessPlugin.field_Book_isFinalChest != null)
                {
                    bool isFinal = (bool)EndlessPlugin.field_Book_isFinalChest.GetValue(__instance);
                    if (isFinal)
                    {
                        EndlessPlugin.Log.LogInfo("Endless Mode: Final boss chest closed. Awarding win progression and continuing run...");

                        // Award win credit in progression so the player is credited with winning the level
                        if (PlayerProgressionManager.Instance != null)
                        {
                            PlayerProgressionManager.Instance.UpdateGameplayData(0, 0, 1, 0, 0, 0, null, null);
                        }

                        // Reset isFinalChest to false so CloseModal executes the non-ending branch (unpauses game)
                        EndlessPlugin.field_Book_isFinalChest.SetValue(__instance, false);
                    }
                }
            }
            catch (Exception ex)
            {
                EndlessPlugin.Log.LogError("Exception in BookObtainModalView.CloseModal prefix: " + ex);
            }
        }
    }

    /// <summary>
    /// Intercepts any call to StartGameEndFlow(win = true) to prevent forced victory screen in Endless Mode.
    /// Win = false (death / defeat) is allowed through so the player can always end the run normally.
    /// </summary>
    [HarmonyPatch(typeof(GameEndManager), "StartGameEndFlow")]
    public static class Patch_GameEndManager_StartGameEndFlow
    {
        [HarmonyPrefix]
        public static bool Prefix(bool win)
        {
            if (!EndlessPlugin.IsEndlessActive())
            {
                return true;
            }

            // If it's a win, suppress the forced game-over sequence so the player can keep playing
            if (win)
            {
                EndlessPlugin.Log.LogInfo("Endless Mode: StartGameEndFlow(win=true) suppressed; continuing run.");
                return false;
            }

            // Player died: allow normal defeat flow
            EndlessPlugin.Log.LogInfo("Endless Mode: StartGameEndFlow(win=false) allowed; ending run naturally via death.");
            return true;
        }
    }

    /// <summary>
    /// Extends match timer when session countdown expires so GameTimerSeconds continues counting upward.
    /// </summary>
    [HarmonyPatch(typeof(Map), "FixedUpdateNetwork")]
    public static class Patch_Map_FixedUpdateNetwork
    {
        [HarmonyPostfix]
        public static void Postfix(Map __instance)
        {
            if (!EndlessPlugin.IsEndlessActive())
            {
                return;
            }

            try
            {
                if (__instance == null || EndlessPlugin.field_Map_fakeTime == null) return;

                Session session = null;
                if (AppService.Instance != null)
                {
                    session = AppService.Instance.Session;
                }

                if (session == null) return;

                // When the session game timer has expired, advance fakeTime by RealDuration and provide a fresh timer chunk
                if (session.GameTimer.Expired(session, false, ""))
                {
                    int realDuration = session.RealDuration;
                    if (realDuration > 0)
                    {
                        int currentFake = (int)EndlessPlugin.field_Map_fakeTime.GetValue(__instance);
                        EndlessPlugin.field_Map_fakeTime.SetValue(__instance, currentFake + realDuration);

                        session.GameTimer = PausableTickTimer.CreateFromSeconds(session, (float)realDuration, false, "");
                        EndlessPlugin.Log.LogInfo("Endless Mode: Extended session timer chunk by " + realDuration + "s (fakeTime=" + (currentFake + realDuration) + ").");
                    }
                }
            }
            catch (Exception ex)
            {
                EndlessPlugin.Log.LogError("Exception in Map.FixedUpdateNetwork postfix: " + ex);
            }
        }
    }

    /// <summary>
    /// Loops late-game waves when WavesSpawner completes its final wave.
    /// </summary>
    [HarmonyPatch(typeof(WavesSpawner), "UnPausedFixedUpdateNetwork")]
    public static class Patch_WavesSpawner_UnPausedFixedUpdateNetwork
    {
        [HarmonyPostfix]
        public static void Postfix(WavesSpawner __instance)
        {
            if (!EndlessPlugin.IsEndlessActive())
            {
                return;
            }

            try
            {
                if (__instance == null || __instance.Object == null || !__instance.Object.HasStateAuthority)
                {
                    return;
                }

                if (EndlessPlugin.field_Waves_waves == null || EndlessPlugin.method_Waves_GetWaveIndex == null || EndlessPlugin.field_Waves_spawnDelay == null)
                {
                    return;
                }

                IList waves = EndlessPlugin.field_Waves_waves.GetValue(__instance) as IList;
                if (waves == null || waves.Count == 0)
                {
                    return;
                }

                // Keep maxWaves unlimited so the spawner doesn't stop
                if (EndlessPlugin.field_Waves_maxWaves != null)
                {
                    EndlessPlugin.field_Waves_maxWaves.SetValue(__instance, int.MaxValue);
                }

                int currentWaveIndex = (int)EndlessPlugin.method_Waves_GetWaveIndex.Invoke(__instance, null);
                PausableTickTimer spawnDelay = (PausableTickTimer)EndlessPlugin.field_Waves_spawnDelay.GetValue(__instance);

                // Check if current wave is at or past the final wave and spawn timer is not running
                if (currentWaveIndex >= waves.Count - 1 && !spawnDelay.IsRunning(__instance, false, ""))
                {
                    // Loop back to late-game waves (last 3 waves, or wave 0 if fewer)
                    int lateStartWave = Mathf.Max(0, waves.Count - 3);
                    int addWaveIndex = (EndlessPlugin.field_Waves_additionalWaveIndex != null) ? (int)EndlessPlugin.field_Waves_additionalWaveIndex.GetValue(__instance) : 0;
                    int newWaveIndex = lateStartWave - addWaveIndex;
                    if (newWaveIndex < 0)
                    {
                        newWaveIndex = 0;
                    }

                    EndlessPlugin.field_Waves_waveIndex.SetValue(__instance, newWaveIndex);

                    // Invoke onWaveChanged if subscribed
                    if (EndlessPlugin.field_Waves_onWaveChanged != null)
                    {
                        Action<int> onWaveChanged = EndlessPlugin.field_Waves_onWaveChanged.GetValue(__instance) as Action<int>;
                        if (onWaveChanged != null)
                        {
                            int updatedWaveIndex = (int)EndlessPlugin.method_Waves_GetWaveIndex.Invoke(__instance, null);
                            onWaveChanged.Invoke(updatedWaveIndex);
                        }
                    }

                    EndlessPlugin.Log.LogInfo("Endless Mode: Looping late-game waves. Restarting at wave index " + newWaveIndex);
                    EndlessPlugin.method_Waves_StartNextWave.Invoke(__instance, null);
                }
            }
            catch (Exception ex)
            {
                EndlessPlugin.Log.LogError("Exception in WavesSpawner.UnPausedFixedUpdateNetwork postfix: " + ex);
            }
        }
    }

    /// <summary>
    /// Loops late-game events and subevents when EventsSpawner completes all tiers.
    /// </summary>
    [HarmonyPatch(typeof(EventsSpawner), "UnPausedFixedUpdateNetwork")]
    public static class Patch_EventsSpawner_UnPausedFixedUpdateNetwork
    {
        [HarmonyPostfix]
        public static void Postfix(EventsSpawner __instance)
        {
            if (!EndlessPlugin.IsEndlessActive())
            {
                return;
            }

            try
            {
                if (__instance == null || __instance.Object == null || !__instance.Object.HasStateAuthority)
                {
                    return;
                }

                // 1. Loop main tier events (miniboss / boss tier events)
                if (EndlessPlugin.field_Events_events != null && EndlessPlugin.field_Events_eventIndex != null && EndlessPlugin.method_Events_get_spawnCooldown != null)
                {
                    IList eventsList = EndlessPlugin.field_Events_events.GetValue(__instance) as IList;
                    int eventIndex = (int)EndlessPlugin.field_Events_eventIndex.GetValue(__instance);
                    PausableTickTimer spawnCooldown = (PausableTickTimer)EndlessPlugin.method_Events_get_spawnCooldown.Invoke(__instance, null);

                    if (eventsList != null && eventsList.Count > 0)
                    {
                        if (eventIndex >= eventsList.Count && !spawnCooldown.IsRunning(__instance, false, ""))
                        {
                            // Cycle back to late-game tier events (e.g. last 3 events)
                            int lateEventIndex = Mathf.Max(0, eventsList.Count - 3);
                            EndlessPlugin.field_Events_eventIndex.SetValue(__instance, lateEventIndex);
                            EndlessPlugin.method_Events_StartNextDelay.Invoke(__instance, null);
                            EndlessPlugin.Log.LogInfo("Endless Mode: Looping late-game tier events. Resetting eventIndex to " + lateEventIndex);
                        }
                    }
                }

                // 2. Loop subevents (subtier mob swarms)
                if (EndlessPlugin.field_Events_subevents != null && EndlessPlugin.field_Events_subtierIndex != null && EndlessPlugin.method_Events_get_spawnSubtierCooldown != null)
                {
                    IList subeventsList = EndlessPlugin.field_Events_subevents.GetValue(__instance) as IList;
                    int subtierIndex = (int)EndlessPlugin.field_Events_subtierIndex.GetValue(__instance);
                    PausableTickTimer spawnSubtierCooldown = (PausableTickTimer)EndlessPlugin.method_Events_get_spawnSubtierCooldown.Invoke(__instance, null);

                    if (subeventsList != null && subeventsList.Count > 0)
                    {
                        if (subtierIndex >= subeventsList.Count && !spawnSubtierCooldown.IsRunning(__instance, false, ""))
                        {
                            int lateSubtierIndex = Mathf.Max(0, subeventsList.Count - 4);
                            EndlessPlugin.field_Events_subtierIndex.SetValue(__instance, lateSubtierIndex);
                            EndlessPlugin.method_Events_StartNextSubtierDelay.Invoke(__instance, null);
                            EndlessPlugin.Log.LogInfo("Endless Mode: Looping late-game subevents. Resetting subtierIndex to " + lateSubtierIndex);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                EndlessPlugin.Log.LogError("Exception in EventsSpawner.UnPausedFixedUpdateNetwork postfix: " + ex);
            }
        }
    }

    /// <summary>
    /// Scales mob, elite, miniboss, and boss health and damage by configurable percentages per extra minute.
    /// </summary>
    [HarmonyPatch(typeof(MobBehaviour), "ModifyAttributesByType")]
    public static class Patch_MobBehaviour_ModifyAttributesByType
    {
        /// <summary>
        /// Scales an override's effect by <paramref name="mult"/>. Flat overrides scale their
        /// value directly; percent overrides (+P%) become ((1 + P/100) * mult - 1) * 100 so the
        /// resulting stat is multiplied by exactly <paramref name="mult"/>.
        /// </summary>
        private static AttributeBase.AttributeOverride ScaleOverride(AttributeBase.AttributeOverride ov, float mult)
        {
            float value = ov.percent
                ? ((1.0f + ov.baseValue / 100.0f) * mult - 1.0f) * 100.0f
                : ov.baseValue * mult;
            return new AttributeBase.AttributeOverride(ov.type, value, ov.percent);
        }

        [HarmonyPostfix]
        public static void Postfix(MobBehaviour __instance)
        {
            if (!EndlessPlugin.IsEndlessActive())
            {
                return;
            }

            try
            {
                if (__instance == null || EndlessPlugin.field_Mob_attributes == null)
                {
                    return;
                }

                DynamicAttributes attributes = EndlessPlugin.field_Mob_attributes.GetValue(__instance) as DynamicAttributes;
                if (attributes == null)
                {
                    return;
                }

                int normalDuration = EndlessPlugin.GetNormalDuration();
                int currentSeconds = (Map.Instance != null) ? Map.Instance.GameTimerSeconds : 0;
                int extraSeconds = currentSeconds - normalDuration;
                int extraMinutes = extraSeconds > 0 ? (extraSeconds / 60) : 0;

                float healthMult = Mathf.Pow(1.0f + (EndlessPlugin.HealthScalePercentPerMinute.Value / 100.0f), (float)extraMinutes);
                float damageMult = Mathf.Pow(1.0f + (EndlessPlugin.DamageScalePercentPerMinute.Value / 100.0f), (float)extraMinutes);

                if (EndlessPlugin.NightmareModeEnabled.Value)
                {
                    healthMult *= EndlessPlugin.NightmareBaseMultiplier.Value;
                    damageMult *= EndlessPlugin.NightmareBaseMultiplier.Value;
                }

                // If no scaling needed, exit
                if (healthMult <= 1.001f && damageMult <= 1.001f)
                {
                    return;
                }

                var overrides = attributes.attributesOverrides;
                if (overrides == null)
                {
                    overrides = new List<AttributeBase.AttributeOverride>();
                    attributes.attributesOverrides = overrides;
                }

                // Scale Health:
                // For MobType.Mob, vanilla has already applied levelInfo.mobHealthModifier ^ minutes.
                // We multiply that existing baseValue by healthMult.
                // For Elites, Minibosses, and Bosses, vanilla returned early and did not modify health;
                // so we scale their baseline health by healthMult.
                bool healthFound = false;
                for (int i = 0; i < overrides.Count; i++)
                {
                    AttributeBase.AttributeOverride ov = overrides[i];
                    if (ov.type == AttributeBase.AttributeType.Health)
                    {
                        healthFound = true;
                        overrides[i] = ScaleOverride(ov, healthMult);
                    }
                }

                if (!healthFound && healthMult > 1.001f)
                {
                    // If no explicit health override existed, apply percent modifier
                    float pct = (healthMult - 1.0f) * 100.0f;
                    overrides.Add(new AttributeBase.AttributeOverride(
                        AttributeBase.AttributeType.Health,
                        pct,
                        true
                    ));
                }

                // Scale Damage:
                bool damageFound = false;
                for (int i = 0; i < overrides.Count; i++)
                {
                    AttributeBase.AttributeOverride ov = overrides[i];
                    if (ov.type == AttributeBase.AttributeType.Damage)
                    {
                        damageFound = true;
                        overrides[i] = ScaleOverride(ov, damageMult);
                    }
                }

                if (!damageFound && damageMult > 1.001f)
                {
                    // Add percent damage bonus to scale baseline damage
                    float pct = (damageMult - 1.0f) * 100.0f;
                    overrides.Add(new AttributeBase.AttributeOverride(
                        AttributeBase.AttributeType.Damage,
                        pct,
                        true
                    ));
                }
            }
            catch (Exception ex)
            {
                EndlessPlugin.Log.LogError("Exception in MobBehaviour.ModifyAttributesByType postfix: " + ex);
            }
        }
    }
}
