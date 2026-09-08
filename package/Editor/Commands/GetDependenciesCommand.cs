using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityBridge.Models;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace UnityBridge.Commands {
    /// <summary>
    /// Lists the assets that a given asset depends on (forward dependency edges).
    /// Read-only: safe to run while Unity is compiling.
    /// </summary>
    public class GetDependenciesCommand : ICommand {
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

            bool recursive = AssetAnalysisUtil.TryParseBool(request.@params?.recursive, false);

#if DEBUG
            Debug.Log($"{AgentsBridge.LogPrefix} get-dependencies: {asset} (recursive: {recursive})");
#endif

            try {
                string[] deps = AssetDatabase.GetDependencies(asset, recursive);
                stopwatch.Stop();

                // GetDependencies may include the input asset itself; exclude it.
                var dependencies = deps
                    .Where(d => d != asset)
                    .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var result = new AssetDependencyResult {
                    asset = asset,
                    dependencies = dependencies,
                    count = dependencies.Count,
                    recursive = recursive
                };

                var response = CommandResponse.Success(request.id, request.action, stopwatch.ElapsedMilliseconds);
                response.assetDependencies = result;
                onComplete?.Invoke(response);
            }
            catch (Exception e) {
                stopwatch.Stop();
                Debug.LogError($"{AgentsBridge.LogPrefix} get-dependencies failed: {e.Message}");
                onComplete?.Invoke(CommandResponse.Error(request.id, request.action, e.Message));
            }
        }
    }
}
