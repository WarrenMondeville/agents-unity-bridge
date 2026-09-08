using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace UnityBridge {
    /// <summary>
    /// Installs the companion Python CLI and the DeepSeek Harness skill from inside the
    /// Unity Editor. This mirrors how Unity MCP plugins bootstrap their external tooling:
    /// install the Unity package first, then run the installer from a menu / window.
    /// </summary>
    public class AgentsBridgeInstaller : EditorWindow {
        private const string PipPackage = "agents-unity-bridge";
        private const string MenuRoot = "Tools/Unity Bridge/";

        private static string _lastLog = "";
        private static bool _busy;
        private Vector2 _scroll;

        // ---------------------------------------------------------------------
        // Menu items
        // ---------------------------------------------------------------------

        [MenuItem(MenuRoot + "Open Installer")]
        public static void OpenWindow() {
            GetWindow<AgentsBridgeInstaller>("Unity Bridge Installer").Show();
        }

        [MenuItem(MenuRoot + "Install Python CLI")]
        public static void InstallCli() {
            string python = FindPython();
            if (python == null) {
                ReportError("Python 3 was not found. Install Python 3.8+ and try again.");
                return;
            }
            RunCommand($"Install Python CLI ({PipPackage})", python, $"-m pip install --user --upgrade {PipPackage}");
        }

        [MenuItem(MenuRoot + "Install Skill")]
        public static void InstallSkill() {
            string python = FindPython();
            if (python == null) {
                ReportError("Python 3 was not found. Install Python 3.8+ and try again.");
                return;
            }
            RunCommand("Install Skill", python, $"-m agents_unity_bridge.cli install-skill");
        }

        // ---------------------------------------------------------------------
        // Window UI
        // ---------------------------------------------------------------------

        private void OnGUI() {
            string python = FindPython();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Unity Bridge Installer", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Install the companion Python CLI and the DeepSeek Harness skill from inside Unity. " +
                "This is equivalent to running the following commands manually:\n" +
                $"  pip install {PipPackage}\n" +
                $"  {PipPackage} install-skill",
                MessageType.Info);

            EditorGUILayout.Space();
            if (python != null) {
                EditorGUILayout.LabelField("Python:", $"Detected ({python})");
            } else {
                EditorGUILayout.HelpBox(
                    "Python 3.8+ was not detected on PATH. Install Python from https://www.python.org/downloads/ and restart Unity.",
                    MessageType.Warning);
            }

            EditorGUILayout.Space();
            GUI.enabled = !_busy && python != null;
            if (GUILayout.Button("Install Python CLI", GUILayout.Height(28))) {
                InstallCli();
            }
            if (GUILayout.Button("Install Skill", GUILayout.Height(28))) {
                InstallSkill();
            }
            GUI.enabled = !_busy;

            EditorGUILayout.Space();
            if (_busy) {
                EditorGUILayout.LabelField("Working, see Console for details...");
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(160));
            EditorGUILayout.TextArea(string.IsNullOrEmpty(_lastLog) ? "(no output yet)" : _lastLog, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        // ---------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------

        private static string FindPython() {
            string[] candidates = { "python3", "python", "py" };
            foreach (string c in candidates) {
                try {
                    var psi = new ProcessStartInfo {
                        FileName = c,
                        Arguments = "--version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                    using (var p = Process.Start(psi)) {
                        if (p == null) continue;
                        string stdout = ReadToEndSafely(p.StandardOutput);
                        string stderr = ReadToEndSafely(p.StandardError);
                        if (!p.WaitForExit(5000)) {
                            try { p.Kill(); } catch { /* ignore */ }
                            continue;
                        }
                        string version = (stdout ?? "") + (stderr ?? "");
                        if (version.Contains("Python 3")) return c;
                    }
                } catch {
                    // candidate not available; try the next one
                }
            }
            return null;
        }

        private static string ReadToEndSafely(StreamReader reader) {
            try { return reader.ReadToEnd(); } catch { return ""; }
        }

        private static void RunCommand(string label, string file, string arguments) {
            _busy = true;
            _lastLog = "";
            Debug.Log($"[AgentsBridge] {label}: {file} {arguments}");
            var thread = new System.Threading.Thread(() => RunCommandWorker(label, file, arguments));
            thread.IsBackground = true;
            thread.Start();
        }

        private static void RunCommandWorker(string label, string file, string arguments) {
            var output = new StringBuilder();
            var outputLock = new object();
            int exitCode = -1;
            string error = null;

            void AppendLine(string line) {
                if (line == null) return;
                lock (outputLock) {
                    output.AppendLine(line);
                }
            }

            try {
                var psi = new ProcessStartInfo {
                    FileName = file,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using (var p = new Process { StartInfo = psi }) {
                    p.OutputDataReceived += (s, e) => AppendLine(e.Data);
                    p.ErrorDataReceived += (s, e) => AppendLine(e.Data);
                    p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    p.WaitForExit();
                    exitCode = p.ExitCode;
                }
            } catch (Exception e) {
                error = e.Message;
            }

            string text = output.ToString();
            EditorApplication.delayCall += () => {
                _busy = false;
                _lastLog = text;
                if (error != null) {
                    Debug.LogError($"[AgentsBridge] {label} error: {error}");
                } else if (exitCode == 0) {
                    Debug.Log($"[AgentsBridge] {label} succeeded.\n{text}");
                } else {
                    Debug.LogError($"[AgentsBridge] {label} failed (exit code {exitCode}).\n{text}");
                }
            };
        }

        private static void ReportError(string message) {
            _lastLog = message;
            Debug.LogError($"[AgentsBridge] {message}");
        }
    }
}
