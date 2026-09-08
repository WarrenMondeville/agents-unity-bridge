using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityBridge.Models;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace UnityBridge.Commands {
    /// <summary>
    /// Returns identity and dependency metrics for a single asset. Read-only.
    /// </summary>
    public class GetAssetInfoCommand : ICommand {
        public void Execute(CommandRequest request, Action<CommandResponse> onProgress, Action<CommandResponse> onComplete) {
            var stopwatch = Stopwatch.StartNew();

            var asset = AssetAnalysisUtil.ResolveAssetPath(request.@params?.asset);
            if (string.IsNullOrEmpty(asset)) {
                onComplete?.Invoke(CommandResponse.Error(request.id, request.action,
                    "Missing required parameter: asset (an asset path or GUID)."));
                return;
            }

            string guid = AssetDatabase.AssetPathToGUID(asset);
            if (string.IsNullOrEmpty(guid)) {
                onComplete?.Invoke(CommandResponse.Error(request.id, request.action,
                    $"Asset not found: {asset}"));
                return;
            }

#if DEBUG
            Debug.Log($"{AgentsBridge.LogPrefix} get-asset-info: {asset}");
#endif

            try {
                var info = new AssetInfo {
                    path = asset,
                    guid = guid,
                    type = "Unknown"
                };

                try {
                    var obj = AssetDatabase.LoadMainAssetAtPath(asset);
                    if (obj != null) {
                        info.type = obj.GetType().FullName;
                    }
                }
                catch {
                    // Type is best-effort; keep "Unknown" on failure.
                }

                try {
                    var fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", asset));
                    if (File.Exists(fullPath)) {
                        info.sizeBytes = new FileInfo(fullPath).Length;
                    }
                }
                catch {
                    // Size is best-effort.
                }

                try {
                    info.directDependencyCount = AssetDatabase.GetDependencies(asset, false).Count(d => d != asset);
                    info.dependencyCount = AssetDatabase.GetDependencies(asset, true).Count(d => d != asset);
                }
                catch {
                    // Dependency counts are best-effort.
                }

                stopwatch.Stop();

                var response = CommandResponse.Success(request.id, request.action, stopwatch.ElapsedMilliseconds);
                response.assetInfo = info;
                onComplete?.Invoke(response);
            }
            catch (Exception e) {
                stopwatch.Stop();
                Debug.LogError($"{AgentsBridge.LogPrefix} get-asset-info failed: {e.Message}");
                onComplete?.Invoke(CommandResponse.Error(request.id, request.action, e.Message));
            }
        }
    }
}
