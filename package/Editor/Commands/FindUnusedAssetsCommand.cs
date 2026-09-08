using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityBridge.Models;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace UnityBridge.Commands {
    /// <summary>
    /// Finds project assets that are unreachable from entry-point roots (enabled build
    /// scenes + Resources folders). Script/code assets (.cs/.asmdef/.asmref) are excluded
    /// because their usage is code-level, not asset-graph-level. Read-only.
    /// </summary>
    public class FindUnusedAssetsCommand : ICommand {
        private static readonly HashSet<string> CodeExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            ".cs", ".asmdef", ".asmref"
        };

        public void Execute(CommandRequest request, Action<CommandResponse> onProgress, Action<CommandResponse> onComplete) {
            var stopwatch = Stopwatch.StartNew();

            bool includePackages = AssetAnalysisUtil.TryParseBool(request.@params?.includePackages, false);

#if DEBUG
            Debug.Log($"{AgentsBridge.LogPrefix} find-unused-assets: scanning (includePackages: {includePackages})");
#endif

            try {
                // 1. Entry-point roots: enabled build scenes + everything under a Resources/ folder.
                var roots = new List<string>();
                foreach (var scene in EditorBuildSettings.scenes) {
                    if (scene.enabled && !string.IsNullOrEmpty(scene.path)) {
                        roots.Add(scene.path);
                    }
                }

                var allPaths = AssetDatabase.GetAllAssetPaths();
                foreach (var path in allPaths) {
                    if (path.Contains("/Resources/")) {
                        roots.Add(path);
                    }
                }

                var distinctRoots = roots.Where(r => !string.IsNullOrEmpty(r)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                // 2. Reachability closure over recursive dependencies.
                var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var queue = new Queue<string>(distinctRoots);
                while (queue.Count > 0) {
                    var path = queue.Dequeue();
                    if (!reachable.Add(path)) {
                        continue;
                    }

                    string[] deps;
                    try {
                        deps = AssetDatabase.GetDependencies(path, true);
                    }
                    catch {
                        continue;
                    }

                    foreach (var dep in deps) {
                        if (!reachable.Contains(dep)) {
                            queue.Enqueue(dep);
                        }
                    }
                }

                // 3. Unused = candidate project assets not in the reachable set.
                var unused = new List<string>();
                int totalAssets = 0;
                foreach (var path in allPaths) {
                    if (!IsCandidate(path, includePackages)) {
                        continue;
                    }

                    totalAssets++;
                    if (!reachable.Contains(path)) {
                        unused.Add(path);
                    }
                }

                unused.Sort(StringComparer.OrdinalIgnoreCase);
                stopwatch.Stop();

                var result = new UnusedAssetsResult {
                    unusedAssets = unused,
                    totalAssets = totalAssets,
                    unusedCount = unused.Count,
                    roots = distinctRoots
                };

                var response = CommandResponse.Success(request.id, request.action, stopwatch.ElapsedMilliseconds);
                response.unusedAssets = result;
                onComplete?.Invoke(response);
            }
            catch (Exception e) {
                stopwatch.Stop();
                Debug.LogError($"{AgentsBridge.LogPrefix} find-unused-assets failed: {e.Message}");
                onComplete?.Invoke(CommandResponse.Error(request.id, request.action, e.Message));
            }
        }

        private static bool IsCandidate(string path, bool includePackages) {
            if (string.IsNullOrEmpty(path)) {
                return false;
            }

            if (!path.StartsWith("Assets/")) {
                if (!(includePackages && path.StartsWith("Packages/"))) {
                    return false;
                }
            }

            if (AssetDatabase.IsValidFolder(path)) {
                return false;
            }

            if (CodeExtensions.Contains(Path.GetExtension(path))) {
                return false;
            }

            return true;
        }
    }
}
