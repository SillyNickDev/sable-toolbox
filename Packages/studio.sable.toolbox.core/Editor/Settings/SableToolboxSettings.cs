using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Sable.Toolbox.Settings
{
    /// <summary>
    /// The one per-project settings file, stored in ProjectSettings/ so it never ends up in an avatar
    /// package. Edit it under Project Settings > Sable Toolbox (changes are undoable).
    /// </summary>
    [FilePath("ProjectSettings/SableToolboxSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class SableToolboxSettings : ScriptableSingleton<SableToolboxSettings>
    {
        [Tooltip("Folder for every generated asset. Must be under Assets/ and should end in Generated.")]
        [SerializeField] private string generatedRoot = "Assets/Generated";

        [Tooltip("Module ids hidden from the toolbox window.")]
        [SerializeField] private List<string> disabledModules = new List<string>();

        [SerializeField, HideInInspector] private string lastModule = "";

        public string GeneratedRoot
        {
            get => string.IsNullOrWhiteSpace(generatedRoot) || !generatedRoot.StartsWith("Assets")
                ? "Assets/Generated"
                : generatedRoot;
            set { generatedRoot = value; Save(true); }
        }

        public bool IsModuleEnabled(string id) => !disabledModules.Contains(id);

        public void SetModuleEnabled(string id, bool enabled)
        {
            disabledModules.Remove(id);
            if (!enabled) disabledModules.Add(id);
            Save(true);
        }

        public string LastModule
        {
            get => lastModule;
            set { if (lastModule == value) return; lastModule = value; Save(true); }
        }

        public void SaveSettings() => Save(true);
    }

    internal static class SableToolboxSettingsProvider
    {
        [SettingsProvider]
        private static SettingsProvider Create() => new SettingsProvider("Project/Sable Toolbox", SettingsScope.Project)
        {
            guiHandler = _ =>
            {
                SableToolboxSettings settings = SableToolboxSettings.instance;
                settings.hideFlags &= ~HideFlags.NotEditable;
                var so = new SerializedObject(settings);
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(so.FindProperty("generatedRoot"));
                EditorGUILayout.PropertyField(so.FindProperty("disabledModules"), true);
                if (EditorGUI.EndChangeCheck())
                {
                    so.ApplyModifiedProperties(); // registers Undo
                    settings.SaveSettings();
                }
            },
            keywords = new[] { "Sable", "Toolbox", "Generated", "Module" }
        };
    }
}
