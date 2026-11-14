using System;
using System.IO;
using Sable.Toolbox.Settings;
using UnityEditor;

namespace Sable.Toolbox
{
    /// <summary>Every mutating toolbox action goes through Group so it is exactly one Undo step.</summary>
    public static class ToolboxUndo
    {
        public static void Group(string name, Action action)
        {
            UnityEditor.Undo.IncrementCurrentGroup();
            UnityEditor.Undo.SetCurrentGroupName(name);
            int group = UnityEditor.Undo.GetCurrentGroup();
            try
            {
                action();
            }
            finally
            {
                UnityEditor.Undo.CollapseUndoOperations(group);
            }
        }
    }

    /// <summary>
    /// Where generated assets go. Tools never write over source assets: they get a fresh, unique path
    /// under the project's Generated folder (Settings) and save their copy there.
    /// Note: Unity Undo reverts references to a generated asset, not the file itself; the file stays
    /// under Generated/ where it is harmless and easy to clean.
    /// </summary>
    public static class GeneratedAssets
    {
        /// <summary>Unique asset path under {GeneratedRoot}/{subfolder}/, creating folders as needed.</summary>
        public static string UniquePath(string subfolder, string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) throw new ArgumentException("File name required.", nameof(fileName));
            if (fileName.IndexOfAny(new[] { '/', '\\' }) >= 0) throw new ArgumentException("Pass a file name, not a path.", nameof(fileName));

            string folder = SableToolboxSettings.instance.GeneratedRoot.TrimEnd('/');
            if (!string.IsNullOrEmpty(subfolder)) folder += "/" + subfolder.Trim('/');
            EnsureFolder(folder);
            return AssetDatabase.GenerateUniqueAssetPath(folder + "/" + fileName);
        }

        public static bool IsGenerated(string assetPath)
        {
            string root = SableToolboxSettings.instance.GeneratedRoot.TrimEnd('/') + "/";
            return assetPath != null && assetPath.Replace('\\', '/').StartsWith(root, StringComparison.Ordinal);
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent)) throw new ArgumentException($"Generated folder '{folder}' must be under Assets/.");
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
