using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using UnityBridge.Models;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace UnityBridge.Commands {
    /// <summary>
    /// Dumps Inspector-visible serialized field values of a Unity asset (prefab, asset,
    /// or scene) into a structured hierarchy. Uses SerializedObject / SerializedProperty,
    /// so no text serialization is required — the output always reflects Unity's live data.
    /// Read-only: safe to run while Unity is compiling.
    /// </summary>
    public class DumpAssetCommand : ICommand {
        public void Execute(CommandRequest request, Action<CommandResponse> onProgress, Action<CommandResponse> onComplete) {
            var stopwatch = Stopwatch.StartNew();

            var asset = request.@params?.asset;
            if (string.IsNullOrEmpty(asset)) {
                stopwatch.Stop();
                onComplete?.Invoke(CommandResponse.Error(request.id, request.action,
                    "Missing required parameter: asset (an asset path)."));
                return;
            }

            string ext = Path.GetExtension(asset).ToLowerInvariant();

#if DEBUG
            Debug.Log($"{AgentsBridge.LogPrefix} dump-asset: {asset}");
#endif

            try {
                AssetDumpResult result;

                if (ext == ".prefab") {
                    result = DumpPrefab(asset);
                }
                else if (ext == ".asset") {
                    result = DumpAsset(asset);
                }
                else if (ext == ".unity") {
                    result = DumpScene(asset);
                }
                else {
                    stopwatch.Stop();
                    onComplete?.Invoke(CommandResponse.Error(request.id, request.action,
                        $"Unsupported asset type '{ext}'. Must be .prefab, .asset, or .unity."));
                    return;
                }

                stopwatch.Stop();
                var response = CommandResponse.Success(request.id, request.action, stopwatch.ElapsedMilliseconds);
                response.assetDump = result;
                onComplete?.Invoke(response);
            }
            catch (Exception e) {
                stopwatch.Stop();
                Debug.LogError($"{AgentsBridge.LogPrefix} dump-asset failed: {e.Message}");
                onComplete?.Invoke(CommandResponse.Error(request.id, request.action, e.Message));
            }
        }

        #region dispatch

        private static AssetDumpResult DumpPrefab(string path) {
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            if (contents == null) {
                throw new InvalidOperationException($"Failed to load prefab contents from '{path}'.");
            }

            try {
                var gameObjects = new List<GameObjectDump>();
                DumpGameObjectTree(contents.transform, "", gameObjects);

                return new AssetDumpResult {
                    asset = path,
                    assetType = "prefab",
                    rootName = contents.name,
                    gameObjectCount = gameObjects.Count,
                    gameObjects = gameObjects
                };
            }
            finally {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static AssetDumpResult DumpAsset(string path) {
            UnityEngine.Object obj = AssetDatabase.LoadMainAssetAtPath(path);
            if (obj == null) {
                throw new InvalidOperationException($"No asset found at path '{path}'.");
            }

            var components = new List<ComponentDump>();
            string displayName;
            var fields = ExtractSerializedFields(obj, out displayName);
            if (fields.Count > 0) {
                components.Add(new ComponentDump { type = displayName, fields = fields });
            }

            return new AssetDumpResult {
                asset = path,
                assetType = "asset",
                rootName = obj.name,
                components = components
            };
        }

        private static AssetDumpResult DumpScene(string path) {
            Scene scene = SceneManager.GetActiveScene();
            if (!string.Equals(scene.path, path, StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidOperationException(
                    $"Scene '{path}' is not open. The active scene is '{scene.path}'. Open the scene first, or pass '{scene.path}'.");
            }

            var gameObjects = new List<GameObjectDump>();
            foreach (GameObject root in scene.GetRootGameObjects()) {
                DumpGameObjectTree(root.transform, "", gameObjects);
            }

            return new AssetDumpResult {
                asset = path,
                assetType = "scene",
                rootName = scene.name,
                gameObjectCount = gameObjects.Count,
                gameObjects = gameObjects
            };
        }

        #endregion

        #region traversal

        private static void DumpGameObjectTree(Transform transform, string parentPath, List<GameObjectDump> result) {
            if (transform == null) {
                return;
            }

            GameObject go = transform.gameObject;
            string path = string.IsNullOrEmpty(parentPath) ? go.name : parentPath + "/" + go.name;

            var dump = new GameObjectDump {
                name = go.name,
                path = path,
                active = go.activeSelf,
                components = new List<ComponentDump>()
            };

            foreach (Component component in go.GetComponents<Component>()) {
                if (component == null) {
                    continue;
                }
                string displayName;
                var fields = ExtractSerializedFields(component, out displayName);
                if (fields.Count == 0) {
                    continue;
                }
                dump.components.Add(new ComponentDump { type = displayName, fields = fields });
            }

            result.Add(dump);

            for (int i = 0; i < transform.childCount; i++) {
                DumpGameObjectTree(transform.GetChild(i), path, result);
            }
        }

        #endregion

        #region serialized field extraction

        /// <summary>
        /// Extracts Inspector-visible serialized fields from any UnityEngine.Object via
        /// SerializedObject. SerializedProperty.NextVisible only visits fields Unity shows
        /// in the Inspector, which naturally filters internal fields (m_ObjectHideFlags, etc.).
        /// </summary>
        private static List<FieldDump> ExtractSerializedFields(UnityEngine.Object obj, out string displayName) {
            var fields = new List<FieldDump>();
            displayName = obj.GetType().Name;

            SerializedObject so = new SerializedObject(obj);

            // Resolve MonoBehaviour/ScriptableObject script name from m_Script GUID.
            SerializedProperty scriptProp = so.FindProperty("m_Script");
            if (scriptProp != null && scriptProp.objectReferenceValue != null) {
                string scriptPath = AssetDatabase.GetAssetPath(scriptProp.objectReferenceValue);
                if (!string.IsNullOrEmpty(scriptPath)) {
                    displayName = Path.GetFileNameWithoutExtension(scriptPath);
                }
            }

            SerializedProperty it = so.GetIterator();
            if (it.NextVisible(true)) {
                do {
                    // m_Script is used as the component display name, not a field.
                    if (it.name == "m_Script") {
                        continue;
                    }

                    string name = CleanFieldName(it.name);
                    string value = ExtractSerializedValue(it);
                    if (value == null) {
                        continue; // Nested generic object without a direct value.
                    }

                    fields.Add(new FieldDump { name = name, value = value });
                } while (it.NextVisible(false));
            }

            return fields;
        }

        /// <summary>
        /// Extracts a human-readable value string from a SerializedProperty leaf.
        /// Returns null for composite generic properties (nested objects) that have no
        /// single value to report.
        /// </summary>
        private static string ExtractSerializedValue(SerializedProperty prop) {
            switch (prop.propertyType) {
                case SerializedPropertyType.Integer:
                    return prop.intValue.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.Float:
                    return prop.floatValue.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.String:
                    return prop.stringValue;
                case SerializedPropertyType.Boolean:
                    return prop.boolValue ? "true" : "false";
                case SerializedPropertyType.Enum:
                    int idx = prop.enumValueIndex;
                    var names = prop.enumDisplayNames;
                    return (names != null && idx >= 0 && idx < names.Length) ? names[idx] : prop.intValue.ToString();
                case SerializedPropertyType.Color:
                    Color c = prop.colorValue;
                    return $"({c.r}, {c.g}, {c.b}, {c.a})";
                case SerializedPropertyType.Vector2:
                    Vector2 v2 = prop.vector2Value;
                    return $"({v2.x}, {v2.y})";
                case SerializedPropertyType.Vector3:
                    Vector3 v3 = prop.vector3Value;
                    return $"({v3.x}, {v3.y}, {v3.z})";
                case SerializedPropertyType.Vector4:
                    Vector4 v4 = prop.vector4Value;
                    return $"({v4.x}, {v4.y}, {v4.z}, {v4.w})";
                case SerializedPropertyType.Quaternion:
                    Quaternion q = prop.quaternionValue;
                    return $"({q.x}, {q.y}, {q.z}, {q.w})";
                case SerializedPropertyType.Rect:
                    Rect r = prop.rectValue;
                    return $"({r.x}, {r.y}, {r.width}, {r.height})";
                case SerializedPropertyType.RectInt:
                    RectInt ri = prop.rectIntValue;
                    return $"({ri.x}, {ri.y}, {ri.width}, {ri.height})";
                case SerializedPropertyType.Bounds:
                    Bounds b = prop.boundsValue;
                    return $"center=({b.center.x}, {b.center.y}, {b.center.z}), size=({b.size.x}, {b.size.y}, {b.size.z})";
                case SerializedPropertyType.BoundsInt:
                    BoundsInt bi = prop.boundsIntValue;
                    return $"position=({bi.position.x}, {bi.position.y}, {bi.position.z}), size=({bi.size.x}, {bi.size.y}, {bi.size.z})";
                case SerializedPropertyType.ArraySize:
                    return prop.arraySize.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.Character:
                    return ((char)prop.intValue).ToString();
                case SerializedPropertyType.LayerMask:
                    return prop.intValue.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.AnimationCurve:
                    return "AnimationCurve";
                case SerializedPropertyType.Gradient:
                    return "Gradient";
                case SerializedPropertyType.ObjectReference:
                    UnityEngine.Object refObj = prop.objectReferenceValue;
                    if (refObj == null) {
                        return "null";
                    }
                    string refPath = AssetDatabase.GetAssetPath(refObj);
                    return string.IsNullOrEmpty(refPath) ? refObj.name : refPath;
                case SerializedPropertyType.Generic:
                    // Arrays report their size; other nested objects are skipped.
                    if (prop.isArray) {
                        return $"[{prop.arraySize} items]";
                    }
                    return null;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Converts Unity's serialized field name (m_LocalPosition) to a clean
        /// Inspector-like name (localPosition).
        /// </summary>
        private static string CleanFieldName(string name) {
            if (string.IsNullOrEmpty(name)) {
                return name;
            }
            if (name.StartsWith("m_", StringComparison.Ordinal)) {
                name = name.Substring(2);
            }
            if (name.Length > 0) {
                name = char.ToLowerInvariant(name[0]) + name.Substring(1);
            }
            return name;
        }

        #endregion
    }
}
