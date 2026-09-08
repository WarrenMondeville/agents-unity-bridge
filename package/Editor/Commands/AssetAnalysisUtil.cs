using System.Text.RegularExpressions;
using UnityEditor;

namespace UnityBridge.Commands {
    /// <summary>
    /// Shared helpers for asset dependency analysis commands.
    /// </summary>
    internal static class AssetAnalysisUtil {
        private static readonly Regex GuidPattern = new Regex("^[a-fA-F0-9]{32}$", RegexOptions.Compiled);

        /// <summary>
        /// Resolve an asset parameter that may be either a project path ("Assets/Foo.png")
        /// or a 32-character GUID. Returns the project path, or null when the input is
        /// empty or a GUID that does not resolve.
        /// </summary>
        public static string ResolveAssetPath(string assetOrGuid) {
            if (string.IsNullOrEmpty(assetOrGuid)) {
                return null;
            }

            if (GuidPattern.IsMatch(assetOrGuid)) {
                var path = AssetDatabase.GUIDToAssetPath(assetOrGuid);
                return string.IsNullOrEmpty(path) ? null : path;
            }

            return assetOrGuid;
        }

        /// <summary>
        /// True when the path corresponds to a real asset (has a GUID in the asset database).
        /// </summary>
        public static bool IsValidAssetPath(string path) {
            if (string.IsNullOrEmpty(path)) {
                return false;
            }

            return !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path));
        }

        /// <summary>
        /// Parse a boolean parameter that arrives as the string "true"/"false".
        /// </summary>
        public static bool TryParseBool(string value, bool defaultValue) {
            if (string.IsNullOrEmpty(value)) {
                return defaultValue;
            }

            bool result;
            return bool.TryParse(value, out result) ? result : defaultValue;
        }

        /// <summary>
        /// Parse an integer parameter that arrives as a string.
        /// </summary>
        public static int TryParseInt(string value, int defaultValue) {
            if (string.IsNullOrEmpty(value)) {
                return defaultValue;
            }

            int result;
            return int.TryParse(value, out result) ? result : defaultValue;
        }
    }
}
