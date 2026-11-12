using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using UnityEngine;

namespace NecromanticCommon
{
    /// <summary>
    /// One rebindable hotkey: a KeyCode config entry of a Necromantic plugin.
    /// Setting <see cref="Key"/> writes the .cfg file immediately, and plugins read
    /// ConfigEntry.Value every frame, so a rebind takes effect at once.
    /// </summary>
    public class HotkeyBinding
    {
        public string PluginGuid;
        public string PluginName;
        public string Label;
        public ConfigEntryBase Entry;
        /// <summary>Optional bool entry that adds a Ctrl requirement (QoL restart).</summary>
        public ConfigEntryBase CtrlRequiredEntry;

        public KeyCode Key
        {
            get { return (KeyCode)Entry.BoxedValue; }
            set { Entry.BoxedValue = value; }
        }

        public KeyCode DefaultKey
        {
            get { return (KeyCode)Entry.DefaultValue; }
        }

        public string KeyText
        {
            get
            {
                bool ctrl = CtrlRequiredEntry != null && (bool)CtrlRequiredEntry.BoxedValue;
                return (ctrl ? "Ctrl+" : "") + HotkeyRegistry.KeyName(Key);
            }
        }
    }

    /// <summary>
    /// Discovers every KeyCode setting of every loaded "com.necromantic.*" plugin through
    /// BepInEx's plugin list, so a menu can show and rebind them without the plugins
    /// referencing each other.
    /// </summary>
    public static class HotkeyRegistry
    {
        public const string GuidPrefix = "com.necromantic.";

        private static List<HotkeyBinding> _cache;

        /// <summary>All Necromantic hotkeys, grouped by plugin in load order.</summary>
        public static List<HotkeyBinding> GetAll(bool refresh = false)
        {
            if (_cache != null && !refresh) return _cache;

            List<HotkeyBinding> result = new List<HotkeyBinding>();
            try
            {
                foreach (KeyValuePair<string, PluginInfo> kv in Chainloader.PluginInfos)
                {
                    PluginInfo info = kv.Value;
                    if (info == null || info.Instance == null) continue;
                    if (!kv.Key.StartsWith(GuidPrefix, StringComparison.OrdinalIgnoreCase)) continue;

                    ConfigFile cfg = info.Instance.Config;
                    foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> entry in cfg)
                    {
                        if (entry.Value.SettingType != typeof(KeyCode)) continue;

                        HotkeyBinding b = new HotkeyBinding();
                        b.PluginGuid = kv.Key;
                        b.PluginName = ShortPluginName(info.Metadata.Name);
                        b.Label = Humanize(entry.Key.Key);
                        b.Entry = entry.Value;
                        if (entry.Key.Key == "RestartKey")
                        {
                            foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> other in cfg)
                            {
                                if (other.Key.Section == entry.Key.Section && other.Key.Key == "RequireCtrlForRestart" &&
                                    other.Value.SettingType == typeof(bool))
                                {
                                    b.CtrlRequiredEntry = other.Value;
                                }
                            }
                        }
                        result.Add(b);
                    }
                }
            }
            catch (Exception)
            {
                // Plugin list not ready yet; try again next call.
                return result;
            }

            _cache = result;
            return result;
        }

        /// <summary>Current key text for one setting, or <paramref name="fallback"/> if not found.</summary>
        public static string KeyText(string guid, string settingKey, string fallback)
        {
            foreach (HotkeyBinding b in GetAll())
            {
                if (b.PluginGuid == guid && b.Entry.Definition.Key == settingKey) return b.KeyText;
            }
            return fallback;
        }

        /// <summary>The binding (other than <paramref name="except"/>) already using <paramref name="key"/>, if any.</summary>
        public static HotkeyBinding FindConflict(KeyCode key, HotkeyBinding except)
        {
            foreach (HotkeyBinding b in GetAll())
            {
                if (b != except && b.Key == key) return b;
            }
            return null;
        }

        public static string KeyName(KeyCode key)
        {
            if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9) return ((int)(key - KeyCode.Alpha0)).ToString();
            if (key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9) return "Num" + (int)(key - KeyCode.Keypad0);
            switch (key)
            {
                case KeyCode.None: return "(none)";
                case KeyCode.BackQuote: return "`";
                case KeyCode.PageUp: return "PgUp";
                case KeyCode.PageDown: return "PgDn";
                case KeyCode.Delete: return "Del";
                case KeyCode.Insert: return "Insert";
                default: return key.ToString();
            }
        }

        private static string ShortPluginName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "?";
            if (name.IndexOf("Trainer", StringComparison.OrdinalIgnoreCase) >= 0) return "Trainer";
            if (name.IndexOf("Save", StringComparison.OrdinalIgnoreCase) >= 0) return "Save";
            if (name.IndexOf("Endless", StringComparison.OrdinalIgnoreCase) >= 0) return "Endless";
            if (name.IndexOf("QoL", StringComparison.OrdinalIgnoreCase) >= 0) return "QoL";
            return name.Replace("Necromantic ", "");
        }

        /// <summary>"ToggleHudKey" -> "Toggle Hud", "Speed2x" -> "Speed 2x".</summary>
        private static string Humanize(string key)
        {
            if (key.EndsWith("Hotkey") && key.Length > 6) key = key.Substring(0, key.Length - 6);
            else if (key.EndsWith("Key") && key.Length > 3) key = key.Substring(0, key.Length - 3);
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < key.Length; i++)
            {
                char c = key[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(key[i - 1])) sb.Append(' ');
                else if (i > 0 && char.IsDigit(c) && !char.IsDigit(key[i - 1])) sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
