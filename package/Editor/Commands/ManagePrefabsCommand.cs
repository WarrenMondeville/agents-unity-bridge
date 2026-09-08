using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityBridge.Models;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace UnityBridge.Commands {
    /// <summary>
    /// Manages Unity prefabs: inspect metadata, dump the hierarchy, and create a
    /// prefab asset from a scene GameObject. Uses Unity's PrefabUtility directly.
    /// get-info / get-hierarchy are read-only; create mutates the asset database.
    /// </summary>
    public class ManagePrefabsCommand : ICommand {
        public void Execute(CommandRequest request, Action<CommandResponse> onProgress, Action<CommandResponse> onComplete) {
            var stopwatch = Stopwatch.StartNew();
            var action = (request.@params?.prefabAction ?? string.Empty).ToLowerInvariant();

#if DEBUG
            Debug.Log($"{AgentsBridge.LogPrefix} manage-prefabs: {action}");
#endif

            try {
                switch (action) {
                    case "get-info":
                        GetInfo(request, stopwatch, onComplete);
                        break;
                    case "get-hierarchy":
                        GetHierarchy(request, stopwatch, onComplete);
                        break;
                    case "create":
                        CreatePrefab(request, stopwatch, onComplete);
                        break;
                    default:
                        stopwatch.Stop();
                        onComplete?.Invoke(CommandResponse.Error(request.id, request.action,
                            $"Unknown or missing prefabAction '{action}'. Valid actions: get-info, get-hierarchy, create."));
                        break;
                }
            }
            catch (Exception e) {
                stopwatch.Stop();
                Debug.LogError($"{AgentsBridge.LogPrefix} manage-prefabs '{action}' failed: {e.Message}");
                onComplete?.Invoke(CommandResponse.Error(request.id, request.action, e.Message));
            }
        }

        #region get-info

        private static void GetInfo(CommandRequest request, Stopwatch stopwatch, Action<CommandResponse> onComplete) {
            string path = SanitizePrefabPath(request.@params?.prefabPath);
            if (path == null) {
                stopwatch.Stop();
                onComplete?.Invoke(CommandResponse.Error(request.id, request.action,
                    "Missing or invalid 'prefabPath' parameter."));
                return;
            }

            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefabAsset == null) {
                stopwatch.Stop();
                onComplete?.Invoke(CommandResponse.Error(request.id, request.action,
                    $"No prefab asset found at path '{path}'."));
                return;
            }

            var result = new PrefabResult {
                prefabAction = "get-info",
                prefabPath = path,
                guid = AssetDatabase.AssetPathToGUID(path),
                prefabType = PrefabUtility.GetPrefabAssetType(prefabAsset).ToString(),
                rootObjectName = prefabAsset.name,
                rootComponentTypes = GetComponentTypeNames(prefabAsset),
                childCount = CountChildrenRecursive(prefabAsset.transform)
            };

            if (PrefabUtility.GetPrefabAssetType(prefabAsset) == PrefabAssetType.Variant) {
                GameObject parentAsset = PrefabUtility.GetCorrespondingObjectFromSource(prefabAsset);
                if (parentAsset != null) {
                    result.isVariant = true;
                    result.parentPrefab = AssetDatabase.GetAssetPath(parentAsset);
                }
            }

            stopwatch.Stop();
            var response = CommandResponse.Success(request.id, request.action, stopwatch.ElapsedMilliseconds);
            response.prefabResult = result;
            onComplete?.Invoke(response);
        }

        #endregion

        #region get-hierarchy

        private static void GetHierarchy(CommandRequest request, Stopwatch stopwatch, Action<CommandResponse> onComplete) {
            string path = SanitizePrefabPath(request.@params?.prefabPath);
            if (path == null) {
                stopwatch.Stop();
                onComplete?.Invoke(CommandResponse.Error(request.id, request.action,
                    "Missing or invalid 'prefabPath' parameter."));
                return;
            }

            GameObject prefabContents = PrefabUtility.LoadPrefabContents(path);
            if (prefabContents == null) {
                stopwatch.Stop();
                onComplete?.Invoke(CommandResponse.Error(request.id, request.action,
                    $"Failed to load prefab contents from '{path}'."));
                return;
            }

            try {
                var items = new List<PrefabHierarchyItem>();
                BuildHierarchyItems(prefabContents.transform, prefabContents.transform, path, "", items);

                stopwatch.Stop();
                var result = new PrefabResult {
                    prefabAction = "get-hierarchy",
                    prefabPath = path,
                    total = items.Count,
                    items = items,
                    childCount = CountChildrenRecursive(prefabContents.transform)
                };

                var response = CommandResponse.Success(request.id, request.action, stopwatch.ElapsedMilliseconds);
                response.prefabResult = result;
                onComplete?.Invoke(response);
            }
            finally {
                PrefabUtility.UnloadPrefabContents(prefabContents);
            }
        }

        private static void BuildHierarchyItems(Transform transform, Transform mainRoot, string mainPrefabPath, string parentPath, List<PrefabHierarchyItem> items) {
            if (transform == null) {
                return;
            }

            string name = transform.gameObject.name;
            string path = string.IsNullOrEmpty(parentPath) ? name : parentPath + "/" + name;

            bool isPrefabRoot = transform == mainRoot;
            bool isNestedRoot = PrefabUtility.IsAnyPrefabInstanceRoot(transform.gameObject);
            int nestingDepth = isPrefabRoot ? 0 : GetNestingDepth(transform.gameObject, mainRoot);
            string nestedPath = isNestedRoot ? GetNestedPrefabPath(transform.gameObject) : null;
            string parentPrefabPath = isNestedRoot && !isPrefabRoot ? GetParentPrefabPath(transform.gameObject, mainRoot) : null;

            items.Add(new PrefabHierarchyItem {
                name = name,
                instanceId = transform.gameObject.GetInstanceID(),
                path = path,
                activeSelf = transform.gameObject.activeSelf,
                childCount = transform.childCount,
                componentTypes = GetComponentTypeNames(transform.gameObject),
                isPrefabRoot = isPrefabRoot,
                isNestedRoot = isNestedRoot,
                nestingDepth = nestingDepth,
                assetPath = isNestedRoot ? nestedPath : mainPrefabPath,
                parentPath = parentPrefabPath
            });

            foreach (Transform child in transform) {
                BuildHierarchyItems(child, mainRoot, mainPrefabPath, path, items);
            }
        }

        #endregion

        #region create

        private static void CreatePrefab(CommandRequest request, Stopwatch stopwatch, Action<CommandResponse> onComplete) {
            string objectName = request.@params?.objectName ?? request.@params?.target;
            if (string.IsNullOrEmpty(objectName)) {
                stopwatch.Stop();
                onComplete?.Invoke(CommandResponse.Error(request.id, request.action,
                    "Missing required parameter: 'objectName' (the scene GameObject name)."));
                return;
            }

            string path = SanitizePrefabPath(request.@params?.prefabPath);
            if (path == null) {
                stopwatch.Stop();
                onComplete?.Invoke(CommandResponse.Error(request.id, request.action,
                    "Missing or invalid 'prefabPath' parameter."));
                return;
            }

            bool includeInactive = ParseBool(request.@params?.searchInactive, false);
            bool replaceExisting = ParseBool(request.@params?.allowOverwrite, false);
            bool unlinkIfInstance = ParseBool(request.@params?.unlinkIfInstance, false);

            GameObject source = FindSceneObjectByName(objectName, includeInactive);
            if (source == null) {
                stopwatch.Stop();
                onComplete?.Invoke(CommandResponse.Error(request.id, request.action,
                    $"GameObject '{objectName}' not found in the active scene or prefab stage."));
                return;
            }

            // Reject objects that are part of a prefab asset (the .prefab file itself).
            if (PrefabUtility.IsPartOfPrefabAsset(source)) {
                stopwatch.Stop();
                onComplete?.Invoke(CommandResponse.Error(request.id, request.action,
                    $"GameObject '{source.name}' is part of a prefab asset. Open the prefab stage to save changes instead."));
                return;
            }

            // Handle existing prefab instances.
            PrefabInstanceStatus status = PrefabUtility.GetPrefabInstanceStatus(source);
            bool shouldUnlink = false;
            if (status != PrefabInstanceStatus.NotAPrefab) {
                string existingPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(source);
                if (!unlinkIfInstance) {
                    stopwatch.Stop();
                    onComplete?.Invoke(CommandResponse.Error(request.id, request.action,
                        $"GameObject '{source.name}' is already linked to prefab '{existingPath}'. Set 'unlinkIfInstance' to true to unlink it first."));
                    return;
                }
                shouldUnlink = true;
            }

            bool fileExistedAtPath = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) != null;
            if (!replaceExisting && fileExistedAtPath) {
                path = AssetDatabase.GenerateUniqueAssetPath(path);
            }

            EnsureAssetDirectoryExists(path);

            if (shouldUnlink) {
                GameObject rootToUnlink = PrefabUtility.GetOutermostPrefabInstanceRoot(source);
                if (rootToUnlink != null) {
                    PrefabUtility.UnpackPrefabInstance(rootToUnlink, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                }
            }

            GameObject result = PrefabUtility.SaveAsPrefabAssetAndConnect(source, path, InteractionMode.AutomatedAction);
            if (result == null) {
                stopwatch.Stop();
                onComplete?.Invoke(CommandResponse.Error(request.id, request.action,
                    $"Failed to create prefab asset at '{path}'."));
                return;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeGameObject = result;

            stopwatch.Stop();
            var prefabResult = new PrefabResult {
                prefabAction = "create",
                prefabPath = path,
                rootObjectName = result.name,
                instanceId = result.GetInstanceID(),
                instanceName = result.name,
                componentCount = result.GetComponents<Component>().Length,
                childCount = result.transform.childCount,
                wasUnlinked = shouldUnlink,
                wasReplaced = replaceExisting && fileExistedAtPath
            };

            var response = CommandResponse.Success(request.id, request.action, stopwatch.ElapsedMilliseconds);
            response.prefabResult = prefabResult;
            onComplete?.Invoke(response);
        }

        #endregion

        #region helpers

        /// <summary>
        /// Normalizes a prefab path, rejects traversal sequences, and ensures the
        /// ".prefab" extension. Returns null on invalid input.
        /// </summary>
        private static string SanitizePrefabPath(string path) {
            if (string.IsNullOrEmpty(path)) {
                return null;
            }

            path = path.Replace('\\', '/');
            if (path.Contains("..")) {
                return null;
            }

            if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) {
                if (string.Equals(path, "Assets", StringComparison.OrdinalIgnoreCase)) {
                    return null;
                }
                path = "Assets/" + path.TrimStart('/');
            }

            if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) {
                path += ".prefab";
            }

            return path;
        }

        private static bool ParseBool(string value, bool defaultValue) {
            if (string.IsNullOrEmpty(value)) {
                return defaultValue;
            }
            bool result;
            return bool.TryParse(value, out result) ? result : defaultValue;
        }

        private static List<string> GetComponentTypeNames(GameObject obj) {
            var names = new List<string>();
            if (obj == null) {
                return names;
            }
            foreach (var component in obj.GetComponents<Component>()) {
                if (component != null) {
                    names.Add(component.GetType().FullName);
                }
            }
            return names;
        }

        private static int CountChildrenRecursive(Transform transform) {
            if (transform == null) {
                return 0;
            }
            int count = transform.childCount;
            for (int i = 0; i < transform.childCount; i++) {
                count += CountChildrenRecursive(transform.GetChild(i));
            }
            return count;
        }

        private static int GetNestingDepth(GameObject gameObject, Transform mainRoot) {
            if (gameObject == null || gameObject.transform == mainRoot) {
                return 0;
            }
            if (!PrefabUtility.IsAnyPrefabInstanceRoot(gameObject)) {
                return -1;
            }
            int depth = 0;
            Transform current = gameObject.transform;
            while (current != null && current != mainRoot) {
                if (PrefabUtility.IsAnyPrefabInstanceRoot(current.gameObject)) {
                    depth++;
                }
                current = current.parent;
            }
            return depth;
        }

        private static string GetNestedPrefabPath(GameObject gameObject) {
            if (gameObject == null || !PrefabUtility.IsAnyPrefabInstanceRoot(gameObject)) {
                return null;
            }
            var sourcePrefab = PrefabUtility.GetCorrespondingObjectFromSource(gameObject);
            return sourcePrefab != null ? AssetDatabase.GetAssetPath(sourcePrefab) : null;
        }

        private static string GetParentPrefabPath(GameObject gameObject, Transform mainRoot) {
            if (gameObject == null || gameObject.transform == mainRoot) {
                return null;
            }
            if (!PrefabUtility.IsAnyPrefabInstanceRoot(gameObject)) {
                return null;
            }
            Transform current = gameObject.transform.parent;
            while (current != null && current != mainRoot) {
                if (PrefabUtility.IsAnyPrefabInstanceRoot(current.gameObject)) {
                    return GetNestedPrefabPath(current.gameObject);
                }
                current = current.parent;
            }
            return mainRoot != null ? AssetDatabase.GetAssetPath(mainRoot.gameObject) : null;
        }

        private static GameObject FindSceneObjectByName(string name, bool includeInactive) {
            // Prefab stage first.
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.prefabContentsRoot != null) {
                foreach (Transform transform in stage.prefabContentsRoot.GetComponentsInChildren<Transform>(includeInactive)) {
                    if (transform.name == name && (includeInactive || transform.gameObject.activeSelf)) {
                        return transform.gameObject;
                    }
                }
            }

            // Active scene.
            Scene activeScene = SceneManager.GetActiveScene();
            foreach (GameObject root in activeScene.GetRootGameObjects()) {
                if (root.name == name && (includeInactive || root.activeSelf)) {
                    return root;
                }
                foreach (Transform transform in root.GetComponentsInChildren<Transform>(includeInactive)) {
                    if (transform.name == name && (includeInactive || transform.gameObject.activeSelf)) {
                        return transform.gameObject;
                    }
                }
            }

            return null;
        }

        private static void EnsureAssetDirectoryExists(string assetPath) {
            string directory = Path.GetDirectoryName(assetPath);
            if (string.IsNullOrEmpty(directory)) {
                return;
            }
            string fullDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", directory));
            if (!Directory.Exists(fullDirectory)) {
                Directory.CreateDirectory(fullDirectory);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
        }

        #endregion
    }
}
