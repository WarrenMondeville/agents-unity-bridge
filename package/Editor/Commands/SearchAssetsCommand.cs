using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityBridge.Models;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace UnityBridge.Commands {
    /// <summary>
    /// Searches the asset database by name (and optional type filter) and returns the
    /// matching asset paths. Read-only.
    /// </summary>
    public class SearchAssetsCommand : ICommand {
        public void Execute(CommandRequest request, Action<CommandResponse> onProgress, Action<CommandResponse> onComplete) {
            var stopwatch = Stopwatch.StartNew();

            var query = request.@params?.query;
            if (string.IsNullOrEmpty(query)) {
                onComplete?.Invoke(CommandResponse.Error(request.id, request.action,
                    "Missing required parameter: query."));
                return;
            }

            int limit = AssetAnalysisUtil.TryParseInt(request.@params?.limit, 50);
            if (limit < 1) {
                limit = 50;
            }

            string type = request.@params?.type;
            string filter = query;
            if (!string.IsNullOrEmpty(type)) {
                filter += " t:" + type;
            }

#if DEBUG
            Debug.Log($"{AgentsBridge.LogPrefix} search-assets: '{filter}' (limit: {limit})");
#endif

            try {
                string[] guids = AssetDatabase.FindAssets(filter);
                var results = new List<string>();

                foreach (var guid in guids) {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path)) {
                        continue;
                    }

                    results.Add(path);
                    if (results.Count >= limit) {
                        break;
                    }
                }

                results.Sort(StringComparer.OrdinalIgnoreCase);
                stopwatch.Stop();

                var searchResult = new AssetSearchResult {
                    query = query,
                    results = results,
                    count = results.Count
                };

                var response = CommandResponse.Success(request.id, request.action, stopwatch.ElapsedMilliseconds);
                response.searchResult = searchResult;
                onComplete?.Invoke(response);
            }
            catch (Exception e) {
                stopwatch.Stop();
                Debug.LogError($"{AgentsBridge.LogPrefix} search-assets failed: {e.Message}");
                onComplete?.Invoke(CommandResponse.Error(request.id, request.action, e.Message));
            }
        }
    }
}
