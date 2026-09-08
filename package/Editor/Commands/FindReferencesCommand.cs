using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityBridge.Models;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace UnityBridge.Commands {
    /// <summary>
    /// Finds the assets that directly reference a given asset (reverse dependency edges).
    /// Unity exposes no reverse index, so this scans all project assets. Read-only.
    /// </summary>
    public class FindReferencesCommand : ICommand {
        public void Execute(CommandRequest request, Action<CommandResponse> onProgress, Action<CommandResponse> onComplete) {
            var stopwatch = Stopwatch.StartNew();

            var asset = AssetAnalysisUtil.ResolveAssetPath(request.@params?.asset);
            if (string.IsNullOrEmpty(asset)) {
                onComplete?.Invoke(CommandResponse.Error(request.id, request.action,
                    "Missing required parameter: asset (an asset path or GUID)."));
                return;
            }

            if (!AssetAnalysisUtil.IsValidAssetPath(asset)) {
                onComplete?.Invoke(CommandResponse.Error(request.id, request.action,
                    $"Asset not found: {asset}"));
                return;
            }

            bool includePackages = AssetAnalysisUtil.TryParseBool(request.@params?.includePackages, false);

#if DEBUG
            Debug.Log($"{AgentsBridge.LogPrefix} find-references: {asset} (includePackages: {includePackages})");
#endif

            try {
                var allPaths = AssetDatabase.GetAllAssetPaths();
                var references = new List<string>();
                int current = 0;

                foreach (var path in allPaths) {
                    current++;
                    if (current % 200 == 0) {
                        var progress = CommandResponse.Running(request.id, request.action);
                        progress.progress = new ProgressInfo { current = current, total = allPaths.Length };
                        onProgress?.Invoke(progress);
                    }

                    if (path == asset) {
                        continue;
                    }

                    if (!path.StartsWith("Assets/") && !(includePackages && path.StartsWith("Packages/"))) {
                        continue;
                    }

                    string[] deps;
                    try {
                        deps = AssetDatabase.GetDependencies(path, false);
                    }
                    catch {
                        continue;
                    }

                    if (Array.IndexOf(deps, asset) >= 0) {
                        references.Add(path);
                    }
                }

                references.Sort(StringComparer.OrdinalIgnoreCase);
                stopwatch.Stop();

                var result = new AssetReferencesResult {
                    asset = asset,
                    references = references,
                    count = references.Count
                };

                var response = CommandResponse.Success(request.id, request.action, stopwatch.ElapsedMilliseconds);
                response.assetReferences = result;
                onComplete?.Invoke(response);
            }
            catch (Exception e) {
                stopwatch.Stop();
                Debug.LogError($"{AgentsBridge.LogPrefix} find-references failed: {e.Message}");
                onComplete?.Invoke(CommandResponse.Error(request.id, request.action, e.Message));
            }
        }
    }
}
