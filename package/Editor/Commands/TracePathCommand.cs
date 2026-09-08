using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityBridge.Models;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace UnityBridge.Commands {
    /// <summary>
    /// Finds the shortest dependency path (breadth-first search) from one asset to
    /// another through forward dependency edges. Read-only.
    /// </summary>
    public class TracePathCommand : ICommand {
        public void Execute(CommandRequest request, Action<CommandResponse> onProgress, Action<CommandResponse> onComplete) {
            var stopwatch = Stopwatch.StartNew();

            var from = AssetAnalysisUtil.ResolveAssetPath(request.@params?.from);
            var to = AssetAnalysisUtil.ResolveAssetPath(request.@params?.to);

            if (string.IsNullOrEmpty(from)) {
                onComplete?.Invoke(CommandResponse.Error(request.id, request.action,
                    "Missing required parameter: from (an asset path or GUID)."));
                return;
            }

            if (string.IsNullOrEmpty(to)) {
                onComplete?.Invoke(CommandResponse.Error(request.id, request.action,
                    "Missing required parameter: to (an asset path or GUID)."));
                return;
            }

            int maxDepth = AssetAnalysisUtil.TryParseInt(request.@params?.maxDepth, 20);
            if (maxDepth < 0) {
                maxDepth = 20;
            }

#if DEBUG
            Debug.Log($"{AgentsBridge.LogPrefix} trace-path: {from} -> {to} (maxDepth: {maxDepth})");
#endif

            try {
                var result = new TracePathResult { from = from, to = to, found = false, path = new List<string>(), depth = -1 };

                if (from == to) {
                    result.found = true;
                    result.depth = 0;
                    result.path.Add(from);
                    stopwatch.Stop();
                    var immediate = CommandResponse.Success(request.id, request.action, stopwatch.ElapsedMilliseconds);
                    immediate.tracePath = result;
                    onComplete?.Invoke(immediate);
                    return;
                }

                // BFS over direct dependency edges.
                var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { from };
                var parent = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var depth = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [from] = 0 };
                var queue = new Queue<string>();
                queue.Enqueue(from);

                int foundDepth = -1;
                while (queue.Count > 0) {
                    var node = queue.Dequeue();
                    int d = depth[node];

                    if (node == to) {
                        foundDepth = d;
                        break;
                    }

                    if (d >= maxDepth) {
                        continue;
                    }

                    string[] deps;
                    try {
                        deps = AssetDatabase.GetDependencies(node, false);
                    }
                    catch {
                        continue;
                    }

                    foreach (var dep in deps) {
                        if (visited.Contains(dep)) {
                            continue;
                        }

                        visited.Add(dep);
                        parent[dep] = node;
                        depth[dep] = d + 1;
                        queue.Enqueue(dep);
                    }
                }

                stopwatch.Stop();

                if (foundDepth >= 0) {
                    var path = new List<string>();
                    var cursor = to;
                    while (cursor != null) {
                        path.Add(cursor);
                        if (cursor == from) {
                            break;
                        }

                        string prev;
                        if (!parent.TryGetValue(cursor, out prev)) {
                            break;
                        }

                        cursor = prev;
                    }

                    path.Reverse();
                    result.found = true;
                    result.depth = foundDepth;
                    result.path = path;
                }

                var response = CommandResponse.Success(request.id, request.action, stopwatch.ElapsedMilliseconds);
                response.tracePath = result;
                onComplete?.Invoke(response);
            }
            catch (Exception e) {
                stopwatch.Stop();
                Debug.LogError($"{AgentsBridge.LogPrefix} trace-path failed: {e.Message}");
                onComplete?.Invoke(CommandResponse.Error(request.id, request.action, e.Message));
            }
        }
    }
}
