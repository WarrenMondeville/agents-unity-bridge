using System;
using System.Collections.Generic;

namespace UnityBridge.Models {
    [Serializable]
    public class CommandResponse {
        public string id;
        public string status;
        public string action;
        public long duration_ms;
        public TestResult result;
        public ProgressInfo progress;
        public List<TestFailure> failures;
        public string error;
        public List<ConsoleLogEntry> consoleLogs;
        public EditorStatus editorStatus;
        public BuildInfo buildInfo;

        // Asset dependency analysis results
        public AssetDependencyResult assetDependencies;
        public AssetReferencesResult assetReferences;
        public UnusedAssetsResult unusedAssets;
        public TracePathResult tracePath;
        public AssetSearchResult searchResult;
        public AssetInfo assetInfo;

        // Prefab management result
        public PrefabResult prefabResult;

        // Asset inspector dump result
        public AssetDumpResult assetDump;

        public static CommandResponse Running(string id, string action) {
            return new CommandResponse {
                id = id,
                status = "running",
                action = action,
                progress = new ProgressInfo { current = 0, total = 0 }
            };
        }

        public static CommandResponse Success(string id, string action, long durationMs) {
            return new CommandResponse {
                id = id,
                status = "success",
                action = action,
                duration_ms = durationMs
            };
        }

        public static CommandResponse Failure(string id, string action, long durationMs, string error = null) {
            return new CommandResponse {
                id = id,
                status = "failure",
                action = action,
                duration_ms = durationMs,
                error = error
            };
        }

        public static CommandResponse Error(string id, string action, string error) {
            return new CommandResponse {
                id = id,
                status = "error",
                action = action,
                error = error
            };
        }
    }

    [Serializable]
    public class TestResult {
        public int passed;
        public int failed;
        public int skipped;
        public List<TestFailure> failures;
    }

    [Serializable]
    public class TestFailure {
        public string name;
        public string message;
    }

    [Serializable]
    public class ProgressInfo {
        public int current;
        public int total;
        public string currentTest;
    }

    [Serializable]
    public class ConsoleLogEntry {
        public string message;
        public string stackTrace;
        public string type; // "Log", "Warning", "Error"
        public int count; // For collapsed duplicates
    }

    [Serializable]
    public class EditorStatus {
        public bool isCompiling;
        public bool isUpdating;
        public bool isPlaying;
        public bool isPaused;
    }

    [Serializable]
    public class BuildInfo {
        public string buildResult;     // "Succeeded", "Failed", "Cancelled", "Unknown"
        public int totalErrors;
        public int totalWarnings;
        public float totalSeconds;     // Build duration from BuildReport
        public string outputPath;
        public long sizeBytes;         // Total build size
        public string method;          // "direct" or the invoked method name
    }

    [Serializable]
    public class AssetDependencyResult {
        public string asset;              // The queried asset path
        public List<string> dependencies; // Paths this asset depends on (sorted, input excluded)
        public int count;                 // Number of dependencies
        public bool recursive;            // Whether transitive dependencies were included
    }

    [Serializable]
    public class AssetReferencesResult {
        public string asset;              // The queried asset path
        public List<string> references;   // Paths that directly reference this asset (sorted)
        public int count;                 // Number of direct referencers
    }

    [Serializable]
    public class UnusedAssetsResult {
        public List<string> unusedAssets; // Asset paths nothing reachable references (sorted)
        public int totalAssets;           // Number of candidate assets scanned
        public int unusedCount;           // Number of unused assets
        public List<string> roots;        // Roots used for reachability (build scenes + Resources)
    }

    [Serializable]
    public class TracePathResult {
        public string from;               // Start asset path
        public string to;                 // End asset path
        public List<string> path;         // Ordered dependency path from -> to (empty if not found)
        public int depth;                 // Number of edges in the path
        public bool found;                // Whether a path was found
    }

    [Serializable]
    public class AssetSearchResult {
        public string query;              // The search query used
        public List<string> results;      // Matching asset paths (sorted, capped at limit)
        public int count;                 // Number of results returned
    }

    [Serializable]
    public class AssetInfo {
        public string path;               // Asset path
        public string guid;               // Asset GUID
        public string type;               // Main asset C# type full name
        public long sizeBytes;            // On-disk file size
        public int directDependencyCount; // Direct dependencies only
        public int dependencyCount;       // Transitive (recursive) dependencies
    }

    [Serializable]
    public class PrefabResult {
        public string prefabAction;           // Sub-action that produced this result
        public string prefabPath;             // Prefab asset path
        public string guid;                   // Prefab GUID (get-info)
        public string prefabType;             // PrefabAssetType name (get-info)
        public string rootObjectName;         // Root GameObject name (get-info / create)
        public List<string> rootComponentTypes; // Root component type names (get-info)
        public int childCount;                // Total child count (get-info / get-hierarchy)
        public bool isVariant;                // Whether it's a prefab variant (get-info)
        public string parentPrefab;           // Variant parent asset path (get-info)
        public int total;                     // Hierarchy item count (get-hierarchy)
        public List<PrefabHierarchyItem> items; // Hierarchy items (get-hierarchy)
        public int instanceId;                // Created instance ID (create)
        public string instanceName;           // Created instance name (create)
        public int componentCount;            // Component count on created prefab (create)
        public bool wasUnlinked;              // Whether an instance was unlinked (create)
        public bool wasReplaced;              // Whether an existing prefab was replaced (create)
        public string message;                // Human-readable result message
    }

    [Serializable]
    public class PrefabHierarchyItem {
        public string name;                   // GameObject name
        public int instanceId;                // GameObject instance ID
        public string path;                   // Full hierarchy path ("Root/Child/Grandchild")
        public bool activeSelf;               // Whether the GameObject is active
        public int childCount;                // Direct child count
        public List<string> componentTypes;   // Component type names
        public bool isPrefabRoot;             // Whether this is the main prefab root
        public bool isNestedRoot;             // Whether this is a nested prefab instance root
        public int nestingDepth;              // Prefab nesting depth (0 = main root)
        public string assetPath;              // Source prefab asset path
        public string parentPath;             // Parent prefab asset path (nested)
    }

    [Serializable]
    public class AssetDumpResult {
        public string asset;                      // The queried asset path
        public string assetType;                  // "prefab" | "asset" | "scene"
        public string rootName;                   // Root object name
        public int gameObjectCount;               // Total GameObject count (prefab/scene)
        public List<GameObjectDump> gameObjects;  // Hierarchy (prefab/scene)
        public List<ComponentDump> components;    // Root object components (asset)
        public string message;                    // Human-readable result message
    }

    [Serializable]
    public class GameObjectDump {
        public string name;                       // GameObject name
        public string path;                       // Full hierarchy path
        public bool active;                       // Whether activeSelf
        public List<ComponentDump> components;    // Inspector-visible components
    }

    [Serializable]
    public class ComponentDump {
        public string type;                       // Component type name (script name for MonoBehaviour)
        public List<FieldDump> fields;            // Inspector-visible serialized fields
    }

    [Serializable]
    public class FieldDump {
        public string name;                       // Cleaned field name ("localPosition")
        public string value;                      // Formatted value ("(27.13, -6.38, 0)")
    }
}
