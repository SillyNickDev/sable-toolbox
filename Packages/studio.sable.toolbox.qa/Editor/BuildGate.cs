using System;
using System.Collections.Generic;
using System.Linq;
using Sable.Toolbox.Findings;
using Sable.Toolbox.Scanning;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Editor.BuildPipeline;

namespace Sable.Toolbox.Qa
{
    /// <summary>
    /// Optional upload gate: runs the active profile on the avatar the SDK is about to build and blocks
    /// the build when there are errors. Runs before NDMF (order -11000) so it sees the avatar as authored.
    /// </summary>
    internal sealed class BuildGate : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => -12000;

        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            QaSettings settings = QaSettings.Load();
            if (!settings.buildGate) return true;
            var descriptor = avatarGameObject.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null) return true;

            QaProfile profile = Linter.Profile(settings.activeProfile);
            List<Finding> errors = Linter.Run(AvatarScanner.Scan(descriptor), profile, settings)
                .Where(f => f.Severity == Severity.Error).ToList();
            if (errors.Count == 0) return true;

            string list = string.Join("\n", errors.Take(8).Select(f => "• " + f.Message)) + (errors.Count > 8 ? $"\n… and {errors.Count - 8} more" : "");
            Debug.LogError($"Sable Toolbox: build blocked by {errors.Count} lint error(s) in profile '{profile.name}':\n{list}");
            if (Application.isBatchMode) return false;
            return EditorUtility.DisplayDialog("Sable Toolbox: lint errors",
                $"Profile '{profile.name}' found {errors.Count} error(s):\n\n{list}\n\nOpen Tools > Sable Toolbox > Linter for fixes.",
                "Build anyway", "Cancel build");
        }
    }

    /// <summary>Raised after Unity saves assets or scenes; the linter page re-runs when "run on save" is on.</summary>
    internal sealed class SaveWatcher : AssetModificationProcessor
    {
        public static event Action Saved;

        private static string[] OnWillSaveAssets(string[] paths)
        {
            if (Saved != null) EditorApplication.delayCall += () => Saved?.Invoke();
            return paths;
        }
    }
}
