using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace NecromanticCommon
{
    /// <summary>
    /// Keeps the BepInEx manager GameObject (which hosts every plugin's Update/OnGUI) alive.
    ///
    /// On this game (Unity 2022.3) the BepInEx chainloader runs before the first scene is
    /// loaded. With BepInEx's default HideManagerGameObject = false, the "BepInEx_Manager"
    /// object is torn down during startup, so plugin Awake() runs but Start/Update/OnGUI
    /// never do: no hotkeys, no HUD, no windows. Marking the object HideAndDontSave (exactly
    /// what HideManagerGameObject = true does) keeps it out of scene teardown.
    ///
    /// Call <see cref="Protect"/> as the first line of every plugin's Awake().
    /// </summary>
    public static class ModHost
    {
        private static bool _watcherAdded;

        public static void Protect(BaseUnityPlugin plugin, ManualLogSource log)
        {
            if (plugin == null) return;

            GameObject host = plugin.gameObject;
            if ((host.hideFlags & HideFlags.HideAndDontSave) != HideFlags.HideAndDontSave)
            {
                host.hideFlags = HideFlags.HideAndDontSave;
                if (log != null)
                {
                    log.LogInfo("[ModHost] Marked '" + host.name + "' HideAndDontSave so plugin Update/OnGUI keep running.");
                }
            }

            if (!_watcherAdded)
            {
                _watcherAdded = true;
                HostWatcher watcher = host.AddComponent<HostWatcher>();
                watcher.Log = log;
            }
        }

        /// <summary>
        /// Diagnostic component: logs once when the update loop is confirmed running, and
        /// logs a loud error if the host object is ever destroyed.
        /// </summary>
        private class HostWatcher : MonoBehaviour
        {
            public ManualLogSource Log;
            private bool _quitting;

            private void Start()
            {
                if (Log != null) Log.LogInfo("[ModHost] Plugin update loop is running (hotkeys active).");
            }

            private void OnApplicationQuit()
            {
                _quitting = true;
            }

            private void OnDestroy()
            {
                if (!_quitting && Log != null)
                {
                    Log.LogError("[ModHost] BepInEx manager object was destroyed! Plugin hotkeys and windows will stop working. " +
                                 "Set [Chainloader] HideManagerGameObject = true in BepInEx/config/BepInEx.cfg.");
                }
            }
        }
    }
}
