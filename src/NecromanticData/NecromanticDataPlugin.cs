using System;
using System.IO;
using System.Text;
using System.Globalization;
using System.Collections;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using BansheeGz.BGDatabase;
using Blinkmoon.GameSystem;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NecromanticData
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class NecromanticDataPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.necromantic.data";
        public const string PluginName = "Necromantic Data Framework";
        public const string PluginVersion = "1.0.0";

        public static NecromanticDataPlugin Instance;
        public static ManualLogSource ModLogger;

        // Config entries
        public static ConfigEntry<bool> ExportOnStartup;
        public static ConfigEntry<bool> ForceReExport;
        public static ConfigEntry<bool> EnableOverrides;
        public static ConfigEntry<bool> LogDetailedChanges;

        public static ConfigEntry<bool> EnableWeaponBuffs;
        public static ConfigEntry<bool> EnableHardMode;
        public static ConfigEntry<bool> EnableFastWaves;

        // Paths
        private static string _configRoot;
        private static string _exportDir;
        private static string _overridesDir;
        private static string _presetsDir;

        // State tracking
        private static bool _overridesApplied = false;
        private static bool _exportDone = false;
        private static int _totalModifications = 0;

        private void Awake()
        {
            Instance = this;
            ModLogger = Logger;

            ModLogger.LogInfo(string.Format("{0} v{1} is loading...", PluginName, PluginVersion));

            // Initialize Configuration
            ExportOnStartup = Config.Bind<bool>("Framework", "ExportOnStartup", true,
                "Automatically export database tables to BepInEx/config/NecromanticData/export/ on launch if not already exported.");
            ForceReExport = Config.Bind<bool>("Framework", "ForceReExport", false,
                "Force re-exporting all database tables on every launch.");
            EnableOverrides = Config.Bind<bool>("Framework", "EnableOverrides", true,
                "Load and apply override JSON files from BepInEx/config/NecromanticData/overrides/.");
            LogDetailedChanges = Config.Bind<bool>("Framework", "LogDetailedChanges", true,
                "Log every modified database field value.");

            EnableWeaponBuffs = Config.Bind<bool>("Presets", "EnableWeaponBuffs", false,
                "Enable the 'weapon_buffs' balance preset (buffs underperforming starting weapons like Necromancer, Shadow Knight, and Ranger).");
            EnableHardMode = Config.Bind<bool>("Presets", "EnableHardMode", false,
                "Enable the 'hard_mode' preset (higher mob health, speed, damage, and tighter wave phases).");
            EnableFastWaves = Config.Bind<bool>("Presets", "EnableFastWaves", false,
                "Enable the 'fast_waves' preset (10s wave phases with +35% hero movement speed).");

            // Setup directories
            _configRoot = Path.Combine(Paths.ConfigPath, "NecromanticData");
            _exportDir = Path.Combine(_configRoot, "export");
            _overridesDir = Path.Combine(_configRoot, "overrides");
            _presetsDir = Path.Combine(_configRoot, "presets");

            EnsureDirectoryExists(_configRoot);
            EnsureDirectoryExists(_exportDir);
            EnsureDirectoryExists(_overridesDir);
            EnsureDirectoryExists(_presetsDir);

            // Deploy default presets and example override
            DeployDefaultPresets();
            DeployExampleOverride();

            // Hook BGRepo.OnLoad event
            BGRepo.OnLoad += OnRepoLoaded;

            // Apply Harmony patches (including DataTableHookup.InitService prefix)
            try
            {
                var harmony = new Harmony(PluginGuid);
                harmony.PatchAll(typeof(NecromanticDataPlugin).Assembly);
                ModLogger.LogInfo("Harmony patches registered successfully!");
            }
            catch (Exception ex)
            {
                ModLogger.LogError(string.Format("Failed to apply Harmony patches: {0}", ex));
            }

            // Attempt early loading of BGRepo in Awake
            try
            {
                if (!BGRepo.DefaultRepoLoaded)
                {
                    ModLogger.LogInfo("Triggering early BGRepo load in Awake...");
                    var repo = BGRepo.I;
                }
                else
                {
                    EnsureOverridesApplied("Awake (Repo already loaded)");
                }
            }
            catch (Exception ex)
            {
                ModLogger.LogWarning(string.Format("Early BGRepo load deferred to game startup: {0}", ex.Message));
            }
        }

        private static void OnRepoLoaded(bool success)
        {
            ModLogger.LogInfo(string.Format("BGRepo.OnLoad event received (success={0}).", success));
            if (!success)
            {
                ModLogger.LogWarning("BGRepo reported load failure; skipping data overrides.");
                return;
            }

            _overridesApplied = false; // Reset to allow re-applying on repo reloads
            EnsureOverridesApplied("BGRepo.OnLoad event");
        }

        public static void EnsureOverridesApplied(string triggerReason)
        {
            if (_overridesApplied)
            {
                return;
            }

            try
            {
                var repo = BGRepo.I;
                if (repo == null)
                {
                    ModLogger.LogWarning(string.Format("Cannot apply overrides ({0}): BGRepo.I is null.", triggerReason));
                    return;
                }

                ModLogger.LogInfo(string.Format("Applying NecromanticData overrides (Trigger: {0})...", triggerReason));

                // 1. Export tables if required
                // "If not already exported" means on disk, not just this session; otherwise all
                // tables get rewritten on every launch.
                if (!_exportDone && !ForceReExport.Value && Directory.Exists(_exportDir) &&
                    Directory.GetFiles(_exportDir, "*.json").Length > 0)
                {
                    _exportDone = true;
                    ModLogger.LogInfo("Table export already present; skipping (set ForceReExport = true to refresh).");
                }

                if (ExportOnStartup.Value && (!_exportDone || ForceReExport.Value))
                {
                    ExportAllTables(repo, _exportDir);
                    _exportDone = true;
                }

                // 2. Apply Presets and Overrides
                _totalModifications = 0;
                ApplyAllOverrides(repo);

                _overridesApplied = true;
                ModLogger.LogInfo(string.Format("NecromanticData overrides successfully applied! ({0} total field modifications)", _totalModifications));
            }
            catch (Exception ex)
            {
                ModLogger.LogError(string.Format("Error while applying NecromanticData overrides: {0}", ex));
            }
        }

        #region Exporting

        public static void ExportAllTables(BGRepo repo, string targetDir)
        {
            try
            {
                EnsureDirectoryExists(targetDir);
                ModLogger.LogInfo(string.Format("Exporting database tables to '{0}'...", targetDir));

                int exportedCount = 0;
                repo.ForEachMeta(delegate(BGMetaEntity meta)
                {
                    try
                    {
                        ExportSingleTable(meta, targetDir);
                        exportedCount++;
                    }
                    catch (Exception ex)
                    {
                        ModLogger.LogError(string.Format("Failed to export table '{0}': {1}", meta.Name, ex.Message));
                    }
                });

                ModLogger.LogInfo(string.Format("Exported {0} tables successfully to '{1}'.", exportedCount, targetDir));
            }
            catch (Exception ex)
            {
                ModLogger.LogError(string.Format("Error during table export: {0}", ex));
            }
        }

        private static void ExportSingleTable(BGMetaEntity meta, string targetDir)
        {
            var tableObj = new JObject();
            var usedKeys = new HashSet<string>();

            meta.ForEachEntity(delegate(BGEntity entity)
            {
                string rowKey = entity.Name;
                if (string.IsNullOrEmpty(rowKey))
                {
                    rowKey = entity.Id.ToString();
                }
                else if (usedKeys.Contains(rowKey))
                {
                    rowKey = string.Format("{0}_{1}", rowKey, entity.Id.ToString());
                }
                usedKeys.Add(rowKey);

                var rowObj = new JObject();
                // Add metadata properties for user reference
                rowObj["_Id"] = entity.Id.ToString();

                meta.ForEachField(delegate(BGField field)
                {
                    try
                    {
                        object val = field.GetValue(entity.Id);
                        rowObj[field.Name] = ConvertValueToJToken(val, field);
                    }
                    catch (Exception ex)
                    {
                        rowObj[field.Name] = string.Format("<Error reading field: {0}>", ex.Message);
                    }
                });

                tableObj[rowKey] = rowObj;
            });

            string filePath = Path.Combine(targetDir, meta.Name + ".json");
            File.WriteAllText(filePath, JsonConvert.SerializeObject(tableObj, Formatting.Indented), Encoding.UTF8);
        }

        private static JToken ConvertValueToJToken(object val, BGField field)
        {
            if (val == null)
            {
                return JValue.CreateNull();
            }

            if (val is Enum)
            {
                return new JValue(val.ToString());
            }

            if (val is List<string>)
            {
                var arr = new JArray();
                var list = (List<string>)val;
                for (int i = 0; i < list.Count; i++)
                {
                    arr.Add(new JValue(list[i]));
                }
                return arr;
            }

            if (val is List<float>)
            {
                var arr = new JArray();
                var list = (List<float>)val;
                for (int i = 0; i < list.Count; i++)
                {
                    arr.Add(new JValue(list[i]));
                }
                return arr;
            }

            if (val is float)
            {
                return new JValue((float)val);
            }

            if (val is int)
            {
                return new JValue((int)val);
            }

            if (val is bool)
            {
                return new JValue((bool)val);
            }

            if (val is string)
            {
                return new JValue((string)val);
            }

            // Fallback to string or object
            return new JValue(val.ToString());
        }

        #endregion

        #region Applying Overrides & Presets

        private static void ApplyAllOverrides(BGRepo repo)
        {
            // 1. Presets (applied in priority order)
            if (EnableWeaponBuffs.Value)
            {
                ApplyPreset("weapon_buffs.json", repo);
            }

            if (EnableHardMode.Value)
            {
                ApplyPreset("hard_mode.json", repo);
            }

            if (EnableFastWaves.Value)
            {
                ApplyPreset("fast_waves.json", repo);
            }

            // 2. Custom Overrides from overrides/*.json
            if (EnableOverrides.Value && Directory.Exists(_overridesDir))
            {
                string[] files = Directory.GetFiles(_overridesDir, "*.json");
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);

                for (int i = 0; i < files.Length; i++)
                {
                    string filePath = files[i];
                    string fileName = Path.GetFileName(filePath);
                    if (fileName.EndsWith(".example", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    try
                    {
                        string content = File.ReadAllText(filePath, Encoding.UTF8);
                        ApplyJsonData(fileName, content, repo);
                    }
                    catch (Exception ex)
                    {
                        ModLogger.LogError(string.Format("Failed to load override file '{0}': {1}", fileName, ex.Message));
                    }
                }
            }
        }

        private static void ApplyPreset(string presetFileName, BGRepo repo)
        {
            string path = Path.Combine(_presetsDir, presetFileName);
            if (!File.Exists(path))
            {
                ModLogger.LogWarning(string.Format("Preset file '{0}' not found at '{1}'.", presetFileName, path));
                return;
            }

            try
            {
                ModLogger.LogInfo(string.Format("Applying balance preset '{0}'...", presetFileName));
                string content = File.ReadAllText(path, Encoding.UTF8);
                ApplyJsonData("Preset:" + presetFileName, content, repo);
            }
            catch (Exception ex)
            {
                ModLogger.LogError(string.Format("Failed to apply preset '{0}': {1}", presetFileName, ex.Message));
            }
        }

        public static void ApplyJsonData(string sourceName, string jsonContent, BGRepo repo)
        {
            if (string.IsNullOrEmpty(jsonContent))
            {
                return;
            }

            JToken rootToken;
            try
            {
                rootToken = JToken.Parse(jsonContent);
            }
            catch (Exception ex)
            {
                ModLogger.LogError(string.Format("[{0}] JSON parsing failed: {1}", sourceName, ex.Message));
                return;
            }

            var rootObj = rootToken as JObject;
            if (rootObj == null)
            {
                ModLogger.LogWarning(string.Format("[{0}] Root JSON element must be an object. Skipping.", sourceName));
                return;
            }

            // Check if this is a Multi-Table JSON or Single-Table JSON
            bool isMultiTable = false;
            foreach (var prop in rootObj.Properties())
            {
                if (prop.Value is JObject && FindMeta(repo, prop.Name) != null)
                {
                    isMultiTable = true;
                    break;
                }
            }

            if (isMultiTable)
            {
                // Multi-Table: { "CORE_Mobs": { ... }, "CORE_AbilityEffects": { ... } }
                foreach (var prop in rootObj.Properties())
                {
                    string tableName = prop.Name;
                    var rowsObj = prop.Value as JObject;
                    if (rowsObj == null)
                    {
                        continue;
                    }

                    BGMetaEntity meta = FindMeta(repo, tableName);
                    if (meta == null)
                    {
                        ModLogger.LogWarning(string.Format("[{0}] Unknown table '{1}'. Skipping.", sourceName, tableName));
                        continue;
                    }

                    ApplyRowsToTable(meta, rowsObj, sourceName);
                }
            }
            else
            {
                // Single-Table: filename dictates table name, root keys are row names
                string inferredTableName = Path.GetFileNameWithoutExtension(sourceName);
                if (inferredTableName.StartsWith("Preset:"))
                {
                    inferredTableName = inferredTableName.Substring(7);
                }

                BGMetaEntity meta = FindMeta(repo, inferredTableName);
                if (meta == null)
                {
                    ModLogger.LogWarning(string.Format("[{0}] Could not map file name '{1}' to any known database table. Specify table names inside JSON or rename file to match table.", sourceName, inferredTableName));
                    return;
                }

                ApplyRowsToTable(meta, rootObj, sourceName);
            }
        }

        private static void ApplyRowsToTable(BGMetaEntity meta, JObject rowsObj, string sourceName)
        {
            foreach (var rowProp in rowsObj.Properties())
            {
                string rowKey = rowProp.Name;
                if (rowKey.StartsWith("_"))
                {
                    // Skip metadata/comment entries
                    continue;
                }

                var fieldsObj = rowProp.Value as JObject;
                if (fieldsObj == null)
                {
                    ModLogger.LogWarning(string.Format("[{0}] Row '{1}' in table '{2}' must be an object. Skipping.", sourceName, rowKey, meta.Name));
                    continue;
                }

                // Find entity by Name, ID, or fallback
                BGEntity entity = FindEntity(meta, rowKey);
                if (entity == null)
                {
                    ModLogger.LogWarning(string.Format("[{0}] Row '{1}' not found in table '{2}'. Skipping.", sourceName, rowKey, meta.Name));
                    continue;
                }

                // Apply fields
                foreach (var fieldProp in fieldsObj.Properties())
                {
                    string fieldName = fieldProp.Name;
                    if (fieldName.StartsWith("_"))
                    {
                        // Skip metadata fields like _Id
                        continue;
                    }

                    BGField field = FindField(meta, fieldName);
                    if (field == null)
                    {
                        ModLogger.LogWarning(string.Format("[{0}] Field '{1}' not found in table '{2}'. Skipping.", sourceName, fieldName, meta.Name));
                        continue;
                    }

                    try
                    {
                        object convertedVal = ConvertJTokenToType(fieldProp.Value, field.ValueType);
                        field.SetValue(entity.Id, convertedVal);
                        _totalModifications++;

                        if (LogDetailedChanges.Value)
                        {
                            ModLogger.LogInfo(string.Format("[{0}] {1}.{2}.{3} = {4}",
                                sourceName, meta.Name, entity.Name, field.Name, FormatValueForLog(convertedVal)));
                        }
                    }
                    catch (Exception ex)
                    {
                        ModLogger.LogWarning(string.Format("[{0}] Failed setting {1}.{2}.{3} to '{4}': {5}",
                            sourceName, meta.Name, entity.Name, fieldName, fieldProp.Value.ToString(), ex.Message));
                    }
                }
            }
        }

        #endregion

        #region Value Conversion & Lookups

        private static object ConvertJTokenToType(JToken token, Type targetType)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                if (targetType.IsValueType)
                {
                    return Activator.CreateInstance(targetType);
                }
                return null;
            }

            if (targetType == typeof(float))
            {
                if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
                {
                    return token.Value<float>();
                }
                return float.Parse(token.ToString(), CultureInfo.InvariantCulture);
            }

            if (targetType == typeof(int))
            {
                if (token.Type == JTokenType.Integer)
                {
                    return token.Value<int>();
                }
                return int.Parse(token.ToString(), CultureInfo.InvariantCulture);
            }

            if (targetType == typeof(bool))
            {
                if (token.Type == JTokenType.Boolean)
                {
                    return token.Value<bool>();
                }
                return bool.Parse(token.ToString());
            }

            if (targetType == typeof(string))
            {
                return token.ToString();
            }

            if (targetType.IsEnum)
            {
                if (token.Type == JTokenType.Integer)
                {
                    return Enum.ToObject(targetType, token.Value<int>());
                }
                return Enum.Parse(targetType, token.ToString(), true);
            }

            if (targetType == typeof(List<string>))
            {
                var list = new List<string>();
                var arr = token as JArray;
                if (arr != null)
                {
                    for (int i = 0; i < arr.Count; i++)
                    {
                        list.Add(arr[i].ToString());
                    }
                }
                else
                {
                    list.Add(token.ToString());
                }
                return list;
            }

            if (targetType == typeof(List<float>))
            {
                var list = new List<float>();
                var arr = token as JArray;
                if (arr != null)
                {
                    for (int i = 0; i < arr.Count; i++)
                    {
                        list.Add(float.Parse(arr[i].ToString(), CultureInfo.InvariantCulture));
                    }
                }
                else
                {
                    list.Add(float.Parse(token.ToString(), CultureInfo.InvariantCulture));
                }
                return list;
            }

            // General fallback
            return Convert.ChangeType(token.ToString(), targetType, CultureInfo.InvariantCulture);
        }

        private static string FormatValueForLog(object val)
        {
            if (val == null) return "null";
            var listStr = val as List<string>;
            if (listStr != null) return "[" + string.Join(", ", listStr.ToArray()) + "]";
            var listFlt = val as List<float>;
            if (listFlt != null) return "[" + string.Join(", ", listFlt.ConvertAll<string>(delegate(float f) { return f.ToString(CultureInfo.InvariantCulture); }).ToArray()) + "]";
            return val.ToString();
        }

        public static BGMetaEntity FindMeta(BGRepo repo, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            // Direct match
            BGMetaEntity meta = repo.GetMeta(name);
            if (meta != null) return meta;

            // Strip "DT_" if present
            if (name.StartsWith("DT_", StringComparison.OrdinalIgnoreCase))
            {
                meta = repo.GetMeta(name.Substring(3));
                if (meta != null) return meta;
            }
            else
            {
                // Try prepending "DT_"
                meta = repo.GetMeta("DT_" + name);
                if (meta != null) return meta;
            }

            // Case-insensitive fallback
            repo.ForEachMeta(delegate(BGMetaEntity m)
            {
                if (meta == null && string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    meta = m;
                }
            });

            return meta;
        }

        public static BGEntity FindEntity(BGMetaEntity meta, string rowKey)
        {
            if (string.IsNullOrEmpty(rowKey)) return null;

            // Direct by row name
            BGEntity entity = meta.GetEntity(rowKey);
            if (entity != null) return entity;

            // By BGId
            BGId id;
            if (BGId.TryParse(rowKey, out id))
            {
                entity = meta.GetEntity(id);
                if (entity != null) return entity;
            }

            // Check if key is formatted as Name_BGId
            int underscoreIdx = rowKey.LastIndexOf('_');
            if (underscoreIdx > 0 && underscoreIdx < rowKey.Length - 1)
            {
                string idPart = rowKey.Substring(underscoreIdx + 1);
                if (BGId.TryParse(idPart, out id))
                {
                    entity = meta.GetEntity(id);
                    if (entity != null) return entity;
                }
            }

            // Case-insensitive search
            meta.ForEachEntity(delegate(BGEntity e)
            {
                if (entity == null && string.Equals(e.Name, rowKey, StringComparison.OrdinalIgnoreCase))
                {
                    entity = e;
                }
            });

            return entity;
        }

        public static BGField FindField(BGMetaEntity meta, string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName)) return null;

            BGField field = meta.GetField(fieldName, false);
            if (field != null) return field;

            // Case-insensitive match
            meta.ForEachField(delegate(BGField f)
            {
                if (field == null && string.Equals(f.Name, fieldName, StringComparison.OrdinalIgnoreCase))
                {
                    field = f;
                }
            });

            return field;
        }

        private static void EnsureDirectoryExists(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }

        #endregion

        #region Default Presets & Examples

        private static void DeployDefaultPresets()
        {
            try
            {
                DeployPresetFile("weapon_buffs.json", GetWeaponBuffsJson());
                DeployPresetFile("hard_mode.json", GetHardModeJson());
                DeployPresetFile("fast_waves.json", GetFastWavesJson());
            }
            catch (Exception ex)
            {
                ModLogger.LogWarning(string.Format("Failed deploying default presets: {0}", ex.Message));
            }
        }

        private static void DeployPresetFile(string fileName, string content)
        {
            string dest = Path.Combine(_presetsDir, fileName);
            if (!File.Exists(dest))
            {
                File.WriteAllText(dest, content, Encoding.UTF8);
                ModLogger.LogInfo(string.Format("Deployed default preset to '{0}'.", dest));
            }
        }

        private static void DeployExampleOverride()
        {
            try
            {
                string examplePath = Path.Combine(_overridesDir, "example_custom_mobs.json.example");
                if (!File.Exists(examplePath))
                {
                    string exampleJson = @"{
  ""CORE_Mobs"": {
    ""Undead_T0_Unarmed"": {
      ""Health"": 25.0,
      ""Speed"": 2.5,
      ""Damage"": 2.0
    }
  }
}";
                    File.WriteAllText(examplePath, exampleJson, Encoding.UTF8);
                }
            }
            catch { }
        }

        private static string GetWeaponBuffsJson()
        {
            return @"{
  ""CORE_AbilityEffects"": {
    ""Wpn_Necro_L1"": {
      ""Value"": 18.0,
      ""Cooldown"": 4.5
    },
    ""Wpn_ShdKnight_L1"": {
      ""Value"": 32.0,
      ""Cooldown"": 2.2,
      ""Size"": 1.2
    },
    ""Wpn_Ranger_L1"": {
      ""Value"": 30.0,
      ""Cooldown"": 1.6
    },
    ""Wpn_Fighter_L1"": {
      ""Value"": 35.0,
      ""Cooldown"": 1.0
    },
    ""Wpn_Bard_L1"": {
      ""Value"": 6.0,
      ""Size"": 1.8
    }
  }
}";
        }

        private static string GetHardModeJson()
        {
            return @"{
  ""CORE_Mobs"": {
    ""Undead_T0_Unarmed"": {
      ""Health"": 18.0,
      ""Speed"": 2.3
    },
    ""Undead_T0_1h"": {
      ""Health"": 35.0,
      ""Speed"": 2.2,
      ""Damage"": 3.0
    },
    ""Undead_T0_BigShield"": {
      ""Health"": 55.0,
      ""Speed"": 1.9,
      ""Damage"": 3.0
    },
    ""Undead_T1_Archer"": {
      ""Health"": 38.0,
      ""Speed"": 2.7,
      ""Damage"": 4.0
    },
    ""Undead_T1_2h"": {
      ""Health"": 45.0,
      ""Speed"": 2.4,
      ""Damage"": 8.0
    }
  },
  ""CORE_LevelSettings"": {
    ""Farmland"": {
      ""MobHealthModifier"": 1.15
    },
    ""Nobleland"": {
      ""MobHealthModifier"": 1.15
    },
    ""Churchland"": {
      ""MobHealthModifier"": 1.15
    }
  },
  ""CORE_PhaseDuration"": {
    ""FirstPhase"": {
      ""Duration"": 15.0
    },
    ""SecondPhase"": {
      ""Duration"": 15.0
    },
    ""TrirdPhase"": {
      ""Duration"": 15.0
    }
  }
}";
        }

        private static string GetFastWavesJson()
        {
            return @"{
  ""CORE_PhaseDuration"": {
    ""FirstPhase"": {
      ""Duration"": 10.0
    },
    ""SecondPhase"": {
      ""Duration"": 10.0
    },
    ""TrirdPhase"": {
      ""Duration"": 10.0
    }
  },
  ""CORE_Heroes"": {
    ""Robbie"": {
      ""Movement"": 6.75
    },
    ""Milo"": {
      ""Movement"": 6.75
    },
    ""Danica"": {
      ""Movement"": 6.75
    },
    ""Morgan"": {
      ""Movement"": 6.75
    },
    ""Gorian"": {
      ""Movement"": 6.75
    },
    ""Gwen"": {
      ""Movement"": 6.75
    }
  }
}";
        }

        #endregion
    }

    #region Harmony Patches

    [HarmonyPatch(typeof(DataTableHookup), "InitService")]
    public static class Patch_DataTableHookup_InitService
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            // Guarantees overrides are applied to BGRepo.I BEFORE DataTableHookup reads and caches database data
            NecromanticDataPlugin.EnsureOverridesApplied("DataTableHookup.InitService Prefix");
        }
    }

    #endregion
}
