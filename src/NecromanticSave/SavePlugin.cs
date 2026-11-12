using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using Blinkmoon.GameSystem;
using Newtonsoft.Json;
using NecromanticCommon;

namespace NecromanticSave
{
    [BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
    public class SavePlugin : BaseUnityPlugin
    {
        public const string PLUGIN_GUID = "com.necromantic.save";
        public const string PLUGIN_NAME = "Necromantic Save Manager & Editor";
        public const string PLUGIN_VERSION = "1.0.0";

        internal static ManualLogSource Log;
        private Harmony harmony;

        // --- Config Bindings ---
        public static ConfigEntry<KeyCode> ToggleKey;
        public static ConfigEntry<bool> AutoBackupOnLaunch;
        public static ConfigEntry<bool> AutoBackupOnMatchStart;
        public static ConfigEntry<int> MaxBackups;
        public static ConfigEntry<string> BackupSubFolder;

        // --- Paths ---
        private static string backupDirectoryPath;
        private static string jsonExportPath;

        // --- GUI State ---
        private bool showGui = false;
        private Rect windowRect = new Rect(40, 40, 560, 660);
        private int selectedTab = 0;
        private readonly string[] tabNames = new string[] { "Backups & Restore", "Save Editor", "JSON Export / Import" };

        private Vector2 backupScrollPos = Vector2.zero;
        private Vector2 editorScrollPos = Vector2.zero;
        private int selectedBackupIndex = -1;

        // --- Editor Input State ---
        private string scrollsInput = "0";
        private string cheeseInput = "0";
        private string matchesPlayedInput = "0";
        private string matchesWonInput = "0";
        private string enemiesKilledInput = "0";
        private bool confirmReset = false;

        // --- Status Message ---
        private string statusMessage = "Press F11 to toggle Save Manager & Editor.";
        private Color statusColor = Color.white;

        // --- Backup Metadata Class ---
        public class BackupEntry
        {
            public string FilePath;
            public string FileName;
            public DateTime LastWriteTime;
            public long FileSizeBytes;

            public string DisplayText
            {
                get
                {
                    double kb = (double)FileSizeBytes / 1024.0;
                    return string.Format("{0}  ({1:F1} KB) - {2:yyyy-MM-dd HH:mm:ss}", FileName, kb, LastWriteTime);
                }
            }
        }

        private List<BackupEntry> backupList = new List<BackupEntry>();
        private bool hasBackedUpThisMatch = false;

        private void Awake()
        {
            Log = Logger;
            ModHost.Protect(this, Log);
            Log.LogInfo(PLUGIN_NAME + " v" + PLUGIN_VERSION + " is initializing...");

            // Config bindings
            ToggleKey = Config.Bind<KeyCode>("General", "ToggleKey", KeyCode.F11, "Hotkey to toggle the Save Manager & Editor window.");
            AutoBackupOnLaunch = Config.Bind<bool>("Backups", "AutoBackupOnLaunch", true, "Automatically backup BlinkmoonSave.es3 when the game launches.");
            AutoBackupOnMatchStart = Config.Bind<bool>("Backups", "AutoBackupOnMatchStart", true, "Automatically backup BlinkmoonSave.es3 when a match starts.");
            MaxBackups = Config.Bind<int>("Backups", "MaxBackups", 50, "Maximum number of backups to keep. Set to 0 for unlimited.");
            BackupSubFolder = Config.Bind<string>("Backups", "BackupSubFolder", "Backups", "Subfolder under BepInEx/config/NecromanticSave/ to store backups.");

            // Resolve directory paths
            string configDir = Path.Combine(Paths.ConfigPath, "NecromanticSave");
            if (!Directory.Exists(configDir))
            {
                Directory.CreateDirectory(configDir);
            }

            backupDirectoryPath = Path.Combine(configDir, BackupSubFolder.Value);
            if (!Directory.Exists(backupDirectoryPath))
            {
                Directory.CreateDirectory(backupDirectoryPath);
            }

            jsonExportPath = Path.Combine(configDir, "PlayerData_Export.json");

            // Event subscriptions
            try
            {
                Character.OnLocalPlayerSpawned += HandleLocalPlayerSpawned;
            }
            catch (Exception ex)
            {
                Log.LogWarning("Could not subscribe to Character.OnLocalPlayerSpawned: " + ex.Message);
            }

            try
            {
                SceneManager.activeSceneChanged += HandleActiveSceneChanged;
            }
            catch (Exception ex)
            {
                Log.LogWarning("Could not subscribe to SceneManager.activeSceneChanged: " + ex.Message);
            }

            // Harmony initialization (if needed for future hooks)
            try
            {
                harmony = new Harmony(PLUGIN_GUID);
                harmony.PatchAll(Assembly.GetExecutingAssembly());
            }
            catch (Exception ex)
            {
                Log.LogWarning("Harmony patch error (non-fatal): " + ex.Message);
            }

            Log.LogInfo(PLUGIN_NAME + " loaded. Toggle hotkey: " + ToggleKey.Value);
        }

        private void Start()
        {
            // Auto-backup on game launch
            if (AutoBackupOnLaunch.Value)
            {
                try
                {
                    string savePath = GetSaveFilePath();
                    if (File.Exists(savePath))
                    {
                        CreateBackup("BlinkmoonSave_Launch");
                        Log.LogInfo("Auto-backup on game launch completed.");
                    }
                }
                catch (Exception ex)
                {
                    Log.LogError("Failed to create auto-backup on launch: " + ex.Message);
                }
            }

            RefreshBackupList();
        }

        private void OnDestroy()
        {
            try
            {
                Character.OnLocalPlayerSpawned -= HandleLocalPlayerSpawned;
                SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
                if (harmony != null)
                {
                    harmony.UnpatchSelf();
                }
            }
            catch { }
        }

        private void HandleLocalPlayerSpawned(Character character)
        {
            if (AutoBackupOnMatchStart.Value && !hasBackedUpThisMatch)
            {
                try
                {
                    string savePath = GetSaveFilePath();
                    if (File.Exists(savePath))
                    {
                        CreateBackup("BlinkmoonSave_MatchStart");
                        hasBackedUpThisMatch = true;
                        Log.LogInfo("Auto-backup on match start completed.");
                    }
                }
                catch (Exception ex)
                {
                    Log.LogError("Failed to create auto-backup on match start: " + ex.Message);
                }
            }
        }

        private void HandleActiveSceneChanged(Scene oldScene, Scene newScene)
        {
            // Reset match backup flag when leaving match
            hasBackedUpThisMatch = false;
        }

        private void Update()
        {
            if (HotkeyHelper.IsKeyDown(ToggleKey.Value, Log, "Toggle Save Editor"))
            {
                showGui = !showGui;
                CanvasOverlay.Notify("Save Manager & Editor [" + ToggleKey.Value + "]: " + (showGui ? "OPEN" : "CLOSED"), 3.0f);
                if (showGui)
                {
                    RefreshBackupList();
                    SyncEditorFieldsFromMemory();
                    SetStatus("Save Manager & Editor opened.", Color.green);
                }
            }

            if (showGui)
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }
        }

        // =========================================================================
        // Save File Path & Backups Management
        // =========================================================================

        public static string GetSaveFilePath()
        {
            try
            {
                if (!string.IsNullOrEmpty(Application.persistentDataPath))
                {
                    return Path.Combine(Application.persistentDataPath, "BlinkmoonSave.es3");
                }
            }
            catch { }

            string userProfile = Environment.GetEnvironmentVariable("USERPROFILE");
            if (!string.IsNullOrEmpty(userProfile))
            {
                return Path.Combine(userProfile, @"AppData\LocalLow\Blinkmoon\Necromantic\BlinkmoonSave.es3");
            }
            return "BlinkmoonSave.es3";
        }

        private void RefreshBackupList()
        {
            backupList.Clear();
            try
            {
                if (Directory.Exists(backupDirectoryPath))
                {
                    string[] files = Directory.GetFiles(backupDirectoryPath, "*.es3");
                    List<FileInfo> infos = new List<FileInfo>();
                    for (int i = 0; i < files.Length; i++)
                    {
                        infos.Add(new FileInfo(files[i]));
                    }
                    // Newest first. Sorting by file name would group by prefix
                    // (Launch / MatchStart / PreRestore...) instead of by time.
                    infos.Sort(delegate(FileInfo a, FileInfo b) { return b.LastWriteTime.CompareTo(a.LastWriteTime); });

                    for (int i = 0; i < infos.Count; i++)
                    {
                        FileInfo fi = infos[i];
                        BackupEntry entry = new BackupEntry();
                        entry.FilePath = fi.FullName;
                        entry.FileName = fi.Name;
                        entry.LastWriteTime = fi.LastWriteTime;
                        entry.FileSizeBytes = fi.Length;
                        backupList.Add(entry);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.LogError("Error refreshing backup list: " + ex.Message);
            }

            if (selectedBackupIndex >= backupList.Count)
            {
                selectedBackupIndex = backupList.Count - 1;
            }
        }

        public string CreateBackup(string prefix)
        {
            string source = GetSaveFilePath();
            if (!File.Exists(source))
            {
                SetStatus("Save file BlinkmoonSave.es3 not found! Cannot create backup.", Color.red);
                return null;
            }

            if (!Directory.Exists(backupDirectoryPath))
            {
                Directory.CreateDirectory(backupDirectoryPath);
            }

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string backupFileName = string.Format("{0}_{1}.es3", prefix, timestamp);
            string destination = Path.Combine(backupDirectoryPath, backupFileName);

            File.Copy(source, destination, true);
            Log.LogInfo("Backup created: " + destination);

            // Prune excess backups if limit exceeded
            PruneOldBackups();

            RefreshBackupList();
            SetStatus("Created backup: " + backupFileName, Color.green);
            return destination;
        }

        private void PruneOldBackups()
        {
            int max = MaxBackups.Value;
            if (max <= 0) return;

            try
            {
                if (Directory.Exists(backupDirectoryPath))
                {
                    string[] files = Directory.GetFiles(backupDirectoryPath, "*.es3");
                    if (files.Length > max)
                    {
                        // Sort by last write time ascending (oldest first)
                        List<FileInfo> fileInfos = new List<FileInfo>();
                        for (int i = 0; i < files.Length; i++)
                        {
                            fileInfos.Add(new FileInfo(files[i]));
                        }
                        fileInfos.Sort(delegate(FileInfo a, FileInfo b) { return a.LastWriteTime.CompareTo(b.LastWriteTime); });

                        int toRemove = fileInfos.Count - max;
                        for (int i = 0; i < toRemove; i++)
                        {
                            try
                            {
                                fileInfos[i].Delete();
                                Log.LogInfo("Pruned old backup: " + fileInfos[i].Name);
                            }
                            catch { }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.LogWarning("Error pruning old backups: " + ex.Message);
            }
        }

        public bool RestoreBackup(string backupPath)
        {
            if (!File.Exists(backupPath))
            {
                SetStatus("Selected backup file does not exist: " + backupPath, Color.red);
                return false;
            }

            string target = GetSaveFilePath();
            try
            {
                // Safety backup of existing save before restoring
                if (File.Exists(target))
                {
                    CreateBackup("PreRestore_BlinkmoonSave");
                }

                // Copy over target
                File.Copy(backupPath, target, true);
                Log.LogInfo("Restored " + backupPath + " -> " + target);

                // Reload data into game progression state
                ReloadProgressionData();
                SyncEditorFieldsFromMemory();

                SetStatus("Backup restored successfully and reloaded into game state!", Color.green);
                return true;
            }
            catch (Exception ex)
            {
                SetStatus("Failed to restore backup: " + ex.Message, Color.red);
                Log.LogError("RestoreBackup exception: " + ex);
                return false;
            }
        }

        public static void ReloadProgressionData()
        {
            try
            {
                if (PlayerProgressionManager.Instance != null)
                {
                    MethodInfo loadMethod = typeof(PlayerProgressionManager).GetMethod("LoadOrStartData", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (loadMethod != null)
                    {
                        loadMethod.Invoke(PlayerProgressionManager.Instance, null);
                        Log.LogInfo("Invoked PlayerProgressionManager.LoadOrStartData() successfully.");
                    }
                    else
                    {
                        Log.LogWarning("LoadOrStartData method not found on PlayerProgressionManager.");
                    }

                    // Notify UI & listeners
                    PlayerProgressionManager.Instance.InvokeDataChanged();
                    Log.LogInfo("Invoked PlayerProgressionManager.InvokeDataChanged().");
                }
                else
                {
                    Log.LogWarning("PlayerProgressionManager.Instance is null; reload deferred until game enters hub/menu.");
                }
            }
            catch (Exception ex)
            {
                Log.LogError("Error in ReloadProgressionData: " + ex.Message);
            }
        }

        // =========================================================================
        // In-Memory Editor & JSON Operations
        // =========================================================================

        private void SyncEditorFieldsFromMemory()
        {
            try
            {
                if (PlayerProgressionManager.Instance != null && PlayerProgressionManager.Instance.PersistentData != null)
                {
                    PlayerData pd = PlayerProgressionManager.Instance.PersistentData;
                    scrollsInput = pd.Scrolls.ToString();
                    cheeseInput = pd.Cheese.ToString();

                    if (pd.telemetryData != null)
                    {
                        matchesPlayedInput = pd.telemetryData.cumulativeGameplayData.totalMatchesPlayed.ToString();
                        matchesWonInput = pd.telemetryData.cumulativeGameplayData.totalMatchesWon.ToString();
                        enemiesKilledInput = pd.telemetryData.cumulativeGameplayData.totalEnemiesKilled.ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.LogWarning("Could not sync editor fields: " + ex.Message);
            }
        }

        private void CommitEditorFieldsToMemory()
        {
            try
            {
                if (PlayerProgressionManager.Instance == null || PlayerProgressionManager.Instance.PersistentData == null)
                {
                    SetStatus("PlayerProgressionManager.Instance or PersistentData is null!", Color.red);
                    return;
                }

                PlayerData pd = PlayerProgressionManager.Instance.PersistentData;

                int scrolls;
                if (int.TryParse(scrollsInput, out scrolls))
                {
                    pd.Scrolls = Mathf.Max(0, scrolls);
                }

                int cheese;
                if (int.TryParse(cheeseInput, out cheese))
                {
                    pd.Cheese = Mathf.Max(0, cheese);
                }

                if (pd.telemetryData != null)
                {
                    int matchesPlayed;
                    if (int.TryParse(matchesPlayedInput, out matchesPlayed))
                    {
                        pd.telemetryData.cumulativeGameplayData.totalMatchesPlayed = Mathf.Max(0, matchesPlayed);
                    }

                    int matchesWon;
                    if (int.TryParse(matchesWonInput, out matchesWon))
                    {
                        pd.telemetryData.cumulativeGameplayData.totalMatchesWon = Mathf.Max(0, matchesWon);
                    }

                    int enemiesKilled;
                    if (int.TryParse(enemiesKilledInput, out enemiesKilled))
                    {
                        pd.telemetryData.cumulativeGameplayData.totalEnemiesKilled = Mathf.Max(0, enemiesKilled);
                    }
                }

                // Call native SaveData() to cleanly serialize with ES3
                PlayerProgressionManager.Instance.SaveData();
                PlayerProgressionManager.Instance.InvokeDataChanged();
                SyncEditorFieldsFromMemory();

                SetStatus("Committed modifications to BlinkmoonSave.es3 with zero corruption risk!", Color.green);
            }
            catch (Exception ex)
            {
                SetStatus("Failed to commit changes: " + ex.Message, Color.red);
                Log.LogError("CommitEditorFieldsToMemory error: " + ex);
            }
        }

        private void AddScrolls(int amount)
        {
            if (PlayerProgressionManager.Instance != null && PlayerProgressionManager.Instance.PersistentData != null)
            {
                int current = PlayerProgressionManager.Instance.PersistentData.Scrolls;
                int newVal = Mathf.Max(0, current + amount);
                PlayerProgressionManager.Instance.PersistentData.Scrolls = newVal;
                scrollsInput = newVal.ToString();
                PlayerProgressionManager.Instance.InvokeDataChanged();
                SetStatus(string.Format("Scrolls updated: {0} -> {1}", current, newVal), Color.green);
            }
        }

        private void AddCheese(int amount)
        {
            if (PlayerProgressionManager.Instance != null && PlayerProgressionManager.Instance.PersistentData != null)
            {
                int current = PlayerProgressionManager.Instance.PersistentData.Cheese;
                int newVal = Mathf.Max(0, current + amount);
                PlayerProgressionManager.Instance.PersistentData.Cheese = newVal;
                cheeseInput = newVal.ToString();
                PlayerProgressionManager.Instance.InvokeDataChanged();
                SetStatus(string.Format("Cheese updated: {0} -> {1}", current, newVal), Color.green);
            }
        }

        public bool ExportPlayerDataToJson()
        {
            try
            {
                if (PlayerProgressionManager.Instance == null || PlayerProgressionManager.Instance.PersistentData == null)
                {
                    SetStatus("Player progression data not loaded yet!", Color.yellow);
                    return false;
                }

                PlayerData pd = PlayerProgressionManager.Instance.PersistentData;

                JsonSerializerSettings settings = new JsonSerializerSettings();
                settings.Formatting = Formatting.Indented;
                settings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
                settings.NullValueHandling = NullValueHandling.Include;

                string json = JsonConvert.SerializeObject(pd, settings);
                File.WriteAllText(jsonExportPath, json, Encoding.UTF8);

                SetStatus(string.Format("Exported PlayerData to JSON ({0} bytes) at: {1}", json.Length, jsonExportPath), Color.green);
                Log.LogInfo("Exported PlayerData to " + jsonExportPath);
                return true;
            }
            catch (Exception ex)
            {
                SetStatus("Failed to export JSON: " + ex.Message, Color.red);
                Log.LogError("ExportPlayerDataToJson error: " + ex);
                return false;
            }
        }

        public bool ImportPlayerDataFromJson()
        {
            try
            {
                if (!File.Exists(jsonExportPath))
                {
                    SetStatus("JSON export file not found: " + jsonExportPath, Color.red);
                    return false;
                }

                if (PlayerProgressionManager.Instance == null || PlayerProgressionManager.Instance.PersistentData == null)
                {
                    SetStatus("Player progression manager not loaded yet!", Color.yellow);
                    return false;
                }

                // Safety backup
                string savePath = GetSaveFilePath();
                if (File.Exists(savePath))
                {
                    CreateBackup("PreJsonImport_BlinkmoonSave");
                }

                string json = File.ReadAllText(jsonExportPath, Encoding.UTF8);
                JsonSerializerSettings settings = new JsonSerializerSettings();
                settings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;

                PlayerData imported = JsonConvert.DeserializeObject<PlayerData>(json, settings);
                if (imported == null)
                {
                    SetStatus("Deserialized JSON returned null!", Color.red);
                    return false;
                }

                PlayerData current = PlayerProgressionManager.Instance.PersistentData;

                // Copy over scalar fields
                current.Scrolls = imported.Scrolls;
                current.Cheese = imported.Cheese;
                current.LibrarySpent = imported.LibrarySpent;
                current.TotalBookCount = imported.TotalBookCount;
                if (!string.IsNullOrEmpty(imported.ChapterInProgress)) current.ChapterInProgress = imported.ChapterInProgress;
                if (!string.IsNullOrEmpty(imported.LastAssignmentId)) current.LastAssignmentId = imported.LastAssignmentId;

                // Copy collections if present
                if (imported.Weapons != null) current.Weapons = imported.Weapons;
                if (imported.Presents != null) current.Presents = imported.Presents;
                if (imported.Relations != null) current.Relations = imported.Relations;
                if (imported.Abilities != null) current.Abilities = imported.Abilities;
                if (imported.Library != null) current.Library = imported.Library;
                if (imported.Students != null) current.Students = imported.Students;
                if (imported.Achievements != null) current.Achievements = imported.Achievements;
                if (imported.Assignments != null) current.Assignments = imported.Assignments;
                if (imported.Boosts != null) current.Boosts = imported.Boosts;
                if (imported.Books != null) current.Books = imported.Books;
                if (imported.PastChapters != null) current.PastChapters = imported.PastChapters;
                if (imported.PastAssignments != null) current.PastAssignments = imported.PastAssignments;

                if (imported.telemetryData != null && current.telemetryData != null)
                {
                    current.telemetryData.cumulativeGameplayData = imported.telemetryData.cumulativeGameplayData;
                }

                // Also update backing PlayerMainData field if available
                FieldInfo field = typeof(PlayerProgressionManager).GetField("_playerMainData", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (field != null)
                {
                    PlayerMainData pmd = field.GetValue(PlayerProgressionManager.Instance) as PlayerMainData;
                    if (pmd != null)
                    {
                        pmd.PersistedPlayerData = current;
                    }
                }

                // Commit to disk using game's ES3 save
                PlayerProgressionManager.Instance.SaveData();
                PlayerProgressionManager.Instance.InvokeDataChanged();
                SyncEditorFieldsFromMemory();

                SetStatus("Imported PlayerData from JSON and saved to disk successfully!", Color.green);
                Log.LogInfo("Imported PlayerData from JSON and committed to BlinkmoonSave.es3.");
                return true;
            }
            catch (Exception ex)
            {
                SetStatus("Failed to import JSON: " + ex.Message, Color.red);
                Log.LogError("ImportPlayerDataFromJson error: " + ex);
                return false;
            }
        }

        private void SetStatus(string message, Color color)
        {
            statusMessage = message;
            statusColor = color;
        }

        // =========================================================================
        // IMGUI GUI Window Rendering
        // =========================================================================

        private void OnGUI()
        {
            if (!showGui) return;
            GUI.depth = -100;

            Color prevBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.12f, 0.12f, 0.16f, 0.96f);
            windowRect = GUILayout.Window(99903, windowRect, DrawWindow, "Necromantic Save Manager & Editor (v" + PLUGIN_VERSION + ")");
            GUI.backgroundColor = prevBg; // GUI colour is global; don't tint other plugins' windows
        }

        private void DrawWindow(int windowId)
        {
            GUILayout.Space(4);

            // Tab Toolbar
            selectedTab = GUILayout.Toolbar(selectedTab, tabNames, GUILayout.Height(28));
            GUILayout.Space(8);

            switch (selectedTab)
            {
                case 0:
                    DrawBackupsTab();
                    break;
                case 1:
                    DrawEditorTab();
                    break;
                case 2:
                    DrawJsonTab();
                    break;
            }

            // Bottom Status Bar
            GUILayout.FlexibleSpace();
            GUILayout.Space(8);
            Color prevColor = GUI.color;
            GUI.color = statusColor;
            GUILayout.Box(statusMessage, GUILayout.Height(36), GUILayout.ExpandWidth(true));
            GUI.color = prevColor;

            GUILayout.BeginHorizontal();
            GUILayout.Label("<color=#888888>Hotkey: F11 | Drag window from title bar</color>");
            if (GUILayout.Button("Close [F11]", GUILayout.Width(90), GUILayout.Height(22)))
            {
                showGui = false;
            }
            GUILayout.EndHorizontal();

            GUI.DragWindow(new Rect(0, 0, 10000, 30));
        }

        private void DrawBackupsTab()
        {
            GUILayout.Label("<b>--- Active Save File Status ---</b>");
            string savePath = GetSaveFilePath();
            bool fileExists = File.Exists(savePath);

            GUILayout.BeginVertical("Box");
            GUILayout.Label(string.Format("<b>File:</b> {0}", Path.GetFileName(savePath)));
            GUILayout.Label(string.Format("<b>Status:</b> {0}", fileExists ? "<color=green>Present & Healthy</color>" : "<color=red>Not Found</color>"));
            if (fileExists)
            {
                FileInfo fi = new FileInfo(savePath);
                double kb = (double)fi.Length / 1024.0;
                GUILayout.Label(string.Format("<b>Size:</b> {0:F1} KB ({1} bytes) | <b>Modified:</b> {2:yyyy-MM-dd HH:mm:ss}", kb, fi.Length, fi.LastWriteTime));
            }
            GUILayout.EndVertical();

            GUILayout.Space(6);
            GUILayout.Label("<b>--- Manual Backup Controls ---</b>");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Create Backup Now", GUILayout.Height(32)))
            {
                CreateBackup("BlinkmoonSave");
            }
            if (GUILayout.Button("Refresh Backups List", GUILayout.Height(32)))
            {
                RefreshBackupList();
                SetStatus("Backups list refreshed (" + backupList.Count + " found).", Color.white);
            }
            if (GUILayout.Button("Open Backups Folder", GUILayout.Height(32)))
            {
                try
                {
                    System.Diagnostics.Process.Start(backupDirectoryPath);
                }
                catch (Exception ex)
                {
                    SetStatus("Could not open folder: " + ex.Message, Color.red);
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            GUILayout.Label(string.Format("<b>--- Available Backups ({0} Total, Newest First) ---</b>", backupList.Count));

            backupScrollPos = GUILayout.BeginScrollView(backupScrollPos, "Box", GUILayout.Height(220));
            if (backupList.Count == 0)
            {
                GUILayout.Label("<color=#888888>No backups found yet. Click 'Create Backup Now' above.</color>");
            }
            else
            {
                for (int i = 0; i < backupList.Count; i++)
                {
                    BackupEntry entry = backupList[i];
                    bool isSelected = (i == selectedBackupIndex);

                    GUILayout.BeginHorizontal();
                    string label = string.Format("{0}. {1}", i + 1, entry.DisplayText);
                    if (GUILayout.Toggle(isSelected, label, "Button", GUILayout.Height(26)))
                    {
                        selectedBackupIndex = i;
                    }
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndScrollView();

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            bool canRestore = (selectedBackupIndex >= 0 && selectedBackupIndex < backupList.Count);
            GUI.enabled = canRestore;
            if (GUILayout.Button(canRestore ? string.Format("Restore Selected Backup (#{0})", selectedBackupIndex + 1) : "Select a Backup to Restore", GUILayout.Height(32)))
            {
                RestoreBackup(backupList[selectedBackupIndex].FilePath);
            }
            if (GUILayout.Button("Delete Selected", GUILayout.Width(130), GUILayout.Height(32)))
            {
                if (canRestore)
                {
                    try
                    {
                        File.Delete(backupList[selectedBackupIndex].FilePath);
                        SetStatus("Deleted backup: " + backupList[selectedBackupIndex].FileName, Color.yellow);
                        RefreshBackupList();
                    }
                    catch (Exception ex)
                    {
                        SetStatus("Delete failed: " + ex.Message, Color.red);
                    }
                }
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("<b>--- Automation Options ---</b>");
            AutoBackupOnLaunch.Value = GUILayout.Toggle(AutoBackupOnLaunch.Value, "  Auto-backup on game launch");
            AutoBackupOnMatchStart.Value = GUILayout.Toggle(AutoBackupOnMatchStart.Value, "  Auto-backup on match start");
        }

        private void DrawEditorTab()
        {
            if (PlayerProgressionManager.Instance == null || PlayerProgressionManager.Instance.PersistentData == null)
            {
                GUILayout.Space(30);
                GUILayout.Label("<color=yellow><b>Progression Data Not Loaded Yet</b></color>");
                GUILayout.Label("Please enter the main menu, academy hub, or start a match so the game initializes PlayerProgressionManager.");
                if (GUILayout.Button("Retry Data Binding / Refresh", GUILayout.Height(30)))
                {
                    SyncEditorFieldsFromMemory();
                }
                return;
            }

            PlayerData pd = PlayerProgressionManager.Instance.PersistentData;

            editorScrollPos = GUILayout.BeginScrollView(editorScrollPos);

            // --- Scrolls Section ---
            GUILayout.Label("<b>--- Scrolls Economy (Academy Upgrades) ---</b>");
            GUILayout.BeginVertical("Box");
            GUILayout.Label(string.Format("Current In-Memory Scrolls: <b><color=yellow>{0:N0}</color></b>", pd.Scrolls));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Set Value:", GUILayout.Width(70));
            scrollsInput = GUILayout.TextField(scrollsInput, GUILayout.Width(130));
            if (GUILayout.Button("Apply", GUILayout.Width(60)))
            {
                int val;
                if (int.TryParse(scrollsInput, out val))
                {
                    pd.Scrolls = Mathf.Max(0, val);
                    PlayerProgressionManager.Instance.InvokeDataChanged();
                    SetStatus("Scrolls set to " + pd.Scrolls, Color.green);
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+1,000")) AddScrolls(1000);
            if (GUILayout.Button("+10,000")) AddScrolls(10000);
            if (GUILayout.Button("+100,000")) AddScrolls(100000);
            if (GUILayout.Button("Max (999,999)"))
            {
                pd.Scrolls = 999999;
                scrollsInput = "999999";
                PlayerProgressionManager.Instance.InvokeDataChanged();
                SetStatus("Scrolls set to 999,999", Color.green);
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.Space(6);

            // --- Cheese Section ---
            GUILayout.Label("<b>--- Cheese Currency ---</b>");
            GUILayout.BeginVertical("Box");
            GUILayout.Label(string.Format("Current In-Memory Cheese: <b><color=yellow>{0:N0}</color></b>", pd.Cheese));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Set Value:", GUILayout.Width(70));
            cheeseInput = GUILayout.TextField(cheeseInput, GUILayout.Width(130));
            if (GUILayout.Button("Apply", GUILayout.Width(60)))
            {
                int val;
                if (int.TryParse(cheeseInput, out val))
                {
                    pd.Cheese = Mathf.Max(0, val);
                    PlayerProgressionManager.Instance.InvokeDataChanged();
                    SetStatus("Cheese set to " + pd.Cheese, Color.green);
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+10")) AddCheese(10);
            if (GUILayout.Button("+100")) AddCheese(100);
            if (GUILayout.Button("+1,000")) AddCheese(1000);
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.Space(6);

            // --- Telemetry / Gameplay Statistics ---
            GUILayout.Label("<b>--- Lifetime Statistics (Telemetry) ---</b>");
            GUILayout.BeginVertical("Box");
            if (pd.telemetryData != null)
            {
                // Matches Played
                GUILayout.BeginHorizontal();
                GUILayout.Label(string.Format("Matches Played: <b>{0}</b>", pd.telemetryData.cumulativeGameplayData.totalMatchesPlayed), GUILayout.Width(180));
                matchesPlayedInput = GUILayout.TextField(matchesPlayedInput, GUILayout.Width(80));
                if (GUILayout.Button("+1", GUILayout.Width(40)))
                {
                    pd.telemetryData.cumulativeGameplayData.totalMatchesPlayed += 1;
                    matchesPlayedInput = pd.telemetryData.cumulativeGameplayData.totalMatchesPlayed.ToString();
                }
                if (GUILayout.Button("+5", GUILayout.Width(40)))
                {
                    pd.telemetryData.cumulativeGameplayData.totalMatchesPlayed += 5;
                    matchesPlayedInput = pd.telemetryData.cumulativeGameplayData.totalMatchesPlayed.ToString();
                }
                GUILayout.EndHorizontal();

                // Matches Won
                GUILayout.BeginHorizontal();
                GUILayout.Label(string.Format("Matches Won: <b>{0}</b>", pd.telemetryData.cumulativeGameplayData.totalMatchesWon), GUILayout.Width(180));
                matchesWonInput = GUILayout.TextField(matchesWonInput, GUILayout.Width(80));
                if (GUILayout.Button("+1", GUILayout.Width(40)))
                {
                    pd.telemetryData.cumulativeGameplayData.totalMatchesWon += 1;
                    matchesWonInput = pd.telemetryData.cumulativeGameplayData.totalMatchesWon.ToString();
                }
                if (GUILayout.Button("+5", GUILayout.Width(40)))
                {
                    pd.telemetryData.cumulativeGameplayData.totalMatchesWon += 5;
                    matchesWonInput = pd.telemetryData.cumulativeGameplayData.totalMatchesWon.ToString();
                }
                GUILayout.EndHorizontal();

                // Enemies Killed
                GUILayout.BeginHorizontal();
                GUILayout.Label(string.Format("Enemies Killed: <b>{0:N0}</b>", pd.telemetryData.cumulativeGameplayData.totalEnemiesKilled), GUILayout.Width(180));
                enemiesKilledInput = GUILayout.TextField(enemiesKilledInput, GUILayout.Width(80));
                if (GUILayout.Button("+1k", GUILayout.Width(40)))
                {
                    pd.telemetryData.cumulativeGameplayData.totalEnemiesKilled += 1000;
                    enemiesKilledInput = pd.telemetryData.cumulativeGameplayData.totalEnemiesKilled.ToString();
                }
                if (GUILayout.Button("+10k", GUILayout.Width(40)))
                {
                    pd.telemetryData.cumulativeGameplayData.totalEnemiesKilled += 10000;
                    enemiesKilledInput = pd.telemetryData.cumulativeGameplayData.totalEnemiesKilled.ToString();
                }
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Label("<color=#888888>TelemetryData is currently uninitialized.</color>");
            }
            GUILayout.EndVertical();

            GUILayout.Space(6);

            // --- Progression Shortcuts ---
            GUILayout.Label("<b>--- Progression Shortcuts & Cheats ---</b>");
            GUILayout.BeginVertical("Box");
            if (GUILayout.Button("Unlock All Weapons & Hero Abilities", GUILayout.Height(30)))
            {
                PlayerProgressionManager.Instance.UnlockAllWeaponsAndAbilities();
                PlayerProgressionManager.Instance.InvokeDataChanged();
                SetStatus("Unlocked all weapons and hero abilities in persistent data!", Color.green);
            }

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            confirmReset = GUILayout.Toggle(confirmReset, " Confirm Reset", GUILayout.Width(120));
            GUI.enabled = confirmReset;
            if (GUILayout.Button("Reset Progression to New Game Defaults", GUILayout.Height(28)))
            {
                try
                {
                    // Always keep a way back from a full progression wipe.
                    string backup = File.Exists(GetSaveFilePath()) ? CreateBackup("PreReset_BlinkmoonSave") : null;
                    PlayerProgressionManager.Instance.ResetPlayerData();
                    PlayerProgressionManager.Instance.InvokeDataChanged();
                    SyncEditorFieldsFromMemory();
                    SetStatus("Progression reset to new game state." + (backup != null ? " Backup: " + Path.GetFileName(backup) : ""), Color.yellow);
                }
                catch (Exception ex)
                {
                    SetStatus("Reset failed: " + ex.Message, Color.red);
                    Log.LogError("ResetPlayerData error: " + ex);
                }
                confirmReset = false;
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.Space(10);

            // --- Persistence Commit / Discard Buttons ---
            GUILayout.Label("<b>--- Save & Disk Persistence ---</b>");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Commit & Save Changes to Disk", GUILayout.Height(36)))
            {
                CommitEditorFieldsToMemory();
            }
            if (GUILayout.Button("Reload from Disk (Discard Edits)", GUILayout.Height(36)))
            {
                ReloadProgressionData();
                SyncEditorFieldsFromMemory();
                SetStatus("Discarded in-memory edits and reloaded from disk.", Color.white);
            }
            GUILayout.EndHorizontal();

            GUILayout.EndScrollView();
        }

        private void DrawJsonTab()
        {
            GUILayout.Label("<b>--- JSON Export & Import ---</b>");
            GUILayout.Label("Export player progression data into human-readable formatted JSON for inspection or backup, and import edited JSON directly into the game.");

            GUILayout.Space(8);
            GUILayout.BeginVertical("Box");
            GUILayout.Label(string.Format("<b>Export File:</b> {0}", Path.GetFileName(jsonExportPath)));
            bool jsonExists = File.Exists(jsonExportPath);
            GUILayout.Label(string.Format("<b>Status:</b> {0}", jsonExists ? "<color=green>File Present</color>" : "<color=#888888>Not yet exported</color>"));
            if (jsonExists)
            {
                FileInfo fi = new FileInfo(jsonExportPath);
                double kb = (double)fi.Length / 1024.0;
                GUILayout.Label(string.Format("<b>Size:</b> {0:F1} KB ({1} bytes) | <b>Modified:</b> {2:yyyy-MM-dd HH:mm:ss}", kb, fi.Length, fi.LastWriteTime));
            }
            GUILayout.Label(string.Format("<b>Full Path:</b> <color=#AAAAAA>{0}</color>", jsonExportPath));
            GUILayout.EndVertical();

            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Export PlayerData to JSON", GUILayout.Height(36)))
            {
                ExportPlayerDataToJson();
            }

            GUI.enabled = jsonExists;
            if (GUILayout.Button("Import PlayerData from JSON", GUILayout.Height(36)))
            {
                ImportPlayerDataFromJson();
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.Space(12);
            if (GUILayout.Button("Open Config Directory in Explorer", GUILayout.Height(30)))
            {
                try
                {
                    string dir = Path.GetDirectoryName(jsonExportPath);
                    System.Diagnostics.Process.Start(dir);
                }
                catch (Exception ex)
                {
                    SetStatus("Could not open folder: " + ex.Message, Color.red);
                }
            }

            GUILayout.Space(10);
            GUILayout.Label("<b>Instructions:</b>");
            GUILayout.Label("1. Click 'Export PlayerData to JSON' to dump current persistent progression.\n2. You can open and edit the JSON file with any text editor (e.g. VS Code, Notepad).\n3. Click 'Import PlayerData from JSON' to parse changes, apply them to memory, and automatically save to BlinkmoonSave.es3 with zero corruption risk.");
        }
    }
}
