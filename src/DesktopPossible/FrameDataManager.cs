// Inherited upstream code predates nullable reference types: nullable WARNINGS are off for this
// file until it is annotated (annotations remain valid). New files are fully nullable-clean.
#nullable disable warnings

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Desktop_Frames
{
    /// <summary>
    /// Manages all frame data operations including JSON loading, saving, and basic migrations
    /// Extracted from Framemanager for better code organization and maintainability
    /// Handles core data persistence and simple validation/migration scenarios
    /// </summary>
    public static class FrameDataManager
    {
        #region Private Fields - Data Storage
        // Main frame data collection - moved from Framemanager
        private static List<dynamic> _frameData;

        // JSON file path - moved from Framemanager  
        private static string _jsonFilePath;
        #endregion

        #region Public Properties - Data Access
        /// <summary>
        /// Provides access to frame data collection
        /// Used by: Framemanager, TrayManager, CustomizeFrameForm, ItemMoveDialog
        /// Category: Data Access
        /// </summary>
        public static List<dynamic> FrameData
        {
            get => _frameData;
            set => _frameData = value;




        }

        /// <summary>
        /// Gets the JSON file path
        /// Used by: Framemanager, backup operations, debugging
        /// Category: File Management
        /// </summary>
        public static string JsonFilePath
        {
            get => _jsonFilePath;
            set => _jsonFilePath = value;
        }
        #endregion

        #region Initialization - Used by: Framemanager startup
        /// <summary>
        /// Initializes the data manager with JSON file path
        /// Used by: Framemanager during application startup
        /// Category: Initialization
        /// </summary>
        public static void Initialize()
        {
            try
            {
                // ====================================================================
                // [LEGACY "FENCES" MIGRATION - DO NOT REMOVE]
                // Self-Healing Logic: Checks for legacy fences.json and instantly 
                // renames it to frames.json upon profile load.
                // ====================================================================
                string legacyPath = ProfileManager.GetProfileFilePath("fences.json");
                string newPath = ProfileManager.GetProfileFilePath("frames.json");

                if (!File.Exists(newPath) && File.Exists(legacyPath))
                {
                    try
                    {
                        // KISS: Read raw text, replace strings, write new file, delete old file.
                        string rawJson = File.ReadAllText(legacyPath);

                        rawJson = rawJson.Replace("\"FenceBorderColor\"", "\"FrameBorderColor\"");
                        rawJson = rawJson.Replace("\"frameBorderColor\"", "\"FrameBorderColor\"");
                        rawJson = rawJson.Replace("\"FenceBorderThickness\"", "\"FrameBorderThickness\"");
                        rawJson = rawJson.Replace("\"frameBorderThickness\"", "\"FrameBorderThickness\"");

                        File.WriteAllText(newPath, rawJson);
                        File.Delete(legacyPath);

                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General, "Auto-Migrated and scrubbed fences.json to frames.json");
                    }
                    catch (Exception ex)
                    {
                        LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"Migration scrub failed: {ex.Message}");
                        newPath = legacyPath; // Fallback to old file if blocked
                    }
                }

                _jsonFilePath = newPath;

                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.FrameCreation,
                    $"FrameDataManager initialized with path: {_jsonFilePath}");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.FrameCreation,
                    $"Error initializing FrameDataManager: {ex.Message}");
                throw;
            }
        }
        #endregion

        #region Core Data Operations - Used by: Framemanager, TrayManager
        /// <summary>
        /// Main frame data loading method - moved from Framemanager.LoadFrameData
        /// Used by: Framemanager.LoadFrameData, application startup
        /// Category: Data Loading
        /// </summary>
        public static void LoadFrameData(TargetChecker targetChecker)
        {
            try
            {
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameCreation,
                    "FrameDataManager: Starting frame data load");

                // Initialize frame data list
                _frameData = new List<dynamic>();

                // Check if JSON file exists
                if (!File.Exists(_jsonFilePath))
                {
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameCreation,
                        "JSON file not found. Starting with empty frame configuration.");
                    return;
                }

                // Load and parse JSON data
                if (!LoadFrameDataFromJson())
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.FrameCreation,
                        "Failed to load JSON data. Starting with empty configuration.");
                    return;
                }

                // Apply simple migrations and validation
                ApplySimpleMigrations();

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameCreation,
                    $"Successfully loaded {_frameData?.Count ?? 0} frames from JSON");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.FrameCreation,
                    $"Critical error in LoadFrameData: {ex.Message}");
                _frameData = new List<dynamic>(); // Fallback to empty list
            }
        }

        /// <summary>
        /// JSON file parsing and loading - moved from Framemanager.LoadFrameDataFromJson
        /// Used by: LoadFrameData method
        /// Category: JSON Operations
        /// </summary>
        private static bool LoadFrameDataFromJson()
        {
            try
            {
                string jsonContent = File.ReadAllText(_jsonFilePath);

                // Check if the file is empty or contains only whitespace
                if (string.IsNullOrWhiteSpace(jsonContent))
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.FrameCreation,
                        "JSON file is empty or contains only whitespace. Using default frame configuration.");
                    return false;
                }

                // First, try to parse as a list of frames
                try
                {
                    _frameData = JsonConvert.DeserializeObject<List<dynamic>>(jsonContent);
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.FrameCreation,
                        $"Successfully loaded {_frameData?.Count ?? 0} frames from JSON array.");
                    Framemanager.RefreshFrameHotkeys();
                    return _frameData != null;
                }
                catch (JsonException)
                {
                    // If that fails, try to parse as a single frames object
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.FrameCreation,
                        "Failed to parse as array, trying single object format.");

                    dynamic singleFrame = JsonConvert.DeserializeObject(jsonContent);
                    _frameData = new List<dynamic> { singleFrame };
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.FrameCreation,
                        "Successfully loaded single frame from JSON object.");
                    return true;
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.FrameCreation,
                    $"Error loading frame data from JSON: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Saves frame data to JSON with consistent formatting - moved from Framemanager.SaveFrameData
        /// Used by: Framemanager operations, frame updates, position changes
        /// Category: JSON Operations
        /// </summary>
        public static void SaveFrameData()
        {
            try
            {
                var serializedData = new List<JObject>();

                foreach (dynamic frame in _frameData)
                {
                    IDictionary<string, object> frameDict = frame is IDictionary<string, object> dict ?
                        dict : ((JObject)frame).ToObject<IDictionary<string, object>>();

                    // ====================================================================
                    // [LEGACY "FENCES" MIGRATION - DO NOT REMOVE]
                    // The Ultimate Interceptor: Forces old or accidentally generated
                    // "Fence" keys into official "Frame" keys RIGHT BEFORE saving to disk.
                    // ====================================================================
                    ConsolidateLegacyKeys(frameDict);

                    // Apply simple format consistency
                    ApplyFormatConsistency(frameDict);

                    serializedData.Add(JObject.FromObject(frameDict));
                }

                string formattedJson = JsonConvert.SerializeObject(serializedData, Formatting.Indented);
                AtomicFile.WriteAllText(_jsonFilePath, formattedJson);

                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.Settings,
                    $"Saved frames.json with consistent formatting for {serializedData.Count} frames");

                // Keep the per-frame focus-hotkey lookup in sync with the latest data.
                Framemanager.RefreshFrameHotkeys();

                // Write the same geometry into the active display configuration. This has to
                // happen continuously: by the time Windows reports a display change it has
                // already moved windows off the monitor that vanished, so the only record of
                // where the user actually had them is the one kept before the change.
                // (No-op while the display configuration is unstable, and when the content
                // is unchanged the sidecar is not rewritten.)
                DisplayLayoutManager.MirrorActiveLayout();
            }
            catch (Exception ex)
            {
                // Do NOT rethrow: a transient IO failure (file briefly locked by backup/AV)
                // must never crash the app from a background save path — the atomic write
                // guarantees the previous file is intact, and the next save retries.
                // (A rethrow here once escaped all the way to WndProc via the
                // resize-end flush.)
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.Settings,
                    $"Error saving frame data (previous file intact, will retry on next save): {ex.Message}");
            }
        }
        #endregion

        #region Frame Creation - Used by: TrayManager, Framemanager
        /// <summary>
        /// Creates new frame with proper defaults - moved from Framemanager.CreateNewFrame
        /// Category: Frame Creation
        /// </summary>
        public static dynamic CreateNewFrame(string title, string itemsType, double x = 20, double y = 20,
            string customColor = null, string customLaunchEffect = null)
        {
            try
            {
                // Generate appropriate frame name
                string frameName = (itemsType != "Portal") ?
                    CoreUtilities.GenerateRandomName() : title;

                // Create new frame object
                dynamic newFrame = new System.Dynamic.ExpandoObject();
                newFrame.Id = Guid.NewGuid().ToString();
                IDictionary<string, object> newframeDict = newFrame;

                // Set basic properties
                newframeDict["Title"] = frameName;
                newframeDict["X"] = x;
                newframeDict["Y"] = y;
                newframeDict["Width"] = 230;
                newframeDict["Height"] = 130;
                newframeDict["ItemsType"] = itemsType;

                // Set items based on frame type
                newframeDict["Items"] = itemsType == "Portal" ? "" : new JArray();

                // Apply default properties with simple validation
                ApplyFrameDefaults(newframeDict, customColor, customLaunchEffect);

                // Add to data collection and save
                _frameData.Add(newFrame);
                SaveFrameData();

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameCreation,
                    $"Created new {itemsType} frame '{frameName}' with ID {newFrame.Id}");

                return newFrame;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.FrameCreation,
                    $"Error creating new frame: {ex.Message}");
                throw;
            }
        }
        #endregion

        #region Simple Migration and Validation - Internal Use

        /// <summary>
        /// [LEGACY "FENCES" MIGRATION - DO NOT REMOVE]
        /// Folds legacy "Fence"/camelCase border keys into the official "Frame" keys.
        /// Only null legacy values are discarded — "0" and "" are legitimate settings
        /// (e.g. border thickness 0) and are preserved.
        /// Used by: SaveFrameData (right before serializing each frame)
        /// Category: Data Migration (Simple)
        /// </summary>
        public static void ConsolidateLegacyKeys(IDictionary<string, object> frameDict)
        {
            void ConsolidateKey(string officialKey, string[] legacyKeys)
            {
                object rescuedValue = null;

                // Extract data from old keys and delete them permanently
                foreach (string oldKey in legacyKeys)
                {
                    if (frameDict.ContainsKey(oldKey))
                    {
                        if (frameDict[oldKey] != null)
                        {
                            rescuedValue = frameDict[oldKey];
                        }
                        frameDict.Remove(oldKey); // Vacuum it out
                    }
                }

                // Apply rescued data to the official key if it doesn't already have a value
                bool hasValidOfficial = frameDict.ContainsKey(officialKey) && frameDict[officialKey] != null;

                if (!hasValidOfficial && rescuedValue != null)
                {
                    frameDict[officialKey] = rescuedValue;
                }
            }

            ConsolidateKey("FrameBorderColor", new[] { "FenceBorderColor", "frameBorderColor" });
            ConsolidateKey("FrameBorderThickness", new[] { "FenceBorderThickness", "frameBorderThickness" });
        }



        /// <summary>
        /// Applies simple migrations and property defaults
        /// Used by: LoadFrameData during startup
        /// Category: Data Migration (Simple)
        /// </summary>
        /// <summary>
        /// Rebrand migration: item paths that point into the pre-rename store root
        /// (%LocalAppData%\DesktopFramesPossible) are rewritten to the current root when the file
        /// has moved there (see FrameStore.MigrateLegacyRoot). Paths still present at the old
        /// location are left alone.
        /// </summary>
        private static bool RemapLegacyStorePaths(IDictionary<string, object> frameDict)
        {
            if (!frameDict.TryGetValue("Items", out object itemsObj) || itemsObj is not JArray items) return false;

            bool modified = false;
            foreach (var item in items.OfType<JObject>())
            {
                string path = item["Filename"]?.ToString();
                string remapped = FrameStore.RemapLegacyPath(path);
                if (remapped == null || string.Equals(remapped, path, StringComparison.Ordinal)) continue;
                if (File.Exists(path) || !File.Exists(remapped)) continue;

                item["Filename"] = remapped;
                modified = true;
            }

            if (modified)
            {
                frameDict["Items"] = items;
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameCreation,
                    $"FrameDataManager: remapped legacy store paths in frame '{frameDict["Title"]}'");
            }
            return modified;
        }

        private static void ApplySimpleMigrations()
        {
            bool jsonModified = false;

            for (int i = 0; i < _frameData.Count; i++)
            {
                try
                {
                    if (_frameData[i] is JObject frameObj)
                    {
                

                        // --- Run existing property/type validations ---
                        IDictionary<string, object> frameDict = frameObj.ToObject<IDictionary<string, object>>();
                        bool dictModified = false;

                        if (AddMissingBasicProperties(frameDict)) dictModified = true;
                        if (ValidateDataTypes(frameDict)) dictModified = true;
                        if (RemapLegacyStorePaths(frameDict)) dictModified = true;

                        if (dictModified)
                        {
                            _frameData[i] = JObject.FromObject(frameDict);
                            jsonModified = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.FrameCreation,
                        $"Error applying simple migration to frame: {ex.Message}");
                }
            }

            if (jsonModified)
            {
                SaveFrameData();
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.FrameCreation,
                    "Applied simple migrations and saved updated frame data");
            }
        }

        /// <summary>
        /// Adds missing basic properties with defaults
        /// Used by: ApplySimpleMigrations
        /// Category: Property Validation
        /// </summary>
        private static bool AddMissingBasicProperties(IDictionary<string, object> frameDict)
        {
            bool modified = false;

            if (!frameDict.ContainsKey("Id") || string.IsNullOrEmpty(frameDict["Id"]?.ToString()))
            {
                frameDict["Id"] = Guid.NewGuid().ToString();
                modified = true;
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.FrameCreation,
                    $"Added missing ID to frame '{(frameDict.ContainsKey("Title") ? frameDict["Title"] : "Unknown")}'");
            }

            if (!frameDict.ContainsKey("TabsEnabled")) { frameDict["TabsEnabled"] = "false"; modified = true; }
            if (!frameDict.ContainsKey("CurrentTab")) { frameDict["CurrentTab"] = 0; modified = true; }
            if (!frameDict.ContainsKey("Tabs")) { frameDict["Tabs"] = new JArray(); modified = true; }
            if (!frameDict.ContainsKey("IsHidden")) { frameDict["IsHidden"] = "false"; modified = true; }
            if (!frameDict.ContainsKey("IsRolled")) { frameDict["IsRolled"] = "false"; modified = true; }

            return modified;
        }

        /// <summary>
        /// Validates and fixes data types for consistency
        /// Used by: ApplySimpleMigrations
        /// Category: Data Validation
        /// </summary>
        public static bool ValidateDataTypes(IDictionary<string, object> frameDict)
        {
            bool modified = false;

            if (frameDict.ContainsKey("UnrolledHeight"))
            {
                if (!TryGetDouble(frameDict, "UnrolledHeight", out double unrolledHeight) || unrolledHeight <= 0)
                {
                    double defaultHeight = TryGetDouble(frameDict, "Height", out double h) ? h : 130;
                    frameDict["UnrolledHeight"] = defaultHeight.ToString(CultureInfo.InvariantCulture);
                    modified = true;
                }
            }

            if (!TryGetDouble(frameDict, "Width", out double width) || width <= 0)
            {
                frameDict["Width"] = 230;
                modified = true;
            }

            if (!TryGetDouble(frameDict, "Height", out double height) || height <= 0)
            {
                frameDict["Height"] = 130;
                modified = true;
            }

            return modified;
        }

        /// <summary>
        /// Culture-safe numeric read: boxed numerics convert directly; strings parse with
        /// InvariantCulture (the format the JSON is persisted in), so comma-decimal locales
        /// can never misread "130.5" as 1305 or reset a valid value.
        /// </summary>
        private static bool TryGetDouble(IDictionary<string, object> frameDict, string key, out double value)
        {
            value = 0;
            if (!frameDict.TryGetValue(key, out object raw) || raw == null) return false;

            try
            {
                value = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Applies format consistency for JSON serialization
        /// Used by: SaveFrameData
        /// Category: Format Consistency
        /// </summary>
        private static void ApplyFormatConsistency(IDictionary<string, object> frameDict)
        {
            if (frameDict.ContainsKey("IsHidden"))
            {
                bool isHidden = false;
                if (frameDict["IsHidden"] is bool boolValue) isHidden = boolValue;
                else if (frameDict["IsHidden"] is string stringValue) isHidden = stringValue.ToLower() == "true";
                frameDict["IsHidden"] = isHidden.ToString().ToLower();
            }

            if (frameDict.ContainsKey("IsRolled"))
            {
                bool isRolled = false;
                if (frameDict["IsRolled"] is bool boolValue) isRolled = boolValue;
                else if (frameDict["IsRolled"] is string stringValue) isRolled = stringValue.ToLower() == "true";
                frameDict["IsRolled"] = isRolled.ToString().ToLower();
            }
        }

        /// <summary>
        /// Applies default properties to new frame
        /// Used by: CreateNewFrame
        /// Category: Frame Defaults
        /// </summary>
        private static void ApplyFrameDefaults(IDictionary<string, object> frameDict,
             string customColor, string customLaunchEffect)
        {
            frameDict["IsHidden"] = "false";
            frameDict["IsRolled"] = "false";
            frameDict["UnrolledHeight"] = Convert.ToString(frameDict["Height"], CultureInfo.InvariantCulture);
            frameDict["TabsEnabled"] = "false";
            frameDict["CurrentTab"] = 0;
            frameDict["Tabs"] = new JArray();

            if (!string.IsNullOrEmpty(customColor)) frameDict["CustomColor"] = customColor;
            if (!string.IsNullOrEmpty(customLaunchEffect)) frameDict["CustomLaunchEffect"] = customLaunchEffect;
        }
        #endregion

        #region Data Access Helpers - Used by: Various managers
        /// <summary>
        /// Finds frame by ID with null safety
        /// Used by: Framemanager, CustomizeFrameForm, various operations
        /// Category: Data Access
        /// </summary>
        public static dynamic FindFrameById(string frameId)
        {
            if (string.IsNullOrEmpty(frameId) || _frameData == null)
                return null;

            return _frameData.FirstOrDefault(f => f.Id?.ToString() == frameId);
        }


        #endregion
    }
}